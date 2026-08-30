using System.Security.Claims;
using HooviePack.Api.Application;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HooviePack.Api.Tests;

public sealed class CoreBehaviorTests
{
    [Fact]
    public async Task Family_access_does_not_reveal_a_family_to_an_outsider()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        var outsider = CreateUser("outsider");
        db.Users.Add(outsider);
        await db.SaveChangesAsync();
        var service = new FamilyAccessService(db);

        var membership = await service.RequireMemberAsync(family.Id, owner.Id);
        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            service.RequireMemberAsync(family.Id, outsider.Id));

        Assert.Equal(FamilyRole.Owner, membership.Role);
        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task Reaction_toggle_adds_then_removes_the_same_reaction()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        var post = CreatePost(owner, family);
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var identity = new IdentityService(db);
        var service = new ReactionService(db, identity, new FamilyAccessService(db));
        var principal = Principal(owner.AuthProviderUserId);

        var added = await service.ToggleAsync(principal, post.Id, "paw");
        var removed = await service.ToggleAsync(principal, post.Id, "PAW");

        Assert.True(added.Added);
        Assert.Equal(1, added.Reactions.Counts["paw"]);
        Assert.False(removed.Added);
        Assert.Equal(0, removed.Reactions.Counts["paw"]);
        Assert.Empty(await db.Reactions.ToListAsync());
    }

    [Fact]
    public async Task Empty_post_without_photos_is_rejected()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        var service = CreatePostService(db);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            Principal(owner.AuthProviderUserId),
            family.Id,
            new CreatePostRequest { Content = "   " }));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Empty(await db.Posts.ToListAsync());
    }

    [Fact]
    public async Task Extreme_page_number_returns_an_empty_page_without_integer_overflow()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        db.Posts.Add(CreatePost(owner, family));
        await db.SaveChangesAsync();

        var response = await CreatePostService(db).ListAsync(
            Principal(owner.AuthProviderUserId),
            family.Id,
            int.MaxValue,
            50);

        Assert.Equal(int.MaxValue, response.Page);
        Assert.Equal(1, response.TotalCount);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Historical_post_and_comment_attribution_does_not_expose_author_bio()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        owner.Bio = "private current biography";
        var post = CreatePost(owner, family);
        post.Comments.Add(new Comment
        {
            AuthorUserId = owner.Id,
            AuthorUser = owner,
            Content = "A comment"
        });
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var response = await CreatePostService(db).GetAsync(
            Principal(owner.AuthProviderUserId),
            post.Id);

        Assert.Null(response.Author.Bio);
        Assert.Null(Assert.Single(response.Comments).Author.Bio);
    }

    [Fact]
    public async Task Comment_list_returns_the_newest_500_in_ascending_display_order()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        var post = CreatePost(owner, family);
        var start = DateTimeOffset.UtcNow.AddDays(-2);
        for (var index = 0; index <= 500; index++)
        {
            post.Comments.Add(new Comment
            {
                AuthorUserId = owner.Id,
                AuthorUser = owner,
                Content = $"comment-{index:D3}",
                CreatedAt = start.AddMinutes(index),
                UpdatedAt = start.AddMinutes(index)
            });
        }

        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var service = new CommentService(db, new IdentityService(db), new FamilyAccessService(db));

        var comments = await service.ListAsync(Principal(owner.AuthProviderUserId), post.Id);

        Assert.Equal(500, comments.Count);
        Assert.Equal("comment-001", comments.First().Content);
        Assert.Equal("comment-500", comments.Last().Content);
        Assert.True(comments.Zip(comments.Skip(1)).All(pair => pair.First.CreatedAt <= pair.Second.CreatedAt));
        Assert.All(comments, comment => Assert.Null(comment.Author.Bio));
    }

    [Fact]
    public async Task Dog_can_manage_is_true_for_creator_and_admin_but_false_for_another_member()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        var creator = CreateUser("dog-creator");
        var viewer = CreateUser("dog-viewer");
        db.Users.AddRange(creator, viewer);
        db.FamilyMemberships.AddRange(
            CreateMembership(family, creator, FamilyRole.Member),
            CreateMembership(family, viewer, FamilyRole.Member));
        await db.SaveChangesAsync();
        var service = new DogService(
            db,
            new IdentityService(db),
            new FamilyAccessService(db),
            new UnusedFileServiceClient(),
            new NoOpMediaCleanup());

        var created = await service.CreateAsync(
            Principal(creator.AuthProviderUserId),
            family.Id,
            new UpsertDogRequest { Name = "Hoovie" });
        var updated = await service.UpdateAsync(
            Principal(creator.AuthProviderUserId),
            family.Id,
            created.Id,
            new UpsertDogRequest { Name = "Hoovie Star" });
        var ownerView = await service.GetAsync(
            Principal(owner.AuthProviderUserId),
            family.Id,
            created.Id);
        var viewerList = await service.ListAsync(
            Principal(viewer.AuthProviderUserId),
            family.Id);

        Assert.True(created.CanManage);
        Assert.True(updated.CanManage);
        Assert.True(ownerView.CanManage);
        Assert.False(Assert.Single(viewerList).CanManage);
    }

    [Fact]
    public async Task Best_effort_cleanup_ignores_request_cancellation_and_does_not_rethrow_delete_failure()
    {
        var fileService = new ThrowingDeleteFileServiceClient();
        var service = new MediaCleanupService(fileService, NullLogger<MediaCleanupService>.Instance);

        await service.DeleteBestEffortAsync(Guid.CreateVersion7(), "test cleanup");

        Assert.Equal(1, fileService.DeleteAttempts);
        Assert.False(fileService.DeleteToken.CanBeCanceled);
    }

    [Fact]
    public async Task Exception_handler_maps_oversized_request_to_413_problem_details()
    {
        var problemWriter = new CapturingProblemDetailsService();
        var handler = new ApiExceptionHandler(problemWriter, NullLogger<ApiExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/posts";

        var handled = await handler.TryHandleAsync(
            context,
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal("Payload too large", problemWriter.Problem?.Title);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, problemWriter.Problem?.Status);
    }

    [Fact]
    public async Task Exception_handler_treats_aborted_request_as_client_closed_without_writing_a_body()
    {
        var problemWriter = new CapturingProblemDetailsService();
        var handler = new ApiExceptionHandler(problemWriter, NullLogger<ApiExceptionHandler>.Instance);
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };

        var handled = await handler.TryHandleAsync(
            context,
            new OperationCanceledException(aborted.Token),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.Null(problemWriter.Problem);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"hooviepack-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static PostService CreatePostService(AppDbContext db) => new(
        db,
        new IdentityService(db),
        new FamilyAccessService(db),
        new UnusedFileServiceClient(),
        new NoOpMediaCleanup());

    private static async Task<(AppUser Owner, Family Family)> SeedFamilyAsync(AppDbContext db)
    {
        var owner = CreateUser("owner");
        var family = new Family
        {
            Name = "Star Pack",
            Slug = $"star-pack-{Guid.NewGuid():N}"[..19],
            CreatedByUserId = owner.Id,
            CreatedByUser = owner
        };
        db.Users.Add(owner);
        db.Families.Add(family);
        db.FamilyMemberships.Add(CreateMembership(family, owner, FamilyRole.Owner));
        await db.SaveChangesAsync();
        return (owner, family);
    }

    private static FamilyMembership CreateMembership(Family family, AppUser user, FamilyRole role) => new()
    {
        FamilyId = family.Id,
        Family = family,
        UserId = user.Id,
        User = user,
        Role = role
    };

    private static Post CreatePost(AppUser author, Family family) => new()
    {
        FamilyId = family.Id,
        Family = family,
        AuthorUserId = author.Id,
        AuthorUser = author,
        Content = "Hello, pack!"
    };

    private static AppUser CreateUser(string subject) => new()
    {
        AuthProviderUserId = subject,
        Email = $"{subject}@example.test",
        DisplayName = subject,
        LastSeenAt = DateTimeOffset.UtcNow
    };

    private static ClaimsPrincipal Principal(string subject) => new(
        new ClaimsIdentity([new Claim("sub", subject)], "test"));

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetails? Problem { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Problem = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Problem = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class NoOpMediaCleanup : IMediaCleanupService
    {
        public Task DeleteBestEffortAsync(Guid? fileId, string reason) => Task.CompletedTask;

        public Task DeleteBestEffortAsync(IEnumerable<Guid> fileIds, string reason) => Task.CompletedTask;
    }

    private class UnusedFileServiceClient : IFileServiceClient
    {
        public long MaxImageBytes => 10 * 1024 * 1024;

        public Task<UploadResponse> CreateUploadAsync(
            CreateUploadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No upload should be created in this test.");

        public Task<FileMetadataResponse> CompleteUploadAsync(
            Guid fileId,
            string uploadToken,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No upload should be completed in this test.");

        public Task<DownloadResponse> GetDownloadAsync(
            Guid fileId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No download should be created in this test.");

        public virtual Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingDeleteFileServiceClient : UnusedFileServiceClient
    {
        public int DeleteAttempts { get; private set; }
        public CancellationToken DeleteToken { get; private set; }

        public override Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            DeleteAttempts++;
            DeleteToken = cancellationToken;
            throw new IOException("Simulated stale-file cleanup failure.");
        }
    }
}
