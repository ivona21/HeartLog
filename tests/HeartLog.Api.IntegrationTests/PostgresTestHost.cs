using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeartLog.Api.Controllers;
using HeartLog.Api.Middleware;
using HeartLog.BLL;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Services.Encryption;
using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using HeartLog.DAL.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HeartLog.Api.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresTestHost.ConnectionVariable)))
            Skip = $"Opt-in: supply {PostgresTestHost.ConnectionVariable} for a local PostgreSQL server.";
    }
}

// Each test owns a newly named database. Never migrate or delete the supplied database.
internal sealed class PostgresTestHost : IAsyncDisposable
{
    public const string ConnectionVariable = "HEARTLOG_TEST_POSTGRES";
    public const string LegacyMigration = "20261106123526_AddEntryEncryption";
    public static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid Other = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public static readonly Guid Joy = Guid.Parse("44444444-4444-4444-8444-444444444444");
    public static readonly Guid Calm = Guid.Parse("55555555-5555-4555-8555-555555555555");
    public static readonly Guid Hope = Guid.Parse("66666666-6666-4666-8666-666666666666");
    public const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private readonly string _adminConnection;
    private readonly string _database = "heartlog_integration_" + Guid.NewGuid().ToString("N");
    private WebApplication? _app;
    public string ConnectionString { get; }
    public CapturedLogs Logs { get; } = new();

    private PostgresTestHost()
    {
        var supplied = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"Supply {ConnectionVariable} explicitly.");
        var connection = new NpgsqlConnectionStringBuilder(supplied);
        if (connection.Host is not ("localhost" or "127.0.0.1" or "::1") || connection.Database != "postgres")
            throw new InvalidOperationException("Tests require a loopback PostgreSQL server and Database=postgres with CREATEDB permission.");
        connection.Pooling = false;
        connection.IncludeErrorDetail = false;
        _adminConnection = connection.ConnectionString;
        connection.Database = _database;
        ConnectionString = connection.ConnectionString;
    }

    public static async Task<PostgresTestHost> CreateAsync(bool legacy = false)
    {
        var host = new PostgresTestHost();
        await using var admin = new NpgsqlConnection(host._adminConnection);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE \"{host._database}\"", admin).ExecuteNonQueryAsync();
        try
        {
            await host.MigrateAsync(legacy ? LegacyMigration : null);
            await using var db = host.CreateDb();
            db.Users.AddRange(
                new User { Id = Owner, SupabaseUserId = Owner, Email = "owner@example.invalid" },
                new User { Id = Other, SupabaseUserId = Other, Email = "other@example.invalid" });
            db.Emotions.AddRange(
                new Emotion { Id = Joy, Key = "test-joy", Level = EmotionLevel.Core },
                new Emotion { Id = Calm, Key = "test-calm", Level = EmotionLevel.Core },
                new Emotion { Id = Hope, Key = "test-hope", Level = EmotionLevel.Core });
            await db.SaveChangesAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(ConnectionString).Options);

    public async Task MigrateAsync(string? target = null)
    {
        await using var db = CreateDb();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    public async Task StartAsync()
    {
        // Compose the real request path without executing Program.cs: no .env, external
        // identity provider, mail workers, production secrets, or production connection.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(Logs);
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        builder.Services.AddControllers().AddApplicationPart(typeof(EmotionEntriesController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(ConnectionString));
        builder.Services.AddSingleton(new EntryEncryption(Key));
        builder.Services.AddScoped<IEmotionEntryService, EmotionEntryService>();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IEmotionsRepository, EmotionsRepository>();
        builder.Services.AddScoped<IEmotionEntriesRepository, EmotionEntriesRepository>();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseMiddleware<ExceptionHandlingMiddleware>();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
    }

    public HttpClient Client(Guid user)
    {
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", user.ToString());
        return client;
    }

    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }

    public async Task<string> RunMigrationToolAsync(string operation)
    {
        // The real CLI accepts an env file. Generate one with this database and test key only.
        var path = Path.Combine(Path.GetTempPath(), _database + ".env");
        try
        {
            await File.WriteAllTextAsync(path, $"ConnectionStrings__DefaultConnection='{ConnectionString}'\nEntryEncryption__Keys__v1='{Key}'\n");
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "MigrationTool", "HeartLog.EntryMigration.dll"));
            start.ArgumentList.Add(operation);
            start.ArgumentList.Add(path);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            var output = await stdout + await stderr;
            Assert.True(process.ExitCode == 0, $"Migration tool failed: {output}");
            Assert.DoesNotContain(Key, output);
            return output;
        }
        finally { File.Delete(path); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }
}

internal sealed class TestIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.Headers["X-Test-Subject"], out var subject))
            return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject.ToString())], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

internal sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Text)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
    public void Dispose() { }
    private sealed class CaptureLogger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            owner.Entries.Enqueue((LogLevel.Information, Describe(state)));
            return null;
        }
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => owner.Entries.Enqueue((level,
                category + ": " + formatter(state, exception) + " " + Describe(state) + " " + exception));

        // Log values as a normal text provider does; MVC logging state can contain
        // runtime Type/Endpoint objects that cannot be serialized as JSON.
        private static string Describe<TState>(TState state) =>
            state is IEnumerable<KeyValuePair<string, object?>> values
                ? string.Join("; ", values.Select(pair => pair.Key + "=" + pair.Value))
                : state?.ToString() ?? "";
    }
}
