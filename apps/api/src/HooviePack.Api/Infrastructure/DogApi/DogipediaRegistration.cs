using HooviePack.Api.Application.Services;
using HooviePack.Api.Configuration;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace HooviePack.Api.Infrastructure.DogApi;

public static class DogipediaRegistration
{
    public static IServiceCollection AddDogipedia(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DogApiOptions>().Bind(configuration.GetSection(DogApiOptions.SectionName))
            .Validate(x => Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"), "DogApi:BaseUrl must be an absolute HTTP(S) URL.")
            .Validate(x => x.PageSize is >= 1 and <= 1000 && x.TimeoutSeconds is >= 1 and <= 120,
                "DogApi:PageSize must be 1–1000 and TimeoutSeconds must be 1–120.").ValidateOnStart();
        services.AddOptions<DogipediaSyncOptions>().Bind(configuration.GetSection(DogipediaSyncOptions.SectionName))
            .Validate(x => double.IsFinite(x.FreshnessHours) && x.FreshnessHours is > 0 and <= 8760 &&
                double.IsFinite(x.CheckIntervalMinutes) && x.CheckIntervalMinutes is > 0 and <= 1440,
                "Dogipedia sync intervals must be positive and within one year / one day.").ValidateOnStart();
        services.AddHttpClient<IDogApiClient, DogApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<DogApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddScoped<IDogipediaBreedService, DogipediaBreedService>();
        services.AddScoped<IDogipediaCatalogSyncService, DogipediaCatalogSyncService>();
        services.AddSingleton<IDogipediaSyncLock, DogipediaSyncLock>();
        services.AddHostedService<DogipediaSyncBackgroundService>();
        return services;
    }
}
