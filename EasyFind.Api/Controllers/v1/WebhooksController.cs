using System.Text;
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
    IChapaWebhookVerifier webhookVerifier,
    ILogger<WebhooksController> logger) : ControllerBase
{
    [HttpPost("chapa")]
    [AllowAnonymous]
    public async Task<IActionResult> ChapaWebhook(CancellationToken ct)
    {
        try
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = await reader.ReadToEndAsync(ct);
            }
            logger.LogInformation(rawBody);
            var chapaSig = Request.Headers["chapa-signature"].FirstOrDefault();
            var xChapaSig = Request.Headers["x-chapa-signature"].FirstOrDefault();

            if (!webhookVerifier.IsValid(rawBody, chapaSig, xChapaSig))
            {
                logger.LogWarning("Chapa webhook failed signature verification.");
                return Unauthorized();
            }

            string? txRef, status, eventType;
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;
                txRef = root.TryGetProperty("tx_ref", out var t) ? t.GetString() : null;
                status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
                eventType = root.TryGetProperty("event", out var e) ? e.GetString() : null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not parse Chapa webhook body.");
                return BadRequest();
            }

            if (string.IsNullOrEmpty(txRef))
            {
                logger.LogWarning("Chapa webhook missing tx_ref.");
                return Ok();
            }

            if (eventType == "charge.success" || status == "success")
                await subscriptionService.HandleWebhookAsync(txRef, ct);
            else
                logger.LogInformation("Chapa webhook {TxRef} status {Status}, no action.", txRef, status);

            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook blew up: {Message}", ex.Message);
            throw;
        }
       
    }
    
    // GET callback — Chapa hits this right after payment with query params.
    // Redundant with the webhook by design; HandleWebhookAsync is idempotent.
    [HttpGet("chapa/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> ChapaCallback(
        [FromQuery(Name = "trx_ref")] string? trxRef,
        [FromQuery(Name = "tx_ref")] string? txRef,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        // Chapa's docs show "trx_ref" on the callback but "tx_ref" elsewhere — accept either
        var reference = trxRef ?? txRef;

        if (string.IsNullOrEmpty(reference))
        {
            logger.LogWarning("Chapa callback missing reference.");
            return Ok();
        }

        logger.LogInformation("Chapa callback for {Ref}, status {Status}", reference, status);

        // Verify-in-service is the real gate; we don't trust this status blindly.
        // Safe to call even if webhook already processed — idempotent.
        await subscriptionService.HandleWebhookAsync(reference, ct);

        return Ok();
    }
    
}