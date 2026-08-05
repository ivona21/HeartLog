namespace HeartLog.Api.DTOs;

public enum ApiErrorCode
{
    InvalidCredentials,
    EmailNotConfirmed,
    AuthenticationUnavailable,
    ValidationFailed,
    UnexpectedError,
    EmailAlreadyExists,
    UsernameAlreadyExists,
    InvalidRequest,
    Unauthorized
}
