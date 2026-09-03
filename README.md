# HooviePack

HooviePack is a private, mobile-first family social app with a warm, corgi-inspired personality. The monorepo contains an Angular web client, an ASP.NET Core domain API, an internal ASP.NET Core File Service, PostgreSQL persistence, Keycloak OIDC authentication, private Amazon S3 object storage, and production Nginx routing.

The product and engineering requirements are preserved in [docs/spec.md](docs/spec.md).

## Architecture

| Service | Container port | Default local URL | Persistent data |
| --- | ---: | --- | --- |
| Angular web | 80 | `http://localhost:4200` | none |
| ASP.NET Core API | 8080 | `http://localhost:5000` | none |
| Internal File Service | 8080 | `http://localhost:5001` (development only) | metadata in `postgres_data`, bytes in S3 |
| EF Core migration jobs | one-shot | none | none |
| Legacy media importer | one-shot, explicit profile | none | reads retained `media_data` read-only |
| PostgreSQL (app) | 5432 | `localhost:5432` | `postgres_data` |
| Keycloak | 8080 | `http://localhost:8081` | via `keycloak_data` |
| PostgreSQL (Keycloak) | 5432 | internal only | `keycloak_data` |
| Nginx | 80 / 443 | production profile only | host TLS files |
| Amazon S3 | HTTPS | presigned URLs only | private file objects |

All published development ports bind to `127.0.0.1` by default. Containers communicate over the private `hooviepack` Compose network. The File Service's loopback development port exists so a host-run API can call it; production removes that port and does not attach the service to the external proxy network. Nginx is deliberately behind the `production` profile because it requires a valid certificate. The migration jobs belong to the production-only Compose overlay and have a separate profile so an ordinary `up` never runs them implicitly.

The browser calls only the HooviePack API for application operations. After domain authorization, that API calls `files-api` over Docker DNS to obtain a stable `FileId` and a short-lived S3 URL. File bytes then travel directly between the browser and the private S3 bucket. Neither API, the File Service, nor Nginx proxies upload or download bodies. S3 storage keys remain an implementation detail owned by the File Service.

## Local setup

Prerequisites:

- Docker Engine or Docker Desktop with Docker Compose v2
- Git
- a private development S3 bucket and a dedicated least-privilege AWS identity or workload role
- Approximately 4 GB of free memory for builds, PostgreSQL, and Keycloak

From the repository root, create the local environment file:

```powershell
Copy-Item .env.example .env
```

On Linux or macOS, use `cp .env.example .env` instead. Replace every required `replace-with-...` value, including the S3 bucket, dedicated file-storage AWS credentials, and internal File Service key, before starting the complete stack. Never commit `.env`; it contains deployment credentials and is intentionally ignored. A convenient generator for passwords and the internal key is:

```bash
openssl rand -base64 48
```

Build and start the local stack:

```bash
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

First startup can take a few minutes while images build, databases initialize, the realm and demo users import, and both EF Core contexts apply their initial migrations. Follow startup with:

```bash
docker compose logs -f api files-api web keycloak
```

Open:

- App: `http://localhost:4200`
- API health: `http://localhost:5000/health/ready`
- File Service health: `http://localhost:5001/health/ready` (loopback development endpoint)
- API Swagger UI: `http://localhost:5000/swagger`
- Keycloak: `http://localhost:8081`
- Keycloak admin console: `http://localhost:8081/admin/`

Swagger UI is enabled only when `APP_ENVIRONMENT=Development`. It includes an **Authorize** control for a Keycloak bearer access token; enter the token using the UI's Bearer security scheme when exercising protected endpoints. Swagger is intentionally unavailable in Production.

The bootstrap admin credentials come from `KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD` in `.env`. Two development identities are imported when the Keycloak database is first created:

- `demo.owner` / the value of `DEMO_OWNER_PASSWORD`
- `demo.member` / the value of `DEMO_MEMBER_PASSWORD`

Both passwords are temporary, so Keycloak requires a change on first login. These are authentication identities only; family ownership and membership are managed by HooviePack after login.

Stop the services without deleting data:

```bash
docker compose down
```

To intentionally discard local database state, use `docker compose down --volumes` (or `docker compose down -v`). **This destroys the database volumes and any retained legacy `media_data` migration source; it does not delete S3 objects and cannot be undone without backups. Never run it against production.**

## Debugging

### Docker development mode

The default Compose configuration runs the application with `ASPNETCORE_ENVIRONMENT=Development`, enabling development logging and Swagger:

```powershell
docker compose up --build
```

Open the app at `http://localhost:4200` or Swagger at `http://localhost:5000/swagger`. Follow service output with:

