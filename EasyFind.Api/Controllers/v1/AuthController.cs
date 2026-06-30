using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Security.Claims;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyFind.Api.Controllers.v1
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class AuthController(IUserService userService, ITokenService tokenService) : ApiControllerBase
    {
        
        //[EnableRateLimiting("auth")]
        [HttpPost("request-otp")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ApiResponse>> SignIn([FromBody] LogInRequestDto model)
        {
            var response = new ApiResponse();
            
            var userDto = await userService.RequestOtpAsync(model);

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

        

        [EnableRateLimiting("otp")]
        [HttpPost("verify-otp")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ApiResponse>> VerifyOtp([FromBody] VerifyOTPRequestDto model)
        {
            var response = new ApiResponse();
            var verificationDto = await userService.VerifyLogIn(model);
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
        public async Task<ActionResult<ApiResponse>> Logout([FromBody] TokenDto tokenDto)
        {
            var response = new ApiResponse();
            try
            {
                await userService.RevokeRefreshToken(tokenDto);

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

        // Controllers/v1/AuthController.cs

        
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<ApiResponse>> GetCurrentUser(CancellationToken ct)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await userService.GetUserProfileAsync(userId, ct);
            return HandleResult(result);
        }
        // POST api/v1/auth/assign-role
        [HttpPost("assign-role")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse>> AssignRole([FromBody] AssignRoleDto dto, CancellationToken ct)
        {
            var result = await userService.AssignRoleAsync(dto, ct);
            return HandleResult(result, $"Assigned role: {dto.Role}");
        }
    }
}
