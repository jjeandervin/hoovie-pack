# HooviePack product and technical specification

## Product vision

HooviePack is a private digital family space for sharing updates, photos, comments, reactions, and dog profiles. It should feel polished and adult-friendly while using a warm corgi/dog personality in branding, illustrations, icons, and microcopy. The working production domain is `hooviestar.com`.

Core qualities:

- private and family-oriented
- fun, friendly, and modern
- mobile-first and photo-forward
- colorful with earthy hues
- dog-themed without feeling childish

Useful product language includes “Welcome to the Pack,” “Family feed,” “Dogs of the family,” “Add a pup,” “Share an update,” and “What’s new in the pack?” Conventional labels should win whenever playful wording would reduce clarity.

## MVP scope

### Authentication

- OAuth 2.0 / OpenID Connect through self-hosted Keycloak
- Authorization Code Flow with PKCE in Angular
- bearer JWT validation in the ASP.NET Core API
- login, logout, refresh, and persistent browser sessions
- create/synchronize an app-level user record on first authenticated use
- local Keycloak accounts are sufficient for MVP; Google/Microsoft identity brokering is a later enhancement

Unauthenticated callers must never receive family content. Authorization is always enforced by the API, not only by route guards or UI state.

### Families and invites

A family is the private group boundary. It includes an ID, name, slug, description, creator, and creation timestamp. A user creates a family or joins one through an expiring invite code/link. The creator becomes Owner.

Membership roles:

- Owner: full family administration
- Admin: invite/remove members and edit family details
- Member: view, post, comment, and react

The model should permit users to belong to multiple families, even if MVP UI focuses on one active family. Every feed, membership, dog, invite, post, comment, reaction, and media access must resolve back to an authorized family membership.

### People profiles

An app user includes a UUID, OIDC subject, email, display name, avatar URL, short bio, created timestamp, and last-seen timestamp. Users can edit their profile and view profiles of people who share a relevant family. Profile information is not public.

### Dog profiles

Dogs are a first-class MVP personality feature. A dog belongs to a family and includes a UUID, name, photo, breed, birthday or approximate age, bio, favorite thing, optional related owner/member, and creation timestamp.

Family members can list and view dog cards/details. Authorized users can create/edit/delete dogs according to the API policy. Optional seed names are Hermes (Hoovie), Freya, Wilfor, and Brownie.

### Posts and photos

Family members can create text/photo posts, browse a reverse-chronological family feed, edit their own posts, and delete their own posts. Owner/Admin can delete any post in their family.

Post rules:

- content is required unless at least one photo is present
- content is limited to 2,000 characters
- at most four photos per post
- at most 10 MiB per photo
- accepted formats: JPEG, PNG, and WebP

The domain API validates authorization and declared metadata, then calls an internal File Service. That service creates a stable `FileId`, owns the private storage-key mapping/metadata, and issues short-lived presigned URLs. The browser transfers bytes directly to or from a private Amazon S3 bucket; file bodies never pass through the domain API, File Service, or Nginx. AWS credentials remain backend-only.

Domain photo metadata includes the domain photo ID, post ID, stable `FileId`, original file name, content type, sort order, and creation timestamp. Storage metadata includes `FileId`, internal S3 key, original name, content type, declared/observed size, and creation time. Raw keys and presigned URLs are not domain identifiers and are not persisted outside the File Service. Orphaned metadata/objects should be reconciled when practical.

### Comments and reactions

Members can add comments up to 500 characters and delete their own. Owner/Admin can remove comments within their family. Comments include ID, post, author, content, and created/updated timestamps.

Supported reactions are `paw`, `heart`, and `bone`. A user may toggle one reaction per post per type. The feed displays aggregate counts.

### Explicitly out of scope

- direct messaging
- notifications and push notifications
- video uploads
- image resizing, thumbnails, optimization, format conversion, and EXIF processing
- CloudFront/CDN, Lambda/S3 event processing, and SQS media pipelines
- stories or reels
- advanced moderation
- separate albums
- events/calendar
- public family discovery
- AI features

## Primary experience

1. Open HooviePack and authenticate.
2. Create a family or accept an invite.
3. Complete a personal profile.
4. Land on the active family feed.
5. Share an update/photo, comment/react, and browse members and dogs.

Mobile navigation uses Home, Family, Dogs, a prominent create-post action, and Profile. Desktop can use a left rail or top navigation. The create flow should work comfortably with one hand.

Required screens:

1. Login
2. Create/join family onboarding
3. Home feed
4. Family members
5. Dog profiles
6. Create/edit post
7. User profile
8. Owner/Admin family settings

Reusable UI includes an app shell, brand/top bar, bottom navigation, family selector where needed, post card, comment list, reaction bar, member card, dog card, image uploader, avatar, empty state, and loading skeletons.

## Design direction

Use warm ivory/oatmeal backgrounds, sage or moss brand tones, terracotta actions, golden mustard highlights, bark-brown details, and muted teal/sky accents. Maintain accessible contrast.

The visual language uses rounded cards, soft shadows, large touch targets, clean typography, prominent photos, and sparing paw/bone/tag/dog-ear motifs. Avoid generic corporate blue, heavy dark layouts, neon, clutter, and cartoon overload.

Layouts begin at small-phone widths and expand cleanly. Mobile has fixed/sticky bottom navigation, compact forms, readable spacing, lazy-loaded images, and swipeable/carousel image blocks where helpful. Controls remain keyboard accessible, semantically marked up, and properly labeled.

## Technical architecture

### Stack

