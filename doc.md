# EasyFind / yisru — operational docs

## Notifications pipeline

User-facing notifications (SMS now, email later) are sent **asynchronously**. The API does not call the
SMS gateway on the request path: it drops a message on an SQS queue and a Lambda sends it. This keeps a
slow or failing gateway from failing the request that triggered the notification (e.g. a Chapa webhook), and
gives failed sends automatic retries plus a dead-letter queue.

> **Status (2026-09-30):** wired for `payment_success`. `ProcessChapaPaymentHandler` publishes once per
> payment, after the activation commits. See [Known limitations](#known-limitations) for what can still go
> wrong.

### Architecture

```
 ┌──────────────────────── ECS: yisru-service (EasyFind.Api) ────────────────────────┐
 │                                                                                   │
 │  ProcessChapaPaymentHandler ──(after commit)───► INotificationPublisher           │
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
               (inline yisru-functions-sqs + inline yisru-functions-secrets)
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
| Lambda | `yisru-functions-role` | Inline `yisru-functions-sqs` (below): `sqs:ReceiveMessage`, `DeleteMessage`, `GetQueueAttributes` on `yisru-notifications` only, and CloudWatch Logs on its own log group only. Inline `yisru-functions-secrets`: `secretsmanager:GetSecretValue` on `yisru/prod/afromessage` only. |

`yisru-functions-sqs` replaces the AWS-managed `AWSLambdaSQSQueueExecutionRole`, which grants the same
actions on **every** queue and log group in the account:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ConsumeNotificationsQueue",
      "Effect": "Allow",
      "Action": ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"],
      "Resource": "arn:aws:sqs:eu-central-1:454252678518:yisru-notifications"
    },
    {
      "Sid": "WriteOwnLogs",
      "Effect": "Allow",
      "Action": ["logs:CreateLogGroup", "logs:CreateLogStream", "logs:PutLogEvents"],
      "Resource": "arn:aws:logs:eu-central-1:454252678518:log-group:/aws/lambda/yisru-notifications-processor:*"
    }
  ]
}
```

The Lambda needs nothing on the DLQ: SQS moves failed messages there itself, and a redrive is done by a
person, not by this role. If the function is renamed, update the log-group ARN, or its logs silently stop.

The Lambda has no network path to, or credentials for, the database. Everything it needs has to be in the
message.

If the queue is ever switched to SSE-KMS with a customer-managed key, both roles also need KMS permissions
(`kms:GenerateDataKey` for the sender, `kms:Decrypt` for the Lambda). The default SSE-SQS encryption needs none.

### Message contract

Every SQS message body is a JSON-serialized `NotificationMessage` envelope:

```json
{
  "MessageId": "3f1c9a2e-…",
  "IdempotencyKey": "payment_success:easyfind-8c2e…",
  "Type": "payment_success",
  "Version": 1,
  "Payload": "{\"TxRef\":\"easyfind-8c2e…\",\"UserId\":\"…\",\"PhoneNumber\":\"0911223344\",\"Email\":null,\"AmountEtb\":300,\"Tier\":\"Pro\"}"
}
```

- `MessageId` is unique per publish, and defaults to a new GUID.
- `IdempotencyKey` is stable per business event (`payment_success:{txRef}`), so the same payment published
  or delivered twice carries the same key. **Nothing checks it yet** (Known limitations #2).
- `Type` selects the handler in `Function.ProcessMessageAsync`. The match is **exact and case-sensitive**,
  which is why both sides use the constants in `NotificationTypes` rather than literals.
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
| `TxRef` | string | Our Chapa reference. Source of the `IdempotencyKey`. |
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
5. Add the `Type` string to `NotificationTypes` in `EasyFind.Contracts` and use the constant on both sides,
   never a literal. The two sides drifted once (`"Payment_success"` vs `"payment_success"`), which would have
   sent every message to the DLQ.

### Deploying the Lambda

The Lambda is **not** part of `deploy.yml`. Pushing to `main` deploys only the API image. Deploy the Lambda by
hand:

```powershell
dotnet tool install -g Amazon.Lambda.Tools      # once; `dotnet tool update -g Amazon.Lambda.Tools` to upgrade
aws sso login --profile AdministratorAccess-454252678518

cd EasyFind.Functions
dotnet lambda deploy-function
```

`deploy-function` reads the function name, role, region, runtime, memory, timeout, handler and AWS profile
from `aws-lambda-tools-defaults.json`. It builds in Release, zips the output and updates the function's code and
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

Current as of 2026-09-30. Items 1 and 2 were **deliberately deferred**: they are known and accepted for now,
not overlooked.

1. **No outbox: a failed publish loses the SMS.** *(deferred)* `ProcessChapaPaymentHandler` publishes after
   the activation commits, and only on the delivery that won the `Pending → Success` guard. So duplicate
   callbacks publish nothing, and a rollback texts no one. But if the SQS call itself fails, the error is
   logged ("…notification was not published for {TxRef}") and nothing ever retries it. Payments with no
   phone number are skipped with a warning.
   *Fix when needed:* write a `PendingNotifications` row **in the same transaction** as the activation, and
   have a Hangfire job send pending rows and mark them sent. It needs a migration, which must be applied to
   production before the code merges.
2. **No deduplication: an SMS can be sent twice.** *(deferred)* Every message carries an `IdempotencyKey`,
   but nothing checks it. Duplicates can come from:
   - SQS delivering the same message twice (it is at-least-once);
   - a send that AfroMessage accepted but whose response was lost, or that was cancelled at the deadline;
   - a DLQ redrive of a message that had actually been delivered;
   - the SDK retrying `SendMessage` after a network error.

   *Fix when needed:* a DynamoDB table keyed on `IdempotencyKey`, with a TTL. The Lambda claims the key with a
   conditional put before sending, marks it sent afterwards, and skips keys already sent. That narrows the
   window but cannot close it, because AfroMessage has no idempotency of its own. The Lambda role would also
   need `dynamodb:PutItem`, `UpdateItem` and `GetItem` on that table. A FIFO queue is not a substitute: it only
   deduplicates sends within 5 minutes, not consumer retries, and it caps throughput.
3. **A slow gateway pushes messages into retries.** The Lambda stops starting new messages 2s before its
   timeout, and each HTTP call is capped at 5s. Anything not started is handed back, so nothing is lost. But
   every hand-back counts toward `maxReceiveCount` 3, so a sustained AfroMessage slowdown can move healthy
   messages to the DLQ.
4. **Every environment needs `Notifications:QueueUrl`.** It has no default, and the API refuses to start
   without it. Production gets it from the env var `Notifications__QueueUrl` in the ECS task definition.
   Locally, put a placeholder in `appsettings.Development.json`, e.g.
   `"Notifications": { "QueueUrl": "https://sqs.invalid/local-dev-placeholder" }`. That file is git-ignored,
   so each developer adds it themselves. With a placeholder, publishes fail and are logged, and nothing is
   sent. Never point it at the production queue.
5. **The AfroMessage token exists in two secrets.** The API reads it from `yisru/prod/app`, and the Lambda
   from `yisru/prod/afromessage`. Rotating it means updating both. Warm Lambda instances also keep the old
   token until they are recycled.
6. **No correlation ID in the logs.** The envelope now has a `MessageId`, but neither side logs it. An API log
   line can only be joined to the Lambda's line for the same notification by timestamp and `UserId`.
7. **No alarm on the DLQ.** Failed notifications sit there unnoticed and expire 14 days after they were first
   sent. Until the AfroMessage account has balance, every payment SMS ends up there.
8. **No tests for the Lambda.** `Function` builds its own Secrets Manager client and `HttpClient`, so it cannot
   be unit tested without first putting the SMS sender and secret loading behind interfaces. The deadline
   handling and the secret-id lookup are untested. `EasyFind.Functions/Readme.md` refers to a test project
   that does not exist.
9. **The Lambda is deployed by hand** from a developer machine. `deploy.yml` only deploys the API, so the
   deployed Lambda can differ from `main`.
10. **Infrastructure is click-ops.** The queues, roles, secret and event source mapping exist only in the AWS
    account. The IAM JSON in this document is a record, not something that applies itself.
11. **The consumer ignores `Version`**, and messages that can never succeed (unknown type, malformed JSON, a
    number AfroMessage rejects) are still retried three times before reaching the DLQ.
12. **SMS text is hardcoded in English** inside the Lambda. Email is not implemented.
