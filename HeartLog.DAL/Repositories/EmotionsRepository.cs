using HeartLog.DAL.Data;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.DAL.Repositories;

public class EmotionsRepository
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
            .Include(e => e.Translations.Where(t => t.Locale == locale))
            .OrderBy(e => e.SortOrder)
            .ToListAsync();
    }
}
