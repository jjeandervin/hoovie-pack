using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MeController(IProfileService profileService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await profileService.GetMeAsync(User, cancellationToken));

    [HttpPut]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Update(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await profileService.UpdateMeAsync(User, request, cancellationToken));

    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> UpdateAvatar(
        [FromForm] AvatarUploadRequest request,
        CancellationToken cancellationToken) =>
        Ok(await profileService.UpdateAvatarAsync(User, request.Avatar, cancellationToken));
}
