namespace HeartLog.DAL.Models;

public class RegistrationIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = string.Empty;
    public Guid? ProviderUserId { get; set; }
    public string TermsVersion { get; set; } = string.Empty;
    public string PrivacyPolicyVersion { get; set; } = string.Empty;
    public string JournalProcessingVersion { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
