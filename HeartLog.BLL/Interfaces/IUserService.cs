using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IUserService
{
    Task<ExternalAuthRegistrationResult> RegisterUserAsync(User user, string password);
    Task<ExternalAuthEmailConfirmationResult> ConfirmEmailAsync(string tokenHash, string type);
    Task ResendConfirmationAsync(string email);
    Task<ExternalAuthSession> LoginUserAsync(string email, string password);
    Task<ExternalAuthSession> RefreshSessionAsync(string refreshToken);
}
