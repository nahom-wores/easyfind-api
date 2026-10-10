using System.Text.Json;
using EasyFind.Api.Services.IServices;
using EasyFind.Contracts;

namespace EasyFind.Api.Services;

// Development only: writes the notification to the log instead of sending it
// to SQS. Locally there is no queue (the dev QueueUrl is a placeholder), and
// the SQS client can't even be created without AWS credentials — the app
// can't use an SSO login — which made every local payment callback crash
// before the subscription was activated.
//
// Registered in Program.cs under IsDevelopment() only, like ConsoleSmsService.
public class ConsoleNotificationPublisher(ILogger<ConsoleNotificationPublisher> logger) : INotificationPublisher
{
    public Task PublishPaymentSuccessAsync(PaymentSuccessPayload payload, CancellationToken ct = default)
    {
        logger.LogWarning("DEV NOTIFICATION: payment_success {Payload}", JsonSerializer.Serialize(payload));
        return Task.CompletedTask;
    }
}
