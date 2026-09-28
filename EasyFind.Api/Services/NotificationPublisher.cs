using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using EasyFind.Api.Services.IServices;
using EasyFind.Contracts;

namespace EasyFind.Api.Services;

public class NotificationPublisher(IAmazonSQS sqs) : INotificationPublisher
{
    private readonly IAmazonSQS _sqs = sqs;
    private const string QueueUrl =
        "https://sqs.eu-central-1.amazonaws.com/454252678518/yisru-notifications";

    public async Task PublishPaymentSuccessAsync(
        PaymentSuccessPayload payload, CancellationToken ct = default)
    {
        var message = new NotificationMessage
        {
            Type = NotificationTypes.PaymentSuccess,
            Version = 1,
            IdempotencyKey = $"{NotificationTypes.PaymentSuccess}:{payload.TxRef}",
            Payload = JsonSerializer.Serialize(payload),
        };

        await _sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = QueueUrl,
            MessageBody = JsonSerializer.Serialize(message),
        }, ct);
    }
}