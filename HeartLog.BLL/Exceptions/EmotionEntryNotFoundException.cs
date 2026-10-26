namespace HeartLog.BLL.Exceptions;

public class EmotionEntryNotFoundException : Exception
{
    public EmotionEntryNotFoundException() : base("Emotion entry was not found.")
    {
    }
}
