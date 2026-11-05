namespace HeartLog.DAL.Models;

public class EmailOutboxMessage
{
    public const string LegacyAccountDeletedTemplate = "account-deleted-v1";
    public const string AccountDeletedTemplate = "account-deleted-v2";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Recipient { get; set; } = string.Empty;
    public string Template { get; set; } = AccountDeletedTemplate;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LockedUntil { get; set; }
}
