namespace HeartLog.BLL.Exceptions;

public class ExistingEmailException : Exception
{
    public ExistingEmailException(string email) : base($"User with email ${email} already exists.")
    {
        
    }
}