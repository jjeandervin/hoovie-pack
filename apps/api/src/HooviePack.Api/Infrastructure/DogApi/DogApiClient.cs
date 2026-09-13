using System.Net;
using System.Text.Json;
using HooviePack.Api.Configuration;
using HooviePack.Api.Domain;
using Microsoft.Extensions.Options;

namespace HooviePack.Api.Infrastructure.DogApi;

public sealed record CatalogBreed(DogipediaBreed Breed, Guid? ExternalGroupId,
    IReadOnlyList<DogipediaBreedImage> Images);

public interface IDogApiClient
{
    Task<IReadOnlyList<DogipediaBreedGroup>> GetAllGroupsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CatalogBreed>> GetAllBreedsAsync(CancellationToken cancellationToken);
}

public sealed class DogApiClient(HttpClient http, IOptions<DogApiOptions> options,
    ILogger<DogApiClient> logger) : IDogApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DogipediaBreedGroup>> GetAllGroupsAsync(CancellationToken cancellationToken) =>
        (await GetCollectionAsync<DogApiGroupAttributes>("groups", "group", cancellationToken))
        .Select(x => new DogipediaBreedGroup { ExternalGroupId = x.Id, Name = x.Attributes!.Name ?? "" }).ToList();

    public async Task<IReadOnlyList<CatalogBreed>> GetAllBreedsAsync(CancellationToken cancellationToken) =>
        (await GetCollectionAsync<DogApiBreedAttributes>("breeds", "breed", cancellationToken)).Select(MapBreed).ToList();

    private async Task<List<DogApiResource<T>>> GetCollectionAsync<T>(string endpoint, string resourceType,
        CancellationToken cancellationToken)
    {
        var all = new List<DogApiResource<T>>();
        var ids = new HashSet<Guid>();
        int? expectedCount = null;
        int? expectedLast = null;
        var page = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug("Fetching Dog API {Endpoint} page {Page}", endpoint, page);
            try
            {
                using var response = await GetWithRetryAsync(
                    $"{endpoint}?page[number]={page}&page[size]={options.Value.PageSize}", cancellationToken);
                var result = await response.Content.ReadFromJsonAsync<DogApiCollectionResponse<T>>(JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("Null collection response.");
                var pagination = result.Meta?.Pagination ?? throw new InvalidDataException("Missing pagination metadata.");
                if (result.Data is null || result.Data.Count == 0 || pagination.Current != page ||
                    pagination.Records < 0 || pagination.Last < page)
                    throw new InvalidDataException("Empty collection or inconsistent pagination.");
                if (expectedCount.HasValue && pagination.Records != expectedCount ||
                    expectedLast.HasValue && pagination.Last != expectedLast)
                    throw new InvalidDataException("Catalog changed during pagination.");
                expectedCount ??= pagination.Records;
                expectedLast ??= pagination.Last;
                foreach (var resource in result.Data)
                {
                    if (resource.Id == Guid.Empty || !ids.Add(resource.Id) || resource.Attributes is null || resource.Type != resourceType)
                        throw new InvalidDataException("Missing or duplicate resource ID, attributes, or invalid resource type.");
                    all.Add(resource);
                }
                if (pagination.Next is not null)
                {
                    if (pagination.Next != page + 1 || pagination.Last == page)
                        throw new InvalidDataException("Invalid next page.");
                    page++;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(result.Links?.Next) || pagination.Last > page ||
                    expectedCount.HasValue && all.Count != expectedCount)
                    throw new InvalidDataException("Incomplete collection.");
                return all;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidDataException($"Dog API {endpoint} page {page} failed: {ex.Message}", ex);
            }
        }
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt) + Random.Shared.Next(100));
            try
            {
                var response = await http.GetAsync(path, cancellationToken);
                if (response.IsSuccessStatusCode) return response;
                var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500;
                if (!transient || attempt >= 2)
                {
                    using (response) response.EnsureSuccessStatusCode();
                }
                var retryAfter = response.Headers.RetryAfter;
                var requestedDelay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow);
                if (requestedDelay > delay) delay = requestedDelay.Value;
                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < 2 && ex.StatusCode is null) { }
            catch (OperationCanceledException) when (attempt < 2 && !cancellationToken.IsCancellationRequested) { }
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static CatalogBreed MapBreed(DogApiResource<DogApiBreedAttributes> resource)
    {
        var a = resource.Attributes!;
        var b = new DogipediaBreed
        {
            ExternalBreedId = resource.Id, Name = a.Name ?? "", Description = a.Description,
            Hypoallergenic = a.Hypoallergenic, LifeMinYears = a.Life?.Min, LifeMaxYears = a.Life?.Max,
            MaleWeightMinKg = a.MaleWeight?.Min, MaleWeightMaxKg = a.MaleWeight?.Max,
            FemaleWeightMinKg = a.FemaleWeight?.Min, FemaleWeightMaxKg = a.FemaleWeight?.Max,
            MaleHeightMinCm = a.MaleHeight?.Min, MaleHeightMaxCm = a.MaleHeight?.Max,
            FemaleHeightMinCm = a.FemaleHeight?.Min, FemaleHeightMaxCm = a.FemaleHeight?.Max,
            OriginCountry = a.Origin?.Country, OriginRegion = a.Origin?.Region, OriginEra = a.Origin?.Era,
            CoatType = a.Coat?.Type, CoatLength = a.Coat?.Length, CoatColors = a.Coat?.Colors ?? [],
            OtherNames = a.OtherNames ?? [], RecognizedBy = a.RecognizedBy ?? [],
            Sources = JsonSerializer.Serialize(a.Sources ?? [], JsonOptions),
            Temperament = a.Traits?.Temperament ?? [], Energy = a.Traits?.Energy,
            Trainability = a.Traits?.Trainability, Barking = a.Traits?.Barking, Grooming = a.Traits?.Grooming,
            Shedding = a.Traits?.Shedding, Drooling = a.Traits?.Drooling,
            GoodWithChildren = a.Traits?.GoodWithChildren, GoodWithDogs = a.Traits?.GoodWithDogs,
            GoodWithStrangers = a.Traits?.GoodWithStrangers, ApartmentFriendly = a.Traits?.ApartmentFriendly,
            ExerciseMinutes = a.Traits?.ExerciseMinutes
        };
        var images = (a.Images ?? []).Select((i, order) => new DogipediaBreedImage
        {
            ExternalImageId = i.Id, OriginalUrl = i.Url, ThumbUrl = i.Thumb, MediumUrl = i.Medium,
            LargeUrl = i.Large, Author = i.Attribution?.Author, License = i.Attribution?.License,
            LicenseUrl = i.Attribution?.LicenseUrl, Source = i.Attribution?.Source,
            SourceUrl = i.Attribution?.SourceUrl, SortOrder = order
        }).ToList();
        var group = resource.Relationships?.Group?.Data;
        if (group is not null && (group.Id == Guid.Empty || group.Type != "group"))
            throw new InvalidDataException($"Breed {resource.Id} has an invalid group relationship.");
        return new(b, group?.Id, images);
    }
}
