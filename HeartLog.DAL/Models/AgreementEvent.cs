namespace HeartLog.DAL.Models;

public class AgreementEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string AgreementKey { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public Guid SubmissionId { get; set; }
}
