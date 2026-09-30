# EasyFind / yisru — operational docs

## Notifications pipeline

User-facing notifications (SMS now, email later) are sent **asynchronously**. The API does not call the
SMS gateway on the request path: it drops a message on an SQS queue and a Lambda sends it. This keeps a
slow or failing gateway from failing the request that triggered the notification (e.g. a Chapa webhook), and
gives failed sends automatic retries plus a dead-letter queue.

> **Status (2026-09-28):** the pipeline is deployed but **not yet called** from the payment flow.
> `INotificationPublisher` is registered but nothing calls it. See [Known limitations](#known-limitations)
> before wiring it in.

### Architecture

```
 ┌──────────────────────── ECS: yisru-service (EasyFind.Api) ────────────────────────┐
 │                                                                                   │
 │  ProcessChapaPaymentHandler ──(not wired yet)──► INotificationPublisher           │
 │                                                    NotificationPublisher          │
 │                                                    IAmazonSQS.SendMessageAsync    │
 └──────────────────────────────────────────────────────────┬────────────────────────┘
          task role: yisru-task-role                         │ sqs:SendMessage
          (inline policy yisru-sqs-send)                     ▼
                                    ┌──────────────────────────────────────────┐
                                    │ SQS  yisru-notifications   (standard)    │
                                    │   visibility timeout 90s                 │
                                    │   redrive → DLQ after 3 receives         │
                                    └───────────────┬─────────────────┬────────┘
                         event source mapping       │                 │ 3rd failed receive
                         batch 10, ReportBatch-     │                 ▼
                         ItemFailures               │   ┌───────────────────────────────┐
                                                    │   │ SQS  yisru-notifications-dlq  │
                                                    │   │   14-day retention            │
                                                    │   │   (manual inspect / redrive)  │
                                                    │   └───────────────────────────────┘
                                                    ▼
          ┌────────────── Lambda: yisru-notifications-processor (EasyFind.Functions) ─────────┐
          │  Function.FunctionHandler(SQSEvent) → SQSBatchResponse                            │
          │    for each record: deserialize NotificationMessage → switch on Type              │
          │      "payment_success" → AfroMessageClient.SendAsync ──► api.afromessage.com      │
          │      unknown type      → throw  (reported as a batch-item failure)                │
          │  cold start: GetSecretValue($SMS_SECRET_ID) → AfroMessage__* keys                 │
          └───────────────────────────────────────────────────────────────────────────────────┘
               execution role: yisru-functions-role
               (AWSLambdaSQSQueueExecutionRole + inline yisru-functions-secrets)
```

Projects involved:

| Project | Role |
|---|---|
| `EasyFind.Contracts` | Message types shared by producer and consumer: `NotificationMessage` (envelope) and `PaymentSuccessPayload`. No dependencies. |
| `EasyFind.Api` | Producer. `Services/NotificationPublisher.cs` behind `Services/IServices/INotificationPublisher.cs`, registered in `AddInfrastructure` together with `AddAWSService<IAmazonSQS>()`. Sends to `Notifications:QueueUrl`, which is validated at startup. Integration tests swap in `FakeNotificationPublisher`. |
| `EasyFind.Functions` | Consumer Lambda. `Function.cs` dispatches, `AfroMessageClient.cs` calls AfroMessage. |

### AWS resources (eu-central-1, account 454252678518)

| Resource | Settings | Notes |
|---|---|---|
| SQS `yisru-notifications` | Standard queue. Visibility timeout **90s**. Redrive policy → `yisru-notifications-dlq`, `maxReceiveCount` **3**. | 90s = 6 × the Lambda timeout, which is AWS's recommended minimum for an SQS trigger. **If you raise the Lambda timeout, raise this too**, or a message becomes visible again while still being processed and gets sent twice. |
| SQS `yisru-notifications-dlq` | Standard queue, **14-day** retention. | For standard queues a message keeps its *original* enqueue timestamp when moved to the DLQ, so its 14 days start from when the API sent it, not from when it failed. Nothing consumes this queue automatically. |
| Lambda `yisru-notifications-processor` | Runtime `dotnet10`, 512 MB, timeout **15s**, handler `EasyFind.Functions::EasyFind.Functions.Function::FunctionHandler`. | Settings live in `EasyFind.Functions/aws-lambda-tools-defaults.json`. |
| Event source mapping (queue → Lambda) | Batch size **10**, `FunctionResponseTypes = [ReportBatchItemFailures]`. | Configured in AWS, not in code. `ReportBatchItemFailures` is load-bearing: without it, Lambda ignores the returned `SQSBatchResponse` and deletes the whole batch as soon as the invocation returns, so failed sends are silently dropped. |
| Secret `yisru/prod/afromessage` | JSON key/value secret holding **only** the AfroMessage keys, separate from the API's `yisru/prod/app`. The Lambda reads `AfroMessage__ApiToken` (required), `AfroMessage__IdentifierId`, `AfroMessage__SenderName`. | Name comes from the Lambda env var `SMS_SECRET_ID` (default `yisru/prod/afromessage`). Loaded once per cold start and cached in the Lambda instance, so after rotating the token, warm instances keep the old one until they are recycled. The API still reads its own copy of these keys from `yisru/prod/app`, so a rotation has to update both. |

### IAM split

The producer and the consumer have separate roles, each with only its half of the queue:

| Principal | Role | Permissions |
|---|---|---|
| API (ECS tasks) | `yisru-task-role` | Inline `yisru-sqs-send`: `sqs:SendMessage` on `yisru-notifications` **only**. The API cannot read, delete or purge messages, and cannot touch the DLQ. |
| Lambda | `yisru-functions-role` | AWS-managed `AWSLambdaSQSQueueExecutionRole` (`sqs:ReceiveMessage`, `DeleteMessage`, `GetQueueAttributes` + CloudWatch Logs). Inline `yisru-functions-secrets`: `secretsmanager:GetSecretValue` on `yisru/prod/afromessage` only. |

The Lambda has no network path to, or credentials for, the database. Everything it needs has to be in the
message.

If the queue is ever switched to SSE-KMS with a customer-managed key, both roles also need KMS permissions
(`kms:GenerateDataKey` for the sender, `kms:Decrypt` for the Lambda). The default SSE-SQS encryption needs none.

### Message contract

Every SQS message body is a JSON-serialized `NotificationMessage` envelope:

```json
{
  "Type": "payment_success",
  "Version": 1,
  "Payload": "{\"UserId\":\"…\",\"PhoneNumber\":\"0911223344\",\"Email\":null,\"AmountEtb\":300,\"Tier\":\"Pro\"}"
}
```

- `Type` selects the handler in `Function.ProcessMessageAsync`. The match is **exact and case-sensitive**.
- `Version` is the payload schema version for that type. It exists so a payload can change shape without
  breaking messages already in flight. The consumer does not read it yet.
- `Payload` is a **JSON string**, not a nested object. It is deserialized a second time into the type-specific
  class, so the envelope stays the same for every message type.
- Property names are PascalCase, which is `System.Text.Json`'s default, and the default deserializer is
  case-sensitive. A hand-written message with `"type"` instead of `"Type"` deserializes with an empty `Type`
  and fails as an unknown type.

`PaymentSuccessPayload`:

| Field | Type | Notes |
|---|---|---|
| `UserId` | string | Used for logging only. |
| `PhoneNumber` | string | Normalized by `AfroMessageClient`: `+251…` → `251…`, `09…` (10 digits) → `2519…`, anything else is sent as-is. |
| `Email` | string? | Reserved for the email channel and not used yet. |
| `AmountEtb` | int | Whole birr. |
| `Tier` | string | e.g. `"Pro"`, and appears in the SMS text. |

#### Adding a new message type

1. **Contracts:** add a payload class to `EasyFind.Contracts` (e.g. `ListingDeadlinePayload`). The contract
   is shared by two independently deployed programs, so treat every field as a public API: add fields freely,
   but never rename or remove one, or change its meaning, without bumping `Version`.
2. **Producer:** add a method to `INotificationPublisher` / `NotificationPublisher` that wraps the payload in a
   `NotificationMessage` with the new `Type` string and `Version = 1`, and call it from the handler that owns
   the event.
3. **Consumer:** add a `case` for the new `Type` in `Function.ProcessMessageAsync`. It must **throw** on any
   failure it wants retried, and must not swallow exceptions.
4. **Deploy the Lambda first**, then the API. If the API goes first, the new messages hit the old Lambda and
   fail as unknown types. They are retried three times and then parked in the DLQ, where they can be
   redriven once the Lambda is updated. That is recoverable, but noisy.
5. Use the **same `Type` string** on both sides. Put it in a shared constant in `EasyFind.Contracts`, not a
   literal on each side (see Known limitations #1).

### Deploying the Lambda

The Lambda is **not** part of `deploy.yml`. Pushing to `main` deploys only the API image. Deploy the Lambda by
hand:

```powershell
dotnet tool install -g Amazon.Lambda.Tools      # once; `dotnet tool update -g Amazon.Lambda.Tools` to upgrade
aws sso login --profile AdministratorAccess-454252678518

cd EasyFind.Functions
dotnet lambda deploy-function yisru-notifications-processor --function-role yisru-functions-role
```

`deploy-function` reads region, runtime, memory, timeout, handler and AWS profile from
`aws-lambda-tools-defaults.json`. It builds in Release, zips the output and updates the function's code and
configuration. It does **not** create or modify the SQS trigger or the queues.

After deploying, confirm the trigger is still configured the way the retry logic assumes:

```powershell
aws lambda list-event-source-mappings --function-name yisru-notifications-processor --region eu-central-1 `
  --query "EventSourceMappings[].{State:State,Batch:BatchSize,Response:FunctionResponseTypes}"
# Expect: State Enabled, Batch 10, Response ["ReportBatchItemFailures"]
```

Deploy from committed code. The deployed Lambda is whatever was on the machine that ran the command, and
nothing records which commit that was.

### Testing it end to end

**1. Send a test message (SQS console).** Open SQS → `yisru-notifications` → *Send and receive messages*,
and paste into *Message body*:

```json
{
  "Type": "payment_success",
  "Version": 1,
  "Payload": "{\"UserId\":\"manual-test\",\"PhoneNumber\":\"<your own number, e.g. 09XXXXXXXX>\",\"AmountEtb\":1,\"Tier\":\"Pro\"}"
}
```

This sends a **real SMS** through the production AfroMessage account. Use your own number.

CLI equivalent:

```powershell
aws sqs send-message --region eu-central-1 `
  --queue-url https://sqs.eu-central-1.amazonaws.com/454252678518/yisru-notifications `
  --message-body file://test-message.json
```

**2. Check CloudWatch Logs.** The log group is `/aws/lambda/yisru-notifications-processor`.

```powershell
aws logs tail /aws/lambda/yisru-notifications-processor --follow --region eu-central-1
```

- Success: `[payment_success] SMS sent for user manual-test`
- Failure: `Failed <sqs-message-id>: <reason>`. The same message ID then appears again on each retry, up to 3
  receives.

**3. Inspect the DLQ.** A message lands here after its 3rd failed receive. That takes at least 3 × 90s
(the visibility timeout) after the first failure.

- Console: SQS → `yisru-notifications-dlq` → *Send and receive messages* → *Poll for messages*. Polling
  makes the messages temporarily invisible and increments their receive count, but does not delete them.
- CLI: `aws sqs get-queue-attributes --queue-url <dlq-url> --attribute-names ApproximateNumberOfMessages`

**4. Redrive the DLQ.** Fix the cause first (deploy the Lambda fix, correct the secret). Then move the
messages back to the main queue:

- Console: SQS → `yisru-notifications-dlq` → *Start DLQ redrive* → *Redrive to source queue(s)*.
- CLI:

  ```powershell
  aws sqs start-message-move-task --region eu-central-1 `
    --source-arn arn:aws:sqs:eu-central-1:454252678518:yisru-notifications-dlq `
    --destination-arn arn:aws:sqs:eu-central-1:454252678518:yisru-notifications
  ```

Redriven messages are processed again **in full**. Nothing deduplicates them, so a message whose SMS was
actually delivered before the failure will be sent again.

### Known limitations

These come from the 2026-09-28 audit, and each has a proposed fix there. The first one blocks the feature.

1. **Producer and consumer disagree on the type string.** `NotificationPublisher` sends `"Payment_success"`,
   but the Lambda matches `"payment_success"`, and the match is case-sensitive. Once wired, every payment
   notification would fail as an unknown type and end up in the DLQ.
2. **Not wired.** Nothing calls `INotificationPublisher` yet. Where and how it is called from
   `ProcessChapaPaymentHandler` decides whether messages can be lost or sent for a payment that rolled back.
3. **No idempotency.** SQS is at-least-once, and a Lambda timeout, a retry after a delivered-but-unacknowledged
   send, or a DLQ redrive can all resend an SMS. Nothing deduplicates. The payload carries no `TxRef` or
   message ID to deduplicate on.
4. **The whole batch shares a 15s timeout.** Up to 10 messages are sent one after another, and the
   `HttpClient` has the default 100s timeout. If the invocation times out, the entire batch returns to the
   queue, including messages already sent.
5. **Every environment needs `Notifications:QueueUrl`.** It has no default, and the API refuses to start
   without it. Production gets it as the env var `Notifications__QueueUrl`, which **must be added to the ECS
   task definition (or `yisru/prod/app`) before merging** the change that introduced it, or the new tasks
   fail at startup. Locally, put a placeholder in `appsettings.Development.json`, e.g.
   `"Notifications": { "QueueUrl": "https://sqs.invalid/local-dev-placeholder" }`. That file is git-ignored, so
   each developer adds it themselves. With a placeholder, publishes fail and are logged, and nothing is sent.
   Never point it at the production queue.
6. **The AfroMessage token exists in two secrets.** The API reads it from `yisru/prod/app`, and the Lambda from
   `yisru/prod/afromessage`. Rotating it means updating both.
7. **No correlation ID.** A log line in the API cannot be joined to the Lambda's log line for the same
   notification except by timestamp.
8. **No alarm on the DLQ.** Failed notifications sit there unnoticed and expire after 14 days.
9. **No tests** for the Lambda. `EasyFind.Functions/Readme.md` refers to a test project that does not exist.
10. **Infrastructure is click-ops.** The queues, roles and event source mapping exist only in the AWS account,
    so nothing in the repo can recreate them.
11. **SMS text is hardcoded in English** inside the Lambda. Email is not implemented.
