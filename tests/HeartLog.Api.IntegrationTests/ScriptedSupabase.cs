using System.Net;
using System.Text.Json;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Auth;
using HeartLog.BLL.Services.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeartLog.Api.IntegrationTests;

// The SDK constructs its own HttpClient, so use a loopback-only provider for both
// SDK calls and the adapter's IHttpClientFactory calls. Never contact Supabase.
internal sealed class ScriptedSupabase : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Queue<(string Method, string Path, int Status, object Body)> _steps = new();
    public List<(string Method, string Path, string Body, string ApiKey, string Authorization)> Calls { get; } = [];
    private readonly List<(string Method, string Path)> _expected = [];
    public string Url => _app.Urls.Single();
    public const string Password = "Synthetic123!";
    public const string Access = "synthetic-access";
    public const string Refresh = "synthetic-refresh";

    private ScriptedSupabase(WebApplication app) => _app = app;

    public static async Task<ScriptedSupabase> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var provider = new ScriptedSupabase(app);
        app.Run(async context =>
        {
            var path = context.Request.Path + context.Request.QueryString;
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            provider.Calls.Add((context.Request.Method, path, body,
                context.Request.Headers["apikey"].ToString(), context.Request.Headers.Authorization.ToString()));
            if (!provider._steps.TryDequeue(out var step))
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { msg = "Unexpected synthetic provider call" });
                return;
            }
            context.Response.StatusCode = step.Status;
            await context.Response.WriteAsJsonAsync(step.Body);
        });
        await app.StartAsync();
        return provider;
    }

    public void Reply(string method, string path, int status = 200, object? body = null)
    {
        _expected.Add((method, path));
        _steps.Enqueue((method, path, status, body ?? new { }));
    }

    public static object User(Guid id, string email) => new { id, email, aud = "authenticated", role = "authenticated",
        confirmed_at = "2026-01-01T00:00:00Z", email_confirmed_at = "2026-01-01T00:00:00Z" };
    public static object Session(Guid id, string email, string access = Access, string refresh = Refresh) => new
    {
        access_token = access, refresh_token = refresh, token_type = "bearer", expires_in = 3600,
        expires_at = 1893456000, user = User(id, email)
    };
    public static object Error(string message, string code) => new { msg = message, error_description = message, error = message, error_code = code };

    public void Configure(IServiceCollection services)
    {
        services.AddHttpClient();
        services.Configure<SupabaseSettings>(o =>
        {
            o.ProjectUrl = Url;
            o.PublishableKey = "synthetic-public-key";
            o.SecretKey = "sb_secret_synthetic";
        });
        services.AddScoped<IExternalAuthService, SupabaseAuthService>();
        services.AddScoped<IUserService, HeartLog.BLL.UserService>();
    }

    public void AssertComplete()
    {
        Assert.Empty(_steps);
        Assert.Equal(_expected, Calls.Select(c => (c.Method, c.Path)).ToList());
        Assert.All(Calls, c => Assert.Contains(c.ApiKey, new[] { "synthetic-public-key", "sb_secret_synthetic" }));
    }

    public static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement.Clone();
    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
