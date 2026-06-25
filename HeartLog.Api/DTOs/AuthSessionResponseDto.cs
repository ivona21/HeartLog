namespace HeartLog.Api.DTOs;

public class AuthSessionResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public string Email { get; set; } = string.Empty;
}
