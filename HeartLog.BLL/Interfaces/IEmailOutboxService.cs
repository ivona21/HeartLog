namespace HeartLog.BLL.Interfaces;

public interface IEmailOutboxService
{
    Task ProcessAsync(CancellationToken cancellationToken);
}
