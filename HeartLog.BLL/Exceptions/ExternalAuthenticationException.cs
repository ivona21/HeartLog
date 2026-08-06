namespace HeartLog.BLL.Exceptions;

public enum ExternalAuthenticationFailureReason
{
    InvalidCredentials,
    EmailNotConfirmed,
    ProviderUnavailable
}

public class ExternalAuthenticationException : ExternalAuthException
{
    public ExternalAuthenticationException(ExternalAuthenticationFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public ExternalAuthenticationException(
        ExternalAuthenticationFailureReason reason,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public ExternalAuthenticationFailureReason Reason { get; }
}
