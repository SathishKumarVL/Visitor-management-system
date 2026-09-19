namespace Tiaano.Vms.Api.Models;

/// <summary>
/// Raised when an optimistic concurrency token (SQL Server rowversion) rejects a Visit lifecycle write.
/// Mapped to HTTP 409 by controllers and <see cref="Middleware.ExceptionMiddleware"/>.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public const string DefaultVisitMessage =
        "The visit was modified by another user. Refresh the visit and try again.";

    public ConcurrencyConflictException(string? message = null)
        : base(string.IsNullOrWhiteSpace(message) ? DefaultVisitMessage : message)
    {
    }
}
