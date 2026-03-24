using HeartLog.DAL.Models;

namespace HeartLog.DAL.Seeding;

public sealed record EmotionSeedNode(
    string Key,
    string Label,
    string? Color = null,
    IReadOnlyList<EmotionSeedNode>? Children = null);

public sealed record EmotionSeedItem(
    string Key,
    string Label,
    EmotionLevel Level,
    string? Color,
    int SortOrder,
    string? ParentKey);
