namespace HeartLog.BLL.Models;

public class EmotionEntryResult
{
    public Guid EntryId { get; set; }
    public string? Comment { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SelectedEmotion> SelectedEmotions { get; set; } = [];
}
