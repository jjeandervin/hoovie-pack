using HooviePack.Api.Configuration;
using Microsoft.Extensions.Options;

namespace HooviePack.Api.Infrastructure.Storage;

public sealed record StoredImage(
    string StoragePath,
    string OriginalFileName,
    string ContentType,
    int Width,
    int Height);

public sealed record StoredFile(Stream Stream, string ContentType, long Length);

public sealed class InvalidMediaException(string message) : Exception(message);

public interface IFileStorage
{
    long MaxImageBytes { get; }
    Task<StoredImage> StoreImageAsync(
        Stream input,
        string originalFileName,
        string category,
        CancellationToken cancellationToken = default);
    Task<StoredFile?> OpenReadAsync(
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(string? storagePath, CancellationToken cancellationToken = default);
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(
        IOptions<MediaStorageOptions> options,
        IWebHostEnvironment environment,
        ILogger<LocalFileStorage> logger)
    {
        _logger = logger;
        MaxImageBytes = options.Value.MaxImageBytes;
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath));
        _rootPrefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_rootPath);
    }

    public long MaxImageBytes { get; }

    public async Task<StoredImage> StoreImageAsync(
        Stream input,
        string originalFileName,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (category.Length is < 1 or > 40 || category.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Invalid storage category.", nameof(category));
        }

        var stagingDirectory = ResolvePath(".staging");
        Directory.CreateDirectory(stagingDirectory);
        var stagingPath = Path.Combine(stagingDirectory, $"{Guid.CreateVersion7():N}.upload");
        var sanitizedPath = Path.Combine(stagingDirectory, $"{Guid.CreateVersion7():N}.sanitized");

        try
        {
            await using (var output = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                long totalBytes = 0;
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > MaxImageBytes)
                    {
                        throw new InvalidMediaException($"Images must be no larger than {MaxImageBytes / (1024 * 1024)} MB.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                if (totalBytes == 0)
                {
                    throw new InvalidMediaException("The uploaded image is empty.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var info = await ImageFileInspector.ReencodeWithoutMetadataAsync(
                stagingPath,
                sanitizedPath,
                MaxImageBytes,
                cancellationToken);
            if (new FileInfo(sanitizedPath).Length > MaxImageBytes)
            {
                throw new InvalidMediaException(
                    $"Images must be no larger than {MaxImageBytes / (1024 * 1024)} MB after processing.");
            }

            var extension = info.Extension;
            var now = DateTime.UtcNow;
            var relativePath = Path.Combine(
                category,
                now.ToString("yyyy"),
                now.ToString("MM"),
                $"{Guid.CreateVersion7():N}{extension}");
            var destinationPath = ResolvePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(sanitizedPath, destinationPath);

            var safeOriginalName = Path.GetFileName(originalFileName.Trim());
            if (safeOriginalName.Length == 0)
            {
                safeOriginalName = $"image{extension}";
            }
            else if (safeOriginalName.Length > 255)
            {
                safeOriginalName = safeOriginalName[..255];
            }

            return new StoredImage(
                relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                safeOriginalName,
                info.ContentType,
                info.Width,
                info.Height);
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                try
                {
                    File.Delete(stagingPath);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Could not remove media staging file {StagingPath}.", stagingPath);
                }
            }

            if (File.Exists(sanitizedPath))
            {
                try
                {
                    File.Delete(sanitizedPath);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Could not remove sanitized media staging file {StagingPath}.", sanitizedPath);
                }
            }
        }
    }

    public Task<StoredFile?> OpenReadAsync(
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var absolutePath = ResolvePath(storagePath);
        if (!File.Exists(absolutePath))
        {
            return Task.FromResult<StoredFile?>(null);
        }

        var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<StoredFile?>(new StoredFile(stream, contentType, stream.Length));
    }

    public Task DeleteAsync(string? storagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return Task.CompletedTask;
        }

        var absolutePath = ResolvePath(storagePath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Storage paths must be relative.");
        }

        var resolved = Path.GetFullPath(Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage path escapes the configured media root.");
        }

        return resolved;
    }
}
