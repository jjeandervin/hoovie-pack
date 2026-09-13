namespace HooviePack.Api.Domain;

public sealed class DogipediaBreedImage : Entity
{
    public Guid ExternalImageId { get; set; }
    public Guid BreedId { get; set; }
    public DogipediaBreed Breed { get; set; } = null!;
    public string? OriginalUrl { get; set; }
    public string? ThumbUrl { get; set; }
    public string? MediumUrl { get; set; }
    public string? LargeUrl { get; set; }
    public string? Author { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public string? Source { get; set; }
    public string? SourceUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset LastSyncedAtUtc { get; set; }
}
