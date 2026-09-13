namespace HooviePack.Api.Domain;

public sealed class DogipediaBreedGroup : Entity
{
    public Guid ExternalGroupId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSyncedAtUtc { get; set; }
}
