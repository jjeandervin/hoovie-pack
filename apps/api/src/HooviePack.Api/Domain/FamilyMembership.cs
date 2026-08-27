namespace HooviePack.Api.Domain;

public sealed class FamilyMembership : Entity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public FamilyRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<DogProfile> OwnedDogs { get; set; } = [];
}
