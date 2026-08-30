using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IProfileService
{
    Task<MeResponse> GetMeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<MeResponse> UpdateMeAsync(ClaimsPrincipal principal, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task<MeResponse> UpdateAvatarAsync(ClaimsPrincipal principal, FileUploadReferenceRequest avatar, CancellationToken cancellationToken = default);
    Task<UserSummaryResponse> GetUserAsync(ClaimsPrincipal principal, Guid userId, CancellationToken cancellationToken = default);
}

public sealed class ProfileService(
    AppDbContext db,
    IIdentityService identityService,
    IFileServiceClient fileServiceClient,
    IMediaCleanupService mediaCleanup) : IProfileService
{
    public async Task<MeResponse> GetMeAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        return MapMe(user);
    }

    public async Task<MeResponse> UpdateMeAsync(
        ClaimsPrincipal principal,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var displayName = request.DisplayName.Trim();
        if (displayName.Length == 0)
        {
            throw ApiException.BadRequest("Display name is required.", "displayName");
        }

        user.DisplayName = displayName;
        user.Bio = Normalize(request.Bio);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return MapMe(user);
    }

    public async Task<MeResponse> UpdateAvatarAsync(
        ClaimsPrincipal principal,
        FileUploadReferenceRequest avatar,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        await MediaFileOperations.RequireUnassociatedAsync(
            db,
            [avatar.FileId],
            "avatar",
            cancellationToken);
        var stored = await MediaFileOperations.CompleteImageAsync(
            fileServiceClient,
            mediaCleanup,
            avatar,
            "avatar",
            cancellationToken);

        var previousFileId = user.AvatarFileId;
        try
        {
            user.AvatarFileId = stored.FileId;
            user.AvatarStoragePath = null;
            user.AvatarContentType = stored.ContentType;
            user.AvatarUrl = $"/api/media/avatars/{user.Id}";
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await mediaCleanup.DeleteBestEffortAsync(stored.FileId, "avatar database update failed");
            throw;
        }

        if (previousFileId != stored.FileId)
        {
            await mediaCleanup.DeleteBestEffortAsync(previousFileId, "avatar was replaced after database commit");
        }

        return MapMe(user);
    }

    public async Task<UserSummaryResponse> GetUserAsync(
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

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw ApiException.NotFound();
        return new UserSummaryResponse(user.Id, user.DisplayName, user.AvatarUrl, user.Bio);
    }

    private static MeResponse MapMe(Domain.AppUser user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.AvatarUrl,
        user.Bio,
        user.CreatedAt,
        user.LastSeenAt);

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
