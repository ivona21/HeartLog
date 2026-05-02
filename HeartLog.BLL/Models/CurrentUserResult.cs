namespace HeartLog.BLL.Models;

public class CurrentUserResult
{
    public Guid Id { get; set; }
    public string? Username { get; set; }
    public string Email { get; set; } = string.Empty;
}
