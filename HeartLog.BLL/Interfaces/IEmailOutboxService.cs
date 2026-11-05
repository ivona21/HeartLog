namespace HeartLog.BLL.Interfaces;

public interface IEmailOutboxService
{
    Task<Guid> QueueAccountDeletionConfirmationAsync(string recipient, CancellationToken cancellationToken = default);
    Task ProcessAsync(CancellationToken cancellationToken);
}
