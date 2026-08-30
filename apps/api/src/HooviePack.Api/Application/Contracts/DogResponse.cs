namespace HooviePack.Api.Application.Contracts;

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
