using System.Security.Claims;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IMediaService
{
    Task<StoredFile> GetPostPhotoAsync(ClaimsPrincipal principal, Guid photoId, CancellationToken cancellationToken = default);
    Task<StoredFile> GetDogPhotoAsync(ClaimsPrincipal principal, Guid dogId, CancellationToken cancellationToken = default);
    Task<StoredFile> GetAvatarAsync(ClaimsPrincipal principal, Guid userId, CancellationToken cancellationToken = default);
}

public sealed class MediaService(
    AppDbContext db,
    IIdentityService identityService,
    IFamilyAccessService accessService,
    IFileStorage fileStorage) : IMediaService
{
    public async Task<StoredFile> GetPostPhotoAsync(
        ClaimsPrincipal principal,
        Guid photoId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var photo = await db.PostPhotos
            .AsNoTracking()
            .Where(x => x.Id == photoId)
            .Select(x => new { x.StoragePath, x.ContentType, x.Post.FamilyId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ApiException.NotFound();
        await accessService.RequireMemberAsync(photo.FamilyId, user.Id, cancellationToken);
        return await OpenRequiredAsync(photo.StoragePath, photo.ContentType, cancellationToken);
    }

    public async Task<StoredFile> GetDogPhotoAsync(
        ClaimsPrincipal principal,
        Guid dogId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var dog = await db.DogProfiles
            .AsNoTracking()
            .Where(x => x.Id == dogId && x.PhotoStoragePath != null && x.PhotoContentType != null)
            .Select(x => new { x.FamilyId, x.PhotoStoragePath, x.PhotoContentType })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ApiException.NotFound();
        await accessService.RequireMemberAsync(dog.FamilyId, user.Id, cancellationToken);
        return await OpenRequiredAsync(dog.PhotoStoragePath!, dog.PhotoContentType!, cancellationToken);
    }

    public async Task<StoredFile> GetAvatarAsync(
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

        var avatar = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId && x.AvatarStoragePath != null && x.AvatarContentType != null)
            .Select(x => new { x.AvatarStoragePath, x.AvatarContentType })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ApiException.NotFound();
        return await OpenRequiredAsync(avatar.AvatarStoragePath!, avatar.AvatarContentType!, cancellationToken);
    }

    private async Task<StoredFile> OpenRequiredAsync(
        string storagePath,
        string contentType,
        CancellationToken cancellationToken)
    {
        return await fileStorage.OpenReadAsync(storagePath, contentType, cancellationToken)
            ?? throw ApiException.NotFound();
    }
}
