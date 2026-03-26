namespace HeartLog.Api.DTOs;

public class EmotionEntryResponse
{
    public Guid EntryId { get; set; }
    public string? Comment { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SelectedEmotionResponse> SelectedEmotions { get; set; } = [];
}
