using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.FileMigration;
using HooviePack.Files.Api.Configuration;
using HooviePack.Files.Api.Domain;
using HooviePack.Files.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.FileMigration.Tests;

public sealed class LegacyMediaMigratorTests
{
    [Fact]
    public async Task Migration_is_resumable_preserves_source_and_backfills_file_id()
    {
        var root = CreateTempDirectory();
        try
        {
            var relativePath = "avatars/2026/08/legacy.jpg";
            var sourcePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
            await using var applicationDb = CreateApplicationDb();
            await using var filesDb = CreateFilesDb();
            var user = new AppUser
            {
                AuthProviderUserId = "subject",
                Email = "owner@example.test",
                DisplayName = "Owner",
                AvatarStoragePath = relativePath,
                AvatarContentType = "image/jpeg"
            };
            applicationDb.Users.Add(user);
            await applicationDb.SaveChangesAsync();
            var objects = new FakeLegacyObjectStorage();
            var output = new StringWriter();
            var migrator = CreateMigrator(applicationDb, filesDb, objects, root, output);

            Assert.Equal(0, await migrator.RunAsync(dryRun: false));

            var record = await filesDb.Files.SingleAsync();
            Assert.Equal(FileStatus.Ready, record.Status);
            Assert.Equal(relativePath, record.LegacySourcePath);
            Assert.Equal(4, record.ActualSize);
            Assert.Equal(record.Id, user.AvatarFileId);
            Assert.Equal(relativePath, user.AvatarStoragePath);
            Assert.True(File.Exists(sourcePath));
            Assert.Equal(1, objects.PutCalls);

            Assert.Equal(0, await migrator.RunAsync(dryRun: false));
            Assert.Single(await filesDb.Files.ToListAsync());
            Assert.Equal(1, objects.PutCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dry_run_inventories_without_mutating_database_or_object_store()
    {
        var root = CreateTempDirectory();
        try
        {
            var relativePath = "dogs/2026/08/legacy.png";
            var sourcePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(root, "unreferenced.webp"), [4]);
            await using var applicationDb = CreateApplicationDb();
            await using var filesDb = CreateFilesDb();
            applicationDb.DogProfiles.Add(new DogProfile
            {
                FamilyId = Guid.CreateVersion7(),
                Name = "Hoovie",
                CreatedByUserId = Guid.CreateVersion7(),
                PhotoStoragePath = relativePath,
                PhotoContentType = "image/png"
            });
            await applicationDb.SaveChangesAsync();
            var objects = new FakeLegacyObjectStorage();
            var output = new StringWriter();
            var migrator = CreateMigrator(applicationDb, filesDb, objects, root, output);

            Assert.Equal(0, await migrator.RunAsync(dryRun: true));

            Assert.Empty(await filesDb.Files.ToListAsync());
            Assert.Equal(0, objects.PutCalls);
            Assert.Contains("WOULD-MIGRATE dogs/2026/08/legacy.png", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("UNREFERENCED unreferenced.webp", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Completion_check_is_nonzero_while_legacy_references_remain()
    {
        var root = CreateTempDirectory();
        try
        {
            var relativePath = "avatars/legacy.jpg";
            var sourcePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            await File.WriteAllBytesAsync(sourcePath, [1]);
            await using var applicationDb = CreateApplicationDb();
            await using var filesDb = CreateFilesDb();
            applicationDb.Users.Add(new AppUser
            {
                AuthProviderUserId = "subject",
                Email = "owner@example.test",
                DisplayName = "Owner",
                AvatarStoragePath = relativePath,
                AvatarContentType = "image/jpeg"
            });
            await applicationDb.SaveChangesAsync();
            var migrator = CreateMigrator(
                applicationDb,
                filesDb,
                new FakeLegacyObjectStorage(),
                root,
                new StringWriter());

            Assert.Equal(2, await migrator.RunAsync(dryRun: true, requireComplete: true));
            Assert.Empty(await filesDb.Files.ToListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_referenced_file_fails_without_abandoning_legacy_reference()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var applicationDb = CreateApplicationDb();
            await using var filesDb = CreateFilesDb();
            var user = new AppUser
            {
                AuthProviderUserId = "subject",
                Email = "owner@example.test",
                DisplayName = "Owner",
                AvatarStoragePath = "avatars/missing.jpg",
                AvatarContentType = "image/jpeg"
            };
            applicationDb.Users.Add(user);
            await applicationDb.SaveChangesAsync();
            var output = new StringWriter();
            var migrator = CreateMigrator(
                applicationDb,
                filesDb,
                new FakeLegacyObjectStorage(),
                root,
                output);

            Assert.Equal(1, await migrator.RunAsync(dryRun: false));

            Assert.Null(user.AvatarFileId);
            Assert.Equal("avatars/missing.jpg", user.AvatarStoragePath);
            Assert.Empty(await filesDb.Files.ToListAsync());
            Assert.Contains("FAILED avatars/missing.jpg", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static LegacyMediaMigrator CreateMigrator(
        AppDbContext applicationDb,
        FilesDbContext filesDb,
        FakeLegacyObjectStorage objects,
        string root,
        TextWriter output) => new(
        applicationDb,
        filesDb,
        objects,
        new FileStorageOptions
        {
            BucketName = "private-bucket",
            Region = "us-east-1",
            KeyPrefix = "files",
            MaxFileBytes = 10 * 1024 * 1024
        },
        root,
        output);

    private static AppDbContext CreateApplicationDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FilesDbContext CreateFilesDb() => new(
        new DbContextOptionsBuilder<FilesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hooviepack-file-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeLegacyObjectStorage : ILegacyObjectStorage
    {
        private readonly Dictionary<string, LegacyObjectMetadata> _objects = new(StringComparer.Ordinal);
        public int PutCalls { get; private set; }

        public Task<LegacyObjectMetadata?> GetMetadataAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_objects.GetValueOrDefault(storageKey));

        public Task PutAsync(
            string storageKey,
            string contentType,
            Stream input,
            CancellationToken cancellationToken = default)
        {
            PutCalls++;
            _objects[storageKey] = new LegacyObjectMetadata(input.Length, contentType);
            return Task.CompletedTask;
        }
    }
}