```powershell
docker compose logs -f api files-api web keycloak
```

The API Docker image is published in Release mode, so use the local workflow below when source breakpoints or .NET Hot Reload are needed.

### Local API debugging

Stop the containerized web and main API services if they are running before running the main API from the .NET SDK:

```powershell
docker compose stop web api
```

Every main API Debug build automatically builds and starts PostgreSQL, Keycloak, and `files-api`, then waits for them to become healthy. The host-run API calls the File Service through its loopback port at `http://localhost:5001`; it does not use Docker DNS. Release builds do not start containers. To explicitly skip automatic dependency startup—for example in CI—build with `-p:StartDebugDependencies=false`.

The API project has a `UserSecretsId`. On each development workstation, populate its machine-local secret store once from the ignored `.env` file:

```powershell
$localSettings = Get-Content .env -Raw | ConvertFrom-StringData

$connectionString = `
  "Host=localhost;Port=$($localSettings.POSTGRES_PORT);Database=$($localSettings.POSTGRES_DB);Username=$($localSettings.POSTGRES_USER);Password=$($localSettings.POSTGRES_PASSWORD)"

dotnet user-secrets set "ConnectionStrings:DefaultConnection" $connectionString `
  --project .\apps\api\src\HooviePack.Api\HooviePack.Api.csproj

dotnet user-secrets set "FileService:BaseUrl" "http://localhost:$($localSettings.FILES_API_PORT)" `
  --project .\apps\api\src\HooviePack.Api\HooviePack.Api.csproj
dotnet user-secrets set "FileService:ApiKey" $localSettings.FILES_INTERNAL_API_KEY `
  --project .\apps\api\src\HooviePack.Api\HooviePack.Api.csproj
```

Then start the API with its `http` launch profile:

```powershell
dotnet watch --project .\apps\api\src\HooviePack.Api\HooviePack.Api.csproj `
  run --launch-profile http
```

The local API and Swagger UI will be available at `http://localhost:5103` and `http://localhost:5103/swagger`. For IDE breakpoints, open `apps/api/HooviePack.slnx`, select the `http` launch profile, and start debugging. ASP.NET Core loads user secrets automatically in Development. The secret value is stored outside the repository and must not be copied into `launchSettings.json`.

### Angular source debugging

Angular debugging requires Node.js 22 and npm. The development environment calls the local API directly at `http://localhost:5103/api`; the API allows requests from `http://localhost:4200` through its CORS configuration. Install dependencies and start the Angular development server:

```powershell
Set-Location .\apps\web
npm ci
npm start
```

Open `http://localhost:4200`. The development Angular configuration enables source maps, so TypeScript breakpoints work in browser developer tools or an IDE browser-debug configuration. Keycloak already permits the local `http://localhost:4200` callback URLs.

## Verification

Run the API unit and PostgreSQL integration tests from `apps/api`:

```powershell
dotnet test HooviePack.slnx -c Release
```

With the local Compose stack healthy, Windows PowerShell or PowerShell 7 can run the full OIDC/API smoke test:

```powershell
.\scripts\e2e-smoke.ps1
```

The script uses Authorization Code Flow with PKCE to create disposable owner, member, and outsider identities. It verifies profile synchronization, family creation and invite joining, photo posts, comments, reactions, dogs, authorized presigned media access, direct S3 transfer, and cross-family isolation. It requires working development-bucket configuration and leaves generated database records and S3 objects for inspection; clean up both deliberately after testing.

### CI and security checks

`.github/workflows/ci.yml` runs on pushes and pull requests. It restores, builds, and tests both backend services against PostgreSQL without real AWS credentials; installs the locked web dependencies, audits them, runs web tests, and makes a production build; validates the internal-only production Compose topology and profile-gated schema/content migration jobs; scans full Git history with Gitleaks; fails on vulnerable direct or transitive NuGet packages using JSON output; and builds and scans the custom API, File Service, migration/importer, and web images with Trivy for fixable high/critical vulnerabilities. `.github/dependabot.yml` opens grouped weekly NuGet, npm, Docker/Compose, and GitHub Actions updates.

The core checks can be run locally from the repository root (the NuGet guard requires `jq`, and image scans require Trivy):

