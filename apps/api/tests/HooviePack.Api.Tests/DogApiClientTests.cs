using System.Net;
using System.Text;
using System.Text.Json;
using HooviePack.Api.Configuration;
using HooviePack.Api.Infrastructure.DogApi;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HooviePack.Api.Tests;

public sealed class DogApiClientTests
{
    private static readonly Guid BreedId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();

    [Fact]
    public async Task Maps_full_breed_and_group_collections_without_leaking_upstream_models()
    {
        var handler = new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("groups")
            ? Collection([new { id = GroupId, type = "group", attributes = new { name = "Herding" } }], 1, null, 1)
            : Collection([Breed(BreedId)], 1, null, 1))));
        var client = Client(handler);
        var group = Assert.Single(await client.GetAllGroupsAsync(default));
        var breed = Assert.Single(await client.GetAllBreedsAsync(default));
        Assert.Equal(GroupId, group.ExternalGroupId);
        Assert.Equal("Herding", group.Name);
        Assert.Equal(GroupId, breed.ExternalGroupId);
        Assert.Equal(10m, breed.Breed.MaleWeightMinKg);
        Assert.Equal(30m, breed.Breed.FemaleHeightMaxCm);
        Assert.Equal("Wales", breed.Breed.OriginCountry);
        Assert.Equal(4, breed.Breed.GoodWithChildren);
        Assert.Equal(["Welsh Corgi"], breed.Breed.OtherNames);
        Assert.Equal("Jane", Assert.Single(breed.Images).Author);
        Assert.Contains("page[size]=1000", Uri.UnescapeDataString(handler.Paths[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Combines_pages_and_stops_at_last_for_both_collections(bool groups)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(Collection(
            groups ? [new { id = Guid.NewGuid(), type = "group", attributes = new { name = "Group" } }] : [Breed(Guid.NewGuid())],
            1, 2, 2))));
        handler.Respond = (_, _) => Task.FromResult(Json(Collection(
            groups ? [new { id = Guid.NewGuid(), type = "group", attributes = new { name = "Group" } }] : [Breed(Guid.NewGuid())],
            handler.Paths.Count, handler.Paths.Count == 1 ? 2 : null, 2)));
        var client = Client(handler);
        var count = groups ? (await client.GetAllGroupsAsync(default)).Count : (await client.GetAllBreedsAsync(default)).Count;
        Assert.Equal(2, count);
        Assert.Equal(2, handler.Paths.Count);
        Assert.Contains("page[number]=2", Uri.UnescapeDataString(handler.Paths[1]));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("http")]
    [InlineData("duplicate")]
    [InlineData("count")]
    [InlineData("empty")]
    [InlineData("missing-meta")]
    public async Task Rejects_failed_or_incomplete_later_page(string failure)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("{}")));
        handler.Respond = (_, _) => Task.FromResult(handler.Paths.Count == 1
            ? Json(Collection([Breed(BreedId)], 1, 2, 2))
            : failure switch
            {
                "malformed" => Json("{"),
                "http" => new HttpResponseMessage(HttpStatusCode.BadRequest),
                "duplicate" => Json(Collection([Breed(BreedId)], 2, null, 2)),
                "count" => Json(Collection([Breed(Guid.NewGuid())], 2, null, 3)),
                "empty" => Json(Collection([], 2, null, 2)),
                _ => Json(JsonSerializer.Serialize(new { data = new[] { Breed(Guid.NewGuid()) } }))
            });
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(handler).GetAllBreedsAsync(default));
        Assert.Equal(2, handler.Paths.Count);
    }

    [Fact]
    public async Task Retries_transient_errors_but_not_permanent_errors()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(handler).GetAllGroupsAsync(default));
        Assert.Equal(3, handler.Paths.Count);
        handler.Paths.Clear();
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(handler).GetAllGroupsAsync(default));
        Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task Cancellation_interrupts_retry_after_delay()
    {
        var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(5));
            return Task.FromResult(response);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).GetAllBreedsAsync(cts.Token));
        Assert.Single(handler.Paths);
    }

    private static object Breed(Guid id) => new
    {
        id, type = "breed", attributes = new
        {
            name = "Corgi", male_weight = new { min = 10, max = 14 }, female_height = new { min = 25, max = 30 },
            origin = new { country = "Wales" }, traits = new { good_with_children = 4 }, other_names = new[] { "Welsh Corgi" },
            images = new[] { new { id = Guid.NewGuid(), medium = "https://images.example/corgi.webp", attribution = new { author = "Jane", license_url = "https://example.com/license" } } }
        }, relationships = new { group = new { data = new { id = GroupId, type = "group" } } }
    };
    private static string Collection(object[] data, int current, int? next, int records) =>
        JsonSerializer.Serialize(new { data, meta = new { pagination = new { current, next, records } } });
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static DogApiClient Client(Handler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://dog.example/api/v2/") },
        Options.Create(new DogApiOptions()), NullLogger<DogApiClient>.Instance);
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Paths.Add(request.RequestUri!.ToString()); return Respond(request, cancellationToken); }
    }
}
