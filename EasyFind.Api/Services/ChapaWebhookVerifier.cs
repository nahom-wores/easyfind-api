using System.Security.Cryptography;
using System.Text;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services;

public class ChapaWebhookVerifier(IConfiguration config, ILogger<ChapaWebhookVerifier> logger) 
    : IChapaWebhookVerifier
{
    public bool IsValid(string payload, string chapaSignature, string xChapaSignature)
    {
        var secretKey = config["Chapa:SecretKey"];
        if (string.IsNullOrEmpty(secretKey))
        {
            logger.LogError("Chapa:SecretKey not configured — cannot verify webhook.");
            return false;
        }

        // x-chapa-signature = HMAC of the PAYLOAD, keyed with the secret key
        if (!string.IsNullOrEmpty(xChapaSignature)
            && HmacMatches(payload, secretKey, xChapaSignature))
            return true;

        // chapa-signature = HMAC of the SECRET KEY itself, keyed with the secret key
        if (!string.IsNullOrEmpty(chapaSignature)
            && HmacMatches(secretKey, secretKey, chapaSignature))
            return true;

        return false;
    }
    private static bool HmacMatches(string message, string key, string providedHex)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(providedHex.Trim().ToLowerInvariant()));
    }
}