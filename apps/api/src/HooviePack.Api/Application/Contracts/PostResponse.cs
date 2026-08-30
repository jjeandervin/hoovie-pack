namespace HooviePack.Api.Application.Contracts;

public sealed record PostResponse(
    Guid Id,
    Guid FamilyId,
    UserSummaryResponse Author,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsEdited,
    bool CanEdit,
    bool CanDelete,
    IReadOnlyCollection<PostPhotoResponse> Photos,
    IReadOnlyCollection<CommentResponse> Comments,
    int CommentCount,
    ReactionSummaryResponse Reactions);
