namespace HooviePack.Api.Infrastructure.Storage.Contracts;

public sealed record DownloadResponse(
    Guid FileId,
    string DownloadUrl,
    DateTimeOffset ExpiresAt);
