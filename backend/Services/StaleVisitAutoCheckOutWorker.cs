namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Automatically checks out visitors who have remained Inside longer than the configured max stay
/// (default 24 hours). Those exits are flagged as manual check-outs for dashboard/report counts.
/// </summary>
public sealed class StaleVisitAutoCheckOutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<StaleVisitAutoCheckOutWorker> _logger;

    public StaleVisitAutoCheckOutWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<StaleVisitAutoCheckOutWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Clamp(_config.GetValue("AutoCheckOut:PollMinutes", 15), 1, 120);
        // Brief delay so startup/seeding finishes before the first sweep.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Stale visit auto check-out sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var hours = Math.Clamp(_config.GetValue("AutoCheckOut:MaxStayHours", 24), 1, 168);
        using var scope = _scopeFactory.CreateScope();
        var visitors = scope.ServiceProvider.GetRequiredService<IVisitorService>();
        var count = await visitors.AutoCheckOutStaleVisitsAsync(TimeSpan.FromHours(hours), ct);
        if (count > 0)
            _logger.LogInformation("Auto-checked out {Count} visit(s) after {Hours}h without exit", count, hours);
    }
}
