namespace HooviePack.Api.Infrastructure.Storage;

public interface IMediaCleanupService
{
    Task DeleteBestEffortAsync(string? storagePath, string reason);
    Task DeleteBestEffortAsync(IEnumerable<string> storagePaths, string reason);
}

public sealed class MediaCleanupService(
    IFileStorage fileStorage,
    ILogger<MediaCleanupService> logger) : IMediaCleanupService
{
    public async Task DeleteBestEffortAsync(string? storagePath, string reason)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return;
        }

        try
        {
            await fileStorage.DeleteAsync(storagePath, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Best-effort media cleanup failed for {StoragePath}. Reason: {Reason}",
                storagePath,
                reason);
        }
    }

    public async Task DeleteBestEffortAsync(IEnumerable<string> storagePaths, string reason)
    {
        foreach (var storagePath in storagePaths.Distinct(StringComparer.Ordinal))
        {
            await DeleteBestEffortAsync(storagePath, reason);
        }
    }
}
