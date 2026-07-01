namespace EasyFind.Api.Services.IServices;

public interface IChapaWebhookVerifier
{
    bool IsValid(string payload, string? chapaSignature, string? xChapaSignature);
}