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
        var client = new Client(_settings.ProjectUrl, _settings.SecretKey);
        await client.InitializeAsync();
    }
}
