using HeartLog.BLL.Models.Auth;

namespace HeartLog.BLL.Interfaces;

public interface IExternalAuthService
{
    Task TestConnectionAsync();
    Task<ExternalAuthRegistrationResult> RegisterAsync(string email, string password);
    Task<ExternalAuthSession> LoginAsync(string email, string password);
    Task<ExternalAuthSession> RefreshAsync(string refreshToken);
}
