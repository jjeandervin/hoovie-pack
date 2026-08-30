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

namespace HooviePack.Api.Tests;

public sealed class MediaAuthorizationTests
{
    [Fact]
    public async Task Knowing_media_ids_does_not_let_an_outsider_request_download_urls()
    {
        await using var db = CreateDb();
        var owner = CreateUser("owner");
        owner.AvatarFileId = Guid.CreateVersion7();
        owner.AvatarContentType = "image/jpeg";
        var outsider = CreateUser("outsider");
        var family = new Family
        {
            Name = "Private Pack",
            Slug = $"private-pack-{Guid.NewGuid():N}"[..22],
            CreatedByUserId = owner.Id,
            CreatedByUser = owner
        };
        var post = new Post
        {
            FamilyId = family.Id,
            Family = family,
            AuthorUserId = owner.Id,
            AuthorUser = owner,
            Content = "Family-only post"
        };
        var photo = new PostPhoto
        {
            PostId = post.Id,
            Post = post,
            FileId = Guid.CreateVersion7(),
            OriginalFileName = "private.jpg",
            ContentType = "image/jpeg"
        };
        var dog = new DogProfile
        {
            FamilyId = family.Id,
            Family = family,
            Name = "Hoovie",
            PhotoFileId = Guid.CreateVersion7(),
            PhotoContentType = "image/png",
            CreatedByUserId = owner.Id,
            CreatedByUser = owner
        };
        db.AddRange(owner, outsider, family, new FamilyMembership
        {
            FamilyId = family.Id,
            Family = family,
            UserId = owner.Id,
            User = owner,
            Role = FamilyRole.Owner
        }, post, photo, dog);
        await db.SaveChangesAsync();

        var fileService = new RecordingFileServiceClient();
        var service = new MediaService(
            db,
            new IdentityService(db),
            new FamilyAccessService(db),
            fileService);
        var outsiderPrincipal = Principal(outsider.AuthProviderUserId);

        var postException = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetPostPhotoAsync(outsiderPrincipal, photo.Id));
        var dogException = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetDogPhotoAsync(outsiderPrincipal, dog.Id));
        var avatarException = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetAvatarAsync(outsiderPrincipal, owner.Id));

        Assert.Equal(StatusCodes.Status404NotFound, postException.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, dogException.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, avatarException.StatusCode);
        Assert.Equal(0, fileService.DownloadAttempts);

        var ownerPrincipal = Principal(owner.AuthProviderUserId);
        var ownerPostPhoto = await service.GetPostPhotoAsync(ownerPrincipal, photo.Id);
        var ownerDogPhoto = await service.GetDogPhotoAsync(ownerPrincipal, dog.Id);
        var ownerAvatar = await service.GetAvatarAsync(ownerPrincipal, owner.Id);

        Assert.Equal(photo.FileId, ownerPostPhoto.FileId);
        Assert.Equal(dog.PhotoFileId, ownerDogPhoto.FileId);
        Assert.Equal(owner.AvatarFileId, ownerAvatar.FileId);
        Assert.All(
            [ownerPostPhoto, ownerDogPhoto, ownerAvatar],
            download => Assert.StartsWith("https://s3.example.test/", download.DownloadUrl, StringComparison.Ordinal));
        Assert.Equal(3, fileService.DownloadAttempts);
    }

    [Fact]
    public async Task Family_upload_is_authorized_before_the_file_service_is_called()
    {
        await using var db = CreateDb();
        var owner = CreateUser("owner");
        var outsider = CreateUser("outsider");
        var family = new Family
        {
            Name = "Private Pack",
            Slug = $"private-pack-{Guid.NewGuid():N}"[..22],
            CreatedByUserId = owner.Id,
            CreatedByUser = owner
        };
        db.AddRange(owner, outsider, family, new FamilyMembership
        {
            FamilyId = family.Id,
            Family = family,
            UserId = owner.Id,
            User = owner,
            Role = FamilyRole.Owner
        });
        await db.SaveChangesAsync();

        var fileService = new RecordingFileServiceClient();
        var service = new MediaService(
            db,
            new IdentityService(db),
            new FamilyAccessService(db),
            fileService);
        var request = new InitializeMediaUploadRequest
        {
            FileName = "hoovie.png",
            ContentType = "image/png",
            Size = 1234,
            Purpose = UploadPurpose.PostPhoto,
            FamilyId = family.Id
        };

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateUploadAsync(Principal(outsider.AuthProviderUserId), request));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal(0, fileService.CreateAttempts);

        var upload = await service.CreateUploadAsync(Principal(owner.AuthProviderUserId), request);

        Assert.Equal(fileService.Upload.FileId, upload.FileId);
        Assert.Equal(fileService.Upload.UploadUrl, upload.UploadUrl);
        Assert.Equal(fileService.Upload.UploadToken, upload.UploadToken);
        Assert.Equal("image/png", Assert.IsType<CreateUploadRequest>(fileService.LastUploadRequest).ContentType);
        Assert.Equal(1, fileService.CreateAttempts);
    }

    [Theory]
    [InlineData("image/gif", 100)]
    [InlineData("image/png", 0)]
    [InlineData("image/png", 10485761)]
    public async Task Invalid_image_metadata_is_rejected_before_the_file_service_is_called(
        string contentType,
        long size)
    {
        await using var db = CreateDb();
        var user = CreateUser("owner");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var fileService = new RecordingFileServiceClient();
        var service = new MediaService(
            db,
            new IdentityService(db),
            new FamilyAccessService(db),
            fileService);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateUploadAsync(
            Principal(user.AuthProviderUserId),
            new InitializeMediaUploadRequest
            {
                FileName = "hoovie.png",
                ContentType = contentType,
                Size = size,
                Purpose = UploadPurpose.Avatar
            }));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(0, fileService.CreateAttempts);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"hooviepack-media-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
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
        public int CreateAttempts { get; private set; }
        public int DownloadAttempts { get; private set; }
        public CreateUploadRequest? LastUploadRequest { get; private set; }
        public long MaxImageBytes => 10 * 1024 * 1024;
        public UploadResponse Upload { get; } = new(
            Guid.CreateVersion7(),
            "https://s3.example.test/upload",
            DateTimeOffset.UtcNow.AddMinutes(5),
            new Dictionary<string, string> { ["Content-Type"] = "image/png" },
            "one-time-token");

        public Task<UploadResponse> CreateUploadAsync(
            CreateUploadRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateAttempts++;
            LastUploadRequest = request;
            return Task.FromResult(Upload);
        }

        public Task<FileMetadataResponse> CompleteUploadAsync(
            Guid fileId,
            string uploadToken,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No upload should be completed in this test.");

        public Task<DownloadResponse> GetDownloadAsync(
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            DownloadAttempts++;
            return Task.FromResult(new DownloadResponse(
                fileId,
                $"https://s3.example.test/{fileId:D}",
                DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
