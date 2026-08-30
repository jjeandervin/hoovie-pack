namespace HooviePack.Api.Application.Contracts;

public sealed record CommentResponse(
    Guid Id,
    Guid PostId,
    UserSummaryResponse Author,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool CanDelete);
