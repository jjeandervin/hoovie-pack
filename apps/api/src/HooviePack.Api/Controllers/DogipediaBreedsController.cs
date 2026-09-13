using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dogipedia/breeds")]
public sealed class DogipediaBreedsController(IDogipediaBreedService breeds) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DogipediaPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DogipediaPageResponse>> List(CancellationToken cancellationToken,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 24)
    {
        if (!await breeds.IsAvailableAsync(cancellationToken))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Dogipedia is getting its breed guide ready.", detail: "Please try again in a moment.",
                extensions: new Dictionary<string, object?> { ["code"] = "dogipedia_catalog_unavailable" });
        return Ok(await breeds.ListAsync(search, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DogipediaBreedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DogipediaBreedResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await breeds.GetAsync(id, cancellationToken));
}
