using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Files.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/media")]
public sealed class MediaController(IMediaService mediaService) : ControllerBase
{
    [HttpPost("uploads")]
    [ProducesResponseType<UploadResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UploadResponse>> CreateUpload(
        [FromBody] InitializeMediaUploadRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediaService.CreateUploadAsync(User, request, cancellationToken));

    [HttpGet("post-photos/{photoId:guid}")]
    [ProducesResponseType<DownloadResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DownloadResponse>> GetPostPhoto(
        Guid photoId,
        CancellationToken cancellationToken) =>
        Ok(await mediaService.GetPostPhotoAsync(User, photoId, cancellationToken));

    [HttpGet("dogs/{dogId:guid}")]
    [ProducesResponseType<DownloadResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DownloadResponse>> GetDogPhoto(
        Guid dogId,
        CancellationToken cancellationToken) =>
        Ok(await mediaService.GetDogPhotoAsync(User, dogId, cancellationToken));

    [HttpGet("avatars/{userId:guid}")]
    [ProducesResponseType<DownloadResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DownloadResponse>> GetAvatar(
        Guid userId,
        CancellationToken cancellationToken) =>
        Ok(await mediaService.GetAvatarAsync(User, userId, cancellationToken));
}
