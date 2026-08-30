using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HooviePack.Api.Infrastructure.Storage;

public sealed class FileServiceHealthCheck(HttpClient httpClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("health/ready", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("File service is reachable.")
                : HealthCheckResult.Unhealthy("File service is unavailable.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("File service is unavailable.", exception);
        }
    }
}
