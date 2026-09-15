using HooviePack.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HooviePack.Api.Infrastructure.Data;

internal sealed class DogQuoteConfiguration : IEntityTypeConfiguration<DogQuote>
{
    public void Configure(EntityTypeBuilder<DogQuote> entity)
    {
        entity.ToTable("DogQuotes");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Text).IsRequired().HasColumnType("text");
        entity.Property(x => x.Author).HasMaxLength(250);
        entity.Property(x => x.Work).HasMaxLength(500);
        entity.Property(x => x.SourceUrl).HasMaxLength(2000);
        entity.Property(x => x.Category).HasMaxLength(100);
        entity.Property(x => x.IsActive).HasDefaultValue(true);
        entity.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        entity.HasIndex(x => x.IsActive);
        entity.HasIndex(x => x.Category);
        entity.HasIndex(x => x.Author);
        entity.HasIndex(x => new { x.IsActive, x.Category });
    }
}
