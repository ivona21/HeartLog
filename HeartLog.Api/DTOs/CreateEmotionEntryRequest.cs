using System.ComponentModel.DataAnnotations;

namespace HeartLog.Api.DTOs;

public class CreateEmotionEntryRequest
{
    [Required]
    [MinLength(1)]
    public List<string> EmotionKeys { get; set; } = [];

    [Required]
    public string PrimaryEmotionKey { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public DateTime? OccurredAt { get; set; }
}
