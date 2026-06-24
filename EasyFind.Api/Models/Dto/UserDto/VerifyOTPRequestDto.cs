namespace EasyFind.Api.Models.Dto.UserDto
{
    public class VerifyOTPRequestDto
    {
        public string PhoneNumber { get; set; } 
        public string OTP { get; set; }
    }
}
