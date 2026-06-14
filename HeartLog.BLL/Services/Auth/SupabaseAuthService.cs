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

    public async Task<ExternalAuthUser> RegisterAsync(string email, string password)
    {
        var client = await CreateClientAsync();
        var session = await client.Auth.SignUp(email, password);

        return ToExternalAuthUser(session);
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

    private static ExternalAuthUser ToExternalAuthUser(Supabase.Gotrue.Session? session)
    {
        if (session is null)
        {
            throw new InvalidOperationException("Supabase registration did not return a user.");
        }

        if (session.User is null)
        {
            throw new InvalidOperationException("Supabase registration did not return a user.");
        }

        if (!Guid.TryParse(session.User.Id, out var providerUserId))
        {
            throw new InvalidOperationException("Supabase registration returned an invalid user id.");
        }

        if (string.IsNullOrWhiteSpace(session.User.Email))
        {
            throw new InvalidOperationException("Supabase registration did not return a user email.");
        }

        return new ExternalAuthUser
        {
            ProviderUserId = providerUserId,
            Email = session.User.Email
        };
    }
}
