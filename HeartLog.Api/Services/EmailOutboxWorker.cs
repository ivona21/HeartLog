using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Email;
using Microsoft.Extensions.Options;

namespace HeartLog.Api.Services;

public sealed class EmailOutboxWorker(IServiceScopeFactory scopeFactory, IOptions<EmailSettings> options,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            logger.LogWarning("Email delivery is disabled. Notifications remain queued until enabled or expired.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IEmailOutboxService>().ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogError("Email outbox processing failed; processing will resume automatically.");
            }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
