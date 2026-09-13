using System.Diagnostics;
using System.Text.RegularExpressions;
using HooviePack.Api.Configuration;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.DogApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HooviePack.Api.Application.Services;

public interface IDogipediaCatalogSyncService
{
    Task SyncAsync(bool force = true, CancellationToken cancellationToken = default);
}

public sealed class DogipediaCatalogSyncService(AppDbContext db, IDogApiClient client,
    IDogipediaSyncLock syncLock, IOptions<DogipediaSyncOptions> options,
    ILogger<DogipediaCatalogSyncService> logger) : IDogipediaCatalogSyncService
{
    public async Task SyncAsync(bool force = true, CancellationToken cancellationToken = default)
    {
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        { ["Component"] = "Dogipedia", ["Operation"] = "CatalogSync" });
        await using var lease = await syncLock.TryAcquireAsync(cancellationToken);
        if (lease is null)
        {
            logger.LogInformation("Catalog sync {Status}: another instance owns the lock", "Skipped");
            return;
        }
        // AsNoTracking deliberately re-reads the timestamp after acquiring the lock.
        var previous = await db.DogipediaSyncStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Key == DogipediaSyncState.CatalogKey, cancellationToken);
        if (!force && IsFresh(previous?.LastSuccessfulSyncAtUtc, DateTimeOffset.UtcNow, options.Value.FreshnessHours))
        {
            logger.LogDebug("Catalog sync {Status}: catalog is fresh after acquiring the lock", "Skipped");
            return;
        }
        var attemptAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Catalog sync {Status} at {AttemptAtUtc}", "Started", attemptAt);
        try
        {
            var groups = await client.GetAllGroupsAsync(cancellationToken);
            var breeds = await client.GetAllBreedsAsync(cancellationToken);
            ValidateCatalog(groups, breeds);
            // EF's configured retry strategy must wrap the whole explicit transaction.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var now = DateTimeOffset.UtcNow;
                var localGroups = await db.DogipediaBreedGroups.ToDictionaryAsync(x => x.ExternalGroupId, cancellationToken);
                var localBreeds = await db.DogipediaBreeds.ToDictionaryAsync(x => x.ExternalBreedId, cancellationToken);
                var localImages = await db.DogipediaBreedImages.ToDictionaryAsync(x => x.ExternalImageId, cancellationToken);
                var groupIds = groups.Select(x => x.ExternalGroupId).ToHashSet();
                var breedIds = breeds.Select(x => x.Breed.ExternalBreedId).ToHashSet();
                var imageIds = breeds.SelectMany(x => x.Images).Select(x => x.ExternalImageId).ToHashSet();
                var breedsInserted = breedIds.Count(x => !localBreeds.ContainsKey(x));
                var groupsInserted = groupIds.Count(x => !localGroups.ContainsKey(x));
                var imagesInserted = imageIds.Count(x => !localImages.ContainsKey(x));
                var breedsDeactivated = localBreeds.Values.Count(x => x.IsActive && !breedIds.Contains(x.ExternalBreedId));
                var groupsDeactivated = localGroups.Values.Count(x => x.IsActive && !groupIds.Contains(x.ExternalGroupId));
                var imagesDeactivated = localImages.Values.Count(x => x.IsActive && !imageIds.Contains(x.ExternalImageId));
                foreach (var local in localGroups.Values) local.IsActive = groupIds.Contains(local.ExternalGroupId);
                foreach (var local in localBreeds.Values) local.IsActive = breedIds.Contains(local.ExternalBreedId);
                foreach (var local in localImages.Values) local.IsActive = imageIds.Contains(local.ExternalImageId);

                foreach (var incoming in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!localGroups.TryGetValue(incoming.ExternalGroupId, out var local))
                    {
                        local = new DogipediaBreedGroup { ExternalGroupId = incoming.ExternalGroupId, CreatedAtUtc = now };
                        db.DogipediaBreedGroups.Add(local);
                        localGroups.Add(local.ExternalGroupId, local);
                    }
                    local.Name = incoming.Name.Trim();
                    local.IsActive = true;
                    local.LastSyncedAtUtc = now;
                }
                foreach (var incoming in breeds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!localBreeds.TryGetValue(incoming.Breed.ExternalBreedId, out var local))
                    {
                        local = new DogipediaBreed { ExternalBreedId = incoming.Breed.ExternalBreedId, CreatedAtUtc = now };
                        db.DogipediaBreeds.Add(local);
                    }
                    CopyImportedValues(incoming.Breed, local, nameof(DogipediaBreed.CreatedAtUtc));
                    local.Name = local.Name.Trim();
                    local.SearchText = NormalizeSearchText(local.Name, local.OtherNames);
                    local.BreedGroupId = incoming.ExternalGroupId is Guid groupId ? localGroups[groupId].Id : null;
                    local.IsActive = true;
                    local.LastSyncedAtUtc = now;
                    foreach (var image in incoming.Images)
                    {
                        if (!localImages.TryGetValue(image.ExternalImageId, out var localImage))
                        {
                            localImage = new DogipediaBreedImage();
                            db.DogipediaBreedImages.Add(localImage);
                        }
                        CopyImportedValues(image, localImage);
                        localImage.BreedId = local.Id;
                        localImage.IsActive = true;
                        localImage.LastSyncedAtUtc = now;
                    }
                }
                var state = await GetStateAsync(cancellationToken);
                state.LastAttemptAtUtc = attemptAt;
                state.LastSuccessfulSyncAtUtc = now;
                state.LastSuccessfulBreedCount = breeds.Count;
                state.LastSuccessfulGroupCount = groups.Count;
                state.LastSuccessfulImageCount = imageIds.Count;
                state.ConsecutiveFailures = 0;
                state.LastError = null;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Catalog sync {Status}: BreedCount={BreedCount} GroupCount={GroupCount} ImageCount={ImageCount} " +
                    "BreedsInserted={BreedsInserted} BreedsUpdated={BreedsUpdated} BreedsDeactivated={BreedsDeactivated} " +
                    "GroupsInserted={GroupsInserted} GroupsUpdated={GroupsUpdated} GroupsDeactivated={GroupsDeactivated} " +
                    "ImagesInserted={ImagesInserted} ImagesUpdated={ImagesUpdated} ImagesDeactivated={ImagesDeactivated} DurationMs={DurationMs}",
                    "Completed", breeds.Count, groups.Count, imageIds.Count, breedsInserted, breeds.Count - breedsInserted,
                    breedsDeactivated, groupsInserted, groups.Count - groupsInserted, groupsDeactivated,
                    imagesInserted, imageIds.Count - imagesInserted, imagesDeactivated, stopwatch.ElapsedMilliseconds);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog sync {Status} at {AttemptAtUtc}, DurationMs={DurationMs}", "Failed", attemptAt, stopwatch.ElapsedMilliseconds);
            try
            {
                // Discard rolled-back tracked changes before recording failure separately.
                db.ChangeTracker.Clear();
                var state = await GetStateAsync(cancellationToken);
                state.LastAttemptAtUtc = attemptAt;
                state.LastFailureAtUtc = DateTimeOffset.UtcNow;
                state.LastError = ex.Message[..Math.Min(ex.Message.Length, 2000)];
                state.ConsecutiveFailures++;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception stateError) when (!cancellationToken.IsCancellationRequested)
            { logger.LogError(stateError, "Could not persist catalog sync failure state"); }
            throw;
        }
    }

    private void CopyImportedValues<T>(T source, T target, params string[] preserved) where T : Entity
    {
        var entry = db.Entry(target);
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (name == nameof(Entity.Id) || preserved.Contains(name)) continue;
            property.CurrentValue = property.Metadata.PropertyInfo!.GetValue(source);
        }
    }

    private async Task<DogipediaSyncState> GetStateAsync(CancellationToken cancellationToken)
    {
        var state = await db.DogipediaSyncStates.SingleOrDefaultAsync(x => x.Key == DogipediaSyncState.CatalogKey, cancellationToken);
        if (state is not null) return state;
        state = new DogipediaSyncState();
        db.DogipediaSyncStates.Add(state);
        return state;
    }

    public static bool IsFresh(DateTimeOffset? lastSuccess, DateTimeOffset now, double freshnessHours) =>
        lastSuccess >= now.AddHours(-freshnessHours);

    public static string NormalizeSearchText(string name, IEnumerable<string?> otherNames) =>
        string.Join(' ', otherNames.Prepend(name).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Regex.Replace(x!.Trim().ToLowerInvariant(), @"\s+", " ")).Distinct());

    private static void ValidateCatalog(IReadOnlyList<DogipediaBreedGroup> groups, IReadOnlyList<CatalogBreed> breeds)
    {
        if (groups.Count == 0 || breeds.Count == 0) throw new InvalidDataException("The upstream catalog is empty.");
        var groupIds = new HashSet<Guid>();
        foreach (var group in groups)
            if (group.ExternalGroupId == Guid.Empty || !groupIds.Add(group.ExternalGroupId) ||
                string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 300)
                throw new InvalidDataException("Invalid or duplicate group.");
        var breedIds = new HashSet<Guid>();
        var imageIds = new HashSet<Guid>();
        foreach (var item in breeds)
        {
            if (item.Breed.ExternalBreedId == Guid.Empty || !breedIds.Add(item.Breed.ExternalBreedId) ||
                string.IsNullOrWhiteSpace(item.Breed.Name) || item.Breed.Name.Length > 300 ||
                item.ExternalGroupId is Guid groupId && !groupIds.Contains(groupId))
                throw new InvalidDataException("Invalid breed, duplicate ID, or unresolved group.");
            foreach (var image in item.Images)
                if (image.ExternalImageId == Guid.Empty || !imageIds.Add(image.ExternalImageId))
                    throw new InvalidDataException("Invalid or duplicate image ID.");
        }
    }
}
