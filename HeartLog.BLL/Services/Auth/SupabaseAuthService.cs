using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Auth;
using Microsoft.Extensions.Options;
using Supabase;
using Supabase.Gotrue.Exceptions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace HeartLog.BLL.Services.Auth;

public class SupabaseAuthService : IExternalAuthService
{
    private readonly SupabaseSettings _settings;

    public SupabaseAuthService(IOptions<SupabaseSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task TestConnectionAsync()
    {
        await CreateClientAsync();
    }

    public async Task<ExternalAuthRegistrationResult> RegisterAsync(string email, string password)
    {
        try
        {
            var client = await CreateClientAsync();
            var session = await client.Auth.SignUp(email, password);

            return new ExternalAuthRegistrationResult
            {
                Email = string.IsNullOrWhiteSpace(session?.User?.Email)
                    ? email
                    : session.User.Email
            };
        }
        catch (ExternalAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ExternalAuthException("Supabase registration failed.", ex);
        }
    }

    public async Task<ExternalAuthEmailConfirmationResult> ConfirmEmailAsync(string tokenHash, string type)
    {
        if (!IsSupportedEmailConfirmationType(type))
        {
            throw new EmailConfirmationException(
                EmailConfirmationFailureReason.Invalid,
                "Unsupported email confirmation type.");
        }

        try
        {
            var client = await CreateClientAsync();
            var session = await client.Auth.VerifyTokenHash(
                tokenHash,
                Supabase.Gotrue.Constants.EmailOtpType.Email);

            if (session?.User is null)
            {
                throw new EmailConfirmationException(
                    EmailConfirmationFailureReason.Invalid,
                    "Supabase email confirmation did not return a user.");
            }

            if (string.IsNullOrWhiteSpace(session.User.Email))
            {
                throw new EmailConfirmationException(
                    EmailConfirmationFailureReason.Invalid,
                    "Supabase email confirmation did not return a user email.");
            }

            return new ExternalAuthEmailConfirmationResult
            {
                Email = session.User.Email
            };
        }
        catch (ExternalAuthException)
        {
            throw;
        }
        catch (GotrueException ex)
        {
            throw new EmailConfirmationException(
                GetEmailConfirmationFailureReason(ex),
                "Supabase email confirmation failed.",
                ex);
        }
        catch (Exception ex)
        {
            throw new EmailConfirmationException(
                EmailConfirmationFailureReason.Invalid,
                "Supabase email confirmation failed.",
                ex);
        }
    }

    private static EmailConfirmationFailureReason GetEmailConfirmationFailureReason(GotrueException exception)
    {
        var details = string.Join(
            ' ',
            exception.Message,
            exception.Content ?? string.Empty,
            exception.Reason.ToString());

        return details.Contains("expired", StringComparison.OrdinalIgnoreCase)
            ? EmailConfirmationFailureReason.Expired
            : EmailConfirmationFailureReason.Invalid;
    }

    public async Task ResendConfirmationAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ExternalAuthException("Email is required.");
        }

        try
        {
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_settings.ProjectUrl.TrimEnd('/')}/auth/v1/resend")
            {
                Content = JsonContent.Create(new ResendConfirmationRequest(
                    Type: "signup",
                    Email: email))
            };

            request.Headers.Add("apikey", _settings.PublishableKey);
            request.Headers.Add("Authorization", $"Bearer {_settings.PublishableKey}");

