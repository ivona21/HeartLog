using System.Security.Claims;
using HeartLog.DAL.Models;

namespace HeartLog.BLL.Interfaces;

public interface ICurrentUserService
{
    Task<User> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
