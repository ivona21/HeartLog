namespace HeartLog.BLL.Services.Encryption;

// Shared persisted contract for API writes and historical entry migration.
public sealed record StoredEmotion(Guid EmotionId, bool IsPrimary);