- Angular, TypeScript, Angular Router, HttpClient, standalone components where useful
- ASP.NET Core Web API on modern .NET (target .NET 10 where available)
- Entity Framework Core, code-first migrations, and PostgreSQL
- Keycloak for OAuth/OIDC
- dedicated internal File Service with stable `FileId` metadata and private Amazon S3 storage
- browser-to-S3 transfer through short-lived presigned PUT/GET URLs
- Dockerfiles, Docker Compose, and Linux Nginx reverse proxy

Keep frontend state in focused Angular services unless a heavier state library becomes demonstrably useful. Use DTOs and explicit API contracts. Organize backend concerns into domain, application, infrastructure/data, authentication, storage, and endpoint/controller layers.

### Repository

```text
apps/
  web/                 Angular application and Dockerfile
  api/                 domain API + File Service, separate EF contexts, and service/migration Dockerfiles
infra/
  nginx/               production reverse proxy and TLS paths
  keycloak/            importable realm configuration
compose.yaml
compose.prod.yaml
.env.example
README.md
docs/spec.md
```

### Runtime services and URLs

Compose runs `web`, `api`, internal-only `files-api`, `postgres`, and `keycloak`, with a dedicated Keycloak database and named volumes for application and identity data. A retained legacy `media_data` volume is a migration/rollback source only and is not mounted by the normal API. The production overlay defines profile-gated `db-migrations` and `files-db-migrations` jobs that run explicitly before application updates and are excluded from normal `up` commands. The optional production-profile `nginx` service routes:

- `https://hooviestar.com` to Angular
- `https://hooviestar.com/api` to ASP.NET Core
- `https://auth.hooviestar.com` to Keycloak

It never routes `files-api`; browsers use presigned regional S3 URLs directly. Production removes the File Service development port and keeps it off the external proxy network.

Configuration and credentials come from environment variables; no real secret is committed.

## Data model

Use UUID primary keys and UTC timestamps. Minimum entities:

- `AppUser`
- `Family`
- `FamilyMembership`
- `FamilyInvite`
- `DogProfile`
- `Post`
- `PostPhoto`
- `Comment`
- `Reaction`

Relationships:

- users and families are many-to-many through membership
- a family has many dogs and posts
- a post has many photos, comments, and reactions

Use created timestamps everywhere and updated timestamps where content can change. Index family/feed lookups, membership authorization checks, invite codes, OIDC subjects, post ordering, and reaction uniqueness.

## API contract and authorization

RESTful JSON route groups:

- `/api/me`
- `/api/families`
- `/api/families/{familyId}/members`
- `/api/families/{familyId}/dogs`
- `/api/families/{familyId}/posts`
- `/api/posts/{postId}/comments`
- `/api/posts/{postId}/reactions`
- authorized upload initialization, FileId association, and download-URL routes

Every secured endpoint verifies the authenticated OIDC subject, family membership, and required ownership/role. Object IDs supplied by clients never establish authorization on their own. Examples:

- only members can read a family feed or dogs
- only author or Owner/Admin can delete a post
- only comment author or Owner/Admin can delete a comment
- only Owner/Admin can invite or remove members

Validate DTOs, normalize invite behavior, and return clear RFC-appropriate errors. Main API CORS is restricted to the configured app origin; the private bucket has an exact-origin/method/header CORS rule for direct transfer. Authentication failures must not reveal private entity existence. Presigned URLs are short-lived capabilities and are never logged or persisted.

In Development, expose Swagger UI at `/swagger` with an Authorize/Bearer security definition so protected endpoints can be exercised with a Keycloak access token. Do not expose Swagger UI in Production.

## Persistence and operations

Commit migrations for both `AppDbContext` and the File Service's `FilesDbContext`/`files` schema. Development can apply them on service startup. Production service containers keep automatic migrations disabled. Deployment backs up data, runs both reviewed one-shot bundles in order, aborts the application update on either failure, and verifies both services after success.

Named storage survives container replacement:

- PostgreSQL application data
- Keycloak PostgreSQL data
- private S3 file objects

Backups must cover all three, be encrypted/off-host, preserve database/S3 recovery-point consistency, and be restoration-tested. Existing local media is inventoried and migrated idempotently by the explicit, profile-gated legacy importer during a maintenance window; its source is mounted read-only, startup never performs a destructive or implicit byte migration, and cutover is blocked if referenced files cannot be verified in S3. Nginx terminates TLS; PostgreSQL, Keycloak management, and the File Service remain private. Inputs, metadata, family boundaries, issuer, audience, CORS, CSP, and proxy headers are validated server-side.

## Non-functional requirements

- semantic HTML, visible focus, keyboard access, labels/errors, and sufficient contrast
- lazy/appropriately sized images and responsive mobile interaction
- clear loading, empty, and error states
- readable, maintainable code with comments only where they add context
- healthchecks for containers and documented local/production operations
- no committed secrets, public bucket/object ACL, or public File Service route

## Acceptance criteria

The MVP is complete when:

1. `docker compose up --build` starts the local stack after environment setup.
2. A user authenticates through OIDC Authorization Code + PKCE.
3. A user creates a family and becomes its Owner.
4. Another authenticated user joins through an invite.
5. Members create text/photo posts in a private feed.
6. Only appropriate family members can view feed content and media.
7. Members comment and toggle paw/heart/bone reactions.
8. Members can create and view dog profiles.
9. The UI is responsive, accessible, earthy, polished, and subtly corgi-themed.
10. PostgreSQL data, Keycloak data, and private S3 objects persist across restarts.
11. Both EF Core contexts and dedicated service/migration Dockerfiles are committed.
12. The documented Nginx profile can serve `hooviestar.com` and `auth.hooviestar.com` over TLS.

When tradeoffs are required, prefer simple architecture, correct authentication, good mobile UX, strict family privacy boundaries, and tasteful branding—in that order.
