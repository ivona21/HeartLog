using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

// Local-only integration runner. Never loads production configuration.
const string adminConnectionString = "Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Password=local-migration-test;Include Error Detail=false";
var databaseName = "migration_check_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(adminConnectionString);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin).ExecuteNonQueryAsync();
var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
var root = Directory.GetCurrentDirectory();
var envPath = Path.Combine(Path.GetTempPath(), $"heartlog-entry-check-{Guid.NewGuid():N}.env");
var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
await File.WriteAllTextAsync(envPath, $"ConnectionStrings__DefaultConnection={connectionString}\nEntryEncryption__Keys__v1={key}\n");
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(envPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
await using var db = new ApplicationDbContext(options);
await db.GetService<IMigrator>().MigrateAsync("20261106123526_AddEntryEncryption");
var owner = Guid.NewGuid();
var firstEmotion = Guid.NewGuid();
var secondEmotion = Guid.NewGuid();
db.Users.Add(new User { Id = owner, Email = "migration-check@example.invalid" });
db.Emotions.AddRange(new Emotion { Id = firstEmotion, Key = "migration-first", Level = EmotionLevel.Core, IsActive = false }, new Emotion { Id = secondEmotion, Key = "migration-second", Level = EmotionLevel.Core });
await db.SaveChangesAsync();
var notes = new string?[] { null, "", "  Čuvam osjećaje 💙\nwith whitespace  " };
var ids = Enumerable.Range(1, 3).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToArray();
await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
for (var i = 0; i < ids.Length; i++)
{
    await using var insert = new NpgsqlCommand("INSERT INTO \"EmotionEntries\" (\"Id\", \"UserId\", \"Comment\", \"OccurredAt\", \"CreatedAt\", \"UpdatedAt\") VALUES (@id, @owner, @note, '2025-01-01Z', '2025-01-02Z', '2025-01-03Z'); INSERT INTO \"EmotionEntryEmotions\" (\"Id\", \"EmotionEntryId\", \"EmotionId\", \"IsPrimary\", \"CreatedAt\") VALUES (@link1, @id, @emotion1, true, now()), (@link2, @id, @emotion2, false, now())", connection);
    insert.Parameters.AddWithValue("id", ids[i]); insert.Parameters.AddWithValue("owner", owner);
    insert.Parameters.AddWithValue("note", NpgsqlTypes.NpgsqlDbType.Text, (object?)notes[i] ?? DBNull.Value);
    insert.Parameters.AddWithValue("link1", Guid.NewGuid()); insert.Parameters.AddWithValue("link2", Guid.NewGuid());
    insert.Parameters.AddWithValue("emotion1", firstEmotion); insert.Parameters.AddWithValue("emotion2", secondEmotion);
    await insert.ExecuteNonQueryAsync();
}
try
{
    await MustRejectCutover("pending legacy rows");
    await Run("backfill", "1");
    Assert((long)(await new NpgsqlCommand("SELECT count(*) FROM \"EmotionEntries\" WHERE \"EncryptedEmotions\" IS NULL", connection).ExecuteScalarAsync())! == 2, "partial run");
    await Run("backfill");
    var before = await Ciphertext();
    await Run("backfill");
    Assert(before == await Ciphertext(), "restart must skip encrypted entries");
    await MustRejectCutover("missing verification receipt");
    await Run("verify");
    await using (var change = new NpgsqlCommand("UPDATE \"EmotionEntries\" SET \"UpdatedAt\" = \"UpdatedAt\" + interval '1 second' WHERE \"Id\" = @id", connection))
    { change.Parameters.AddWithValue("id", ids[0]); await change.ExecuteNonQueryAsync(); }
    await MustRejectCutover("stale verification receipt");
    await new NpgsqlCommand("UPDATE \"EmotionEntries\" SET \"UpdatedAt\" = '2025-01-03Z'", connection).ExecuteNonQueryAsync();
    await new NpgsqlCommand("UPDATE \"EmotionEntries\" SET \"Comment\" = 'mismatch' WHERE \"Id\" = '00000000-0000-0000-0000-000000000003'", connection).ExecuteNonQueryAsync();
    await Run("verify", expected: 1);
    await using (var restore = new NpgsqlCommand("UPDATE \"EmotionEntries\" SET \"Comment\" = @note WHERE \"Id\" = @id", connection))
    { restore.Parameters.AddWithValue("note", notes[2]!); restore.Parameters.AddWithValue("id", ids[2]); await restore.ExecuteNonQueryAsync(); }
    await Run("verify");
    Assert((long)(await new NpgsqlCommand("SELECT count(*) FROM \"EmotionEntries\" WHERE \"OccurredAt\" = '2025-01-01Z' AND \"CreatedAt\" = '2025-01-02Z' AND \"UpdatedAt\" = '2025-01-03Z'", connection).ExecuteScalarAsync())! == 3, "all dates preserved");
    await db.Database.MigrateAsync();
    Assert((long)(await new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_name = 'EmotionEntries' AND column_name = 'Comment'", connection).ExecuteScalarAsync())! == 0, "plaintext column removed");
    Assert(await new NpgsqlCommand("SELECT to_regclass('\"EmotionEntryEmotions\"')::text", connection).ExecuteScalarAsync() is DBNull, "links table removed");
    await Run("verify");

    // Real journal controllers over HTTP, with test-only authentication on loopback.
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:55440");
    builder.Services.AddControllers().AddApplicationPart(typeof(HeartLog.Api.Controllers.EmotionEntriesController).Assembly);
    var sqlCounter = new ListSqlCounter();
    builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString).AddInterceptors(sqlCounter));
    builder.Services.AddSingleton(new EntryEncryption(key));
    var currentUser = new TestCurrentUser(owner);
    builder.Services.AddSingleton<ICurrentUserService>(currentUser);
    builder.Services.AddScoped<IEmotionEntriesRepository, EmotionEntriesRepository>();
    builder.Services.AddScoped<IEmotionsRepository, EmotionsRepository>();
    builder.Services.AddScoped<IEmotionEntryService, EmotionEntryService>();
    builder.Services.AddAuthentication("local-test").AddScheme<AuthenticationSchemeOptions, TestAuth>("local-test", _ => { });
    builder.Services.AddAuthorization();
    await using var app = builder.Build();
    app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
    await app.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:55440") };
    sqlCounter.Count = 0;
    var get = await client.GetAsync("/api/emotion-entries");
    get.EnsureSuccessStatusCode();
    Assert(sqlCounter.Count == 2, "list must issue exactly one fetch and one emotion lookup for multiple entries");
    var body = System.Text.Json.JsonDocument.Parse(await get.Content.ReadAsStringAsync());
    var entries = body.RootElement.GetProperty("data");
    foreach (var entry in entries.EnumerateArray())
    {
        var index = Array.IndexOf(ids, entry.GetProperty("entryId").GetGuid());
        Assert(entry.GetProperty("comment").GetString() == notes[index], "API note preservation");
        var selections = entry.GetProperty("selectedEmotions").EnumerateArray().ToArray();
        Assert(selections.Length == 2, "API selections preservation");
        Assert(selections.Single(e => e.GetProperty("emotionKey").GetString() == "migration-first").GetProperty("isPrimary").GetBoolean(), "primary flag preserved");
        Assert(!selections.Single(e => e.GetProperty("emotionKey").GetString() == "migration-second").GetProperty("isPrimary").GetBoolean(), "secondary flag preserved");
    }
    foreach (var id in ids)
    {
        var put = await client.PutAsJsonAsync($"/api/emotion-entries/{id}", new { emotionKeys = new[] { "migration-second" }, primaryEmotionKey = "migration-second", comment = "edited through API" });
        put.EnsureSuccessStatusCode();
    }
    var afterEdit = await client.GetStringAsync("/api/emotion-entries");
    Assert(System.Text.Json.JsonDocument.Parse(afterEdit).RootElement.GetProperty("data").EnumerateArray().All(e => e.GetProperty("comment").GetString() == "edited through API" && e.GetProperty("selectedEmotions").GetArrayLength() == 1), "API edits persisted");
    currentUser.UserId = Guid.NewGuid();
    sqlCounter.Count = 0;
    var empty = await client.GetStringAsync("/api/emotion-entries");
    Assert(System.Text.Json.JsonDocument.Parse(empty).RootElement.GetProperty("data").GetArrayLength() == 0, "empty account list");
    Assert(sqlCounter.Count == 1, "empty list must skip emotion lookup");
    await app.StopAsync();
    Console.WriteLine("verified_fixture_entries_count=3 verified_list_sql_count=2 verified_empty_list_sql_count=1");
}
finally { File.Delete(envPath); }

async Task Run(string mode, string? limit = null, int expected = 0)
{
    var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(root, "tools/HeartLog.EntryMigration/bin/Debug/net9.0/HeartLog.EntryMigration.dll"));
    start.ArgumentList.Add(mode); start.ArgumentList.Add(envPath);
    if (limit is not null) start.ArgumentList.Add(limit);
    using var process = Process.Start(start)!;
    var output = await process.StandardOutput.ReadToEndAsync();
    var error = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    Assert(process.ExitCode == expected, $"{mode}: {output} {error}");
}
async Task MustRejectCutover(string reason)
{
    try { await db.Database.MigrateAsync(); }
    catch (PostgresException ex) when (ex.SqlState == "P0001") { return; }
    throw new Exception($"Cutover should reject {reason}");
}
async Task<string> Ciphertext() => (string)(await new NpgsqlCommand("SELECT string_agg(\"EncryptedEmotions\"::text, '' ORDER BY \"Id\") FROM \"EmotionEntries\"", connection).ExecuteScalarAsync())!;
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
sealed class TestCurrentUser(Guid id) : ICurrentUserService
{
    public Guid UserId { get; set; } = id;
    public Task<User> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => Task.FromResult(new User { Id = UserId });
}
sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "local-migration-check") }, Scheme.Name)), Scheme.Name)));
}

sealed class ListSqlCounter : DbCommandInterceptor
{
    public int Count;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Count);
        return ValueTask.FromResult(result);
    }
}
