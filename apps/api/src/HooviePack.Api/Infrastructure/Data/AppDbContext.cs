using HooviePack.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<CalendarEventPhoto> CalendarEventPhotos => Set<CalendarEventPhoto>();
    public DbSet<DogQuote> DogQuotes => Set<DogQuote>();
    public DbSet<DogipediaBreed> DogipediaBreeds => Set<DogipediaBreed>();
    public DbSet<DogipediaBreedGroup> DogipediaBreedGroups => Set<DogipediaBreedGroup>();
    public DbSet<DogipediaBreedImage> DogipediaBreedImages => Set<DogipediaBreedImage>();
    public DbSet<DogipediaSyncState> DogipediaSyncStates => Set<DogipediaSyncState>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<FamilyMembership> FamilyMemberships => Set<FamilyMembership>();
    public DbSet<FamilyInvite> FamilyInvites => Set<FamilyInvite>();
    public DbSet<DogProfile> DogProfiles => Set<DogProfile>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostPhoto> PostPhotos => Set<PostPhoto>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Reaction> Reactions => Set<Reaction>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareDogQuotes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareDogQuotes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareDogQuotes()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<DogQuote>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            if (string.IsNullOrWhiteSpace(entry.Entity.Text))
                throw new InvalidOperationException("Dog quote text is required.");
            entry.Entity.Text = entry.Entity.Text.Trim();
            if (entry.State == EntityState.Added) entry.Entity.CreatedAtUtc = now;
            entry.Entity.UpdatedAtUtc = now;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureDogipedia();
        modelBuilder.ConfigureCalendar();
        modelBuilder.ApplyConfiguration(new DogQuoteConfiguration());

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.AuthProviderUserId).IsUnique();
            entity.HasIndex(x => x.AvatarFileId).IsUnique();
            entity.Property(x => x.AuthProviderUserId).HasMaxLength(255);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.DisplayName).HasMaxLength(100);
            entity.Property(x => x.AvatarUrl).HasMaxLength(500);
            entity.Property(x => x.AvatarStoragePath).HasMaxLength(500);
            entity.Property(x => x.AvatarContentType).HasMaxLength(100);
            entity.Property(x => x.Bio).HasMaxLength(500);
        });

        modelBuilder.Entity<Family>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Slug).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FamilyMembership>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.FamilyId, x.UserId }).IsUnique();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(x => x.Family)
                .WithMany(x => x.Memberships)
                .HasForeignKey(x => x.FamilyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany(x => x.Memberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FamilyInvite>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CodeHash).IsUnique();
            entity.Property(x => x.CodeHash).HasMaxLength(64);
            entity.Property(x => x.CodeHint).HasMaxLength(16);
            entity.HasOne(x => x.Family)
                .WithMany(x => x.Invites)
                .HasForeignKey(x => x.FamilyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RedeemedByUser)
                .WithMany()
                .HasForeignKey(x => x.RedeemedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DogProfile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PhotoFileId).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.PhotoUrl).HasMaxLength(500);
            entity.Property(x => x.PhotoStoragePath).HasMaxLength(500);
            entity.Property(x => x.PhotoContentType).HasMaxLength(100);
            entity.Property(x => x.Breed).HasMaxLength(100);
            entity.Property(x => x.Bio).HasMaxLength(500);
            entity.Property(x => x.FavoriteThing).HasMaxLength(200);
            entity.HasOne(x => x.Family)
                .WithMany(x => x.Dogs)
                .HasForeignKey(x => x.FamilyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OwnerMembership)
                .WithMany(x => x.OwnedDogs)
                .HasForeignKey(x => x.OwnerMembershipId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.FamilyId, x.CreatedAt });
            entity.Property(x => x.Content).HasMaxLength(2000);
            entity.HasOne(x => x.Family)
                .WithMany(x => x.Posts)
                .HasForeignKey(x => x.FamilyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.AuthorUser)
                .WithMany(x => x.Posts)
                .HasForeignKey(x => x.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PostPhoto>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PostId, x.SortOrder }).IsUnique();
            entity.HasIndex(x => x.FileId).IsUnique();
            entity.Property(x => x.StoragePath).HasMaxLength(500);
            entity.Property(x => x.OriginalFileName).HasMaxLength(255);
            entity.Property(x => x.ContentType).HasMaxLength(100);
            entity.HasOne(x => x.Post)
                .WithMany(x => x.Photos)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PostId, x.CreatedAt });
            entity.Property(x => x.Content).HasMaxLength(500);
            entity.HasOne(x => x.Post)
                .WithMany(x => x.Comments)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.AuthorUser)
                .WithMany(x => x.Comments)
                .HasForeignKey(x => x.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Reaction>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PostId, x.UserId, x.Type }).IsUnique();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(x => x.Post)
                .WithMany(x => x.Reactions)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany(x => x.Reactions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
