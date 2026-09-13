using HooviePack.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Infrastructure.Data;

internal static class DogipediaModelConfiguration
{
    public static void ConfigureDogipedia(this ModelBuilder model)
    {
        model.Entity<DogipediaBreed>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ExternalBreedId).IsUnique();
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.IsActive);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Sources).HasColumnType("jsonb");
            e.HasOne(x => x.BreedGroup).WithMany().HasForeignKey(x => x.BreedGroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DogipediaBreedGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ExternalGroupId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(300);
        });
        model.Entity<DogipediaBreedImage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ExternalImageId).IsUnique();
            e.HasOne(x => x.Breed).WithMany(x => x.Images).HasForeignKey(x => x.BreedId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<DogipediaSyncState>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(80);
            e.Property(x => x.LastError).HasMaxLength(2000);
        });
    }
}
