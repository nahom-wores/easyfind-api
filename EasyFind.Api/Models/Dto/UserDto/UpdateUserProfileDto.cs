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
}
