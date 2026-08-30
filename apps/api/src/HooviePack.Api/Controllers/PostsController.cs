using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/posts")]
public sealed class PostsController(IPostService postService) : ControllerBase
{
    [HttpGet("{postId:guid}")]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> Get(
        Guid postId,
        CancellationToken cancellationToken) =>
        Ok(await postService.GetAsync(User, postId, cancellationToken));

    [HttpPut("{postId:guid}")]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> Update(
        Guid postId,
        [FromBody] UpdatePostRequest request,
        CancellationToken cancellationToken) =>
        Ok(await postService.UpdateAsync(User, postId, request, cancellationToken));

    [HttpDelete("{postId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid postId, CancellationToken cancellationToken)
    {
        await postService.DeleteAsync(User, postId, cancellationToken);
        return NoContent();
    }
}
