using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Interfaces;
using HeartLog.BLL.Models;
using HeartLog.DAL.Models;

namespace HeartLog.BLL;

public class EmotionEntryService : IEmotionEntryService
{
    private readonly IUserRepository _userRepository;
    private readonly IEmotionsRepository _emotionsRepository;
    private readonly IEmotionEntriesRepository _emotionEntriesRepository;

    public EmotionEntryService(
        IUserRepository userRepository,
        IEmotionsRepository emotionsRepository,
        IEmotionEntriesRepository emotionEntriesRepository)
    {
        _userRepository = userRepository;
        _emotionsRepository = emotionsRepository;
        _emotionEntriesRepository = emotionEntriesRepository;
    }

    public async Task<IReadOnlyList<EmotionEntryResult>> GetAllByUserAsync(
        string userEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new UnauthorizedAccessException("Authenticated user email was not found.");
        }

        var user = await _userRepository.GetByEmailAsync(userEmail);
        if (user is null)
        {
            throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
        }

        var entries = await _emotionEntriesRepository.GetAllByUserIdAsync(user.Id, cancellationToken);

        return entries
            .Select(entry => new EmotionEntryResult
            {
                EntryId = entry.Id,
                Comment = entry.Comment,
                OccurredAt = entry.OccurredAt,
                CreatedAt = entry.CreatedAt,
                SelectedEmotions = entry.EmotionEntryEmotions
                    .OrderByDescending(entryEmotion => entryEmotion.IsPrimary)
                    .ThenBy(entryEmotion => entryEmotion.Emotion.Key)
                    .Select(entryEmotion => new SelectedEmotion
                    {
                        EmotionKey = entryEmotion.Emotion.Key,
                        IsPrimary = entryEmotion.IsPrimary
                    })
                    .ToList()
            })
            .ToList();
    }

    public async Task<EmotionEntryResult> CreateEmotionEntryAsync(
        string userEmail,
        IReadOnlyList<string> emotionKeys,
        string primaryEmotionKey,
        string? comment,
        DateTime? occurredAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new UnauthorizedAccessException("Authenticated user email was not found.");
        }

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

        var user = await _userRepository.GetByEmailAsync(userEmail);
        if (user is null)
        {
            throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
        }

        var emotions = await _emotionsRepository.GetActiveByKeysAsync(normalizedEmotionKeys);
        if (emotions.Count != normalizedEmotionKeys.Count)
        {
            throw new InvalidEmotionEntryException("One or more selected emotions do not exist or are inactive.");
        }

        var emotionsByKey = emotions.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var entryOccurredAt = occurredAt ?? DateTime.UtcNow;
        var entryCreatedAt = DateTime.UtcNow;

        var emotionEntry = new EmotionEntry
        {
            UserId = user.Id,
            Comment = comment,
            OccurredAt = entryOccurredAt,
            CreatedAt = entryCreatedAt,
            UpdatedAt = entryCreatedAt,
            EmotionEntryEmotions = normalizedEmotionKeys
                .Select(key => new EmotionEntryEmotion
                {
                    EmotionId = emotionsByKey[key].Id,
                    IsPrimary = string.Equals(key, normalizedPrimaryEmotionKey, StringComparison.Ordinal),
                    CreatedAt = entryCreatedAt
                })
                .ToList()
        };

        await _emotionEntriesRepository.AddAsync(emotionEntry, cancellationToken);
        await _emotionEntriesRepository.SaveChangesAsync(cancellationToken);

        return new EmotionEntryResult
        {
            EntryId = emotionEntry.Id,
            Comment = emotionEntry.Comment,
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

    public async Task<EmotionEntriesSummary> GetSummaryAsync(
        string userEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new UnauthorizedAccessException("Authenticated user email was not found.");
        }

        var user = await _userRepository.GetByEmailAsync(userEmail);
        if (user is null)
        {
            throw new UnauthorizedAccessException("Authenticated user could not be resolved.");
        }

        var totalEntries = await _emotionEntriesRepository.CountByUserIdAsync(user.Id, cancellationToken);
        var latestOccurredAt = await _emotionEntriesRepository.GetLatestOccurredAtByUserIdAsync(user.Id, cancellationToken);

        return new EmotionEntriesSummary
        {
            TotalEntries = totalEntries,
            LatestOccurredAt = latestOccurredAt
        };
    }
}
