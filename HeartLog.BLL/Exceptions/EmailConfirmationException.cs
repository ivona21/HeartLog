namespace HeartLog.BLL.Exceptions;

public enum EmailConfirmationFailureReason
{
    Invalid,
    Expired
}

public class EmailConfirmationException : ExternalAuthException
{
    public EmailConfirmationException(EmailConfirmationFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public EmailConfirmationException(EmailConfirmationFailureReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public EmailConfirmationFailureReason Reason { get; }
}
