using System.Text.Json;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IDogipediaBreedService
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    Task<DogipediaPageResponse> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<DogipediaPageResponse> ListAsync(DogipediaBreedSearchRequest request, CancellationToken cancellationToken);
    Task<DogipediaFilterOptions> FilterOptionsAsync(CancellationToken cancellationToken);
    Task<DogipediaBreedResponse> GetAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DogipediaBreedService(AppDbContext db) : IDogipediaBreedService
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        await db.DogipediaSyncStates.AnyAsync(x => x.Key == DogipediaSyncState.CatalogKey && x.LastSuccessfulSyncAtUtc != null, cancellationToken) ||
        await db.DogipediaBreeds.AnyAsync(x => x.IsActive, cancellationToken);

    public Task<DogipediaPageResponse> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken) =>
        ListAsync(new DogipediaBreedSearchRequest { Search = search, Page = page, PageSize = pageSize }, cancellationToken);

    public async Task<DogipediaPageResponse> ListAsync(DogipediaBreedSearchRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var (search, page, pageSize) = (request.Search, request.Page, request.PageSize);
        search = search?.Trim();
        if (search?.Length > 100) throw ApiException.BadRequest("Search must be at most 100 characters.", "search");
        if (page < 1) throw ApiException.BadRequest("Page must be at least 1.", "page");
        if (pageSize is < 1 or > 100) throw ApiException.BadRequest("Page size must be between 1 and 100.", "pageSize");
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue) throw ApiException.BadRequest("Page is too large.", "page");
        var query = db.DogipediaBreeds.AsNoTracking().Where(x => x.IsActive);
        if (request.EnergyMin is not null) query = query.Where(x => x.Energy >= request.EnergyMin);
        if (request.EnergyMax is not null) query = query.Where(x => x.Energy <= request.EnergyMax);
        if (request.TrainabilityMin is not null) query = query.Where(x => x.Trainability >= request.TrainabilityMin);
        if (request.BarkingMax is not null) query = query.Where(x => x.Barking <= request.BarkingMax);
        if (request.GroomingMax is not null) query = query.Where(x => x.Grooming <= request.GroomingMax);
        if (request.SheddingMax is not null) query = query.Where(x => x.Shedding <= request.SheddingMax);
        if (request.DroolingMax is not null) query = query.Where(x => x.Drooling <= request.DroolingMax);
        if (request.GoodWithChildrenMin is not null) query = query.Where(x => x.GoodWithChildren >= request.GoodWithChildrenMin);
        if (request.GoodWithDogsMin is not null) query = query.Where(x => x.GoodWithDogs >= request.GoodWithDogsMin);
        if (request.GoodWithStrangersMin is not null) query = query.Where(x => x.GoodWithStrangers >= request.GoodWithStrangersMin);
        if (request.ApartmentFriendlyMin is not null) query = query.Where(x => x.ApartmentFriendly >= request.ApartmentFriendlyMin);
        if (request.ExerciseMinMinutes is not null) query = query.Where(x => x.ExerciseMinutes >= request.ExerciseMinMinutes);
        if (request.ExerciseMaxMinutes is not null) query = query.Where(x => x.ExerciseMinutes <= request.ExerciseMaxMinutes);
        if (request.MinimumLifeMaxYears is not null) query = query.Where(x => x.LifeMaxYears >= request.MinimumLifeMaxYears);
        if (request.HypoallergenicOnly) query = query.Where(x => x.Hypoallergenic == true);
        if (request.BreedGroupIds.Length > 0) query = query.Where(x => x.BreedGroupId != null && request.BreedGroupIds.Contains(x.BreedGroupId.Value));
        if (request.OriginCountries.Length > 0) query = query.Where(x => x.OriginCountry != null && request.OriginCountries.Contains(x.OriginCountry.Trim().ToLower()));
        if (request.CoatTypes.Length > 0) query = query.Where(x => x.CoatType != null && request.CoatTypes.Contains(x.CoatType.Trim().ToLower()));
        if (request.CoatLengths.Length > 0) query = query.Where(x => x.CoatLength != null && request.CoatLengths.Contains(x.CoatLength.Trim().ToLower()));
        if (request.AdultWeightMaxKg is not null) query = query.Where(x => (x.MaleWeightMinKg == null ? x.FemaleWeightMinKg : x.FemaleWeightMinKg == null ? x.MaleWeightMinKg : Math.Min(x.MaleWeightMinKg.Value, x.FemaleWeightMinKg.Value)) <= request.AdultWeightMaxKg);
        if (request.AdultWeightMinKg is not null) query = query.Where(x => (x.MaleWeightMaxKg == null ? x.FemaleWeightMaxKg : x.FemaleWeightMaxKg == null ? x.MaleWeightMaxKg : Math.Max(x.MaleWeightMaxKg.Value, x.FemaleWeightMaxKg.Value)) >= request.AdultWeightMinKg);
        if (request.AdultHeightMaxCm is not null) query = query.Where(x => (x.MaleHeightMinCm == null ? x.FemaleHeightMinCm : x.FemaleHeightMinCm == null ? x.MaleHeightMinCm : Math.Min(x.MaleHeightMinCm.Value, x.FemaleHeightMinCm.Value)) <= request.AdultHeightMaxCm);
        if (request.AdultHeightMinCm is not null) query = query.Where(x => (x.MaleHeightMaxCm == null ? x.FemaleHeightMaxCm : x.FemaleHeightMaxCm == null ? x.MaleHeightMaxCm : Math.Max(x.MaleHeightMaxCm.Value, x.FemaleHeightMaxCm.Value)) >= request.AdultHeightMinCm);
        // Collection semantics are evaluated over a small projection before count/page.
        if (request.Temperaments.Length > 0 || request.CoatColors.Length > 0 || request.RecognizedBy.Length > 0)
        {
            var candidates = await query.Select(x => new { x.Id, x.Temperament, x.CoatColors, x.RecognizedBy }).ToListAsync(cancellationToken);
            var ids = candidates.Where(x =>
                request.Temperaments.All(t => DogipediaBreedSearchRequest.Normalize(x.Temperament).Contains(t)) &&
                (request.CoatColors.Length == 0 || request.CoatColors.Any(t => DogipediaBreedSearchRequest.Normalize(x.CoatColors).Any(c => c.Contains(t, StringComparison.Ordinal)))) &&
                (request.RecognizedBy.Length == 0 || request.RecognizedBy.Any(t => DogipediaBreedSearchRequest.Normalize(x.RecognizedBy).Contains(t))))
                .Select(x => x.Id).ToArray();
            query = query.Where(x => ids.Contains(x.Id));
        }
        IOrderedQueryable<DogipediaBreed> ordered;
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Treat %, _ and backslash as literal user input, not LIKE wildcards.
            var term = search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var contains = $"%{term}%";
            var starts = $"{term}%";
            query = query.Where(x => EF.Functions.ILike(x.SearchText, contains, "\\") || EF.Functions.ILike(x.Name, contains, "\\"));
            ordered = query.OrderBy(x => EF.Functions.ILike(x.Name, term, "\\") ? 0 :
                EF.Functions.ILike(x.Name, starts, "\\") ? 1 : EF.Functions.ILike(x.Name, contains, "\\") ? 2 : 3)
                .ThenBy(x => x.Name).ThenBy(x => x.Id);
        }
        else ordered = query.OrderBy(x => x.Name).ThenBy(x => x.Id);
        var count = await query.CountAsync(cancellationToken);
        // Project only card fields and the first active image; never materialize the full catalog.
        var cards = await ordered.Skip((int)offset).Take(pageSize).Select(x => new DogipediaBreedCardResponse(
            x.Id, x.Name, x.Description == null ? null : x.Description.Length > 180 ? x.Description.Substring(0, 180) + "…" : x.Description,
            x.BreedGroup == null ? null : x.BreedGroup.Name, x.LifeMinYears, x.LifeMaxYears,
            x.Images.Where(i => i.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => new DogipediaCardImageResponse(i.ThumbUrl, i.MediumUrl, i.Author, i.License, i.LicenseUrl, i.Source, i.SourceUrl))
                .FirstOrDefault())).ToListAsync(cancellationToken);
        return new(cards.Select(x => x with { Image = SafeImage(x.Image) }).ToList(), page, pageSize, count);
    }

    public async Task<DogipediaFilterOptions> FilterOptionsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.DogipediaBreeds.AsNoTracking().Where(x => x.IsActive).Select(x => new
        {
            Group = x.BreedGroup == null ? null : new DogipediaGroupResponse(x.BreedGroup.Id, x.BreedGroup.Name),
            x.CoatType, x.CoatLength, x.CoatColors, x.Temperament, x.RecognizedBy, x.OriginCountry,
            x.ExerciseMinutes, x.LifeMaxYears, x.MaleWeightMinKg, x.FemaleWeightMinKg,
            x.MaleWeightMaxKg, x.FemaleWeightMaxKg, x.MaleHeightMinCm, x.FemaleHeightMinCm,
            x.MaleHeightMaxCm, x.FemaleHeightMaxCm
        }).ToListAsync(cancellationToken);
        static DogipediaFilterRange Range(IEnumerable<decimal?> values)
        {
            var known = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            return known.Length == 0 ? new(null, null) : new(known.Min(), known.Max());
        }
        return new(new(1, 5), Range(rows.Select(x => (decimal?)x.ExerciseMinutes)), Range(rows.Select(x => x.LifeMaxYears)),
            Range(rows.SelectMany(x => new[] { x.MaleWeightMinKg, x.FemaleWeightMinKg, x.MaleWeightMaxKg, x.FemaleWeightMaxKg })),
            Range(rows.SelectMany(x => new[] { x.MaleHeightMinCm, x.FemaleHeightMinCm, x.MaleHeightMaxCm, x.FemaleHeightMaxCm })),
            rows.Where(x => x.Group != null).Select(x => x.Group!).DistinctBy(x => x.Id).OrderBy(x => x.Name).ThenBy(x => x.Id).ToArray(),
            DogipediaBreedSearchRequest.Normalize(rows.Select(x => x.CoatType!)),
            DogipediaBreedSearchRequest.Normalize(rows.Select(x => x.CoatLength!)),
            DogipediaBreedSearchRequest.Normalize(rows.SelectMany(x => x.CoatColors ?? [])),
            DogipediaBreedSearchRequest.Normalize(rows.SelectMany(x => x.Temperament ?? [])),
            DogipediaBreedSearchRequest.Normalize(rows.SelectMany(x => x.RecognizedBy ?? [])),
            DogipediaBreedSearchRequest.Normalize(rows.Select(x => x.OriginCountry!)));
    }

    public async Task<DogipediaBreedResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var b = await db.DogipediaBreeds.AsNoTracking().Include(x => x.BreedGroup)
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken)
            ?? throw ApiException.NotFound("This breed could not be found.");
        var images = await db.DogipediaBreedImages.AsNoTracking()
            .Where(i => i.BreedId == b.Id && i.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new DogipediaImageResponse(i.Id, i.ThumbUrl, i.MediumUrl, i.LargeUrl,
                i.Author, i.License, i.LicenseUrl, i.Source, i.SourceUrl)).ToListAsync(cancellationToken);
        images = images.Select(i => i with
        {
            ThumbUrl = SafeUrl(i.ThumbUrl), MediumUrl = SafeUrl(i.MediumUrl), LargeUrl = SafeUrl(i.LargeUrl),
            LicenseUrl = SafeUrl(i.LicenseUrl), SourceUrl = SafeUrl(i.SourceUrl)
        }).ToList();
        var related = await GetRelatedBreedsAsync(b.Id, b.BreedGroupId, cancellationToken);
        var sources = JsonSerializer.Deserialize<List<DogipediaSourceResponse>>(b.Sources, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        return new(b.Id, b.Name, b.Description, b.Hypoallergenic,
            b.BreedGroup is null ? null : new(b.BreedGroup.Id, b.BreedGroup.Name),
            new(b.OriginCountry, b.OriginRegion, b.OriginEra), new(b.LifeMinYears, b.LifeMaxYears),
            new(b.MaleWeightMinKg, b.MaleWeightMaxKg), new(b.FemaleWeightMinKg, b.FemaleWeightMaxKg),
            new(b.MaleHeightMinCm, b.MaleHeightMaxCm), new(b.FemaleHeightMinCm, b.FemaleHeightMaxCm),
            new(b.CoatType, b.CoatLength, b.CoatColors),
            new(b.Energy, b.Trainability, b.Barking, b.Grooming, b.Shedding, b.Drooling, b.GoodWithChildren,
                b.GoodWithDogs, b.GoodWithStrangers, b.ApartmentFriendly, b.ExerciseMinutes, b.Temperament),
            b.OtherNames, b.RecognizedBy, sources.Select(s => s with { Url = SafeUrl(s.Url) }).ToList(),
            images.FirstOrDefault(), images, related);
    }

    private async Task<IReadOnlyList<DogipediaRelatedBreedResponse>> GetRelatedBreedsAsync(
        Guid breedId, Guid? groupId, CancellationToken cancellationToken)
    {
        // Ungrouped breeds are not considered related to each other.
        if (groupId is null) return [];
        // One projection for the entire section, including each card's primary image.
        var related = await db.DogipediaBreeds.AsNoTracking()
            .Where(b => b.IsActive && b.Id != breedId && b.BreedGroupId == groupId)
            .OrderBy(b => b.Name).ThenBy(b => b.Id).Take(6)
            .Select(b => new DogipediaRelatedBreedResponse(b.Id, b.Name, b.BreedGroup == null ? null : b.BreedGroup.Name,
                b.Images.Where(i => i.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => new DogipediaCardImageResponse(i.ThumbUrl, i.MediumUrl, i.Author,
                        i.License, i.LicenseUrl, i.Source, i.SourceUrl)).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return related.Select(b => b with { Image = SafeImage(b.Image) }).ToList();
    }

    public static string? SafeUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.AbsoluteUri : null;

    private static DogipediaCardImageResponse? SafeImage(DogipediaCardImageResponse? image) => image is null ? null : image with
    { ThumbUrl = SafeUrl(image.ThumbUrl), MediumUrl = SafeUrl(image.MediumUrl), LicenseUrl = SafeUrl(image.LicenseUrl), SourceUrl = SafeUrl(image.SourceUrl) };
}
