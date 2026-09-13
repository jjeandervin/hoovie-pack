namespace HooviePack.Api.Application.Contracts;

public sealed record DogipediaPageResponse(IReadOnlyList<DogipediaBreedCardResponse> Items,
    int Page, int PageSize, int TotalItems)
{
    public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
public sealed record DogipediaBreedCardResponse(Guid Id, string Name, string? DescriptionExcerpt,
    string? GroupName, decimal? LifeMinYears, decimal? LifeMaxYears, DogipediaCardImageResponse? Image);
public record DogipediaCardImageResponse(string? ThumbUrl, string? MediumUrl, string? Author,
    string? License, string? LicenseUrl, string? Source, string? SourceUrl);
public sealed record DogipediaImageResponse(Guid Id, string? ThumbUrl, string? MediumUrl, string? LargeUrl,
    string? Author, string? License, string? LicenseUrl, string? Source, string? SourceUrl);
public sealed record DogipediaRelatedBreedResponse(Guid Id, string Name, string? GroupName,
    DogipediaCardImageResponse? Image);
public sealed record DogipediaGroupResponse(Guid Id, string Name);
public sealed record DogipediaOriginResponse(string? Country, string? Region, string? Era);
public sealed record DogipediaLifeResponse(decimal? MinYears, decimal? MaxYears);
public sealed record DogipediaWeightResponse(decimal? MinKg, decimal? MaxKg);
public sealed record DogipediaHeightResponse(decimal? MinCm, decimal? MaxCm);
public sealed record DogipediaCoatResponse(string? Type, string? Length, string[] Colors);
public sealed record DogipediaTraitsResponse(int? Energy, int? Trainability, int? Barking, int? Grooming,
    int? Shedding, int? Drooling, int? GoodWithChildren, int? GoodWithDogs, int? GoodWithStrangers,
    int? ApartmentFriendly, int? ExerciseMinutes, string[] Temperament);
public sealed record DogipediaSourceResponse(string? Title, string? Url);
public sealed record DogipediaBreedResponse(Guid Id, string Name, string? Description, bool? Hypoallergenic,
    DogipediaGroupResponse? Group, DogipediaOriginResponse Origin, DogipediaLifeResponse Life,
    DogipediaWeightResponse MaleWeight, DogipediaWeightResponse FemaleWeight,
    DogipediaHeightResponse MaleHeight, DogipediaHeightResponse FemaleHeight,
    DogipediaCoatResponse Coat, DogipediaTraitsResponse Traits, string[] OtherNames, string[] RecognizedBy,
    IReadOnlyList<DogipediaSourceResponse> Sources, DogipediaImageResponse? PrimaryImage,
    IReadOnlyList<DogipediaImageResponse> Images, IReadOnlyList<DogipediaRelatedBreedResponse> RelatedBreeds)
{
    // Retain the Phase 1 field during rolling API/client deployments.
    public DogipediaImageResponse? Image => PrimaryImage;
}
