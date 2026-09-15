using HooviePack.Api.Application;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Tests;

public sealed partial class DogipediaTests
{
    private static readonly (string Parameter, string Field, bool Minimum)[] RatingCases = [
        ("EnergyMin", "Energy", true), ("EnergyMax", "Energy", false), ("TrainabilityMin", "Trainability", true),
        ("BarkingMax", "Barking", false), ("GroomingMax", "Grooming", false), ("SheddingMax", "Shedding", false),
        ("DroolingMax", "Drooling", false), ("GoodWithChildrenMin", "GoodWithChildren", true),
        ("GoodWithDogsMin", "GoodWithDogs", true), ("GoodWithStrangersMin", "GoodWithStrangers", true),
        ("ApartmentFriendlyMin", "ApartmentFriendly", true)
    ];

    [Fact]
    public void Search_validation_rejects_bad_ranges_and_normalizes_collections()
    {
        foreach (var (parameter, _, _) in RatingCases)
        foreach (var value in new[] { 0, 6 })
        {
            var request = new DogipediaBreedSearchRequest();
            typeof(DogipediaBreedSearchRequest).GetProperty(parameter)!.SetValue(request, value);
            Assert.Throws<ApiException>(request.Validate);
        }
        foreach (var request in new DogipediaBreedSearchRequest[] {
            new() { EnergyMin = 4, EnergyMax = 2 }, new() { ExerciseMinMinutes = 60, ExerciseMaxMinutes = 30 },
            new() { AdultWeightMinKg = 30, AdultWeightMaxKg = 20 }, new() { AdultHeightMinCm = 50, AdultHeightMaxCm = 40 },
            new() { ExerciseMaxMinutes = -1 }, new() { AdultWeightMinKg = -1 }, new() { MinimumLifeMaxYears = -1 }, new() { Sort = "best-match" }
        }) Assert.Throws<ApiException>(request.Validate);
        var normalized = new DogipediaBreedSearchRequest { Temperaments = [" Friendly ", "friendly", "", "  ", "LOYAL"] };
        normalized.Validate();
        Assert.Equal(new[] { "friendly", "loyal" }, normalized.Temperaments);
    }

    [PostgresFact]
    public Task Every_rating_filters_inclusively_and_excludes_missing_only_when_required() => WithDatabase(async (_, db) =>
    {
        var rows = Enumerable.Range(1, 5).Select(n => new DogipediaBreed { Name = $"Rating {n}", ExternalBreedId = Guid.NewGuid(), IsActive = true }).ToArray();
        for (var i = 0; i < rows.Length; i++) foreach (var (_, field, _) in RatingCases)
            typeof(DogipediaBreed).GetProperty(field)!.SetValue(rows[i], i + 1);
        db.DogipediaBreeds.AddRange(rows);
        db.DogipediaBreeds.Add(new() { Name = "Missing", ExternalBreedId = Guid.NewGuid(), IsActive = true });
        db.DogipediaBreeds.Add(new() { Name = "Inactive", ExternalBreedId = Guid.NewGuid(), Energy = 5, IsActive = false });
        await db.SaveChangesAsync();
        var service = new DogipediaBreedService(db);
        Assert.Equal(6, (await service.ListAsync(new(), default)).TotalItems);
        foreach (var (parameter, _, minimum) in RatingCases)
        foreach (var threshold in new[] { 1, 3, 5 })
        {
            var request = new DogipediaBreedSearchRequest();
            typeof(DogipediaBreedSearchRequest).GetProperty(parameter)!.SetValue(request, threshold);
            var result = await service.ListAsync(request, default);
            Assert.Equal(minimum ? 6 - threshold : threshold, result.TotalItems);
            Assert.Contains(result.Items, x => x.Name == $"Rating {threshold}");
            Assert.DoesNotContain(result.Items, x => x.Name == "Missing" || x.Name == "Inactive");
        }
        Assert.Equal(2, (await service.ListAsync(new() { GoodWithChildrenMin = 3, SheddingMax = 4, EnergyMax = 4 }, default)).TotalItems);
    });

