using HeartLog.DAL.Models;

namespace HeartLog.DAL.Interfaces;

public interface IUserRepository
{
    Task AddUserAsync(User user);
    Task SaveChangesAsync();
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetBySupabaseUserIdAsync(Guid supabaseUserId);
    Task<User?> GetByUsername(string username);
}
