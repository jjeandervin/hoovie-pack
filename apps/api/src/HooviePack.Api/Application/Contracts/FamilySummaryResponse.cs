using HooviePack.Api.Domain;

namespace HooviePack.Api.Application.Contracts;

public sealed record FamilySummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    FamilyRole Role,
    int MemberCount,
    DateTimeOffset CreatedAt);
