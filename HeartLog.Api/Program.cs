using System.Text;
using HeartLog.Api.DTOs;
using HeartLog.Api.JwtToken;
using HeartLog.BLL;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Data;
using HeartLog.DAL.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer; // for ApplicationDbContext
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models; // for UseNpgsql

DotNetEnv.Env.Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
var jwtSettings = builder.Configuration.GetSection("JwtSettings");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddControllers();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IItemService, ItemService>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<ItemsRepository>();
// Add DbContext to the DI container
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(Environment.GetEnvironmentVariable("DATABASE_URL")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "HeartLog API", Version = "v1" });

    // JWT Authentication setup
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtSettings = builder.Configuration.GetSection("JwtSettings");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWT_SECRET_KEY")))
        };
    });

// 1. Define a CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowV0Frontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",      // localhost 01
                "http://localhost:5001", // localhost 02
                "https://v0-heart-log-calm-fee5obmca-ivonas-projects-17db0703.vercel.app", // deployed V0 frontend,
                "https://heart-log-calm.vercel.app", // deployed on v0
                "https://71eb8564-b79f-4920-af09-9cd6317e6a88-00-1cgns8l1wsjzo.picard.replit.dev", // replit 01
                "https://replit.com/@coffeebreak5551/HeartLogCalm" // replit 02
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // if using cookies/auth headers
    });
});


// Add services
builder.Services.AddControllers();
builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtTokenGenerator>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(80); // <--- This forces the app to listen on port 80
});

var app = builder.Build();
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
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            Message = "Internal server error",
            Errors = null
        };

        await context.Response.WriteAsJsonAsync(errorResponse);
    });
});
app.MapGet("/ping", () => Results.Ok("pong"));
app.MapGet("/", () => "HeartLog API is running 🚀");
app.MapControllers();
app.Run();