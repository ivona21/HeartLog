using System.Net;
using System.Reflection;
using System.Security.Claims;
using HeartLog.Api.Controllers;
using HeartLog.Api.DTOs;
using HeartLog.Api.OpenApi;
using HeartLog.BLL;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Auth;
using HeartLog.BLL.Services.Auth;
using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
var providerId = Guid.NewGuid();
var user = new User { SupabaseUserId = providerId, Email = "test@example.invalid" };
var session = new ExternalAuthSession { User = new ExternalAuthUser { ProviderUserId = providerId, Email = user.Email }, AccessToken = "access" };
var repo = DispatchProxy.Create<IUserRepository, Stub>();
var auth = DispatchProxy.Create<IExternalAuthService, Stub>();
var repository = (Stub)(object)repo;
var external = (Stub)(object)auth;
var events = new List<string>();
var wrongPassword = false;
var providerFailure = false;
var staleSession = false;
var localFailure = false;
AccountDeletion? task = null;
repository.Handler = async (method, args) =>
{
    switch (method.Name)
    {
        case "WithProviderLockAsync":
            Check((Guid)args[0]! == providerId, "Wrong lock identity");
            await ((Func<Task>)args[1]!)(); return null;
        case "GetBySupabaseUserIdAsync": return user;
        case "RequestDeletionAsync":
            events.Add("record"); user.DeletionRequestedAt = DateTime.UtcNow;
            task = new AccountDeletion { UserId = user.Id, SupabaseUserId = providerId }; return null;
        case "GetDueDeletionsAsync": return new List<Guid> { providerId };
        case "GetDeletionAsync": return task;
        case "CompleteDeletionAsync":
            if (localFailure) throw new InvalidOperationException("Database unavailable");
            events.Add("local-delete"); task = null; return null;
        case "RecordDeletionFailureAsync": events.Add("retry"); return null;
        default: throw new Exception($"Unexpected repository call: {method.Name}");
    }
};
external.Handler = (method, args) =>
{
    switch (method.Name)
    {
        case "LoginAsync":
            if (wrongPassword) throw new ExternalAuthenticationException(ExternalAuthenticationFailureReason.InvalidCredentials, "wrong password");
            return Task.FromResult<object?>(session);
        case "RefreshAsync": return Task.FromResult<object?>(session);
        case "ValidateSessionAsync":
            if (staleSession) throw new UnauthorizedAccessException();
            return Task.FromResult<object?>(null);
        case "DeleteUserAsync":
            if (providerFailure) throw new ExternalAuthException("unavailable");
            events.Add("provider-delete"); return Task.FromResult<object?>(null);
        default: throw new Exception($"Unexpected auth call: {method.Name}");
    }
};
var service = new UserService(repo, NullLogger<UserService>.Instance, auth);
wrongPassword = true;
await Throws<CurrentPasswordIncorrectException>(() => service.RequestAccountDeletionAsync(providerId, "wrong"));
Check(task is null, "Wrong password queued deletion");
wrongPassword = false;
session.User.ProviderUserId = Guid.NewGuid();
await Throws<UnauthorizedAccessException>(() => service.RequestAccountDeletionAsync(providerId, "password"));
Check(task is null, "Mismatched identity queued deletion");
session.User.ProviderUserId = providerId;
await service.RequestAccountDeletionAsync(providerId, "password");
Check(task is not null && user.DeletionRequestedAt is not null, "Missing durable task or access marker");
wrongPassword = true;
await service.RequestAccountDeletionAsync(providerId, "ignored-on-duplicate");
Check(events.Count(e => e == "record") == 1, "Duplicate created another task");
wrongPassword = false;
await Throws<UnauthorizedAccessException>(() => service.LoginUserAsync(user.Email, "password"));
await Throws<UnauthorizedAccessException>(() => service.RefreshSessionAsync("refresh"));
var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", providerId.ToString()) }, "test"));
await Throws<UnauthorizedAccessException>(() => new CurrentUserService(repo, NullLogger<CurrentUserService>.Instance).GetCurrentUserAsync(principal));
providerFailure = true;
await service.ProcessAccountDeletionsAsync(default);
Check(task is not null && events.Contains("retry") && !events.Contains("local-delete"), "Provider failure lost task or deleted local data");
providerFailure = false;
localFailure = true;
await service.ProcessAccountDeletionsAsync(default);
Check(task is not null, "Local failure lost durable task");
localFailure = false;
await service.ProcessAccountDeletionsAsync(default);
Check(task is null && events[^2] == "provider-delete" && events[^1] == "local-delete", "Deletion order or completion incorrect");
staleSession = true;
await Throws<UnauthorizedAccessException>(() => service.LoginUserAsync(user.Email, "password"));

