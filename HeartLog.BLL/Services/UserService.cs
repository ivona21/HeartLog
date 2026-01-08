using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Services;
using HeartLog.DAL.Models;
using HeartLog.DAL.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace HeartLog.BLL;

public class UserService: IUserService
{
    private readonly UserRepository _userRepository;
    private readonly ILogger<UserService> _logger;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly JwtTokenGenerator _tokenGenerator;
    
    public UserService(UserRepository userRepository, ILogger<UserService> logger, JwtTokenGenerator tokenGenerator)
    {
        _userRepository = userRepository;
        _logger = logger;
        _passwordHasher = new PasswordHasher<User>();
        _tokenGenerator = tokenGenerator;
    }
    
    public async Task RegisterUserAsync(User user, string password)
    {
        // check if user already exists
        var existingUser = await _userRepository.GetByEmailAsync(user.Email);
        if (existingUser != null)
        {
            _logger.LogInformation("Attempted registration with existing email: {Email}", user.Email);
            throw new ExistingEmailException(user.Email);
        }
        
        // check if username is taken
        User userWithSameUsername = await _userRepository.GetByUsername(user.Username);
        if (userWithSameUsername != null)
        {
            _logger.LogInformation("Attempted registration with existing username: {Username} from an email {Email}", user.Username, user.Email);
            throw new ExistingUsernameException(user.Username);
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        
        // call repository to save user
        await _userRepository.AddUserAsync(user);
        await _userRepository.SaveChangesAsync();
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
}