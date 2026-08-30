namespace HooviePack.Api.Application.Contracts;

public sealed record UserSummaryResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    string? Bio);