// Exercise actual provider requests without contacting Supabase.
foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable, HttpStatusCode.Unauthorized })
{
    var handler = new Handler(request =>
    {
        Check(request.Method == HttpMethod.Delete && request.RequestUri!.AbsolutePath == $"/auth/v1/admin/users/{providerId}", "Wrong admin request");
        Check(request.Headers.GetValues("apikey").Single() == "sb_secret_test", "Missing admin key");
        Check(request.Headers.Authorization is null, "Secret API key used as JWT");
        return new HttpResponseMessage(status);
    });
    using var client = new HttpClient(handler);
    var adapter = new SupabaseAuthService(Options.Create(new SupabaseSettings
    {
        ProjectUrl = "https://example.invalid", SecretKey = "sb_secret_test"
    }), new ClientFactory(client));
    if (status is HttpStatusCode.OK or HttpStatusCode.NotFound) await adapter.DeleteUserAsync(providerId);
    else await Throws<ExternalAuthException>(() => adapter.DeleteUserAsync(providerId));
}

// Validate relational cascades and migration SQL without touching an application database.
using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
Check(db.Model.FindEntityType(typeof(EmotionEntry))!.GetForeignKeys().Single().DeleteBehavior == DeleteBehavior.Cascade, "Missing user cascade");
Check(db.Model.FindEntityType(typeof(EmotionEntryEmotion))!.GetForeignKeys()
    .Single(f => f.PrincipalEntityType.ClrType == typeof(EmotionEntry)).DeleteBehavior == DeleteBehavior.Cascade, "Missing selection cascade");
Check(!db.Database.HasPendingModelChanges(), "Migration snapshot differs from runtime model");
var sql = db.GetService<IMigrator>().GenerateScript();
Check(sql.Contains("CREATE TABLE \"AccountDeletions\"") && sql.Contains("ADD \"DeletionRequestedAt\""), "Missing deletion migration SQL");

var builder = WebApplication.CreateBuilder();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "HeartLog API", Version = "v1" });
    c.EnableAnnotations(); c.SupportNonNullableReferenceTypes();
    c.SchemaFilter<ApiErrorCodeSchemaFilter>();
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization", In = ParameterLocation.Header, Type = SecuritySchemeType.Http, Scheme = "bearer"
    });
    c.OperationFilter<AuthorizeOperationFilter>();
});
await using var app = builder.Build();
var controller = new AuthController(service, null!, app.Environment, app.Configuration)
{
    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
};
var response = await controller.DeleteCurrentUser(new DeleteAccountRequestDto { CurrentPassword = "password" }, default);
Check(response.Result is AcceptedResult, "Delete did not return 202");
var cookies = controller.Response.Headers.SetCookie.ToString();
Check(cookies.Contains("heartlog_refresh_token=") && cookies.Contains("heartlog_password_reset_token="), "Authentication cookies not cleared");
var swagger = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
var operation = swagger.Paths["/api/auth/me"].Operations[OperationType.Delete];
Check(operation.Responses.ContainsKey("202") && operation.Security.Count > 0 && operation.RequestBody.Required, "Incorrect delete API contract");
if (args.Length == 1)
{
    using var writer = new StringWriter();
    swagger.SerializeAsV3(new OpenApiJsonWriter(writer));
    await File.WriteAllTextAsync(args[0], writer.ToString());
}
Console.WriteLine("PASS: password and ownership verification, durable acceptance, duplicate requests, pending access/login/refresh rejection, provider and database failure retries, deletion ordering, stale-login rejection, admin HTTP requests, cascades, migration consistency, cookies, and OpenAPI contract.");

public class Stub : DispatchProxy
{
    public Func<MethodInfo, object?[], Task<object?>> Handler = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var result = Handler(method!, args!);
        if (method!.ReturnType == typeof(Task)) return result;
        var type = method.ReturnType.GenericTypeArguments.Single();
        return typeof(Stub).GetMethod(nameof(Convert), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type).Invoke(null, new object[] { result });
    }
    private static async Task<T> Convert<T>(Task<object?> task) => (T)(await task)!;
}
public sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
public sealed class ClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
