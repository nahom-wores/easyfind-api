
using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services
{
    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<UserService> _logger;
        private readonly ISmsService _smsService;
        private readonly ITokenService _tokenService;
        private readonly IImageService _imageService;

        public UserService(ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IConfiguration configuration,
            ILogger<UserService> logger,
            ISmsService smsService,
            ITokenService tokenService,
            IImageService imageService)
        {
            this._db = db;
            this._userManager = userManager;
            this._roleManager = roleManager;
            this._logger = logger;
            this._smsService = smsService;
            _tokenService = tokenService;
            _imageService = imageService;
        }

        #region User Profile
        public async Task<UserProfileDto> GetUserProfileAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return null;

            var userRoles = await _userManager.GetRolesAsync(user);
    
            return new UserProfileDto
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                ProfilePictureUrl = user.ProfilePictureUrl,
                IsVerified = user.PhoneNumberConfirmed || user.EmailConfirmed,
                CreatedAt = user.CreatedAt,
                Roles = userRoles.ToList() 
            };
        }

        // To-DO
        public async Task<UserProfileDto> UpdateUserProfileAsync(string userId,
            UpdateUserProfileDto updateUserProfileDto)
        {
            var user = await _userManager.FindByIdAsync(userId);
            user.FirstName = updateUserProfileDto.FirstName;
            user.LastName = updateUserProfileDto.LastName;
            user.PhoneNumber = updateUserProfileDto.PhoneNumber;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return null;
            }

            return new UserProfileDto
            {
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                CreatedAt = user.CreatedAt,
                FirstName = user.FirstName,
                LastName = user.LastName,
                ProfilePictureUrl = user.ProfilePictureUrl,
            };
        }

        public async Task<PhoneNumberUpdateResponseDto> UpdateUserPhoneNumberAsync(string userId,
            UpdateUserPhoneNumberDto updateUserPhoneNumberDto)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return new PhoneNumberUpdateResponseDto
                {
                    IsSuccess = false,
                    ResultMessage = "User Not Found"
                };
            }

            var exist = await _db.ApplicationUsers
                .FirstOrDefaultAsync(x => x.PhoneNumber == updateUserPhoneNumberDto.PhoneNumber &&
                                          x.Id != userId);
            if (exist != null)
            {
                return new PhoneNumberUpdateResponseDto
                {
                    IsSuccess = false,
                    ResultMessage = "Phone Number Already Exists"
                };
            }
            var result = await _userManager
                .SetPhoneNumberAsync(user, updateUserPhoneNumberDto.PhoneNumber);
            // user.PhoneNumber = updateUserPhoneNumberDto.PhoneNumber;
            // var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return new PhoneNumberUpdateResponseDto
                {
                    IsSuccess = false,
                    ResultMessage = "Error Updating Phone Number"
                };
            }

            return new PhoneNumberUpdateResponseDto
            {
                IsSuccess = true,
                ResultMessage = "Phone Number Updated Successfully"
            };
        }

        #endregion

        public bool IsUniqueUser(string username)
        {
            var user = _db.ApplicationUsers
                .FirstOrDefault(x => x.PhoneNumber == username || x.Email == username || x.UserName == username);
            return user == null;
        }

        public async Task<UserPhoneStatusDto> CheckUserPhoneStatusAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return new UserPhoneStatusDto
                {
                    PhoneNumber = null,
                    HasPhoneNumber = false
                };
            }
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            
            return new UserPhoneStatusDto
            {
                PhoneNumber = phoneNumber,
                HasPhoneNumber = !string.IsNullOrEmpty(phoneNumber)
            };
        }

        public async Task<LoginResponseDto> Login(LogInRequestDto loginRequestDTO)
        {
            var user = await _userManager.FindByNameAsync(loginRequestDTO.PhoneNumber);

            // Fix: Check if user exists before proceeding
            if (user == null)
            {
                return new LoginResponseDto
                {
                    IsSuccess = false,
                    ResultMessage = "User not found."
                };
            }

            var otp = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);
            var sendOtpResult =
                await _smsService.SendOTPAsync(user.PhoneNumber, otp);
            if (!sendOtpResult)
            {
                return new LoginResponseDto
                {
                    IsSuccess = false,
                    PhoneNumber = user.UserName,
                    ResultMessage = "Error Sending OTP"
                };
            }

            return new LoginResponseDto
            {
                IsSuccess = true,
                PhoneNumber = user.UserName,
                ResultMessage = "OTP Message sent Successfully"
            };
        }


        public async Task<LoginResponseDto> Register(LogInRequestDto registerationRequestDTO)
        {
            var user = new ApplicationUser
            {
                UserName = registerationRequestDTO.PhoneNumber,
                PhoneNumber = registerationRequestDTO.PhoneNumber,
                TwoFactorEnabled = true,
            };

            try
            {

                var result = await _userManager.CreateAsync(user);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogError($"User creation failed: {errors}");
                    return new LoginResponseDto
                    {
                        ResultMessage = errors,
                        IsSuccess = false,
                        PhoneNumber = user.UserName,
                    };
                }


                // Fix: Use AddToRoleAsync instead of manual DB context manipulation
                await _userManager.AddToRoleAsync(user, AppRoles.User);

                // Generate and send OTP
                var otp = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);

                // Fix: Consistent brand messaging (MedaHub)
                var sendOtpResult =
                    await _smsService.SendOTPAsync(user.PhoneNumber, otp);

                if (!sendOtpResult)
                {
                    return new LoginResponseDto
                    {
                        IsSuccess = false,
                        PhoneNumber = user.UserName,
                        ResultMessage = "Error Sending OTP"
                    };
                }

                return new LoginResponseDto
                {
                    IsSuccess = true,
                    PhoneNumber = user.UserName,
                    ResultMessage = "OTP Message sent Successfully"
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occurred during user registration");
                return new LoginResponseDto
                {
                    IsSuccess = false,
                    ResultMessage = "An internal error occurred during registration."
                };
            }
        }


        public async Task<TokenDto> VerifyLogIn(VerifyOTPRequestDto verifyOTPRequestDTO)
        {
            try
            {
                var user = await _db.ApplicationUsers.FirstOrDefaultAsync(x =>
                    x.PhoneNumber == verifyOTPRequestDTO.PhoneNumber);
                if (user == null)
                {
                    return new TokenDto()
                    {
                        AccessToken = string.Empty,
                        IsSuccess = false,
                        Message = "User Not Found!"
                    };
                }

                // Validate OTP
                var isValidOTP = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider,
                    verifyOTPRequestDTO.OTP);
                if (!isValidOTP)
                {
                    return new TokenDto()
                    {
                        AccessToken = string.Empty,
                        IsSuccess = false,
                        Message = "Invalid OTP"
                    };
                }

                // Generate Token
                var jwtTokenId = $"JIT{Guid.NewGuid()}";
                var accessToken = await _tokenService.GenerteAccessToken(user, jwtTokenId);
                var refreshToken = await _tokenService.CreateNewRefreshToken(user.Id, jwtTokenId);

                // Update User Status
                if (!await _userManager.IsPhoneNumberConfirmedAsync(user))
                {
                    user.PhoneNumberConfirmed = true;
                    await _userManager.UpdateAsync(user);
                }

                // Ensure user has the default role
                var role = await _roleManager.FindByNameAsync(AppRoles.User);
                var existingUserRole = await _db.UserRoles
                    .FirstOrDefaultAsync(x => x.UserId == user.Id && x.RoleId == role.Id);
                if (existingUserRole == null && role != null)
                {
                    var userRole = new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id };
                    await _db.UserRoles.AddAsync(userRole);
                    await _db.SaveChangesAsync();
                    await _userManager.UpdateSecurityStampAsync(user);
                }
                
                //return success
                return new TokenDto
                {
                    IsSuccess = true,
                    Message = "Verification Successful",
                    AccessToken = accessToken,
                    RefreshToken = refreshToken
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying OTP");
                return new TokenDto
                {
                    IsSuccess = false,
                    Message = "Verification failed"
                };
            }

        }

        

        public async Task RevokeRefreshToken(TokenDto tokenDto)
        {
            if (string.IsNullOrWhiteSpace(tokenDto.RefreshToken))
            {
                throw new ArgumentException("Refresh token is required.");
            }
            // invalidates the entire chain
            await _tokenService.RevokeRefreshToken(tokenDto);
        }
        public async Task<string> UpdateProfilePictureAsync(string userId, IFormFile image)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return null;

            // 1. Store the old image URL for cleanup
            var oldImageUrl = user.ProfilePictureUrl;

            // 2. Upload the new image
            var folder = $"users/{userId}";
            var newImageUrl = await _imageService.UploadImageAsync(image, folder);

            // 3. Update the user record
            user.ProfilePictureUrl = newImageUrl;
            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded) return null;
            
            // 4. Cleanup: Delete the old image if it exists
            if (!string.IsNullOrEmpty(oldImageUrl))
            {
                await _imageService.DeleteImageAsync(oldImageUrl);
            }
            return newImageUrl;

        }

        public async Task<bool> AssignRoleAsync(AssignRoleDto assignRoleDto)
        {
            var user = await _userManager.FindByIdAsync(assignRoleDto.UserId);
            if (user == null) return false;

            if (await _userManager.IsInRoleAsync(user, assignRoleDto.Role)) return true;

            var result = await _userManager.AddToRoleAsync(user, assignRoleDto.Role);
            if (result.Succeeded)
            {
                await _userManager.UpdateSecurityStampAsync(user); // Important!
                return true;
            }
            return false;
        }
    }
}
