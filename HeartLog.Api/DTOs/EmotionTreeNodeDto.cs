namespace HeartLog.Api.DTOs;

public class EmotionTreeNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Color { get; set; }
    public List<EmotionTreeNodeDto> Children { get; set; } = [];
}
