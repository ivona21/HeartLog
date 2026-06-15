using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Auth;
using Microsoft.Extensions.Options;
using Supabase;

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

    public async Task<ExternalAuthSession> RegisterAsync(string email, string password)
    {
        try
        {
            var client = await CreateClientAsync();
            var session = await client.Auth.SignUp(email, password);

            return ToExternalAuthSession(session);
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

    public Task<ExternalAuthSession> LoginAsync(string email, string password)
    {
        throw new NotImplementedException();
    }

    private async Task<Client> CreateClientAsync()
    {
        var client = new Client(_settings.ProjectUrl, _settings.PublishableKey);
        await client.InitializeAsync();

        return client;
    }

    private static ExternalAuthSession ToExternalAuthSession(Supabase.Gotrue.Session? session)
    {
        if (session is null)
        {
            throw new ExternalAuthException("Supabase registration did not return a session.");
        }

        if (session.User is null)
        {
            throw new ExternalAuthException("Supabase registration did not return a user.");
        }

        if (!Guid.TryParse(session.User.Id, out var providerUserId))
        {
            throw new ExternalAuthException("Supabase registration returned an invalid user id.");
        }

        if (string.IsNullOrWhiteSpace(session.User.Email))
        {
            throw new ExternalAuthException("Supabase registration did not return a user email.");
        }

        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            throw new ExternalAuthException("Supabase registration did not return an access token. Confirm email may be enabled.");
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
}
