using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Security.Claims;
using EasyFind.Api.Features.Users.Commands;
using EasyFind.Api.Features.Users.Queries;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyFind.Api.Controllers.v1;

// Phone + OTP sign-in. There is no password and no separate registration:
// request-otp creates the account if the number is new, verify-otp returns tokens.
//
// NOTE: these endpoints do NOT use the Result/HandleResult convention used
// elsewhere. They return the handlers' own response DTOs (with their own
// IsSuccess/Message fields) and request-otp answers 201, because the mobile
// client depends on those exact bodies. Changing that is a client-coordinated
// break, not a tidy-up.
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class AuthController(ITokenService tokenService) : ApiControllerBase
{
    //[EnableRateLimiting("auth")]
    [HttpPost("request-otp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> SignIn(
        [FromBody] LogInRequestDto model,
        [FromServices] RequestOtpHandler handler)
    {
        var response = new ApiResponse();

        var userDto = await handler.HandleAsync(model);

        if (!userDto.IsSuccess)
        {
            response.IsSuccess = false;
            response.Errors.Add(userDto.ResultMessage);
            return BadRequest(response);
        }

        response.IsSuccess = true;
        response.Result = userDto;

        return StatusCode((int)HttpStatusCode.Created, response);
    }

    // [EnableRateLimiting("otp")]
    [HttpPost("verify-otp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> VerifyOtp(
        [FromBody] VerifyOTPRequestDto model,
        [FromServices] VerifyOtpHandler handler)
    {
        var response = new ApiResponse();
        var verificationDto = await handler.HandleAsync(model);
        if (!verificationDto.IsSuccess)
        {
            response.IsSuccess = false;
            response.Errors.Add($"{verificationDto.Message}");
            return BadRequest(response);
        }
        response.IsSuccess = true;
        response.Result = verificationDto;
        return Ok(response);
    }

    // Token refresh/revoke talk straight to ITokenService — they are token
    // plumbing, not a use case of their own.
    [HttpPost("refresh-token")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> RefreshAccessToken([FromBody] TokenDto tokenDto)
    {
        var response = new ApiResponse();
        if (ModelState.IsValid)
        {
            var tokenDtoResponse = await tokenService.RefreshAccessToken(tokenDto);
            if (tokenDtoResponse == null)
            {
                response.IsSuccess = false;
                response.Errors.Add("Invalid Token");
                return BadRequest(response);
            }

            response.Result = tokenDtoResponse;
            return Ok(response);
        }
        else
        {
            response.IsSuccess = false;
            response.Result = "Invalid Input";
            return BadRequest(response);
        }
    }

    [HttpPost("revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> RevokeRefreshToken([FromBody] TokenDto tokenDto)
    {
        var response = new ApiResponse();
        if (ModelState.IsValid)
        {
            await tokenService.RevokeRefreshToken(tokenDto);
            return Ok(response);
        }

        response.IsSuccess = false;
        response.Result = "Invalid Input";
        return BadRequest(response);
    }

    /// <summary>
    /// Logout (revoke refresh token)
    /// POST /api/v1/auth/logout
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse>> Logout(
        [FromBody] TokenDto tokenDto,
        [FromServices] LogoutHandler handler)
    {
        var response = new ApiResponse();
        try
        {
            await handler.HandleAsync(tokenDto);

            response.IsSuccess = true;
            response.Result = "Logged out successfully";

            return Ok(response);
        }
        catch (Exception ex)
        {
            response.Errors.Add(ex.Message);
            return StatusCode(500, response);
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> GetCurrentUser(
        [FromServices] GetCurrentUserHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return HandleResult(await handler.HandleAsync(userId, ct));
    }

    // PUT api/v1/auth/me — edit name. Phone and email are not editable here;
    // see UpdateCurrentUserHandler for why.
    [HttpPut("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> UpdateCurrentUser(
        [FromBody] UpdateUserProfileDto dto,
        [FromServices] UpdateCurrentUserHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return HandleResult(await handler.HandleAsync(userId, dto, ct));
    }

    // POST api/v1/auth/me/picture — replace the avatar
    [HttpPost("me/picture")]
    [Authorize]
    [RequestSizeLimit(6 * 1024 * 1024)]   // 6MB ceiling at the framework level
    public async Task<ActionResult<ApiResponse>> UpdateProfilePicture(
        IFormFile image,
        [FromServices] UpdateProfilePictureHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (image is null) return HandleResult(Result<object>.Validation("No image provided."));

        return HandleResult(await handler.HandleAsync(userId, image, ct));
    }

    // ── Changing the login phone number (two steps) ──────────────────────────
    // The number IS the login identifier, so it can only move once the user has
    // proved they hold the new one. Step 1 texts a code to the new number;
    // step 2 sends that code back with the same number.

    // POST api/v1/auth/me/phone/request-otp
    [HttpPost("me/phone/request-otp")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> RequestPhoneChange(
        [FromBody] UpdateUserPhoneNumberDto dto,
        [FromServices] RequestPhoneChangeHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return HandleResult(await handler.HandleAsync(userId, dto, ct),
            "Verification code sent to the new number.");
    }

    // POST api/v1/auth/me/phone/confirm
    [HttpPost("me/phone/confirm")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> ConfirmPhoneChange(
        [FromBody] ConfirmPhoneChangeDto dto,
        [FromServices] ConfirmPhoneChangeHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return HandleResult(await handler.HandleAsync(userId, dto, ct));
    }

    // POST api/v1/auth/assign-role
    [HttpPost("assign-role")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse>> AssignRole(
        [FromBody] AssignRoleDto dto,
        [FromServices] AssignRoleHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(dto, ct), $"Assigned role: {dto.Role}");
}
