using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IDogService
{
    Task<IReadOnlyCollection<DogResponse>> ListAsync(ClaimsPrincipal principal, Guid familyId, CancellationToken cancellationToken = default);
    Task<DogResponse> GetAsync(ClaimsPrincipal principal, Guid familyId, Guid dogId, CancellationToken cancellationToken = default);
    Task<DogResponse> CreateAsync(ClaimsPrincipal principal, Guid familyId, UpsertDogRequest request, CancellationToken cancellationToken = default);
    Task<DogResponse> UpdateAsync(ClaimsPrincipal principal, Guid familyId, Guid dogId, UpsertDogRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(ClaimsPrincipal principal, Guid familyId, Guid dogId, CancellationToken cancellationToken = default);
}

public sealed class DogService(
    AppDbContext db,
    IIdentityService identityService,
    IFamilyAccessService accessService,
    IFileServiceClient fileServiceClient,
    IMediaCleanupService mediaCleanup) : IDogService
{
    public async Task<IReadOnlyCollection<DogResponse>> ListAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        var dogs = await DogQuery()
            .AsNoTracking()
            .Where(x => x.FamilyId == familyId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return dogs.Select(dog => MapDog(dog, user.Id, membership.Role)).ToList();
    }

    public async Task<DogResponse> GetAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        Guid dogId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        var dog = await DogQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == dogId && x.FamilyId == familyId, cancellationToken)
            ?? throw ApiException.NotFound();
        return MapDog(dog, user.Id, membership.Role);
    }

    public async Task<DogResponse> CreateAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        UpsertDogRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        await ValidateRequestAsync(familyId, request, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var dog = new DogProfile
        {
            FamilyId = familyId,
            Name = RequireName(request.Name),
            Breed = Normalize(request.Breed),
            Birthday = request.Birthday,
            ApproximateAgeYears = request.ApproximateAgeYears,
            Bio = Normalize(request.Bio),
            FavoriteThing = Normalize(request.FavoriteThing),
            OwnerMembershipId = request.OwnerMembershipId,
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        FileMetadataResponse? stored = null;
        if (request.PhotoFile is not null)
        {
            stored = await CompletePhotoAsync(request.PhotoFile, cancellationToken);
            dog.PhotoFileId = stored.FileId;
            dog.PhotoStoragePath = null;
            dog.PhotoContentType = stored.ContentType;
            dog.PhotoUrl = $"/api/media/dogs/{dog.Id}";
        }

        try
        {
            db.DogProfiles.Add(dog);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await mediaCleanup.DeleteBestEffortAsync(stored?.FileId, "dog profile creation failed");
            throw;
        }

        return await LoadResponseAsync(dog.Id, user.Id, membership.Role, cancellationToken);
    }

    public async Task<DogResponse> UpdateAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        Guid dogId,
        UpsertDogRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        var dog = await db.DogProfiles
            .SingleOrDefaultAsync(x => x.Id == dogId && x.FamilyId == familyId, cancellationToken)
            ?? throw ApiException.NotFound();
        EnsureCanManage(dog, user.Id, membership.Role);
        await ValidateRequestAsync(familyId, request, cancellationToken);

        FileMetadataResponse? stored = null;
        if (request.PhotoFile is not null)
        {
            stored = await CompletePhotoAsync(request.PhotoFile, cancellationToken);
        }

        var previousPhotoFileId = dog.PhotoFileId;
        try
        {
            dog.Name = RequireName(request.Name);
            dog.Breed = Normalize(request.Breed);
            dog.Birthday = request.Birthday;
            dog.ApproximateAgeYears = request.ApproximateAgeYears;
            dog.Bio = Normalize(request.Bio);
            dog.FavoriteThing = Normalize(request.FavoriteThing);
            dog.OwnerMembershipId = request.OwnerMembershipId;
            dog.UpdatedAt = DateTimeOffset.UtcNow;

            if (stored is not null)
            {
                dog.PhotoFileId = stored.FileId;
                dog.PhotoStoragePath = null;
                dog.PhotoContentType = stored.ContentType;
                dog.PhotoUrl = $"/api/media/dogs/{dog.Id}";
            }
            else if (request.RemovePhoto)
            {
                dog.PhotoFileId = null;
                dog.PhotoStoragePath = null;
                dog.PhotoContentType = null;
                dog.PhotoUrl = null;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await mediaCleanup.DeleteBestEffortAsync(stored?.FileId, "dog profile update failed");
            throw;
        }

        if ((stored is not null || request.RemovePhoto) && previousPhotoFileId != dog.PhotoFileId)
        {
            await mediaCleanup.DeleteBestEffortAsync(previousPhotoFileId, "dog photo was replaced after database commit");
        }

        return await LoadResponseAsync(dog.Id, user.Id, membership.Role, cancellationToken);
    }

    public async Task DeleteAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        Guid dogId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        var dog = await db.DogProfiles
            .SingleOrDefaultAsync(x => x.Id == dogId && x.FamilyId == familyId, cancellationToken)
            ?? throw ApiException.NotFound();
        EnsureCanManage(dog, user.Id, membership.Role);
        var fileId = dog.PhotoFileId;
        db.DogProfiles.Remove(dog);
        await db.SaveChangesAsync(cancellationToken);
        await mediaCleanup.DeleteBestEffortAsync(fileId, "dog profile was deleted after database commit");
    }

    private async Task ValidateRequestAsync(
        Guid familyId,
        UpsertDogRequest request,
        CancellationToken cancellationToken)
    {
        _ = RequireName(request.Name);
        if (request.Birthday > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw ApiException.BadRequest("Birthday cannot be in the future.", "birthday");
        }

        if (request.Birthday is not null && request.ApproximateAgeYears is not null)
        {
            throw ApiException.BadRequest("Provide either a birthday or an approximate age, not both.", "birthday");
        }

        if (request.OwnerMembershipId is not null)
        {
            var ownerExists = await db.FamilyMemberships.AnyAsync(
                x => x.Id == request.OwnerMembershipId && x.FamilyId == familyId,
                cancellationToken);
            if (!ownerExists)
            {
                throw ApiException.BadRequest("The selected owner is not a member of this family.", "ownerMembershipId");
            }
        }
    }

    private Task<FileMetadataResponse> CompletePhotoAsync(
        FileUploadReferenceRequest photo,
        CancellationToken cancellationToken) =>
        CompleteUnassociatedPhotoAsync(photo, cancellationToken);

    private async Task<FileMetadataResponse> CompleteUnassociatedPhotoAsync(
        FileUploadReferenceRequest photo,
        CancellationToken cancellationToken)
    {
        await MediaFileOperations.RequireUnassociatedAsync(
            db,
            [photo.FileId],
            "photoFile",
            cancellationToken);
        return await MediaFileOperations.CompleteImageAsync(
            fileServiceClient,
            mediaCleanup,
            photo,
            "photoFile",
            cancellationToken);
    }

    private IQueryable<DogProfile> DogQuery() => db.DogProfiles
        .Include(x => x.OwnerMembership)
        .ThenInclude(x => x!.User);

    private async Task<DogResponse> LoadResponseAsync(
        Guid dogId,
        Guid currentUserId,
        FamilyRole role,
        CancellationToken cancellationToken)
    {
        var dog = await DogQuery().AsNoTracking().SingleAsync(x => x.Id == dogId, cancellationToken);
        return MapDog(dog, currentUserId, role);
    }

    private static DogResponse MapDog(DogProfile dog, Guid currentUserId, FamilyRole role)
    {
        UserSummaryResponse? owner = null;
        if (dog.OwnerMembership?.User is { } ownerUser)
        {
            owner = new UserSummaryResponse(ownerUser.Id, ownerUser.DisplayName, ownerUser.AvatarUrl, ownerUser.Bio);
        }

        return new DogResponse(
            dog.Id,
            dog.FamilyId,
            dog.Name,
            dog.PhotoUrl,
            dog.Breed,
            dog.Birthday,
            dog.ApproximateAgeYears,
            dog.Bio,
            dog.FavoriteThing,
            dog.OwnerMembershipId,
            owner,
            CanManage(dog, currentUserId, role),
            dog.CreatedAt,
            dog.UpdatedAt);
    }

    private static void EnsureCanManage(DogProfile dog, Guid userId, FamilyRole role)
    {
        if (!CanManage(dog, userId, role))
        {
            throw ApiException.Forbidden("Only the creator or a family admin can manage this dog profile.");
        }
    }

    private static bool CanManage(DogProfile dog, Guid userId, FamilyRole role) =>
        dog.CreatedByUserId == userId || role is FamilyRole.Owner or FamilyRole.Admin;

    private static string RequireName(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0
            ? throw ApiException.BadRequest("Dog name is required.", "name")
            : normalized;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
