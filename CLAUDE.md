# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

EasyFind (deployed as "yisru") is an ASP.NET Core 10 REST API serving a mobile/web client for Ethiopian
job-seekers and scholarship applicants. Users onboard with a profile of preferences, get a *personalized,
ranked* feed of listings, bookmark/track applications, upload documents, and pay for a Pro subscription via
Chapa (Ethiopian payment gateway). Admins manage listings, users, and subscriptions.

Solution: `EasyFind.Api` (the whole app), `EasyFind.UnitTests`, `EasyFind.IntegrationTests`. Target `net10.0`.

## Commands

```powershell
dotnet build EasyFind.sln
dotnet run --project EasyFind.Api                   # http://localhost:5123, API docs at /scalar
docker compose up -d                                # local postgres:5432 + redis:6379

dotnet test                                         # both test projects
dotnet test EasyFind.UnitTests
dotnet test --filter "FullyQualifiedName~ListingScorerTests"
dotnet test --filter "DisplayName~GetListing_WhenNotFound_Returns404"

dotnet ef migrations add <Name> --project EasyFind.Api
dotnet ef database update --project EasyFind.Api
```

Migrations are **never** applied at startup, in any environment — run `dotnet ef database update` yourself.
This is a deliberate choice: a deploy must not be able to alter the schema on its own. Identity roles
(`SuperAdmin`/`Admin`/`User`) *are* seeded on every boot.

**The consequence is that a migration is a separate manual step, and it has to land before the code that
needs it.** Nothing in `deploy.yml` checks this, and integration tests build their schema with
`EnsureCreated()` so they pass whether or not a migration exists — merge to `main` with an unapplied
migration and the new ECS tasks come up against the old schema and fail on first use. **The deploy will
still go green:** `ecs wait services-stable` only waits for tasks to pass `/health`, which touches no table,
so a missing table surfaces as 500s on whichever endpoint uses it. This happened with `OtpThrottles` —
`request-otp` failed with `relation "OtpThrottles" does not exist` while the deploy reported success.
Apply the migration against production **first**, then merge.

Deploy: pushing to `main` triggers `.github/workflows/deploy.yml` — builds `EasyFind.Api/Dockerfile`, pushes
to ECR, forces a new ECS deployment (eu-central-1). Production's database is **AWS RDS**.

### Local development

- **Database:** the Docker Postgres from `compose.yaml` (`localhost:5432`, db `EasyFind`, user `postgres`,
  password `123`), set in the git-ignored `appsettings.Development.json`. A fresh volume is empty: run
  `dotnet ef database update` after `docker compose up -d`. Migrate locally first, so a forgotten migration
  fails on your machine rather than in production. The Supabase database in `appsettings.json` is a shared
  dev database, not production.
- **Sign-in without SMS:** in Development, `ConsoleSmsService` replaces `AfroSmsService`, so `request-otp`
  writes the code to the console (`DEV SMS: OTP for ... is ...`) instead of texting it. Nothing is bypassed —
  the code is still generated, throttled and verified. It is registered in `Program.cs` under
  `IsDevelopment()` only; production must never run with `ASPNETCORE_ENVIRONMENT=Development`, or every
  user's code would be written to CloudWatch.
- **The first SuperAdmin** has to be granted in the database, since `assign-role` requires SuperAdmin. Sign
  in once so the account exists, then insert its `SuperAdmin` row into `AspNetUserRoles`, and sign in again
  so the token carries the role.

## Architecture

### CQRS handlers, and nothing else

All business logic lives in `Features/<Domain>/{Commands,Queries}/`. **One file = one use case = one message
+ one handler + one public `HandleAsync`.** Commands write, queries read, and nothing sits between the
controller and the work — there is no mediator (MediatR and AutoMapper were removed because nothing used
them) and no service layer left to route through.

Each file declares its message directly above its handler:

```csharp
public sealed record GrantSubscriptionCommand(string AdminUserId, string TargetUserId, GrantSubscriptionDto Grant);

public class GrantSubscriptionHandler(...)
{
    public async Task<Result> HandleAsync(GrantSubscriptionCommand command, CancellationToken ct = default)
    {
        var (adminUserId, targetUserId, dto) = command;
        ...
```

Two rules keep these from bloating:

