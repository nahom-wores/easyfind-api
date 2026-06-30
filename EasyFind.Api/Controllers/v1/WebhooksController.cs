using System.Text.Json;
using Asp.Versioning;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/webhooks")]
[ApiController]
[ApiVersion("1.0")]
public class WebhooksController(
    ISubscriptionService subscriptionService,
    IConfiguration config,
    ILogger<WebhooksController> logger) : ControllerBase
{
    [HttpPost("chapa")]
    [AllowAnonymous] // Chapa is not an authenticated user
    public async Task<IActionResult> ChapaWebhook(CancellationToken ct)
    {
        // 1. Read the raw body
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);

        // 2. Verify the signature — confirms this is really from Chapa
        var signature = Request.Headers["Chapa-Signature"].FirstOrDefault()
                        ?? Request.Headers["x-chapa-signature"].FirstOrDefault();

        if (!VerifySignature(rawBody, signature))
        {
            logger.LogWarning("Chapa webhook with invalid signature.");
            return Unauthorized();
        }

        // 3. Extract tx_ref from the payload
        string? txRef;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            txRef = doc.RootElement.TryGetProperty("tx_ref", out var t) ? t.GetString() : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not parse Chapa webhook body.");
            return BadRequest();
        }

        if (string.IsNullOrEmpty(txRef))
        {
            logger.LogWarning("Chapa webhook missing tx_ref.");
            return BadRequest();
        }

        // 4. Process (idempotent inside the service)
        await subscriptionService.HandleWebhookAsync(txRef, ct);

        // Always 200 so Chapa stops retrying — our internal state is set correctly
        return Ok();
    }

    private bool VerifySignature(string payload, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        var secret = config["Chapa:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            // If no webhook secret configured yet, log loudly.
            logger.LogError("Chapa:WebhookSecret not configured — cannot verify webhook.");
            return false;
        }

        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();

        // Constant-time comparison to prevent timing attacks
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(computed),
            System.Text.Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
    }
}