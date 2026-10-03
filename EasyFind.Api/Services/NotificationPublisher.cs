using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using EasyFind.Api.Models.Options;
using EasyFind.Api.Services.IServices;
using EasyFind.Contracts;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Services;

public class NotificationPublisher(IAmazonSQS sqs, IOptions<NotificationOptions> options) : INotificationPublisher
{
    private readonly IAmazonSQS _sqs = sqs;
    private readonly string _queueUrl = options.Value.QueueUrl;

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
            QueueUrl = _queueUrl,
            MessageBody = JsonSerializer.Serialize(message),
        }, ct);
    }
}
