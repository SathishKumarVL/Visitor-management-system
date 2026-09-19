using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Maintenance;

/// <summary>
/// Sends one SMTP test message. Usage: dotnet run --project backend -- send-test-email &lt;to&gt;
/// </summary>
public static class SendTestEmailCommand
{
    public const string Verb = "send-test-email";

    public static bool ShouldRun(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine($"Usage: dotnet run --project backend -- {Verb} <recipient@email>");
            return 2;
        }

        var to = args[1].Trim();
        using var scope = services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        // Demo/default tenant used by local DbSeeder.
        tenant.Set(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var (ok, message) = await notifications.SendTestEmailAsync(to);
        Console.WriteLine(message);
        return ok ? 0 : 1;
    }
}
