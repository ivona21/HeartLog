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
        return await _externalAuthService.RegisterAsync(user.Email, password);
    }

    public async Task<ExternalAuthEmailConfirmationResult> ConfirmEmailAsync(string tokenHash, string type)
    {
        return await _externalAuthService.ConfirmEmailAsync(tokenHash, type);
    }

    public async Task ResendConfirmationAsync(string email)
    {
        await _externalAuthService.ResendConfirmationAsync(email);
    }

    public async Task SendPasswordResetAsync(string email)
    {
        await _externalAuthService.SendPasswordResetAsync(email);
    }

    public async Task<ExternalAuthSession> ConfirmPasswordResetAsync(string tokenHash, string type)
    {
        return await _externalAuthService.ConfirmPasswordResetAsync(tokenHash, type);
    }

    public async Task ResetPasswordAsync(string recoveryAccessToken, string newPassword)
    {
        await _externalAuthService.ResetPasswordAsync(recoveryAccessToken, newPassword);
    }

    public async Task<ExternalAuthSession> LoginUserAsync(string email, string password)
    {
        var session = await _externalAuthService.LoginAsync(email, password);

        await EnsureLocalUserExistsOrCreateAsync(session);

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

    private async Task EnsureLocalUserExistsOrCreateAsync(ExternalAuthSession session)
    {
        var existingUser = await _userRepository.GetBySupabaseUserIdAsync(session.User.ProviderUserId);
        if (existingUser is not null)
        {
            return;
        }

        var existingUserByEmail = await _userRepository.GetByEmailAsync(session.User.Email);
        if (existingUserByEmail is not null)
        {
            if (existingUserByEmail.SupabaseUserId is not null)
            {
                _logger.LogWarning(
                    "Supabase login returned user id {SupabaseUserId} for email {Email}, but local user is linked to different Supabase user id {ExistingSupabaseUserId}.",
                    session.User.ProviderUserId,
                    session.User.Email,
                    existingUserByEmail.SupabaseUserId);
                throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
            }

            existingUserByEmail.SupabaseUserId = session.User.ProviderUserId;
            await _userRepository.SaveChangesAsync();

            return;
        }

        var user = new User
        {
            Email = session.User.Email,
            SupabaseUserId = session.User.ProviderUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddUserAsync(user);
        await _userRepository.SaveChangesAsync();
    }
}
