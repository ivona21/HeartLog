namespace HeartLog.BLL.Models;

public class EmotionTreeNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Color { get; set; }
    public List<EmotionTreeNode> Children { get; set; } = [];
}
