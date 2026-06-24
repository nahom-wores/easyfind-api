public static class LifetimeServicesCollectionExtensions
{
    public static IServiceCollection AddLifetimeServices(this
        IServiceCollection services)
    {
        //services.AddScoped<IPropertyService, PropertyService>();

        services.AddHttpClient();

        //services.AddTransient<,>();
        //services.AddSingleton<,>();
        return services;
    }
}