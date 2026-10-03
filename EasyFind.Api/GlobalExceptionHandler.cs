using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace EasyFind.Api;

// Last line of defence. Without this an unhandled exception returns the
// framework's ProblemDetails, so a client parsing ApiResponse gets a shape it
// does not recognise exactly when something has gone wrong.
//
// The message is deliberately generic: the detail goes to the log, not to the
// caller, so an exception cannot leak a connection string or a stack trace.
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception,
            "Unhandled exception on {Method} {Path}",
            context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var response = new ApiResponse { IsSuccess = false };
        response.Errors.Add("An unexpected error occurred. Please try again.");

        await context.Response.WriteAsJsonAsync(response, ct);
        return true;
    }
}
