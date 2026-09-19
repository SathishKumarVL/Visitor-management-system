using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Maintenance;

/// <summary>
/// Re-attempts SMTP delivery for unsent Email rows in NotificationOutbox.
/// Usage: dotnet run --project backend -- retry-emails [--take 20]
/// </summary>
public static class RetryEmailsCommand
{
    public const string Verb = "retry-emails";

    public static bool ShouldRun(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var take = 20;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--take", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var parsed))
                take = parsed;
        }

        using var scope = services.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var (attempted, sent) = await notifications.RetryUnsentEmailsAsync(take);
        Console.WriteLine($"Retried {attempted} unsent email(s); {sent} sent successfully.");
        return attempted > 0 && sent == 0 ? 1 : 0;
    }
}
