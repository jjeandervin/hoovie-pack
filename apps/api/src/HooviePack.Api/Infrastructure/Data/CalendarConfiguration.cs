using HooviePack.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Infrastructure.Data;

internal static class CalendarConfiguration
{
    public static void ConfigureCalendar(this ModelBuilder model)
    {
        model.Entity<CalendarEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.FamilyId, x.StartDate });
            e.HasIndex(x => new { x.FamilyId, x.EndDate });
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.EventType).HasMaxLength(40);
            e.Property(x => x.Location).HasMaxLength(500);
            e.Property(x => x.TimeZoneId).HasMaxLength(100);
            e.HasOne(x => x.Family).WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CalendarEventPhoto>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FileId).IsUnique();
            e.HasIndex(x => new { x.CalendarEventId, x.SortOrder }).IsUnique();
            e.HasOne(x => x.CalendarEvent).WithMany(x => x.Photos).HasForeignKey(x => x.CalendarEventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
