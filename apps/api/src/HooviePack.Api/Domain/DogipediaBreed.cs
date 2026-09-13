namespace HooviePack.Api.Domain;

public sealed class DogipediaBreed : Entity
{
    public Guid ExternalBreedId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool? Hypoallergenic { get; set; }
    public decimal? LifeMinYears { get; set; }
    public decimal? LifeMaxYears { get; set; }
    public decimal? MaleWeightMinKg { get; set; }
    public decimal? MaleWeightMaxKg { get; set; }
    public decimal? FemaleWeightMinKg { get; set; }
    public decimal? FemaleWeightMaxKg { get; set; }
    public decimal? MaleHeightMinCm { get; set; }
    public decimal? MaleHeightMaxCm { get; set; }
    public decimal? FemaleHeightMinCm { get; set; }
    public decimal? FemaleHeightMaxCm { get; set; }
    public string? OriginCountry { get; set; }
    public string? OriginRegion { get; set; }
    public string? OriginEra { get; set; }
    public string? CoatType { get; set; }
    public string? CoatLength { get; set; }
    public string[] CoatColors { get; set; } = [];
    public string[] Temperament { get; set; } = [];
    public string[] OtherNames { get; set; } = [];
    public string[] RecognizedBy { get; set; } = [];
    public string Sources { get; set; } = "[]";
    public int? Energy { get; set; }
    public int? Trainability { get; set; }
    public int? Barking { get; set; }
    public int? Grooming { get; set; }
    public int? Shedding { get; set; }
    public int? Drooling { get; set; }
    public int? GoodWithChildren { get; set; }
    public int? GoodWithDogs { get; set; }
    public int? GoodWithStrangers { get; set; }
    public int? ApartmentFriendly { get; set; }
    public int? ExerciseMinutes { get; set; }
    public Guid? BreedGroupId { get; set; }
    public DogipediaBreedGroup? BreedGroup { get; set; }
    public ICollection<DogipediaBreedImage> Images { get; set; } = [];
    public string SearchText { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSyncedAtUtc { get; set; }
}
