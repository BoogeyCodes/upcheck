using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Upcheck.Api.Common;

/// <summary>Maps application exceptions to RFC 7807 problem responses.</summary>
public sealed class ExceptionHandler(ILogger<ExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException ve => Problem(StatusCodes.Status400BadRequest, "Invalid request", ve.Message),
            NotFoundException nf => Problem(StatusCodes.Status404NotFound, "Not found", nf.Message),
            _ => null,
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
            return false;
        }

        http.Response.StatusCode = problem.Status!.Value;
        await http.Response.WriteAsJsonAsync(problem, problem.GetType(), ct);
        return true;
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
