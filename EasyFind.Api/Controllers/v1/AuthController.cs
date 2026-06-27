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
    public class AuthController(IUserService userService, ITokenService tokenService) : ControllerBase
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
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorMessage.Add(userDto.ResultMessage);
                return BadRequest(response);
            }

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.Created;
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
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorMessage.Add($"{verificationDto.Message}");
                return BadRequest(response);
            }
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
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
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.IsSuccess = false;
                    response.ErrorMessage.Add("Invalid Token");
                    return BadRequest(response);
                }

                response.StatusCode = HttpStatusCode.OK;
                response.Result = tokenDtoResponse;
                return Ok(response);
            }
            else
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.BadRequest;
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
                response.StatusCode = HttpStatusCode.OK;
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
                response.StatusCode = HttpStatusCode.OK;
                response.Result = "Logged out successfully";

                return Ok(response);
            }
            catch (Exception ex)
            {
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessage.Add(ex.Message);
                return StatusCode(500, response);
            }
        }

        // Controllers/v1/AuthController.cs

        
        [HttpGet("me")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<ApiResponse>> GetCurrentUser()
        {
            ApiResponse response = new();
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                {
                    response.StatusCode = HttpStatusCode.Unauthorized;
                    response.ErrorMessage.Add("User not authenticated");
                    return Unauthorized(response);
                }

                var user = await userService.GetUserProfileAsync(userId);

                if (user == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    response.ErrorMessage.Add("User not found");
                    return NotFound(response);
                }
                
                response.Result = user;
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;

                return Ok(response);
            }
            catch (Exception ex)
            {
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessage.Add(ex.Message);
                return StatusCode(500, response);
            }
        }
        // POST api/v1/auth/assign-role
        [HttpPost("assign-role")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse>> AssignRole([FromBody] AssignRoleDto dto)
        {
            var response = new ApiResponse();
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                {
                    response.StatusCode = HttpStatusCode.Unauthorized;
                    response.ErrorMessage.Add("User not authenticated");
                    return Unauthorized(response);
                }

                var result = await userService.AssignRoleAsync(dto);

                if (!result)
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorMessage.Add("Role assignment failed.");
                    return BadRequest(response);
                }
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = $"Assigned role: {dto.Role}";
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessage.Add(ex.Message);
                return StatusCode(500, response);
            }
        }
    }
}
