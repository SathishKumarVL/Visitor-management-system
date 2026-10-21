namespace Tiaano.Vms.Api.Services;

/// <summary>Sends minutes-of-meeting reminders and the daily summary. Idempotent via NotificationOutbox.IdempotencyKey.</summary>
public sealed class MomReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MomReminderWorker> _logger;

    public MomReminderWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<MomReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Clamp(_config.GetValue("Reminders:PollMinutes", 5), 1, 60);
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reminders = scope.ServiceProvider.GetRequiredService<IEmployeeNotificationService>();
                var now = DateTime.UtcNow;
                var single = await reminders.SendMomRemindersAsync(now, stoppingToken);
                var digests = await reminders.SendMomDigestsAsync(now, stoppingToken);
                if (single + digests > 0)
                    _logger.LogInformation("Minutes of meeting reminders: {Single} visit(s), {Digests} daily summaries", single, digests);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Minutes of meeting reminder sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
