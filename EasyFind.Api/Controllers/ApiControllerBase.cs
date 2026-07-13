using System.Net;
using System.Security.Claims;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers;


[ApiController]
public class ApiControllerBase : ControllerBase
{
    protected string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("User ID claim is missing.");

    // Map a Result<T> to a consistent ApiResponse + status code
    protected ActionResult<ApiResponse> HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(new ApiResponse { IsSuccess = true, Result = result.Value });

        var response = new ApiResponse { IsSuccess = false };
        response.Errors.Add(result.Error ?? "An error occurred.");
        return StatusCode((int)MapStatus(result.ErrorType), response);
    }
    // Same, for non-typed Result (delete, update with no return body)
    protected ActionResult<ApiResponse> HandleResult(Result result, string? successMessage = null)
    {
        if (result.IsSuccess)
            return Ok(new ApiResponse { IsSuccess = true, Result = successMessage });

        var response = new ApiResponse { IsSuccess = false };
        response.Errors.Add(result.Error ?? "An error occurred.");
        return StatusCode((int)MapStatus(result.ErrorType), response);
    }

   
    private static HttpStatusCode MapStatus(ErrorType type) => type switch
    {
        ErrorType.NotFound     => HttpStatusCode.NotFound,
        ErrorType.Conflict     => HttpStatusCode.Conflict,
        ErrorType.Validation   => HttpStatusCode.BadRequest,
        ErrorType.Forbidden    => HttpStatusCode.Forbidden,
        ErrorType.Unauthorized => HttpStatusCode.Unauthorized,
        _                      => HttpStatusCode.InternalServerError,
    };
}