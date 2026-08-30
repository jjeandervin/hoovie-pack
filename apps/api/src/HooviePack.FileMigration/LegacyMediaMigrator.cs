using System.Security.Cryptography;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Files.Api.Application;
using HooviePack.Files.Api.Configuration;
using HooviePack.Files.Api.Domain;
using HooviePack.Files.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.FileMigration;

public sealed record LegacyReference(
    string StoragePath,
    string OriginalFileName,
    string ContentType,
    DateTimeOffset CreatedAt,
    bool NeedsMigration);

public sealed record LegacyObjectMetadata(long Size, string ContentType);

public interface ILegacyObjectStorage
{
    Task<LegacyObjectMetadata?> GetMetadataAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task PutAsync(
        string storageKey,
        string contentType,
        Stream input,
        CancellationToken cancellationToken = default);
}

public sealed class LegacyMediaMigrator(
    AppDbContext applicationDb,
    FilesDbContext filesDb,
    ILegacyObjectStorage objectStorage,
    FileStorageOptions storageOptions,
    string legacyRoot,
    TextWriter output)
{
    private readonly string _rootPath = Path.GetFullPath(legacyRoot);
    private readonly string _rootPrefix = Path.GetFullPath(legacyRoot)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    public async Task<int> RunAsync(
        bool dryRun,
        bool requireComplete = false,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootPath))
        {
            await output.WriteLineAsync($"Legacy media root does not exist: {_rootPath}");
            return 1;
        }

        var references = await LoadReferencesAsync(cancellationToken);
        var grouped = references
            .GroupBy(x => NormalizeRelativePath(x.StoragePath), StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToList();
        var referencedPaths = grouped.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var sourceFiles = Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories)
            .Where(path => !IsStagingPath(path))
            .Select(path => Path.GetRelativePath(_rootPath, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        var unreferenced = sourceFiles.Except(referencedPaths, StringComparer.Ordinal).Order().ToList();

        await output.WriteLineAsync(
            $"Inventory: {references.Count} database reference(s), {grouped.Count} unique legacy object(s), " +
            $"{sourceFiles.Count} source file(s), {unreferenced.Count} unreferenced source file(s).");
        foreach (var path in unreferenced)
        {
            await output.WriteLineAsync($"UNREFERENCED {path}");
        }

        var failures = 0;
        var migrated = 0;
        var alreadyMigrated = 0;
        foreach (var group in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!group.Any(x => x.NeedsMigration))
            {
                alreadyMigrated++;
                continue;
            }

            try
            {
                ValidateReferenceMetadata(group);
                var sourcePath = ResolveSourcePath(group.Key);
                if (!File.Exists(sourcePath))
                {
                    throw new InvalidOperationException("The referenced source file is missing.");
                }

                var source = group.First();
                if (!storageOptions.AllowedContentTypes.Contains(
                        source.ContentType.Trim(),
                        StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The legacy content type is not allowed by File Storage.");
                }

                var size = new FileInfo(sourcePath).Length;
                if (size <= 0 || size > storageOptions.MaxFileBytes)
                {
                    throw new InvalidOperationException(
                        $"The source size {size} is outside the configured 1..{storageOptions.MaxFileBytes} byte range.");
                }

                if (dryRun)
                {
                    await output.WriteLineAsync($"WOULD-MIGRATE {group.Key} ({size} bytes)");
                    continue;
                }

                var record = await GetOrCreateMetadataAsync(
                    group.Key,
                    source,
                    size,
                    cancellationToken);
                await UploadAndVerifyAsync(record, sourcePath, cancellationToken);
                await BackfillReferencesAsync(group, record.Id, cancellationToken);
                migrated++;
                await output.WriteLineAsync($"MIGRATED {group.Key} -> {record.Id:D}");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures++;
                await output.WriteLineAsync($"FAILED {group.Key}: {Sanitize(exception.Message)}");
            }
        }

        var outstanding = grouped.Count(x => x.Any(reference => reference.NeedsMigration));
        await output.WriteLineAsync(
            dryRun
                ? $"Dry run complete: {outstanding} object(s) require migration."
                : $"Migration complete: {migrated} migrated, {alreadyMigrated} already migrated, {failures} failed.");
        if (failures > 0)
        {
            return 1;
        }

        return requireComplete && outstanding > 0 ? 2 : 0;
    }

    private async Task<List<LegacyReference>> LoadReferencesAsync(CancellationToken cancellationToken)
    {
        var references = new List<LegacyReference>();
        var users = await applicationDb.Users.AsNoTracking()
            .Where(x => x.AvatarStoragePath != null)
            .Select(x => new
            {
                StoragePath = x.AvatarStoragePath!,
                ContentType = x.AvatarContentType ?? "application/octet-stream",
                x.CreatedAt,
                NeedsMigration = x.AvatarFileId == null
            })
            .ToListAsync(cancellationToken);
        references.AddRange(users.Select(x => new LegacyReference(
            x.StoragePath,
            "avatar" + Path.GetExtension(x.StoragePath),
            x.ContentType,
            x.CreatedAt,
            x.NeedsMigration)));

        var dogs = await applicationDb.DogProfiles.AsNoTracking()
            .Where(x => x.PhotoStoragePath != null)
            .Select(x => new
            {
                StoragePath = x.PhotoStoragePath!,
                ContentType = x.PhotoContentType ?? "application/octet-stream",
                x.CreatedAt,
                NeedsMigration = x.PhotoFileId == null
            })
            .ToListAsync(cancellationToken);
        references.AddRange(dogs.Select(x => new LegacyReference(
            x.StoragePath,
            "dog-photo" + Path.GetExtension(x.StoragePath),
            x.ContentType,
            x.CreatedAt,
            x.NeedsMigration)));

        references.AddRange(await applicationDb.PostPhotos.AsNoTracking()
            .Where(x => x.StoragePath != null)
            .Select(x => new LegacyReference(
                x.StoragePath!,
                x.OriginalFileName,
                x.ContentType,
                x.CreatedAt,
                x.FileId == null))
            .ToListAsync(cancellationToken));
        return references;
    }

    private async Task<FileRecord> GetOrCreateMetadataAsync(
        string legacyPath,
        LegacyReference source,
        long size,
        CancellationToken cancellationToken)
    {
        var record = await filesDb.Files.SingleOrDefaultAsync(
            x => x.LegacySourcePath == legacyPath,
            cancellationToken);
        if (record is null)
        {
            var fileId = Guid.CreateVersion7();
            record = new FileRecord
            {
                Id = fileId,
                StorageKey = FileManager.BuildStorageKey(storageOptions.KeyPrefix, fileId),
                OriginalFileName = SanitizeFileName(source.OriginalFileName),
                ContentType = source.ContentType.Trim().ToLowerInvariant(),
                DeclaredSize = size,
                UploadTokenHash = SHA256.HashData(RandomNumberGenerator.GetBytes(32)),
                LegacySourcePath = legacyPath,
                CreatedAt = source.CreatedAt
            };
            filesDb.Files.Add(record);
            await filesDb.SaveChangesAsync(cancellationToken);
        }
        else if (record.DeclaredSize != size ||
                 !string.Equals(record.ContentType, source.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The source changed after migration metadata was recorded; restore the original or review manually.");
        }

        return record;
    }

    private async Task UploadAndVerifyAsync(
        FileRecord record,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var existing = await objectStorage.GetMetadataAsync(record.StorageKey, cancellationToken);
        if (existing is null ||
            existing.Size != record.DeclaredSize ||
            !string.Equals(existing.ContentType, record.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await objectStorage.PutAsync(record.StorageKey, record.ContentType, stream, cancellationToken);
        }

        var verified = await objectStorage.GetMetadataAsync(record.StorageKey, cancellationToken)
            ?? throw new InvalidOperationException("S3 did not return the uploaded object during verification.");
        if (verified.Size != record.DeclaredSize ||
            !string.Equals(verified.ContentType, record.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The S3 object metadata does not match the legacy source.");
        }

        if (record.Status != FileStatus.Ready || record.ActualSize != verified.Size)
        {
            record.Status = FileStatus.Ready;
            record.ActualSize = verified.Size;
            record.UploadedAt ??= DateTimeOffset.UtcNow;
            await filesDb.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task BackfillReferencesAsync(
        IGrouping<string, LegacyReference> references,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var storagePaths = references.Select(x => x.StoragePath).Distinct(StringComparer.Ordinal).ToList();
        var users = await applicationDb.Users
            .Where(x => x.AvatarStoragePath != null &&
                        storagePaths.Contains(x.AvatarStoragePath) &&
                        x.AvatarFileId == null)
            .ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            user.AvatarFileId = fileId;
        }

        var dogs = await applicationDb.DogProfiles
            .Where(x => x.PhotoStoragePath != null &&
                        storagePaths.Contains(x.PhotoStoragePath) &&
                        x.PhotoFileId == null)
            .ToListAsync(cancellationToken);
        foreach (var dog in dogs)
        {
            dog.PhotoFileId = fileId;
        }

        var photos = await applicationDb.PostPhotos
            .Where(x => x.StoragePath != null &&
                        storagePaths.Contains(x.StoragePath) &&
                        x.FileId == null)
            .ToListAsync(cancellationToken);
        foreach (var photo in photos)
        {
            photo.FileId = fileId;
        }

        await applicationDb.SaveChangesAsync(cancellationToken);
    }

    private string ResolveSourcePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("The legacy path must be relative.");
        }

        var resolved = Path.GetFullPath(
            Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The legacy path escapes the configured media root.");
        }

        return resolved;
    }

    private static void ValidateReferenceMetadata(IGrouping<string, LegacyReference> references)
    {
        if (references.Count() != 1)
        {
            throw new InvalidOperationException(
                "Multiple domain records reference the same legacy object; resolve the ownership ambiguity before migration.");
        }

        var contentTypes = references.Select(x => x.ContentType.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (contentTypes.Count != 1)
        {
            throw new InvalidOperationException("Database references disagree about the legacy object's content type.");
        }
    }

    private static string NormalizeRelativePath(string path) =>
        path.Trim().Replace('\\', '/').TrimStart('/');

    private static string SanitizeFileName(string fileName)
    {
        var safe = fileName.Replace('\\', '/').Split('/').LastOrDefault()?.Trim() ?? "legacy-file";
        safe = new string(safe.Select(character => char.IsControl(character) ? '_' : character).ToArray());
        return safe.Length switch
        {
            0 => "legacy-file",
            > 255 => safe[..255],
            _ => safe
        };
    }

    private bool IsStagingPath(string absolutePath)
    {
        var relative = Path.GetRelativePath(_rootPath, absolutePath).Replace('\\', '/');
        return relative.StartsWith(".staging/", StringComparison.Ordinal);
    }

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
