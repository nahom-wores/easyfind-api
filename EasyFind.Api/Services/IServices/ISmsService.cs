namespace EasyFind.Api.Services.IServices
{
    public interface ISmsService
    { 
        Task<bool> SendNotificationAsync(string toPhoneNumber, string message);
        Task<bool> SendOTPAsync(string toPhoneNumber, string otpCode);
    }
}
