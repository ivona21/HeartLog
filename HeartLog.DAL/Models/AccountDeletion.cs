namespace HeartLog.DAL.Models;

// Intentionally independent of Users so a failed cleanup cannot lose its retry record.
public class AccountDeletion
{
    public Guid SupabaseUserId { get; set; }
    public Guid UserId { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int Attempts { get; set; }
}
