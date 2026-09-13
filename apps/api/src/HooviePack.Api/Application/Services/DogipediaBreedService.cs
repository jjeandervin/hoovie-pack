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
    Task<DogipediaBreedResponse> GetAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DogipediaBreedService(AppDbContext db) : IDogipediaBreedService
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        await db.DogipediaSyncStates.AnyAsync(x => x.Key == DogipediaSyncState.CatalogKey && x.LastSuccessfulSyncAtUtc != null, cancellationToken) ||
        await db.DogipediaBreeds.AnyAsync(x => x.IsActive, cancellationToken);

    public async Task<DogipediaPageResponse> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        search = search?.Trim();
        if (search?.Length > 100) throw ApiException.BadRequest("Search must be at most 100 characters.", "search");
        if (page < 1) throw ApiException.BadRequest("Page must be at least 1.", "page");
        if (pageSize is < 1 or > 100) throw ApiException.BadRequest("Page size must be between 1 and 100.", "pageSize");
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue) throw ApiException.BadRequest("Page is too large.", "page");
        var query = db.DogipediaBreeds.AsNoTracking().Where(x => x.IsActive);
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
