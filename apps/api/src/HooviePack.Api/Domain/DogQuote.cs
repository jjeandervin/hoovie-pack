namespace HooviePack.Api.Domain;

public sealed class DogQuote : Entity
{
    public string Text { get; set; } = "";
    public string? Author { get; set; }
    public string? Work { get; set; }
    public int? Year { get; set; }
    public string? SourceUrl { get; set; }
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
