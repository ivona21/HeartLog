namespace HeartLog.BLL.Models.Auth;

public class ExternalAuthUser
{
    public Guid ProviderUserId { get; set; }
    public string Email { get; set; } = string.Empty;
}
