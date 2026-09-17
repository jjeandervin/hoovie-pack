using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Controllers;

[ApiController, Authorize, Route("api/families/{familyId:guid}/calendar-events")]
public sealed class CalendarEventsController(CalendarService calendar) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid familyId, [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate,
        [FromQuery] string direction = "upcoming", [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await calendar.ListAsync(User, familyId, startDate, endDate, direction, page, pageSize, ct));
    [HttpGet("{eventId:guid}")]
    public async Task<IActionResult> Get(Guid familyId, Guid eventId, CancellationToken ct) => Ok(await calendar.GetAsync(User, familyId, eventId, ct));
    [HttpPost]
    public async Task<IActionResult> Create(Guid familyId, SaveCalendarEventRequest request, CancellationToken ct)
    {
        var e = await calendar.SaveAsync(User, familyId, null, request, ct);
        return CreatedAtAction(nameof(Get), new { familyId, eventId = e.Id }, e);
    }
    [HttpPut("{eventId:guid}")]
    public async Task<IActionResult> Update(Guid familyId, Guid eventId, SaveCalendarEventRequest request, CancellationToken ct) =>
        Ok(await calendar.SaveAsync(User, familyId, eventId, request, ct));
    [HttpDelete("{eventId:guid}")]
    public async Task<IActionResult> Delete(Guid familyId, Guid eventId, CancellationToken ct)
    { await calendar.DeleteAsync(User, familyId, eventId, ct); return NoContent(); }
    [HttpPost("{eventId:guid}/photos")]
    public async Task<IActionResult> AddPhoto(Guid familyId, Guid eventId, FileUploadReferenceRequest request, CancellationToken ct) =>
        Ok(await calendar.AddPhotoAsync(User, familyId, eventId, request, ct));
    [HttpGet("{eventId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid familyId, Guid eventId, Guid photoId, CancellationToken ct) =>
        Ok(await calendar.DownloadAsync(User, familyId, eventId, photoId, ct));
    [HttpPut("{eventId:guid}/photos/{photoId:guid}/cover")]
    public async Task<IActionResult> Cover(Guid familyId, Guid eventId, Guid photoId, CancellationToken ct) =>
        Ok(await calendar.ChangePhotoAsync(User, familyId, eventId, photoId, false, ct));
    [HttpDelete("{eventId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> RemovePhoto(Guid familyId, Guid eventId, Guid photoId, CancellationToken ct) =>
        Ok(await calendar.ChangePhotoAsync(User, familyId, eventId, photoId, true, ct));
}
