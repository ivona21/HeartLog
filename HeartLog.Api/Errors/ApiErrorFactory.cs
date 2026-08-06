using HeartLog.Api.DTOs;

namespace HeartLog.Api.Errors;

public static class ApiErrorFactory
{
    public static ErrorResponse Create(
        ApiErrorCode code,
        string traceId,
        Dictionary<string, string[]>? errors = null,
        string? message = null)
    {
        return new ErrorResponse
        {
            Code = code,
            Message = message ?? ApiErrorMessages.GetDefaultMessage(code),
            Errors = errors,
            TraceId = traceId
        };
    }
}
