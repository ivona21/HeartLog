using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.Extensions.Logging;

namespace HeartLog.BLL;

public class UserService: IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserService> _logger;
    private readonly IExternalAuthService _externalAuthService;
    
    public UserService(
        IUserRepository userRepository,
        ILogger<UserService> logger,
        IExternalAuthService externalAuthService)
    {
        _userRepository = userRepository;
        _logger = logger;
        _externalAuthService = externalAuthService;
    }
    
    public async Task<ExternalAuthRegistrationResult> RegisterUserAsync(User user, string password)
    {
        // check if user already exists
        var existingUser = await _userRepository.GetByEmailAsync(user.Email);
        if (existingUser != null)
        {
            _logger.LogInformation("Attempted registration with existing email: {Email}", user.Email);
            throw new ExistingEmailException(user.Email);
        }

        return await _externalAuthService.RegisterAsync(user.Email, password);
    }

    public async Task<ExternalAuthSession> LoginUserAsync(string email, string password)
    {
        var session = await _externalAuthService.LoginAsync(email, password);

        await EnsureLocalUserExistsAsync(session);

        return session;
    }

    public async Task<ExternalAuthSession> RefreshSessionAsync(string refreshToken)
    {
        var session = await _externalAuthService.RefreshAsync(refreshToken);

        await EnsureLocalUserExistsAsync(session);

        return session;
    }

    private async Task EnsureLocalUserExistsAsync(ExternalAuthSession session)
    {
        var existingUser = await _userRepository.GetBySupabaseUserIdAsync(session.User.ProviderUserId);
        if (existingUser is null)
        {
            _logger.LogInformation(
                "Supabase authentication succeeded but local HeartLog user was not found. SupabaseUserId: {SupabaseUserId}, Email: {Email}",
                session.User.ProviderUserId,
                session.User.Email);
            throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
        }
    }
}