    [PostgresFact]
    public Task Categories_collections_search_and_pagination_combine_correctly() => WithDatabase(async (_, db) =>
    {
        var groupA = new DogipediaBreedGroup { Name = "Herding", ExternalGroupId = Guid.NewGuid(), IsActive = true };
        var groupB = new DogipediaBreedGroup { Name = "Sporting", ExternalGroupId = Guid.NewGuid(), IsActive = true };
        db.DogipediaBreedGroups.AddRange(groupA, groupB);
        var a = new DogipediaBreed { Name = "Alpha", SearchText = "alpha shepherd", ExternalBreedId = Guid.NewGuid(), IsActive = true,
            BreedGroup = groupA, Hypoallergenic = true, CoatLength = " Short ", CoatType = "DOUBLE", OriginCountry = " England ",
            CoatColors = ["Black and tan"], Temperament = ["FRIENDLY", "Intelligent"], RecognizedBy = ["AKC"] };
        var b = new DogipediaBreed { Name = "Beta", SearchText = "beta shepherd", ExternalBreedId = Guid.NewGuid(), IsActive = true,
            BreedGroup = groupB, CoatLength = "Medium", CoatType = "smooth", OriginCountry = "Scotland",
            CoatColors = ["BLACK and white"], Temperament = ["Friendly"], RecognizedBy = ["FCI"] };
        db.DogipediaBreeds.AddRange(a, b, new() { Name = "Other", ExternalBreedId = Guid.NewGuid(), IsActive = true },
            new() { Name = "Inactive", ExternalBreedId = Guid.NewGuid(), IsActive = false, CoatColors = ["black"] });
        await db.SaveChangesAsync();
        var service = new DogipediaBreedService(db);
        foreach (var request in new DogipediaBreedSearchRequest[] {
            new() { BreedGroupIds = [groupA.Id, groupB.Id] }, new() { CoatLengths = [" short ", "MEDIUM"] },
            new() { CoatTypes = ["double", "smooth"] }, new() { OriginCountries = ["england", "scotland"] },
            new() { RecognizedBy = ["akc", "fci"] }, new() { CoatColors = [" BlAcK ", "red"] }
        }) Assert.Equal(2, (await service.ListAsync(request, default)).TotalItems);
        var all = await service.ListAsync(new() { Temperaments = ["friendly", " INTELLIGENT "] }, default);
        Assert.Equal(a.Id, Assert.Single(all.Items).Id);
        Assert.Equal(a.Id, Assert.Single((await service.ListAsync(new() { HypoallergenicOnly = true }, default)).Items).Id);
        var combined = await service.ListAsync(new() { Search = "shepherd", CoatColors = ["black"], CoatLengths = ["short", "medium"], PageSize = 1, Page = 2 }, default);
        Assert.Equal(2, combined.TotalItems);
        Assert.Equal(b.Id, Assert.Single(combined.Items).Id);
        Assert.Empty((await service.ListAsync(new() { Search = "other", CoatColors = ["black"] }, default)).Items);
        Assert.Empty((await service.ListAsync(new() { CoatTypes = ["unknown"] }, default)).Items);
    });

    [PostgresFact]
    public Task Size_overlap_exercise_lifespan_and_metadata_use_active_known_values() => WithDatabase(async (_, db) =>
    {
        var group = new DogipediaBreedGroup { Name = "Working", ExternalGroupId = Guid.NewGuid(), IsActive = true };
        var large = new DogipediaBreed { Name = "Large", ExternalBreedId = Guid.NewGuid(), IsActive = true, BreedGroup = group,
            MaleWeightMinKg = 20, FemaleWeightMinKg = 18, MaleWeightMaxKg = 28, FemaleWeightMaxKg = 30,
            MaleHeightMinCm = 50, FemaleHeightMinCm = 45, MaleHeightMaxCm = 55, FemaleHeightMaxCm = 60,
            ExerciseMinutes = 60, LifeMaxYears = 14, Temperament = ["Friendly", "friendly", ""], CoatColors = [" BLACK ", "black"] };
        var small = new DogipediaBreed { Name = "Small", ExternalBreedId = Guid.NewGuid(), IsActive = true,
            FemaleWeightMinKg = 5, FemaleWeightMaxKg = 10, FemaleHeightMinCm = 15, FemaleHeightMaxCm = 25, ExerciseMinutes = 20, LifeMaxYears = 20 };
        db.DogipediaBreeds.AddRange(large, small, new() { Name = "Missing", ExternalBreedId = Guid.NewGuid(), IsActive = true },
            new() { Name = "Inactive", ExternalBreedId = Guid.NewGuid(), IsActive = false, ExerciseMinutes = 999, Temperament = ["inactive"] });
        await db.SaveChangesAsync();
        var service = new DogipediaBreedService(db);
        foreach (var request in new DogipediaBreedSearchRequest[] {
            new() { AdultWeightMinKg = 25, AdultWeightMaxKg = 40 }, new() { AdultWeightMaxKg = 18 },
            new() { AdultWeightMinKg = 30 }, new() { AdultHeightMinCm = 60, AdultHeightMaxCm = 70 },
            new() { ExerciseMinMinutes = 60 }, new() { ExerciseMaxMinutes = 60, ExerciseMinMinutes = 60 },
            new() { MinimumLifeMaxYears = 14, ExerciseMinMinutes = 60 }
        }) Assert.Contains((await service.ListAsync(request, default)).Items, x => x.Id == large.Id);
        Assert.Equal(small.Id, Assert.Single((await service.ListAsync(new() { AdultWeightMaxKg = 10 }, default)).Items).Id);
        Assert.Equal(small.Id, Assert.Single((await service.ListAsync(new() { AdultHeightMaxCm = 25 }, default)).Items).Id);
        Assert.Empty((await service.ListAsync(new() { AdultWeightMinKg = 31 }, default)).Items);
        Assert.Equal(small.Id, Assert.Single((await service.ListAsync(new() { MinimumLifeMaxYears = 20 }, default)).Items).Id);
        var metadata = await service.FilterOptionsAsync(default);
        Assert.Equal(new(20, 60), metadata.ExerciseMinutes);
        Assert.Equal(new(14, 20), metadata.LifeMaxYears);
        Assert.Equal(new(5, 30), metadata.AdultWeightKg);
        Assert.Equal(new(15, 60), metadata.AdultHeightCm);
        Assert.Equal(new[] { "friendly" }, metadata.Temperaments);
        Assert.Equal(new[] { "black" }, metadata.CoatColors);
        Assert.Equal(group.Id, Assert.Single(metadata.BreedGroups).Id);
        Assert.Equal("Working", metadata.BreedGroups[0].Name);
    });
}
