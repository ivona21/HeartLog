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
    
    public async Task RequestAccountDeletionAsync(Guid providerId, string currentPassword, CancellationToken cancellationToken = default)
    {
        await _userRepository.WithProviderLockAsync(providerId, async () =>
        {
            var user = await _userRepository.GetBySupabaseUserIdAsync(providerId)
                ?? throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
            if (user.DeletionRequestedAt is not null) return;
            ExternalAuthSession verification;
            try
            {
                verification = await _externalAuthService.LoginAsync(user.Email, currentPassword);
            }
            catch (ExternalAuthenticationException ex) when (ex.Reason == ExternalAuthenticationFailureReason.InvalidCredentials)
            {
                throw new CurrentPasswordIncorrectException();
            }
            if (verification.User.ProviderUserId != providerId)
                throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
            await _userRepository.RequestDeletionAsync(user, cancellationToken);
        }, cancellationToken);
    }

    public async Task ProcessAccountDeletionsAsync(CancellationToken cancellationToken)
    {
        var ids = await _userRepository.GetDueDeletionsAsync(cancellationToken);
        foreach (var providerId in ids)
        {
            try
            {
                await _userRepository.WithProviderLockAsync(providerId, async () =>
                {
                    var deletion = await _userRepository.GetDeletionAsync(providerId, cancellationToken);
                    if (deletion is null || deletion.NextAttemptAt > DateTime.UtcNow) return;
                    try
                    {
                        await _externalAuthService.DeleteUserAsync(providerId, cancellationToken);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        await _userRepository.RecordDeletionFailureAsync(deletion, cancellationToken);
                        _logger.LogWarning("Account deletion provider cleanup failed; attempt {Attempt}. Request {RequestId}",
                            deletion.Attempts, deletion.UserId);
                        return;
                    }
                    await _userRepository.CompleteDeletionAsync(deletion, cancellationToken);
                }, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The transaction rolls back; the durable task remains eligible for retry.
                _logger.LogError("Account deletion transaction failed; cleanup will be retried.");
            }
        }
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

    public async Task ChangePasswordAsync(string email, string accessToken, string currentPassword, string newPassword)
    {
        await _externalAuthService.ChangePasswordAsync(email, accessToken, currentPassword, newPassword);
    }

    public async Task<ExternalAuthSession> LoginUserAsync(string email, string password)
    {
        var session = await _externalAuthService.LoginAsync(email, password);

        await _userRepository.WithProviderLockAsync(session.User.ProviderUserId, async () =>
        {
            // A login response obtained before deletion must never recreate a deleted user.
            await _externalAuthService.ValidateSessionAsync(session.AccessToken);
            await EnsureLocalUserExistsOrCreateAsync(session);
        });

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
        if (existingUser is null || existingUser.DeletionRequestedAt is not null)
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
            if (existingUser.DeletionRequestedAt is not null)
                throw new UnauthorizedAccessException("Account deletion is pending.");
            return;
        }

        var existingUserByEmail = await _userRepository.GetByEmailAsync(session.User.Email);
        if (existingUserByEmail is not null)
        {
            if (existingUserByEmail.DeletionRequestedAt is not null)
                throw new UnauthorizedAccessException("Account deletion is pending.");
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
