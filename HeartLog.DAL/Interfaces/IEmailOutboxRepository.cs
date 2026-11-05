using HeartLog.DAL.Models;

namespace HeartLog.DAL.Interfaces;

public interface IEmailOutboxRepository
{
    Task AddAsync(EmailOutboxMessage message, CancellationToken cancellationToken);
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);
    Task<EmailOutboxMessage?> ClaimNextAsync(CancellationToken cancellationToken);
    Task CompleteAsync(Guid id, Guid leaseId, CancellationToken cancellationToken);
    Task RetryAsync(EmailOutboxMessage message, DateTime nextAttemptAt, CancellationToken cancellationToken);
}
