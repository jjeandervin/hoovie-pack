using System.Security.Claims;
using HooviePack.Api.Application;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HooviePack.Api.Tests;

public sealed class CalendarTests
{
    [Fact]
    public async Task Birthdays_merge_with_events_have_stable_ids_and_never_create_calendar_rows()
    {
        await using var f = await Fixture.Create();
        f.Author.BirthDate = new(1983, 9, 20);
        f.Author.AvatarUrl = $"/api/media/avatars/{f.Author.Id}";
        var dog = new DogProfile { FamilyId = f.Family.Id, Name = "Hoovie", Birthday = new(2022, 9, 8), CreatedByUserId = f.Author.Id, PhotoUrl = "/api/media/dogs/hoovie" };
        f.Db.DogProfiles.Add(dog);
        await f.Db.SaveChangesAsync();
        var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request("2026-09-15"));
        var result = await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30));
        Assert.Equal(new[] { "dogBirthday", "calendarEvent", "memberBirthday" }, result.Items.Select(i => i.SourceType));
        var member = result.Items.Last();
        var membership = await f.Db.FamilyMemberships.SingleAsync(m => m.UserId == f.Author.Id);
        Assert.Equal($"member-birthday-{membership.Id}-2026", member.Id);
        Assert.Equal(membership.Id, member.MemberId);
        Assert.Equal(f.Author.AvatarUrl, member.ImageUrl);
        Assert.False(member.IsEditable);
        Assert.Null(member.CalendarEventId);
        Assert.Equal(dog.Id, result.Items.First().DogId);
        Assert.Equal(dog.PhotoUrl, result.Items.First().ImageUrl);
        Assert.Equal(e.Id, result.Items.ElementAt(1).CalendarEventId);
        Assert.Single(f.Db.CalendarEvents);
        Assert.Empty(f.Db.CalendarEventPhotos);
        var repeat = await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30));
        Assert.Equal(result.Items.Select(i => i.Id), repeat.Items.Select(i => i.Id));
        var page = await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30), page: 2, pageSize: 1);
        Assert.Equal(e.Id, Assert.Single(page.Items).CalendarEventId);
        Assert.Equal(3, page.TotalCount);
        var reverse = await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30), direction: "history", pageSize: 1);
        Assert.Equal(member.Id, Assert.Single(reverse.Items).Id);
    }

    [Fact]
    public async Task Birthdays_handle_cross_year_ranges_leap_days_and_birth_year_boundaries()
    {
        await using var f = await Fixture.Create();
        f.Author.BirthDate = new(2000, 2, 29);
        var dog = new DogProfile { FamilyId = f.Family.Id, Name = "Hoovie", Birthday = new(2022, 12, 20), CreatedByUserId = f.Author.Id };
        f.Db.Add(dog); await f.Db.SaveChangesAsync();
        Assert.Empty((await f.Service.ListAsync(f.Member, f.Family.Id, new(1999, 1, 1), new(1999, 12, 31))).Items);
        Assert.Equal(new DateOnly(2027, 2, 28), Assert.Single((await f.Service.ListAsync(f.Member, f.Family.Id, new(2027, 2, 1), new(2027, 2, 28))).Items).StartDate);
        Assert.Equal(new DateOnly(2028, 2, 29), Assert.Single((await f.Service.ListAsync(f.Member, f.Family.Id, new(2028, 2, 1), new(2028, 2, 29))).Items).StartDate);
        Assert.Equal(new DateOnly(2000, 2, 29), f.Author.BirthDate);
        Assert.Empty((await f.Service.ListAsync(f.Member, f.Family.Id, new(2021, 12, 1), new(2021, 12, 31))).Items);
        f.Author.BirthDate = new(1983, 1, 10); await f.Db.SaveChangesAsync();
        var span = await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 12, 15), new(2027, 1, 15));
        Assert.Equal(new[] { new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 10) }, span.Items.Select(i => i.StartDate));
        var upcoming = await f.Service.ListAsync(f.Member, f.Family.Id, startDate: new(2026, 12, 15), pageSize: 2);
        Assert.Equal(span.Items.Select(i => i.Id), upcoming.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Profile_edits_and_dog_changes_update_projection_without_sync_and_preserve_family_isolation()
    {
        await using var f = await Fixture.Create();
        var profile = new ProfileService(f.Db, new IdentityService(f.Db), f.Files, new MediaCleanupService(f.Files, NullLogger<MediaCleanupService>.Instance));
        var saved = await profile.UpdateMeAsync(f.Creator, new() { DisplayName = "Jeremy", BirthDate = new(1983, 9, 20) });
        Assert.Equal(new DateOnly(1983, 9, 20), saved.BirthDate);
        Assert.Equal(saved.BirthDate, (await profile.GetMeAsync(f.Creator)).BirthDate);
        var outsider = await f.Db.Users.SingleAsync(u => u.DisplayName == "outsider");
        outsider.BirthDate = new(1980, 9, 20);
        var other = new Family { Name = "Other", Slug = "other", CreatedByUserId = outsider.Id };
        var dog = new DogProfile { FamilyId = other.Id, Name = "Private dog", Birthday = new(2020, 9, 20), CreatedByUserId = outsider.Id };
        f.Db.AddRange(other, dog, new FamilyMembership { FamilyId = other.Id, UserId = outsider.Id, Role = FamilyRole.Owner });
        await f.Db.SaveChangesAsync();
        Assert.Single((await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30))).Items);
        await Denied(404, () => f.Service.ListAsync(f.Outsider, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30)));
        await profile.UpdateMeAsync(f.Creator, new() { DisplayName = "Jeremy", BirthDate = new(1983, 10, 3) });
        Assert.Empty((await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30))).Items);
        Assert.Single((await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 10, 1), new(2026, 10, 31))).Items);
        await profile.UpdateMeAsync(f.Creator, new() { DisplayName = "Jeremy", BirthDate = null });
        Assert.Empty((await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 10, 1), new(2026, 10, 31))).Items);
        dog.FamilyId = f.Family.Id; await f.Db.SaveChangesAsync();
        Assert.Single((await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30))).Items);
        dog.Birthday = null; await f.Db.SaveChangesAsync();
        Assert.Empty((await f.Service.ListAsync(f.Member, f.Family.Id, new(2026, 9, 1), new(2026, 9, 30))).Items);
        Assert.Empty(f.Db.CalendarEvents);
    }

    [PostgresFact]
    public async Task Migration_and_historical_overlap_query_work_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)!;
        var schema = "hp_calendar_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
            await command.ExecuteNonQueryAsync();
        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(scoped).Options);
            await db.Database.MigrateAsync();
            await using var f = await Fixture.Create(db);
            f.Author.BirthDate = new(1983, 6, 1);
            db.DogProfiles.Add(new DogProfile { FamilyId = f.Family.Id, Name = "Hoovie", Birthday = new(1990, 6, 2), CreatedByUserId = f.Author.Id });
            await db.SaveChangesAsync();
            var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request("1997-05-28", "1997-06-04"));
            e = await f.Service.AddPhotoAsync(f.Creator, f.Family.Id, e.Id, Reference());
            db.ChangeTracker.Clear();
            var items = (await f.Service.ListAsync(f.Member, f.Family.Id, new(1997, 6, 1), new(1997, 6, 30))).Items;
            Assert.Equal(new[] { "calendarEvent", "memberBirthday", "dogBirthday" }, items.Select(i => i.SourceType));
            await f.Service.DeleteAsync(f.Creator, f.Family.Id, e.Id);
            Assert.Empty(await db.CalendarEventPhotos.ToListAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await using var command = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Historical_dates_are_independent_and_month_queries_include_overlapping_events()
    {
        await using var f = await Fixture.Create();
        var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request("1997-05-28", "1997-06-04"));
        Assert.Equal(new DateOnly(1997, 5, 28), e.StartDate);
        Assert.True(e.CreatedAtUtc.Year >= 2026);
        var june = await f.Service.ListAsync(f.Member, f.Family.Id, new(1997, 6, 1), new(1997, 6, 30));
        Assert.Equal(e.Id, Assert.Single(june.Items).CalendarEventId);
        Assert.Empty((await f.Service.ListAsync(f.Member, f.Family.Id, new(1997, 7, 1), new(1997, 7, 31))).Items);
        Assert.False((await f.Service.GetAsync(f.Member, f.Family.Id, e.Id)).CanManage);
    }

    [Fact]
    public async Task Outsiders_cannot_read_create_change_or_download_events_and_photos()
    {
        await using var f = await Fixture.Create();
        var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request());
        e = await f.Service.AddPhotoAsync(f.Creator, f.Family.Id, e.Id, Reference());
        var photo = Assert.Single(e.Photos);
        await Denied(404, () => f.Service.ListAsync(f.Outsider, f.Family.Id));
        await Denied(404, () => f.Service.GetAsync(f.Outsider, f.Family.Id, e.Id));
        await Denied(404, () => f.Service.SaveAsync(f.Outsider, f.Family.Id, null, Request()));
        await Denied(404, () => f.Service.SaveAsync(f.Outsider, f.Family.Id, e.Id, Request()));
        await Denied(404, () => f.Service.DeleteAsync(f.Outsider, f.Family.Id, e.Id));
        await Denied(404, () => f.Service.DownloadAsync(f.Outsider, f.Family.Id, e.Id, photo.Id));
        await Denied(404, () => f.Service.AddPhotoAsync(f.Outsider, f.Family.Id, e.Id, Reference()));
        await Denied(404, () => f.Service.ChangePhotoAsync(f.Outsider, f.Family.Id, e.Id, photo.Id, true));
        Assert.Equal(0, f.Files.Downloads);
    }

    [Fact]
    public async Task Wrong_family_route_denies_access_even_for_members_of_both_families()
    {
        await using var f = await Fixture.Create();
        var other = new Family { Name = "Other", Slug = "other", CreatedByUserId = f.Author.Id };
        f.Db.AddRange(other, new FamilyMembership { FamilyId = other.Id, UserId = f.Author.Id, Role = FamilyRole.Owner });
        await f.Db.SaveChangesAsync();
        var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request());
        await Denied(404, () => f.Service.GetAsync(f.Creator, other.Id, e.Id));
        await Denied(404, () => f.Service.SaveAsync(f.Creator, other.Id, e.Id, Request()));
        await Denied(404, () => f.Service.DeleteAsync(f.Creator, other.Id, e.Id));
        Assert.Empty((await f.Service.ListAsync(f.Creator, other.Id)).Items);
    }

    [Theory]
    [InlineData("creator")]
    [InlineData("admin")]
    [InlineData("owner")]
    public async Task Creator_admin_and_owner_can_manage_events_and_photos(string actor)
    {
        await using var f = await Fixture.Create();
        var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request());
        var principal = Principal(actor);
        e = await f.Service.SaveAsync(principal, f.Family.Id, e.Id, Request("2012-06-09"));
        Assert.True(e.CanManage);
        Assert.Equal(f.Author.Id, e.CreatedBy.Id);
        e = await f.Service.AddPhotoAsync(principal, f.Family.Id, e.Id, Reference());
        var first = Assert.Single(e.Photos);
        Assert.True(first.IsCover);
        e = await f.Service.AddPhotoAsync(principal, f.Family.Id, e.Id, Reference());
        var second = e.Photos.Last();
        e = await f.Service.ChangePhotoAsync(principal, f.Family.Id, e.Id, second.Id, false);
        Assert.True(e.Photos.Single(p => p.Id == second.Id).IsCover);
        await f.Service.DownloadAsync(f.Member, f.Family.Id, e.Id, second.Id);
        Assert.Equal(1, f.Files.Downloads);
        e = await f.Service.ChangePhotoAsync(principal, f.Family.Id, e.Id, second.Id, true);
        Assert.True(Assert.Single(e.Photos).IsCover);
        Assert.Contains(second.FileId, f.Files.Deleted);
        await f.Service.DeleteAsync(principal, f.Family.Id, e.Id);
        Assert.Empty(f.Db.CalendarEvents);
        Assert.Empty(f.Db.CalendarEventPhotos);
        Assert.Contains(first.FileId, f.Files.Deleted);
    }

    [Fact]
    public async Task Other_members_can_create_but_cannot_manage_someone_elses_events()
    {
        await using var f = await Fixture.Create();
        var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request());
        e = await f.Service.AddPhotoAsync(f.Creator, f.Family.Id, e.Id, Reference());
        var p = e.Photos.Single();
        await Denied(403, () => f.Service.SaveAsync(f.Member, f.Family.Id, e.Id, Request()));
        await Denied(403, () => f.Service.DeleteAsync(f.Member, f.Family.Id, e.Id));
        await Denied(403, () => f.Service.AddPhotoAsync(f.Member, f.Family.Id, e.Id, Reference()));
        await Denied(403, () => f.Service.ChangePhotoAsync(f.Member, f.Family.Id, e.Id, p.Id, false));
        await Denied(403, () => f.Service.ChangePhotoAsync(f.Member, f.Family.Id, e.Id, p.Id, true));
        Assert.True((await f.Service.SaveAsync(f.Member, f.Family.Id, null, Request())).CanManage);
    }

    [Fact]
    public async Task Validates_dates_times_titles_timezone_and_clears_all_day_times()
    {
        await using var f = await Fixture.Create();
        foreach (var r in new[] { Request("2026-10-02", "2026-10-01"), new SaveCalendarEventRequest { Title = "Missing date" }, new SaveCalendarEventRequest { Title = " ", StartDate = new(1997, 6, 1) } })
            await Denied(400, () => f.Service.SaveAsync(f.Creator, f.Family.Id, null, r));
        var timed = Request();
        timed.IsAllDay = false;
        await Denied(400, () => f.Service.SaveAsync(f.Creator, f.Family.Id, null, timed));
        timed.StartTime = new(17, 0); timed.EndTime = new(16, 0); timed.TimeZoneId = "America/New_York";
        await Denied(400, () => f.Service.SaveAsync(f.Creator, f.Family.Id, null, timed));
        timed.EndTime = new(18, 0); timed.TimeZoneId = "Invalid/timezone";
        await Denied(400, () => f.Service.SaveAsync(f.Creator, f.Family.Id, null, timed));
        timed.TimeZoneId = "America/New_York";
        var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, timed);
        Assert.Equal(new TimeOnly(17, 0), e.StartTime);
        timed.IsAllDay = true;
        e = await f.Service.SaveAsync(f.Creator, f.Family.Id, e.Id, timed);
        Assert.Null(e.StartTime); Assert.Null(e.EndTime); Assert.Null(e.TimeZoneId);
        Assert.Equal(new DateOnly(1, 1, 1), (await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request("0001-01-01"))).StartDate);
    }

    [Fact]
    public async Task Timeline_orders_by_occurrence_and_pages_without_overflow()
    {
        await using var f = await Fixture.Create();
        foreach (var date in new[] { "2018-07-14", "1997-06-01", "2012-06-09" })
            await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request(date));
        var page = await f.Service.ListAsync(f.Member, f.Family.Id, direction: "history", page: 2, pageSize: 1);
        Assert.Equal(new DateOnly(2012, 6, 9), Assert.Single(page.Items).StartDate);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new DateOnly(1997, 6, 1), (await f.Service.ListAsync(f.Member, f.Family.Id)).Items.First().StartDate);
        Assert.Empty((await f.Service.ListAsync(f.Member, f.Family.Id, page: int.MaxValue)).Items);
    }

    [Fact]
    public async Task File_references_require_upload_token_and_cannot_reuse_associated_files()
    {
        await using var f = await Fixture.Create();
        var e = await f.Service.SaveAsync(f.Creator, f.Family.Id, null, Request());
        await Denied(400, () => f.Service.AddPhotoAsync(f.Creator, f.Family.Id, e.Id, new() { FileId = Guid.NewGuid() }));
        var reference = Reference();
        await f.Service.AddPhotoAsync(f.Creator, f.Family.Id, e.Id, reference);
        await Denied(400, () => f.Service.AddPhotoAsync(f.Creator, f.Family.Id, e.Id, reference));
        Assert.Equal(1, f.Files.Completions);
    }

    private static SaveCalendarEventRequest Request(string start = "1997-06-01", string? end = null) => new() { Title = "Family Trip", StartDate = DateOnly.Parse(start), EndDate = end is null ? null : DateOnly.Parse(end) };
    private static FileUploadReferenceRequest Reference() => new() { FileId = Guid.NewGuid(), UploadToken = "valid-token" };
    private static ClaimsPrincipal Principal(string subject) => new(new ClaimsIdentity([new Claim("sub", subject)], "test"));
    private static async Task Denied(int status, Func<Task> action) => Assert.Equal(status, (await Assert.ThrowsAsync<ApiException>(action)).StatusCode);

    private sealed class Fixture : IAsyncDisposable
    {
        public required AppDbContext Db { get; init; }
        public required Family Family { get; init; }
        public required AppUser Author { get; init; }
        public required CalendarService Service { get; init; }
        public required FakeFiles Files { get; init; }
        public ClaimsPrincipal Creator => Principal("creator");
        public ClaimsPrincipal Member => Principal("member");
        public ClaimsPrincipal Outsider => Principal("outsider");
        public static async Task<Fixture> Create(AppDbContext? database = null)
        {
            var db = database ?? new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var users = new[] { "creator", "member", "admin", "owner", "outsider" }.Select(s => new AppUser { AuthProviderUserId = s, Email = s + "@example.test", DisplayName = s }).ToArray();
            var family = new Family { Name = "Pack", Slug = "pack", CreatedByUserId = users[0].Id };
            db.AddRange(users); db.Add(family);
            foreach (var u in users.Take(4))
                db.Add(new FamilyMembership { FamilyId = family.Id, UserId = u.Id, Role = u.DisplayName == "admin" ? FamilyRole.Admin : u.DisplayName == "owner" ? FamilyRole.Owner : FamilyRole.Member });
            await db.SaveChangesAsync();
            var files = new FakeFiles();
            return new() { Db = db, Family = family, Author = users[0], Files = files, Service = new(db, new IdentityService(db), new FamilyAccessService(db), files, new MediaCleanupService(files, NullLogger<MediaCleanupService>.Instance)) };
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeFiles : IFileServiceClient
    {
        public long MaxImageBytes => 10 * 1024 * 1024;
        public int Downloads { get; private set; }
        public int Completions { get; private set; }
        public List<Guid> Deleted { get; } = [];
        public Task<UploadResponse> CreateUploadAsync(CreateUploadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileMetadataResponse> CompleteUploadAsync(Guid fileId, string uploadToken, CancellationToken cancellationToken = default)
        { Completions++; return Task.FromResult(new FileMetadataResponse(fileId, "photo.jpg", "image/jpeg", 1024, DateTimeOffset.UtcNow)); }
        public Task<DownloadResponse> GetDownloadAsync(Guid fileId, CancellationToken cancellationToken = default)
        { Downloads++; return Task.FromResult(new DownloadResponse(fileId, "https://files.example.test/photo", DateTimeOffset.UtcNow.AddMinutes(5))); }
        public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default) { Deleted.Add(fileId); return Task.CompletedTask; }
    }
}
