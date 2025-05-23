using System.ComponentModel.DataAnnotations;

namespace HeartLog.Api.DTOs;

public class UserRegisterDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;
    
    [Required]
    [MinLength(8, ErrorMessage = "Password must be at least 6 characters.")] //todo - enforce more validation
    public string Password { get; set; } = string.Empty;
}