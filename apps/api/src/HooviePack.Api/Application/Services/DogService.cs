using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
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
    IFileStorage fileStorage,
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

        StoredImage? stored = null;
        if (request.Photo is not null)
        {
            stored = await StorePhotoAsync(request.Photo, cancellationToken);
            dog.PhotoStoragePath = stored.StoragePath;
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
            await mediaCleanup.DeleteBestEffortAsync(stored?.StoragePath, "dog profile creation failed");
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

        StoredImage? stored = null;
        if (request.Photo is not null)
        {
            stored = await StorePhotoAsync(request.Photo, cancellationToken);
        }

        var previousPhotoPath = dog.PhotoStoragePath;
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
                dog.PhotoStoragePath = stored.StoragePath;
                dog.PhotoContentType = stored.ContentType;
                dog.PhotoUrl = $"/api/media/dogs/{dog.Id}";
            }
            else if (request.RemovePhoto)
            {
                dog.PhotoStoragePath = null;
                dog.PhotoContentType = null;
                dog.PhotoUrl = null;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await mediaCleanup.DeleteBestEffortAsync(stored?.StoragePath, "dog profile update failed");
            throw;
        }

        if ((stored is not null || request.RemovePhoto) && previousPhotoPath != dog.PhotoStoragePath)
        {
            await mediaCleanup.DeleteBestEffortAsync(previousPhotoPath, "dog photo was replaced after database commit");
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
        var storagePath = dog.PhotoStoragePath;
        db.DogProfiles.Remove(dog);
        await db.SaveChangesAsync(cancellationToken);
        await mediaCleanup.DeleteBestEffortAsync(storagePath, "dog profile was deleted after database commit");
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

    private async Task<StoredImage> StorePhotoAsync(IFormFile photo, CancellationToken cancellationToken)
    {
        if (photo.Length <= 0 || photo.Length > fileStorage.MaxImageBytes)
        {
            throw ApiException.BadRequest(
                $"Dog photo must be a non-empty image no larger than {fileStorage.MaxImageBytes / (1024 * 1024)} MB.",
                "photo");
        }

        try
        {
            await using var input = photo.OpenReadStream();
            return await fileStorage.StoreImageAsync(input, photo.FileName, "dogs", cancellationToken);
        }
        catch (InvalidMediaException exception)
        {
            throw ApiException.BadRequest(exception.Message, "photo");
        }
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
