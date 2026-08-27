using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController(IProfileService profileService) : ControllerBase
{
    [HttpGet("{userId:guid}")]
    [ProducesResponseType<UserSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserSummaryResponse>> Get(
        Guid userId,
        CancellationToken cancellationToken) =>
        Ok(await profileService.GetUserAsync(User, userId, cancellationToken));
}
