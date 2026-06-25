using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Models.Dto.UserDto
{ 
    public class UserProfileDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        [EmailAddress]
        public string Email { get; set; }
        [Phone]
        public string PhoneNumber { get; set; }
        public string ProfilePictureUrl { get; set; }
        public bool IsVerified { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public DateTimeOffset CreatedAt { get; set; } 
    }
}
