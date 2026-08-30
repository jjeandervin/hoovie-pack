using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IPostService
{
    Task<PagedResponse<PostResponse>> ListAsync(ClaimsPrincipal principal, Guid familyId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PostResponse> GetAsync(ClaimsPrincipal principal, Guid postId, CancellationToken cancellationToken = default);
    Task<PostResponse> CreateAsync(ClaimsPrincipal principal, Guid familyId, CreatePostRequest request, CancellationToken cancellationToken = default);
    Task<PostResponse> UpdateAsync(ClaimsPrincipal principal, Guid postId, UpdatePostRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(ClaimsPrincipal principal, Guid postId, CancellationToken cancellationToken = default);
}

public sealed class PostService(
    AppDbContext db,
    IIdentityService identityService,
    IFamilyAccessService accessService,
    IFileServiceClient fileServiceClient,
    IMediaCleanupService mediaCleanup) : IPostService
{
    private const int MaxPhotos = 4;

    public async Task<PagedResponse<PostResponse>> ListAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var familyPosts = db.Posts.AsNoTracking().Where(x => x.FamilyId == familyId);
        var totalCount = await familyPosts.CountAsync(cancellationToken);
        var skip = (long)(page - 1) * pageSize;
        if (skip >= totalCount)
        {
            return new PagedResponse<PostResponse>([], page, pageSize, totalCount);
        }

        var posts = await PostGraph(asTracking: false)
            .Where(x => x.FamilyId == familyId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(checked((int)skip))
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var commentCounts = await LoadCommentCountsAsync(posts.Select(x => x.Id), cancellationToken);
        var responses = posts
            .Select(post => MapPost(
                post,
                user.Id,
                membership.Role,
                commentCounts.GetValueOrDefault(post.Id)))
            .ToList();
        return new PagedResponse<PostResponse>(responses, page, pageSize, totalCount);
    }

    public async Task<PostResponse> GetAsync(
        ClaimsPrincipal principal,
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var (_, membership) = await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        return await LoadResponseAsync(postId, user.Id, membership.Role, cancellationToken);
    }

    public async Task<PostResponse> CreateAsync(
        ClaimsPrincipal principal,
        Guid familyId,
        CreatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var membership = await accessService.RequireMemberAsync(familyId, user.Id, cancellationToken);
        var content = NormalizeContent(request.Content);
        var photoFiles = request.PhotoFiles ?? [];
        ValidatePost(content, photoFiles.Count);

        var storedImages = await CompletePhotosAsync(photoFiles, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var post = new Post
        {
            FamilyId = familyId,
            AuthorUserId = user.Id,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now
        };
        for (var index = 0; index < storedImages.Count; index++)
        {
            var image = storedImages[index];
            post.Photos.Add(new PostPhoto
            {
                FileId = image.FileId,
                StoragePath = null,
                OriginalFileName = image.OriginalFileName,
                ContentType = image.ContentType,
                Width = 0,
                Height = 0,
                SortOrder = index,
                CreatedAt = now
            });
        }

        try
        {
            db.Posts.Add(post);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await mediaCleanup.DeleteBestEffortAsync(
                storedImages.Select(x => x.FileId),
                "post creation failed");
            throw;
        }

        return await LoadResponseAsync(post.Id, user.Id, membership.Role, cancellationToken);
    }

    public async Task<PostResponse> UpdateAsync(
        ClaimsPrincipal principal,
        Guid postId,
        UpdatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var (_, membership) = await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        var post = await db.Posts
            .Include(x => x.Photos)
            .SingleAsync(x => x.Id == postId, cancellationToken);
        if (post.AuthorUserId != user.Id)
        {
            throw ApiException.Forbidden("Only the post author can edit this post.");
        }

        var removedPhotoIds = request.RemovedPhotoIds ?? [];
        var photoFiles = request.PhotoFiles ?? [];
        var removalIds = removedPhotoIds.Distinct().ToHashSet();
        if (removalIds.Count != removedPhotoIds.Count || removalIds.Any(id => post.Photos.All(photo => photo.Id != id)))
        {
            throw ApiException.BadRequest("One or more removed photo IDs are invalid.", "removedPhotoIds");
        }

        var remainingPhotoCount = post.Photos.Count - removalIds.Count;
        var content = NormalizeContent(request.Content);
        ValidatePost(content, remainingPhotoCount + photoFiles.Count);
        var storedImages = await CompletePhotosAsync(photoFiles, cancellationToken);
        var removedPhotos = post.Photos.Where(x => removalIds.Contains(x.Id)).ToList();
        var nextSortOrder = post.Photos.Where(x => !removalIds.Contains(x.Id)).Select(x => x.SortOrder).DefaultIfEmpty(-1).Max() + 1;

        try
        {
            db.PostPhotos.RemoveRange(removedPhotos);
            foreach (var image in storedImages)
            {
                post.Photos.Add(new PostPhoto
                {
                    FileId = image.FileId,
                    StoragePath = null,
                    OriginalFileName = image.OriginalFileName,
                    ContentType = image.ContentType,
                    Width = 0,
                    Height = 0,
                    SortOrder = nextSortOrder++,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            post.Content = content;
            post.IsEdited = true;
            post.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await mediaCleanup.DeleteBestEffortAsync(
                storedImages.Select(x => x.FileId),
                "post update failed");
            throw;
        }

        await mediaCleanup.DeleteBestEffortAsync(
            removedPhotos.Where(x => x.FileId.HasValue).Select(x => x.FileId!.Value),
            "post photos were removed after database commit");
        return await LoadResponseAsync(post.Id, user.Id, membership.Role, cancellationToken);
    }

    public async Task DeleteAsync(
        ClaimsPrincipal principal,
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityService.GetCurrentUserAsync(principal, cancellationToken);
        var (_, membership) = await accessService.RequirePostAccessAsync(postId, user.Id, cancellationToken);
        var post = await db.Posts
            .Include(x => x.Photos)
            .SingleAsync(x => x.Id == postId, cancellationToken);
        var isAdmin = membership.Role is FamilyRole.Owner or FamilyRole.Admin;
        if (post.AuthorUserId != user.Id && !isAdmin)
        {
            throw ApiException.Forbidden("Only the post author or a family admin can delete this post.");
        }

        var fileIds = post.Photos.Where(x => x.FileId.HasValue).Select(x => x.FileId!.Value).ToList();
        db.Posts.Remove(post);
        await db.SaveChangesAsync(cancellationToken);
        await mediaCleanup.DeleteBestEffortAsync(fileIds, "post was deleted after database commit");
    }

    private IQueryable<Post> PostGraph(bool asTracking)
    {
        var query = db.Posts
            .Include(x => x.AuthorUser)
            .Include(x => x.Photos.OrderBy(photo => photo.SortOrder))
            .Include(x => x.Comments.OrderByDescending(comment => comment.CreatedAt).Take(5))
                .ThenInclude(comment => comment.AuthorUser)
            .Include(x => x.Reactions)
            .AsQueryable();
        return asTracking ? query : query.AsNoTracking();
    }

    private async Task<PostResponse> LoadResponseAsync(
        Guid postId,
        Guid userId,
        FamilyRole role,
        CancellationToken cancellationToken)
    {
        var post = await PostGraph(asTracking: false)
            .AsSplitQuery()
            .SingleAsync(x => x.Id == postId, cancellationToken);
        var commentCount = await db.Comments.CountAsync(x => x.PostId == postId, cancellationToken);
        return MapPost(post, userId, role, commentCount);
    }

    private async Task<Dictionary<Guid, int>> LoadCommentCountsAsync(
        IEnumerable<Guid> postIds,
        CancellationToken cancellationToken)
    {
        var ids = postIds.ToArray();
        return await db.Comments
            .AsNoTracking()
            .Where(x => ids.Contains(x.PostId))
            .GroupBy(x => x.PostId)
            .Select(group => new { PostId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.PostId, x => x.Count, cancellationToken);
    }

    private static PostResponse MapPost(Post post, Guid currentUserId, FamilyRole role, int commentCount)
    {
        var isAdmin = role is FamilyRole.Owner or FamilyRole.Admin;
        var author = new UserSummaryResponse(
            post.AuthorUser.Id,
            post.AuthorUser.DisplayName,
            post.AuthorUser.AvatarUrl,
            null);
        var photos = post.Photos
            .OrderBy(x => x.SortOrder)
            .Select(x => new PostPhotoResponse(
                x.Id,
                $"/api/media/post-photos/{x.Id}",
                x.OriginalFileName,
                x.ContentType,
                x.Width,
                x.Height,
                x.SortOrder))
            .ToList();
        var comments = post.Comments
            .OrderBy(x => x.CreatedAt)
            .Select(x => new CommentResponse(
                x.Id,
                x.PostId,
                new UserSummaryResponse(
                    x.AuthorUser.Id,
                    x.AuthorUser.DisplayName,
                    x.AuthorUser.AvatarUrl,
                    null),
                x.Content,
                x.CreatedAt,
                x.UpdatedAt,
                x.AuthorUserId == currentUserId || isAdmin))
            .ToList();
        var counts = Enum.GetValues<ReactionType>()
            .ToDictionary(
                type => type.ToString().ToLowerInvariant(),
                type => post.Reactions.Count(x => x.Type == type));
        var myReactions = post.Reactions
            .Where(x => x.UserId == currentUserId)
            .Select(x => x.Type)
            .OrderBy(x => x)
            .ToList();

        return new PostResponse(
            post.Id,
            post.FamilyId,
            author,
            post.Content,
            post.CreatedAt,
            post.UpdatedAt,
            post.IsEdited,
            post.AuthorUserId == currentUserId,
            post.AuthorUserId == currentUserId || isAdmin,
            photos,
            comments,
            commentCount,
            new ReactionSummaryResponse(counts, myReactions));
    }

    private static string NormalizeContent(string? content) => content?.Trim() ?? string.Empty;

    private static void ValidatePost(string content, int photoCount)
    {
        if (content.Length > 2000)
        {
            throw ApiException.BadRequest("Post content cannot exceed 2,000 characters.", "content");
        }

        if (photoCount is < 0 or > MaxPhotos)
        {
            throw ApiException.BadRequest($"A post can contain at most {MaxPhotos} photos.", "photos");
        }

        if (content.Length == 0 && photoCount == 0)
        {
            throw ApiException.BadRequest("A post needs text or at least one photo.", "content");
        }
    }

    private async Task<List<FileMetadataResponse>> CompletePhotosAsync(
        IReadOnlyCollection<FileUploadReferenceRequest> photos,
        CancellationToken cancellationToken)
    {
        if (photos.Count > MaxPhotos)
        {
            throw ApiException.BadRequest($"A post can contain at most {MaxPhotos} photos.", "photos");
        }

        var duplicateFileId = photos
            .GroupBy(x => x.FileId)
            .FirstOrDefault(group => group.Key == Guid.Empty || group.Count() > 1);
        if (duplicateFileId is not null)
        {
            throw ApiException.BadRequest("Each uploaded photo reference must be unique and valid.", "photoFiles");
        }

        await MediaFileOperations.RequireUnassociatedAsync(
            db,
            photos.Select(x => x.FileId),
            "photoFiles",
            cancellationToken);

        var storedImages = new List<FileMetadataResponse>(photos.Count);
        try
        {
            foreach (var photo in photos)
            {
                storedImages.Add(await MediaFileOperations.CompleteImageAsync(
                    fileServiceClient,
                    mediaCleanup,
                    photo,
                    "photoFiles",
                    cancellationToken));
            }

            return storedImages;
        }
        catch
        {
            await mediaCleanup.DeleteBestEffortAsync(
                storedImages.Select(x => x.FileId),
                "partial post photo upload failed");
            throw;
        }
    }
}
