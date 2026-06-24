namespace EasyFind.Api.Models.Dto.UserDto
{
    public class TokenDto
    {
        public bool IsSuccess { get; set; } = true;
        public string Message { get; set; } 
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }
}
