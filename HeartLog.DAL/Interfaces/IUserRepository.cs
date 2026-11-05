using HeartLog.DAL.Models;

namespace HeartLog.DAL.Interfaces;

public interface IUserRepository
{
    Task WithProviderLockAsync(Guid providerId, Func<Task> action, CancellationToken cancellationToken = default);
    Task RequestDeletionAsync(User user, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetDueDeletionsAsync(CancellationToken cancellationToken);
    Task<AccountDeletion?> GetDeletionAsync(Guid providerId, CancellationToken cancellationToken);
    Task CompleteDeletionAsync(AccountDeletion deletion, CancellationToken cancellationToken);
    Task RecordDeletionFailureAsync(AccountDeletion deletion, CancellationToken cancellationToken);
    Task AddUserAsync(User user);
    Task SaveChangesAsync();
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetBySupabaseUserIdAsync(Guid supabaseUserId);
    Task<User?> GetByUsername(string username);
}