            using var response = await httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 500)
            {
                throw new ExternalAuthException($"Supabase resend confirmation failed with status code {(int)response.StatusCode}.");
            }
        }
        catch (ExternalAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ExternalAuthException("Supabase resend confirmation failed.", ex);
        }
    }

    public async Task<ExternalAuthSession> LoginAsync(string email, string password)
    {
        try
        {
            var client = await CreateClientAsync();
            var session = await client.Auth.SignIn(email, password);

            return ToExternalAuthSession(session, "login");
        }
        catch (GotrueException ex)
        {
            if (ex.Reason is FailureHint.Reason.UserBadLogin
                or FailureHint.Reason.UserBadPassword
                or FailureHint.Reason.UserBadEmailAddress)
            {
                throw new UnauthorizedAccessException("Invalid email or password.", ex);
            }

            throw new ExternalAuthException("Supabase login failed.", ex);
        }
        catch (ExternalAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ExternalAuthException("Supabase login failed.", ex);
        }
    }

    public async Task<ExternalAuthSession> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        try
        {
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_settings.ProjectUrl.TrimEnd('/')}/auth/v1/token?grant_type=refresh_token")
            {
                Content = JsonContent.Create(new RefreshTokenRequest(refreshToken))
            };

            request.Headers.Add("apikey", _settings.PublishableKey);

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException("Invalid refresh token.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalAuthException($"Supabase refresh failed with status code {(int)response.StatusCode}.");
            }

            var session = await response.Content.ReadFromJsonAsync<SupabaseTokenResponse>();
            return ToExternalAuthSession(session);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (ExternalAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ExternalAuthException("Supabase refresh failed.", ex);
        }
    }

    private async Task<Client> CreateClientAsync()
    {
        var client = new Client(_settings.ProjectUrl, _settings.PublishableKey);
        await client.InitializeAsync();

        return client;
    }

    private static ExternalAuthSession ToExternalAuthSession(Supabase.Gotrue.Session? session, string operation)
    {
        if (session is null)
        {
            throw new ExternalAuthException($"Supabase {operation} did not return a session.");
        }

        if (session.User is null)
        {
            throw new ExternalAuthException($"Supabase {operation} did not return a user.");
        }

        if (!Guid.TryParse(session.User.Id, out var providerUserId))
        {
            throw new ExternalAuthException($"Supabase {operation} returned an invalid user id.");
        }

        if (string.IsNullOrWhiteSpace(session.User.Email))
        {
            throw new ExternalAuthException($"Supabase {operation} did not return a user email.");
        }

        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            throw new ExternalAuthException($"Supabase {operation} did not return an access token.");
        }

        return new ExternalAuthSession
        {
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            ExpiresAt = session.ExpiresAt(),
            User = new ExternalAuthUser
            {
                ProviderUserId = providerUserId,
                Email = session.User.Email
            }
        };
    }

    private static ExternalAuthSession ToExternalAuthSession(SupabaseTokenResponse? session)
    {
        if (session is null)
        {
            throw new ExternalAuthException("Supabase refresh did not return a session.");
        }

        if (session.User is null)
        {
            throw new ExternalAuthException("Supabase refresh did not return a user.");
        }

        if (!Guid.TryParse(session.User.Id, out var providerUserId))
        {
            throw new ExternalAuthException("Supabase refresh returned an invalid user id.");
        }

        if (string.IsNullOrWhiteSpace(session.User.Email))
        {
            throw new ExternalAuthException("Supabase refresh did not return a user email.");
        }

        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            throw new ExternalAuthException("Supabase refresh did not return an access token.");
        }

        return new ExternalAuthSession
        {
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            ExpiresAt = ToExpiresAt(session.ExpiresAt, session.ExpiresIn),
            User = new ExternalAuthUser
            {
                ProviderUserId = providerUserId,
                Email = session.User.Email
            }
        };
    }

    private static DateTime? ToExpiresAt(long? expiresAt, int? expiresIn)
    {
        if (expiresAt is not null)
        {
            return DateTimeOffset.FromUnixTimeSeconds(expiresAt.Value).UtcDateTime;
        }

        if (expiresIn is not null)
        {
            return DateTime.UtcNow.AddSeconds(expiresIn.Value);
        }

        return null;
    }

    private static bool IsSupportedEmailConfirmationType(string type)
    {
        return string.Equals(type, "email", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RefreshTokenRequest(
        [property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record ResendConfirmationRequest(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("email")] string Email);

    private sealed class SupabaseTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("expires_at")]
        public long? ExpiresAt { get; set; }

        [JsonPropertyName("user")]
        public SupabaseTokenUser? User { get; set; }
    }

    private sealed class SupabaseTokenUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }
}
