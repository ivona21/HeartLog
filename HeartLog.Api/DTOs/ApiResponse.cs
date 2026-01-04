namespace HeartLog.Api.DTOs;

// Basic non-generic response
public record ApiResponse(bool Success, string Message);

// Generic response with data
public record ApiResponse<T>(bool Success, string Message, T? Data);