using HeartLog.Api.DTOs;
using HeartLog.DAL.Models;

namespace HeartLog.Api.Mappers;

public static class UserMapper
{
    public static User ToEntity(UserRegisterDto dto)
    {
        return new User
        {
            Email = dto.Email,
            Username = dto.Username,
            CreatedAt = DateTime.UtcNow
        };
    }
}