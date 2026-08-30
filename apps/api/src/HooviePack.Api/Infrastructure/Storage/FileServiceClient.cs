using System.Net;
using System.Net.Http.Json;
using HooviePack.Api.Application;
using HooviePack.Api.Configuration;
using HooviePack.Files.Domain;
using Microsoft.Extensions.Options;

namespace HooviePack.Api.Infrastructure.Storage;

public sealed class FileServiceFileNotFoundException : Exception
{
}

public sealed class FileServiceRejectedRequestException : Exception
{
}

public interface IFileServiceClient
{
    long MaxImageBytes { get; }

    Task<UploadResponse> CreateUploadAsync(
        CreateUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<FileMetadataResponse> CompleteUploadAsync(
        Guid fileId,
        string uploadToken,
        CancellationToken cancellationToken = default);

    Task<DownloadResponse> GetDownloadAsync(
        Guid fileId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default);
}

public sealed class FileServiceClient(
    HttpClient httpClient,
    IOptions<MediaStorageOptions> mediaOptions) : IFileServiceClient
{
    private const string UnavailableMessage = "File storage is temporarily unavailable.";

    public long MaxImageBytes { get; } = mediaOptions.Value.MaxImageBytes;

    public async Task<UploadResponse> CreateUploadAsync(
        CreateUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => httpClient.PostAsJsonAsync("files/uploads", request, cancellationToken),
            cancellationToken);
        await EnsureSuccessAsync(response, allowNotFound: false, cancellationToken);
        var upload = await ReadRequiredAsync<UploadResponse>(response, cancellationToken);
        if (upload.FileId == Guid.Empty ||
            string.IsNullOrWhiteSpace(upload.UploadUrl) ||
            string.IsNullOrWhiteSpace(upload.UploadToken) ||
            upload.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw ApiException.ServiceUnavailable(UnavailableMessage);
        }

        return upload with
        {
            RequiredHeaders = upload.RequiredHeaders ?? new Dictionary<string, string>()
        };
    }

    public async Task<FileMetadataResponse> CompleteUploadAsync(
        Guid fileId,
        string uploadToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => httpClient.PostAsJsonAsync(
                $"files/{fileId:D}/complete",
                new CompleteUploadRequest { UploadToken = uploadToken },
                cancellationToken),
            cancellationToken);
        await EnsureSuccessAsync(response, allowNotFound: true, cancellationToken);
        var file = await ReadRequiredAsync<FileMetadataResponse>(response, cancellationToken);
        if (file.FileId != fileId ||
            string.IsNullOrWhiteSpace(file.OriginalFileName) ||
            string.IsNullOrWhiteSpace(file.ContentType) ||
            file.Size <= 0)
        {
            throw ApiException.ServiceUnavailable(UnavailableMessage);
        }

        return file;
    }

    public async Task<DownloadResponse> GetDownloadAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => httpClient.GetAsync($"files/{fileId:D}/download", cancellationToken),
            cancellationToken);
        await EnsureSuccessAsync(response, allowNotFound: true, cancellationToken);
        var download = await ReadRequiredAsync<DownloadResponse>(response, cancellationToken);
        if (download.FileId != fileId ||
            string.IsNullOrWhiteSpace(download.DownloadUrl) ||
            download.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw ApiException.ServiceUnavailable(UnavailableMessage);
        }

        return download;
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => httpClient.DeleteAsync($"files/{fileId:D}", cancellationToken),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, allowNotFound: false, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ApiException.ServiceUnavailable(UnavailableMessage);
        }
        catch (HttpRequestException)
        {
            throw ApiException.ServiceUnavailable(UnavailableMessage);
        }
    }

    private static Task EnsureSuccessAsync(
        HttpResponseMessage response,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileServiceFileNotFoundException();
        }

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            throw new FileServiceRejectedRequestException();
        }

        throw ApiException.ServiceUnavailable(UnavailableMessage);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw ApiException.ServiceUnavailable(UnavailableMessage);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        {
            throw ApiException.ServiceUnavailable(UnavailableMessage);
        }
    }
}
