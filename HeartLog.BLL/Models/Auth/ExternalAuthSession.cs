namespace HeartLog.BLL.Models.Auth;

public class ExternalAuthSession
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public ExternalAuthUser User { get; set; } = new();
}
