using System.ComponentModel.DataAnnotations;

namespace HeartLog.Api.DTOs;

public class RefreshSessionRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
