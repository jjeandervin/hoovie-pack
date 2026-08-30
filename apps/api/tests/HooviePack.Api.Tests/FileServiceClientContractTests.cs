using System.Net;
using System.Net.Http.Json;
using HooviePack.Api.Application;
using HooviePack.Api.Configuration;
using HooviePack.Api.Infrastructure.Storage;
using HooviePack.Files.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HooviePack.Api.Tests;

public sealed class FileServiceClientContractTests
{
    [Fact]
    public async Task Client_uses_metadata_only_internal_contracts()
    {
        var fileId = Guid.CreateVersion7();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var requests = new List<CapturedRequest>();
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            return (request.Method.Method, request.RequestUri.AbsolutePath) switch
            {
                ("POST", "/files/uploads") => Json(new UploadResponse(
                    fileId,
                    "https://s3.example.test/upload",
                    expiresAt,
                    new Dictionary<string, string> { ["Content-Type"] = "image/png" },
                    "upload-token")),
                ("POST", var path) when path == $"/files/{fileId:D}/complete" => Json(new FileMetadataResponse(
                    fileId,
                    "hoovie.png",
                    "image/png",
                    1234,
                    DateTimeOffset.UtcNow)),
                ("GET", var path) when path == $"/files/{fileId:D}/download" => Json(new DownloadResponse(
                    fileId,
                    "https://s3.example.test/download",
                    expiresAt)),
                ("DELETE", var path) when path == $"/files/{fileId:D}" => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files-api:8080/") };
        var client = new FileServiceClient(httpClient, Options.Create(new MediaStorageOptions()));

        var upload = await client.CreateUploadAsync(new CreateUploadRequest("hoovie.png", "image/png", 1234));
        var completed = await client.CompleteUploadAsync(fileId, upload.UploadToken);
        var download = await client.GetDownloadAsync(fileId);
        await client.DeleteAsync(fileId);

        Assert.Equal(fileId, upload.FileId);
        Assert.Equal(fileId, completed.FileId);
        Assert.Equal("https://s3.example.test/download", download.DownloadUrl);
        Assert.Equal(4, requests.Count);
        Assert.Equal("application/json", requests[0].ContentType);
        Assert.Contains("\"fileName\":\"hoovie.png\"", requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"contentType\":\"image/png\"", requests[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("data:", requests[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("application/json", requests[1].ContentType);
        Assert.Contains("\"uploadToken\":\"upload-token\"", requests[1].Body, StringComparison.Ordinal);
        Assert.Null(requests[2].Body);
        Assert.Null(requests[3].Body);
    }

    [Fact]
    public async Task Internal_failure_is_sanitized_as_service_unavailable()
    {
        var handler = new StubHandler((_request, _cancellationToken) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("bucket=private-bucket; secret=do-not-expose")
            }));
        var client = new FileServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://files-api:8080/") },
            Options.Create(new MediaStorageOptions()));

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.CreateUploadAsync(
            new CreateUploadRequest("hoovie.jpg", "image/jpeg", 100)));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal("File storage is temporarily unavailable.", exception.Detail);
        Assert.DoesNotContain("bucket", exception.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Network_failure_is_sanitized_as_service_unavailable()
    {
        var handler = new StubHandler((_request, _cancellationToken) =>
            throw new HttpRequestException("AWS credential details should not escape"));
        var client = new FileServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://files-api:8080/") },
            Options.Create(new MediaStorageOptions()));

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.GetDownloadAsync(Guid.CreateVersion7()));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal("File storage is temporarily unavailable.", exception.Detail);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(value)
    };

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string? ContentType,
        string? Body);

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }
}
