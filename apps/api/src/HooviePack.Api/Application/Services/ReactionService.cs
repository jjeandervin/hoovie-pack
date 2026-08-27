using System.Buffers.Binary;
using System.Data;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Security.Cryptography;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IReactionService
{
    Task<ToggleReactionResponse> ToggleAsync(ClaimsPrincipal principal, Guid postId, string type, CancellationToken cancellationToken = default);
    Task<ReactionSummaryResponse> RemoveAsync(ClaimsPrincipal principal, Guid postId, string type, CancellationToken cancellationToken = default);
}

public sealed class ReactionService(
    AppDbContext db,
    IIdentityService identityService,
    IFamilyAccessService accessService) : IReactionService
{
    public async Task<ToggleReactionResponse> ToggleAsync(
        ClaimsPrincipal principal,
        Guid postId,
        string type,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        var reactionType = ParseType(type);
        var added = await MutateAsync(
            postId,
            user.Id,
            reactionType,
            removeOnly: false,
            cancellationToken);
        return new ToggleReactionResponse(
            added,
            await BuildSummaryAsync(postId, user.Id, cancellationToken));
    }

    public async Task<ReactionSummaryResponse> RemoveAsync(
        ClaimsPrincipal principal,
        Guid postId,
        string type,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        var reactionType = ParseType(type);
        await MutateAsync(postId, user.Id, reactionType, removeOnly: true, cancellationToken);
        return await BuildSummaryAsync(postId, user.Id, cancellationToken);
    }

    private async Task<bool> MutateAsync(
        Guid postId,
        Guid userId,
        ReactionType reactionType,
        bool removeOnly,
        CancellationToken cancellationToken)
    {
        if (!IsPostgres())
        {
            return await MutateTrackedAsync(postId, userId, reactionType, removeOnly, cancellationToken);
        }

        var advisoryLockKey = BuildAdvisoryLockKey(postId, userId, reactionType);
        var executionStrategy = db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({advisoryLockKey})",
                cancellationToken);

            var added = await MutateTrackedAsync(
                postId,
                userId,
                reactionType,
                removeOnly,
                cancellationToken);
            try
            {
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception commitException) when (!cancellationToken.IsCancellationRequested)
            {
                // A connection loss during COMMIT can mean PostgreSQL committed even
                // though no acknowledgement arrived. Verify before allowing a retry,
                // otherwise a retry could invert the toggle a second time.
                await transaction.DisposeAsync();
                db.ChangeTracker.Clear();
                bool isPresent;
                try
                {
                    using var verificationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    isPresent = await db.Reactions.AsNoTracking().AnyAsync(
                        x => x.PostId == postId && x.UserId == userId && x.Type == reactionType,
                        verificationTimeout.Token);
                }
                catch (Exception)
                {
                    throw ApiException.ServiceUnavailable(
                        "The reaction may have been saved, but its final state could not be confirmed. Refresh before trying again.");
                }

                var expectedPresent = removeOnly ? false : added;
                if (isPresent == expectedPresent)
                {
                    return added;
                }

                ExceptionDispatchInfo.Capture(commitException).Throw();
                return added;
            }

            return added;
        });
    }

    private async Task<bool> MutateTrackedAsync(
        Guid postId,
        Guid userId,
        ReactionType reactionType,
        bool removeOnly,
        CancellationToken cancellationToken)
    {
        var reaction = await db.Reactions.SingleOrDefaultAsync(
            x => x.PostId == postId && x.UserId == userId && x.Type == reactionType,
            cancellationToken);
        if (reaction is not null)
        {
            db.Reactions.Remove(reaction);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (removeOnly)
        {
            return false;
        }

        db.Reactions.Add(new Reaction
        {
            PostId = postId,
            UserId = userId,
            Type = reactionType,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ReactionSummaryResponse> BuildSummaryAsync(
        Guid postId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var reactions = await db.Reactions
            .AsNoTracking()
            .Where(x => x.PostId == postId)
            .Select(x => new { x.UserId, x.Type })
            .ToListAsync(cancellationToken);
        var counts = Enum.GetValues<ReactionType>()
            .ToDictionary(
                type => type.ToString().ToLowerInvariant(),
                type => reactions.Count(x => x.Type == type));
        var mine = reactions
            .Where(x => x.UserId == userId)
            .Select(x => x.Type)
            .OrderBy(x => x)
            .ToList();
        return new ReactionSummaryResponse(counts, mine);
    }

    private bool IsPostgres() =>
        string.Equals(
            db.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal);

    private static long BuildAdvisoryLockKey(Guid postId, Guid userId, ReactionType type)
    {
        Span<byte> input = stackalloc byte[36];
        postId.TryWriteBytes(input[..16]);
        userId.TryWriteBytes(input[16..32]);
        BinaryPrimitives.WriteInt32LittleEndian(input[32..], (int)type);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }

    private static ReactionType ParseType(string type)
    {
        return Enum.TryParse<ReactionType>(type, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw ApiException.BadRequest("Reaction type must be paw, heart, or bone.", "type");
    }
}
