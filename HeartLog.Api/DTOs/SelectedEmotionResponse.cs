namespace HeartLog.Api.DTOs;

public class SelectedEmotionResponse
{
    public string EmotionKey { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}
