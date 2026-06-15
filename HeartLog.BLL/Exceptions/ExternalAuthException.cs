namespace HeartLog.BLL.Exceptions;

public class ExternalAuthException : Exception
{
    public ExternalAuthException(string message) : base(message)
    {
    }

    public ExternalAuthException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
