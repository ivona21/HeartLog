using HeartLog.BLL.Models;
using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IUserService
{
    Task<ExternalAuthSession> RegisterUserAsync(User user, string password);
    Task<ExternalAuthSession> LoginUserAsync(string email, string password);
    Task<CurrentUserResult> GetCurrentUserAsync(string email);
}
