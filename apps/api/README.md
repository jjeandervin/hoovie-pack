# HooviePack backend services

This directory contains two .NET 10 controller-based services:

- `HooviePack.Api` owns authentication, family/domain authorization, and application APIs used by Angular.
- `HooviePack.Files.Api` owns stable `FileId` metadata, private S3 keys, presigned PUT/GET URLs, and object deletion.
- `HooviePack.Files.Domain` is the dependency-free contract library referenced by both APIs so their internal request/response models cannot drift.

The services share PostgreSQL while using separate EF Core contexts and schema history. Only the main API is an application endpoint. The File Service is called over the internal Docker network with `X-Internal-Api-Key`; Angular is never configured with its URL or AWS credentials.

## Local commands

From `apps/api`:

```powershell
dotnet tool restore
dotnet build HooviePack.slnx
dotnet test HooviePack.slnx
dotnet tool run dotnet-ef -- database update --project src/HooviePack.Api/HooviePack.Api.csproj --startup-project src/HooviePack.Api/HooviePack.Api.csproj --context AppDbContext
dotnet tool run dotnet-ef -- database update --project src/HooviePack.Files.Api/HooviePack.Files.Api.csproj --startup-project src/HooviePack.Files.Api/HooviePack.Files.Api.csproj --context FilesDbContext
dotnet run --project src/HooviePack.Files.Api/HooviePack.Files.Api.csproj
dotnet run --project src/HooviePack.Api/HooviePack.Api.csproj
```

In Development, the main API Swagger UI is at `http://localhost:5103/swagger` and its document is at `http://localhost:5103/swagger/v1/openapi.json`. Use Swagger's **Authorize** control with a Keycloak access token. Compose publishes the File Service on loopback port 5001 only so a host-run main API can reach it; production publishes no File Service port.

## Main API configuration

Configuration uses standard ASP.NET Core environment-variable mapping. `appsettings.Development.json` contains a plainly labeled development-only database password for a directly launched local database. Compose and deployments inject their own connection strings and secrets.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | shared PostgreSQL connection string |
| `Authentication__Authority` | public token issuer |
| `Authentication__MetadataAddress` | optional Docker-internal discovery URL |
| `Authentication__ValidIssuer` | optional public issuer when discovery uses an internal address |
| `Authentication__Audience` | required API audience, normally `hooviepack-api` |
| `Authentication__RequireHttpsMetadata` | keep true outside local development; only loopback or the Docker-internal Keycloak host may use HTTP metadata |
| `FileService__BaseUrl` | internal File Service base URL |
| `FileService__ApiKey` | shared internal request credential |
| `FileService__TimeoutSeconds` | internal HTTP timeout |
| `MediaStorage__MaxImageBytes` | domain-side declared/completed image limit |
| `Database__ApplyMigrations` | development-only startup migration switch |
| `Cors__AllowedOrigins__0` | first allowed browser origin |

## File Service configuration

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | shared PostgreSQL connection; file tables/history use the `files` schema |
| `InternalApi__ApiKey` | expected `X-Internal-Api-Key` value |
| `Database__ApplyMigrations` | development-only startup migration switch |
| `FileStorage__BucketName` | private S3 bucket |
| `FileStorage__Region` | AWS region |
| `FileStorage__KeyPrefix` | internal object-key prefix, normally `files` |
| `FileStorage__UploadUrlLifetimeMinutes` | presigned PUT lifetime |
| `FileStorage__DownloadUrlLifetimeMinutes` | presigned GET lifetime |
| `FileStorage__MaxFileBytes` | declared file-size limit |
| `FileStorage__ServiceUrl` | optional SDK-compatible development endpoint |
| `FileStorage__ForcePathStyle` | optional path-style addressing for the development endpoint |

The AWS SDK receives `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, and optional `AWS_SESSION_TOKEN` only inside the File Service container and the explicitly invoked legacy importer. Compose sources them from distinctly named `FILES_AWS_*` host variables to avoid reusing unrelated Route 53 credentials. The bucket must remain private and the identity should have only `s3:GetObject`, `s3:PutObject`, and `s3:DeleteObject` on the configured bucket prefix. Prefer workload-role credentials when available.

## Health and migrations

Each API exposes liveness at `/health/live` and readiness at `/health/ready`. Readiness checks process/configuration and PostgreSQL; it does not upload an object or require broad bucket-list permissions. Normal file operations report S3 failures through the existing safe API error handling.

Production forces `Database__ApplyMigrations=false` in both long-running services. Schema updates run explicitly through the profile-gated `db-migrations` (`AppDbContext`) and `files-db-migrations` (`FilesDbContext`) services in the repository-root `compose.prod.yaml`. The deployment wrapper runs both successfully before replacing any application container. See the root README for failure handling and the separate existing-media migration gate.

`HooviePack.FileMigration` is the separate idempotent legacy importer. The `legacy-media-migration` Compose service mounts `media_data` read-only at `LegacyMedia__RootPath=/legacy-media`, inventories with `--dry-run`, then imports only when explicitly run without `--dry-run` or `--check`. It records `LegacySourcePath` in File Service metadata, verifies uploaded S3 metadata, and backfills domain `FileId` references; it never deletes the source. It is not part of normal startup. `deploy-prod.sh` uses only its non-mutating `--check` mode as a fail-closed cutover gate. Follow the root README's maintenance-window and verification procedure before first production cutover.

## File behavior

The main API retains all Keycloak and family authorization. It requests upload/download capabilities from the internal File Service and returns stable `FileId` values plus short-lived URLs as needed. After the direct PUT, the one-time upload token lets the API complete the upload; the File Service compares S3 size/content type with the recorded declaration before a domain association is committed. The browser transfers bytes directly to/from S3; no backend or Nginx endpoint streams normal file content. Raw S3 keys and presigned URLs are never stored as domain identifiers.

Initialization validates basic file name, content type, and declared size. The refactor intentionally performs no image resizing, thumbnail generation, re-encoding, optimization, EXIF processing, transcoding, CDN, or event processing. A failed/expired upload or missing S3 object is handled as an expected storage failure without disclosing keys, credentials, signatures, or internal stack traces.

The normal test run uses mocked AWS interactions and requires no real AWS credentials. Set `HOOVIEPACK_TEST_POSTGRES` to a disposable PostgreSQL connection string to also verify PostgreSQL migrations and behavior; each database test must isolate and remove its own schema.
