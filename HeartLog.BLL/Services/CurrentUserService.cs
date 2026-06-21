using System.Security.Claims;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.Extensions.Logging;

namespace HeartLog.BLL;

public class CurrentUserService : ICurrentUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<CurrentUserService> _logger;

    public CurrentUserService(
        IUserRepository userRepository,
        ILogger<CurrentUserService> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<User> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var subject = principal.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var supabaseUserId))
        {
            throw new UnauthorizedAccessException("Authenticated user id was not found.");
        }

        var user = await _userRepository.GetBySupabaseUserIdAsync(supabaseUserId);
        if (user is null)
        {
            _logger.LogInformation(
                "Authenticated Supabase user could not be resolved locally: {SupabaseUserId}",
                supabaseUserId);
            throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
        }

        return user;
    }
}
