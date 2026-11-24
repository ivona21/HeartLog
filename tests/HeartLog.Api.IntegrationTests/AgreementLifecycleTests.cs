using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HeartLog.BLL.Models.Agreements;
using HeartLog.Api.DTOs;

namespace HeartLog.Api.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class AgreementLifecycleTests
{
    [PostgresFact]
    public async Task MigrationPreservesPreviouslyRecordedConsentWithoutInventingStatement()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync(legacy: true);
        await host.MigrateAsync("20261123105452_BindAgreementIntentToProvider");
        await host.ScalarAsync("""
            INSERT INTO "AgreementEvents" ("Id", "UserId", "AgreementKey", "Version", "Action", "Source", "OccurredAtUtc", "SubmissionId")
            VALUES (@id, @user, 'journalProcessing', 'old', 'granted', 'registration', now(), @submission) RETURNING 1
            """, ("id", Guid.NewGuid()), ("user", PostgresTestHost.Owner), ("submission", Guid.NewGuid()));
        await host.MigrateAsync();
        Assert.Equal("old", await host.ScalarAsync("SELECT \"Version\" FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing'"));
        Assert.Equal(1L, await host.ScalarAsync("SELECT count(*) FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing' AND \"Statement\" IS NULL"));
        await host.StartAsync(provider.Configure);
        using var owner = host.Client(PostgresTestHost.Owner);
        var me = await owner.GetFromJsonAsync<JsonObject>("/api/auth/me");
        Assert.Equal("granted", me!["data"]!["journalConsent"]!["status"]!.GetValue<string>());
    }

    [PostgresFact]
    public async Task ExistingUserCanReviewAndRecordAgreementsWithoutChangingAnotherUser()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync(withAgreements: false);
        await host.StartAsync(provider.Configure);
        using var owner = host.Client(PostgresTestHost.Owner);
        using var other = host.Client(PostgresTestHost.Other);
        var before = await owner.GetFromJsonAsync<JsonArray>("/api/agreements/me");
        Assert.All(before!, x => Assert.True(x!["actionRequired"]!.GetValue<bool>()));
        using var denied = await owner.GetAsync("/api/emotion-entries");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedSave = await owner.PostAsJsonAsync("/api/emotion-entries", new CreateEmotionEntryRequest
        { EmotionKeys = ["test-joy"], PrimaryEmotionKey = "test-joy", OccurredAt = DateTime.UtcNow });
        Assert.Equal(HttpStatusCode.Forbidden, deniedSave.StatusCode);
        Assert.Equal(0L, await host.ScalarAsync("SELECT count(*) FROM \"EmotionEntries\""));
        var me = await owner.GetFromJsonAsync<JsonObject>("/api/auth/me");
        Assert.Equal("notGranted", me!["data"]!["journalConsent"]!["status"]!.GetValue<string>());

        var submission = new
        {
            submissionId = Guid.NewGuid(),
            items = AgreementCatalog.Requirements.Select(x => new { key = x.Key, version = x.Version }).ToArray()
        };
        using var saved = await owner.PostAsJsonAsync("/api/agreements/me", submission);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var repeated = await owner.PostAsJsonAsync("/api/agreements/me", submission);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        var after = await owner.GetFromJsonAsync<JsonArray>("/api/agreements/me");
        Assert.All(after!, x => Assert.False(x!["actionRequired"]!.GetValue<bool>()));
        var grant = new { submissionId = Guid.NewGuid(), journalProcessingConsent = true, statement = AgreementCatalog.JournalStatement };
        using var wrong = await owner.PostAsJsonAsync("/api/agreements/journal-consent", new { submissionId = Guid.NewGuid(), journalProcessingConsent = true, statement = "different wording" });
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        using var uncheckedConsent = await owner.PostAsJsonAsync("/api/agreements/journal-consent", new { submissionId = Guid.NewGuid(), journalProcessingConsent = false, statement = AgreementCatalog.JournalStatement });
        Assert.Equal(HttpStatusCode.BadRequest, uncheckedConsent.StatusCode);
        using var granted = await owner.PostAsJsonAsync("/api/agreements/journal-consent", grant);
        Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
        using var retry = await owner.PostAsJsonAsync("/api/agreements/journal-consent", grant);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        using var conflict = await owner.PostAsJsonAsync("/api/agreements/journal-consent", new { grant.submissionId, journalProcessingConsent = true, statement = "different" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(AgreementCatalog.JournalStatement, await host.ScalarAsync("SELECT \"Statement\" FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing'"));
        me = await owner.GetFromJsonAsync<JsonObject>("/api/auth/me");
        Assert.Equal("granted", me!["data"]!["journalConsent"]!["status"]!.GetValue<string>());
        using var allowed = await owner.GetAsync("/api/emotion-entries");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var save = await owner.PostAsJsonAsync("/api/emotion-entries", new CreateEmotionEntryRequest
        { EmotionKeys = ["test-joy"], PrimaryEmotionKey = "test-joy", OccurredAt = DateTime.UtcNow });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(1L, await host.ScalarAsync("SELECT count(*) FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing'"));
        var untouched = await other.GetFromJsonAsync<JsonArray>("/api/agreements/me");
        Assert.All(untouched!, x => Assert.True(x!["actionRequired"]!.GetValue<bool>()));

        using var stale = await owner.PostAsJsonAsync("/api/agreements/me", new
        {
            submissionId = Guid.NewGuid(), items = new[] { new { key = AgreementCatalog.Terms, version = "old" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
    }
}
