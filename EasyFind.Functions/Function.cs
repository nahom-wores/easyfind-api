using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
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
        // Stop short of the Lambda timeout. If the invocation itself times out,
        // Lambda discards our response and the WHOLE batch returns to the queue —
        // including messages whose SMS already went out, which then get sent again.
        // Finishing early lets us report exactly what didn't run.
        var budget = context.RemainingTime - SafetyBuffer;
        using var cts = new CancellationTokenSource(budget > TimeSpan.Zero ? budget : TimeSpan.Zero);

        var failures = new List<SQSBatchResponse.BatchItemFailure>();
        foreach(var message in evnt.Records)
        {
            if (cts.IsCancellationRequested)
            {
                // Out of time: never started, so hand it straight back for retry.
                context.Logger.LogWarning($"Out of time, returning {message.MessageId} unprocessed");
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = message.MessageId });
                continue;
            }

            try
            {
                await ProcessMessageAsync(message, context, cts.Token);
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

    private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context,
        CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<NotificationMessage>(message.Body)
                       ?? throw new InvalidOperationException("Empty message body");

        switch (envelope.Type)
        {
            case NotificationTypes.PaymentSuccess:
                var payload = JsonSerializer.Deserialize<PaymentSuccessPayload>(envelope.Payload)!;
                var sms = await GetSmsAsync(ct);
                await sms.SendAsync(payload.PhoneNumber,
                    $"Payment of {payload.AmountEtb} ETB received. Your Yisru {payload.Tier} plan is now active. Thank you!", ct);
                context.Logger.LogInformation($"[payment_success] SMS sent for user {payload.UserId}");
                break;

            default:
                throw new InvalidOperationException($"Unknown message type: {envelope.Type}");
        }
        await Task.CompletedTask;
    }
    // How long before the Lambda timeout we stop starting new work.
    private static readonly TimeSpan SafetyBuffer = TimeSpan.FromSeconds(2);

    // Reused across warm invocations — created once per cold start. The default
    // 100s timeout would outlive the whole 15s invocation; 5s bounds one hung send.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private AfroMessageClient? _sms;

    // A secret holding ONLY the AfroMessage__* keys. Not the app-wide
    // yisru/prod/app, which also carries the DB connection string and JWT key.
    private static readonly string SmsSecretId =
        Environment.GetEnvironmentVariable("SMS_SECRET_ID") is { Length: > 0 } id ? id : "yisru/prod/afromessage";

    private async Task<AfroMessageClient> GetSmsAsync(CancellationToken ct)
    {
        if (_sms is not null) return _sms;   // already loaded on a warm start

        using var secrets = new AmazonSecretsManagerClient();
        var secret = await secrets.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = SmsSecretId }, ct);

        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(secret.SecretString)!;

        _sms = new AfroMessageClient(
            Http,
            values["AfroMessage__ApiToken"],
            values.GetValueOrDefault("AfroMessage__IdentifierId", ""),
            values.GetValueOrDefault("AfroMessage__SenderName", ""));

        return _sms;
    }
}