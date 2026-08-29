using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HooviePack.Api.Application.Services;

public sealed record HealthCheckEntryResponse(string Status, string? Description, double DurationMilliseconds);

public sealed record HealthResponse(
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? TotalDurationMilliseconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, HealthCheckEntryResponse>? Checks);

public interface IApplicationHealthService
{
    Task<HealthResponse> CheckAsync(bool readiness, CancellationToken cancellationToken = default);
}

public sealed class ApplicationHealthService(
    HealthCheckService healthCheckService,
    IHostEnvironment hostEnvironment) : IApplicationHealthService
{
    public async Task<HealthResponse> CheckAsync(bool readiness, CancellationToken cancellationToken = default)
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => readiness
                ? registration.Tags.Contains("ready") || registration.Tags.Contains("live")
                : registration.Tags.Contains("live"),
            cancellationToken);
        if (!hostEnvironment.IsDevelopment())
        {
            return new HealthResponse(
                report.Status.ToString().ToLowerInvariant(),
                TotalDurationMilliseconds: null,
                Checks: null);
        }

        var entries = report.Entries.ToDictionary(
            x => x.Key,
            x => new HealthCheckEntryResponse(
                x.Value.Status.ToString().ToLowerInvariant(),
                x.Value.Description,
                x.Value.Duration.TotalMilliseconds));
        return new HealthResponse(
            report.Status.ToString().ToLowerInvariant(),
            report.TotalDuration.TotalMilliseconds,
            entries);
    }
}
