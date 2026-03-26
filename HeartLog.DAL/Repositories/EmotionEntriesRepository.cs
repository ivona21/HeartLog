using HeartLog.DAL.Data;
using HeartLog.DAL.Models;

namespace HeartLog.DAL.Repositories;

public class EmotionEntriesRepository
{
    private readonly ApplicationDbContext _context;

    public EmotionEntriesRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(EmotionEntry emotionEntry, CancellationToken cancellationToken = default)
    {
        await _context.EmotionEntries.AddAsync(emotionEntry, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
