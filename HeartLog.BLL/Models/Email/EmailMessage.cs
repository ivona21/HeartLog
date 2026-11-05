namespace HeartLog.BLL.Models.Email;

public record EmailMessage(Guid Id, string Recipient, string Template, DateTime CompletedAt);
