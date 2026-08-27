namespace HooviePack.Api.Domain;

public sealed class Comment : Entity
{
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid AuthorUserId { get; set; }
    public AppUser AuthorUser { get; set; } = null!;
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
