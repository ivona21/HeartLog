using HeartLog.Api.DTOs;

namespace HeartLog.Api.Errors;

public static class ApiErrorMessages
{
    public const string InvalidCredentials = "Invalid email or password.";
    public const string EmailNotConfirmed = "Please confirm your email before logging in.";
    public const string AuthenticationUnavailable = "Unable to complete authentication. Please try again.";
    public const string ValidationFailed = "One or more validation errors occurred.";
    public const string UnexpectedError = "Something went wrong. Please try again.";
    public const string EmailAlreadyExists = "Unable to register. Please check your input or try logging in if you already have an account.";
    public const string UsernameAlreadyExists = "Username is taken.";
    public const string InvalidRequest = "The request could not be processed.";
    public const string Unauthorized = "Unauthorized.";

    public static string GetDefaultMessage(ApiErrorCode code)
    {
        return code switch
        {
            ApiErrorCode.InvalidCredentials => InvalidCredentials,
            ApiErrorCode.EmailNotConfirmed => EmailNotConfirmed,
            ApiErrorCode.AuthenticationUnavailable => AuthenticationUnavailable,
            ApiErrorCode.ValidationFailed => ValidationFailed,
            ApiErrorCode.EmailAlreadyExists => EmailAlreadyExists,
            ApiErrorCode.UsernameAlreadyExists => UsernameAlreadyExists,
            ApiErrorCode.InvalidRequest => InvalidRequest,
            ApiErrorCode.Unauthorized => Unauthorized,
            _ => UnexpectedError
        };
    }
}
