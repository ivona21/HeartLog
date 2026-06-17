namespace HeartLog.BLL.Models.Auth;

public class SupabaseSettings
{
    public string ProjectUrl { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string JwtAudience { get; set; } = "authenticated";
}
