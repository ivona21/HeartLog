using HeartLog.Api.DTOs;
using HeartLog.BLL.Models;

namespace HeartLog.Api.Mappers;

public static class EmotionMapper
{
    public static EmotionTreeNodeDto ToDto(this EmotionTreeNode node)
    {
        return new EmotionTreeNodeDto
        {
            Id = node.Id,
            Label = node.Label,
            Color = node.Color,
            Children = node.Children.Select(c => c.ToDto()).ToList()
        };
    }
}
