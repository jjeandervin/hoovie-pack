using System.Text.Json.Serialization;

namespace HooviePack.Api.Infrastructure.DogApi;

// These models are confined to the upstream integration. Public API contracts are independent.
internal sealed record DogApiCollectionResponse<T>(
    List<DogApiResource<T>>? Data, DogApiMeta? Meta, DogApiLinks? Links);
internal sealed record DogApiResource<T>(Guid Id, string? Type, T? Attributes, DogApiRelationships? Relationships);
internal sealed record DogApiMeta(DogApiPaginationMeta? Pagination);
internal sealed record DogApiPaginationMeta(int? Current, int? Next, int? Last, int? Records);
internal sealed record DogApiLinks(string? Next);
internal sealed record DogApiRelationships(DogApiRelationship? Group);
internal sealed record DogApiRelationship(DogApiIdentifier? Data);
internal sealed record DogApiIdentifier(Guid Id, string? Type);
internal sealed record DogApiGroupAttributes(string? Name);
internal sealed record DogApiRange(decimal? Min, decimal? Max);
internal sealed record DogApiOrigin(string? Country, string? Region, string? Era);
internal sealed record DogApiCoat(string? Type, string? Length, string[]? Colors);
internal sealed record DogApiSource(string? Title, string? Url);
internal sealed record DogApiImageAttribution(string? Author, string? License,
    [property: JsonPropertyName("license_url")] string? LicenseUrl, string? Source,
    [property: JsonPropertyName("source_url")] string? SourceUrl);
internal sealed record DogApiBreedImage(Guid Id, string? Url, string? Thumb, string? Medium,
    string? Large, DogApiImageAttribution? Attribution);
internal sealed record DogApiTraits(int? Energy, int? Trainability, int? Barking, int? Grooming,
    int? Shedding, int? Drooling,
    [property: JsonPropertyName("good_with_children")] int? GoodWithChildren,
    [property: JsonPropertyName("good_with_dogs")] int? GoodWithDogs,
    [property: JsonPropertyName("good_with_strangers")] int? GoodWithStrangers,
    [property: JsonPropertyName("apartment_friendly")] int? ApartmentFriendly,
    [property: JsonPropertyName("exercise_minutes")] int? ExerciseMinutes, string[]? Temperament);
internal sealed record DogApiBreedAttributes(string? Name, string? Description, bool? Hypoallergenic,
    DogApiRange? Life,
    [property: JsonPropertyName("male_weight")] DogApiRange? MaleWeight,
    [property: JsonPropertyName("female_weight")] DogApiRange? FemaleWeight,
    [property: JsonPropertyName("male_height")] DogApiRange? MaleHeight,
    [property: JsonPropertyName("female_height")] DogApiRange? FemaleHeight,
    DogApiOrigin? Origin, DogApiCoat? Coat, DogApiTraits? Traits,
    [property: JsonPropertyName("other_names")] string[]? OtherNames,
    [property: JsonPropertyName("recognized_by")] string[]? RecognizedBy,
    DogApiSource[]? Sources, DogApiBreedImage[]? Images);
