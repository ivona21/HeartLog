using HeartLog.Api.DTOs;
using HeartLog.BLL;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Data;
using HeartLog.DAL.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using HeartLog.Api.Middleware;
using HeartLog.Api.OpenApi;
using HeartLog.BLL.Models.Auth;
using HeartLog.BLL.Services.Auth;
using HeartLog.DAL.Seeding;
using HeartLog.DAL.Interfaces;
using Microsoft.AspNetCore.Mvc;

DotNetEnv.Env.Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
var supabaseSettings = builder.Configuration.GetSection("Supabase");
var supabaseProjectUrl = supabaseSettings["ProjectUrl"]
                         ?? throw new InvalidOperationException("Supabase project URL is missing.");
var supabaseIssuer = $"{supabaseProjectUrl.TrimEnd('/')}/auth/v1";
var supabaseJwksUri = $"{supabaseIssuer}/.well-known/jwks.json";
var supabaseAudience = supabaseSettings["JwtAudience"] ?? "authenticated";

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            var errorResponse = new ErrorResponse
            {
                Message = "Validation failed",
                Errors = errors
            };

            return new BadRequestObjectResult(errorResponse);
        };
    });
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IItemService, ItemService>();
builder.Services.AddScoped<IEmotionService, EmotionService>();
builder.Services.AddScoped<IEmotionEntryService, EmotionEntryService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ItemsRepository>();
builder.Services.AddScoped<IEmotionsRepository, EmotionsRepository>();
builder.Services.AddScoped<IEmotionEntriesRepository, EmotionEntriesRepository>();
builder.Services.AddScoped<EmotionSeeder>();
builder.Services.AddScoped<IExternalAuthService, SupabaseAuthService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "HeartLog API", Version = "v1" });
    c.EnableAnnotations();
    c.SupportNonNullableReferenceTypes();

    var xmlCommentsPath = Path.Combine(AppContext.BaseDirectory, "HeartLog.Api.xml");
    if (File.Exists(xmlCommentsPath))
    {
        c.IncludeXmlComments(xmlCommentsPath);
    }

    // JWT Authentication setup
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    c.OperationFilter<AuthorizeOperationFilter>();
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            supabaseJwksUri,
            new JsonWebKeySetConfigurationRetriever());
        options.AutomaticRefreshInterval = TimeSpan.FromMinutes(10);
        options.RefreshInterval = TimeSpan.FromMinutes(5);
        options.RefreshOnIssuerKeyNotFound = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = supabaseIssuer,
            ValidateAudience = true,
            ValidAudience = supabaseAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = ["ES256"]
        };
    });

var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .GetChildren()
    .Select(origin => origin.Value)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin!)
    .ToArray();

if (allowedCorsOrigins.Length == 0)
{
    allowedCorsOrigins =
    [
        "http://localhost:5173",
        "http://localhost:5001",
        "https://v0-heart-log-calm-fee5obmca-ivonas-projects-17db0703.vercel.app",
        "https://heart-log-calm.vercel.app",
        "https://71eb8564-b79f-4920-af09-9cd6317e6a88-00-1cgns8l1wsjzo.picard.replit.dev",
        "https://replit.com"
    ];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowV0Frontend", policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});


// Add services
builder.Services.AddAuthorization();

builder.Services.Configure<SupabaseSettings>(builder.Configuration.GetSection("Supabase"));

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(80); // <--- This forces the app to listen on port 80
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var emotionSeeder = scope.ServiceProvider.GetRequiredService<EmotionSeeder>();

    await dbContext.Database.MigrateAsync();
    await emotionSeeder.SeedAsync();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors("AllowV0Frontend");
// Configure the HTTP request pipeline.
// if (app.Environment.IsDevelopment())
// {
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
        c.RoutePrefix = string.Empty; // serves Swagger UI at root "/"
    });
// }

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/ping", () => Results.Ok("pong"));
app.MapGet("/", () => "HeartLog API is running 🚀");
if (app.Environment.IsDevelopment())
{
    app.MapGet("/test-supabase", async (IExternalAuthService authService) =>
    {
        await authService.TestConnectionAsync();
        return Results.Ok("Supabase connection successful!");
    });
}

app.MapControllers();
app.Run();

public sealed class JsonWebKeySetConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        var json = await retriever.GetDocumentAsync(address, cancel);
        var keySet = new JsonWebKeySet(json);
        var configuration = new OpenIdConnectConfiguration();

        foreach (var key in keySet.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
