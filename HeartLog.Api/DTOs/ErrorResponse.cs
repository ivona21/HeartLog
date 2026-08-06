namespace HeartLog.Api.DTOs;

public class ErrorResponse
{
    public required ApiErrorCode Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string[]>? Errors { get; set; }
    public string? TraceId { get; set; }
}
