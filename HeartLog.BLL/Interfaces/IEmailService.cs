using HeartLog.BLL.Models.Email;

namespace HeartLog.BLL.Interfaces;

public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
