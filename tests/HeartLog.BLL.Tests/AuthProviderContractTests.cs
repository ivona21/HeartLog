using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Models.Auth;
using HeartLog.BLL.Models.Email;
using HeartLog.BLL.Services.Auth;
using HeartLog.BLL.Services.Email;
using HeartLog.DAL.Models;
using Microsoft.Extensions.Options;

namespace HeartLog.BLL.Tests;

public sealed class AuthProviderContractTests
{
    [Theory]
    [InlineData(false, 200)]
    [InlineData(false, 400)]
    [InlineData(false, 422)]
    [InlineData(true, 200)]
    [InlineData(true, 400)]
    [InlineData(true, 422)]
    public async Task RecoveryAndResend_ConcealProviderClientErrors_AndSendExpectedPayload(bool resend, int status)
    {
        using var boundary = new Boundary();
        boundary.Reply(status);
        var provider = boundary.Provider();
        if (resend) await provider.ResendConfirmationAsync("synthetic@example.invalid");
        else await provider.SendPasswordResetAsync("synthetic@example.invalid");
        var call = Assert.Single(boundary.Calls);
        Assert.Equal(resend ? "/auth/v1/resend" : "/auth/v1/recover", call.Path);
        Assert.Equal("POST", call.Method);
        Assert.Equal("synthetic@example.invalid", JsonDocument.Parse(call.Body).RootElement.GetProperty("email").GetString());
        if (resend) Assert.Equal("signup", JsonDocument.Parse(call.Body).RootElement.GetProperty("type").GetString());
        Assert.Equal("synthetic-public", call.ApiKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryAndResend_ProviderOutageIsAnError(bool resend)
    {
        using var boundary = new Boundary();
        boundary.Reply(503);
        var provider = boundary.Provider();
        await Assert.ThrowsAsync<ExternalAuthException>(() => resend
            ? provider.ResendConfirmationAsync("synthetic@example.invalid")
            : provider.SendPasswordResetAsync("synthetic@example.invalid"));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    public async Task InvalidRecoveryOrRefreshToken_IsUnauthorized(int status)
    {
        using var boundary = new Boundary();
        var provider = boundary.Provider();
        boundary.Reply(status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResetPasswordAsync("expired", "Synthetic123!"));
        boundary.Reply(status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.RefreshAsync("expired"));
        Assert.Equal(2, boundary.Calls.Count); // Rejected reset never revokes sessions.
    }

    [Fact]
    public async Task Reset_UpdatesPasswordThenRevokesAllSessions_AndSurfacesRevocationFailure()
    {
        using var boundary = new Boundary();
        var provider = boundary.Provider();
        boundary.Reply(200);
        boundary.Reply(503);
        await Assert.ThrowsAsync<ExternalAuthException>(() => provider.ResetPasswordAsync("recovery-access", "Synthetic123!"));
        Assert.Equal(new[] { "/auth/v1/user", "/auth/v1/logout?scope=global" }, boundary.Calls.Select(c => c.Path));
        Assert.Equal("PUT", boundary.Calls[0].Method);
        Assert.Equal("Synthetic123!", JsonDocument.Parse(boundary.Calls[0].Body).RootElement.GetProperty("password").GetString());
        Assert.All(boundary.Calls, c => Assert.Equal("Bearer recovery-access", c.Authorization));
    }

    [Fact]
    public async Task Refresh_ValidatesProviderSessionShape_AndReturnsTokensAndExpiry()
    {
        using var boundary = new Boundary();
        var provider = boundary.Provider();
        var id = Guid.NewGuid();
        boundary.Reply(200, new { access_token = "access", refresh_token = "rotated", expires_at = 1893456000,
            user = new { id, email = "synthetic@example.invalid" } });
        var session = await provider.RefreshAsync("old-refresh");
        Assert.Equal(id, session.User.ProviderUserId);
        Assert.Equal("access", session.AccessToken);
        Assert.Equal("rotated", session.RefreshToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000).UtcDateTime, session.ExpiresAt);
        foreach (var malformed in new object[] { new { }, new { access_token = "access", user = new { id = "invalid", email = "synthetic@example.invalid" } },
            new { user = new { id, email = "synthetic@example.invalid" } }, new { access_token = "access", user = new { id, email = "" } } })
        {
            boundary.Reply(200, malformed);
            await Assert.ThrowsAsync<ExternalAuthException>(() => provider.RefreshAsync("old-refresh"));
        }
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(404)]
    public async Task Delete_AcceptsSuccessAndAlreadyAbsent_UsesAdminApiKey(int status)
    {
        using var boundary = new Boundary();
        boundary.Reply(status);
        var id = Guid.NewGuid();
        await boundary.Provider().DeleteUserAsync(id);
        var call = Assert.Single(boundary.Calls);
        Assert.Equal("DELETE", call.Method);
        Assert.Equal($"/auth/v1/admin/users/{id}", call.Path);
        Assert.Equal("sb_secret_synthetic", call.ApiKey);
        Assert.Equal("", call.Authorization); // Secret API keys are not JWT bearer tokens.
    }

    [Theory]
    [InlineData(401)]
    [InlineData(500)]
    public async Task Delete_RejectsOtherFailures(int status)
    {
        using var boundary = new Boundary();
        boundary.Reply(status);
        await Assert.ThrowsAsync<ExternalAuthException>(() => boundary.Provider().DeleteUserAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task BlankTokensAndUnsupportedConfirmationTypes_FailBeforeNetwork()
    {
        using var boundary = new Boundary();
        var provider = boundary.Provider();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.RefreshAsync(" "));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResetPasswordAsync(" ", "Synthetic123!"));
        var confirm = await Assert.ThrowsAsync<EmailConfirmationException>(() => provider.ConfirmEmailAsync("hash", "signup"));
        var reset = await Assert.ThrowsAsync<EmailConfirmationException>(() => provider.ConfirmPasswordResetAsync("hash", "email"));
        Assert.Equal(EmailConfirmationFailureReason.Invalid, confirm.Reason);
        Assert.Equal(EmailConfirmationFailureReason.Invalid, reset.Reason);
        Assert.Empty(boundary.Calls);
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(400, true)]
    [InlineData(422, true)]
    [InlineData(429, false)]
    [InlineData(503, false)]
    public async Task CompletionEmail_UsesStableIdempotencyKey_AndClassifiesFailures(int status, bool permanent)
    {
        using var boundary = new Boundary();
        boundary.Reply(status);
        var email = new ResendEmailService(boundary.Client, Options.Create(new EmailSettings
        { Enabled = true, ApiKey = "synthetic-mail-key", FromAddress = "HeartLog <sender@example.invalid>" }));
        var message = new EmailMessage(Guid.NewGuid(), "synthetic@example.invalid", EmailOutboxMessage.AccountDeletedTemplate, DateTime.UtcNow);
        if (status == 200) await email.SendAsync(message);
        else
        {
            var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => email.SendAsync(message));
            Assert.Equal(permanent, error.Permanent);
        }
        var call = Assert.Single(boundary.Calls);
        Assert.Equal("/emails", call.Path);
        Assert.Equal($"account-deleted/{message.Id}", call.IdempotencyKey);
        Assert.Equal("Bearer synthetic-mail-key", call.Authorization);
        var body = JsonDocument.Parse(call.Body).RootElement;
        Assert.Equal(message.Recipient, body.GetProperty("to")[0].GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("subject").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("text").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("html").GetString()));
    }

    // An HttpMessageHandler substitutes the external boundary, with no network or DB.
    private sealed class Boundary : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<(string Method, string Path, string Body, string ApiKey, string Authorization, string IdempotencyKey)> Calls { get; } = [];
        public HttpClient Client { get; }
        public Boundary() => Client = new HttpClient(this, disposeHandler: false);
        public HttpClient CreateClient(string name) => Client;
        public void Reply(int status, object? body = null) => _responses.Enqueue(new HttpResponseMessage((HttpStatusCode)status) { Content = JsonContent.Create(body ?? new { }) });
        public SupabaseAuthService Provider() => new(Options.Create(new SupabaseSettings
        { ProjectUrl = "https://supabase.example.invalid", PublishableKey = "synthetic-public", SecretKey = "sb_secret_synthetic" }), this);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("apikey", out var keys) ? keys.Single() : "", request.Headers.Authorization?.ToString() ?? "",
                request.Headers.TryGetValues("Idempotency-Key", out var ids) ? ids.Single() : ""));
            Assert.NotEmpty(_responses);
            return _responses.Dequeue();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Client.Dispose(); foreach (var response in _responses) response.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
