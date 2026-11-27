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
    public async Task LegacyCompatibilityOnlyRecordsOriginalVersionAndTextWithoutInventingHashes()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync(withAgreements: false);
        await host.StartAsync(provider.Configure, allowLegacy: true);
        using var owner = host.Client(PostgresTestHost.Owner);
        provider.Reply("POST", "/auth/v1/signup", body: new { id = Guid.NewGuid(), email = "synthetic@example.invalid", email_confirmed_at = (string?)null });
        using var registration = await owner.PostAsJsonAsync("/api/auth/register", new
        {
            email = "synthetic@example.invalid", password = ScriptedSupabase.Password,
            agreements = new { termsAccepted = true, privacyPolicyAcknowledged = true, termsVersion = "2026-11-23", privacyPolicyVersion = "2026-11-23" }
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        Assert.Equal(1L, await host.ScalarAsync("SELECT count(*) FROM \"RegistrationIntents\" WHERE \"TermsVersion\"='2026-11-23' AND \"TermsHash\" IS NULL AND \"PrivacyPolicyHash\" IS NULL"));
        var grant = new { submissionId = Guid.NewGuid(), journalProcessingConsent = true, statement = AgreementCatalog.JournalStatement };
        using var response = await owner.PostAsJsonAsync("/api/agreements/journal-consent", grant);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1L, await host.ScalarAsync("SELECT count(*) FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing' AND \"Version\" IS NULL AND \"ContentHash\" IS NULL AND \"ConsentId\" IS NULL"));
        var decisions = new { submissionId = Guid.NewGuid(), items = new[]
        {
            new { key = AgreementCatalog.Terms, version = AgreementCatalog.TermsVersion },
            new { key = AgreementCatalog.PrivacyPolicy, version = AgreementCatalog.PrivacyPolicyVersion }
        } };
        using var accepted = await owner.PostAsJsonAsync("/api/agreements/me", decisions);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(2L, await host.ScalarAsync("SELECT count(*) FROM \"AgreementEvents\" WHERE \"AgreementKey\" IN ('terms', 'privacyPolicy') AND \"Version\"='2026-11-23' AND \"ContentHash\" IS NULL"));
    }

    [PostgresFact]
    public async Task MigrationPreservesPreviouslyRecordedConsentWithoutInventingStatement()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync(legacy: true);
        await host.MigrateAsync("20261124090008_SeparateJournalConsent");
        var consentId = Guid.NewGuid();
        var submission = Guid.NewGuid();
        var originalTime = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await host.ScalarAsync("""
            INSERT INTO "AgreementEvents" ("Id", "UserId", "AgreementKey", "Version", "Statement", "Action", "Source", "OccurredAtUtc", "SubmissionId")
            VALUES (@id, @user, 'journalProcessing', 'old', @statement, 'granted', 'registration', @time, @submission) RETURNING 1
            """, ("id", consentId), ("user", PostgresTestHost.Owner), ("statement", "Original consent text"), ("time", originalTime), ("submission", submission));
        var termsId = Guid.NewGuid();
        await host.ScalarAsync("""
            INSERT INTO "AgreementEvents" ("Id", "UserId", "AgreementKey", "Version", "Action", "Source", "OccurredAtUtc", "SubmissionId")
            VALUES (@id, @user, 'terms', '2026-11-23', 'accepted', 'registration', @time, @submission) RETURNING 1
            """, ("id", termsId), ("user", PostgresTestHost.Owner), ("time", originalTime), ("submission", submission));
        await host.MigrateAsync();
        Assert.Equal("old", await host.ScalarAsync("SELECT \"Version\" FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing'"));
        Assert.Equal("Original consent text", await host.ScalarAsync("SELECT \"Statement\" FROM \"AgreementEvents\" WHERE \"Id\"=@id", ("id", consentId)));
        Assert.Equal(1L, await host.ScalarAsync("SELECT count(*) FROM \"AgreementEvents\" WHERE \"Id\"=@id AND \"UserId\"=@user AND \"OccurredAtUtc\"=@time AND \"SubmissionId\"=@submission", ("id", consentId), ("user", PostgresTestHost.Owner), ("time", originalTime), ("submission", submission)));
        Assert.Equal(1L, await host.ScalarAsync("SELECT count(*) FROM \"AgreementEvents\" WHERE \"Id\"=@id AND \"Version\"='2026-11-23' AND \"OccurredAtUtc\"=@time AND \"ContentHash\" IS NULL", ("id", termsId), ("time", originalTime)));
        Assert.Equal(1L, await host.ScalarAsync("SELECT count(*) FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing' AND \"ContentHash\" IS NULL AND \"ConsentId\" IS NULL"));
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

        var legal = PostgresTestHost.Legal;
        var prompt = await owner.GetFromJsonAsync<JsonObject>("/api/agreements/journal-consent/statement");
        Assert.Equal(legal.Consent.Id, prompt!["consentId"]!.GetValue<string>());
        Assert.Equal(legal.ConsentFile.Sha256, prompt["contentHash"]!.GetValue<string>());
        Assert.Equal(legal.Consent.Explanation, prompt["explanation"]!.GetValue<string>());
        var submission = new
        {
            submissionId = Guid.NewGuid(),
            items = new[] { new { key = AgreementCatalog.Terms, version = legal.Terms.Version, contentHash = legal.Terms.Sha256 },
                new { key = AgreementCatalog.PrivacyPolicy, version = legal.Privacy.Version, contentHash = legal.Privacy.Sha256 } }
        };
        var concurrent = await Task.WhenAll(owner.PostAsJsonAsync("/api/agreements/me", submission), owner.PostAsJsonAsync("/api/agreements/me", submission));
        foreach (var response in concurrent)
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            response.Dispose();
        }
        Assert.Equal(2L, await host.ScalarAsync("SELECT count(*) FROM \"AgreementEvents\" WHERE \"SubmissionId\"=@id", ("id", submission.submissionId)));
        using var repeated = await owner.PostAsJsonAsync("/api/agreements/me", submission);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        var after = await owner.GetFromJsonAsync<JsonArray>("/api/agreements/me");
        Assert.All(after!, x => Assert.False(x!["actionRequired"]!.GetValue<bool>()));
        var grant = new { submissionId = Guid.NewGuid(), journalProcessingConsent = true, statement = legal.Consent.Statement,
            consentId = legal.Consent.Id, consentVersion = legal.Consent.Version, contentHash = legal.ConsentFile.Sha256 };
        using var wrong = await owner.PostAsJsonAsync("/api/agreements/journal-consent", new { submissionId = Guid.NewGuid(), journalProcessingConsent = true, statement = "different wording" });
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        using var uncheckedConsent = await owner.PostAsJsonAsync("/api/agreements/journal-consent", new { submissionId = Guid.NewGuid(), journalProcessingConsent = false, statement = legal.Consent.Statement });
        Assert.Equal(HttpStatusCode.BadRequest, uncheckedConsent.StatusCode);
        using var granted = await owner.PostAsJsonAsync("/api/agreements/journal-consent", grant);
        Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
        using var retry = await owner.PostAsJsonAsync("/api/agreements/journal-consent", grant);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal(legal.ConsentFile.Sha256, await host.ScalarAsync("SELECT \"ContentHash\" FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing'"));
        Assert.Equal(legal.Consent.Id, await host.ScalarAsync("SELECT \"ConsentId\" FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing'"));
        Assert.Equal(legal.Consent.Version, await host.ScalarAsync("SELECT \"Version\" FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing'"));
        using var conflict = await owner.PostAsJsonAsync("/api/agreements/journal-consent", new { grant.submissionId, journalProcessingConsent = true, statement = "different" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(legal.Consent.Statement, await host.ScalarAsync("SELECT \"Statement\" FROM \"AgreementEvents\" WHERE \"AgreementKey\"='journalProcessing'"));
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
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var forgedHash = await owner.PostAsJsonAsync("/api/agreements/me", new
        {
            submissionId = Guid.NewGuid(), items = new[] { new { key = AgreementCatalog.Terms, version = legal.Terms.Version, contentHash = new string('0', 64) } }
        });
        Assert.Equal(HttpStatusCode.Conflict, forgedHash.StatusCode);
        Assert.Equal(legal.Terms.Sha256, await host.ScalarAsync("SELECT \"ContentHash\" FROM \"AgreementEvents\" WHERE \"AgreementKey\"='terms' AND \"UserId\"=@user", ("user", PostgresTestHost.Owner)));
        await host.ScalarAsync("""
            INSERT INTO "AgreementEvents" ("Id", "UserId", "AgreementKey", "Action", "Source", "OccurredAtUtc", "SubmissionId")
            VALUES (@id, @user, 'journalProcessing', 'withdrawn', 'test', @time, @submission) RETURNING 1
            """, ("id", Guid.NewGuid()), ("user", PostgresTestHost.Owner), ("time", DateTime.UtcNow.AddMinutes(1)), ("submission", Guid.NewGuid()));
        me = await owner.GetFromJsonAsync<JsonObject>("/api/auth/me");
        Assert.Equal("withdrawn", me!["data"]!["journalConsent"]!["status"]!.GetValue<string>());
        using var withdrawn = await owner.GetAsync("/api/emotion-entries");
        Assert.Equal(HttpStatusCode.Forbidden, withdrawn.StatusCode);
    }
}
