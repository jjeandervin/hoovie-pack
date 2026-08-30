namespace HooviePack.Api.Application.Contracts;

public sealed record PostPhotoResponse(
    Guid Id,
    string Url,
    string OriginalFileName,
    string ContentType,
    int Width,
    int Height,
    int SortOrder);
