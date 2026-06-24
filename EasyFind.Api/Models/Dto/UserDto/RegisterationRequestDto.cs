using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Models.Dto.UserDto
{
    public class RegisterationRequestDto
    {

        [Required]
        [RegularExpression(@"^\+251[97]\d{8}$", ErrorMessage = "Invalid Ethiopian phone number")]
        public string PhoneNumber { get; set; } // Format: +251911234567
        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }
}
