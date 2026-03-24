namespace HeartLog.DAL.Models;

public enum EmotionLevel
{
    Core = 1,
    Secondary = 2,
    Tertiary = 3
}

public class Emotion
{
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public EmotionLevel Level { get; set; }

    public string? Color { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? ParentId { get; set; }
    public Emotion? Parent { get; set; }

    public List<Emotion> Children { get; set; } = [];
    public List<EmotionTranslation> Translations { get; set; } = [];
}
