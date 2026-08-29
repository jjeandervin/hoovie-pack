using System.Security.Claims;
using HooviePack.Api.Application;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Tests;

public sealed class MediaAuthorizationTests
{
    [Fact]
    public async Task Knowing_media_ids_does_not_let_an_outsider_open_family_media()
    {
        await using var db = CreateDb();
        var owner = CreateUser("owner");
        owner.AvatarStoragePath = "avatars/owner.jpg";
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
            StoragePath = "posts/private.jpg",
            OriginalFileName = "private.jpg",
            ContentType = "image/jpeg",
            Width = 10,
            Height = 10
        };
        var dog = new DogProfile
        {
            FamilyId = family.Id,
            Family = family,
            Name = "Hoovie",
            PhotoStoragePath = "dogs/private.png",
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

        var storage = new RecordingFileStorage();
        var service = new MediaService(
            db,
            new IdentityService(db),
            new FamilyAccessService(db),
            storage);
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
        Assert.Equal(0, storage.OpenAttempts);

        var ownerPrincipal = Principal(owner.AuthProviderUserId);
        var ownerPostPhoto = await service.GetPostPhotoAsync(ownerPrincipal, photo.Id);
        var ownerDogPhoto = await service.GetDogPhotoAsync(ownerPrincipal, dog.Id);
        var ownerAvatar = await service.GetAvatarAsync(ownerPrincipal, owner.Id);
        await ownerPostPhoto.Stream.DisposeAsync();
        await ownerDogPhoto.Stream.DisposeAsync();
        await ownerAvatar.Stream.DisposeAsync();
        Assert.Equal(3, storage.OpenAttempts);
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

    private sealed class RecordingFileStorage : IFileStorage
    {
        public int OpenAttempts { get; private set; }
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
            CancellationToken cancellationToken = default)
        {
            OpenAttempts++;
            return Task.FromResult<StoredFile?>(
                new StoredFile(new MemoryStream([1, 2, 3]), contentType, 3));
        }

        public Task DeleteAsync(string? storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
