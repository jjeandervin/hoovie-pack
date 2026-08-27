namespace HooviePack.Api.Domain;

public sealed class FamilyInvite : Entity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;
    public required string CodeHash { get; set; }
    public required string CodeHint { get; set; }
    public Guid CreatedByUserId { get; set; }
    public AppUser CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }
    public Guid? RedeemedByUserId { get; set; }
    public AppUser? RedeemedByUser { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
