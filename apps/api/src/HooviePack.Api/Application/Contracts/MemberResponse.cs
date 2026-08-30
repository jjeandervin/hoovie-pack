using HooviePack.Api.Domain;

namespace HooviePack.Api.Application.Contracts;

public sealed record MemberResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    string? Bio,
    FamilyRole Role,
    DateTimeOffset JoinedAt);
