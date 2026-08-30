namespace HooviePack.Api.Domain;

public sealed class PostPhoto : Entity
{
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid? FileId { get; set; }
    public string? StoragePath { get; set; }
    public required string OriginalFileName { get; set; }
    public required string ContentType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
