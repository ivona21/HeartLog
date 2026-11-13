using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HeartLog.Api.Auth;
using HeartLog.BLL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace HeartLog.Api.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class AuthenticationLifecycleTests
{
    private const string Route = "/api/auth";
    private const string Email = "new-account@example.invalid";

    [PostgresFact]
    public async Task Login_ProvisionsUser_MeResolvesThem_RefreshRotatesCookie_LogoutClearsIt()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        var id = Guid.NewGuid();
        using var client = host.Client(id);
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        provider.Reply("POST", "/auth/v1/token?grant_type=password", body: ScriptedSupabase.Session(id, Email));
        provider.Reply("GET", "/auth/v1/user", body: ScriptedSupabase.User(id, Email));
        using var login = await client.PostAsJsonAsync($"{Route}/login", new { email = Email, password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await Json(login);
        Assert.Equal(ScriptedSupabase.Access, body["data"]!["accessToken"]!.GetValue<string>());
        Assert.Equal(Email, body["data"]!["email"]!.GetValue<string>());
        Assert.NotNull(body["data"]!["expiresAt"]);
        Assert.DoesNotContain(ScriptedSupabase.Refresh, body.ToJsonString());
        AssertCookie(login, RefreshTokenCookie.Name, ScriptedSupabase.Refresh, RefreshTokenCookie.Path, TimeSpan.FromDays(30));
        Assert.Equal(ScriptedSupabase.Access, provider.Calls[1].Authorization["Bearer ".Length..]);
        await using (var db = host.CreateDb())
        {
            var user = await db.Users.SingleAsync(u => u.SupabaseUserId == id);
            Assert.Equal(Email, user.Email);
        }
        client.DefaultRequestHeaders.Add("X-Test-Subject", id.ToString());
        using var me = await client.GetAsync($"{Route}/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(Email, (await Json(me))["data"]!["email"]!.GetValue<string>());
        provider.Reply("POST", "/auth/v1/token?grant_type=refresh_token", body: ScriptedSupabase.Session(id, Email, "rotated-access", "rotated-refresh"));
        client.DefaultRequestHeaders.Add("Cookie", $"{RefreshTokenCookie.Name}={ScriptedSupabase.Refresh}");
        using var refresh = await client.PostAsync($"{Route}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal("rotated-access", (await Json(refresh))["data"]!["accessToken"]!.GetValue<string>());
        AssertCookie(refresh, RefreshTokenCookie.Name, "rotated-refresh", RefreshTokenCookie.Path, TimeSpan.FromDays(30));
        Assert.Equal(ScriptedSupabase.Refresh, ScriptedSupabase.Json(provider.Calls.Last().Body).GetProperty("refresh_token").GetString());
        using var logout = await client.PostAsync($"{Route}/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        AssertDeletedCookies(logout, RefreshTokenCookie.Name);
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        client.DefaultRequestHeaders.Remove("Cookie");
        using var anonymousLogout = await client.PostAsync($"{Route}/logout", null);
        Assert.Equal(HttpStatusCode.OK, anonymousLogout.StatusCode);
        AssertDeletedCookies(anonymousLogout, RefreshTokenCookie.Name);
        // Logout is local cookie clearing; it makes no provider revocation call.
        provider.AssertComplete();
    }

    [PostgresFact]
    public async Task LoginFailures_DoNotProvisionOrSetCookies_AndRefreshRequiresExistingLocalUser()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        using var client = host.Client(Guid.NewGuid());
        foreach (var (message, code, status, expected) in new[]
        {
            ("Invalid login credentials", "invalid_credentials", 400, HttpStatusCode.Unauthorized),
            ("Email not confirmed", "email_not_confirmed", 400, HttpStatusCode.Unauthorized)
        })
        {
            provider.Reply("POST", "/auth/v1/token?grant_type=password", status, ScriptedSupabase.Error(message, code));
            using var rejected = await client.PostAsJsonAsync($"{Route}/login", new { email = Email, password = "wrong" });
            Assert.Equal(expected, rejected.StatusCode);
            Assert.Equal(code == "invalid_credentials" ? "invalidCredentials" : "emailNotConfirmed", (await Json(rejected))["code"]!.GetValue<string>());
            Assert.False(rejected.Headers.Contains("Set-Cookie"));
        }
        provider.Reply("POST", "/auth/v1/token?grant_type=password", 503, ScriptedSupabase.Error("Synthetic provider unavailable", "unexpected_failure"));
        using var outage = await client.PostAsJsonAsync($"{Route}/login", new { email = Email, password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);
        Assert.False(outage.Headers.Contains("Set-Cookie"));
        using var noCookie = await client.PostAsync($"{Route}/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", $"{RefreshTokenCookie.Name}=expired");
        provider.Reply("POST", "/auth/v1/token?grant_type=refresh_token", 401);
        using var expired = await client.PostAsync($"{Route}/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        provider.Reply("POST", "/auth/v1/token?grant_type=refresh_token", body: ScriptedSupabase.Session(Guid.NewGuid(), Email));
        using var missingLocal = await client.PostAsync($"{Route}/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, missingLocal.StatusCode);
        provider.Reply("POST", "/auth/v1/token?grant_type=refresh_token", body: ScriptedSupabase.Session(PostgresTestHost.Owner, "owner@example.invalid", refresh: ""));
        using var missingRefresh = await client.PostAsync($"{Route}/refresh", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingRefresh.StatusCode);
        Assert.False(missingRefresh.Headers.Contains("Set-Cookie"));
        using var missingMe = await client.GetAsync($"{Route}/me");
        Assert.Equal(HttpStatusCode.Unauthorized, missingMe.StatusCode);
        Assert.Equal(2L, await host.ScalarAsync("SELECT count(*) FROM \"Users\""));
        provider.AssertComplete();
    }

    [PostgresFact]
    public async Task SignupAndDuplicateEmail_ReturnSameConfirmationResponse_WithoutLocalProvisioning()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        using var client = host.Client(PostgresTestHost.Owner);
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        provider.Reply("POST", "/auth/v1/signup", body: new { id = Guid.NewGuid(), email = Email, email_confirmed_at = (string?)null });
        using var signup = await client.PostAsJsonAsync($"{Route}/register", new { email = Email, password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
        var first = await Json(signup);
        Assert.Equal(Email, first["data"]!["email"]!.GetValue<string>());
        Assert.Contains("confirm your email", first["message"]!.GetValue<string>());
        Assert.False(signup.Headers.Contains("Set-Cookie"));
        provider.Reply("POST", "/auth/v1/signup", 422, ScriptedSupabase.Error("User already registered", "user_already_exists"));
        provider.Reply("POST", "/auth/v1/resend", 422);
        using var duplicate = await client.PostAsJsonAsync($"{Route}/register", new { email = Email, password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(first.ToJsonString(), (await Json(duplicate)).ToJsonString());
        Assert.False(duplicate.Headers.Contains("Set-Cookie"));
        Assert.Equal("signup", ScriptedSupabase.Json(provider.Calls.Last().Body).GetProperty("type").GetString());
        Assert.Equal(2L, await host.ScalarAsync("SELECT count(*) FROM \"Users\""));
        using var invalid = await client.PostAsJsonAsync($"{Route}/register", new { email = "bad", password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        provider.Reply("POST", "/auth/v1/signup", 500, ScriptedSupabase.Error("Synthetic provider outage", "unexpected_failure"));
        using var outage = await client.PostAsJsonAsync($"{Route}/register", new { email = Email, password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);
        provider.AssertComplete();
    }

    [PostgresFact]
    public async Task ForgotAndResend_ConcealUnknownOrIneligibleAccounts_ButSurfaceProviderOutage()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        using var client = host.Client(PostgresTestHost.Owner);
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        foreach (var (route, path) in new[] { ("forgot-password", "recover"), ("resend-confirmation", "resend") })
        {
            string? expected = null;
            foreach (var (email, status) in new[] { ("owner@example.invalid", 200), ("unknown@example.invalid", 422) })
            {
                provider.Reply("POST", $"/auth/v1/{path}", status);
                using var response = await client.PostAsJsonAsync($"{Route}/{route}", new { email });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var text = await response.Content.ReadAsStringAsync();
                expected ??= text;
                Assert.Equal(expected, text);
                Assert.Equal(email, ScriptedSupabase.Json(provider.Calls.Last().Body).GetProperty("email").GetString());
            }
            provider.Reply("POST", $"/auth/v1/{path}", 503);
            using var outage = await client.PostAsJsonAsync($"{Route}/{route}", new { email = Email });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);
            using var invalid = await client.PostAsJsonAsync($"{Route}/{route}", new { email = "bad" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        client.DefaultRequestHeaders.Add("X-Test-Subject", PostgresTestHost.Owner.ToString());
        provider.Reply("POST", "/auth/v1/recover");
        using var current = await client.PostAsync($"{Route}/forgot-password/me", null);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal("owner@example.invalid", ScriptedSupabase.Json(provider.Calls.Last().Body).GetProperty("email").GetString());
        provider.AssertComplete();
    }

    [PostgresFact]
    public async Task ConfirmationCallbacks_RedirectSuccessInvalidAndExpired_RecoveryCookieCompletesReset()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        using var client = host.Client(PostgresTestHost.Owner);
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        foreach (var recovery in new[] { false, true })
        {
            var route = recovery ? "reset-password/confirm" : "confirm-email";
            var frontend = recovery ? "reset-password" : "email-confirmation";
            var type = recovery ? "recovery" : "email";
            foreach (var status in new[] { "success", "invalid", "expired" })
            {
                provider.Reply("POST", "/auth/v1/verify", status == "success" ? 200 : 403,
                    status == "success" ? ScriptedSupabase.Session(PostgresTestHost.Owner, "owner@example.invalid")
                        : ScriptedSupabase.Error(status == "expired" ? "Token has expired" : "Token is invalid", status == "expired" ? "otp_expired" : "otp_invalid"));
                using var response = await client.GetAsync($"{Route}/{route}?token_hash=synthetic-hash&type={type}");
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                var expected = recovery && status == "success" ? "ready" : status;
                Assert.Equal($"https://frontend.example.invalid/{frontend}?status={expected}", response.Headers.Location!.ToString());
                var sent = ScriptedSupabase.Json(provider.Calls.Last().Body);
                Assert.Equal("synthetic-hash", sent.GetProperty("token_hash").GetString());
                Assert.Equal(type, sent.GetProperty("type").GetString());
                if (recovery && status == "success")
                    AssertCookie(response, PasswordResetCookie.Name, ScriptedSupabase.Access, PasswordResetCookie.Path, TimeSpan.FromMinutes(15));
                else Assert.False(response.Headers.Contains("Set-Cookie"));
            }
            using var wrongType = await client.GetAsync($"{Route}/{route}?token_hash=synthetic-hash&type=unsupported");
            Assert.Equal($"https://frontend.example.invalid/{frontend}?status=invalid", wrongType.Headers.Location!.ToString());
            // Missing query arguments are rejected by ApiController binding, before the action.
            using var missing = await client.GetAsync($"{Route}/{route}");
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }
        using var noCookie = await client.PostAsJsonAsync($"{Route}/reset-password", new { password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", $"{PasswordResetCookie.Name}={ScriptedSupabase.Access}");
        using var weak = await client.PostAsJsonAsync($"{Route}/reset-password", new { password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        provider.Reply("PUT", "/auth/v1/user", 401);
        using var invalidToken = await client.PostAsJsonAsync($"{Route}/reset-password", new { password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);
        provider.Reply("PUT", "/auth/v1/user");
        provider.Reply("POST", "/auth/v1/logout?scope=global");
        using var reset = await client.PostAsJsonAsync($"{Route}/reset-password", new { password = ScriptedSupabase.Password });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        AssertDeletedCookies(reset, PasswordResetCookie.Name, RefreshTokenCookie.Name);
        Assert.Equal($"Bearer {ScriptedSupabase.Access}", provider.Calls.Last().Authorization);
        provider.AssertComplete();
    }

    [PostgresFact]
    public async Task ChangePassword_RequiresBearerAndCurrentPassword_UpdatesProviderAndRevokesOtherSessions()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        using var client = host.Client(PostgresTestHost.Owner);
        var request = new { currentPassword = ScriptedSupabase.Password, newPassword = "ChangedSynthetic123!" };
        using var missingBearer = await client.PostAsJsonAsync($"{Route}/change-password", request);
        Assert.Equal(HttpStatusCode.Unauthorized, missingBearer.StatusCode);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ScriptedSupabase.Access);
        provider.Reply("POST", "/auth/v1/token?grant_type=password", 400, ScriptedSupabase.Error("Invalid login credentials", "invalid_credentials"));
        using var wrongPassword = await client.PostAsJsonAsync($"{Route}/change-password", request);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal("invalidCurrentPassword", (await Json(wrongPassword))["code"]!.GetValue<string>());
        using var weak = await client.PostAsJsonAsync($"{Route}/change-password", new { currentPassword = "password", newPassword = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        provider.Reply("POST", "/auth/v1/token?grant_type=password", body: ScriptedSupabase.Session(PostgresTestHost.Owner, "owner@example.invalid"));
        provider.Reply("PUT", "/auth/v1/user");
        provider.Reply("POST", "/auth/v1/logout?scope=others");
        using var changed = await client.PostAsJsonAsync($"{Route}/change-password", request);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var update = provider.Calls[^2];
        Assert.Equal($"Bearer {ScriptedSupabase.Access}", update.Authorization);
        var body = ScriptedSupabase.Json(update.Body);
        Assert.Equal(request.newPassword, body.GetProperty("password").GetString());
        Assert.Equal(request.currentPassword, body.GetProperty("current_password").GetString());
        Assert.False(changed.Headers.Contains("Set-Cookie"));
        provider.AssertComplete();
    }

    [PostgresFact]
    public async Task ProtectedAccountRoutes_RejectAnonymousRequests()
    {
        await using var provider = await ScriptedSupabase.StartAsync();
        await using var host = await PostgresTestHost.CreateAsync();
        await host.StartAsync(provider.Configure);
        using var client = host.Client(PostgresTestHost.Owner);
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        using var me = await client.GetAsync($"{Route}/me");
        using var forgot = await client.PostAsync($"{Route}/forgot-password/me", null);
        using var change = await client.PostAsJsonAsync($"{Route}/change-password", new { currentPassword = "wrong", newPassword = ScriptedSupabase.Password });
        using var delete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Route}/me") { Content = JsonContent.Create(new { currentPassword = ScriptedSupabase.Password }) });
        foreach (var response in new[] { me, forgot, change, delete }) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        provider.AssertComplete();
    }

    internal static async Task<JsonNode> Json(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    internal static void AssertCookie(HttpResponseMessage response, string name, string value, string path, TimeSpan lifetime)
    {
        var cookie = Assert.Single(SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList()), c => c.Name == name);
        Assert.Equal(value, cookie.Value.ToString());
        Assert.Equal(path, cookie.Path.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.None, cookie.SameSite);
        Assert.InRange(cookie.Expires!.Value, DateTimeOffset.UtcNow.Add(lifetime).AddMinutes(-1), DateTimeOffset.UtcNow.Add(lifetime).AddMinutes(1));
    }

    internal static void AssertDeletedCookies(HttpResponseMessage response, params string[] names)
    {
        var cookies = SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList());
        foreach (var name in names)
        {
            var cookie = Assert.Single(cookies, c => c.Name == name);
            // ASP.NET Cookie.Delete expires an empty cookie and omits Max-Age.
            Assert.Null(cookie.MaxAge);
            Assert.Equal("", cookie.Value.ToString());
            Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
            Assert.True(cookie.HttpOnly);
            Assert.True(cookie.Secure);
            Assert.Equal(name == RefreshTokenCookie.Name ? RefreshTokenCookie.Path : PasswordResetCookie.Path, cookie.Path.ToString());
        }
    }
}