- **Messages compose the request DTO, they never re-declare its fields** — `CreateListingCommand(CreateListingDto Listing)`, not 20 copied properties. The DTO stays the wire contract; the message names the *operation's* full input.
- **Naming the fields is the point.** `GrantSubscriptionCommand` has two adjacent strings; passed positionally, swapping them would credit the wrong account and file the audit row backwards. That is the whole reason these records exist, not conformance to a pattern.

Handlers deconstruct the message on the first line, so bodies read exactly as they would with loose
parameters. The single exception is `GetOverviewStatsHandler`, which takes no input at all — an empty record
would be ceremony, so it just takes a `CancellationToken`.

```
Features/
  Listings/       ListingAuthorizationService  ListingMapper
                  Commands/ Create Update Delete Restore SetActive UploadImage
                  Queries/  GetFeed GetListingDetail GetAdminListing ListAdminListings
  Bookmarks/      Add Remove | GetUserBookmarks
  Applications/   Create Update Delete | GetUserApplications        (+ ApplicationMapper)
  Profile/        UpsertProfile | GetProfile                        (+ ProfileMapper)
  Documents/      Upload Delete | GetUserDocuments GetDocumentDownloadUrl  (+ DocumentMapper)
  Subscriptions/  InitiateSubscription ProcessChapaPayment | GetMySubscriptionStatus
  Users/          RequestOtp VerifyOtp Logout AssignRole UpdateCurrentUser
                  UpdateProfilePicture RequestPhoneChange ConfirmPhoneChange
                  | GetCurrentUser        (+ UserMapper, PhoneNumberRules)
  Admin/          GrantSubscription RevokeSubscription
                  | ListUsers GetUserDetail ListPayments GetOverviewStats
```

`Services/` now holds **only infrastructure** — `TokenService`, `AfroSmsService` (`ConsoleSmsService` in Development), `S3StorageService`,
`ImageService`, `ChapaClient`, `ChapaWebhookVerifier`, `RedisCacheService`/`NoOpCacheService`, plus
`CurrentUser`, `SubscriptionGate` and `ImageValidator`. These keep their interfaces: they are external
boundaries worth being able to swap or fake. **Handlers get no interface** — one implementation, injected
concretely.

Handlers are injected **per action** with `[FromServices]`, so an endpoint's signature is the complete list of
what it touches and adding one can't disturb another. Controllers hold no business logic: they normalise
input (clamp paging, check the user id) and hand the `Result` to `HandleResult`.

**To add a use case:** new file with a `XCommand`/`XQuery` record and its handler + one line in the feature's
`Add<Feature>Feature()` method in `LifetimeServicesCollectionExtensions.cs` + one controller action. Nothing is registered by assembly
scanning, so "why did this resolve?" is always answerable by reading that one file; miss the line and the
endpoint fails fast on its first request.

### Result -> ApiResponse convention

Handlers return `Result` / `Result<T>` (`Models/Common/Result.cs`) carrying an `ErrorType`
(`Validation`/`NotFound`/`Conflict`/`Forbidden`/`Unauthorized`/`Failure`). Controllers inherit
`ApiControllerBase` and call `HandleResult(result)`, which maps `ErrorType` to an HTTP status and wraps the
payload in `ApiResponse { IsSuccess, Errors[], Result }`. List endpoints return `Result<PagedResult<T>>` for
the same reason — one path, one shape. Don't return raw `Ok()`/`NotFound()` from a new endpoint.

**The one exception is `AuthController`'s `request-otp` and `verify-otp`.** `RequestOtpHandler` returns
`LoginResponseDto` and `VerifyOtpHandler` returns `TokenDto` — both carrying their own `IsSuccess`/`Message`
fields — and `request-otp` answers **201**, not 200. The mobile client depends on those exact bodies, so
moving them onto `Result` is a client-coordinated breaking change, not a tidy-up. The handlers say so too.

**`refresh-token` used to be a third shape, and was actively misleading.** It answered **200 for both
outcomes**: the failure path returned a bare `TokenDto` whose `IsSuccess` defaulted to `true`, while the
success path never set the envelope's `IsSuccess` at all — so both said `isSuccess: false` outside and
`true` inside, and the only real signal was whether `result.accessToken` was null. Success is now 200 with
`IsSuccess` true, a missing refresh token is 400, and every rejected credential is **401**. `TokenDto.IsSuccess`
now defaults to `false`, which is what made the original bug possible.

The success **body** is deliberately unchanged, so a client keying off `result.accessToken` keeps working —
old app versions will hit this endpoint for months. `RefreshTokenTests` pins all of it.

### The feed is the core of the product

