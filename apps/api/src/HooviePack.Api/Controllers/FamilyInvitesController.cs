using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/families/{familyId:guid}/invites")]
public sealed class FamilyInvitesController(IFamilyService familyService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<InviteResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<InviteResponse>>> List(
        Guid familyId,
        CancellationToken cancellationToken) =>
        Ok(await familyService.ListInvitesAsync(User, familyId, cancellationToken));

    [HttpPost]
    [ProducesResponseType<InviteResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InviteResponse>> Create(
        Guid familyId,
        [FromBody] CreateInviteRequest request,
        CancellationToken cancellationToken)
    {
        var invite = await familyService.CreateInviteAsync(User, familyId, request, cancellationToken);
        return CreatedAtAction(nameof(List), new { familyId }, invite);
    }

    [HttpDelete("{inviteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(
        Guid familyId,
        Guid inviteId,
        CancellationToken cancellationToken)
    {
        await familyService.RevokeInviteAsync(User, familyId, inviteId, cancellationToken);
        return NoContent();
    }
}
