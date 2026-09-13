using HooviePack.Api.Application.Services;
using HooviePack.Api.Configuration;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HooviePack.Api.Infrastructure.DogApi;

public sealed class DogipediaSyncBackgroundService(IServiceScopeFactory scopes,
    IOptions<DogipediaSyncOptions> options, ILogger<DogipediaSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        await Task.Yield();
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.CheckIntervalMinutes));
        try
        {
            do
            {
                try { await CheckAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                { logger.LogError(ex, "{Component} {Operation} {Status}", "Dogipedia", "CatalogSyncCheck", "Failed"); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lastSuccess = await db.DogipediaSyncStates.AsNoTracking()
            .Where(x => x.Key == DogipediaSyncState.CatalogKey)
            .Select(x => x.LastSuccessfulSyncAtUtc).SingleOrDefaultAsync(cancellationToken);
        if (!DogipediaCatalogSyncService.IsFresh(lastSuccess, DateTimeOffset.UtcNow, options.Value.FreshnessHours))
            await scope.ServiceProvider.GetRequiredService<IDogipediaCatalogSyncService>().SyncAsync(false, cancellationToken);
    }
}
