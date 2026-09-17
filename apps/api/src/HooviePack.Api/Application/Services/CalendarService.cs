using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public sealed class CalendarService(AppDbContext db, IIdentityService identity,
    IFamilyAccessService access, IFileServiceClient files, IMediaCleanupService cleanup)
{
    private async Task<(AppUser User, FamilyMembership Member)> Member(ClaimsPrincipal principal, Guid familyId, CancellationToken ct)
    {
        var user = await identity.GetCurrentUserAsync(principal, ct);
        return (user, await access.RequireMemberAsync(familyId, user.Id, ct));
    }
    private IQueryable<CalendarEvent> Graph => db.CalendarEvents.Include(x => x.CreatedByUser).Include(x => x.Photos);
    private async Task<CalendarEvent> Find(Guid familyId, Guid eventId, CancellationToken ct) =>
        await Graph.SingleOrDefaultAsync(x => x.FamilyId == familyId && x.Id == eventId, ct) ?? throw ApiException.NotFound();
    private static bool CanManage(CalendarEvent e, Guid userId, FamilyRole role) =>
        e.CreatedByUserId == userId || role is FamilyRole.Owner or FamilyRole.Admin;
    private static void Manage(CalendarEvent e, AppUser user, FamilyMembership member)
    {
        if (!CanManage(e, user.Id, member.Role)) throw ApiException.Forbidden("Only the creator or a family admin can manage this event.");
    }
    public async Task<PagedResponse<CalendarItemResponse>> ListAsync(ClaimsPrincipal principal, Guid familyId,
        DateOnly? startDate = null, DateOnly? endDate = null, string direction = "upcoming", int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var (user, member) = await Member(principal, familyId, ct);
        if (startDate > endDate) throw ApiException.BadRequest("End date cannot precede start date.", "endDate");
        if (direction is not ("upcoming" or "history")) throw ApiException.BadRequest("Choose upcoming or history.", "direction");
        var query = Graph.AsNoTracking().Where(x => x.FamilyId == familyId);
        if (startDate.HasValue) query = query.Where(x => (x.EndDate ?? x.StartDate) >= startDate.Value);
        if (endDate.HasValue) query = query.Where(x => x.StartDate <= endDate.Value);
        query = direction == "history" ? query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.StartTime).ThenByDescending(x => x.Id)
            : query.OrderBy(x => x.StartDate).ThenBy(x => x.StartTime).ThenBy(x => x.Id);
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var eventCount = await query.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        var limit = (int)Math.Min(int.MaxValue, skip + pageSize);
        var rangeStart = startDate ?? DateOnly.MinValue;
        var rangeEnd = endDate ?? DateOnly.MaxValue;
        // Read profiles only after family authorization. Membership IDs link to the family profile.
        var members = await db.FamilyMemberships.AsNoTracking()
            .Where(x => x.FamilyId == familyId && x.User.BirthDate != null)
            .Select(x => new BirthdayProfile(x.Id, x.User.DisplayName, x.User.BirthDate!.Value, x.User.AvatarUrl, false)).ToListAsync(ct);
        var dogs = await db.DogProfiles.AsNoTracking()
            .Where(x => x.FamilyId == familyId && x.Birthday != null)
            .Select(x => new BirthdayProfile(x.Id, x.Name, x.Birthday!.Value, x.PhotoUrl, true)).ToListAsync(ct);
        var birthdayRanges = members.Concat(dogs).Select(p => BirthdayRange(p, rangeStart, rangeEnd)).ToList();
        var total = eventCount + birthdayRanges.Sum(r => Math.Max(0, r.Last - r.First + 1));
        if (skip >= total) return new([], page, pageSize, total);
        // Only the first skip+pageSize candidates from each source can enter this page.
        // This avoids loading the family's full event history or thousands of future birthday DTOs.
        var events = await query.Take(limit).AsSplitQuery().ToListAsync(ct);
        var items = events.Select(e => {
            var response = Map(e, user.Id, member.Role);
            return new CalendarItemResponse(e.Id.ToString(), "calendarEvent", e.Title, e.EventType,
                e.StartDate, e.EndDate, e.IsAllDay, e.StartTime, e.Id, null, null,
                response.Photos.FirstOrDefault(p => p.IsCover)?.Url, response.CanManage, response.Photos);
        }).Concat(birthdayRanges.SelectMany(r => BirthdayItems(r.Profile, r.First, r.Last, direction == "history", limit)));
        var ordered = direction == "history"
            ? items.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.StartTime).ThenByDescending(x => x.Id, StringComparer.Ordinal)
            : items.OrderBy(x => x.StartDate).ThenBy(x => x.StartTime).ThenBy(x => x.Id, StringComparer.Ordinal);
        return new(ordered.Skip((int)skip).Take(pageSize).ToList(), page, pageSize, total);
    }

    private sealed record BirthdayProfile(Guid Id, string Name, DateOnly BirthDate, string? ImageUrl, bool IsDog);
    private static DateOnly BirthdayDate(DateOnly birthDate, int year) =>
        new(year, birthDate.Month, Math.Min(birthDate.Day, DateTime.DaysInMonth(year, birthDate.Month)));
    private static (BirthdayProfile Profile, int First, int Last) BirthdayRange(BirthdayProfile profile, DateOnly start, DateOnly end)
    {
        var first = Math.Max(start.Year, profile.BirthDate.Year);
        var last = end.Year;
        if (BirthdayDate(profile.BirthDate, first) < start) first++;
        if (BirthdayDate(profile.BirthDate, last) > end) last--;
        return (profile, first, last);
    }
    private static IEnumerable<CalendarItemResponse> BirthdayItems(BirthdayProfile profile, int first, int last, bool descending, int limit)
    {
        var count = Math.Min(limit, Math.Max(0, last - first + 1));
        for (var i = 0; i < count; i++)
        {
            var year = descending ? last - i : first + i;
            var source = profile.IsDog ? "dog" : "member";
            yield return new($"{source}-birthday-{profile.Id}-{year}", $"{source}Birthday", $"{profile.Name}'s Birthday",
                profile.IsDog ? "DogBirthday" : "Birthday", BirthdayDate(profile.BirthDate, year), null, true, null,
                null, profile.IsDog ? null : profile.Id, profile.IsDog ? profile.Id : null, profile.ImageUrl, false, []);
        }
    }

    public async Task<CalendarEventResponse> GetAsync(ClaimsPrincipal principal, Guid familyId, Guid eventId, CancellationToken ct = default)
    {
        var (user, member) = await Member(principal, familyId, ct);
        return Map(await Find(familyId, eventId, ct), user.Id, member.Role);
    }
    public async Task<CalendarEventResponse> SaveAsync(ClaimsPrincipal principal, Guid familyId, Guid? eventId, SaveCalendarEventRequest request, CancellationToken ct = default)
    {
        var (user, member) = await Member(principal, familyId, ct);
        var e = eventId.HasValue ? await Find(familyId, eventId.Value, ct) : new CalendarEvent { FamilyId = familyId, CreatedByUserId = user.Id, CreatedByUser = user };
        if (eventId.HasValue) Manage(e, user, member);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            throw ApiException.BadRequest("A title of up to 200 characters is required.", "title");
        if (!request.StartDate.HasValue) throw ApiException.BadRequest("A start date is required.", "startDate");
        if (request.EndDate < request.StartDate) throw ApiException.BadRequest("End date cannot precede start date.", "endDate");
        if (string.IsNullOrWhiteSpace(request.EventType) || request.EventType.Length > 40 || request.Location?.Length > 500 || request.TimeZoneId?.Length > 100)
            throw ApiException.BadRequest("Check the event type, location, and timezone lengths.");
        if (!request.IsAllDay)
        {
            if (request.StartTime is null) throw ApiException.BadRequest("A start time is required for timed events.", "startTime");
            if ((request.EndDate ?? request.StartDate) == request.StartDate && request.EndTime < request.StartTime)
                throw ApiException.BadRequest("End time cannot precede start time.", "endTime");
            try { TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId ?? ""); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            { throw ApiException.BadRequest("A valid timezone is required for timed events.", "timeZoneId"); }
        }
        e.Title = request.Title.Trim(); e.Description = request.Description?.Trim(); e.EventType = request.EventType.Trim();
        e.StartDate = request.StartDate.Value; e.EndDate = request.EndDate; e.IsAllDay = request.IsAllDay;
        e.StartTime = request.IsAllDay ? null : request.StartTime; e.EndTime = request.IsAllDay ? null : request.EndTime;
        e.TimeZoneId = request.IsAllDay ? null : request.TimeZoneId; e.Location = request.Location?.Trim();
        e.UpdatedByUserId = user.Id; e.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (!eventId.HasValue) db.CalendarEvents.Add(e);
        await db.SaveChangesAsync(ct);
        return Map(e, user.Id, member.Role);
    }
    public async Task DeleteAsync(ClaimsPrincipal principal, Guid familyId, Guid eventId, CancellationToken ct = default)
    {
        var (user, member) = await Member(principal, familyId, ct);
        var e = await Find(familyId, eventId, ct); Manage(e, user, member);
        var ids = e.Photos.Select(p => p.FileId).ToArray();
        db.CalendarEvents.Remove(e); await db.SaveChangesAsync(ct);
        await cleanup.DeleteBestEffortAsync(ids, "calendar event deleted after database commit");
    }
    public async Task<CalendarEventResponse> AddPhotoAsync(ClaimsPrincipal principal, Guid familyId, Guid eventId, FileUploadReferenceRequest reference, CancellationToken ct = default)
    {
        var (user, member) = await Member(principal, familyId, ct);
        var e = await Find(familyId, eventId, ct); Manage(e, user, member);
        await MediaFileOperations.RequireUnassociatedAsync(db, [reference.FileId], "fileId", ct);
        var file = await MediaFileOperations.CompleteImageAsync(files, cleanup, reference, "fileId", ct);
        var photo = new CalendarEventPhoto { CalendarEvent = e, FileId = file.FileId, SortOrder = e.Photos.Select(x => x.SortOrder).DefaultIfEmpty(-1).Max() + 1, UploadedByUserId = user.Id };
        db.CalendarEventPhotos.Add(photo);
        e.UpdatedByUserId = user.Id; e.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(ct); }
        catch { await cleanup.DeleteBestEffortAsync(file.FileId, "calendar photo association failed"); throw; }
        return Map(e, user.Id, member.Role);
    }
    public async Task<CalendarEventResponse> ChangePhotoAsync(ClaimsPrincipal principal, Guid familyId, Guid eventId, Guid photoId, bool delete, CancellationToken ct = default)
    {
        var (user, member) = await Member(principal, familyId, ct);
        var e = await Find(familyId, eventId, ct); Manage(e, user, member);
        var photo = e.Photos.SingleOrDefault(p => p.Id == photoId) ?? throw ApiException.NotFound();
        if (delete) { db.CalendarEventPhotos.Remove(photo); e.Photos.Remove(photo); }
        else foreach (var p in e.Photos) p.IsCover = p.Id == photoId;
        e.UpdatedByUserId = user.Id; e.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (delete) await cleanup.DeleteBestEffortAsync(photo.FileId, "calendar photo removed after database commit");
        return Map(e, user.Id, member.Role);
    }
    public async Task<DownloadResponse> DownloadAsync(ClaimsPrincipal principal, Guid familyId, Guid eventId, Guid photoId, CancellationToken ct = default)
    {
        await Member(principal, familyId, ct);
        var e = await Find(familyId, eventId, ct);
        var photo = e.Photos.SingleOrDefault(p => p.Id == photoId) ?? throw ApiException.NotFound();
        return await MediaFileOperations.GetDownloadAsync(files, photo.FileId, ct);
    }
    private static CalendarEventResponse Map(CalendarEvent e, Guid userId, FamilyRole role)
    {
        var photos = e.Photos.OrderBy(p => p.SortOrder).ToList();
        var cover = photos.FirstOrDefault(p => p.IsCover) ?? photos.FirstOrDefault();
        return new(e.Id, e.FamilyId, e.Title, e.Description, e.EventType, e.StartDate, e.EndDate, e.IsAllDay,
            e.StartTime, e.EndTime, e.TimeZoneId, e.Location,
            new(e.CreatedByUser.Id, e.CreatedByUser.DisplayName, e.CreatedByUser.AvatarUrl, null),
            e.CreatedAtUtc, e.UpdatedAtUtc, CanManage(e, userId, role),
            photos.Select(p => new CalendarPhotoResponse(p.Id, p.FileId,
                $"/api/families/{e.FamilyId}/calendar-events/{e.Id}/photos/{p.Id}", p == cover, p.SortOrder)).ToList());
    }
}
