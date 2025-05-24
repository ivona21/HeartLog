using HeartLog.Api.DTOs;
using HeartLog.BLL;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Identity;

namespace HeartLog.Api.Mappers;

public static class UserMapper
{
    public static User ToEntity(UserRegisterDto dto, PasswordHasher<User> passwordHasher = null)
    {
        // If passwordHasher is not provided, use a default instance
        passwordHasher ??= new PasswordHasher<User>();

        // Hash the password using the provided or default PasswordHasher
        string hashedPassword = passwordHasher.HashPassword(null, dto.Password);

        // Create a new User entity with the hashed password
        User user = new User
        {
            Email = dto.Email,
            PasswordHash = hashedPassword,
            Username = dto.Username,
            CreatedAt = DateTime.UtcNow
        };
        return user;
    }
}