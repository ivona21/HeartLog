using System.ComponentModel.DataAnnotations;
using HeartLog.Api.Validation;

namespace HeartLog.Api.DTOs;

public class ResetPasswordRequestDto
{
    [Required]
    [PasswordComplexity(MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
