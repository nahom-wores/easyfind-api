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

Migrations are **not** applied at startup (the `MigrateAsync` block in `Program.cs` is commented out) — run
`dotnet ef database update` yourself. Identity roles (`SuperAdmin`/`Admin`/`User`) *are* seeded on every boot.

Deploy: pushing to `main` triggers `.github/workflows/deploy.yml` — builds `EasyFind.Api/Dockerfile`, pushes
to ECR, forces a new ECS deployment (eu-central-1).

## Architecture

### CQRS handlers, and nothing else

All business logic lives in `Features/<Domain>/{Commands,Queries}/`. **One file = one use case = one handler
class = one public `HandleAsync`.** Commands write, queries read, and nothing sits between the controller and
the work — there is no mediator (MediatR and AutoMapper were removed because nothing used them) and no
service layer left to route through.

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

`Services/` now holds **only infrastructure** — `TokenService`, `AfroSmsService`, `S3StorageService`,
`ImageService`, `ChapaClient`, `ChapaWebhookVerifier`, `RedisCacheService`/`NoOpCacheService`, plus
`CurrentUser`, `SubscriptionGate` and `ImageValidator`. These keep their interfaces: they are external
boundaries worth being able to swap or fake. **Handlers get no interface** — one implementation, injected
concretely.

Handlers are injected **per action** with `[FromServices]`, so an endpoint's signature is the complete list of
what it touches and adding one can't disturb another. Controllers hold no business logic: they normalise
input (clamp paging, check the user id) and hand the `Result` to `HandleResult`.

**To add a use case:** new handler class + one line in the feature's `Add<Feature>Feature()` method in
`LifetimeServicesCollectionExtensions.cs` + one controller action. Nothing is registered by assembly
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
  `AfroSmsService`), plus refresh tokens.
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
  else gets an empty set, so a missing `[Authorize]` can't leak). Unknown or missing role sees nothing.
  Add new listing queries through one of these two — never `db.Listings`, and never via the `b.Listing` /
  `a.Listing` navigation properties, which bypass the check (join to the authorized set instead).
- The service depends on `ICurrentUser`, so it only works inside a request. A background job that needs
  listings must take an explicit scope rather than calling it.
- Admin controllers use `[Authorize(Roles = "Admin")]` and route under `api/v{version}/admin/...`.
- Rate-limit policies `otp` and `auth` are defined in `Program.cs`, but the `[EnableRateLimiting]` attributes
  on `AuthController` are currently commented out.

### Data

`Data/ApplicationDbContext.cs` (`IdentityDbContext<ApplicationUser>`, Npgsql). All enums are stored as `int`
via `HasConversion<int>()`. `UserProfile`'s enum lists map to Postgres `int[]` through explicit converters;
`List<string>` maps to `text[]` natively — mirror this when adding array columns.

**Soft delete:** `Listing.DeletedAt` exists but the global `HasQueryFilter` is commented out, so `DeletedAt`
is filtered *explicitly* inside `ListingAuthorizationService` instead (and `IgnoreQueryFilters()` no longer
appears anywhere — it was a no-op). Note that `DeleteListingHandler` does a **hard** `db.Listings.Remove`, so
nothing sets `DeletedAt` today and `RestoreListingHandler` can never find a row. That handler is the switch if
you re-enable soft delete (the two-line change is written out in its comment), and the read side already
honours it.

### Subscriptions and payments (Chapa)

`SubscriptionService` + `ChapaClient` initiate a payment. `WebhooksController` accepts **both** the POST
webhook (signature-verified by `ChapaWebhookVerifier` against `chapa-signature`/`x-chapa-signature`) and the
GET callback, and both funnel into `HandleWebhookAsync`, which must stay **idempotent**. Tier is `Free | Pro`.
`Services/Jobs/SubscriptionExpiryJob` runs daily at 02:00 UTC via Hangfire (Postgres storage, dashboard at
`/hangfire`, currently unauthenticated).

### Infrastructure notes

- **Redis is optional.** `Program.cs` registers `RedisCacheService` only when the connection string is set and
  does *not* contain `localhost`; otherwise `NoOpCacheService` is used. Locally that means the feed cache is a
  no-op by default, even with `docker compose up`.
- **Storage:** `IStorageService` -> `S3StorageService` (buckets under `AWS:S3`). Cloudinary is still wired into
  `ImageService`. `ImageValidator` checks magic bytes on listing-image upload; `DocumentUploadOptions` caps
  documents at 5 MB and `.pdf .doc .docx`.
- API versioning is URL-segment based (`api/v1/...`), default 1.0. Scalar UI is exposed in Development **and
  Production**. CORS is `AllowAnyOrigin`.
- Serilog writes to console and `logs/easyfind_api_log.txt` at minimum level **Warning** — lower it when
  debugging.
- `Nullable` is **disabled** in `EasyFind.Api` but enabled in both test projects.

### Validation

FluentValidation validators exist in `Validators/` (`CreateListingValidator`, `UpdateListingValidator`,
`OnboardingValidator`) but are **not registered in DI and never invoked** — nothing calls them today. If you
add validation, either wire up `AddValidatorsFromAssembly` plus an auto-validation filter, or validate
explicitly in the handler and return `Result.Validation(...)`.

## Tests

- `EasyFind.UnitTests` — xUnit + FluentAssertions, pure functions only (`ListingScorerTests`).
- `EasyFind.IntegrationTests` — `WebApplicationFactory<Program>` (`Program` is `public partial` for this).
  `CustomWebApplicationFactory` swaps every EF/Npgsql registration for a single kept-open **SQLite in-memory**
  connection, calls `EnsureCreated()` (no migrations), forces the `Test` environment, and blanks the Redis
  connection string so the no-op cache is used. Postgres-specific SQL (`EF.Functions.ILike`, array columns)
  will not run under these tests.
