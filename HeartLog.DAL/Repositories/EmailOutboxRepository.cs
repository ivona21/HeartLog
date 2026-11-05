using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.DAL.Repositories;

public class EmailOutboxRepository(ApplicationDbContext context) : IEmailOutboxRepository
{
    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken) =>
        context.EmailOutbox.Where(m => m.ExpiresAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<EmailOutboxMessage?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        // Row locks skip work already being claimed by another instance. The persisted lease
        // also prevents another sender claiming it after this short transaction commits.
        var candidates = await context.EmailOutbox.FromSqlInterpolated($"""
            SELECT * FROM "EmailOutbox"
            WHERE "NextAttemptAt" <= {now} AND "ExpiresAt" > {now}
              AND ("LockedUntil" IS NULL OR "LockedUntil" <= {now})
            ORDER BY "NextAttemptAt", "Id"
            LIMIT 1 FOR UPDATE SKIP LOCKED
            """).AsNoTracking().ToListAsync(cancellationToken);
        var message = candidates.SingleOrDefault();
        if (message is not null)
        {
            message.LeaseId = Guid.NewGuid();
            message.LockedUntil = now.AddMinutes(2);
            await context.EmailOutbox.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.LeaseId, message.LeaseId)
                .SetProperty(m => m.LockedUntil, message.LockedUntil), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return message;
    }

    public async Task CompleteAsync(Guid id, Guid leaseId, CancellationToken cancellationToken) =>
        await context.EmailOutbox.Where(m => m.Id == id && m.LeaseId == leaseId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task RetryAsync(EmailOutboxMessage message, DateTime nextAttemptAt, CancellationToken cancellationToken) =>
        await context.EmailOutbox.Where(m => m.Id == message.Id && m.LeaseId == message.LeaseId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                .SetProperty(m => m.LeaseId, (Guid?)null)
                .SetProperty(m => m.LockedUntil, (DateTime?)null), cancellationToken);
}
