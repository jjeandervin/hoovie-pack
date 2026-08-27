using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/families/{familyId:guid}/posts")]
public sealed class FamilyPostsController(IPostService postService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<PostResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<PostResponse>>> List(
        Guid familyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await postService.ListAsync(User, familyId, page, pageSize, cancellationToken));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(42 * 1024 * 1024)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PostResponse>> Create(
        Guid familyId,
        [FromForm] CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        var post = await postService.CreateAsync(User, familyId, request, cancellationToken);
        return CreatedAtAction(nameof(PostsController.Get), "Posts", new { postId = post.Id }, post);
    }
}
