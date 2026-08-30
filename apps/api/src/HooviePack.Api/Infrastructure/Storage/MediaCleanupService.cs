namespace HooviePack.Api.Infrastructure.Storage;

public interface IMediaCleanupService
{
    Task DeleteBestEffortAsync(Guid? fileId, string reason);
    Task DeleteBestEffortAsync(IEnumerable<Guid> fileIds, string reason);
}

public sealed class MediaCleanupService(
    IFileServiceClient fileServiceClient,
    ILogger<MediaCleanupService> logger) : IMediaCleanupService
{
    public async Task DeleteBestEffortAsync(Guid? fileId, string reason)
    {
        if (fileId is null || fileId == Guid.Empty)
        {
            return;
        }

        try
        {
            await fileServiceClient.DeleteAsync(fileId.Value, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Best-effort media cleanup failed for file {FileId}. Reason: {Reason}",
                fileId,
                reason);
        }
    }

    public async Task DeleteBestEffortAsync(IEnumerable<Guid> fileIds, string reason)
    {
        foreach (var fileId in fileIds.Where(x => x != Guid.Empty).Distinct())
        {
            await DeleteBestEffortAsync(fileId, reason);
        }
    }
}
