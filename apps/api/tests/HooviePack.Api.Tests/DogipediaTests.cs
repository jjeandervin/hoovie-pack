using System.Data.Common;
using HooviePack.Api.Application;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Configuration;
using HooviePack.Api.Controllers;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.DogApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HooviePack.Api.Tests;

public sealed class DogipediaTests
{
    [Fact]
    public void Search_text_normalizes_case_whitespace_duplicates_and_blank_names() =>
        Assert.Equal("pembroke corgi welsh corgi", DogipediaCatalogSyncService.NormalizeSearchText(
            " Pembroke   CORGI ", [null, "", " Welsh\tCorgi ", "WELSH CORGI", "Pembroke corgi"]));

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("//example.com", null)]
    [InlineData("data:text/html,hello", null)]
    [InlineData("https://example.com/photo", "https://example.com/photo")]
    public void Only_http_urls_are_exposed(string input, string? expected) => Assert.Equal(expected, DogipediaBreedService.SafeUrl(input));

    [Theory]
    [InlineData(2, false)]
    [InlineData(25, true)]
    [InlineData(-1, true)]
    public async Task Worker_checks_freshness_and_resolves_scoped_sync_service(int ageHours, bool expectedSync)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        // Use shared options so each scope sees the same test database.
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        services.AddScoped(_ => new AppDbContext(dbOptions));
        var sync = new CountingSync();
        services.AddSingleton<IDogipediaCatalogSyncService>(sync);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DogipediaSyncStates.Add(new DogipediaSyncState { LastSuccessfulSyncAtUtc = ageHours < 0 ? null : DateTimeOffset.UtcNow.AddHours(-ageHours) });
            await db.SaveChangesAsync();
        }
        var worker = new DogipediaSyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DogipediaSyncOptions()), NullLogger<DogipediaSyncBackgroundService>.Instance);
        await worker.CheckAsync(default);
        Assert.Equal(expectedSync ? 1 : 0, sync.Calls);
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        worker.Dispose();
    }

    [PostgresFact]
    public Task Sync_upserts_stable_ids_relationships_images_and_soft_deactivations() => WithDatabase(async (connection, db) =>
    {
        var client = new CatalogClient();
        var sync = Sync(db, client);
        await sync.SyncAsync();
        var original = await db.DogipediaBreeds.AsNoTracking().SingleAsync();
        var originalImage = await db.DogipediaBreedImages.AsNoTracking().SingleAsync();
        await sync.SyncAsync();
        Assert.Equal(1, await db.DogipediaBreeds.CountAsync());
        Assert.Equal(1, await db.DogipediaBreedImages.CountAsync());
        Assert.Equal(original.Id, (await db.DogipediaBreeds.SingleAsync()).Id);
        var newGroup = new DogipediaBreedGroup { ExternalGroupId = Guid.NewGuid(), Name = "Working" };
        client.Groups.Add(newGroup);
        client.Breeds[0].Breed.Description = "Updated story";
        client.Breeds[0].Images[0].Author = "Updated author";
        client.Breeds[0] = client.Breeds[0] with { ExternalGroupId = newGroup.ExternalGroupId };
        await sync.SyncAsync();
        var updated = await db.DogipediaBreeds.AsNoTracking().Include(x => x.BreedGroup).SingleAsync();
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal("Updated story", updated.Description);
        Assert.Equal("Working", updated.BreedGroup!.Name);
        var updatedImage = await db.DogipediaBreedImages.AsNoTracking().SingleAsync();
        Assert.Equal(originalImage.Id, updatedImage.Id);
        Assert.Equal("Updated author", updatedImage.Author);
        client.Breeds.Add(new(new DogipediaBreed { ExternalBreedId = Guid.NewGuid(), Name = "Akita" }, null, []));
        await sync.SyncAsync();
        Assert.Equal(2, await db.DogipediaBreeds.CountAsync());
        client.Breeds.RemoveAt(0);
        client.Groups.RemoveAt(0);
        await sync.SyncAsync();
        Assert.False((await db.DogipediaBreeds.SingleAsync(x => x.Id == original.Id)).IsActive);
        Assert.False((await db.DogipediaBreedImages.SingleAsync()).IsActive);
        Assert.Equal(1, await db.DogipediaBreedGroups.CountAsync(x => x.IsActive));
        Assert.Equal(2, await db.DogipediaBreeds.CountAsync());
        var state = await db.DogipediaSyncStates.SingleAsync();
        Assert.NotNull(state.LastSuccessfulSyncAtUtc);
        Assert.Equal(1, state.LastSuccessfulBreedCount);
        Assert.Equal(0, state.LastSuccessfulImageCount);
    });

    [PostgresFact]
    public Task Gallery_returns_only_this_breeds_active_images_in_stable_order_with_individual_credits() => WithDatabase(async (_, db) =>
    {
        var breed = new DogipediaBreed { ExternalBreedId = Guid.NewGuid(), Name = "Gallery breed", IsActive = true };
        var other = new DogipediaBreed { ExternalBreedId = Guid.NewGuid(), Name = "Other breed", IsActive = true };
        DogipediaBreedImage Photo(DogipediaBreed owner, int order, string author, bool active = true) => new()
        {
            ExternalImageId = Guid.NewGuid(), Breed = owner, BreedId = owner.Id, IsActive = active, SortOrder = order,
            Author = author, MediumUrl = $"https://example.com/{author}.jpg", License = $"License {author}",
            LicenseUrl = $"https://example.com/license/{author}", Source = "Wikimedia Commons",
            SourceUrl = $"https://example.com/source/{author}", OriginalUrl = "https://example.com/original.jpg"
        };
        var later = Photo(breed, 9, "Later");
        var primary = Photo(breed, 2, "Primary");
        var sameOrder = Photo(breed, 2, "SameOrder");
        var inactive = Photo(breed, 0, "Inactive", false);
        var foreign = Photo(other, 0, "Foreign");
        db.DogipediaBreeds.AddRange(breed, other);
        db.DogipediaBreedImages.AddRange(later, sameOrder, inactive, foreign, primary);
        await db.SaveChangesAsync();
        var service = new DogipediaBreedService(db);
        var detail = await service.GetAsync(breed.Id, default);
        var expected = new[] { primary, sameOrder, later }.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        Assert.Equal(expected.Select(i => i.Id), detail.Images.Select(i => i.Id));
        Assert.Equal(detail.Images[0], detail.PrimaryImage);
        Assert.Equal(detail.PrimaryImage, detail.Image); // Phase 1 compatibility
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Author, detail.Images[index].Author);
            Assert.Equal(expected[index].License, detail.Images[index].License);
            Assert.Equal(expected[index].LicenseUrl, detail.Images[index].LicenseUrl);
            Assert.Equal(expected[index].SourceUrl, detail.Images[index].SourceUrl);
            Assert.NotEqual(expected[index].ExternalImageId, detail.Images[index].Id);
        }
        foreach (var photo in new[] { primary, sameOrder, later }) photo.IsActive = false;
        await db.SaveChangesAsync();
        var empty = await service.GetAsync(breed.Id, default);
        Assert.Null(empty.PrimaryImage);
        Assert.Empty(empty.Images);
        Assert.Empty(empty.RelatedBreeds); // Two ungrouped breeds do not become related.
    });

    [PostgresFact]
    public Task Related_breeds_are_active_same_group_six_alphabetical_cards_without_n_plus_one_queries() => WithDatabase(async (connection, db) =>
    {
        var group = new DogipediaBreedGroup { ExternalGroupId = Guid.NewGuid(), Name = "Herding", IsActive = true };
        var otherGroup = new DogipediaBreedGroup { ExternalGroupId = Guid.NewGuid(), Name = "Sporting", IsActive = true };
        DogipediaBreed Breed(string name, DogipediaBreedGroup? owner, bool active = true) => new()
        { ExternalBreedId = Guid.NewGuid(), Name = name, BreedGroup = owner, BreedGroupId = owner?.Id, IsActive = active };
        var current = Breed("A current breed", group);
        var related = new[] { "Zulu", "Echo", "Delta", "Bravo", "Golf", "Foxtrot", "Charlie", "Alpha" }
            .Select(name => Breed(name, group)).ToList();
        var inactive = Breed("A inactive breed", group, false);
        var different = Breed("A sporting breed", otherGroup);
        var ungrouped = Breed("A ungrouped breed", null);
        db.DogipediaBreeds.AddRange([current, inactive, different, ungrouped, .. related]);
        var alpha = related.Single(b => b.Name == "Alpha");
        db.DogipediaBreedImages.AddRange(
            new DogipediaBreedImage { ExternalImageId = Guid.NewGuid(), Breed = alpha, IsActive = false, SortOrder = 0, Author = "Inactive" },
            new DogipediaBreedImage { ExternalImageId = Guid.NewGuid(), Breed = alpha, IsActive = true, SortOrder = 3, Author = "Later" },
            new DogipediaBreedImage { ExternalImageId = Guid.NewGuid(), Breed = alpha, IsActive = true, SortOrder = 1, Author = "Primary",
                MediumUrl = "https://example.com/alpha.jpg", LicenseUrl = "javascript:alert(1)" });
        await db.SaveChangesAsync();
        var counter = new QueryCounter();
        await using var queryDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(counter).Options);
        var service = new DogipediaBreedService(queryDb);
        var detail = await service.GetAsync(current.Id, default);
        Assert.Equal(3, counter.Count); // Breed/group, gallery projection, related cards projection.
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot" }, detail.RelatedBreeds.Select(b => b.Name));
        Assert.All(detail.RelatedBreeds, b => Assert.Equal("Herding", b.GroupName));
        Assert.DoesNotContain(detail.RelatedBreeds, b => b.Id == current.Id || b.Id == inactive.Id || b.Id == different.Id || b.Id == ungrouped.Id);
        Assert.Equal("Primary", detail.RelatedBreeds[0].Image!.Author);
        Assert.Equal("https://example.com/alpha.jpg", detail.RelatedBreeds[0].Image!.MediumUrl);
        Assert.Null(detail.RelatedBreeds[0].Image!.LicenseUrl);
        Assert.Null(detail.RelatedBreeds[1].Image);
        Assert.Equal(detail.RelatedBreeds, (await service.GetAsync(current.Id, default)).RelatedBreeds);
        Assert.Empty((await service.GetAsync(different.Id, default)).RelatedBreeds);
        foreach (var b in related.Skip(2)) b.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(2, (await service.GetAsync(current.Id, default)).RelatedBreeds.Count);
    });

    [PostgresFact]
    public Task Image_order_removal_and_reactivation_preserve_the_primary_image_contract() => WithDatabase(async (_, db) =>
    {
        var client = new CatalogClient();
        var first = client.Breeds[0].Images[0];
        var second = new DogipediaBreedImage { ExternalImageId = Guid.NewGuid(), Author = "Second", SortOrder = 1 };
        client.Breeds[0] = client.Breeds[0] with { Images = [second, first] };
        await Sync(db, client).SyncAsync();
        var breed = await db.DogipediaBreeds.AsNoTracking().SingleAsync();
        var service = new DogipediaBreedService(db);
        Assert.Equal("Jane", (await service.GetAsync(breed.Id, default)).Image!.Author);
        client.Breeds[0] = client.Breeds[0] with { Images = [second] };
        await Sync(db, client).SyncAsync();
        Assert.Equal("Second", (await service.GetAsync(breed.Id, default)).Image!.Author);
        Assert.False((await db.DogipediaBreedImages.SingleAsync(x => x.ExternalImageId == first.ExternalImageId)).IsActive);
        client.Breeds[0] = client.Breeds[0] with { Images = [] };
        await Sync(db, client).SyncAsync();
        Assert.Null((await service.GetAsync(breed.Id, default)).Image);
        client.Breeds[0] = client.Breeds[0] with { Images = [first] };
        await Sync(db, client).SyncAsync();
        Assert.Equal(2, await db.DogipediaBreedImages.CountAsync());
        Assert.Equal("Jane", (await service.GetAsync(breed.Id, default)).Image!.Author);
    });

    [PostgresFact]
    public Task Download_validation_and_persistence_failures_preserve_previous_catalog() => WithDatabase(async (connection, db) =>
    {
        var client = new CatalogClient();
        await Sync(db, client).SyncAsync();
        var successful = (await db.DogipediaSyncStates.AsNoTracking().SingleAsync()).LastSuccessfulSyncAtUtc;
        client.Failure = new InvalidDataException("Dog API breeds page 2 failed");
        await Assert.ThrowsAsync<InvalidDataException>(() => Sync(db, client).SyncAsync());
        Assert.True((await db.DogipediaBreeds.AsNoTracking().SingleAsync()).IsActive);
        Assert.Equal(successful, (await db.DogipediaSyncStates.AsNoTracking().SingleAsync()).LastSuccessfulSyncAtUtc);
        client.Failure = null;
        var original = client.Breeds[0];
        client.Breeds.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => Sync(db, client).SyncAsync());
        client.Breeds.Add(original with { ExternalGroupId = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidDataException>(() => Sync(db, client).SyncAsync());
        client.Breeds[0] = original;
        client.Breeds.Add(original);
        await Assert.ThrowsAsync<InvalidDataException>(() => Sync(db, client).SyncAsync());
        client.Breeds.RemoveAt(1);
        original.Breed.Description = "Must roll back";
        var interceptor = new FailAfterSave();
        await using var failingDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection)
            .AddInterceptors(interceptor).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sync(failingDb, client).SyncAsync());
        Assert.Equal("A loyal companion", (await db.DogipediaBreeds.AsNoTracking().SingleAsync()).Description);
        var state = await db.DogipediaSyncStates.AsNoTracking().SingleAsync();
        Assert.Equal(successful, state.LastSuccessfulSyncAtUtc);
        Assert.Equal(5, state.ConsecutiveFailures);
        Assert.NotNull(state.LastFailureAtUtc);
    });

    [PostgresFact]
    public Task Query_searches_before_paging_ranks_names_and_returns_local_details() => WithDatabase(async (_, db) =>
    {
        var client = new CatalogClient();
        client.Breeds.AddRange(new[] { "Corgi", "Corgi Mix", "Cardigan Welsh Corgi", "Akita", "Zed" }.Select(name =>
            new CatalogBreed(new DogipediaBreed { ExternalBreedId = Guid.NewGuid(), Name = name, OtherNames = name == "Zed" ? ["Corgi friend"] : [] }, null, [])));
        await Sync(db, client).SyncAsync();
        db.DogipediaBreeds.Add(new DogipediaBreed { ExternalBreedId = Guid.NewGuid(), Name = "Inactive Corgi", SearchText = "corgi", IsActive = false });
        await db.SaveChangesAsync();
        var service = new DogipediaBreedService(db);
        var all = await service.ListAsync(null, 1, 2, default);
        Assert.Equal(new[] { "Akita", "Cardigan Welsh Corgi" }, all.Items.Select(x => x.Name));
        Assert.Equal(6, all.TotalItems);
        Assert.Equal(3, all.TotalPages);
        Assert.False(all.HasPreviousPage);
        Assert.True(all.HasNextPage);
        foreach (var search in new[] { "corgi", "Corgi", "CORGI", " corgi " })
        {
            var result = await service.ListAsync(search, 1, 2, default);
            Assert.Equal(5, result.TotalItems);
            Assert.Equal(new[] { "Corgi", "Corgi Mix" }, result.Items.Select(x => x.Name));
            var second = await service.ListAsync(search, 2, 2, default);
            Assert.Equal(new[] { "Cardigan Welsh Corgi", "Pembroke Welsh Corgi" }, second.Items.Select(x => x.Name));
        }
        Assert.Equal(1, (await service.ListAsync("pembroke", 1, 24, default)).TotalItems);
        Assert.Equal(2, (await service.ListAsync("welsh corgi", 1, 24, default)).TotalItems);
        Assert.Equal(1, (await service.ListAsync("little herder", 1, 24, default)).TotalItems);
        Assert.Equal(0, (await service.ListAsync("%", 1, 24, default)).TotalItems);
        Assert.Empty((await service.ListAsync("no match", 1, 24, default)).Items);
        Assert.Equal(6, (await service.ListAsync("  ", 1, 24, default)).TotalItems);
        await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(new string('a', 101), 1, 24, default));
        await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(null, 0, 24, default));
        await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(null, 1, 101, default));
        await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(null, int.MaxValue, 100, default));
        var b = await db.DogipediaBreeds.SingleAsync(x => x.Name == "Pembroke Welsh Corgi");
        var detail = await service.GetAsync(b.Id, default);
        Assert.Equal("Herding", detail.Group!.Name);
        Assert.Equal("Jane", detail.Image!.Author);
        Assert.Equal("https://example.com/license", detail.Image.LicenseUrl);
        Assert.Null(detail.Image.SourceUrl);
        Assert.Null(detail.MaleHeight.MinCm);
        Assert.Null((await service.GetAsync(all.Items[0].Id, default)).Image);
        var missing = await Assert.ThrowsAsync<ApiException>(() => service.GetAsync(Guid.NewGuid(), default));
        Assert.Equal(404, missing.StatusCode);
        b.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => service.GetAsync(b.Id, default))).StatusCode);
        // The client now fails, but queries still use only PostgreSQL.
        client.Failure = new HttpRequestException("offline");
        Assert.True(await service.IsAvailableAsync(default));
        Assert.NotEmpty((await service.ListAsync(null, 1, 24, default)).Items);
    });

    [PostgresFact]
    public Task Empty_catalog_returns_recognizable_503_and_failed_initial_sync_remains_unavailable() => WithDatabase(async (_, db) =>
    {
        var controller = new DogipediaBreedsController(new DogipediaBreedService(db));
        var response = await controller.List(default);
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(response.Result).Value);
        Assert.Equal(503, problem.Status);
        Assert.Equal("dogipedia_catalog_unavailable", problem.Extensions["code"]);
        await Assert.ThrowsAsync<HttpRequestException>(() => Sync(db, new CatalogClient { Failure = new HttpRequestException("offline") }).SyncAsync());
        Assert.False(await new DogipediaBreedService(db).IsAvailableAsync(default));
        Assert.Null((await db.DogipediaSyncStates.SingleAsync()).LastSuccessfulSyncAtUtc);
    });

    [PostgresFact]
    public Task Lock_contention_and_freshness_recheck_skip_without_upstream_calls() => WithDatabase(async (connection, db) =>
    {
        var client = new CatalogClient();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        var syncLock = new DogipediaSyncLock(configuration);
        await using (var held = await syncLock.TryAcquireAsync(default))
        {
            Assert.NotNull(held);
            await Sync(db, client, syncLock).SyncAsync();
            Assert.Equal(0, client.Calls);
        }
        await Sync(db, client, syncLock).SyncAsync();
        Assert.Equal(2, client.Calls);
        await Sync(db, client, syncLock).SyncAsync(false);
        Assert.Equal(2, client.Calls);
        Assert.Equal(0, (await db.DogipediaSyncStates.SingleAsync()).ConsecutiveFailures);
        await using var reacquired = await syncLock.TryAcquireAsync(default);
        Assert.NotNull(reacquired);
    });

    private static DogipediaCatalogSyncService Sync(AppDbContext db, CatalogClient client, IDogipediaSyncLock? syncLock = null) =>
        new(db, client, syncLock ?? new NoContentionLock(), Options.Create(new DogipediaSyncOptions()), NullLogger<DogipediaCatalogSyncService>.Instance);

    private static async Task WithDatabase(Func<string, AppDbContext, Task> action)
    {
        var connection = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)!;
        var schema = $"hp_dogipedia_test_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        var isolated = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(isolated, x => x.EnableRetryOnFailure()).Options);
            await db.Database.MigrateAsync();
            await action(isolated, db);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class CatalogClient : IDogApiClient
    {
        public List<DogipediaBreedGroup> Groups { get; } = [new() { ExternalGroupId = Guid.NewGuid(), Name = "Herding" }];
        public List<CatalogBreed> Breeds { get; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }
        public CatalogClient() => Breeds = [new(new DogipediaBreed
        {
            ExternalBreedId = Guid.NewGuid(), Name = "Pembroke Welsh Corgi", Description = "A loyal companion",
            OtherNames = ["Little Herder"], LifeMinYears = 12, LifeMaxYears = 13
        }, Groups[0].ExternalGroupId, [new DogipediaBreedImage
        {
            ExternalImageId = Guid.NewGuid(), MediumUrl = "https://example.com/photo", Author = "Jane",
            License = "CC BY-SA", LicenseUrl = "https://example.com/license", SourceUrl = "javascript:alert(1)"
        }])];
        public Task<IReadOnlyList<DogipediaBreedGroup>> GetAllGroupsAsync(CancellationToken cancellationToken)
        { Calls++; return Task.FromResult<IReadOnlyList<DogipediaBreedGroup>>(Groups); }
        public Task<IReadOnlyList<CatalogBreed>> GetAllBreedsAsync(CancellationToken cancellationToken)
        { Calls++; if (Failure is not null) throw Failure; return Task.FromResult<IReadOnlyList<CatalogBreed>>(Breeds); }
    }
    private sealed class NoContentionLock : IDogipediaSyncLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) => Task.FromResult<IAsyncDisposable?>(new Lease());
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class CountingSync : IDogipediaCatalogSyncService
    {
        public int Calls { get; private set; }
        public Task SyncAsync(bool force = true, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        private bool failed;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!failed) { failed = true; throw new InvalidOperationException("Simulated failure after SQL persistence, before commit"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Count++; return ValueTask.FromResult(result); }
    }
}
