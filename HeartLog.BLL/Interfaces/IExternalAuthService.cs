using HeartLog.BLL.Models.Auth;

namespace HeartLog.BLL.Interfaces;

public interface IExternalAuthService
{
    Task TestConnectionAsync();
    Task<ExternalAuthRegistrationResult> RegisterAsync(string email, string password);
    Task<ExternalAuthEmailConfirmationResult> ConfirmEmailAsync(string tokenHash, string type);
    Task ResendConfirmationAsync(string email);
    Task SendPasswordResetAsync(string email);
    Task<ExternalAuthSession> ConfirmPasswordResetAsync(string tokenHash, string type);
    Task ResetPasswordAsync(string recoveryAccessToken, string newPassword);
    Task<ExternalAuthSession> LoginAsync(string email, string password);
    Task<ExternalAuthSession> RefreshAsync(string refreshToken);
}
