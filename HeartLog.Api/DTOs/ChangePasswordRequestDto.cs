using System.ComponentModel.DataAnnotations;
using HeartLog.Api.Validation;

namespace HeartLog.Api.DTOs;

public class ChangePasswordRequestDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [PasswordComplexity(MinimumLength = ValidationConstants.PasswordMinimumLength)]
    public string NewPassword { get; set; } = string.Empty;
}
