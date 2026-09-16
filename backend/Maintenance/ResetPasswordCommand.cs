using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Maintenance;

/// <summary>
/// Operator command for recovering an account when nobody knows its password. Identity stores only a
/// one-way hash, so a lost password can be replaced but never read back.
///
/// Usage: dotnet run --project backend -- reset-password &lt;username&gt;
///
/// The new password is typed at the console and never appears in arguments, so it stays out of shell
/// history and process listings.
/// </summary>
public static class ResetPasswordCommand
{
    public const string Verb = "reset-password";

    public static bool ShouldRun(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"Usage: dotnet run --project backend -- {Verb} <username>");
            return 2;
        }

        var username = args[1].Trim();

        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Bypass the tenant filter: this runs with no request and therefore no tenant context.
        var user = await users.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.UserName == username);

        if (user is null)
        {
            Console.Error.WriteLine($"No user named '{username}'.");
            return 1;
        }

        var password = ReadHidden($"New password for {username}: ");
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Aborted: empty password.");
            return 1;
        }

        if (ReadHidden("Confirm password: ") != password)
        {
            Console.Error.WriteLine("Aborted: passwords did not match.");
            return 1;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"  - {error.Description}");
            return 1;
        }

        // Clear the things that would otherwise still block sign-in.
        user.IsActive = true;
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = "reset-password-cli";
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);

        var roles = await users.GetRolesAsync(user);
        Console.WriteLine($"Password reset for '{username}' (roles: {string.Join(", ", roles)}).");
        return 0;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);

        // Redirected input has no console key source, so fall back to a plain read.
        if (Console.IsInputRedirected)
        {
            var piped = Console.ReadLine() ?? "";
            Console.WriteLine();
            return piped;
        }

        var buffer = new Stack<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return new string(buffer.Reverse().ToArray());
                case ConsoleKey.Backspace when buffer.Count > 0:
                    buffer.Pop();
                    Console.Write("\b \b");
                    break;
                case ConsoleKey.Escape:
                    Console.WriteLine();
                    return "";
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Push(key.KeyChar);
                        Console.Write('*');
                    }
                    break;
            }
        }
    }
}
