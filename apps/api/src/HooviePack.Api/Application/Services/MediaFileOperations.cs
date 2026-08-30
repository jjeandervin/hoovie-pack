using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

internal static class MediaFileOperations
{
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public static bool IsSupportedContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && SupportedContentTypes.Contains(contentType.Trim());

    public static async Task RequireUnassociatedAsync(
        AppDbContext db,
        IEnumerable<Guid> fileIds,
        string field,
        CancellationToken cancellationToken)
    {
        var ids = fileIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var isAssociated = await db.Users.AnyAsync(x =>
                x.AvatarFileId != null && ids.Contains(x.AvatarFileId.Value), cancellationToken) ||
            await db.DogProfiles.AnyAsync(x =>
                x.PhotoFileId != null && ids.Contains(x.PhotoFileId.Value), cancellationToken) ||
            await db.PostPhotos.AnyAsync(x =>
                x.FileId != null && ids.Contains(x.FileId.Value), cancellationToken);
        if (isAssociated)
        {
            throw ApiException.BadRequest("One or more uploaded files are already in use.", field);
        }
    }

    public static async Task<FileMetadataResponse> CompleteImageAsync(
        IFileServiceClient fileServiceClient,
        IMediaCleanupService mediaCleanup,
        FileUploadReferenceRequest reference,
        string field,
        CancellationToken cancellationToken)
    {
        if (reference.FileId == Guid.Empty || string.IsNullOrWhiteSpace(reference.UploadToken))
        {
            throw ApiException.BadRequest("The uploaded image reference is invalid.", field);
        }

        FileMetadataResponse file;
        try
        {
            file = await fileServiceClient.CompleteUploadAsync(
                reference.FileId,
                reference.UploadToken,
                cancellationToken);
        }
        catch (FileServiceFileNotFoundException)
        {
            throw ApiException.BadRequest("The uploaded image was not found.", field);
        }
        catch (FileServiceRejectedRequestException)
        {
            throw ApiException.BadRequest("The uploaded image is incomplete or invalid.", field);
        }

        if (!IsSupportedContentType(file.ContentType) ||
            file.Size <= 0 ||
            file.Size > fileServiceClient.MaxImageBytes)
        {
            await mediaCleanup.DeleteBestEffortAsync(file.FileId, "completed upload failed media validation");
            throw ApiException.BadRequest(
                $"The uploaded image must be JPEG, PNG, or WebP and no larger than {fileServiceClient.MaxImageBytes / (1024 * 1024)} MB.",
                field);
        }

        var originalFileName = file.OriginalFileName.Trim().Replace('\\', '/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            originalFileName = "image";
        }
        else if (originalFileName.Length > 255)
        {
            originalFileName = originalFileName[..255];
        }

        return file with
        {
            OriginalFileName = originalFileName,
            ContentType = file.ContentType.Trim().ToLowerInvariant()
        };
    }

    public static async Task<DownloadResponse> GetDownloadAsync(
        IFileServiceClient fileServiceClient,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await fileServiceClient.GetDownloadAsync(fileId, cancellationToken);
        }
        catch (FileServiceFileNotFoundException)
        {
            throw ApiException.NotFound();
        }
        catch (FileServiceRejectedRequestException)
        {
            throw ApiException.NotFound();
        }
    }
}
