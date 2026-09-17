# Family Calendar — Phase 1

Open **Tools → Family Calendar** (`/tools/calendar`). The month view supports previous/next, Today, and direct month/year selection. Select a date to see its events and add an event with that date filled in. On mobile, the grid shows counts and the selected day's events appear below it. Timeline/List provides paginated upcoming and historical browsing, grouped by occurrence month.

Events support historical and future dates, inclusive multi-day ranges, all-day or timed events, a timezone for timed events (initially the browser/user timezone), title, category, description, location, and multiple photos. Event occurrence dates are stored as PostgreSQL `date` values independently of UTC creation/update timestamps. Dates are never converted through UTC in the calendar UI.

## Profile birthdays

Members can add, change, or clear their optional complete birth date under **Profile → Birthday**. `AppUser.BirthDate` is nullable and is returned by `/api/me`; `PUT /api/me` accepts `birthDate` (use `null` to clear). The `AddMemberBirthDate` migration adds only this profile column. Dogs continue to use the existing `DogProfile.Birthday` field.

Calendar lists now return unified items with `sourceType` (`calendarEvent`, `memberBirthday`, or `dogBirthday`), `calendarEventId`, `memberId`, `dogId`, `imageUrl`, and `isEditable`. Detail/create/update endpoints still return the full persisted-event DTO. A member birthday links to its family membership profile; a dog birthday links to the dog detail page. The member's own profile provides **Edit profile**, and dog editing continues to use the existing dog permissions. Birthday entries cannot be edited as calendar events.

Birthday occurrences are generated after family authorization, from only that family's current members and dogs. They are never stored as calendar rows. Profile edits, removals, and membership changes take effect on the next query. Synthetic IDs use `member-birthday-{membershipId}-{year}` and `dog-birthday-{dogId}-{year}`. Both sources use the profile image when available, with cake/paw icons distinguishing them.

Projection includes each year intersecting the requested range, including the birth year but never earlier. February 29 birthdays appear on **February 28 in non-leap years**; the stored birthday remains February 29. Multi-year queries, history, and open-ended upcoming queries use the same projection. Missing date bounds span the supported date range, so upcoming pages continue to include annual birthdays. Counts include all matching sources and pagination occurs after combining them in date order. Only the candidate prefix needed for a page is materialized; future birthday DTOs for every supported year are not loaded on each request.

## Authorization and files

All endpoints require authentication and use the existing `FamilyAccessService`. Members can view and create events; the creator and family owners/admins can edit/delete events and manage photos. Every event and photo lookup is scoped to the family route. Nonmembers and mismatched family/event routes return 404; members lacking management permission receive 403.

Photo uploads use `/api/media/uploads` with `purpose: "calendarPhoto"` and `familyId`, followed by the existing presigned PUT. Attach the returned `{ fileId, uploadToken }` reference to the event; an already-uploaded, unassociated reference can be attached through the same endpoint. As with existing post uploads, a bare file ID cannot authorize an attachment and files already associated elsewhere cannot be reassigned. The calendar stores file references only. Image downloads check family membership before returning the file service's short-lived download URL. The UI uses `AuthImageDirective` for galleries and the enlarged-image dialog.

The first attached photo is the default cover. Selecting another cover or removing it updates the effective cover. Removing photos or deleting an event commits the database change before invoking the existing best-effort file cleanup.

## API

Base: `/api/families/{familyId}/calendar-events`

| Method | Path | Behavior |
| --- | --- | --- |
| GET | base | Paged events; optional `startDate`, `endDate`, `direction=upcoming\|history`, `page`, `pageSize` (maximum 100) |
| GET | `/{eventId}` | Complete event, creator summary, management permission, and photos |
| POST | base | Create event, returning 201 and Location |
| PUT | `/{eventId}` | Update event fields |
| DELETE | `/{eventId}` | Delete event and clean up its files |
| POST | `/{eventId}/photos` | Attach an uploaded `{ fileId, uploadToken }` |
| GET | `/{eventId}/photos/{photoId}` | Authorized presigned download |
| PUT | `/{eventId}/photos/{photoId}/cover` | Select cover photo |
| DELETE | `/{eventId}/photos/{photoId}` | Remove attachment and clean up file |

Date filters match overlapping event ranges, including events beginning before the displayed month. Lists sort by occurrence date/time and ID, ascending for upcoming and descending for history. The monthly UI requests only the visible month's range and fetches additional pages when needed. The timeline supplies today's lower bound for upcoming or yesterday's upper bound for history.

## Migration and checks

`20260916231514_AddFamilyCalendar` creates `CalendarEvents` and `CalendarEventPhotos`, family/date indexes, foreign keys, and unique photo file/order indexes. Deploy using the existing API migration process before serving the updated application. Development startup can apply migrations through the existing `Database:ApplyMigrations` setting.

From the repository root:

```powershell
dotnet test apps/api/HooviePack.slnx -p:StartDebugDependencies=false
```

Set `HOOVIEPACK_TEST_POSTGRES` to a local test connection string to include isolated-schema PostgreSQL tests. These verify migrations, historical range queries, photo persistence, and cascade deletion without changing application tables.

From `apps/web`:

```powershell
npm test
npm run build
npx playwright test calendar.spec.mjs
```

If using installed Edge instead of Playwright Chromium, set `PLAYWRIGHT_CHANNEL=msedge`. Calendar browser tests exercise the actual Angular components with mocked HTTP boundaries; file-service behavior is covered by the .NET suite. Recurrence, reminders, external calendar integrations, comments, and other later-phase features are excluded.
