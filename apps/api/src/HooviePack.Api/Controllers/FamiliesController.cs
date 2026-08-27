using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/families")]
public sealed class FamiliesController(IFamilyService familyService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<FamilySummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<FamilySummaryResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await familyService.ListAsync(User, cancellationToken));

    [HttpGet("{familyId:guid}")]
    [ProducesResponseType<FamilyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FamilyResponse>> Get(Guid familyId, CancellationToken cancellationToken) =>
        Ok(await familyService.GetAsync(User, familyId, cancellationToken));

    [HttpPost]
    [ProducesResponseType<FamilyResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<FamilyResponse>> Create(
        [FromBody] CreateFamilyRequest request,
        CancellationToken cancellationToken)
    {
        var family = await familyService.CreateAsync(User, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { familyId = family.Id }, family);
    }

    [HttpPut("{familyId:guid}")]
    [ProducesResponseType<FamilyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FamilyResponse>> Update(
        Guid familyId,
        [FromBody] UpdateFamilyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await familyService.UpdateAsync(User, familyId, request, cancellationToken));

    [HttpPost("join")]
    [ProducesResponseType<FamilyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FamilyResponse>> Join(
        [FromBody] JoinFamilyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await familyService.JoinAsync(User, request, cancellationToken));
}
