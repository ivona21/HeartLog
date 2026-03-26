namespace HeartLog.Api.DTOs;

public class EmotionEntriesSummaryResponse
{
    public int TotalEntries { get; set; }
    public DateTime? LatestOccurredAt { get; set; }
}
