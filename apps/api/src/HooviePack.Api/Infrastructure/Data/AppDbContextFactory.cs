using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HooviePack.Api.Infrastructure.Data;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var projectDirectory = ResolveProjectDirectory();
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environments.Development;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(projectDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required for EF Core design-time commands.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }

    private static string ResolveProjectDirectory()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(currentDirectory, "appsettings.json")))
        {
            return currentDirectory;
        }

        var projectDirectory = Path.Combine(currentDirectory, "src", "HooviePack.Api");
        if (File.Exists(Path.Combine(projectDirectory, "appsettings.json")))
        {
            return projectDirectory;
        }

        projectDirectory = Path.Combine(currentDirectory, "apps", "api", "src", "HooviePack.Api");
        if (File.Exists(Path.Combine(projectDirectory, "appsettings.json")))
        {
            return projectDirectory;
        }

        throw new InvalidOperationException(
            "Run EF Core design-time commands from the repository root, apps/api, or apps/api/src/HooviePack.Api.");
    }
}
