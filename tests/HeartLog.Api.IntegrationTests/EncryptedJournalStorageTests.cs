using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeartLog.Api.DTOs;
using HeartLog.Api.Errors;
using Microsoft.Extensions.Logging;

namespace HeartLog.Api.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class EncryptedJournalStorageTests
{
    private const string Route = "/api/emotion-entries";
    private const string Note = "SYNTHETIC journal čćž: osjećam mir.\nDrugi red privatne bilješke.";
    private const string ChangedNote = "SYNTHETIC changed journal: želim učiti.\nČitam knjigu.";

    [PostgresFact]
    public async Task CreateAndRead_PersistOnlyBundles_AndIsolateOwners()
    {
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync();
        using var owner = host.Client(PostgresTestHost.Owner);
        using var other = host.Client(PostgresTestHost.Other);
        var withNote = await SaveAsync(owner, Note, ["test-joy", "test-calm"], "test-joy");
        var emotionsOnly = await SaveAsync(owner, null, ["test-calm"], "test-calm");
        var otherEntry = await SaveAsync(other, "Other synthetic user", ["test-hope"], "test-hope");

        await AssertStorageAsync(host, withNote.EntryId, Note);
        await AssertStorageAsync(host, emotionsOnly.EntryId, null);
        await AssertNoLegacyStorageAsync(host);
        var entries = await ReadAsync(owner);
        Assert.Equal(2, entries.Count);
        AssertEntry(Assert.Single(entries, e => e.EntryId == withNote.EntryId), Note,
            ("test-joy", true), ("test-calm", false));
        AssertEntry(Assert.Single(entries, e => e.EntryId == emotionsOnly.EntryId), null, ("test-calm", true));
        Assert.Equal(otherEntry.EntryId, Assert.Single(await ReadAsync(other)).EntryId);

        using var deniedEdit = await other.PutAsJsonAsync($"{Route}/{withNote.EntryId}", Request(ChangedNote, ["test-hope"], "test-hope"));
        Assert.Equal(HttpStatusCode.NotFound, deniedEdit.StatusCode);
        using var deniedDelete = await other.DeleteAsync($"{Route}/{withNote.EntryId}");
        Assert.Equal(HttpStatusCode.NotFound, deniedDelete.StatusCode);
        AssertEntry(Assert.Single(await ReadAsync(owner), e => e.EntryId == withNote.EntryId), Note,
            ("test-joy", true), ("test-calm", false));

        using var deleted = await owner.DeleteAsync($"{Route}/{emotionsOnly.EntryId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0L, await host.ScalarAsync("SELECT count(*) FROM \"EmotionEntries\" WHERE \"Id\"=@id", ("id", emotionsOnly.EntryId)));
    }

    [PostgresFact]
    public async Task EditAndRead_ReplaceEncryptedValues_AndReadFixedV1Fixture()
    {
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync();
        using var client = host.Client(PostgresTestHost.Owner);
        var original = await SaveAsync(client, Note, ["test-joy"], "test-joy");
        var before = await RawRowAsync(host, original.EntryId);
        var changed = await SaveAsync(client, ChangedNote, ["test-calm", "test-hope"], "test-hope", original.EntryId);
        Assert.Equal(original.EntryId, changed.EntryId);
        var after = await RawRowAsync(host, original.EntryId);
        Assert.NotEqual(before["EncryptedNote"]!.ToJsonString(), after["EncryptedNote"]!.ToJsonString());
        Assert.NotEqual(before["EncryptedEmotions"]!.ToJsonString(), after["EncryptedEmotions"]!.ToJsonString());
        await AssertStorageAsync(host, original.EntryId, ChangedNote);
        Assert.DoesNotContain(Note, after.ToJsonString());
        AssertEntry(Assert.Single(await ReadAsync(client)), ChangedNote, ("test-hope", true), ("test-calm", false));

        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "entry-v1.json")));
        var root = fixture.RootElement;
        Assert.Equal(PostgresTestHost.Key, root.GetProperty("keyBase64").GetString());
        Assert.Equal(PostgresTestHost.Owner, root.GetProperty("ownerId").GetGuid());
        var fixtureId = root.GetProperty("entryId").GetGuid();
        await host.ScalarAsync("""
            INSERT INTO "EmotionEntries" ("Id", "UserId", "EncryptedNote", "EncryptedEmotions", "OccurredAt", "CreatedAt", "UpdatedAt")
            VALUES (@id, @owner, @note::jsonb, @emotions::jsonb, now(), now(), now()) RETURNING 1
            """, ("id", fixtureId), ("owner", PostgresTestHost.Owner),
            ("note", root.GetProperty("noteBundle").GetRawText()), ("emotions", root.GetProperty("emotionsBundle").GetRawText()));
        // These catalog IDs are the IDs encoded in the immutable v1 fixture.
        Assert.Equal(PostgresTestHost.Joy, root.GetProperty("expectedEmotions")[0].GetProperty("emotionId").GetGuid());
        Assert.Equal(PostgresTestHost.Calm, root.GetProperty("expectedEmotions")[1].GetProperty("emotionId").GetGuid());
        AssertEntry(Assert.Single(await ReadAsync(client), e => e.EntryId == fixtureId),
            root.GetProperty("expectedNote").GetString(), ("test-joy", true), ("test-calm", false));
        using var other = host.Client(PostgresTestHost.Other);
        Assert.Empty(await ReadAsync(other));
    }

    [PostgresFact]
    public async Task DamagedBundle_ReturnsGeneric500_WithoutSensitiveResponseOrLogs()
    {
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync();
        using var client = host.Client(PostgresTestHost.Owner);
        await SaveAsync(client, "Healthy synthetic note", ["test-joy"], "test-joy");
        var entry = await SaveAsync(client, Note, ["test-calm"], "test-calm");
        var raw = await RawRowAsync(host, entry.EntryId);
        var damaged = raw["EncryptedNote"]!.DeepClone();
        var bytes = Convert.FromBase64String(damaged["ciphertext"]!.GetValue<string>());
        bytes[0] ^= 1;
        damaged["ciphertext"] = Convert.ToBase64String(bytes);
        await host.ScalarAsync("UPDATE \"EmotionEntries\" SET \"EncryptedNote\"=@bundle::jsonb WHERE \"Id\"=@id RETURNING 1",
            ("bundle", damaged.ToJsonString()), ("id", entry.EntryId));

        using var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonNode.Parse(body)!;
        Assert.Equal("unexpectedError", error["code"]!.GetValue<string>());
        Assert.Equal(ApiErrorMessages.UnexpectedError, error["message"]!.GetValue<string>());
        Assert.Null(error["data"]);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error && e.Text.Contains("Entry content could not be decrypted."));
        var logs = string.Join('\n', host.Logs.Entries.Select(e => e.Text));
        foreach (var secret in new[] { Note, ChangedNote, "Healthy synthetic note", "test-calm", "test-joy",
            PostgresTestHost.Key, Convert.ToHexString(Convert.FromBase64String(PostgresTestHost.Key)),
            raw["EncryptedNote"]!["ciphertext"]!.GetValue<string>() })
        {
            Assert.DoesNotContain(secret, body);
            Assert.DoesNotContain(secret, logs);
            Assert.DoesNotContain(JsonSerializer.Serialize(secret), logs);
        }
        // Damage is local to this owner's list. Other users still get their own list.
        using var other = host.Client(PostgresTestHost.Other);
        Assert.Empty(await ReadAsync(other));
    }

    [PostgresFact]
    public async Task LegacyBackfillAndRerun_PreserveValuesThroughCutoverAndNormalRead()
    {
        await using var host = await PostgresTestHost.CreateAsync(legacy: true);
        var id = Guid.NewGuid();
        await host.ScalarAsync("""
            INSERT INTO "EmotionEntries" ("Id", "UserId", "Comment", "OccurredAt", "CreatedAt", "UpdatedAt")
            VALUES (@id, @owner, @note, now(), now(), now()) RETURNING 1
            """, ("id", id), ("owner", PostgresTestHost.Owner), ("note", Note));
        foreach (var (emotion, primary) in new[] { (PostgresTestHost.Joy, true), (PostgresTestHost.Calm, false) })
            await host.ScalarAsync("""
                INSERT INTO "EmotionEntryEmotions" ("Id", "EmotionEntryId", "EmotionId", "IsPrimary", "CreatedAt")
                VALUES (@link, @entry, @emotion, @primary, now()) RETURNING 1
                """, ("link", Guid.NewGuid()), ("entry", id), ("emotion", emotion), ("primary", primary));
        Assert.Equal(Note, await host.ScalarAsync("SELECT \"Comment\" FROM \"EmotionEntries\" WHERE \"Id\"=@id", ("id", id)));

        var firstOutput = await host.RunMigrationToolAsync("backfill");
        Assert.Contains("committed_verified=1", firstOutput);
        var first = await RawRowAsync(host, id);
        var secondOutput = await host.RunMigrationToolAsync("backfill");
        Assert.DoesNotContain("committed_verified=", secondOutput);
        var second = await RawRowAsync(host, id);
        Assert.Equal(first["EncryptedNote"]!.ToJsonString(), second["EncryptedNote"]!.ToJsonString());
        Assert.Equal(first["EncryptedEmotions"]!.ToJsonString(), second["EncryptedEmotions"]!.ToJsonString());
        var verification = await host.RunMigrationToolAsync("verify");
        Assert.Contains("verified=1; legacy_schema=True", verification);
        Assert.DoesNotContain(Note, firstOutput + secondOutput + verification);
        await host.MigrateAsync();
        await AssertNoLegacyStorageAsync(host);
        await AssertStorageAsync(host, id, Note);
        var postCutover = await host.RunMigrationToolAsync("verify");
        Assert.Contains("verified=1; legacy_schema=False", postCutover);
        await host.StartAsync();
        using var client = host.Client(PostgresTestHost.Owner);
        AssertEntry(Assert.Single(await ReadAsync(client)), Note, ("test-joy", true), ("test-calm", false));
    }

    private static CreateEmotionEntryRequest Request(string? note, string[] emotions, string primary) =>
        new() { Comment = note, EmotionKeys = emotions.ToList(), PrimaryEmotionKey = primary };

    private static async Task<EmotionEntryResponse> SaveAsync(HttpClient client, string? note, string[] emotions,
        string primary, Guid? id = null)
    {
        using var response = id is null
            ? await client.PostAsJsonAsync(Route, Request(note, emotions, primary))
            : await client.PutAsJsonAsync($"{Route}/{id}", Request(note, emotions, primary));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<EmotionEntryResponse>>();
        Assert.True(result!.Success);
        Assert.NotNull(result.Data);
        return result.Data;
    }

    private static async Task<List<EmotionEntryResponse>> ReadAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<EmotionEntryResponse>>>();
        Assert.True(result!.Success);
        Assert.NotNull(result.Data);
        return result.Data;
    }

    private static void AssertEntry(EmotionEntryResponse entry, string? note, params (string Key, bool Primary)[] selected)
    {
        Assert.Equal(note, entry.Comment);
        Assert.Equal(selected.OrderBy(e => e.Key), entry.SelectedEmotions
            .Select(e => (e.EmotionKey, e.IsPrimary)).OrderBy(e => e.EmotionKey));
    }

    private static async Task<JsonNode> RawRowAsync(PostgresTestHost host, Guid id) =>
        JsonNode.Parse((string)(await host.ScalarAsync(
            "SELECT to_jsonb(e)::text FROM \"EmotionEntries\" e WHERE \"Id\"=@id", ("id", id)))!)!;

    private static async Task AssertStorageAsync(PostgresTestHost host, Guid id, string? note)
    {
        var row = await RawRowAsync(host, id);
        AssertBundle(row["EncryptedEmotions"]!);
        if (note is null)
        {
            Assert.Null(row["EncryptedNote"]);
            Assert.Equal(true, await host.ScalarAsync("SELECT \"EncryptedNote\" IS NULL FROM \"EmotionEntries\" WHERE \"Id\"=@id", ("id", id)));
        }
        else
        {
            AssertBundle(row["EncryptedNote"]!);
            Assert.DoesNotContain(note, row.ToJsonString());
            Assert.DoesNotContain(JsonSerializer.Serialize(note), row.ToJsonString());
        }
        foreach (var readable in new[] { PostgresTestHost.Joy.ToString(), PostgresTestHost.Calm.ToString(),
            PostgresTestHost.Hope.ToString(), "test-joy", "test-calm", "test-hope", PostgresTestHost.Key })
            Assert.DoesNotContain(readable, row.ToJsonString());
    }

    private static void AssertBundle(JsonNode bundle)
    {
        Assert.Equal(new[] { "authTag", "ciphertext", "keyId", "nonce" }, bundle.AsObject().Select(p => p.Key).Order());
        Assert.Equal("v1", bundle["keyId"]!.GetValue<string>());
        Assert.Equal(12, Convert.FromBase64String(bundle["nonce"]!.GetValue<string>()).Length);
        Assert.Equal(16, Convert.FromBase64String(bundle["authTag"]!.GetValue<string>()).Length);
        Assert.NotEmpty(Convert.FromBase64String(bundle["ciphertext"]!.GetValue<string>()));
    }

    private static async Task AssertNoLegacyStorageAsync(PostgresTestHost host)
    {
        Assert.Equal(0L, await host.ScalarAsync("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema='public' AND table_name='EmotionEntries' AND column_name='Comment'
            """));
        Assert.Equal(true, await host.ScalarAsync("SELECT to_regclass('public.\"EmotionEntryEmotions\"') IS NULL"));
        Assert.Equal("jsonb", await host.ScalarAsync("""
            SELECT data_type FROM information_schema.columns
            WHERE table_schema='public' AND table_name='EmotionEntries' AND column_name='EncryptedEmotions'
            """));
    }
}
