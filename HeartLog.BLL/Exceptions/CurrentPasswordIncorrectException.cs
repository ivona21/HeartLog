namespace HeartLog.BLL.Exceptions;

public class CurrentPasswordIncorrectException : ExternalAuthException
{
    public CurrentPasswordIncorrectException()
        : base("Current password is incorrect.")
    {
    }
}
