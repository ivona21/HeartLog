using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.DAL.Repositories;

public class EmotionEntriesRepository : IEmotionEntriesRepository
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

    public async Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.EmotionEntries
            .CountAsync(entry => entry.UserId == userId, cancellationToken);
    }

    public async Task<DateTime?> GetLatestOccurredAtByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.EmotionEntries
            .Where(entry => entry.UserId == userId)
            .MaxAsync(entry => (DateTime?)entry.OccurredAt, cancellationToken);
    }

    public async Task<List<EmotionEntry>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.EmotionEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .Include(entry => entry.EmotionEntryEmotions)
            .ThenInclude(entryEmotion => entryEmotion.Emotion)
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
