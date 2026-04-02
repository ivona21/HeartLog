using HeartLog.BLL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IEmotionEntryService
{
    Task<IReadOnlyList<EmotionEntryResult>> GetAllByUserAsync(
        string userEmail,
        CancellationToken cancellationToken = default);

    Task<EmotionEntryResult> CreateEmotionEntryAsync(
        string userEmail,
        IReadOnlyList<string> emotionKeys,
        string primaryEmotionKey,
        string? comment,
        DateTime? occurredAt,
        CancellationToken cancellationToken = default);

    Task<EmotionEntriesSummary> GetSummaryAsync(
        string userEmail,
        CancellationToken cancellationToken = default);
}