```bash
docker run --rm -v "$PWD:/repo" -w /repo \
  ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f \
  git --redact --verbose --config .gitleaks.toml .

dotnet restore apps/api/HooviePack.slnx
dotnet build apps/api/HooviePack.slnx -c Release --no-restore
dotnet test apps/api/HooviePack.slnx -c Release --no-build
dotnet package list --project apps/api/HooviePack.slnx --vulnerable \
  --include-transitive --format json --output-version 1 --no-restore \
  > nuget-vulnerabilities.json
jq -e '[.projects[]?.frameworks[]? | ((.topLevelPackages // []) + \
  (.transitivePackages // []))[]? | select((.vulnerabilities // []) | \
  length > 0)] | length == 0' nuget-vulnerabilities.json
rm -f nuget-vulnerabilities.json

(cd apps/web && npm ci --no-audit --no-fund && npm audit --audit-level=high \
  && npm test && npm run build:production)

docker build --pull -t hooviepack-api:local apps/api
docker build --pull -f apps/api/Dockerfile.files -t hooviepack-files-api:local apps/api
docker build --pull -f apps/api/Dockerfile.migrations -t hooviepack-db-migrations:local apps/api
docker build --pull -f apps/api/Dockerfile.files.migrations -t hooviepack-files-db-migrations:local apps/api
docker build --pull -f apps/api/Dockerfile.file-migration -t hooviepack-file-migration:local apps/api
docker build --pull -t hooviepack-web:local apps/web
trivy image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
  --exit-code 1 hooviepack-api:local
trivy image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
  --exit-code 1 hooviepack-files-api:local
trivy image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
  --exit-code 1 hooviepack-db-migrations:local
trivy image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
  --exit-code 1 hooviepack-files-db-migrations:local
trivy image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
  --exit-code 1 hooviepack-file-migration:local
trivy image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
  --exit-code 1 hooviepack-web:local
```

PostgreSQL integration tests run when `HOOVIEPACK_TEST_POSTGRES` points to a disposable test database; CI provisions that database automatically. Never use production credentials for local or CI testing, and never commit scan output containing sensitive findings.

## Authentication configuration

The imported `hooviepack` realm contains:

- `hooviepack-web`, a public OIDC client using Authorization Code Flow with mandatory S256 PKCE
- `hooviepack-api`, the bearer-token audience
- an audience mapper that adds `hooviepack-api` to web access tokens
- local and production redirect/origin entries derived from `LOCAL_APP_ORIGIN` and `APP_ORIGIN`
- brute-force protection and two temporary-password demo users

The browser and `Authentication__Authority` use `${KEYCLOAK_PUBLIC_URL}/realms/hooviepack`. `Authentication__ValidIssuer` independently pins tokens to that public issuer, which must be HTTPS in production. `Authentication__MetadataAddress` may use `http://keycloak:8080` on the isolated Compose network so the API can fetch discovery/JWKS without routing out through the public proxy. Production keeps `Authentication__RequireHttpsMetadata=true`; the API narrowly exempts only loopback and the Docker-internal `keycloak` hostname and rejects arbitrary external HTTP metadata URLs. Signature, public issuer, audience, and lifetime validation remain enabled.

Realm startup import happens only when `hooviepack` does not already exist. Editing the JSON does not overwrite a live realm. To deliberately re-import it:

```bash
docker compose stop keycloak
docker compose run --rm keycloak import --file /opt/keycloak/data/import/hooviepack-realm.json --override true
docker compose up -d keycloak
```

Export/back up the realm before overriding a non-development instance. The bootstrap administrator is likewise created only when the Keycloak database is empty. Removing users from the import JSON does **not** delete or disable users in an existing Keycloak database. Operators must manually disable or delete any existing `demo.owner` and `demo.member` accounts in production.

For production, disable or delete both demo users, enable verified email once SMTP is configured, rotate the bootstrap password, and review redirect URIs in the Keycloak console. Google or Microsoft login can later be added as Keycloak identity providers without changing the app's OIDC contract.

## File storage

### Request and data flow

HooviePack uses stable application-level `FileId` values. A `FileId` is safe to retain in domain records; the S3 object key is private File Service metadata and is never used as a client-facing identifier. Presigned URLs are temporary capabilities and must not be persisted.

Uploads follow this sequence:

1. Angular asks the HooviePack API to initialize an upload in the context of an authorized domain operation.
2. The API enforces identity, family membership, and domain rules, then calls `files-api` with `X-Internal-Api-Key`.
3. The File Service validates basic metadata, creates the `FileId` and internal key, and returns a short-lived presigned PUT URL.
4. Angular PUTs the bytes directly to S3 using every required signed header.
5. Angular submits the returned `FileId` and one-time upload token with the domain operation. The API asks the File Service to complete the upload; it verifies the S3 object's size/content type before the domain association is committed.

For a download, the HooviePack API first performs the same domain authorization, asks the File Service for a short-lived GET URL by `FileId`, and returns that URL to Angular. The browser then reads directly from S3. Deletion resolves the `FileId` inside the File Service and removes the corresponding private object and metadata while preserving the domain API's existing best-effort cleanup semantics.

