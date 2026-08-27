using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/posts/{postId:guid}/reactions")]
public sealed class ReactionsController(IReactionService reactionService) : ControllerBase
{
    [HttpPost("{type}")]
    [HttpPut("{type}")]
    [ProducesResponseType<ToggleReactionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ToggleReactionResponse>> Toggle(
        Guid postId,
        string type,
        CancellationToken cancellationToken) =>
        Ok(await reactionService.ToggleAsync(User, postId, type, cancellationToken));

    [HttpDelete("{type}")]
    [ProducesResponseType<ReactionSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReactionSummaryResponse>> Remove(
        Guid postId,
        string type,
        CancellationToken cancellationToken) =>
        Ok(await reactionService.RemoveAsync(User, postId, type, cancellationToken));
}
