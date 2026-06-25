using HeartLog.Api.DTOs;
using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Models;

namespace HeartLog.Api.Mappers;

public static class UserMapper
{
    public static User ToEntity(UserRegisterDto dto)
    {
        return new User
        {
            Email = dto.Email,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static UserMeResponseDto ToDto(User user)
    {
        return new UserMeResponseDto
        {
            Id = user.Id.ToString(),
            Username = user.Username,
            Email = user.Email
        };
    }

    public static AuthSessionResponseDto ToDto(ExternalAuthSession session)
    {
        return new AuthSessionResponseDto
        {
            AccessToken = session.AccessToken,
            ExpiresAt = session.ExpiresAt,
            Email = session.User.Email
        };
    }
}