The File Service has no production host port, external-network alias, Nginx upstream, or browser runtime endpoint. Its loopback `FILES_API_PORT` is a development-only exception for the host-run API. The browser learns only the main API URL, a `FileId`, and a single-use-purpose presigned S3 URL.

### Configuration

Compose maps standard ASP.NET Core settings as follows:

| Setting | Consumer | Purpose |
| --- | --- | --- |
| `FileService__BaseUrl` | API | internal Docker URL (`http://files-api:8080`) |
| `FileService__ApiKey` | API | shared internal request credential |
| `FileService__TimeoutSeconds` | API | bounded internal HTTP request timeout |
| `MediaStorage__MaxImageBytes` | API | domain-side declared/completed image limit |
| `InternalApi__ApiKey` | File Service | validates the same shared credential |
| `ConnectionStrings__DefaultConnection` | both backend services | shared PostgreSQL database; the File Service owns a separate `files` schema and migration history |
| `Database__ApplyMigrations` | each backend service | development-only startup migration switch; forced off in production |
| `FileStorage__BucketName` | File Service | private S3 bucket |
| `FileStorage__Region` | File Service | AWS region |
| `FileStorage__KeyPrefix` | File Service | IAM-restricted object prefix, normally `files` |
| `FileStorage__UploadUrlLifetimeMinutes` | File Service | presigned PUT lifetime |
| `FileStorage__DownloadUrlLifetimeMinutes` | File Service | presigned GET lifetime |
| `FileStorage__MaxFileBytes` | File Service | declared file-size limit |
| `CSP_S3_ORIGIN` | web container | exact HTTPS bucket origin allowed by browser CSP |

The optional `FileStorage__ServiceUrl` and `FileStorage__ForcePathStyle` settings support isolated SDK-compatible development tests. Leave them empty/false for Amazon S3 production.

The host-side `FILES_AWS_ACCESS_KEY_ID`, `FILES_AWS_SECRET_ACCESS_KEY`, and optional `FILES_AWS_SESSION_TOKEN` are mapped to the AWS SDK's standard names only inside `files-api` and the explicitly invoked legacy importer. Prefer a dedicated workload role when the host platform supports one. The domain API, Angular/web, Keycloak, schema-migration jobs, and Nginx receive no AWS credentials. `FILES_INTERNAL_API_KEY` is a separate secret, not an AWS or database credential, and must be rotated on the API and File Service together.

### Private bucket, IAM, CORS, and CSP

