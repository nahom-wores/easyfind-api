using Amazon.SQS;
using EasyFind.Api.Features.Admin.Commands;
using EasyFind.Api.Features.Admin.Queries;
using EasyFind.Api.Features.Applications.Commands;
using EasyFind.Api.Features.Applications.Queries;
using EasyFind.Api.Features.Bookmarks.Commands;
using EasyFind.Api.Features.Bookmarks.Queries;
using EasyFind.Api.Features.Documents.Commands;
using EasyFind.Api.Features.Documents.Queries;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Features.Listings.Commands;
using EasyFind.Api.Features.Listings.Queries;
using EasyFind.Api.Features.Profile.Commands;
using EasyFind.Api.Features.Profile.Queries;
using EasyFind.Api.Features.Subscriptions.Commands;
using EasyFind.Api.Features.Subscriptions.Queries;
using EasyFind.Api.Features.Users.Commands;
using EasyFind.Api.Features.Users.Queries;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Dto.Profile;
using EasyFind.Api.Validators;
using FluentValidation;
using EasyFind.Api.Services;
using EasyFind.Api.Services.Gemini;
using EasyFind.Api.Services.IServices;
using EasyFind.Api.Services.Jobs;

// The whole application graph, in one file, in dependency order.
//
// Two kinds of registration and the difference matters:
//
//   Infrastructure  — an interface + an implementation. These are capabilities
//                     (send an SMS, store a file, sign a token) that many use
//                     cases call. The interface earns its keep: it marks an
//                     external boundary and lets the implementation be swapped.
//
//   Handlers        — a concrete class, no interface. One handler = one use
//                     case = one file, injected straight into a controller
//                     action with [FromServices]. There is only ever one
//                     implementation, so an interface would be indirection
//                     pointing at nothing.
//
// Nothing is registered by assembly scanning. "Why did this resolve?" should
// always be answerable by reading this file.
public static class LifetimeServicesCollectionExtensions
{
    public static IServiceCollection AddLifetimeServices(this IServiceCollection services)
    {
        services.AddInfrastructure();

        services.AddListingsFeature();
        services.AddBookmarksFeature();
        services.AddApplicationsFeature();
        services.AddProfileFeature();
        services.AddDocumentsFeature();
        services.AddSubscriptionsFeature();
        services.AddUsersFeature();
        services.AddAdminFeature();

        return services;
    }

    // ── Infrastructure: external boundaries and cross-cutting helpers ────────
    private static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ITokenService, TokenService>();       // JWT + refresh tokens
        services.AddScoped<ISmsService, AfroSmsService>();       // OTP delivery
        services.AddScoped<IStorageService, S3StorageService>(); // documents + listing images
        services.AddScoped<IImageService, ImageService>();       // Cloudinary
        services.AddHttpClient<IChapaClient, ChapaClient>();     // payment gateway
        services.AddScoped<IChapaWebhookVerifier, ChapaWebhookVerifier>();
        services.AddScoped<ICurrentUser, CurrentUser>();         // claims of the caller
        services.AddScoped<IOtpThrottle, OtpThrottleService>();  // per-phone OTP limits
        services.AddAWSService<IAmazonSQS>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        // Request validators, enforced by ValidationFilter. Registered by hand
        // like everything else: a DTO with no validator here is simply not
        // validated beyond its DataAnnotations.
        services.AddScoped<IValidator<CreateListingDto>, CreateListingValidator>();
        services.AddScoped<IValidator<UpdateListingDto>, UpdateListingValidator>();
        services.AddScoped<IValidator<OnboardingDto>, OnboardingValidator>();
        services.AddScoped<SubscriptionGate>();                  // free-vs-paid policy
        services.AddScoped<SubscriptionExpiryJob>();             // nightly Hangfire job
        services.AddOptions<GeminiOptions>()
            .BindConfiguration(GeminiOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IChatModel, GeminiClient>(c =>
        {
            c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            c.Timeout = TimeSpan.FromSeconds(20);
        });
        // NOTE: IRedisCacheService is registered in Program.cs instead — the
        // implementation depends on whether Redis is actually configured.
        return services;
    }

    // ── Features: one line per use case ─────────────────────────────────────
    // Adding a use case = new handler class + one line here + one controller
    // action. Miss the line and the endpoint fails fast on its first request.

    private static IServiceCollection AddListingsFeature(this IServiceCollection services)
    {
        // The only place allowed to read db.Listings — see the class for why.
        services.AddScoped<ListingAuthorizationService>();

        services.AddScoped<CreateListingHandler>();
        services.AddScoped<UpdateListingHandler>();
        services.AddScoped<DeleteListingHandler>();
        services.AddScoped<RestoreListingHandler>();
        services.AddScoped<SetListingActiveHandler>();
        services.AddScoped<UploadListingImageHandler>();

        services.AddScoped<GetFeedHandler>();
        services.AddScoped<GetListingDetailHandler>();
        services.AddScoped<GetAdminListingHandler>();
        services.AddScoped<ListAdminListingsHandler>();
        return services;
    }

    private static IServiceCollection AddBookmarksFeature(this IServiceCollection services)
    {
        services.AddScoped<AddBookmarkHandler>();
        services.AddScoped<RemoveBookmarkHandler>();
        services.AddScoped<GetUserBookmarksHandler>();
        return services;
    }

    private static IServiceCollection AddApplicationsFeature(this IServiceCollection services)
    {
        services.AddScoped<CreateApplicationHandler>();
        services.AddScoped<UpdateApplicationHandler>();
        services.AddScoped<DeleteApplicationHandler>();
        services.AddScoped<GetUserApplicationsHandler>();
        return services;
    }

    private static IServiceCollection AddProfileFeature(this IServiceCollection services)
    {
        services.AddScoped<UpsertProfileHandler>();
        services.AddScoped<GetProfileHandler>();
        return services;
    }

    private static IServiceCollection AddDocumentsFeature(this IServiceCollection services)
    {
        services.AddScoped<UploadDocumentHandler>();
        services.AddScoped<DeleteDocumentHandler>();
        services.AddScoped<GetUserDocumentsHandler>();
        services.AddScoped<GetDocumentDownloadUrlHandler>();
        return services;
    }

    private static IServiceCollection AddSubscriptionsFeature(this IServiceCollection services)
    {
        services.AddScoped<InitiateSubscriptionHandler>();
        services.AddScoped<ProcessChapaPaymentHandler>();   // webhook AND callback
        services.AddScoped<GetMySubscriptionStatusHandler>();
        return services;
    }

    private static IServiceCollection AddUsersFeature(this IServiceCollection services)
    {
        services.AddScoped<RequestOtpHandler>();
        services.AddScoped<VerifyOtpHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<AssignRoleHandler>();
        services.AddScoped<UpdateCurrentUserHandler>();
        services.AddScoped<UpdateProfilePictureHandler>();
        services.AddScoped<RequestPhoneChangeHandler>();
        services.AddScoped<ConfirmPhoneChangeHandler>();
        services.AddScoped<GetCurrentUserHandler>();
        return services;
    }

    private static IServiceCollection AddAdminFeature(this IServiceCollection services)
    {
        services.AddScoped<GrantSubscriptionHandler>();
        services.AddScoped<RevokeSubscriptionHandler>();

        services.AddScoped<ListUsersHandler>();
        services.AddScoped<GetUserDetailHandler>();
        services.AddScoped<ListPaymentsHandler>();
        services.AddScoped<GetOverviewStatsHandler>();
        return services;
    }
}
