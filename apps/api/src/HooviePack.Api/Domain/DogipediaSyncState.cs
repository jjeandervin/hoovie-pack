namespace HooviePack.Api.Domain;

public sealed class DogipediaSyncState
{
    public const string CatalogKey = "dog-api-catalog";
    public string Key { get; set; } = CatalogKey;
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? LastSuccessfulSyncAtUtc { get; set; }
    public int LastSuccessfulBreedCount { get; set; }
    public int LastSuccessfulGroupCount { get; set; }
    public int LastSuccessfulImageCount { get; set; }
    public DateTimeOffset? LastFailureAtUtc { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
}
