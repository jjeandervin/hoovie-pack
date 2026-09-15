using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dog-quotes")]
public sealed class DogQuotesController(IDogQuoteService quotes) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<DogQuoteDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<DogQuoteDto>>> List(CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? category = null, [FromQuery] string? author = null) =>
        Ok(await quotes.GetPagedAsync(page, pageSize, category, author, cancellationToken));

    [HttpGet("random")]
    [ProducesResponseType<DogQuoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DogQuoteDto>> Random(CancellationToken cancellationToken, [FromQuery] string? category = null) =>
        QuoteResult(await quotes.GetRandomAsync(category, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DogQuoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DogQuoteDto>> Get(Guid id, CancellationToken cancellationToken) =>
        QuoteResult(await quotes.GetByIdAsync(id, cancellationToken));

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> Categories(CancellationToken cancellationToken) =>
        Ok(await quotes.GetCategoriesAsync(cancellationToken));

    private ActionResult<DogQuoteDto> QuoteResult(DogQuoteDto? quote) => quote is not null ? Ok(quote) :
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found",
            detail: "No matching dog quote was found.",
            extensions: new Dictionary<string, object?> { ["code"] = "dog_quote_not_found" });
}
