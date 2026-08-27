using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/posts/{postId:guid}/comments")]
public sealed class CommentsController(ICommentService commentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<CommentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CommentResponse>>> List(
        Guid postId,
        CancellationToken cancellationToken) =>
        Ok(await commentService.ListAsync(User, postId, cancellationToken));

    [HttpPost]
    [ProducesResponseType<CommentResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CommentResponse>> Create(
        Guid postId,
        [FromBody] UpsertCommentRequest request,
        CancellationToken cancellationToken)
    {
        var comment = await commentService.CreateAsync(User, postId, request, cancellationToken);
        return CreatedAtAction(nameof(List), new { postId }, comment);
    }

    [HttpPut("{commentId:guid}")]
    [ProducesResponseType<CommentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CommentResponse>> Update(
        Guid postId,
        Guid commentId,
        [FromBody] UpsertCommentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await commentService.UpdateAsync(User, postId, commentId, request, cancellationToken));

    [HttpDelete("{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid postId,
        Guid commentId,
        CancellationToken cancellationToken)
    {
        await commentService.DeleteAsync(User, postId, commentId, cancellationToken);
        return NoContent();
    }
}
