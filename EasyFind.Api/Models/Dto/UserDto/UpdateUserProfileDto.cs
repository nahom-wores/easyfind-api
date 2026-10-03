using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Models.Dto.UserDto
{
    public class UpdateUserProfileDto
    {
        
        public string FirstName { get; set; } 
        public string LastName { get; set; }
        [EmailAddress]
        public string Email { get; set; }
        [Phone]
        public string PhoneNumber { get; set; }

    }

    public class UpdateUserPhoneNumberDto
    {
        public string PhoneNumber { get; set; }
    }
    public class PhoneNumberUpdateResponseDto
    {
        public bool IsSuccess { get; set; }
        public string ResultMessage { get; set; }
    }
    public class UserPhoneStatusDto
    {
        public bool HasPhoneNumber { get; set; }
        public string PhoneNumber { get; set; }
    }

    // What the avatar upload endpoint returns
    public class ProfilePictureDto
    {
        public string ProfilePictureUrl { get; set; } = string.Empty;
    }

    // Step 2 of the phone-change flow. The number is sent again (not held
    // server-side) because the OTP is bound to it.
    public class ConfirmPhoneChangeDto
    {
        [Phone]
        public string PhoneNumber { get; set; }
        public string OTP { get; set; }
    }
}
