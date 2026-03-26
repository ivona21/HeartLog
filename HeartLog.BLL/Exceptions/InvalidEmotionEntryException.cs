namespace HeartLog.BLL.Exceptions;

public class InvalidEmotionEntryException : Exception
{
    public InvalidEmotionEntryException(string message) : base(message)
    {
    }
}
