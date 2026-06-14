using HeartLog.BLL.Models.Auth;

namespace HeartLog.BLL.Interfaces;

public interface IExternalAuthService
{
    Task TestConnectionAsync();
    Task<ExternalAuthUser> RegisterAsync(string email, string password);
    Task<ExternalAuthSession> LoginAsync(string email, string password);
}
