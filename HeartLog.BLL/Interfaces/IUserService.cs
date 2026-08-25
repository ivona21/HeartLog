using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IUserService
{
    Task<ExternalAuthRegistrationResult> RegisterUserAsync(User user, string password);
    Task<ExternalAuthEmailConfirmationResult> ConfirmEmailAsync(string tokenHash, string type);
    Task ResendConfirmationAsync(string email);
    Task SendPasswordResetAsync(string email);
    Task<ExternalAuthSession> ConfirmPasswordResetAsync(string tokenHash, string type);
    Task ResetPasswordAsync(string recoveryAccessToken, string newPassword);
    Task<ExternalAuthSession> LoginUserAsync(string email, string password);
    Task<ExternalAuthSession> RefreshSessionAsync(string refreshToken);
}
