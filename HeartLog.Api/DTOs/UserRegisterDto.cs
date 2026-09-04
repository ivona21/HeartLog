using System.ComponentModel.DataAnnotations;
using HeartLog.Api.Validation;

namespace HeartLog.Api.DTOs;

public class UserRegisterDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [PasswordComplexity(MinimumLength = ValidationConstants.PasswordMinimumLength)]
    public string Password { get; set; } = string.Empty;
}
