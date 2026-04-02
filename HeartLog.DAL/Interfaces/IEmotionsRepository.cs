using HeartLog.DAL.Models;

namespace HeartLog.DAL.Interfaces;

public interface IEmotionsRepository
{
    Task<List<Emotion>> GetAllWithTranslationsAsync(string locale);
    Task<List<Emotion>> GetActiveByKeysAsync(IEnumerable<string> keys);
}
