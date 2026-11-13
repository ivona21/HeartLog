using System.Net;
using System.Net.Http.Json;
using HeartLog.Api.DTOs;

namespace HeartLog.Api.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class EmotionEntryRegressionTests
{
    private const string Route = "/api/emotion-entries";

    [PostgresFact]
    public async Task ListAndSummary_OrderAndScopeResults_AndDeletionRemovesThem()
    {
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync();
        using var owner = host.Client(PostgresTestHost.Owner);
        using var other = host.Client(PostgresTestHost.Other);
        Assert.Empty(await List(owner));
        await Summary(owner, 0, null);
        var date = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var older = await Create(owner, date.AddDays(-1));
        var first = await Create(owner, date);
        var second = await Create(owner, date);
        // Set deterministic tie-break values rather than depending on clock resolution.
        await host.ScalarAsync("UPDATE \"EmotionEntries\" SET \"CreatedAt\"=@date WHERE \"Id\"=@id RETURNING 1", ("date", date), ("id", first.EntryId));
        await host.ScalarAsync("UPDATE \"EmotionEntries\" SET \"CreatedAt\"=@date WHERE \"Id\"=@id RETURNING 1", ("date", date.AddMinutes(1)), ("id", second.EntryId));
        await Create(other, date.AddDays(1));
        Assert.Equal(new[] { second.EntryId, first.EntryId, older.EntryId }, (await List(owner)).Select(e => e.EntryId));
        await Summary(owner, 3, date);
        await Summary(other, 1, date.AddDays(1));
        Assert.Equal(PostgresTestHost.Owner, await host.ScalarAsync("SELECT \"UserId\" FROM \"EmotionEntries\" WHERE \"Id\"=@id", ("id", first.EntryId)));
        Assert.Equal(date, first.OccurredAt);
        Assert.Equal(PostgresTestHost.AtPostgresPrecision(older.CreatedAt),
            (await List(owner)).Single(e => e.EntryId == older.EntryId).CreatedAt);
        foreach (var entry in new[] { first, second, older })
        {
            using var deleted = await owner.DeleteAsync($"{Route}/{entry.EntryId}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.DoesNotContain(await List(owner), e => e.EntryId == entry.EntryId);
            Assert.Equal(0L, await host.ScalarAsync("SELECT count(*) FROM \"EmotionEntries\" WHERE \"Id\"=@id", ("id", entry.EntryId)));
            using var repeated = await owner.DeleteAsync($"{Route}/{entry.EntryId}");
            Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        }
        await Summary(owner, 0, null);
        Assert.Single(await List(other));
    }

    [PostgresFact]
    public async Task InvalidRequestsAndMissingWrites_ReturnExistingErrors_WithoutChangingOwnerEntry()
    {
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync();
        using var client = host.Client(PostgresTestHost.Owner);
        var original = await Create(client, DateTime.UtcNow);
        await host.ScalarAsync("UPDATE \"Emotions\" SET \"IsActive\"=false WHERE \"Id\"=@id RETURNING 1", ("id", PostgresTestHost.Hope));
        var invalidRequests = new[]
        {
            new CreateEmotionEntryRequest { EmotionKeys = [], PrimaryEmotionKey = "test-joy" },
            new CreateEmotionEntryRequest { EmotionKeys = null!, PrimaryEmotionKey = "test-joy" },
            new CreateEmotionEntryRequest { EmotionKeys = ["test-joy"], PrimaryEmotionKey = null! },
            new CreateEmotionEntryRequest { EmotionKeys = ["unknown"], PrimaryEmotionKey = "unknown" },
            new CreateEmotionEntryRequest { EmotionKeys = ["test-hope"], PrimaryEmotionKey = "test-hope" }
        };
        foreach (var request in invalidRequests)
        foreach (var update in new[] { false, true })
        {
            using var response = update ? await client.PutAsJsonAsync($"{Route}/{original.EntryId}", request) : await client.PostAsJsonAsync(Route, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("synthetic note", Assert.Single(await List(client)).Comment);
        }
        var missing = Guid.NewGuid();
        using var put = await client.PutAsJsonAsync($"{Route}/{missing}", Request(DateTime.UtcNow));
        using var delete = await client.DeleteAsync($"{Route}/{missing}");
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(original.EntryId, Assert.Single(await List(client)).EntryId);
    }

    [PostgresFact]
    public async Task AllSupportedRoutes_RequireAuthentication()
    {
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync();
        using var client = host.Client(PostgresTestHost.Owner);
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        var id = Guid.NewGuid();
        using var list = await client.GetAsync(Route);
        using var summary = await client.GetAsync($"{Route}/summary");
        using var post = await client.PostAsJsonAsync(Route, Request(DateTime.UtcNow));
        using var put = await client.PutAsJsonAsync($"{Route}/{id}", Request(DateTime.UtcNow));
        using var delete = await client.DeleteAsync($"{Route}/{id}");
        foreach (var response in new[] { list, summary, post, put, delete })
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0L, await host.ScalarAsync("SELECT count(*) FROM \"EmotionEntries\""));
    }

    private static CreateEmotionEntryRequest Request(DateTime date) => new()
    { EmotionKeys = ["test-calm", "test-joy"], PrimaryEmotionKey = "test-joy", Comment = "synthetic note", OccurredAt = date };

    private static async Task<EmotionEntryResponse> Create(HttpClient client, DateTime date)
    {
        using var response = await client.PostAsJsonAsync(Route, Request(date));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<EmotionEntryResponse>>())!.Data!;
    }

    private static async Task<List<EmotionEntryResponse>> List(HttpClient client)
    {
        using var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<List<EmotionEntryResponse>>>())!.Data!;
    }

    private static async Task Summary(HttpClient client, int count, DateTime? latest)
    {
        using var response = await client.GetAsync($"{Route}/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<ApiResponse<EmotionEntriesSummaryResponse>>())!.Data!;
        Assert.Equal(count, data.TotalEntries);
        Assert.Equal(latest, data.LatestOccurredAt);
    }
}
