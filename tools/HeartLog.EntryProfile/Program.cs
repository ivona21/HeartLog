using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using HeartLog.Api.DTOs;
using HeartLog.BLL;
using HeartLog.BLL.Diagnostics;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Services.Encryption;
using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using HeartLog.DAL.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

// Read-only diagnostic host: real journal controller/service/repositories. The
// loopback-only identity adapter omits external authentication from these timings.
// Never log SQL, identifiers, payloads, exception messages, or plaintext hashes.
try
{
    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
    if (args.Length != 3 || args[0] is not ("before" or "after")) throw new InvalidOperationException();
    DotNetEnv.Env.Load(args[1]);
    var encryption = new EntryEncryption(Environment.GetEnvironmentVariable("EntryEncryption__Keys__v1"));
    var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"))
    {
        Options = "-c default_transaction_read_only=on",
        IncludeErrorDetail = false
    }.ConnectionString;
    var metrics = new Measurements();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
    await using var setup = new ApplicationDbContext(options);
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    Reference? reference = args[0] == "after"
        ? encryption.Decrypt<Reference>(Guid.Empty, Guid.Empty, "profile-reference", await File.ReadAllTextAsync(args[2]))
        : null;
    var owner = reference?.Owner ?? await setup.EmotionEntries.GroupBy(e => e.UserId)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstAsync();

    using var listener = new ActivityListener
    {
        ShouldListenTo = source => source.Name == EntryReadDiagnostics.SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = activity => metrics.Record(activity)
    };
    ActivitySource.AddActivityListener(listener);
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddControllers().AddApplicationPart(typeof(HeartLog.Api.Controllers.EmotionEntriesController).Assembly);
    builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connection).AddInterceptors(new CommandCounter(metrics)));
    builder.Services.AddSingleton(encryption);
    builder.Services.AddSingleton<ICurrentUserService>(new ProfileUser(owner));
    builder.Services.AddScoped<IEmotionEntriesRepository, EmotionEntriesRepository>();
    builder.Services.AddScoped<IEmotionsRepository, EmotionsRepository>();
    builder.Services.AddScoped<IEmotionEntryService, EmotionEntryService>();
    builder.Services.AddAuthentication("profile").AddScheme<AuthenticationSchemeOptions, ProfileAuth>("profile", _ => { });
    builder.Services.AddAuthorization();
    await using var app = builder.Build();
    app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(5) };
    string? response = null;
    var samples = new List<Sample>();
    for (var sample = 0; sample <= 3; sample++)
    {
        metrics.Reset();
        var watch = Stopwatch.StartNew();
        using var result = await client.GetAsync("/api/emotion-entries");
        result.EnsureSuccessStatusCode();
        var body = await result.Content.ReadAsStringAsync();
        watch.Stop();
        var entries = JsonSerializer.Deserialize<ApiResponse<List<EmotionEntryResponse>>>(body, json)!.Data!;
        if (response is not null && response != body) throw new InvalidOperationException();
        response = body;
        var measurement = new Sample(watch.Elapsed.TotalMilliseconds, metrics.FetchMs, metrics.DecryptMs, metrics.LookupMs,
            metrics.Commands, metrics.FetchCommands, metrics.LookupCommands, entries.Count);
        Console.WriteLine($"warmup_count={(sample == 0 ? 1 : 0)} sample_count={sample} entries_count={measurement.Entries} sql_count={measurement.Sql} fetch_sql_count={measurement.FetchSql} emotion_sql_count={measurement.LookupSql} fetch_ms={measurement.FetchMs:F3} decrypt_ms={measurement.DecryptMs:F3} emotion_ms={measurement.LookupMs:F3} request_ms={measurement.TotalMs:F3}");
        if (args[0] == "after" && (measurement.Sql != (entries.Count == 0 ? 1 : 2)
            || measurement.FetchSql != 1 || measurement.LookupSql != (entries.Count == 0 ? 0 : 1)))
            throw new InvalidOperationException();
        if (sample > 0) samples.Add(measurement);
    }
    if (reference is not null && reference.Response != response) throw new InvalidOperationException();

    // Independently compare every response note and selection to direct decryption
    // and the catalog. Only success counts leave this process.
    var source = await new EmotionEntriesRepository(setup).GetAllByUserIdAsync(owner);
    var catalog = await setup.Emotions.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Key);
    var actual = JsonSerializer.Deserialize<ApiResponse<List<EmotionEntryResponse>>>(response!, json)!.Data!.ToDictionary(e => e.EntryId);
    if (source.Count != actual.Count) throw new InvalidOperationException();
    var verifiedSelections = 0;
    foreach (var entry in source)
    {
        var selected = encryption.Decrypt<List<StoredEmotion>>(owner, entry.Id, "emotions", entry.EncryptedEmotions!);
        var note = entry.EncryptedNote is null ? null : encryption.Decrypt<string>(owner, entry.Id, "note", entry.EncryptedNote);
        var result = actual[entry.Id];
        if (note != result.Comment || entry.OccurredAt != result.OccurredAt || entry.CreatedAt != result.CreatedAt) throw new InvalidOperationException();
        var expectedSelections = selected.Select(e => (Key: catalog[e.EmotionId], e.IsPrimary)).OrderByDescending(e => e.IsPrimary).ThenBy(e => e.Key).ToArray();
        if (!expectedSelections.SequenceEqual(result.SelectedEmotions.Select(e => (e.EmotionKey, e.IsPrimary)))) throw new InvalidOperationException();
        verifiedSelections += selected.Count;
    }
    if (reference is null)
    {
        // Retain only an encrypted comparison artifact, never plaintext or hashes.
        await File.WriteAllTextAsync(args[2], encryption.Encrypt(Guid.Empty, Guid.Empty, "profile-reference", new Reference(owner, response!)));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(args[2], UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    else File.Delete(args[2]);
    Console.WriteLine($"verified_entries_count={source.Count} verified_selections_count={verifiedSelections} comparison_count={(reference is null ? 0 : 1)}");
    Console.WriteLine($"warm_samples_count={samples.Count} median_request_ms={Median(samples.Select(s => s.TotalMs)):F3} median_fetch_ms={Median(samples.Select(s => s.FetchMs)):F3} median_decrypt_ms={Median(samples.Select(s => s.DecryptMs)):F3} median_emotion_ms={Median(samples.Select(s => s.LookupMs)):F3}");
    await app.StopAsync();
}
catch { Console.Error.WriteLine("error_count=1"); Environment.ExitCode = 1; }

static double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
record Reference(Guid Owner, string Response);
record Sample(double TotalMs, double FetchMs, double DecryptMs, double LookupMs, int Sql, int FetchSql, int LookupSql, int Entries);
sealed class Measurements
{
    public double FetchMs, DecryptMs, LookupMs;
    public int Commands, FetchCommands, LookupCommands;
    public void Reset() { FetchMs = DecryptMs = LookupMs = 0; Commands = FetchCommands = LookupCommands = 0; }
    public void Record(Activity activity)
    {
        switch (activity.OperationName)
        {
            case "entries.fetch": FetchMs += activity.Duration.TotalMilliseconds; break;
            case "entries.decrypt": DecryptMs += activity.Duration.TotalMilliseconds; break;
            case "entries.emotions": LookupMs += activity.Duration.TotalMilliseconds; break;
        }
    }
    public void Count()
    {
        Commands++;
        for (var activity = Activity.Current; activity is not null; activity = activity.Parent)
        {
            if (activity.OperationName == "entries.fetch") { FetchCommands++; return; }
            if (activity.OperationName == "entries.emotions") { LookupCommands++; return; }
        }
    }
}
sealed class CommandCounter(Measurements metrics) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { metrics.Count(); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { metrics.Count(); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { metrics.Count(); return ValueTask.FromResult(result); }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { metrics.Count(); return result; }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { metrics.Count(); return result; }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { metrics.Count(); return result; }
}
sealed class ProfileUser(Guid owner) : ICurrentUserService
{
    public Task<User> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => Task.FromResult(new User { Id = owner });
}
sealed class ProfileAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "profile") }, Scheme.Name)), Scheme.Name)));
}
