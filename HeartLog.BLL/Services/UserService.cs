using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models;
using HeartLog.BLL.Models.Auth;
using HeartLog.BLL.Services;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace HeartLog.BLL;

public class UserService: IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserService> _logger;
    private readonly IExternalAuthService _externalAuthService;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly JwtTokenGenerator _tokenGenerator;
    
    public UserService(
        IUserRepository userRepository,
        ILogger<UserService> logger,
        IExternalAuthService externalAuthService,
        JwtTokenGenerator tokenGenerator)
    {
        _userRepository = userRepository;
        _logger = logger;
        _externalAuthService = externalAuthService;
        _passwordHasher = new PasswordHasher<User>();
        _tokenGenerator = tokenGenerator;
    }
    
    public async Task<ExternalAuthSession> RegisterUserAsync(User user, string password)
    {
        // check if user already exists
        var existingUser = await _userRepository.GetByEmailAsync(user.Email);
        if (existingUser != null)
        {
            _logger.LogInformation("Attempted registration with existing email: {Email}", user.Email);
            throw new ExistingEmailException(user.Email);
        }

        var session = await _externalAuthService.RegisterAsync(user.Email, password);

        user.Email = session.User.Email;
        user.SupabaseUserId = session.User.ProviderUserId;
        user.PasswordHash = string.Empty;

        // call repository to save user
        await _userRepository.AddUserAsync(user);
        await _userRepository.SaveChangesAsync();

        return session;
    }

    public async Task<string> LoginUserAsync(string email, string password)
    {
        var existingUser = await _userRepository.GetByEmailAsync(email);
        if (existingUser == null)
        {
            _logger.LogInformation("Login attempt with non-existing email: {Email}", email);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }
        
        var passwordVerificationResult = _passwordHasher.VerifyHashedPassword(existingUser, existingUser.PasswordHash, password);
        if (passwordVerificationResult == PasswordVerificationResult.Failed)
        {
            _logger.LogInformation("Login attempt with incorrect password for email: {Email}", email);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        return _tokenGenerator.GenerateToken(existingUser);
    }

    public async Task<CurrentUserResult> GetCurrentUserAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new UnauthorizedAccessException("Authenticated user email was not found.");
        }

        var existingUser = await _userRepository.GetByEmailAsync(email);
        if (existingUser == null)
        {
            _logger.LogInformation("Authenticated user could not be resolved for email: {Email}", email);
            throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
        }

        return new CurrentUserResult
        {
            Id = existingUser.Id,
            Username = existingUser.Username,
            Email = existingUser.Email
        };
    }
}
