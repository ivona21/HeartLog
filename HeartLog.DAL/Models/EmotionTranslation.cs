namespace HeartLog.DAL.Models;

public class EmotionTranslation
{
    public Guid Id { get; set; }

    public Guid EmotionId { get; set; }
    public Emotion Emotion { get; set; } = null!;

    public string Locale { get; set; } = null!;
    public string Label { get; set; } = null!;
}
