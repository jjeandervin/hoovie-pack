using System.Text.Json;
using HooviePack.Api.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HooviePack.Api.Tests;

public sealed class SecurityBehaviorTests
{
    [Fact]
    public async Task Production_health_response_omits_check_details_and_timing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddCheck(
            "postgres",
            () => HealthCheckResult.Unhealthy(
                "PostgreSQL is unavailable at an internal host.",
                new InvalidOperationException("ConnectionStrings:DefaultConnection was rejected.")),
            tags: ["ready"]);

        await using var provider = services.BuildServiceProvider();
        var service = new ApplicationHealthService(
            provider.GetRequiredService<HealthCheckService>(),
            new TestHostEnvironment { EnvironmentName = Environments.Production });

        var response = await service.CheckAsync(readiness: true);
        var json = JsonSerializer.Serialize(response);

        Assert.Equal("unhealthy", response.Status);
        Assert.Null(response.TotalDurationMilliseconds);
        Assert.Null(response.Checks);
        Assert.DoesNotContain("postgres", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duration", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "HooviePack.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
