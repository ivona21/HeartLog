using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace HeartLog.Api.Auth;

public static class PasswordResetCookie
{
    public const string Name = "heartlog_password_reset_token";
    public const string Path = "/api/auth/reset-password";

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

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
