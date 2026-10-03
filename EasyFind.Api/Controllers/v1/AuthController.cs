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
    // Two limits guard this: per client IP here, and per destination phone
    // number in IOtpThrottle. Both are needed — the caller chooses the phone
    // number, so a per-phone limit alone is bypassed by rotating numbers, while
    // a per-IP limit alone lets a botnet bomb one victim.
    [EnableRateLimiting("otp-send")]
    [HttpPost("request-otp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse>> SignIn(
        [FromBody] LogInRequestDto model,
        [FromServices] RequestOtpHandler handler,
        [FromServices] IOtpThrottle throttle,
        CancellationToken ct)
    {
        var response = new ApiResponse();

        var gate = await throttle.TryConsumeSendAsync(model.PhoneNumber, ct);
        if (!gate.Allowed)
            return TooManyRequests(gate.RetryAfter,
                "Too many codes requested for this number. Please try again later.");

        var userDto = await handler.HandleAsync(new RequestOtpCommand(model));

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

    // A 6-digit code with unlimited guesses is an oracle, so failures are
    // counted per phone number and lock it out for a cooldown.
    [EnableRateLimiting("otp-verify")]
    [HttpPost("verify-otp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse>> VerifyOtp(
        [FromBody] VerifyOTPRequestDto model,
        [FromServices] VerifyOtpHandler handler,
        [FromServices] IOtpThrottle throttle,
        CancellationToken ct)
    {
        var response = new ApiResponse();

        // Checked before the code is examined, so a locked-out number is refused
        // even when the code offered happens to be right.
        var gate = await throttle.CheckVerifyAllowedAsync(model.PhoneNumber, ct);
        if (!gate.Allowed)
            return TooManyRequests(gate.RetryAfter,
                "Too many incorrect codes. Please try again later.");

        var verificationDto = await handler.HandleAsync(new VerifyOtpCommand(model));
        if (!verificationDto.IsSuccess)
        {
            await throttle.RecordFailedVerificationAsync(model.PhoneNumber, ct);

            response.IsSuccess = false;
            response.Errors.Add($"{verificationDto.Message}");
            return BadRequest(response);
        }

        // Signed in successfully — clear any strikes against the number.
        await throttle.ResetVerificationFailuresAsync(model.PhoneNumber, ct);

        response.IsSuccess = true;
        response.Result = verificationDto;
        return Ok(response);
    }

    // Token refresh/revoke talk straight to ITokenService — they are token
    // plumbing, not a use case of their own.
    // Exchanges a refresh token for a new pair. Deliberately NOT [Authorize]:
    // it is called precisely when the access token has expired.
    //
    // The refresh token is SINGLE-USE — a success rotates it and invalidates the
    // one presented. Re-presenting a consumed token revokes the entire chain and
    // signs the user out, so a client must serialise its refreshes (one in
    // flight at a time) and must persist the new refresh token before using it.
    //
    // FIXED CONTRACT: this used to answer 200 for both outcomes. The failure path
    // returned `new TokenDto()`, whose IsSuccess defaulted to true, while the
    // success path never set the envelope's IsSuccess at all — so a client could
    // only tell the two apart by testing whether Result.AccessToken was null.
    // Success is now 200 with IsSuccess true; every failure is 401.
    //
    // The success BODY is unchanged, so a client still keying off
    // Result.AccessToken keeps working — which matters because mobile users
    // update on their own schedule and old versions will hit this for months.
    [HttpPost("refresh-token")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse>> RefreshAccessToken([FromBody] TokenDto tokenDto)
    {
        var response = new ApiResponse();

        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(tokenDto?.RefreshToken))
        {
            response.IsSuccess = false;
            response.Errors.Add("A refresh token is required.");
            return BadRequest(response);
        }

        var tokenDtoResponse = await tokenService.RefreshAccessToken(tokenDto);

        // Belt and braces: treat a missing access token as failure even if the
        // flag ever says otherwise. These two cannot disagree today, and if they
        // ever do, refusing is the safe direction.
        if (tokenDtoResponse is not { IsSuccess: true }
            || string.IsNullOrEmpty(tokenDtoResponse.AccessToken))
        {
            response.IsSuccess = false;
            response.Errors.Add(string.IsNullOrWhiteSpace(tokenDtoResponse?.Message)
                ? "Invalid or expired refresh token. Please sign in again."
                : tokenDtoResponse.Message);

            return Unauthorized(response);
        }

        response.IsSuccess = true;
        response.Result = tokenDtoResponse;
        return Ok(response);
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
            await handler.HandleAsync(new LogoutCommand(tokenDto));

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

        return HandleResult(await handler.HandleAsync(new GetCurrentUserQuery(userId), ct));
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

        return HandleResult(await handler.HandleAsync(new UpdateCurrentUserCommand(userId, dto), ct));
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

        return HandleResult(await handler.HandleAsync(new UpdateProfilePictureCommand(userId, image), ct));
    }

    // ── Changing the login phone number (two steps) ──────────────────────────
    // The number IS the login identifier, so it can only move once the user has
    // proved they hold the new one. Step 1 texts a code to the new number;
    // step 2 sends that code back with the same number.

    // POST api/v1/auth/me/phone/request-otp
    // Throttled per destination number as well as per IP: this endpoint also
    // sends billable SMS to a number the caller chooses.
    [EnableRateLimiting("otp-send")]
    [HttpPost("me/phone/request-otp")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse>> RequestPhoneChange(
        [FromBody] UpdateUserPhoneNumberDto dto,
        [FromServices] RequestPhoneChangeHandler handler,
        [FromServices] IOtpThrottle throttle,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var gate = await throttle.TryConsumeSendAsync(dto.PhoneNumber, ct);
        if (!gate.Allowed)
            return TooManyRequests(gate.RetryAfter,
                "Too many codes requested for this number. Please try again later.");

        return HandleResult(await handler.HandleAsync(new RequestPhoneChangeCommand(userId, dto), ct),
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

        return HandleResult(await handler.HandleAsync(new ConfirmPhoneChangeCommand(userId, dto), ct));
    }

    // POST api/v1/auth/assign-role
    [HttpPost("assign-role")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse>> AssignRole(
        [FromBody] AssignRoleDto dto,
        [FromServices] AssignRoleHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new AssignRoleCommand(dto), ct), $"Assigned role: {dto.Role}");
}
