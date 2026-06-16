namespace HeartLog.DAL.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Email { get; set; } = string.Empty;

    public Guid? SupabaseUserId { get; set; }
    
    public string? PasswordHash { get; set; }
    
    public string? Username { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<EmotionEntry> EmotionEntries { get; set; } = [];
}
