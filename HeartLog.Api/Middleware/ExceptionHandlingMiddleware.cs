using HeartLog.Api.DTOs;
using HeartLog.Api.Errors;
using HeartLog.BLL.Exceptions;
using System.Net;

namespace HeartLog.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TraceId"] = context.TraceIdentifier
            });

            if (IsExpectedException(ex))
            {
                _logger.LogWarning(ex, "A handled request exception occurred. TraceId: {TraceId}", context.TraceIdentifier);
            }
            else
            {
                _logger.LogError(ex, "An unhandled exception occurred. TraceId: {TraceId}", context.TraceIdentifier);
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    private static bool IsExpectedException(Exception exception)
    {
        return exception is ExistingEmailException
            or ExistingUsernameException
            or ExternalAuthException
            or ExternalAuthenticationException
            or InvalidEmotionEntryException
            or UnauthorizedAccessException;
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var error = MapException(exception);
        context.Response.StatusCode = (int)error.StatusCode;

        var response = ApiErrorFactory.Create(
            error.Code,
            context.TraceIdentifier,
            message: error.Message);

        await context.Response.WriteAsJsonAsync(response);
    }

    private static ApiErrorDescriptor MapException(Exception exception)
    {
        return exception switch
        {
            ExternalAuthenticationException authException => MapExternalAuthenticationException(authException),
            ExistingEmailException => new ApiErrorDescriptor(
                HttpStatusCode.BadRequest,
                ApiErrorCode.EmailAlreadyExists,
                ApiErrorMessages.EmailAlreadyExists),
            ExistingUsernameException => new ApiErrorDescriptor(
                HttpStatusCode.Conflict,
                ApiErrorCode.UsernameAlreadyExists,
                ApiErrorMessages.UsernameAlreadyExists),
            InvalidEmotionEntryException invalidEmotionEntryException => new ApiErrorDescriptor(
                HttpStatusCode.BadRequest,
                ApiErrorCode.InvalidRequest,
                invalidEmotionEntryException.Message),
            UnauthorizedAccessException => new ApiErrorDescriptor(
                HttpStatusCode.Unauthorized,
                ApiErrorCode.Unauthorized,
                ApiErrorMessages.Unauthorized),
            ExternalAuthException => new ApiErrorDescriptor(
                HttpStatusCode.ServiceUnavailable,
                ApiErrorCode.AuthenticationUnavailable,
                ApiErrorMessages.AuthenticationUnavailable),
            _ => new ApiErrorDescriptor(
                HttpStatusCode.InternalServerError,
                ApiErrorCode.UnexpectedError,
                ApiErrorMessages.UnexpectedError)
        };
    }

    private static ApiErrorDescriptor MapExternalAuthenticationException(ExternalAuthenticationException exception)
    {
        return exception.Reason switch
        {
            ExternalAuthenticationFailureReason.InvalidCredentials => new ApiErrorDescriptor(
                HttpStatusCode.Unauthorized,
                ApiErrorCode.InvalidCredentials,
                ApiErrorMessages.InvalidCredentials),
            ExternalAuthenticationFailureReason.EmailNotConfirmed => new ApiErrorDescriptor(
                HttpStatusCode.Unauthorized,
                ApiErrorCode.EmailNotConfirmed,
                ApiErrorMessages.EmailNotConfirmed),
            ExternalAuthenticationFailureReason.ProviderUnavailable => new ApiErrorDescriptor(
                HttpStatusCode.ServiceUnavailable,
                ApiErrorCode.AuthenticationUnavailable,
                ApiErrorMessages.AuthenticationUnavailable),
            _ => new ApiErrorDescriptor(
                HttpStatusCode.InternalServerError,
                ApiErrorCode.UnexpectedError,
                ApiErrorMessages.UnexpectedError)
        };
    }

    private sealed record ApiErrorDescriptor(
        HttpStatusCode StatusCode,
        ApiErrorCode Code,
        string Message);
}
