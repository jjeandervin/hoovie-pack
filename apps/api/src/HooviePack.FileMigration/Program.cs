using Amazon.S3;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.FileMigration;
using HooviePack.Files.Api.Configuration;
using HooviePack.Files.Api.Infrastructure.Data;
using HooviePack.Files.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();
var connectionString = configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings:DefaultConnection is required.");
    return 1;
}

var storageOptions = configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>()
    ?? new FileStorageOptions();
if (string.IsNullOrWhiteSpace(storageOptions.BucketName) || string.IsNullOrWhiteSpace(storageOptions.Region))
{
    Console.Error.WriteLine("FileStorage:BucketName and FileStorage:Region are required.");
    return 1;
}

var legacyOptions = configuration.GetSection(LegacyMediaOptions.SectionName).Get<LegacyMediaOptions>()
    ?? new LegacyMediaOptions();
var requireComplete = args.Any(argument => string.Equals(argument, "--check", StringComparison.OrdinalIgnoreCase));
var dryRun = requireComplete || args.Any(argument =>
    string.Equals(argument, "--dry-run", StringComparison.OrdinalIgnoreCase));
var appOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString)
    .Options;
var filesOptions = new DbContextOptionsBuilder<FilesDbContext>()
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "files"))
    .Options;

await using var applicationDb = new AppDbContext(appOptions);
await using var filesDb = new FilesDbContext(filesOptions);
using IAmazonS3 s3 = S3ObjectStorage.CreateClient(storageOptions);
var migrator = new LegacyMediaMigrator(
    applicationDb,
    filesDb,
    new S3LegacyObjectStorage(s3, storageOptions),
    storageOptions,
    legacyOptions.RootPath,
    Console.Out);
return await migrator.RunAsync(dryRun, requireComplete);
