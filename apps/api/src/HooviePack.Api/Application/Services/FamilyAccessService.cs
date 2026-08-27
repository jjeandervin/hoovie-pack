using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IFamilyAccessService
{
    Task<FamilyMembership> RequireMemberAsync(Guid familyId, Guid userId, CancellationToken cancellationToken = default);
    Task<FamilyMembership> RequireAdminAsync(Guid familyId, Guid userId, CancellationToken cancellationToken = default);
    Task<(Post Post, FamilyMembership Membership)> RequirePostAccessAsync(Guid postId, Guid userId, CancellationToken cancellationToken = default);
}

public sealed class FamilyAccessService(AppDbContext db) : IFamilyAccessService
{
    public async Task<FamilyMembership> RequireMemberAsync(
        Guid familyId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var membership = await db.FamilyMemberships
            .SingleOrDefaultAsync(x => x.FamilyId == familyId && x.UserId == userId, cancellationToken);

        return membership ?? throw ApiException.NotFound();
    }

    public async Task<FamilyMembership> RequireAdminAsync(
        Guid familyId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var membership = await RequireMemberAsync(familyId, userId, cancellationToken);
        if (membership.Role is not (FamilyRole.Owner or FamilyRole.Admin))
        {
            throw ApiException.Forbidden("Only family owners and admins can perform this action.");
        }

        return membership;
    }

    public async Task<(Post Post, FamilyMembership Membership)> RequirePostAccessAsync(
        Guid postId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var post = await db.Posts.SingleOrDefaultAsync(x => x.Id == postId, cancellationToken)
            ?? throw ApiException.NotFound();
        var membership = await RequireMemberAsync(post.FamilyId, userId, cancellationToken);
        return (post, membership);
    }
}
