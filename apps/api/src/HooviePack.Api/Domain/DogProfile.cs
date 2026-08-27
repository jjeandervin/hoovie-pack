namespace HooviePack.Api.Domain;

public sealed class DogProfile : Entity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;
    public required string Name { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PhotoStoragePath { get; set; }
    public string? PhotoContentType { get; set; }
    public string? Breed { get; set; }
    public DateOnly? Birthday { get; set; }
    public int? ApproximateAgeYears { get; set; }
    public string? Bio { get; set; }
    public string? FavoriteThing { get; set; }
    public Guid? OwnerMembershipId { get; set; }
    public FamilyMembership? OwnerMembership { get; set; }
    public Guid CreatedByUserId { get; set; }
    public AppUser CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
