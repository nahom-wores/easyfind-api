using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services;

// Development only: "sends" an SMS by writing it to the log, so the OTP sign-in
// can be tested locally without spending AfroMessage credit or needing a real
// phone. Nothing is bypassed — the code is generated, throttled and verified
// exactly as in production; you just read it from the console.
//
// Registered in Program.cs under IsDevelopment() and nowhere else. Never
// register it for any other environment: it would print every user's sign-in
// code into the logs.
public class ConsoleSmsService(ILogger<ConsoleSmsService> logger) : ISmsService
{
    public Task<bool> SendOTPAsync(string toPhoneNumber, string otpCode)
    {
        logger.LogWarning("DEV SMS: OTP for {Phone} is {Code}", toPhoneNumber, otpCode);
        return Task.FromResult(true);
    }

    public Task<bool> SendNotificationAsync(string toPhoneNumber, string message)
    {
        logger.LogWarning("DEV SMS to {Phone}: {Message}", toPhoneNumber, message);
        return Task.FromResult(true);
    }
}
