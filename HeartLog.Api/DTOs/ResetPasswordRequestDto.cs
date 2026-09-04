using System.ComponentModel.DataAnnotations;
using HeartLog.Api.Validation;

namespace HeartLog.Api.DTOs;

public class ResetPasswordRequestDto
{
    [Required]
    [PasswordComplexity(MinimumLength = ValidationConstants.PasswordMinimumLength)]
    public string Password { get; set; } = string.Empty;
}
