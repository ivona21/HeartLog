namespace HeartLog.DAL.Models;

public class EmotionEntryEmotion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EmotionEntryId { get; set; }
    public EmotionEntry EmotionEntry { get; set; } = null!;

    public Guid EmotionId { get; set; }
    public Emotion Emotion { get; set; } = null!;

    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
