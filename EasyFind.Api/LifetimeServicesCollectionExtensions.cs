using EasyFind.Api.Services;
using EasyFind.Api.Services.IServices;
using EasyFind.Api.Services.Jobs;

public static class LifetimeServicesCollectionExtensions
{
    public static IServiceCollection AddLifetimeServices(this
        IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IFeedService, FeedService>();
        services.AddScoped<IBookmarkService, BookmarkService>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ISmsService, AfroSmsService>();
        services.AddScoped<IImageService, ImageService>();
        services.AddScoped<IRedisCacheService, RedisCacheService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IListingAdminService, ListingAdminService>();
        services.AddHttpClient<IChapaClient, ChapaClient>();
        services.AddScoped<IChapaWebhookVerifier, ChapaWebhookVerifier>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<SubscriptionGate>();
        services.AddScoped<SubscriptionExpiryJob>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IAdminStatsService, AdminStatsService>();
        services.AddScoped<IAdminSubscriptionService, AdminSubscriptionService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IStorageService, S3StorageService>();
        //services.AddHttpClient();

        //services.AddTransient<,>();
        //services.AddSingleton<,>();
        return services;
    }
}