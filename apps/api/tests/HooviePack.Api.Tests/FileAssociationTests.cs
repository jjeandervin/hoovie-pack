using System.Security.Claims;
using HooviePack.Api.Application;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HooviePack.Api.Tests;

public sealed class FileAssociationTests
{
    [Fact]
    public async Task Avatar_replacement_stores_only_the_file_id_and_cleans_up_the_previous_file()
    {
        await using var db = CreateDb();
        var user = CreateUser("owner");
        var previousFileId = Guid.CreateVersion7();
        user.AvatarFileId = previousFileId;
        user.AvatarStoragePath = "avatars/legacy.jpg";
        user.AvatarContentType = "image/jpeg";
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var newFileId = Guid.CreateVersion7();
        var fileService = new RecordingFileServiceClient();
        fileService.CompletedFiles[newFileId] = Completed(newFileId, "new-avatar.png", "image/png");
        fileService.OnDeleteAsync = async fileId =>
        {
            if (fileId == previousFileId)
            {
                var persisted = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
                Assert.Equal(newFileId, persisted.AvatarFileId);
                Assert.Null(persisted.AvatarStoragePath);
            }
        };
        var cleanup = new MediaCleanupService(fileService, NullLogger<MediaCleanupService>.Instance);
        var service = new ProfileService(db, new IdentityService(db), fileService, cleanup);

        var response = await service.UpdateAvatarAsync(
            Principal(user.AuthProviderUserId),
            Reference(newFileId));

        Assert.Equal($"/api/media/avatars/{user.Id}", response.AvatarUrl);
        Assert.Equal(newFileId, user.AvatarFileId);
        Assert.Null(user.AvatarStoragePath);
        Assert.Equal("image/png", user.AvatarContentType);
        Assert.Equal([previousFileId], fileService.DeletedFileIds);
    }

    [Fact]
    public async Task Post_association_persists_file_metadata_without_a_storage_path()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        var fileId = Guid.CreateVersion7();
        var fileService = new RecordingFileServiceClient();
        fileService.CompletedFiles[fileId] = Completed(fileId, "pack-photo.webp", "image/webp");
        var service = CreatePostService(db, fileService);

        var response = await service.CreateAsync(
            Principal(owner.AuthProviderUserId),
            family.Id,
            new CreatePostRequest
            {
                PhotoFiles = [Reference(fileId)]
            });

        var photo = await db.PostPhotos.SingleAsync();
        Assert.Equal(fileId, photo.FileId);
        Assert.Null(photo.StoragePath);
        Assert.Equal("pack-photo.webp", photo.OriginalFileName);
        Assert.Equal("image/webp", photo.ContentType);
        Assert.Equal(0, photo.Width);
        Assert.Equal(0, photo.Height);
        Assert.Equal($"/api/media/post-photos/{photo.Id}", Assert.Single(response.Photos).Url);
        Assert.Empty(fileService.DeletedFileIds);
    }

    [Fact]
    public async Task Partial_post_completion_cleans_up_completed_files_when_a_later_reference_is_rejected()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        var firstFileId = Guid.CreateVersion7();
        var rejectedFileId = Guid.CreateVersion7();
        var fileService = new RecordingFileServiceClient { RejectedFileId = rejectedFileId };
        fileService.CompletedFiles[firstFileId] = Completed(firstFileId, "first.jpg", "image/jpeg");
        var service = CreatePostService(db, fileService);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            Principal(owner.AuthProviderUserId),
            family.Id,
            new CreatePostRequest
            {
                PhotoFiles = [Reference(firstFileId), Reference(rejectedFileId)]
            }));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal([firstFileId], fileService.DeletedFileIds);
        Assert.Empty(await db.Posts.ToListAsync());
        Assert.Empty(await db.PostPhotos.ToListAsync());
    }

    [Fact]
    public async Task File_id_already_used_by_an_avatar_cannot_be_reused_for_a_dog_photo()
    {
        await using var db = CreateDb();
        var (owner, family) = await SeedFamilyAsync(db);
        var reusedFileId = Guid.CreateVersion7();
        owner.AvatarFileId = reusedFileId;
        await db.SaveChangesAsync();
        var fileService = new RecordingFileServiceClient();
        fileService.CompletedFiles[reusedFileId] = Completed(reusedFileId, "reused.jpg", "image/jpeg");
        var service = new DogService(
            db,
            new IdentityService(db),
            new FamilyAccessService(db),
            fileService,
            new MediaCleanupService(fileService, NullLogger<MediaCleanupService>.Instance));

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            Principal(owner.AuthProviderUserId),
            family.Id,
            new UpsertDogRequest
            {
                Name = "Hoovie",
                PhotoFile = Reference(reusedFileId)
            }));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(0, fileService.CompleteAttempts);
        Assert.Empty(await db.DogProfiles.ToListAsync());
    }

    private static PostService CreatePostService(AppDbContext db, RecordingFileServiceClient fileService) => new(
        db,
        new IdentityService(db),
        new FamilyAccessService(db),
        fileService,
        new MediaCleanupService(fileService, NullLogger<MediaCleanupService>.Instance));

    private static FileUploadReferenceRequest Reference(Guid fileId) => new()
    {
        FileId = fileId,
        UploadToken = $"token-{fileId:N}"
    };

    private static FileMetadataResponse Completed(Guid fileId, string name, string contentType) => new(
        fileId,
        name,
        contentType,
        1024,
        DateTimeOffset.UtcNow);

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"hooviepack-file-association-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

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
        db.AddRange(owner, family, new FamilyMembership
        {
            FamilyId = family.Id,
            Family = family,
            UserId = owner.Id,
            User = owner,
            Role = FamilyRole.Owner
        });
        await db.SaveChangesAsync();
        return (owner, family);
    }

    private static AppUser CreateUser(string subject) => new()
    {
        AuthProviderUserId = subject,
        Email = $"{subject}@example.test",
        DisplayName = subject,
        LastSeenAt = DateTimeOffset.UtcNow
    };

    private static ClaimsPrincipal Principal(string subject) => new(
        new ClaimsIdentity([new Claim("sub", subject)], "test"));

    private sealed class RecordingFileServiceClient : IFileServiceClient
    {
        public long MaxImageBytes => 10 * 1024 * 1024;
        public Dictionary<Guid, FileMetadataResponse> CompletedFiles { get; } = [];
        public List<Guid> DeletedFileIds { get; } = [];
        public Guid? RejectedFileId { get; init; }
        public Func<Guid, Task>? OnDeleteAsync { get; set; }
        public int CompleteAttempts { get; private set; }

        public Task<UploadResponse> CreateUploadAsync(
            CreateUploadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No upload should be initialized in this test.");

        public Task<FileMetadataResponse> CompleteUploadAsync(
            Guid fileId,
            string uploadToken,
            CancellationToken cancellationToken = default)
        {
            CompleteAttempts++;
            if (fileId == RejectedFileId)
            {
                throw new FileServiceRejectedRequestException();
            }

            return Task.FromResult(CompletedFiles[fileId]);
        }

        public Task<DownloadResponse> GetDownloadAsync(
            Guid fileId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No download should be created in this test.");

        public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            if (OnDeleteAsync is not null)
            {
                await OnDeleteAsync(fileId);
            }

            DeletedFileIds.Add(fileId);
        }
    }
}