`Features/Listings/Queries/GetFeedHandler.cs` — read this first when touching listings, caching, or
subscriptions. Per request:

1. Read the user's `SubscriptionTier`; `SubscriptionGate` turns it into a result cap (`FreeFeedCap`, 5) and
   decides whether `Organization` and `ApplyUrl` are nulled out for free users (`IsLocked` on the DTO).
2. Ranking is computed **in SQL** (`OrderByDescending(score)` over weighted matches against the user's
   `UserProfile`) and cached in Redis for 5 min under the key from `FeedCacheKeyExtensions.ToFeedCacheKey` —
   that key includes userId, country, search, page, pageSize **and tier**.
3. Per-user flags (`IsBookmarked`, `ApplicationStatus`) are queried fresh every request and **never cached**.
   Keep that split: anything user-mutable must stay out of `CachedFeedPage`.
4. Any write that changes listings must call `cache.InvalidateFeedsAsync()` — every listing command does.

`ListingScorer.cs` is a pure re-implementation of the same formula used **only by unit tests** —
`GetFeedHandler` does not call it (it can't; the scoring has to stay an expression tree so Postgres can sort
it). The weights have already drifted (`DegreeWeight` 20 there vs 15 in the handler). If you change scoring
weights, change both.

Two latent quirks worth knowing before you "fix" them: `CachedFeedItem.ApplyUrl` is read when building the
feed DTO but never populated, so feed items carry no apply link even for paid users; and
`CreateListingDto.SalaryPeriod`/`SalaryCurrency` are accepted by the API but not copied onto the entity
(see the NOTE in `ListingMapper.ApplyTo`). Both predate the CQRS refactor and were preserved deliberately.

### Authorization

- JWT bearer, `ClockSkew.Zero`. Login is **phone + OTP** (`AuthController` request-otp / verify-otp via
  `AfroSmsService`, or `ConsoleSmsService` in Development — see Local development), plus refresh tokens.
- **The access token lifetime is the revocation window.** Nothing validates an issued token against the
  database, so a ban, a role change or a logout only takes effect when the current token expires. It is
  `JwtConfig:AccessTokenMinutes` (default 60), clamped to 8 hours in `TokenService` — it was `AddDays(15)`.
  The client is expected to call `/auth/refresh-token` on a 401; `RefreshAccessToken` *reads* the old token's
  claims rather than validating them, so an expired access token still refreshes cleanly. Don't raise this to
  work around a client that hasn't implemented refresh.
- **Refresh tokens are single-use, and reuse revokes the whole chain.** A successful refresh issues a new
  refresh token and invalidates the one presented; re-presenting a consumed one calls
  `MarkAllTokenInChainAsInvalid`, signing the user out entirely. The usual cause is not an attacker but a
  client refreshing **concurrently** — two requests 401 at once, the second sends what the first just spent.
  Clients must serialise refreshes (one in flight, others await it) and persist the rotated token. That
  revocation is logged at Warning; it is the only trace a "why was I logged out?" report will have.
- **Never change `ApplicationUser.PhoneNumber` without also changing `UserName`.** The two sign-in steps look
  the account up by different columns — `RequestOtpHandler` uses `FindByNameAsync(phone)` (UserName),
  `VerifyOtpHandler` queries `PhoneNumber` — and `RequestOtpHandler` *creates* an account when the lookup
  misses. Let them drift and the next OTP request silently registers a duplicate user. This is why
  `UpdateCurrentUserHandler` edits names only.
- **Changing the phone number** is its own two-step flow: `POST /auth/me/phone/request-otp` texts a code to
  the *new* number, `POST /auth/me/phone/confirm` sends it back with that number. The OTP comes from
  Identity's `GenerateChangePhoneNumberTokenAsync`, so it is bound to (user + new number) and can't be
  replayed against a different number — which is also why no pending-change state is stored anywhere.
  `ConfirmPhoneChangeHandler` moves `UserName` and `PhoneNumber` in a **single** `UpdateAsync`; don't use
  Identity's `ChangePhoneNumberAsync` here, as it saves `PhoneNumber` on its own and leaves `UserName` for a
  second write. `PhoneNumberRules` holds the uniqueness check both steps share.
- `ICurrentUser` exposes `UserId` and `IsInRole(role)` from claims; `ApiControllerBase.UserId` does the same
  for controllers. There is deliberately no single `Role` property — a promoted admin still holds `User`, so
  reading one role claim gives the wrong answer.
