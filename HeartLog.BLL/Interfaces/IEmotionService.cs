using HeartLog.BLL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IEmotionService
{
    Task<IReadOnlyList<EmotionTreeNode>> GetEmotionTreeAsync(string locale);
}
