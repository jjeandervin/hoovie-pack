using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IMediaService
{
    Task<UploadResponse> CreateUploadAsync(
        ClaimsPrincipal principal,
        InitializeMediaUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<DownloadResponse> GetPostPhotoAsync(
        ClaimsPrincipal principal,
        Guid photoId,
        CancellationToken cancellationToken = default);

    Task<DownloadResponse> GetDogPhotoAsync(
        ClaimsPrincipal principal,
        Guid dogId,
        CancellationToken cancellationToken = default);

    Task<DownloadResponse> GetAvatarAsync(
        ClaimsPrincipal principal,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class MediaService(
    AppDbContext db,
    IIdentityService identityService,
    IFamilyAccessService accessService,
    IFileServiceClient fileServiceClient) : IMediaService
{
    public async Task<UploadResponse> CreateUploadAsync(
        ClaimsPrincipal principal,
        InitializeMediaUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        switch (request.Purpose)
        {
            case UploadPurpose.Avatar:
                if (request.FamilyId is not null)
                {
                    throw ApiException.BadRequest("Avatar uploads cannot specify a family.", "familyId");
                }
                break;
            case UploadPurpose.DogPhoto:
            case UploadPurpose.PostPhoto:
                if (request.FamilyId is not { } familyId || familyId == Guid.Empty)
                {
                    throw ApiException.BadRequest("A family is required for this upload.", "familyId");
                }

                await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
                break;
            default:
                throw ApiException.BadRequest("The upload purpose is invalid.", "purpose");
        }

        var fileName = NormalizeFileName(request.FileName);
        var contentType = request.ContentType?.Trim().ToLowerInvariant();
        if (!MediaFileOperations.IsSupportedContentType(contentType))
        {
            throw ApiException.BadRequest("Only JPEG, PNG, and WebP images are accepted.", "contentType");
        }

        if (request.Size <= 0 || request.Size > fileServiceClient.MaxImageBytes)
        {
            throw ApiException.BadRequest(
                $"Images must be non-empty and no larger than {fileServiceClient.MaxImageBytes / (1024 * 1024)} MB.",
                "size");
        }

        try
        {
            var upload = await fileServiceClient.CreateUploadAsync(
                new CreateUploadRequest
                {
                    FileName = fileName,
                    ContentType = contentType!,
                    Size = request.Size
                },
                cancellationToken);
            return upload;
        }
        catch (FileServiceRejectedRequestException)
        {
            throw ApiException.BadRequest("The file metadata is invalid.", "file");
        }
    }

    public async Task<DownloadResponse> GetPostPhotoAsync(
        ClaimsPrincipal principal,
        Guid photoId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var photo = await db.PostPhotos
            .AsNoTracking()
            .Where(x => x.Id == photoId && x.FileId != null)
            .Select(x => new { FileId = x.FileId!.Value, x.Post.FamilyId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ApiException.NotFound();
        await accessService.RequireMemberAsync(photo.FamilyId, user.Id, cancellationToken);
        return await MediaFileOperations.GetDownloadAsync(fileServiceClient, photo.FileId, cancellationToken);
    }

    public async Task<DownloadResponse> GetDogPhotoAsync(
        ClaimsPrincipal principal,
        Guid dogId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var dog = await db.DogProfiles
            .AsNoTracking()
            .Where(x => x.Id == dogId && x.PhotoFileId != null)
            .Select(x => new { x.FamilyId, FileId = x.PhotoFileId!.Value })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ApiException.NotFound();
        await accessService.RequireMemberAsync(dog.FamilyId, user.Id, cancellationToken);
        return await MediaFileOperations.GetDownloadAsync(fileServiceClient, dog.FileId, cancellationToken);
    }

    public async Task<DownloadResponse> GetAvatarAsync(
        ClaimsPrincipal principal,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var mayView = currentUser.Id == userId || await db.FamilyMemberships
            .Where(x => x.UserId == currentUser.Id)
            .AnyAsync(
                mine => db.FamilyMemberships.Any(theirs =>
                    theirs.UserId == userId && theirs.FamilyId == mine.FamilyId),
                cancellationToken);
        if (!mayView)
        {
            throw ApiException.NotFound();
        }

        var fileId = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId && x.AvatarFileId != null)
            .Select(x => x.AvatarFileId)
            .SingleOrDefaultAsync(cancellationToken);
        if (fileId is null)
        {
            throw ApiException.NotFound();
        }

        return await MediaFileOperations.GetDownloadAsync(fileServiceClient, fileId.Value, cancellationToken);
    }

    private static string NormalizeFileName(string? fileName)
    {
        var normalized = fileName?.Trim().Replace('\\', '/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw ApiException.BadRequest("A file name is required.", "fileName");
        }

        return normalized.Length <= 255 ? normalized : normalized[..255];
    }
}
