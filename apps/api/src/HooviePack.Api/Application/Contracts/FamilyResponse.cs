using HooviePack.Api.Domain;

namespace HooviePack.Api.Application.Contracts;

public sealed record FamilyResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid CreatedByUserId,
    FamilyRole Role,
    int MemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
