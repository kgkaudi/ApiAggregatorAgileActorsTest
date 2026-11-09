using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Sources;
using Polly;

namespace ApiAggregator.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers every external API. To add a new one, add a line here.</summary>
    public static IServiceCollection AddExternalDataSources(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDataSource<GitHubDataSource>(configuration, "GitHub");
        services.AddDataSource<HackerNewsDataSource>(configuration, "HackerNews");
        services.AddDataSource<WikipediaDataSource>(configuration, "Wikipedia");
        return services;
    }

    /// <summary>
    /// Registers a data source with its own typed <see cref="HttpClient"/> (base address and optional
    /// bearer token from <c>ExternalApis:{configName}</c>) and a standard resilience pipeline:
    /// retry with exponential backoff + jitter for transient errors (5xx, 408, 429, network),
    /// per-attempt timeout, total timeout and a circuit breaker.
    /// </summary>
    public static IServiceCollection AddDataSource<TSource>(this IServiceCollection services, IConfiguration configuration, string configName)
        where TSource : class, IDataSource
    {
        var api = configuration.GetSection($"ExternalApis:{configName}").Get<ExternalApiOptions>()
                  ?? throw new InvalidOperationException($"Missing configuration section 'ExternalApis:{configName}'.");

        services.AddTransient<IDataSource>(sp => sp.GetRequiredService<TSource>());

        services.AddHttpClient<TSource>(client =>
            {
                client.BaseAddress = new Uri(api.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ApiAggregator/1.0 (+https://github.com/your-user/api-aggregator)");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                if (!string.IsNullOrWhiteSpace(api.Token))
                    client.DefaultRequestHeaders.Authorization = new("Bearer", api.Token);
            })
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 2;
                o.Retry.Delay = TimeSpan.FromMilliseconds(300);
                o.Retry.BackoffType = DelayBackoffType.Exponential;
                o.Retry.UseJitter = true;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(14);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });

        return services;
    }
}
