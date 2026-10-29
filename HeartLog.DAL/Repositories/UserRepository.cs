using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.DAL.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;
    
    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }
    
    // Transaction-scoped PostgreSQL lock coordinates provisioning and deletion across instances.
    public async Task WithProviderLockAsync(Guid providerId, Func<Task> action, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var key = providerId.ToString();
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        // A caller may have resolved a tracked user before waiting for this lock.
        _context.ChangeTracker.Clear();
        await action();
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RequestDeletionAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.DeletionRequestedAt is not null) return;
        var now = DateTime.UtcNow;
        user.DeletionRequestedAt = now;
        _context.AccountDeletions.Add(new AccountDeletion
        {
            SupabaseUserId = user.SupabaseUserId!.Value, UserId = user.Id,
            RequestedAt = now, NextAttemptAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetDueDeletionsAsync(CancellationToken cancellationToken) =>
        await _context.AccountDeletions.AsNoTracking()
            .Where(d => d.NextAttemptAt <= DateTime.UtcNow)
            .OrderBy(d => d.NextAttemptAt).Select(d => d.SupabaseUserId)
            .Take(20).ToListAsync(cancellationToken);

    public Task<AccountDeletion?> GetDeletionAsync(Guid providerId, CancellationToken cancellationToken) =>
        _context.AccountDeletions.SingleOrDefaultAsync(d => d.SupabaseUserId == providerId, cancellationToken);

    public async Task CompleteDeletionAsync(AccountDeletion deletion, CancellationToken cancellationToken)
    {
        await _context.Users.Where(u => u.Id == deletion.UserId && u.SupabaseUserId == deletion.SupabaseUserId)
            .ExecuteDeleteAsync(cancellationToken);
        _context.AccountDeletions.Remove(deletion);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordDeletionFailureAsync(AccountDeletion deletion, CancellationToken cancellationToken)
    {
        deletion.Attempts = Math.Min(deletion.Attempts + 1, 1000);
        deletion.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Min(3600, 15 * Math.Pow(2, Math.Min(deletion.Attempts, 8))));
        await _context.SaveChangesAsync(cancellationToken);
    }

    // Add a new user to the DbSet but does not save changes yet
    public async Task AddUserAsync(User user)
    {
        await _context.Users.AddAsync(user);
    }

    // Save all changes made in the context to the database
    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    // Optional: Find user by email
    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetBySupabaseUserIdAsync(Guid supabaseUserId)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);
    }

    public async Task<User?> GetByUsername(string username)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
    }
    
}
