using Microsoft.AspNetCore.Identity;

namespace EasyFind.Api.Models.Auth
{
    public class ApplicationUser : IdentityUser
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }

        // for Google login
        public string GoogleId { get; set; }          // Store Google user ID
        public string ProfilePictureUrl { get; set; } // Store Google profile photo
        public string LoginProvider { get; set; }     // "Google", "Phone", "telegram", "Apple ID"
        
        public bool IsSuspended { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        
    }
}