Enable all S3 Block Public Access controls and keep object ACLs private. A dedicated file-storage identity needs only the operations used by the service on the configured prefix. A representative identity policy is:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject"
      ],
      "Resource": "arn:aws:s3:::REPLACE_BUCKET/files/*"
    }
  ]
}
```

Do not reuse a Route 53 deployment identity and do not grant S3 administration or wildcard-bucket access. Add `s3:ListBucket` only if a separately reviewed implementation actually lists objects, and then restrict it to the bucket ARN plus the `files/` prefix condition. The normal health check deliberately does not list, upload, or delete an object.

Direct browser requests require an S3 CORS rule; CORS does not make the bucket public. Replace the origins with the exact deployed values and allow only headers the signed PUT sends:

```json
[
  {
    "AllowedOrigins": [
      "http://localhost:4200",
      "https://hooviestar.com"
    ],
    "AllowedMethods": ["GET", "PUT", "HEAD"],
    "AllowedHeaders": ["Content-Type"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 300
  }
]
```

If checksum headers are added to the signed request later, add only those exact headers to this rule. `CSP_S3_ORIGIN` must match the origin emitted in presigned URLs, for example `https://REPLACE_BUCKET.s3.us-east-1.amazonaws.com`, with no path or trailing slash. The web container inserts that exact origin into `connect-src` and `img-src`; it does not allow arbitrary AWS or CDN hosts.

### Validation and failure behavior

Presigned URLs are scoped to one object and operation and expire after the configured short lifetime. Expiry is expected: the client requests a new URL through the authorized domain API rather than retaining or refreshing an old one. Invalid `FileId` values, missing metadata/objects, S3 unavailability, failed uploads, and failed deletion are reported without returning bucket credentials, storage keys, presigned URLs in logs, internal stack traces, or AWS error details to the browser.

Upload initialization validates declared name, content type, and size, but a presigned PUT does not make the API a byte-processing pipeline. This storage refactor deliberately does not decode, resize, re-encode, optimize, strip EXIF, create variants, or transcode files. A failed or abandoned direct PUT can leave pending metadata without an object; operations must tolerate that state, and operators should monitor/reconcile repeated missing-object or cleanup failures.

## Database migrations

Development defaults both `DATABASE_APPLY_MIGRATIONS` and `FILES_DATABASE_APPLY_MIGRATIONS` to true through Compose, so the API applies `AppDbContext` migrations and the File Service applies `FilesDbContext` migrations during local startup.

With a local .NET SDK, restore the repository-pinned `dotnet-ef` 10.0.11 tool and apply migrations manually from `apps/api`:

```bash
dotnet tool restore
dotnet tool run dotnet-ef -- database update \
  --project src/HooviePack.Api/HooviePack.Api.csproj \
  --startup-project src/HooviePack.Api/HooviePack.Api.csproj \
  --context AppDbContext

dotnet tool run dotnet-ef -- database update \
  --project src/HooviePack.Files.Api/HooviePack.Files.Api.csproj \
  --startup-project src/HooviePack.Files.Api/HooviePack.Files.Api.csproj \
  --context FilesDbContext
```

Production is deliberately different. [`compose.prod.yaml`](compose.prod.yaml) forces startup migrations off in both services and defines the profile-gated `db-migrations` and `files-db-migrations` one-shot jobs. [`apps/api/Dockerfile.migrations`](apps/api/Dockerfile.migrations) bundles `AppDbContext`; [`apps/api/Dockerfile.files.migrations`](apps/api/Dockerfile.files.migrations) bundles `FilesDbContext`. Neither final image starts an API.

Both jobs receive the same `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD`-derived `ConnectionStrings__DefaultConnection` as the services. The File Service isolates its tables and EF history in the `files` schema while using the same PostgreSQL database and backup boundary. Populate the values in the ignored production `.env`, ensure `postgres` is reachable, and review and back up both contexts before applying a release. Never put a connection string or credentials in an image or source-controlled file.

Build and run the production migration explicitly from the repository root:

```bash
docker compose -f compose.yaml -f compose.prod.yaml build \
  db-migrations files-db-migrations
docker compose -f compose.yaml -f compose.prod.yaml run --rm db-migrations
docker compose -f compose.yaml -f compose.prod.yaml run --rm files-db-migrations
```

Each run executes `/app/efbundle --no-color`, prints EF Core progress to the terminal, and removes the stopped one-shot container. It returns `0` when its context is current and non-zero on failure. A deployment must stop on either non-zero status; it must not start new application containers. Keep migrations additive and compatible with the still-running release because one context can succeed before the other fails.

Deployment automation should capture that command's standard output and error. For interactive troubleshooting where retained container logs are useful, omit `--rm` temporarily and assign a name:

```bash
docker compose -f compose.yaml -f compose.prod.yaml run \
  --name hooviepack-db-migrations-debug db-migrations
docker logs hooviepack-db-migrations-debug
docker rm hooviepack-db-migrations-debug
```

The debug container is stopped, not long-running; remove it after inspection. Use the same pattern with a distinct name for `files-db-migrations`. Do not run either migration job concurrently. A failed migration is not automatically rolled back by reverting an application image, and this workflow never drops, recreates, or automatically rolls back the production database. Inspect both contexts' actual state, then use a reviewed fix-forward plan or a separately tested restore procedure.

## Existing media migration

The previous implementation stored relative local paths in `Users.AvatarStoragePath`, `DogProfiles.PhotoStoragePath`, and `PostPhotos.StoragePath`, with bytes in `media_data` for Compose or an ignored `media` directory for a directly run API. The repository does not contain production data and cannot prove that a deployed volume is empty. The S3 cutover therefore retains the legacy columns and declares `media_data` for an explicit migration/rollback window, but the normal API no longer mounts or writes that volume.

No file-byte copy runs during service startup or the normal deployment wrapper. Before the first S3-only production release, an operator must perform and record this procedure:

1. Inventory every non-null legacy path in all three tables and every file in the production `media_data` volume. Record reference counts, byte counts, missing referenced files, duplicate references, and unreferenced files. Also check any host-run API `media` directory used by that deployment.
2. If and only if the verified inventory contains zero referenced files, record that result and proceed without a content import. Do not infer this from the checkout or from a newly created empty volume.
3. If referenced files exist, take tested database and read-only media backups, announce a maintenance window, and stop all upload/edit writers. The old API must not accept new local uploads while the inventory/import runs:

   ```bash
   docker compose -f compose.yaml -f compose.prod.yaml stop web api
   ```
4. Apply the additive `AppDbContext` and `FilesDbContext` migrations. Do not drop or overwrite legacy path columns. Build the profile-only `legacy-media-migration` image from the same reviewed checkout.
5. Run its dry-run inventory first. Compose mounts `media_data` at `/legacy-media` read-only, and the utility reports database references, unique source paths, unreferenced files, missing sources, and objects requiring migration:

   ```bash
   docker compose -f compose.yaml -f compose.prod.yaml build \
     db-migrations files-db-migrations legacy-media-migration
   docker compose -f compose.yaml -f compose.prod.yaml run --rm db-migrations
   docker compose -f compose.yaml -f compose.prod.yaml run --rm files-db-migrations
   docker compose -f compose.yaml -f compose.prod.yaml run --rm \
     legacy-media-migration --dry-run
   ```

6. Review and preserve the dry-run output. During the maintenance window, run the same utility without `--dry-run`:

   ```bash
   docker compose -f compose.yaml -f compose.prod.yaml run --rm \
     legacy-media-migration
   ```

   The importer derives size from the source, creates File Service metadata, uploads and verifies the S3 object, and backfills domain `FileId` references. `files.Files.LegacySourcePath` durably reuses the same mapping, so a failed run can be corrected and rerun without generating a second object. The source mount is read-only and is never deleted.
7. Require a zero-failure migration result, then verify every migrated domain reference has exactly one File Service metadata row and readable S3 object of the expected size/content type. Exercise authorized and unauthorized URL issuance, direct download, replacement, and deletion against representative avatars, dog photos, and post photos.
8. Only after verification, run the normal deployment wrapper to start the S3-only API/web stack. As a final fail-closed safeguard, the wrapper runs the importer's read-only `--check` mode and exits before replacing any application container if a legacy reference still needs migration. Retain the legacy database columns, volume, inventory report, and backup for the approved rollback period.
9. Remove legacy columns and the retained volume only in a later reviewed cleanup release and only after backup restoration and migration reconciliation have been tested.

The write-capable importer is deliberately absent from normal `up` and is never invoked by `deploy-prod.sh`. It runs only when explicitly targeted without `--dry-run` or `--check`. The deployment wrapper invokes only the non-mutating completion check. If inventory, migration, or that gate reports a missing/failed referenced file, the production cutover is blocked. Do not deploy the S3-only API and do not delete or detach the only readable legacy source until the discrepancy is resolved and the rerun verifies cleanly.

## Production deployment on Linux

Production uses the external `jeandervin-proxy`; the Nginx container and configuration in this repository are a secured reference/example, not the deployed edge. The production target is:

- `https://hooviestar.com` → Angular
- `https://hooviestar.com/api` → ASP.NET Core
- `https://auth.hooviestar.com` → Keycloak

The browser also connects directly to short-lived regional S3 HTTPS URLs. There is deliberately no public route or external-network alias for `files-api`.

### 1. Prepare the host and DNS

Use a supported Linux distribution, install Docker Engine plus the Compose plugin, and point `A`/`AAAA` records for both `hooviestar.com` and `auth.hooviestar.com` at the external proxy. Permit inbound TCP 80 and 443 (and SSH from trusted sources); do not expose database or application container ports publicly. Ensure the existing `jeandervin` Docker network is available and the external proxy is attached to it.

Place the checkout in a stable location such as `/opt/hooviepack`. Copy the environment template, generate independent random values for every password, then restrict the file:

```bash
cp .env.example .env
chmod 600 .env
```

Set at least these production values in `.env`:

```dotenv
BIND_ADDRESS=127.0.0.1
APP_ENVIRONMENT=Production
APP_ORIGIN=https://hooviestar.com
KEYCLOAK_PUBLIC_URL=https://auth.hooviestar.com
KEYCLOAK_METADATA_URL=http://keycloak:8080/realms/hooviepack/.well-known/openid-configuration
AUTH_REQUIRE_HTTPS_METADATA=true
DATABASE_APPLY_MIGRATIONS=false
FILES_DATABASE_APPLY_MIGRATIONS=false
FILES_INTERNAL_API_KEY=replace-with-a-long-random-internal-file-service-key
AWS_REGION=us-east-1
S3_BUCKET_NAME=replace-with-private-bucket-name
CSP_S3_ORIGIN=https://replace-with-private-bucket-name.s3.us-east-1.amazonaws.com
FILES_AWS_ACCESS_KEY_ID=replace-with-dedicated-files-access-key-id
FILES_AWS_SECRET_ACCESS_KEY=replace-with-dedicated-files-secret-access-key
DEMO_OWNER_PASSWORD=replace-with-a-demo-owner-password
DEMO_MEMBER_PASSWORD=replace-with-a-demo-member-password
```

Do not add a trailing slash to public or CSP origins. Leave `API_BASE_URL=/api`, use independent random database/admin/internal/AWS credentials, and never commit `.env`. Use a separate file-storage identity rather than credentials used for Route 53 or another deployment concern. When workload credentials are available, leave the static `FILES_AWS_*` values empty and supply only the dedicated role.

The production overlay keeps the HTTPS-metadata policy enabled. As described under authentication configuration, the API recognizes `http://keycloak:8080` as a trusted, isolated backchannel while still validating the public HTTPS issuer. The web container builds its CSP from the exact `KEYCLOAK_PUBLIC_URL` and `CSP_S3_ORIGIN`, so production does not inherit a wildcard localhost or AWS allowance.

### 2. Configure the external reverse proxy

Configure TLS and these upstream routes on the real `jeandervin-proxy`:

- `hooviestar.com` `/api` to `hooviepack-api:8080`
- the rest of `hooviestar.com` to `hooviepack-web:80`
- `auth.hooviestar.com` to `hooviepack-keycloak:8080`

Do not add an upstream or route for `files-api`. It stays only on the private `hooviepack` network; the browser uses presigned S3 URLs for bytes.

Forward the original `Host`, scheme, address, and port. Normal OIDC routes must remain public, including discovery, authorization, token, JWKS, logout, and login redirects. Restrict only `/admin/` (and its subpaths) to exact LAN/VPN/trusted administrator ranges. An Nginx-style rule is:

```nginx
location ^~ /admin/ {
    allow <LAN-or-VPN-CIDR>;
    deny all;
    proxy_pass http://hooviepack-keycloak:8080;
}

location / {
    proxy_pass http://hooviepack-keycloak:8080;
}
```

Adapt the syntax to the external proxy and preserve its normal forwarded headers. The in-repository [`infra/nginx/conf.d/hooviepack.conf`](infra/nginx/conf.d/hooviepack.conf) demonstrates a safe loopback-only default, but changing it does not update `jeandervin-proxy`; that external configuration is a required manual production action.

### 3. Start and verify

After completing any required existing-media procedure, back up PostgreSQL and the S3 bucket, fetch the reviewed release, and run the production deployment wrapper. The script uses `set -Eeuo pipefail`, validates Compose, pulls third-party images, builds both APIs, both schema-migration bundles, and the legacy verification image from the same checkout, runs `AppDbContext` and then `FilesDbContext` migrations, verifies that no legacy media reference remains, and only then updates the application stack. A schema migration or legacy verification failure exits before `up`:

```bash
cd /opt/hooviepack
git pull --ff-only
bash scripts/deploy-prod.sh
```

The expanded command order in [`scripts/deploy-prod.sh`](scripts/deploy-prod.sh) is:

```text
1. docker compose ... config --quiet
2. docker compose ... pull --ignore-buildable
3. docker compose ... build --pull web api files-api db-migrations files-db-migrations legacy-media-migration
4. docker compose ... run --rm --no-TTY db-migrations
5. docker compose ... run --rm --no-TTY files-db-migrations
6. docker compose ... run --rm --no-TTY legacy-media-migration --check
7. docker compose ... up --detach --no-build files-api api web postgres keycloak-db keycloak
8. docker compose ... ps
```

Step 6 reads the databases and the read-only legacy volume only. It does not create File Service records, upload objects, backfill references, or delete source bytes.

After the script succeeds, inspect application logs and health:

```bash
docker compose -f compose.yaml -f compose.prod.yaml logs --tail=200 \
  api files-api keycloak web
```

Verify readiness from the deployment host, then verify the public endpoints from another machine:

```bash
docker compose -f compose.yaml -f compose.prod.yaml exec -T api \
  curl --fail --silent --show-error http://127.0.0.1:8080/health/ready
docker compose -f compose.yaml -f compose.prod.yaml exec -T files-api \
  curl --fail --silent --show-error http://127.0.0.1:8080/health/ready
curl --fail --show-error --head https://hooviestar.com/
curl --fail --show-error https://auth.hooviestar.com/realms/hooviepack/.well-known/openid-configuration
```

Also verify that a public request to `https://auth.hooviestar.com/admin/` is denied while a trusted LAN/VPN administrator can reach it, then exercise login/logout and one authorized direct upload/download. Confirm the browser talks to S3 for bytes and an unauthorized user cannot obtain a download URL. The production overlay removes all direct host ports; only web, API, and Keycloak join `jeandervin`, while `files-api` remains internal.

### 4. Upgrade Keycloak and update the stack

TLS renewal belongs to the external proxy. Before application, PostgreSQL, or Keycloak image upgrades, read the relevant release/migration notes and take tested backups. For a Keycloak upgrade, back up `keycloak-db`, update the deliberate `KEYCLOAK_IMAGE` pin (currently `quay.io/keycloak/keycloak:26.7.2`), then recreate only Keycloak and verify discovery, login/logout, and the `/admin/` restriction:

```bash
cd /opt/hooviepack
docker compose -f compose.yaml -f compose.prod.yaml pull keycloak
docker compose -f compose.yaml -f compose.prod.yaml up -d keycloak
```

For a normal application/image update:

```bash
cd /opt/hooviepack
git pull --ff-only
bash scripts/deploy-prod.sh
```

Back up first and review pending migrations for both DbContexts. Schema changes that are incompatible with the running services require a coordinated maintenance window or expand/contract rollout. Do not use `docker compose down -v` during an upgrade: it destroys both database volumes and any retained legacy-media migration source. It does not roll back or remove S3 objects.

## Backups and security checklist

Create encrypted, off-host backups on a tested schedule. A logical application database backup can be made without placing its password on the command line:

```bash
mkdir -p backups
docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' \
  | gzip > "backups/hooviepack-$(date +%F-%H%M%S).sql.gz"
docker compose exec -T keycloak-db sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' \
  | gzip > "backups/keycloak-$(date +%F-%H%M%S).sql.gz"
```

Back up private S3 objects with a tested, encrypted strategy such as bucket versioning plus a separately controlled backup or replication target. Test restoration of PostgreSQL file metadata/domain references and S3 objects to a consistent recovery point. During the legacy migration/rollback window, also archive `media_data`; after a verified cleanup it is no longer part of normal application backup.

Before exposing the service:

- replace all sample passwords, keep `.env` mode `0600`, and never commit it
- keep S3 Block Public Access enabled and restrict the dedicated file identity to GetObject/PutObject/DeleteObject on the configured prefix
- configure exact bucket CORS and web CSP origins; never expose AWS credentials, object keys as identifiers, or the internal File Service
- manually remove/disable any demo identities already present, and configure SMTP plus email verification
- expose only the external reverse proxy on ports 80/443
- keep PostgreSQL and the Keycloak management port off the public network
- restrict Keycloak `/admin/` with a VPN or trusted-IP allowlist on the external reverse proxy
- keep host, Docker, base images, Keycloak, PostgreSQL, and AWS SDK dependencies patched
- test metadata validation, URL expiry, missing-object behavior, and family authorization for every file operation
- monitor container health, authentication failures, S3 errors/orphans, database/storage growth, and certificate expiry
- never use realm exports as the sole Keycloak database backup

## Troubleshooting

`docker compose ps` shows health for every service. Useful targeted logs are:

```bash
docker compose logs --tail=200 postgres keycloak-db
docker compose logs --tail=200 keycloak
docker compose logs --tail=200 api files-api
docker compose logs --tail=200 web nginx
```

Common causes:

- **Compose reports a required variable error:** copy `.env.example` to `.env` and fill every required database, admin, demo-user, internal-service, S3, and AWS value.
- **Login redirects to the wrong host:** make `KEYCLOAK_PUBLIC_URL`, `APP_ORIGIN`, DNS, TLS names, and Keycloak client redirect URIs agree.
- **Realm edits are ignored:** startup import skips an existing realm; use the deliberate re-import procedure or edit through the admin console.
- **Migration job fails:** identify which DbContext failed, read its foreground output, confirm the production database values/PostgreSQL health, and stop the rollout until both contexts' state is reviewed.
- **API or File Service stays unhealthy:** confirm both explicit migrations succeeded, then check PostgreSQL readiness and both service logs before querying their `/health/ready` endpoints. Production service logs should not contain startup migration attempts.
- **Direct upload is blocked in the browser:** compare the presigned URL origin with `CSP_S3_ORIGIN`, inspect the browser CSP/CORS error, and verify the bucket CORS origin, method, and signed Content-Type header. Do not make the bucket public to fix CORS.
- **S3 returns AccessDenied or SignatureDoesNotMatch:** verify region, bucket, clock synchronization, signed method/content type, URL expiry, and the dedicated prefix policy. Never print a complete presigned URL into shared logs.
- **The optional in-repo Nginx will not start:** confirm its certificate files exist beneath `LETSENCRYPT_DIR/live/hooviestar.com/` and run `docker compose --profile production run --rm nginx nginx -t`.
- **Port already allocated:** change the relevant local port in `.env`; production ports 80/443 must be free.
