using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteAsync(context, HttpStatusCode.Forbidden, ex.Message);
        }
        catch (ConcurrencyConflictException ex)
        {
            await WriteAsync(context, HttpStatusCode.Conflict, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Safety net if a write path forgets to translate the EF concurrency failure.
            await WriteAsync(context, HttpStatusCode.Conflict, ConcurrencyConflictException.DefaultVisitMessage);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database update failed");
            var detail = ex.GetBaseException().Message;
            var message = context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true
                ? detail
                : "Could not save changes. Please check the entered details and try again.";
            await WriteAsync(context, HttpStatusCode.BadRequest, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            var message = context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true
                ? ex.GetBaseException().Message
                : "An unexpected server error occurred.";
            await WriteAsync(context, HttpStatusCode.InternalServerError, message);
        }
    }

    private static async Task WriteAsync(HttpContext context, HttpStatusCode code, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)code;
        var payload = new ApiResponse<object>(false, null, message);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
