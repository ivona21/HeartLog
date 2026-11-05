namespace HeartLog.BLL.Models.Email;

public class EmailSettings
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
}
