using HeartLog.DAL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IUserService
{
    Task RegisterUserAsync(User user);
    Task LoginUserAsync(string email, string password);
}