using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Models.Dto.UserDto
{
    public class LogInRequestDto
    {
        [Required]
        public string PhoneNumber { get; set; }
    }
}
