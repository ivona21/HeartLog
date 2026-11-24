using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HeartLog.BLL.Models.Agreements;

namespace HeartLog.Api.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class AgreementLifecycleTests
{
    [PostgresFact]
    public async Task ExistingUserCanReviewAndRecordAgreementsWithoutChangingAnotherUser()
    {
        await using var host = await PostgresTestHost.CreateAsync(withAgreements: false);
        await host.StartAsync();
        using var owner = host.Client(PostgresTestHost.Owner);
        using var other = host.Client(PostgresTestHost.Other);
        var before = await owner.GetFromJsonAsync<JsonArray>("/api/agreements/me");
        Assert.All(before!, x => Assert.True(x!["actionRequired"]!.GetValue<bool>()));
        using var denied = await owner.GetAsync("/api/emotion-entries");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

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
        using var allowed = await owner.GetAsync("/api/emotion-entries");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var untouched = await other.GetFromJsonAsync<JsonArray>("/api/agreements/me");
        Assert.All(untouched!, x => Assert.True(x!["actionRequired"]!.GetValue<bool>()));

        using var stale = await owner.PostAsJsonAsync("/api/agreements/me", new
        {
            submissionId = Guid.NewGuid(), items = new[] { new { key = AgreementCatalog.Terms, version = "old" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
    }
}
