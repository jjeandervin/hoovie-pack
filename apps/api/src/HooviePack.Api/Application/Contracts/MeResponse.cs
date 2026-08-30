namespace HooviePack.Api.Application.Contracts;

public sealed record MeResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string? Bio,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);
