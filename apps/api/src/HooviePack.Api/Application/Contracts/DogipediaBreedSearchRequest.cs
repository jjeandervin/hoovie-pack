using HooviePack.Api.Application;
namespace HooviePack.Api.Application.Contracts;

public sealed class DogipediaBreedSearchRequest
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
    public string Sort { get; set; } = "name";
    public bool HypoallergenicOnly { get; set; }
    public Guid[] BreedGroupIds { get; set; } = [];
    public string[] OriginCountries { get; set; } = [];
    public string[] CoatTypes { get; set; } = [];
    public string[] CoatLengths { get; set; } = [];
    public string[] CoatColors { get; set; } = [];
    public string[] Temperaments { get; set; } = [];
    public string[] RecognizedBy { get; set; } = [];
    public int? EnergyMin { get; set; }
    public int? EnergyMax { get; set; }
    public int? TrainabilityMin { get; set; }
    public int? BarkingMax { get; set; }
    public int? GroomingMax { get; set; }
    public int? SheddingMax { get; set; }
    public int? DroolingMax { get; set; }
    public int? GoodWithChildrenMin { get; set; }
    public int? GoodWithDogsMin { get; set; }
    public int? GoodWithStrangersMin { get; set; }
    public int? ApartmentFriendlyMin { get; set; }
    public int? ExerciseMinMinutes { get; set; }
    public int? ExerciseMaxMinutes { get; set; }
    public decimal? MinimumLifeMaxYears { get; set; }
    public decimal? AdultWeightMinKg { get; set; }
    public decimal? AdultWeightMaxKg { get; set; }
    public decimal? AdultHeightMinCm { get; set; }
    public decimal? AdultHeightMaxCm { get; set; }

    public void Validate()
    {
        if (Sort != "name") throw ApiException.BadRequest("Unsupported sort.", "sort");
        if (EnergyMin is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(EnergyMin));
        if (EnergyMax is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(EnergyMax));
        if (TrainabilityMin is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(TrainabilityMin));
        if (BarkingMax is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(BarkingMax));
        if (GroomingMax is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(GroomingMax));
        if (SheddingMax is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(SheddingMax));
        if (DroolingMax is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(DroolingMax));
        if (GoodWithChildrenMin is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(GoodWithChildrenMin));
        if (GoodWithDogsMin is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(GoodWithDogsMin));
        if (GoodWithStrangersMin is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(GoodWithStrangersMin));
        if (ApartmentFriendlyMin is < 1 or > 5) throw ApiException.BadRequest("Rating must be between 1 and 5.", nameof(ApartmentFriendlyMin));
        if (ExerciseMinMinutes < 0) throw ApiException.BadRequest("Value must be non-negative.", nameof(ExerciseMinMinutes));
        if (ExerciseMaxMinutes < 0) throw ApiException.BadRequest("Value must be non-negative.", nameof(ExerciseMaxMinutes));
        if (MinimumLifeMaxYears < 0) throw ApiException.BadRequest("Value must be non-negative.", nameof(MinimumLifeMaxYears));
        if (AdultWeightMinKg < 0) throw ApiException.BadRequest("Value must be non-negative.", nameof(AdultWeightMinKg));
        if (AdultWeightMaxKg < 0) throw ApiException.BadRequest("Value must be non-negative.", nameof(AdultWeightMaxKg));
        if (AdultHeightMinCm < 0) throw ApiException.BadRequest("Value must be non-negative.", nameof(AdultHeightMinCm));
        if (AdultHeightMaxCm < 0) throw ApiException.BadRequest("Value must be non-negative.", nameof(AdultHeightMaxCm));
        if (EnergyMin > EnergyMax) throw ApiException.BadRequest("Minimum must not exceed maximum.", nameof(EnergyMin));
        if (ExerciseMinMinutes > ExerciseMaxMinutes) throw ApiException.BadRequest("Minimum must not exceed maximum.", nameof(ExerciseMinMinutes));
        if (AdultWeightMinKg > AdultWeightMaxKg) throw ApiException.BadRequest("Minimum must not exceed maximum.", nameof(AdultWeightMinKg));
        if (AdultHeightMinCm > AdultHeightMaxCm) throw ApiException.BadRequest("Minimum must not exceed maximum.", nameof(AdultHeightMinCm));
        OriginCountries = Normalize(OriginCountries);
        CoatTypes = Normalize(CoatTypes);
        CoatLengths = Normalize(CoatLengths);
        CoatColors = Normalize(CoatColors);
        Temperaments = Normalize(Temperaments);
        RecognizedBy = Normalize(RecognizedBy);
    }
    public static string[] Normalize(IEnumerable<string>? values) => (values ?? [])
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant())
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}

public sealed record DogipediaFilterRange(decimal? Min, decimal? Max);
public sealed record DogipediaFilterOptions(DogipediaFilterRange TraitScale,
    DogipediaFilterRange ExerciseMinutes, DogipediaFilterRange LifeMaxYears,
    DogipediaFilterRange AdultWeightKg, DogipediaFilterRange AdultHeightCm,
    IReadOnlyList<DogipediaGroupResponse> BreedGroups, string[] CoatTypes, string[] CoatLengths,
    string[] CoatColors, string[] Temperaments, string[] RecognizedBy, string[] OriginCountries);
