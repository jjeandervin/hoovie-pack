using System.ComponentModel.DataAnnotations;
using HooviePack.Api.Domain;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Application.Contracts;

public sealed record UserSummaryResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    string? Bio);

public sealed record MeResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string? Bio,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

public sealed class UpdateProfileRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Bio { get; set; }
}

public sealed class AvatarUploadRequest
{
    [FromForm(Name = "avatar"), Required]
    public IFormFile Avatar { get; set; } = null!;
}

public sealed record FamilySummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    FamilyRole Role,
    int MemberCount,
    DateTimeOffset CreatedAt);

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

public sealed class CreateFamilyRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}

public sealed class UpdateFamilyRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}

public sealed record MemberResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    string? Bio,
    FamilyRole Role,
    DateTimeOffset JoinedAt);

public sealed class UpdateMemberRoleRequest
{
    public FamilyRole Role { get; set; }
}

public sealed class CreateInviteRequest
{
    [Range(1, 30)]
    public int ExpiresInDays { get; set; } = 7;
}

public sealed record InviteResponse(
    Guid Id,
    string CodeHint,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsRedeemed,
    bool IsRevoked,
    string? InviteCode);

public sealed class JoinFamilyRequest
{
    [Required, StringLength(256, MinimumLength = 10)]
    public string InviteCode { get; set; } = string.Empty;
}

public sealed record DogResponse(
    Guid Id,
    Guid FamilyId,
    string Name,
    string? PhotoUrl,
    string? Breed,
    DateOnly? Birthday,
    int? ApproximateAgeYears,
    string? Bio,
    string? FavoriteThing,
    Guid? OwnerMembershipId,
    UserSummaryResponse? Owner,
    bool CanManage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class UpsertDogRequest
{
    [FromForm(Name = "name"), Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [FromForm(Name = "breed"), StringLength(100)]
    public string? Breed { get; set; }

    [FromForm(Name = "birthday")]
    public DateOnly? Birthday { get; set; }

    [FromForm(Name = "approximateAgeYears"), Range(0, 40)]
    public int? ApproximateAgeYears { get; set; }

    [FromForm(Name = "bio"), StringLength(500)]
    public string? Bio { get; set; }

    [FromForm(Name = "favoriteThing"), StringLength(200)]
    public string? FavoriteThing { get; set; }

    [FromForm(Name = "ownerMembershipId")]
    public Guid? OwnerMembershipId { get; set; }

    [FromForm(Name = "photo")]
    public IFormFile? Photo { get; set; }

    [FromForm(Name = "removePhoto")]
    public bool RemovePhoto { get; set; }
}

public sealed record PostPhotoResponse(
    Guid Id,
    string Url,
    string OriginalFileName,
    string ContentType,
    int Width,
    int Height,
    int SortOrder);

public sealed record CommentResponse(
    Guid Id,
    Guid PostId,
    UserSummaryResponse Author,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool CanDelete);

public sealed record ReactionSummaryResponse(
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyCollection<ReactionType> MyReactions);

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

public sealed record PagedResponse<T>(
    IReadOnlyCollection<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed class CreatePostRequest
{
    [FromForm(Name = "content"), StringLength(2000)]
    public string? Content { get; set; }

    [FromForm(Name = "photos")]
    public List<IFormFile> Photos { get; set; } = [];
}

public sealed class UpdatePostRequest
{
    [FromForm(Name = "content"), StringLength(2000)]
    public string? Content { get; set; }

    [FromForm(Name = "photos")]
    public List<IFormFile> Photos { get; set; } = [];

    [FromForm(Name = "removedPhotoIds")]
    public List<Guid> RemovedPhotoIds { get; set; } = [];
}

public sealed class UpsertCommentRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}

public sealed record ToggleReactionResponse(
    bool Added,
    ReactionSummaryResponse Reactions);
