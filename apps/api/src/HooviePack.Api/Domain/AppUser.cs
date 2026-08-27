namespace HooviePack.Api.Domain;

public sealed class AppUser : Entity
{
    public required string AuthProviderUserId { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? AvatarStoragePath { get; set; }
    public string? AvatarContentType { get; set; }
    public string? Bio { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<FamilyMembership> Memberships { get; set; } = [];
    public ICollection<Post> Posts { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
    public ICollection<Reaction> Reactions { get; set; } = [];
}
