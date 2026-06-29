using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace HeartLog.Api.Auth;

public static class RefreshTokenCookie
{
    public const string Name = "heartlog_refresh_token";
    public const string Path = "/api/auth";

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    public static CookieOptions CreateOptions(IHostEnvironment environment)
    {
        return CreateBaseOptions(environment, DateTimeOffset.UtcNow.Add(DefaultLifetime));
    }

    public static CookieOptions CreateDeleteOptions(IHostEnvironment environment)
    {
        var options = CreateBaseOptions(environment, DateTimeOffset.UnixEpoch);
        options.MaxAge = TimeSpan.Zero;

        return options;
    }

    private static CookieOptions CreateBaseOptions(IHostEnvironment environment, DateTimeOffset expires)
    {
        var isDevelopment = environment.IsDevelopment();

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDevelopment,
            SameSite = isDevelopment ? SameSiteMode.Lax : SameSiteMode.None,
            Path = Path,
            Expires = expires
        };
    }
}
