namespace HeartLog.BLL.Interfaces;

public interface IExternalAuthService
{
    Task TestConnectionAsync();
}
