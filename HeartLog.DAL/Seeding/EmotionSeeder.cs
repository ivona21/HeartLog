using HeartLog.DAL.Data;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.DAL.Seeding;

public class EmotionSeeder
{
    private const string DefaultLocale = "en";
    private readonly ApplicationDbContext _context;

    public EmotionSeeder(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seedItems = EmotionSeedData.GetAll();
        var existingEmotions = await _context.Emotions
            .Include(e => e.Translations)
            .ToDictionaryAsync(e => e.Key, cancellationToken);

        foreach (var item in seedItems)
        {
            if (!existingEmotions.ContainsKey(item.Key))
            {
                var emotion = new Emotion
                {
                    Id = Guid.NewGuid(),
                    Key = item.Key,
                    IsActive = true
                };

                existingEmotions[item.Key] = emotion;
                await _context.Emotions.AddAsync(emotion, cancellationToken);
            }
        }

        foreach (var item in seedItems)
        {
            var emotion = existingEmotions[item.Key];

            emotion.Level = item.Level;
            emotion.Color = item.Color;
            emotion.SortOrder = item.SortOrder;
            emotion.IsActive = true;
            emotion.ParentId = item.ParentKey is null
                ? null
                : existingEmotions[item.ParentKey].Id;

            var translation = emotion.Translations
                .FirstOrDefault(t => t.Locale == DefaultLocale);

            if (translation is null)
            {
                translation = new EmotionTranslation
                {
                    Id = Guid.NewGuid(),
                    EmotionId = emotion.Id,
                    Locale = DefaultLocale
                };

                emotion.Translations.Add(translation);
                await _context.EmotionTranslations.AddAsync(translation, cancellationToken);
            }

            translation.Label = item.Label;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
