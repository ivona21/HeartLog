using System.ComponentModel.DataAnnotations;

namespace HeartLog.Api.DTOs;

public class ResendConfirmationRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
