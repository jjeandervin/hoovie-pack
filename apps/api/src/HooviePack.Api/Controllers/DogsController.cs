using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/families/{familyId:guid}/dogs")]
public sealed class DogsController(IDogService dogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<DogResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<DogResponse>>> List(
        Guid familyId,
        CancellationToken cancellationToken) =>
        Ok(await dogService.ListAsync(User, familyId, cancellationToken));

    [HttpGet("{dogId:guid}")]
    [ProducesResponseType<DogResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DogResponse>> Get(
        Guid familyId,
        Guid dogId,
        CancellationToken cancellationToken) =>
        Ok(await dogService.GetAsync(User, familyId, dogId, cancellationToken));

    [HttpPost]
    [ProducesResponseType<DogResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DogResponse>> Create(
        Guid familyId,
        [FromBody] UpsertDogRequest request,
        CancellationToken cancellationToken)
    {
        var dog = await dogService.CreateAsync(User, familyId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { familyId, dogId = dog.Id }, dog);
    }

    [HttpPut("{dogId:guid}")]
    [ProducesResponseType<DogResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DogResponse>> Update(
        Guid familyId,
        Guid dogId,
        [FromBody] UpsertDogRequest request,
        CancellationToken cancellationToken) =>
        Ok(await dogService.UpdateAsync(User, familyId, dogId, request, cancellationToken));

    [HttpDelete("{dogId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid familyId,
        Guid dogId,
        CancellationToken cancellationToken)
    {
        await dogService.DeleteAsync(User, familyId, dogId, cancellationToken);
        return NoContent();
    }
}
