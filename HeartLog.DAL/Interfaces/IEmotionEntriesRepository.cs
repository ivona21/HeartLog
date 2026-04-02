using HeartLog.DAL.Models;

namespace HeartLog.DAL.Interfaces;

public interface IEmotionEntriesRepository
{
    Task AddAsync(EmotionEntry emotionEntry, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLatestOccurredAtByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<EmotionEntry>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
