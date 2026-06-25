using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Models.Dto.UserDto;

namespace EasyFind.Api.Services.IServices;

public interface IUserService
{
    bool IsUniqueUser(string username);
    Task<UserPhoneStatusDto> CheckUserPhoneStatusAsync(string userId);
    Task<LoginResponseDto> Login(LogInRequestDto loginRequestDTO);
    Task<LoginResponseDto> Register(LogInRequestDto registerationRequestDTO);
    Task<TokenDto> VerifyLogIn(VerifyOTPRequestDto verifyOTPRequestDTO);
    Task<UserProfileDto> GetUserProfileAsync(string userId);
    Task<UserProfileDto> UpdateUserProfileAsync(string userId, UpdateUserProfileDto updateUserProfileDto);
    Task<PhoneNumberUpdateResponseDto> UpdateUserPhoneNumberAsync(string userId, UpdateUserPhoneNumberDto updateUserPhoneNumberDto);
    Task RevokeRefreshToken(TokenDto tokenDto);
    Task<string> UpdateProfilePictureAsync(string userId, IFormFile image);
    Task<bool> AssignRoleAsync(AssignRoleDto assignRoleDto);
}