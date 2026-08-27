using HooviePack.Api.Application.Services;
using HooviePack.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/media")]
public sealed class MediaController(IMediaService mediaService) : ControllerBase
{
    [HttpGet("post-photos/{photoId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPostPhoto(Guid photoId, CancellationToken cancellationToken) =>
        ToFileResult(await mediaService.GetPostPhotoAsync(User, photoId, cancellationToken));

    [HttpGet("dogs/{dogId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDogPhoto(Guid dogId, CancellationToken cancellationToken) =>
        ToFileResult(await mediaService.GetDogPhotoAsync(User, dogId, cancellationToken));

    [HttpGet("avatars/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvatar(Guid userId, CancellationToken cancellationToken) =>
        ToFileResult(await mediaService.GetAvatarAsync(User, userId, cancellationToken));

    private FileStreamResult ToFileResult(StoredFile file)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=300";
        return new FileStreamResult(file.Stream, file.ContentType)
        {
            EnableRangeProcessing = true
        };
    }
}
