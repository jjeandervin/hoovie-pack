using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/families/{familyId:guid}/members")]
public sealed class FamilyMembersController(IFamilyService familyService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<MemberResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<MemberResponse>>> List(
        Guid familyId,
        CancellationToken cancellationToken) =>
        Ok(await familyService.ListMembersAsync(User, familyId, cancellationToken));

    [HttpPut("{membershipId:guid}/role")]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberResponse>> UpdateRole(
        Guid familyId,
        Guid membershipId,
        [FromBody] UpdateMemberRoleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await familyService.UpdateMemberRoleAsync(User, familyId, membershipId, request, cancellationToken));

    [HttpDelete("{membershipId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(
        Guid familyId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        await familyService.RemoveMemberAsync(User, familyId, membershipId, cancellationToken);
        return NoContent();
    }
}
