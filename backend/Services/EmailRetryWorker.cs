namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Re-sends emails whose first delivery failed (SMTP down, wrong host, certificate error), so a mail queued while
/// the server was misconfigured still goes out once it is fixed. Rows older than <c>Mail:RetryHours</c> are left
/// alone: an appointment confirmation a day late does more harm than good.
/// </summary>
public sealed class EmailRetryWorker : BackgroundService
{
    // Leaves the first attempt (made on a background task right after queueing) time to finish.
    private static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailRetryWorker> _logger;

    public EmailRetryWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<EmailRetryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Clamp(_config.GetValue("Mail:RetryPollMinutes", 5), 1, 60);
        var retryHours = Math.Clamp(_config.GetValue("Mail:RetryHours", 24), 1, 72);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var now = DateTime.UtcNow;
                var (attempted, sent) = await notifications.RetryFailedEmailsAsync(
                    now.AddHours(-retryHours), now - MinAge, take: 25, stoppingToken);
                if (attempted > 0)
                    _logger.LogInformation("Email retry sweep: {Sent} of {Attempted} unsent emails delivered.", sent, attempted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Email retry sweep failed");
            }
        }
    }
}
