using System.Buffers.Binary;
using System.Security.Claims;
using HooviePack.Api.Application;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
            new UnusedFileStorage(),
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
    public async Task Image_inspector_fully_decodes_a_real_png()
    {
        var path = TempFile(".png");
        try
        {
            using (var image = new Image<Rgba32>(640, 480))
            {
                await image.SaveAsPngAsync(path);
            }

            var metadata = await ImageFileInspector.InspectAsync(path);

            Assert.Equal("image/png", metadata.ContentType);
            Assert.Equal(".png", metadata.Extension);
            Assert.Equal(640, metadata.Width);
            Assert.Equal(480, metadata.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Image_inspector_rejects_a_structural_fake_with_a_png_header()
    {
        var path = TempFile(".png");
        try
        {
            await File.WriteAllBytesAsync(path, BuildPngHeader(640, 480));

            await Assert.ThrowsAsync<InvalidMediaException>(() => ImageFileInspector.InspectAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Image_inspector_normalizes_malformed_overflow_dimensions()
    {
        var path = TempFile(".png");
        try
        {
            await File.WriteAllBytesAsync(path, BuildPngHeader(uint.MaxValue, uint.MaxValue));

            await Assert.ThrowsAsync<InvalidMediaException>(() => ImageFileInspector.InspectAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Image_inspector_rejects_a_valid_but_unsupported_gif()
    {
        var path = TempFile(".gif");
        try
        {
            using (var image = new Image<Rgba32>(10, 10))
            {
                await image.SaveAsGifAsync(path);
            }

            await Assert.ThrowsAsync<InvalidMediaException>(() => ImageFileInspector.InspectAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Best_effort_cleanup_ignores_request_cancellation_and_does_not_rethrow_delete_failure()
    {
        var storage = new ThrowingDeleteFileStorage();
        var service = new MediaCleanupService(storage, NullLogger<MediaCleanupService>.Instance);

        await service.DeleteBestEffortAsync("posts/example.png", "test cleanup");

        Assert.Equal(1, storage.DeleteAttempts);
        Assert.False(storage.DeleteToken.CanBeCanceled);
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
            new BadHttpRequestException("Multipart body length limit exceeded.", StatusCodes.Status413PayloadTooLarge),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal("Payload too large", problemWriter.Problem?.Title);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, problemWriter.Problem?.Status);
    }

    [Fact]
    public async Task Exception_handler_maps_multipart_form_limit_to_413_problem_details()
    {
        var problemWriter = new CapturingProblemDetailsService();
        var handler = new ApiExceptionHandler(problemWriter, NullLogger<ApiExceptionHandler>.Instance);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidDataException("Multipart body length limit 44040192 exceeded."),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal("Payload too large", problemWriter.Problem?.Title);
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
        new UnusedFileStorage(),
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

    private static string TempFile(string extension) =>
        Path.Combine(Path.GetTempPath(), $"hooviepack-{Guid.NewGuid():N}{extension}");

    private static byte[] BuildPngHeader(uint width, uint height)
    {
        var bytes = new byte[45];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8;
        bytes[25] = 6;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(33, 4), 0);
        "IEND"u8.CopyTo(bytes.AsSpan(37, 4));
        return bytes;
    }

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
        public Task DeleteBestEffortAsync(string? storagePath, string reason) => Task.CompletedTask;

        public Task DeleteBestEffortAsync(IEnumerable<string> storagePaths, string reason) => Task.CompletedTask;
    }

    private class UnusedFileStorage : IFileStorage
    {
        public long MaxImageBytes => 10 * 1024 * 1024;

        public Task<StoredImage> StoreImageAsync(
            Stream input,
            string originalFileName,
            string category,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No image should be stored in this test.");

        public Task<StoredFile?> OpenReadAsync(
            string storagePath,
            string contentType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No image should be opened in this test.");

        public virtual Task DeleteAsync(string? storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingDeleteFileStorage : UnusedFileStorage
    {
        public int DeleteAttempts { get; private set; }
        public CancellationToken DeleteToken { get; private set; }

        public override Task DeleteAsync(string? storagePath, CancellationToken cancellationToken = default)
        {
            DeleteAttempts++;
            DeleteToken = cancellationToken;
            throw new IOException("Simulated stale-file cleanup failure.");
        }
    }
}
