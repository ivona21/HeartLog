namespace HeartLog.Api.DTOs;

public class AuthRegistrationResponseDto
{
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmationRequired { get; set; }
}
