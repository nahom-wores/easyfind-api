namespace EasyFind.Api.Models.Dto.UserDtos
{
    public class LoginResponseDto
    {
        public string PhoneNumber { get; set; }
        public bool IsSuccess { get; set; }
        public string ResultMessage { get; set; }
    }
}