- **`Features/Listings/ListingAuthorizationService` is the only place allowed to read `db.Listings`.**
  `AuthorizedListings(activeOnly = true)` is the consumer path (job-seekers get `IsActive && DeletedAt == null`;
  `activeOnly: false` keeps already-saved/applied-to listings visible after they go inactive).
  `ManageableListings()` is the staff path (Admin/SuperAdmin see everything including soft-deleted; everyone
  else gets an empty set, so a missing `[Authorize]` can't leak). `PublishedListings()` is the public view
  (active and not deleted) for **every** role, staff included — for consumer surfaces that must not change
  with the caller's role, like the assistant's `SearchListingsTool` (`AuthorizedListings` would offer an admin
  closed and deleted listings there; `SearchListingsToolTests` pins this). Unknown or missing role sees nothing.
  Add new listing queries through one of these three — never `db.Listings`, and never via the `b.Listing` /
  `a.Listing` navigation properties, which bypass the check (join to the authorized set instead).
- The service depends on `ICurrentUser`, so it only works inside a request. A background job that needs
  listings must take an explicit scope rather than calling it.
- Admin controllers use `[Authorize(Policy = AppPolicies.AdminAccess)]` and route under `api/v{version}/admin/...`.
  **Use the policies in `AppPolicies`, never `[Authorize(Roles = ...)]`.** Identity roles are flat, so
  `Roles = "Admin"` rejects a SuperAdmin who doesn't also hold Admin. `AdminAccess` accepts Admin or
  SuperAdmin; `SuperAdminAccess` is SuperAdmin only. `RoleHierarchyTests` pins both.
- **`POST /auth/assign-role` is SuperAdmin only.** `AssignRoleHandler` grants whatever role it is asked
  for, so when it accepted Admin, any Admin could promote themselves to SuperAdmin. A granted role reaches
  the user's token on their next sign-in or refresh (`GenerteAccessToken` reloads roles from the database);
  until then the old token keeps the old roles.
### OTP abuse limits

Two independent layers, both required. An OTP send costs money and rings a stranger's phone.

- **Per phone number — the real control.** `IOtpThrottle` / `OtpThrottleService`, counted in Postgres
  (`OtpThrottles` table, one row per number). Sends are capped per rolling window; failed verifications lock
  the number out for a cooldown that refuses **even a correct code** — otherwise guessing on the last
  permitted try still wins. Counting is in the database, not memory, because several ECS tasks would each
  keep their own counter and multiply every limit by the task count. The increments are conditional
  `ExecuteUpdateAsync` statements, so a burst cannot all read the same count.
- **Per client IP — a backstop.** The `otp-send` / `otp-verify` rate-limiter policies. In-process, so the
  effective cap is (configured × task count); deliberately loose, because several real users can share one
  NAT address. It catches an attacker rotating phone numbers, which the per-phone limit cannot.

Both are needed: the caller picks the phone number, so a per-phone limit alone is bypassed by rotating
numbers, while a per-IP limit alone lets a botnet bomb one victim. Limits live under `OtpThrottle` in
configuration. 429s return the normal `ApiResponse` envelope plus `Retry-After`.

**`BehindReverseProxy` must stay true in production.** Without `UseForwardedHeaders`, every request behind the
ALB appears to come from the load balancer and per-IP limiting becomes one global bucket. `ForwardLimit = 1`
takes the entry the ALB appended, so a client-supplied `X-Forwarded-For` cannot spoof it.

**History, worth not repeating:** the previous `otp` policy partitioned on an `X-Phone` header that no client
sends. A missing header yields `""` (not `null`), so `?? "anonymous"` never fired and every caller on earth
shared one 3-per-hour bucket — it rate-limited the entire product and had to be removed. Partition on
something the request actually carries.

### Data

`Data/ApplicationDbContext.cs` (`IdentityDbContext<ApplicationUser>`, Npgsql). All enums are stored as `int`
via `HasConversion<int>()`. `UserProfile`'s enum lists map to Postgres `int[]` through explicit converters;
`List<string>` maps to `text[]` natively — mirror this when adding array columns.

**Soft delete:** `Listing.DeletedAt` exists but the global `HasQueryFilter` is commented out, so `DeletedAt`
is filtered *explicitly* inside `ListingAuthorizationService` instead (and `IgnoreQueryFilters()` no longer
appears anywhere — it was a no-op).

`DeleteListingHandler` performs a **soft** delete: it stamps `DeletedAt`, clears `IsActive`, and keeps the
row. That is what makes `POST /admin/listings/{id}/restore` work at all — it previously hard-deleted, so
there was never a row left to restore. It also protects history: `UserApplication` references listings with
`DeleteBehavior.Restrict`, so hard-deleting a listing someone had applied to would have failed outright.
Deleting twice is a no-op rather than re-stamping the time. Restore clears `DeletedAt` but deliberately does
**not** republish — that is `PATCH .../active`.

### Subscriptions and payments (Chapa)

`SubscriptionService` + `ChapaClient` initiate a payment. `WebhooksController` accepts **both** the POST
webhook (signature-verified by `ChapaWebhookVerifier` against `chapa-signature`/`x-chapa-signature`) and the
GET callback, and both funnel into `HandleWebhookAsync`, which must stay **idempotent**. Tier is `Free | Pro`.
`Services/Jobs/SubscriptionExpiryJob` runs daily at 02:00 UTC via Hangfire (Postgres storage).

**The Hangfire dashboard has its own sign-in** (`HangfireDashboardAuth`). Registering the recurring job and
exposing the dashboard are separate: the job runs wherever `Hangfire:Enabled` is true, while `/hangfire` is
mapped when `Hangfire:DashboardEnabled` is *also* true (on by default). It has no authorization of its own and
can enqueue, requeue and delete jobs, so an open one is remote control of the queue —
`HangfireDashboardAuthorizationFilter` (Admin/SuperAdmin) is the whole of its protection.

An admin signs in at **`/hangfire/login`** by pasting an access token, which is exchanged for a session cookie.
Two things forced that design, and both are easy to "simplify" back into a broken dashboard:

- **A query-string token is not enough.** Hangfire serves its CSS, JS, the `/hangfire/stats` poll and every nav
  link as separate requests carrying no query string, so `?access_token=` yields an unstyled page that 401s on
  the first click. It also writes a live admin JWT into ALB access logs, browser history and `Referer`.
- **`AddCookie()` would not survive multiple tasks.** Its ticket is encrypted with Data Protection keys, and
  nothing persists those to shared storage — each ECS task has its own key ring, so a cookie minted by one task
  fails on the next request that lands elsewhere. The cookie therefore carries the JWT itself, validated
  against the same `JwtBearerOptions` every task shares.

The cookie is `HttpOnly`, `Secure` over HTTPS, `Path=/hangfire`, and **`SameSite=Strict`** — that last one is
load-bearing: the dashboard deletes and requeues jobs over plain POSTs, and a cookie sent cross-site would let
any page drive them.

Pipeline order is also load-bearing, all of it below `UseAuthentication()`: `MapHangfireLogin()` first (before
Hangfire's middleware 404s an unknown `/hangfire` path), then `UseHangfireDashboardAuth()` (the cookie →
`HttpContext.User` step, which the *synchronous* dashboard filter depends on), then `UseHangfireDashboard()`.

### Infrastructure notes

- **Redis is optional.** `Program.cs` registers `RedisCacheService` only when the connection string is set and
  does *not* contain `localhost`; otherwise `NoOpCacheService` is used. Locally that means the feed cache is a
  no-op by default, even with `docker compose up`.
- **Storage:** `IStorageService` -> `S3StorageService` (buckets under `AWS:S3`). Cloudinary is still wired into
  `ImageService`. `ImageValidator` checks magic bytes on listing-image upload; `DocumentUploadOptions` caps
  documents at 5 MB and `.pdf .doc .docx`.
- API versioning is URL-segment based (`api/v1/...`), default 1.0. Scalar UI is exposed in Development **and
  Production**. CORS is `AllowAnyOrigin`.
- Serilog writes to console and `logs/easyfind_api_log.txt` at **Information**, with `Microsoft`, `System`
  and EF Core overridden to `Warning` so request noise doesn't bury application logs.
- `Nullable` is **disabled** in `EasyFind.Api` but enabled in both test projects.
- **Health checks are split, and the split is the point.** `/health` is **liveness** — it runs no checks at
  all (`Predicate = _ => false`), just proves the process answers, and it is what the ALB target group probes.
  `/health/ready` is **readiness**, running everything tagged `"ready"` (currently Postgres), and is for
  monitoring and post-deploy checks. Never point the load balancer at readiness: a dependency check there
  fails on every task at once during an RDS blip, the ALB drains the whole service, and a recoverable
  database hiccup becomes a full outage that outlives it. `OperationalHardeningTests` asserts liveness stays
  green while the database is unreachable.

### Validation and error shape

**Every failure uses the `ApiResponse` envelope.** Three things had to be aligned for that:

- `HandleResult` for handler failures (the `Result`/`ErrorType` path).
- `ApiBehaviorOptions.InvalidModelStateResponseFactory` for DataAnnotation failures — `[ApiController]`
  answers those with `ProblemDetails` by default, which was a second shape.
- `GlobalExceptionHandler` (`IExceptionHandler`) for anything unhandled, which was a third. The message is
  generic on purpose; the detail goes to the log so an exception can't leak internals. Note
  `UseExceptionHandler()` will not start without `AddProblemDetails()` registered as a fallback, even though
  the handler always handles.

FluentValidation validators in `Validators/` are registered by hand in `AddInfrastructure` and enforced by
`ValidationFilter` on every action. A DTO with no registered validator passes through untouched, so adding
validation is opt-in: write the validator, add one line, done.

`ListingRules<T>` holds the rules shared by create and update. It's generic because `UpdateListingDto`
derives from `CreateListingDto` and the filter resolves `IValidator<T>` **by exact type** — a validator typed
to the base is never found for the derived DTO. Only `CreateListingValidator` rejects a deadline in the past:
inheriting that on update would make an expired listing uneditable, including to take it down.

## Tests

`dotnet test` runs both projects, and `deploy.yml` runs it before building the image — a failing test now
blocks the deploy.

- `EasyFind.UnitTests` — xUnit + FluentAssertions, pure functions only (`ListingScorerTests`). Note these
  test `ListingScorer`, which production does not call; see the scoring note above.
- `EasyFind.IntegrationTests` — tests that boot the real app over **SQLite in-memory** and drive real HTTP
  requests. `FeedGatingTests` covers the paywall from both sides plus the authorization chokepoint,
  `SubscriptionWebhookTests` covers Chapa idempotency and stacking, `PhoneChangeTests` covers the
  UserName/PhoneNumber invariant end to end, and `OperationalHardeningTests` covers the Program.cs
  decisions that only fail in production — liveness surviving an unreachable database and the issued access
  token expiring in hours.
- `HangfireDashboardAuthTests` is the exception to "boot the real app": the dashboard needs Hangfire's
  Postgres storage at startup, which is exactly what the harness disables. It hosts `MapHangfireLogin` and
  `UseHangfireDashboardAuth` on a bare `TestServer` with a stand-in for the dashboard, and covers the token
  exchange, the cookie flags, and rejection of non-admin, forged and expired tokens.

**Working on the harness** — four things it has to fight, all documented in `CustomWebApplicationFactory`:

1. **Never declare `partial class Program` in the test project.** It shadows the API's entry point and the
   host fails with "The entry point exited without ever building an IHost".
2. **Settings read before `builder.Build()` must be environment variables.** Under minimal hosting the entry
   point reads configuration as it executes, so `ConfigureAppConfiguration` lands too late — that is why
   `Hangfire__Enabled` and the connection strings are set as env vars in a static constructor. Anything read
   lazily (when a service is constructed) can go in `ConfigureAppConfiguration`.
3. **Auth**: `TestAuthHandler` authenticates from `X-Test-UserId` / `X-Test-Roles`. All three
   `AuthenticationOptions` defaults must be overridden, because `Program.cs` names JwtBearer explicitly.
4. **`ISmsService` and `IChapaClient` are replaced by fakes** so no test touches the network;
   `FakeSmsService.LastOtpFor(phone)` is how a test "reads the SMS".

Two provider-specific fallbacks in `ApplicationDbContext` exist purely so this works: the `UserProfile` enum
arrays become a CSV string, and `DateTimeOffset` becomes a binary long (SQLite cannot `ORDER BY` one, and the
feed orders by `CreatedAt`). **Both mean the test database encodes those columns differently than production**,
so anything depending on Postgres array or timestamp semantics still needs a real Postgres. `EF.Functions.ILike`
is still Postgres-only — search filters are untested here. Nor can concurrency be tested: the tests share one
SQLite connection, so parallel requests just return "database is locked" — the atomicity of the OTP throttle
and the payment guard needs a real Postgres to verify.

Migrations: `OtpThrottles` was added in `20260903_OtpThrottle`. Integration tests use `EnsureCreated()`, so
they never run migrations — a new table works in tests whether or not you generated one. **Remember to run
`dotnet ef migrations add`**, or it will be missing in production.
