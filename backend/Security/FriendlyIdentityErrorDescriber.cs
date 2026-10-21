using Microsoft.AspNetCore.Identity;

namespace Tiaano.Vms.Api.Security;

/// <summary>
/// Identity's default "non alphanumeric" wording reads as if the password must be alphanumeric;
/// these messages name each missing rule in plain words.
/// </summary>
public sealed class FriendlyIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"Password must be at least {length} characters long.",
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "Password must include at least one special character, such as ! @ # $ % & *.",
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "Password must include at least one number (0-9).",
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "Password must include at least one lowercase letter (a-z).",
    };

    public override IdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "Password must include at least one uppercase letter (A-Z).",
    };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = nameof(PasswordRequiresUniqueChars),
        Description = $"Password must use at least {uniqueChars} different characters.",
    };

    public override IdentityError DuplicateEmail(string email) => new()
    {
        Code = nameof(DuplicateEmail),
        Description = $"Another login already uses the email '{email}'. Each login needs its own email: change it on the employee or on the other login.",
    };
}
