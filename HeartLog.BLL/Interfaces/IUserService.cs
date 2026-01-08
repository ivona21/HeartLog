using HeartLog.DAL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IUserService
{
    Task RegisterUserAsync(User user, string password);
    Task<string> LoginUserAsync(string email, string password);
}