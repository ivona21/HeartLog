using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Email;
using HeartLog.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeartLog.BLL.Services.Email;

public class EmailOutboxService(IEmailOutboxRepository repository, IEmailService emailService,
    IOptions<EmailSettings> options, ILogger<EmailOutboxService> logger) : IEmailOutboxService
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        // Purge retained addresses even when sending is disabled.
        var expired = await repository.DeleteExpiredAsync(cancellationToken);
        if (expired > 0) logger.LogWarning("Removed {Count} expired email notifications.", expired);
        if (!options.Value.Enabled) return;

        for (var i = 0; i < 20; i++)
        {
            var message = await repository.ClaimNextAsync(cancellationToken);
            if (message is null) break;
            try
            {
                await emailService.SendAsync(new EmailMessage(message.Id, message.Recipient,
                    message.Template, message.CreatedAt), cancellationToken);
                await repository.CompleteAsync(message.Id, message.LeaseId!.Value, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (EmailDeliveryException ex) when (ex.Permanent)
            {
                await repository.CompleteAsync(message.Id, message.LeaseId!.Value, cancellationToken);
                logger.LogWarning("Email notification {MessageId} was permanently rejected and removed.", message.Id);
            }
            catch (Exception ex)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(message.Attempts, 7))));
                if (ex is EmailDeliveryException { RetryAfter: { } retry } && retry > delay) delay = retry;
                var next = DateTime.UtcNow.Add(delay > TimeSpan.FromDays(7) ? TimeSpan.FromDays(7) : delay);
                await repository.RetryAsync(message, next, cancellationToken);
                // Log identifiers only, never recipients, provider bodies, or HTTP exception details.
                logger.LogWarning("Email notification {MessageId} failed; retry scheduled.", message.Id);
            }
        }
    }
}
