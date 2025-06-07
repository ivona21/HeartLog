namespace HeartLog.BLL.Exceptions;

public class ExistingUsernameException : Exception
{
    public ExistingUsernameException(string username) : base("Username ${username} is taken")
    {
    }
}