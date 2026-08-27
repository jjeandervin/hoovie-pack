using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IFamilyService
{
    Task<IReadOnlyCollection<FamilySummaryResponse>> ListAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<FamilyResponse> GetAsync(ClaimsPrincipal principal, Guid familyId, CancellationToken cancellationToken = default);
    Task<FamilyResponse> CreateAsync(ClaimsPrincipal principal, CreateFamilyRequest request, CancellationToken cancellationToken = default);
    Task<FamilyResponse> UpdateAsync(ClaimsPrincipal principal, Guid familyId, UpdateFamilyRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MemberResponse>> ListMembersAsync(ClaimsPrincipal principal, Guid familyId, CancellationToken cancellationToken = default);
    Task<MemberResponse> UpdateMemberRoleAsync(ClaimsPrincipal principal, Guid familyId, Guid membershipId, UpdateMemberRoleRequest request, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(ClaimsPrincipal principal, Guid familyId, Guid membershipId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<InviteResponse>> ListInvitesAsync(ClaimsPrincipal principal, Guid familyId, CancellationToken cancellationToken = default);
    Task<InviteResponse> CreateInviteAsync(ClaimsPrincipal principal, Guid familyId, CreateInviteRequest request, CancellationToken cancellationToken = default);
    Task RevokeInviteAsync(ClaimsPrincipal principal, Guid familyId, Guid inviteId, CancellationToken cancellationToken = default);
    Task<FamilyResponse> JoinAsync(ClaimsPrincipal principal, JoinFamilyRequest request, CancellationToken cancellationToken = default);
}

public sealed partial class FamilyService(
    AppDbContext db,
    IIdentityService identityService,
    IFamilyAccessService accessService) : IFamilyService
{
    public async Task<IReadOnlyCollection<FamilySummaryResponse>> ListAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        return await db.FamilyMemberships
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.Family.Name)
            .Select(x => new FamilySummaryResponse(
                x.Family.Id,
                x.Family.Name,
                x.Family.Slug,
                x.Family.Description,
                x.Role,
                x.Family.Memberships.Count,
                x.Family.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<FamilyResponse> GetAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        return await MapFamilyAsync(familyId, membership.Role, cancellationToken);
    }

    public async Task<FamilyResponse> CreateAsync(
        ClaimsPrincipal principal,
        CreateFamilyRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var name = RequireName(request.Name);
        var now = DateTimeOffset.UtcNow;
        var family = new Family
        {
            Name = name,
            Slug = BuildSlug(name),
            Description = Normalize(request.Description),
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        var membership = new FamilyMembership
        {
            Family = family,
            UserId = user.Id,
            Role = FamilyRole.Owner,
            JoinedAt = now
        };

        db.Families.Add(family);
        db.FamilyMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);
        return await MapFamilyAsync(family.Id, FamilyRole.Owner, cancellationToken);
    }

    public async Task<FamilyResponse> UpdateAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        UpdateFamilyRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireAdminAsync(familyId, user.Id, cancellationToken);
        var family = await db.Families.SingleAsync(x => x.Id == familyId, cancellationToken);
        family.Name = RequireName(request.Name);
        family.Description = Normalize(request.Description);
        family.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await MapFamilyAsync(family.Id, membership.Role, cancellationToken);
    }

    public async Task<IReadOnlyCollection<MemberResponse>> ListMembersAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);

        return await db.FamilyMemberships
            .AsNoTracking()
            .Where(x => x.FamilyId == familyId)
            .OrderBy(x => x.Role == FamilyRole.Owner ? 0 : x.Role == FamilyRole.Admin ? 1 : 2)
            .ThenBy(x => x.User.DisplayName)
            .Select(x => new MemberResponse(
                x.Id,
                x.UserId,
                x.User.DisplayName,
                x.User.AvatarUrl,
                x.User.Bio,
                x.Role,
                x.JoinedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<MemberResponse> UpdateMemberRoleAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        Guid membershipId,
        UpdateMemberRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var actor = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        if (actor.Role != FamilyRole.Owner)
        {
            throw ApiException.Forbidden("Only the family owner can change member roles.");
        }

        if (!Enum.IsDefined(request.Role) || request.Role == FamilyRole.Owner)
        {
            throw ApiException.BadRequest("Role must be admin or member; ownership transfer is not supported by this endpoint.", "role");
        }

        var target = await db.FamilyMemberships
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == membershipId && x.FamilyId == familyId, cancellationToken)
            ?? throw ApiException.NotFound();
        if (target.Role == FamilyRole.Owner)
        {
            throw ApiException.BadRequest("The owner role cannot be changed.", "role");
        }

        target.Role = request.Role;
        await db.SaveChangesAsync(cancellationToken);
        return MapMember(target);
    }

    public async Task RemoveMemberAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var actor = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        var target = await db.FamilyMemberships
            .SingleOrDefaultAsync(x => x.Id == membershipId && x.FamilyId == familyId, cancellationToken)
            ?? throw ApiException.NotFound();

        if (target.Role == FamilyRole.Owner)
        {
            throw ApiException.BadRequest("The family owner cannot be removed.");
        }

        var removingSelf = target.UserId == user.Id;
        if (!removingSelf && actor.Role is not (FamilyRole.Owner or FamilyRole.Admin))
        {
            throw ApiException.Forbidden();
        }

        if (!removingSelf && actor.Role == FamilyRole.Admin && target.Role == FamilyRole.Admin)
        {
            throw ApiException.Forbidden("Admins cannot remove other admins.");
        }

        db.FamilyMemberships.Remove(target);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<InviteResponse>> ListInvitesAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        await accessService.RequireAdminAsync(familyId, user.Id, cancellationToken);
        return await db.FamilyInvites
            .AsNoTracking()
            .Where(x => x.FamilyId == familyId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new InviteResponse(
                x.Id,
                x.CodeHint,
                x.CreatedAt,
                x.ExpiresAt,
                x.RedeemedAt != null,
                x.RevokedAt != null,
                null))
            .ToListAsync(cancellationToken);
    }

    public async Task<InviteResponse> CreateInviteAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        CreateInviteRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        await accessService.RequireAdminAsync(familyId, user.Id, cancellationToken);
        if (request.ExpiresInDays is < 1 or > 30)
        {
            throw ApiException.BadRequest("Invite expiration must be between 1 and 30 days.", "expiresInDays");
        }

        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var invite = new FamilyInvite
        {
            FamilyId = familyId,
            CodeHash = HashInviteCode(code),
            CodeHint = code[..8],
            CreatedByUserId = user.Id,
            CreatedAt = now,
            ExpiresAt = now.AddDays(request.ExpiresInDays)
        };
        db.FamilyInvites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);
        return new InviteResponse(
            invite.Id,
            invite.CodeHint,
            invite.CreatedAt,
            invite.ExpiresAt,
            false,
            false,
            code);
    }

    public async Task RevokeInviteAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        Guid inviteId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        await accessService.RequireAdminAsync(familyId, user.Id, cancellationToken);
        var invite = await db.FamilyInvites
            .SingleOrDefaultAsync(x => x.Id == inviteId && x.FamilyId == familyId, cancellationToken)
            ?? throw ApiException.NotFound();
        if (invite.RedeemedAt is not null)
        {
            throw ApiException.Conflict("A redeemed invitation cannot be revoked.");
        }

        invite.RevokedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<FamilyResponse> JoinAsync(
        ClaimsPrincipal principal,
        JoinFamilyRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var code = request.InviteCode.Trim();
        if (code.Length < 10)
        {
            throw ApiException.BadRequest("The invite code is invalid.", "inviteCode");
        }

        var hash = HashInviteCode(code);
        var userId = user.Id;
        var executionStrategy = db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            // A failed serializable attempt can leave tracked state from the aborted
            // transaction. Every retry must begin from a clean view of the database.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var invite = await db.FamilyInvites
                .SingleOrDefaultAsync(x => x.CodeHash == hash, cancellationToken)
                ?? throw ApiException.BadRequest("The invite code is invalid or no longer available.", "inviteCode");

            var existing = await db.FamilyMemberships
                .SingleOrDefaultAsync(x => x.FamilyId == invite.FamilyId && x.UserId == userId, cancellationToken);
            if (existing is not null)
            {
                var existingFamily = await MapFamilyAsync(invite.FamilyId, existing.Role, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return existingFamily;
            }

            var now = DateTimeOffset.UtcNow;
            if (invite.RevokedAt is not null || invite.RedeemedAt is not null || invite.ExpiresAt <= now)
            {
                throw ApiException.BadRequest("The invite code is invalid or no longer available.", "inviteCode");
            }

            db.FamilyMemberships.Add(new FamilyMembership
            {
                FamilyId = invite.FamilyId,
                UserId = userId,
                Role = FamilyRole.Member,
                JoinedAt = now
            });
            invite.RedeemedAt = now;
            invite.RedeemedByUserId = userId;
            await db.SaveChangesAsync(cancellationToken);
            var joinedFamily = await MapFamilyAsync(invite.FamilyId, FamilyRole.Member, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return joinedFamily;
        });
    }

    private async Task<FamilyResponse> MapFamilyAsync(
        Guid familyId,
        FamilyRole role,
        CancellationToken cancellationToken)
    {
        return await db.Families
            .AsNoTracking()
            .Where(x => x.Id == familyId)
            .Select(x => new FamilyResponse(
                x.Id,
                x.Name,
                x.Slug,
                x.Description,
                x.CreatedByUserId,
                role,
                x.Memberships.Count,
                x.CreatedAt,
                x.UpdatedAt))
            .SingleAsync(cancellationToken);
    }

    private static MemberResponse MapMember(FamilyMembership membership) => new(
        membership.Id,
        membership.UserId,
        membership.User.DisplayName,
        membership.User.AvatarUrl,
        membership.User.Bio,
        membership.Role,
        membership.JoinedAt);

    private static string RequireName(string name)
    {
        var normalized = name.Trim();
        if (normalized.Length == 0)
        {
            throw ApiException.BadRequest("Family name is required.", "name");
        }

        if (normalized.Length > 100)
        {
            throw ApiException.BadRequest("Family name cannot exceed 100 characters.", "name");
        }

        return normalized;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string HashInviteCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static string BuildSlug(string name)
    {
        var baseSlug = NonSlugCharacterRegex().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        baseSlug = MultipleDashRegex().Replace(baseSlug, "-");
        if (baseSlug.Length == 0)
        {
            baseSlug = "pack";
        }

        if (baseSlug.Length > 108)
        {
            baseSlug = baseSlug[..108].TrimEnd('-');
        }

        return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(baseSlug.Length + 9, 120)];
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacterRegex();

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultipleDashRegex();
}
