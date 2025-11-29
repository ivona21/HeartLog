namespace HeartLog.Api.DTOs;

public class ErrorResponse
{
    public string Message { get; set; }
    public Dictionary<string, string[]> Errors { get; set; }
}