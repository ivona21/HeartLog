using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models;
using HeartLog.DAL.Interfaces;

namespace HeartLog.BLL;

public class EmotionService : IEmotionService
{
    private readonly IEmotionsRepository _emotionsRepository;

    public EmotionService(IEmotionsRepository emotionsRepository)
    {
        _emotionsRepository = emotionsRepository;
    }

    public async Task<IReadOnlyList<EmotionTreeNode>> GetEmotionTreeAsync(string locale)
    {
        var emotions = await _emotionsRepository.GetAllWithTranslationsAsync(locale);

        var nodesByEmotionId = emotions.ToDictionary(
            emotion => emotion.Id,
            emotion => new EmotionTreeNode
            {
                Id = emotion.Key,
                Label = emotion.Translations.FirstOrDefault()?.Label ?? emotion.Key,
                Color = emotion.Color
            });

        foreach (var emotion in emotions)
        {
            if (emotion.ParentId is null)
            {
                continue;
            }

            nodesByEmotionId[emotion.ParentId.Value].Children.Add(nodesByEmotionId[emotion.Id]);
        }

        return emotions
            .Where(e => e.ParentId is null)
            .OrderBy(e => e.SortOrder)
            .Select(e => nodesByEmotionId[e.Id])
            .ToList();
    }
}
