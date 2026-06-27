using EasyFind.Api.Services;
using EasyFind.Api.Services.IServices;

public static class LifetimeServicesCollectionExtensions
{
    public static IServiceCollection AddLifetimeServices(this
        IServiceCollection services)
    {
        services.AddScoped<IFeedService, FeedService>();
        services.AddScoped<IBookmarkService, BookmarkService>();
        services.AddHttpClient();

        //services.AddTransient<,>();
        //services.AddSingleton<,>();
        return services;
    }
}