using HeartLog.Api.DTOs;
using HeartLog.DAL.Models;

namespace HeartLog.Api.Mappers;

public static class UserMapper
{
    public static User ToEntity(UserRegisterDto dto)
    {
        User user = new User
        {
            Email = dto.Email,
            PasswordHash = dto.Password,
            Username = dto.Username,
            CreatedAt = DateTime.UtcNow
        };
        return user;
    }
}