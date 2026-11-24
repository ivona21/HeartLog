namespace HeartLog.Api.DTOs;

public class UserMeResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string Email { get; set; } = string.Empty;
    public JournalConsentStatusDto JournalConsent { get; set; } = new();
}

public sealed class JournalConsentStatusDto
{
    public string Status { get; set; } = "notGranted";
}
