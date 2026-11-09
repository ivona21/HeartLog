using HeartLog.DAL.Models;

namespace HeartLog.DAL.Interfaces;

public interface IEmotionsRepository
{
    Task<List<Emotion>> GetAllWithTranslationsAsync(string locale);
    Task<List<Emotion>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<List<Emotion>> GetActiveByKeysAsync(IEnumerable<string> keys);
}
