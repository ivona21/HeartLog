using System.Text;
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

// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(options =>
//     {
//         var jwtSettings = builder.Configuration.GetSection("JwtSettings");
//         options.TokenValidationParameters = new TokenValidationParameters
//         {
//             ValidateIssuer = true,
//             ValidateAudience = true,
//             ValidateLifetime = true,
//             ValidateIssuerSigningKey = true,
//             ValidIssuer = jwtSettings["Issuer"],
//             ValidAudience = jwtSettings["Audience"],
//             IssuerSigningKey = new SymmetricSecurityKey(
//                 Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWT_SECRET_KEY")))
//         };
//     });


builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtTokenGenerator>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(80); // <--- This forces the app to listen on port 80
});

var app = builder.Build();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
        c.RoutePrefix = string.Empty; // serves Swagger UI at root "/"
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/ping", () => Results.Ok("pong"));
app.MapGet("/", () => "HeartLog API is running 🚀");
app.MapControllers();
app.Run();