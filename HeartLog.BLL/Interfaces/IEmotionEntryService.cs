using HeartLog.BLL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IEmotionEntryService
{
    Task<IReadOnlyList<EmotionEntryResult>> GetAllByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<EmotionEntryResult> CreateEmotionEntryAsync(
        Guid userId,
        IReadOnlyList<string> emotionKeys,
        string primaryEmotionKey,
        string? comment,
        DateTime? occurredAt,
        CancellationToken cancellationToken = default);

    Task<EmotionEntriesSummary> GetSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
