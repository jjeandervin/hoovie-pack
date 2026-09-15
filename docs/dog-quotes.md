# Dog quotes

Curated quotes live in the main API's PostgreSQL `DogQuotes` table. All endpoints
require the same authenticated HooviePack user as Dogipedia.

| Endpoint | Behavior |
| --- | --- |
| `GET /api/dog-quotes` | Active quotes, ordered by author, year, then ID |
| `GET /api/dog-quotes/random` | One random active quote; optional `category` filter |
| `GET /api/dog-quotes/{id}` | One active quote by its HooviePack UUID |
| `GET /api/dog-quotes/categories` | Alphabetical, distinct active categories, ignoring casing and blank values |

Browse accepts `page` (default 1), `pageSize` (default 25, maximum 100),
`category` (case-insensitive exact match), and `author` (case-insensitive partial
match). Filters are trimmed; blank filters are ignored. Category and author
filters allow up to 100 and 250 characters respectively. Invalid pagination or
filter lengths produce the standard 400 validation problem. `%`, `_`, and
backslash in filters are literal characters.

Pagination uses the existing `PagedResponse<T>` contract:
`items`, `page`, `pageSize`, `totalCount`, and `totalPages`.
An empty table returns a 200 browse response with an empty `items` array and
zero counts, and a 200 categories response with `[]`. Missing or inactive IDs,
empty random requests, and categories with no eligible random quote return a
404 problem with code `dog_quote_not_found`.

Quote DTOs expose only `id`, `text`, `author`, `work`, `year`, `sourceUrl`, and
`category`. Metadata may be null. Source URLs are informational and are returned
as stored; clients should only make valid HTTP/HTTPS URLs clickable.

## Schema and curation

The `AddDogQuotes` migration adds the table and indexes without seed data. It
runs through the existing main API `db-migrations` deployment job. Startup
migration behavior is unchanged.

Load curated data separately after applying migrations. There is no importer
or write endpoint in this phase. Imports must supply independent UUIDs and UTC
`CreatedAtUtc` / `UpdatedAtUtc` timestamps; `IsActive` defaults to true. Quote
text is PostgreSQL `text` with no short length limit. Author, work, source URL,
and category have limits of 250, 500, 2000, and 100 characters. Unknown metadata
should be null, including year. Text is not unique and categories are free-form.

Tracked application saves trim surrounding quote text, reject blank text, set
both timestamps on insertion, and refresh `UpdatedAtUtc` on modification.
Independent SQL imports/updates must handle normalization and timestamps themselves.

## Verification

From `apps/api`, run:

```powershell
dotnet test tests/HooviePack.Api.Tests/HooviePack.Api.Tests.csproj -p:StartDebugDependencies=false
```

Set `HOOVIEPACK_TEST_POSTGRES` to a disposable PostgreSQL connection string to
include migration, query, and controller response tests. Each database test
creates and removes an isolated schema. These tests exercise PostgreSQL random
ordering and case-insensitive filtering directly.
