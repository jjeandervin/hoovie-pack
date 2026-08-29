# HooviePack API

.NET 10 controller-based Web API for HooviePack. The API validates Keycloak bearer tokens, uses PostgreSQL through EF Core, and keeps all family data and uploaded media behind membership checks.

## Local commands

From `apps/api`:

```powershell
dotnet tool restore
dotnet build HooviePack.slnx
dotnet test HooviePack.slnx
dotnet tool run dotnet-ef -- database update --project src/HooviePack.Api/HooviePack.Api.csproj --startup-project src/HooviePack.Api/HooviePack.Api.csproj --context AppDbContext
dotnet run --project src/HooviePack.Api/HooviePack.Api.csproj
```

In Development, Swagger UI is at `http://localhost:5103/swagger` and the generated document is at `http://localhost:5103/swagger/v1/openapi.json`. Use Swagger's **Authorize** control with a Keycloak access token.

## Configuration

Configuration uses standard ASP.NET Core environment-variable mapping:

`appsettings.Development.json` contains a plainly labeled development-only password for a directly launched local database. Compose and every deployment inject their own connection string. No database credential is present in base configuration.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Authentication__Authority` | public token issuer |
| `Authentication__MetadataAddress` | optional Docker-internal discovery URL |
| `Authentication__ValidIssuer` | optional public issuer when discovery uses an internal address |
| `Authentication__Audience` | required API audience, normally `hooviepack-api` |
| `Authentication__RequireHttpsMetadata` | keep `true` outside local development; an explicit HTTP metadata URL is accepted only for loopback or the Docker-internal `keycloak` host while `ValidIssuer` remains enforced |
| `MediaStorage__RootPath` | private mounted media directory |
| `Database__ApplyMigrations` | apply committed migrations during local/development API startup |
| `Cors__AllowedOrigins__0` | first allowed browser origin |

Liveness is available at `/health/live`; readiness, including PostgreSQL, is available at `/health/ready`.

Production forces `Database__ApplyMigrations=false` for the API. Its schema is updated explicitly by the profile-gated `db-migrations` service in the repository-root `compose.prod.yaml`, built from `Dockerfile.migrations`. See the root [README](../../README.md#database-migrations) for the manual command, failure behavior, and deployment order.

## Media

JPEG, PNG, and WebP files are identified and fully decoded before acceptance, limited to 10 MB and safe pixel dimensions, assigned server-generated names, and stored beneath the configured media root. Animated and structurally malformed images are rejected. Returned `/api/media/*` URLs require the same bearer token and family authorization as the associated resource; clients should fetch them through an authenticated HTTP client and display them as object URLs.

The normal test run skips PostgreSQL-only concurrency tests when no server is configured. Set `HOOVIEPACK_TEST_POSTGRES` to a disposable PostgreSQL connection string to also verify migrations, retry-safe invite redemption, and serialized reaction toggles; each test creates and removes its own isolated schema.
