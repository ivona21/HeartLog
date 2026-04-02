using HeartLog.DAL.Models;

namespace HeartLog.DAL.Interfaces;

public interface IUserRepository
{
    Task AddUserAsync(User user);
    Task SaveChangesAsync();
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByUsername(string username);
}
