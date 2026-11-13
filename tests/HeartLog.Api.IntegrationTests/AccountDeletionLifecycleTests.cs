using System.Net;
using System.Net.Http.Json;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Email;
using HeartLog.BLL.Services.Email;
using HeartLog.DAL.Models;
using HeartLog.DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HeartLog.Api.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class AccountDeletionLifecycleTests
{
    [PostgresFact]
    public async Task DeleteMe_VerifiesCurrentIdentity_BlocksAccess_RetriesProviderCleanup_ThenSendsCompletionEmail()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        using var owner = host.Client(PostgresTestHost.Owner);
        using var other = host.Client(PostgresTestHost.Other);
        foreach (var client in new[] { owner, other })
        {
            using var entry = await client.PostAsJsonAsync("/api/emotion-entries", new { emotionKeys = new[] { "test-joy" }, primaryEmotionKey = "test-joy", comment = "synthetic account data" });
            Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        }
        using var missingBody = await owner.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/auth/me") { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.BadRequest, missingBody.StatusCode);
        provider.Reply("POST", "/auth/v1/token?grant_type=password", 400, ScriptedSupabase.Error("Invalid login credentials", "invalid_credentials"));
        using var wrongPassword = await Delete(owner, "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal("invalidCurrentPassword", (await AuthenticationLifecycleTests.Json(wrongPassword))["code"]!.GetValue<string>());
        provider.Reply("POST", "/auth/v1/token?grant_type=password", body: ScriptedSupabase.Session(PostgresTestHost.Other, "other@example.invalid"));
        using var mismatchedProvider = await Delete(owner, ScriptedSupabase.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, mismatchedProvider.StatusCode);
        await using (var db = host.CreateDb())
        {
            Assert.Empty(await db.AccountDeletions.ToListAsync());
            Assert.All(await db.Users.ToListAsync(), u => Assert.Null(u.DeletionRequestedAt));
        }
        provider.Reply("POST", "/auth/v1/token?grant_type=password", body: ScriptedSupabase.Session(PostgresTestHost.Owner, "owner@example.invalid"));
        using var accepted = await Delete(owner, ScriptedSupabase.Password);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("Account deletion requested.", (await AuthenticationLifecycleTests.Json(accepted))["message"]!.GetValue<string>());
        AuthenticationLifecycleTests.AssertDeletedCookies(accepted, "heartlog_refresh_token", "heartlog_password_reset_token");
        Assert.Equal("owner@example.invalid", ScriptedSupabase.Json(provider.Calls.Last().Body).GetProperty("email").GetString());
        using var repeated = await Delete(owner, "not-verified-again");
        Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode);
        using var me = await owner.GetAsync("/api/auth/me");
        using var entries = await owner.GetAsync("/api/emotion-entries");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, entries.StatusCode);
        provider.Reply("POST", "/auth/v1/token?grant_type=refresh_token", body: ScriptedSupabase.Session(PostgresTestHost.Owner, "owner@example.invalid"));
        owner.DefaultRequestHeaders.Add("Cookie", "heartlog_refresh_token=synthetic-refresh");
        using var refresh = await owner.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        provider.Reply("POST", "/auth/v1/token?grant_type=password", body: ScriptedSupabase.Session(PostgresTestHost.Owner, "owner@example.invalid"));
        provider.Reply("GET", "/auth/v1/user", body: ScriptedSupabase.User(PostgresTestHost.Owner, "owner@example.invalid"));
        using var login = await owner.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.invalid", password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var email = new CapturedEmail();
        await ProcessEmail(host, email);
        Assert.Empty(email.Messages);
        provider.Reply("DELETE", $"/auth/v1/admin/users/{PostgresTestHost.Owner}", 503);
        await ProcessDeletions(host);
        await using (var db = host.CreateDb())
        {
            var task = Assert.Single(await db.AccountDeletions.ToListAsync());
            Assert.Equal(PostgresTestHost.Owner, task.SupabaseUserId);
            Assert.Equal(1, task.Attempts);
            Assert.True(task.NextAttemptAt > DateTime.UtcNow);
            Assert.Equal(2, await db.Users.CountAsync());
            Assert.Equal(2, await db.EmotionEntries.CountAsync());
            Assert.Empty(await db.EmailOutbox.ToListAsync());
        }
        await ProcessDeletions(host); // Future retry is not attempted early.
        await ProcessEmail(host, email);
        Assert.Empty(email.Messages);
        await host.ScalarAsync("UPDATE \"AccountDeletions\" SET \"NextAttemptAt\"=now() - interval '1 minute' RETURNING 1");
        // Fail the local commit after Supabase succeeds: the delete and outbox
        // insert must roll back together, leaving the durable task retryable.
        await host.ScalarAsync("ALTER TABLE \"EmailOutbox\" ADD CONSTRAINT synthetic_delivery_failure CHECK (false)");
        provider.Reply("DELETE", $"/auth/v1/admin/users/{PostgresTestHost.Owner}");
        await ProcessDeletions(host);
        await using (var db = host.CreateDb())
        {
            Assert.Single(await db.AccountDeletions.ToListAsync());
            Assert.Equal(2, await db.Users.CountAsync());
            Assert.Equal(2, await db.EmotionEntries.CountAsync());
            Assert.Empty(await db.EmailOutbox.ToListAsync());
        }
        await host.ScalarAsync("ALTER TABLE \"EmailOutbox\" DROP CONSTRAINT synthetic_delivery_failure");
        // Already absent in Supabase is successful cleanup, allowing safe retry.
        provider.Reply("DELETE", $"/auth/v1/admin/users/{PostgresTestHost.Owner}", 404);
        await ProcessDeletions(host);
        await using (var db = host.CreateDb())
        {
            Assert.Empty(await db.AccountDeletions.ToListAsync());
            Assert.Equal(PostgresTestHost.Other, Assert.Single(await db.Users.ToListAsync()).Id);
            Assert.Equal(PostgresTestHost.Other, Assert.Single(await db.EmotionEntries.ToListAsync()).UserId);
            var queued = Assert.Single(await db.EmailOutbox.ToListAsync());
            Assert.Equal("owner@example.invalid", queued.Recipient);
            Assert.Equal(EmailOutboxMessage.AccountDeletedTemplate, queued.Template);
        }
        Assert.Empty(email.Messages);
        // A transient delivery error retains the notification, with a stable message id.
        email.FailNext = true;
        await ProcessEmail(host, email);
        var firstAttempt = Assert.Single(email.Messages);
        await using (var db = host.CreateDb())
        {
            var queued = Assert.Single(await db.EmailOutbox.ToListAsync());
            Assert.Equal(1, queued.Attempts);
            Assert.True(queued.NextAttemptAt > DateTime.UtcNow);
            Assert.Null(queued.LeaseId);
        }
        await ProcessEmail(host, email);
        Assert.Single(email.Messages);
        await host.ScalarAsync("UPDATE \"EmailOutbox\" SET \"NextAttemptAt\"=now() - interval '1 minute' RETURNING 1");
        await ProcessEmail(host, email);
        Assert.Equal(2, email.Messages.Count);
        Assert.Equal(firstAttempt.Id, email.Messages[1].Id);
        Assert.Equal("owner@example.invalid", email.Messages[1].Recipient);
        Assert.Equal(EmailOutboxMessage.AccountDeletedTemplate, email.Messages[1].Template);
        await ProcessDeletions(host);
        await ProcessEmail(host, email);
        Assert.Equal(2, email.Messages.Count);
        await using (var db = host.CreateDb()) Assert.Empty(await db.EmailOutbox.ToListAsync());
        using var deletedAgain = await Delete(owner, ScriptedSupabase.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, deletedAgain.StatusCode);
        using var otherMe = await other.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, otherMe.StatusCode);
        provider.AssertComplete();
    }

    [PostgresFact]
    public async Task DeleteMe_ProviderVerificationOutage_DoesNotRequestDeletion()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        using var owner = host.Client(PostgresTestHost.Owner);
        provider.Reply("POST", "/auth/v1/token?grant_type=password", 500, ScriptedSupabase.Error("Synthetic provider unavailable", "unexpected_failure"));
        using var response = await Delete(owner, ScriptedSupabase.Password);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var db = host.CreateDb();
        Assert.Empty(await db.AccountDeletions.ToListAsync());
        Assert.Null((await db.Users.SingleAsync(u => u.Id == PostgresTestHost.Owner)).DeletionRequestedAt);
        provider.AssertComplete();
    }

    private static Task<HttpResponseMessage> Delete(HttpClient client, string password) => client.SendAsync(
        new HttpRequestMessage(HttpMethod.Delete, "/api/auth/me") { Content = JsonContent.Create(new { currentPassword = password }) });

    private static async Task ProcessDeletions(PostgresTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserService>().ProcessAccountDeletionsAsync(CancellationToken.None);
    }

    private static async Task ProcessEmail(PostgresTestHost host, CapturedEmail email)
    {
        await using var db = host.CreateDb();
        var service = new EmailOutboxService(new EmailOutboxRepository(db), email,
            Options.Create(new EmailSettings { Enabled = true }), NullLogger<EmailOutboxService>.Instance);
        await service.ProcessAsync(CancellationToken.None);
    }

    private sealed class CapturedEmail : IEmailService
    {
        public List<EmailMessage> Messages { get; } = [];
        public bool FailNext { get; set; }
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            if (FailNext) { FailNext = false; throw new EmailDeliveryException("Synthetic delivery failure"); }
            return Task.CompletedTask;
        }
    }
}
