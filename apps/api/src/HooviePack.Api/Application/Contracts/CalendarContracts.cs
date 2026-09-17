using System.ComponentModel.DataAnnotations;

namespace HooviePack.Api.Application.Contracts;

public sealed class SaveCalendarEventRequest
{
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    [Required, StringLength(40)] public string EventType { get; set; } = "Family";
    [Required] public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsAllDay { get; set; } = true;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    [StringLength(100)] public string? TimeZoneId { get; set; }
    [StringLength(500)] public string? Location { get; set; }
}
public sealed record CalendarPhotoResponse(Guid Id, Guid FileId, string Url, bool IsCover, int SortOrder);
public sealed record CalendarEventResponse(
    Guid Id, Guid FamilyId, string Title, string? Description, string EventType,
    DateOnly StartDate, DateOnly? EndDate, bool IsAllDay, TimeOnly? StartTime, TimeOnly? EndTime,
    string? TimeZoneId, string? Location, UserSummaryResponse CreatedBy,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, bool CanManage,
    IReadOnlyList<CalendarPhotoResponse> Photos);

// A list item can originate from an event or a profile; synthetic IDs are never entity keys.
public sealed record CalendarItemResponse(
    string Id, string SourceType, string Title, string EventType,
    DateOnly StartDate, DateOnly? EndDate, bool IsAllDay, TimeOnly? StartTime,
    Guid? CalendarEventId, Guid? MemberId, Guid? DogId, string? ImageUrl,
    bool IsEditable, IReadOnlyList<CalendarPhotoResponse> Photos);
