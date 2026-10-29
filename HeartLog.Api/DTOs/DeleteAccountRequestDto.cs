using System.ComponentModel.DataAnnotations;

namespace HeartLog.Api.DTOs;

public class DeleteAccountRequestDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;
}
