namespace HeartLog.BLL.Models.Auth;

public class ExternalAuthRegistrationResult
{
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmationRequired { get; set; } = true;
}
