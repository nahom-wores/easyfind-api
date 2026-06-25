namespace EasyFind.Api.Models.Dto.UserDto
{
    public class LoginResponseDto
    {
        public string PhoneNumber { get; set; }
        public bool IsSuccess { get; set; }
        public string ResultMessage { get; set; }
    }
}
