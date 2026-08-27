namespace HooviePack.Api.Domain;

public sealed class Family : Entity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public AppUser CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<FamilyMembership> Memberships { get; set; } = [];
    public ICollection<FamilyInvite> Invites { get; set; } = [];
    public ICollection<DogProfile> Dogs { get; set; } = [];
    public ICollection<Post> Posts { get; set; } = [];
}
