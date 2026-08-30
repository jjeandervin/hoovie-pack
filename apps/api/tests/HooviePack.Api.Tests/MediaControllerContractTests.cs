using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Controllers;
using HooviePack.Files.Domain;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Tests;

public sealed class MediaControllerContractTests
{
    [Fact]
    public async Task Download_endpoint_returns_url_metadata_instead_of_a_file_stream()
    {
        var expected = new DownloadResponse(
            Guid.CreateVersion7(),
            "https://s3.example.test/download",
            DateTimeOffset.UtcNow.AddMinutes(5));
        var controller = new MediaController(new StubMediaService(expected));

        var result = await controller.GetPostPhoto(Guid.CreateVersion7(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.IsNotAssignableFrom<FileResult>(ok.Value);
    }

    private sealed class StubMediaService(DownloadResponse download) : IMediaService
    {
        public Task<UploadResponse> CreateUploadAsync(
            ClaimsPrincipal principal,
            InitializeMediaUploadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No upload should be initialized in this test.");

        public Task<DownloadResponse> GetPostPhotoAsync(
            ClaimsPrincipal principal,
            Guid photoId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(download);

        public Task<DownloadResponse> GetDogPhotoAsync(
            ClaimsPrincipal principal,
            Guid dogId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(download);

        public Task<DownloadResponse> GetAvatarAsync(
            ClaimsPrincipal principal,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(download);
    }
}
