using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using EasyFind.Contracts;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EasyFind.Functions;

public class Function
{
    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    public Function()
    {

    }


    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an SQS event object and can be used 
    /// to respond to SQS messages.
    /// </summary>
    /// <param name="evnt">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns></returns>
    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        var failures = new List<SQSBatchResponse.BatchItemFailure>();
        foreach(var message in evnt.Records)
        {
            try
            {
                await ProcessMessageAsync(message, context);
            }
            catch (Exception e)
            {
                context.Logger.LogError($"Failed {message.MessageId}: {e.Message}");
                // Report ONLY this message as failed — the rest of the batch still succeeds
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = message.MessageId });
            }
        }
        return new SQSBatchResponse { BatchItemFailures = failures };
    }

    private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
    {
        var envelope = JsonSerializer.Deserialize<NotificationMessage>(message.Body)
                       ?? throw new InvalidOperationException("Empty message body");

        switch (envelope.Type)
        {
            case "payment_success":
                var payload = JsonSerializer.Deserialize<PaymentSuccessPayload>(envelope.Payload)!;
                context.Logger.LogInformation(
                    $"[payment_success v{envelope.Version}] user={payload.UserId} phone={payload.PhoneNumber} amount={payload.AmountEtb}");
                // Step later: actually send SMS + email here
                break;

            default:
                throw new InvalidOperationException($"Unknown message type: {envelope.Type}");
        }
        await Task.CompletedTask;
    }
}