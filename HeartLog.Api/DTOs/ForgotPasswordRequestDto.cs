using System.ComponentModel.DataAnnotations;

namespace HeartLog.Api.DTOs;

public class ForgotPasswordRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
