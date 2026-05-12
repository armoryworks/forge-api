using Microsoft.Extensions.Http.Resilience;

namespace Forge.Api.Extensions;

public static class HttpResilienceExtensions
{
    public static IServiceCollection AddResilientHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient("resilient")
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            });

        return services;
    }
}
