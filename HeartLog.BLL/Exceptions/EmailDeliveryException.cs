namespace HeartLog.BLL.Exceptions;

// Do not attach provider response bodies: they can contain recipient addresses.
public class EmailDeliveryException(string message, bool permanent = false, TimeSpan? retryAfter = null)
    : Exception(message)
{
    public bool Permanent { get; } = permanent;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
