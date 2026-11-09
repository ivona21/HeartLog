using HeartLog.BLL.Services.Encryption;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Interfaces;
using HeartLog.BLL.Models;
using HeartLog.DAL.Models;

namespace HeartLog.BLL;

public class EmotionEntryService : IEmotionEntryService
{
    private readonly EntryEncryption _encryption;
    private readonly IEmotionsRepository _emotionsRepository;
    private readonly IEmotionEntriesRepository _emotionEntriesRepository;

    public EmotionEntryService(
        IEmotionsRepository emotionsRepository,
        IEmotionEntriesRepository emotionEntriesRepository,
        EntryEncryption encryption)
    {
        _encryption = encryption;
        _emotionsRepository = emotionsRepository;
        _emotionEntriesRepository = emotionEntriesRepository;
    }

    public async Task<IReadOnlyList<EmotionEntryResult>> GetAllByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Authenticated user id was not found.");
        }

        var entries = await _emotionEntriesRepository.GetAllByUserIdAsync(userId, cancellationToken);

        var results = new List<EmotionEntryResult>();
        foreach (var entry in entries)
            results.Add(await ToResultAsync(entry, userId));
        return results;
    }

    private sealed record StoredEmotion(Guid EmotionId, bool IsPrimary);

    private async Task<EmotionEntryResult> ToResultAsync(EmotionEntry entry, Guid userId)
    {
        if (entry.UserId != userId)
            throw new UnauthorizedAccessException("Entry owner does not match.");
        string? comment;
        List<SelectedEmotion> selected;
        if (entry.EncryptedEmotions is not null)
        {
            var stored = _encryption.Decrypt<List<StoredEmotion>>(userId, entry.Id, "emotions", entry.EncryptedEmotions);
            comment = entry.EncryptedNote is null ? null
                : _encryption.Decrypt<string>(userId, entry.Id, "note", entry.EncryptedNote);
            var emotions = (await _emotionsRepository.GetByIdsAsync(stored.Select(e => e.EmotionId)))
                .ToDictionary(e => e.Id);
            selected = stored.Select(e => new SelectedEmotion
            {
                EmotionKey = emotions[e.EmotionId].Key, IsPrimary = e.IsPrimary
            }).ToList();
        }
        else
        {
            comment = entry.Comment;
            selected = entry.EmotionEntryEmotions.Select(e => new SelectedEmotion
            {
                EmotionKey = e.Emotion.Key, IsPrimary = e.IsPrimary
            }).ToList();
        }
        return new EmotionEntryResult
        {
            EntryId = entry.Id, Comment = comment, OccurredAt = entry.OccurredAt,
            CreatedAt = entry.CreatedAt,
            SelectedEmotions = selected.OrderByDescending(e => e.IsPrimary).ThenBy(e => e.EmotionKey).ToList()
        };
    }

    public async Task<EmotionEntryResult> CreateEmotionEntryAsync(
        Guid userId,
        IReadOnlyList<string> emotionKeys,
        string primaryEmotionKey,
        string? comment,
        DateTime? occurredAt,
        CancellationToken cancellationToken = default)
    => await SaveEntryAsync(userId, null, emotionKeys, primaryEmotionKey, comment, occurredAt, cancellationToken);

    public Task<EmotionEntryResult> UpdateEmotionEntryAsync(
        Guid userId, Guid entryId, IReadOnlyList<string> emotionKeys, string primaryEmotionKey,
        string? comment, DateTime? occurredAt, CancellationToken cancellationToken = default)
        => SaveEntryAsync(userId, entryId, emotionKeys, primaryEmotionKey, comment, occurredAt, cancellationToken);

    private async Task<EmotionEntryResult> SaveEntryAsync(
        Guid userId, Guid? entryId, IReadOnlyList<string> emotionKeys, string primaryEmotionKey,
        string? comment, DateTime? occurredAt, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Authenticated user id was not found.");
        }

        var existing = entryId.HasValue
            ? await _emotionEntriesRepository.GetByIdAndUserIdAsync(entryId.Value, userId, cancellationToken)
                ?? throw new EmotionEntryNotFoundException()
            : null;
        // Authentication failures are errors, including when replacing an encrypted row.
        if (existing?.EncryptedEmotions is not null)
            await ToResultAsync(existing, userId);

        if (emotionKeys.Count == 0)
        {
            throw new InvalidEmotionEntryException("At least one emotion must be selected.");
        }

        var normalizedEmotionKeys = emotionKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .ToList();

        if (normalizedEmotionKeys.Count != emotionKeys.Count)
        {
            throw new InvalidEmotionEntryException("Emotion keys cannot be empty.");
        }

        if (normalizedEmotionKeys.Distinct(StringComparer.Ordinal).Count() != normalizedEmotionKeys.Count)
        {
            throw new InvalidEmotionEntryException("Duplicate emotion keys are not allowed.");
        }

        if (string.IsNullOrWhiteSpace(primaryEmotionKey))
        {
            throw new InvalidEmotionEntryException("A primary emotion key is required.");
        }

        var normalizedPrimaryEmotionKey = primaryEmotionKey.Trim();

        if (!normalizedEmotionKeys.Contains(normalizedPrimaryEmotionKey, StringComparer.Ordinal))
        {
            throw new InvalidEmotionEntryException("Primary emotion key must be one of the selected emotions.");
        }

        var emotions = await _emotionsRepository.GetActiveByKeysAsync(normalizedEmotionKeys);
        if (emotions.Count != normalizedEmotionKeys.Count)
        {
            throw new InvalidEmotionEntryException("One or more selected emotions do not exist or are inactive.");
        }

        var emotionsByKey = emotions.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var entryOccurredAt = occurredAt ?? DateTime.UtcNow;
        var entryCreatedAt = DateTime.UtcNow;

        var emotionEntry = existing ?? new EmotionEntry
        {
            UserId = userId, CreatedAt = entryCreatedAt
        };
        emotionEntry.OccurredAt = occurredAt ?? existing?.OccurredAt ?? entryOccurredAt;
        emotionEntry.UpdatedAt = entryCreatedAt;
        emotionEntry.EncryptedNote = comment is null ? null
            : _encryption.Encrypt(userId, emotionEntry.Id, "note", comment);
        emotionEntry.EncryptedEmotions = _encryption.Encrypt(userId, emotionEntry.Id, "emotions",
            normalizedEmotionKeys.Select(key => new StoredEmotion(emotionsByKey[key].Id,
                string.Equals(key, normalizedPrimaryEmotionKey, StringComparison.Ordinal))).ToList());
        emotionEntry.Comment = null;
        emotionEntry.EmotionEntryEmotions.Clear();
        // One SaveChanges transaction writes both bundles and deletes legacy selections.
        if (existing is null)
            await _emotionEntriesRepository.AddAsync(emotionEntry, cancellationToken);
        await _emotionEntriesRepository.SaveChangesAsync(cancellationToken);

        return new EmotionEntryResult
        {
            EntryId = emotionEntry.Id,
            Comment = comment,
            OccurredAt = emotionEntry.OccurredAt,
            CreatedAt = emotionEntry.CreatedAt,
            SelectedEmotions = normalizedEmotionKeys
                .Select(key => new SelectedEmotion
                {
                    EmotionKey = key,
                    IsPrimary = string.Equals(key, normalizedPrimaryEmotionKey, StringComparison.Ordinal)
                })
                .ToList()
        };
    }

    public async Task DeleteEmotionEntryAsync(
        Guid userId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Authenticated user id was not found.");
        }

        var deleted = await _emotionEntriesRepository.DeleteByIdAndUserIdAsync(
            entryId, userId, cancellationToken);

        if (!deleted)
        {
            throw new EmotionEntryNotFoundException();
        }
    }

    public async Task<EmotionEntriesSummary> GetSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Authenticated user id was not found.");
        }

        var totalEntries = await _emotionEntriesRepository.CountByUserIdAsync(userId, cancellationToken);
        var latestOccurredAt = await _emotionEntriesRepository.GetLatestOccurredAtByUserIdAsync(userId, cancellationToken);

        return new EmotionEntriesSummary
        {
            TotalEntries = totalEntries,
            LatestOccurredAt = latestOccurredAt
        };
    }
}
