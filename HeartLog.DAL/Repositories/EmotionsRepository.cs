using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.DAL.Repositories;

public class EmotionsRepository : IEmotionsRepository
{
    private readonly ApplicationDbContext _context;

    public EmotionsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Emotion>> GetAllWithTranslationsAsync(string locale)
    {
        return await _context.Emotions
            .AsNoTracking()
            .Where(e => e.IsActive)
            .Include(e => e.Translations.Where(t => t.Locale == locale))
            .OrderBy(e => e.SortOrder)
            .ToListAsync();
    }

    public async Task<List<Emotion>> GetActiveByKeysAsync(IEnumerable<string> keys)
    {
        var keyList = keys.ToList();

        return await _context.Emotions
            .Where(e => e.IsActive && keyList.Contains(e.Key))
            .ToListAsync();
    }
}
