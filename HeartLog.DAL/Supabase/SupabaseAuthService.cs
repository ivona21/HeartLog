using Supabase;

namespace HeartLog.DAL.Supabase;

using Microsoft.Extensions.Options;

public class SupabaseAuthService
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