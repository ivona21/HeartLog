using System.ComponentModel.DataAnnotations;

namespace HeartLog.Api.DTOs;

public class SendTestEmailRequestDto
{
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;
}
