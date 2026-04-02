using HeartLog.Api.DTOs;
using HeartLog.BLL.Models;

namespace HeartLog.Api.Mappers;

public static class EmotionEntryMapper
{
    public static List<EmotionEntryResponse> ToDto(this IEnumerable<EmotionEntryResult> results)
    {
        return results.Select(ToDto).ToList();
    }

    public static EmotionEntryResponse ToDto(this EmotionEntryResult result)
    {
        return new EmotionEntryResponse
        {
            EntryId = result.EntryId,
            Comment = result.Comment,
            OccurredAt = result.OccurredAt,
            CreatedAt = result.CreatedAt,
            SelectedEmotions = result.SelectedEmotions
                .Select(se => new SelectedEmotionResponse
                {
                    EmotionKey = se.EmotionKey,
                    IsPrimary = se.IsPrimary
                })
                .ToList()
        };
    }

    public static EmotionEntriesSummaryResponse ToDto(this EmotionEntriesSummary summary)
    {
        return new EmotionEntriesSummaryResponse
        {
            TotalEntries = summary.TotalEntries,
            LatestOccurredAt = summary.LatestOccurredAt
        };
    }
}
