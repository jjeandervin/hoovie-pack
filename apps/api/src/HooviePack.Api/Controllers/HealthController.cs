using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController(IApplicationHealthService healthService) : ControllerBase
{
    [HttpGet]
    [HttpGet("ready")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Ready(CancellationToken cancellationToken)
    {
        var response = await healthService.CheckAsync(readiness: true, cancellationToken);
        return response.Status == "healthy"
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    [HttpGet("live")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Live(CancellationToken cancellationToken)
    {
        var response = await healthService.CheckAsync(readiness: false, cancellationToken);
        return response.Status == "healthy"
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
