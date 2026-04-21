using HeartLog.Api.DTOs;
using HeartLog.BLL.Models;
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

    public static UserMeResponseDto ToDto(CurrentUserResult result)
    {
        return new UserMeResponseDto
        {
            Id = result.Id.ToString(),
            Username = result.Username,
            Email = result.Email
        };
    }
}
