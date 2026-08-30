namespace HooviePack.Api.Application.Contracts;

public sealed record InviteResponse(
    Guid Id,
    string CodeHint,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsRedeemed,
    bool IsRevoked,
    string? InviteCode);
