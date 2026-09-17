namespace HooviePack.Api.Domain;

public sealed class CalendarEvent : Entity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EventType { get; set; } = "Family";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsAllDay { get; set; } = true;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? TimeZoneId { get; set; }
    public string? Location { get; set; }
    public Guid CreatedByUserId { get; set; }
    public AppUser CreatedByUser { get; set; } = null!;
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<CalendarEventPhoto> Photos { get; set; } = [];
}

public sealed class CalendarEventPhoto : Entity
{
    public Guid CalendarEventId { get; set; }
    public CalendarEvent CalendarEvent { get; set; } = null!;
    public Guid FileId { get; set; }
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
