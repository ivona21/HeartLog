using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Models;
using HeartLog.DAL.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace HeartLog.BLL;

public class UserService: IUserService
{
    private readonly UserRepository _userRepository;
    private readonly ILogger<UserService> _logger;
    
    public UserService(UserRepository userRepository, ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }
    
    public async Task RegisterUserAsync(User user)
    {
        // check if user already exists
        var existingUser = await _userRepository.GetByEmailAsync(user.Email);
        if (existingUser != null)
        {
            _logger.LogInformation("Attempted registration with existing email: {Email}", user.Email);
            throw new Exception("User with this email already exists.");
        }
        
        // send an email - later - todo
        
        // call repository to save user
        await _userRepository.AddUserAsync(user);
        await _userRepository.SaveChangesAsync();
    }

    public async Task LoginUserAsync(string email, string password)
    {
        var existingUser = await _userRepository.GetByEmailAsync(email);
        if (existingUser == null)
        {
            _logger.LogInformation("Login attempt with non-existing email: {Email}", email);
            throw new Exception("User with this email does not exist.");
        }
        var passwordHash = new PasswordHasher<User>().HashPassword(null, password);
        var passwordVerificationResult = new PasswordHasher<User>().VerifyHashedPassword(existingUser, existingUser.PasswordHash, passwordHash);
        if (passwordVerificationResult == PasswordVerificationResult.Failed)
        {
            _logger.LogInformation("Login attempt with incorrect password for email: {Email}", email);
            throw new Exception("Incorrect password.");
        }
    }
}