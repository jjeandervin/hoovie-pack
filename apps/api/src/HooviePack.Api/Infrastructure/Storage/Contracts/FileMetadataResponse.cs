namespace HooviePack.Api.Infrastructure.Storage.Contracts;

public sealed record FileMetadataResponse(
    Guid FileId,
    string OriginalFileName,
    string ContentType,
    long Size,
    DateTimeOffset CreatedAt);
