# Dogipedia

Dogipedia is available at `/dogipedia` and `/dogipedia/breeds/:id` under the existing authenticated app shell. Browse state uses `search` and `page` query parameters. The API reads only local PostgreSQL data; the Angular client never calls Dog API for catalog data.

## Local catalog and API

`GET /api/dogipedia/breeds?search=corgi&page=1&pageSize=24` returns active breeds, card fields, the primary image with attribution, and pagination metadata. Search matches canonical and alternate names, case-insensitively. Canonical exact, prefix, and substring matches rank before alternate-name matches, with alphabetical ordering within each rank. `%`, `_`, and backslash are treated literally. Search is limited to 100 characters, page starts at 1, and page size is 1–100 (default 24). Invalid inputs return the existing validation ProblemDetails.

`GET /api/dogipedia/breeds/{id}` uses a HooviePack UUID and returns detail, or 404 for unknown/inactive breeds. Both routes require the application's normal authentication. An uninitialized list returns 503 ProblemDetails with `code=dogipedia_catalog_unavailable`; an initialized search with no matches returns 200 and an empty page.

The `AddDogipediaCatalog` EF migration creates `DogipediaBreedGroups`, `DogipediaBreeds`, `DogipediaBreedImages`, and `DogipediaSyncStates`. External IDs are unique reference keys; internal IDs remain stable across updates. Small string collections use PostgreSQL arrays and sources use JSONB. Canonical weights and heights remain kilograms and centimeters; the UI converts to pounds and inches.

## Phase 2: gallery and related breeds

The detail response includes `primaryImage`, `images`, and `relatedBreeds` in the same request. `images` contains every active image for the breed, ordered by `SortOrder` then internal ID. Each image exposes its HooviePack ID, thumbnail/medium/large URLs, and individual attribution; original image URLs are not exposed. The first image is `primaryImage`, or null when the collection is empty. The Phase 1 `image` field remains an alias of `primaryImage` for clients already open during an API rollout.

The gallery shows one selected image with its corresponding credits and a thumbnail carousel with no visible scrollbar. Previous/next arrows select one photo at a time and keep its thumbnail in view; the previous arrow is disabled at the first photo and the next arrow at the last. Switching photos makes no additional catalog request. Thumbnails use the smallest available variant and lazy loading; only the selected main photo has high fetch priority. Portrait photos use the full-image, uncropped display from Phase 1. Thumbnail buttons expose pressed state and support Tab, Enter/Space, arrow keys, Home and End. Zero images use the existing placeholder, one image omits the selector, and failed photos do not prevent other photos from being selected.

Related breeds are the first six active breeds in the same non-null group, excluding the current breed, ordered by name then ID. Cards contain only ID, name, group name, and the primary image with credits. Ungrouped breeds are not treated as related, and empty sections stay hidden. Related links use Angular routing and retain the original browse query parameters.

The backend uses at most three local queries: the breed/group, a projection of its active gallery metadata, and a projection of the six related cards with their primary images. It skips the third query when the breed has no group. Phase 2 requires no new schema migration, upstream calls during browsing, image proxy, or application cache.

## Synchronization

`DogipediaSyncBackgroundService` immediately checks eligibility in a new service scope and checks again hourly. A successful sync stays fresh for 24 hours. It does not delay API startup. `IDogipediaCatalogSyncService.SyncAsync()` also supports an independent forced refresh for future internal tooling; no manual HTTP endpoint is exposed.

The service acquires a nonblocking PostgreSQL advisory lock on a dedicated, unpooled connection. It rechecks freshness after acquisition, downloads complete group and breed collections, validates IDs, relationships and pagination counts, then applies all catalog changes and successful state in one transaction. EF's retrying execution strategy wraps that transaction. Missing records become inactive only after the full download validates. Lock release runs during disposal, including cancellation and failure.

Failures preserve the previous successful catalog and timestamp. They update bounded diagnostic state and retry at a subsequent hourly check. The HTTP client retries transient failures at most twice, with backoff, jitter and `Retry-After` support. Normal tests mock this HTTP boundary.

Images remain upstream URLs; no images are copied to S3. Cards and details show image credits. Imported text uses Angular interpolation, and only absolute HTTP(S) links are exposed. Missing/failed images show the Dogipedia book-and-paw placeholder.

## Configuration and deployment

Defaults are in the main API's `appsettings.json`. Environment overrides are:

| Setting | Default |
| --- | --- |
| `DogApi__BaseUrl` | `https://dogapi.dog/api/v2/` |
| `DogApi__PageSize` | `1000` |
| `DogApi__TimeoutSeconds` | `30` |
| `Dogipedia__Sync__Enabled` | `true` |
| `Dogipedia__Sync__FreshnessHours` | `24` |
| `Dogipedia__Sync__CheckIntervalMinutes` | `60` |

No Dog API key is required. Production continues using the existing `db-migrations` job before application replacement, with `Database__ApplyMigrations=false` on the production API. The migration only creates schema; the worker populates it afterward. No deployment or production migration is part of local implementation/testing.

Synchronization logs use the existing logging pipeline. Search for `Component=Dogipedia`, `Operation=CatalogSync`, and `Status=Started/Completed/Failed/Skipped`. Completion events include breed/group/image counts, inserted/updated/deactivated counts, and elapsed milliseconds. `DogipediaSyncStates` stores last attempt/success/failure timestamps, successful counts, consecutive failures and a bounded error message.

The upstream contract is documented at [Dog API v2](https://dogapi.dog/docs/api-v2).

## Verification

From the repository root:

```powershell
# Set this to a disposable PostgreSQL database. Tests create and remove isolated schemas.
$env:HOOVIEPACK_TEST_POSTGRES = '<PostgreSQL connection string>'
dotnet test apps/api/HooviePack.slnx -p:StartDebugDependencies=false
```

From `apps/web`:

```powershell
npm test
npm run build:production
npx playwright install chromium
npm run test:dogipedia:browser
```

To use installed Edge for the browser checks, set `$env:PLAYWRIGHT_CHANNEL = 'msedge'` instead of installing Chromium. The browser suite compiles the real Angular components and app shell with Angular's compiler, substitutes test identity/family providers, and mocks HooviePack's HTTP boundary. It covers debounce, cancellation, URL state, pagination, navigation, missing data, attribution, unsafe content and responsive layouts, plus gallery selection, keyboard controls, per-photo failures, and related-breed navigation. PostgreSQL tests verify image ownership/order, related-breed filtering/limits, and the fixed query count. Screenshots are written beneath `apps/web/test-results/`.
