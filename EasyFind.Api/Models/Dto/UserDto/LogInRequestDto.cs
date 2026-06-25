using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Models.Dto.UserDto
{
    public class LogInRequestDto
    {
        [Required]
        [Phone] // Basic validation
        [RegularExpression(@"^\+[1-9]\d{1,14}$", ErrorMessage = "Phone number must be in international format (e.g. +251...)")]
        public string PhoneNumber { get; set; }
    }
}
