using HeartLog.BLL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IEmotionEntryService
{
    Task<EmotionEntryResult> CreateEmotionEntryAsync(
        string userEmail,
        IReadOnlyList<string> emotionKeys,
        string primaryEmotionKey,
        string? comment,
        DateTime? occurredAt,
        CancellationToken cancellationToken = default);
}
