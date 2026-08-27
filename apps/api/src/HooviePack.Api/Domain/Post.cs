namespace HooviePack.Api.Domain;

public sealed class Post : Entity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;
    public Guid AuthorUserId { get; set; }
    public AppUser AuthorUser { get; set; } = null!;
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsEdited { get; set; }

    public ICollection<PostPhoto> Photos { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
    public ICollection<Reaction> Reactions { get; set; } = [];
}
