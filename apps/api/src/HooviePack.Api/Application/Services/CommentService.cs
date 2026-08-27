using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface ICommentService
{
    Task<IReadOnlyCollection<CommentResponse>> ListAsync(ClaimsPrincipal principal, Guid postId, CancellationToken cancellationToken = default);
    Task<CommentResponse> CreateAsync(ClaimsPrincipal principal, Guid postId, UpsertCommentRequest request, CancellationToken cancellationToken = default);
    Task<CommentResponse> UpdateAsync(ClaimsPrincipal principal, Guid postId, Guid commentId, UpsertCommentRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(ClaimsPrincipal principal, Guid postId, Guid commentId, CancellationToken cancellationToken = default);
}

public sealed class CommentService(
    AppDbContext db,
    IIdentityService identityService,
    IFamilyAccessService accessService) : ICommentService
{
    public async Task<IReadOnlyCollection<CommentResponse>> ListAsync(
        ClaimsPrincipal principal,
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var (_, membership) = await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        var comments = await db.Comments
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Where(x => x.PostId == postId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
        return comments
            .OrderBy(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .Select(comment => MapComment(comment, user.Id, membership.Role))
            .ToList();
    }

    public async Task<CommentResponse> CreateAsync(
        ClaimsPrincipal principal,
        Guid postId,
        UpsertCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var (_, membership) = await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        var content = ValidateContent(request.Content);
        var now = DateTimeOffset.UtcNow;
        var comment = new Comment
        {
            PostId = postId,
            AuthorUserId = user.Id,
            AuthorUser = user,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);
        return MapComment(comment, user.Id, membership.Role);
    }

    public async Task<CommentResponse> UpdateAsync(
        ClaimsPrincipal principal,
        Guid postId,
        Guid commentId,
        UpsertCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var (_, membership) = await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        var comment = await db.Comments
            .Include(x => x.AuthorUser)
            .SingleOrDefaultAsync(x => x.Id == commentId && x.PostId == postId, cancellationToken)
            ?? throw ApiException.NotFound();
        if (comment.AuthorUserId != user.Id)
        {
            throw ApiException.Forbidden("Only the comment author can edit this comment.");
        }

        comment.Content = ValidateContent(request.Content);
        comment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return MapComment(comment, user.Id, membership.Role);
    }

    public async Task DeleteAsync(
        ClaimsPrincipal principal,
        Guid postId,
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var (_, membership) = await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        var comment = await db.Comments
            .SingleOrDefaultAsync(x => x.Id == commentId && x.PostId == postId, cancellationToken)
            ?? throw ApiException.NotFound();
        var isAdmin = membership.Role is FamilyRole.Owner or FamilyRole.Admin;
        if (comment.AuthorUserId != user.Id && !isAdmin)
        {
            throw ApiException.Forbidden("Only the comment author or a family admin can delete this comment.");
        }

        db.Comments.Remove(comment);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static CommentResponse MapComment(Comment comment, Guid currentUserId, FamilyRole role) => new(
        comment.Id,
        comment.PostId,
        new UserSummaryResponse(
            comment.AuthorUser.Id,
            comment.AuthorUser.DisplayName,
            comment.AuthorUser.AvatarUrl,
            null),
        comment.Content,
        comment.CreatedAt,
        comment.UpdatedAt,
        comment.AuthorUserId == currentUserId || role is FamilyRole.Owner or FamilyRole.Admin);

    private static string ValidateContent(string content)
    {
        var normalized = content.Trim();
        if (normalized.Length == 0)
        {
            throw ApiException.BadRequest("Comment content is required.", "content");
        }

        if (normalized.Length > 500)
        {
            throw ApiException.BadRequest("Comments cannot exceed 500 characters.", "content");
        }

        return normalized;
    }
}
