# Mind the Gaps: HTTP API

Every route the plugin serves, under `/MindTheGaps/` on the Jellyfin server. Calls authenticate the way any
Jellyfin API call does (the `Authorization: MediaBrowser Token="..."` header, or an API key), so the plugin
needs nothing of its own. For what each setting does, see the [configuration reference](configuration.md).

How to read the tables:

- **Who**: _User_ is any signed-in user. _Admin_ is an administrator (Jellyfin's `RequiresElevation`
  policy). _Anyone_ needs no sign-in at all, because a browser fetches it from a tag that cannot send the
  header.
- **Switched by**: the setting that turns the route on. While it is off, the route answers 404, so a change
  takes effect on the next request with no restart.
- **Adding and removing** follow one pattern: a `POST` to the bare noun adds (`.../Todo`,
  `.../NotInterested`) and a `POST` to `.../Remove` takes it off again.
- **A gap is never trusted from the client.** A route that takes a `gapId` finds the gap again from the
  same lookup the page listed it from (or from the stored report), so a client can only act on what that
  page or report actually shows.
- **Your own lists.** Want to watch and not interested are each user's own, so a request with an API key,
  which carries no user, gets 403 on the routes that change them. Not interested on the injected pages hides
  a title from the caller alone, an administrator included; the report's own resolutions are the
  server-wide dismissal.

## The injected pages

What the Web UI script shows on jellyfin-web's own pages. Any other client can use these too, whether or not
the script is added. A page is answered only for a user who can see the item it is about, and a user with a
parental rating limit gets only the titles that limit allows.

| Verb | Route                                      | Parameters          | Who    | Switched by                        | Description                                                                                                                          |
| ---- | ------------------------------------------ | ------------------- | ------ | ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| GET  | `Person/{personId}/Missing`                |                     | User   | Person pages                       | The movies and series a person is credited on that the library does not hold.                                                        |
| POST | `Person/{personId}/Todo`                   | `gapId`             | User   | Person pages, Want to watch        | Puts one of those titles on the caller's want-to-watch list.                                                                         |
| POST | `Person/{personId}/Todo/Remove`            | `gapId`             | User   | Person pages, Want to watch        | Takes it off, by title, however it reached the list.                                                                                 |
| POST | `Person/{personId}/NotInterested`          | `gapId`             | User   | Person pages, Not interested       | Hides the title from the caller on every page.                                                                                       |
| POST | `Person/{personId}/NotInterested/Remove`   | `gapId`             | User   | Person pages, Not interested       | Shows it to the caller again.                                                                                                        |
| GET  | `Item/{itemId}/WantedOrRelated`            |                     | User   | Item pages, or the bookmark button | For a movie or series page: its similar titles the library lacks (`Related`), and whether it is on the caller's playlist (`OnList`). |
| POST | `Item/{itemId}/Wanted`                     |                     | User   | Want to watch: bookmark button     | Puts the owned movie or series on the caller's want-to-watch playlist.                                                               |
| POST | `Item/{itemId}/Wanted/Remove`              |                     | User   | Want to watch: bookmark button     | Takes it off the playlist.                                                                                                           |
| POST | `Item/{itemId}/Todo`                       | `gapId`             | User   | Item pages, Want to watch          | Puts one of the similar titles on the caller's want-to-watch list.                                                                   |
| POST | `Item/{itemId}/Todo/Remove`                | `gapId`             | User   | Item pages, Want to watch          | Takes it off.                                                                                                                        |
| POST | `Item/{itemId}/NotInterested`              | `gapId`             | User   | Item pages, Not interested         | Hides one of the similar titles from the caller.                                                                                     |
| POST | `Item/{itemId}/NotInterested/Remove`       | `gapId`             | User   | Item pages, Not interested         | Shows it to the caller again.                                                                                                        |
| GET  | `Item/{itemId}/Works`                      |                     | User   | Item pages                         | An artist's albums, or a book author's other works, that the library lacks. Also answers an author's own page.                       |
| GET  | `Item/{itemId}/Works/Detail`               | `gapId`             | User   | Item pages                         | One work's dialog detail: a book's description from OpenLibrary; nothing extra for an album.                                         |
| POST | `Item/{itemId}/Works/Todo`                 | `gapId`             | User   | Item pages, Want to watch          | Puts a work on the caller's want-to-watch list.                                                                                      |
| POST | `Item/{itemId}/Works/Todo/Remove`          | `gapId`             | User   | Item pages, Want to watch          | Takes it off.                                                                                                                        |
| POST | `Item/{itemId}/Works/NotInterested`        | `gapId`             | User   | Item pages, Not interested         | Hides a work from the caller.                                                                                                        |
| POST | `Item/{itemId}/Works/NotInterested/Remove` | `gapId`             | User   | Item pages, Not interested         | Shows it to the caller again.                                                                                                        |
| GET  | `Studio/{studioId}/Missing`                |                     | User   | Studio pages                       | A movie studio's movies the library lacks, found by matching the studio's name on TMDB.                                              |
| POST | `Studio/{studioId}/Todo`                   | `gapId`             | User   | Studio pages, Want to watch        | Puts one of them on the caller's want-to-watch list.                                                                                 |
| POST | `Studio/{studioId}/Todo/Remove`            | `gapId`             | User   | Studio pages, Want to watch        | Takes it off.                                                                                                                        |
| POST | `Studio/{studioId}/NotInterested`          | `gapId`             | User   | Studio pages, Not interested       | Hides one of them from the caller.                                                                                                   |
| POST | `Studio/{studioId}/NotInterested/Remove`   | `gapId`             | User   | Studio pages, Not interested       | Shows it to the caller again.                                                                                                        |
| GET  | `Home/Discover`                            | `limit`             | User   | Home Discover row                  | The recommendations the scan accumulated, ranked, less what the caller hid.                                                          |
| POST | `Home/Todo`                                | `gapId`             | User   | Home Discover row, Want to watch   | Puts a recommendation on the caller's want-to-watch list.                                                                            |
| POST | `Home/Todo/Remove`                         | `gapId`             | User   | Home Discover row, Want to watch   | Takes it off.                                                                                                                        |
| POST | `Home/NotInterested`                       | `gapId`             | User   | Home Discover row, Not interested  | Hides a recommendation from the caller.                                                                                              |
| POST | `Home/NotInterested/Remove`                | `gapId`             | User   | Home Discover row, Not interested  | Shows it to the caller again.                                                                                                        |
| GET  | `Home/Wanted`                              | `limit`             | User   | Want to watch row                  | The caller's want-to-watch list: what is still missing, and with "include owned titles" on, what is on their playlist.               |
| POST | `Home/Wanted/Remove`                       | `gapId` or `itemId` | User   | Want to watch                      | Takes a title off the row: a missing one off the list by `gapId`, an owned one off the playlist by `itemId`.                         |
| GET  | `Home/Search`                              | `kind`, `q`         | User   | Want to watch                      | Searches TMDB for an unowned movie or series (`kind` is `Movie` or `Series`) to add directly.                                        |
| POST | `Home/Search/Todo`                         | `kind`, `tmdbId`    | User   | Want to watch                      | Puts a search result on the caller's want-to-watch list, looked up again on TMDB.                                                    |
| POST | `Home/Search/Todo/Remove`                  | `kind`, `tmdbId`    | User   | Want to watch                      | Takes it off.                                                                                                                        |
| GET  | `WebUi/NotInterested`                      |                     | User   | Not interested                     | The caller's own list of hidden titles, most recent first.                                                                           |
| POST | `WebUi/NotInterested/Restore`              | `id`                | User   | Not interested                     | Shows one title from that list again.                                                                                                |
| POST | `WebUi/NotInterested/Clear`                |                     | User   | Not interested                     | Empties the caller's list, so every title shows again. Answers how many were removed.                                                |
| GET  | `WebUi/Detail`                             | `tmdbId`, `kind`    | User   | Always on                          | A title's TMDB detail for the card dialog: synopsis, genres, runtime, rating, trailer, IMDb and JustWatch links.                     |
| GET  | `WebUi/client.js`                          |                     | Anyone | Show the surfaces in Jellyfin Web  | The script the plugin adds to jellyfin-web's `index.html`.                                                                           |

Each surface's response carries `CanTodo` and `CanHide` (whether the caller may keep a want-to-watch list,
and a not-interested one) and `NotInterestedCount` (how many titles they have hidden), and the injected
rows carry a `Placement` saying where the script puts them.

## The report and settings

What the plugin's own dashboard pages use. Administrators only.

| Verb | Route                     | Parameters                                  | Who   | Switched by    | Description                                                                                                                    |
| ---- | ------------------------- | ------------------------------------------- | ----- | -------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| GET  | `Summary`                 |                                             | Admin |                | The report's counts per domain and pattern, and the vocabulary the dashboard builds its tabs from. Answers 304 when unchanged. |
| GET  | `Gaps`                    | `pattern`, `domain`, `full`                 | Admin |                | One tab's gaps, as lean rows; `full=true` returns complete gaps, for the Markdown export. Answers 304 when unchanged.          |
| GET  | `GapDetail`               | `id`                                        | Admin |                | One gap's overview, links and offers, fetched when its row is first opened.                                                    |
| POST | `Scan`                    |                                             | Admin |                | Starts a scan in the background.                                                                                               |
| GET  | `ScanStatus`              |                                             | Admin |                | The running scan's progress.                                                                                                   |
| POST | `ResetScanRotation`       |                                             | Admin |                | Forgets which owned items the rotating sources covered, so the next scans start over.                                          |
| POST | `PruneStaleGaps`          |                                             | Admin |                | Removes gaps whose list, id or account is no longer configured. Answers how many.                                              |
| POST | `Verify`                  | body: gap ids                               | Admin |                | Checks gaps against the library and removes every gap about a title now owned.                                                 |
| POST | `RecheckSources`          | body: owner ids                             | Admin |                | Re-checks sets and series with their providers in the background.                                                              |
| GET  | `RecheckStatus`           |                                             | Admin |                | That re-check's progress.                                                                                                      |
| POST | `Explore`                 | `kind`, `ids`                               | Admin |                | Runs one source ad hoc over the given ids, in the background.                                                                  |
| GET  | `Explore/Status`          |                                             | Admin |                | That run's progress.                                                                                                           |
| POST | `Explore/Clear`           | `source`                                    | Admin |                | Removes the gaps an ad hoc run added.                                                                                          |
| GET  | `Explore/Kinds`           |                                             | Admin |                | The sources that can be explored.                                                                                              |
| GET  | `CuratedSearch`           | `kind`, `query`                             | Admin |                | Type-ahead search for a studio, keyword, label, list or subject, for the settings page's pickers.                              |
| GET  | `CuratedResolve`          | `kind`, `ids`                               | Admin |                | Names for stored ids, for the same pickers.                                                                                    |
| GET  | `Resolutions`             |                                             | Admin |                | Every gap resolved on the report, with its status and note.                                                                    |
| POST | `Resolve`                 | body: `Id`, `Note`, `Kind`, `SnoozedUntil`  | Admin |                | Resolves one gap for everyone: not really missing, not interested, or snoozed.                                                 |
| POST | `ResolveBatch`            | body: `Ids`, `Note`, `Kind`                 | Admin |                | Resolves several gaps with one note.                                                                                           |
| POST | `Unresolve`               | `id`                                        | Admin |                | Clears a gap's resolution.                                                                                                     |
| GET  | `Diagnose`                | `id`, `deeper`                              | Admin |                | Explains why one gap is reported missing.                                                                                      |
| GET  | `DiagnoseAudit`           | `domain`, `pattern`                         | Admin |                | The same checks across the library, for the Markdown audit.                                                                    |
| GET  | `Availability`            | `targetKind`, `tmdbId`, `imdbId`, `country` | Admin | Where to watch | Where one title streams, looked up on demand.                                                                                  |
| POST | `Availability/Enrich`     |                                             | Admin | Where to watch | Looks up where to watch for the whole report in the background.                                                                |
| GET  | `Availability/Status`     |                                             | Admin | Where to watch | That pass's progress.                                                                                                          |
| GET  | `Todo`                    |                                             | Admin |                | The caller's own TODO list.                                                                                                    |
| POST | `Todo`                    | body: gap ids                               | Admin |                | Adds report gaps to the caller's TODO list. Answers how many were new.                                                         |
| GET  | `Todo/All`                |                                             | Admin |                | Every user's list, each entry with its owner.                                                                                  |
| POST | `Todo/Remove`             | `id`, `userId`                              | Admin |                | Removes an entry; `userId` acts on another user's list.                                                                        |
| POST | `Todo/SetDone`            | `id`, `done`, `userId`                      | Admin |                | Marks an entry done or not.                                                                                                    |
| POST | `Todo/Verify`             | `id`, `userId`                              | Admin |                | Checks one entry against the library.                                                                                          |
| POST | `Todo/VerifyAll`          | `userId`                                    | Admin |                | Checks a whole list against the library, marking entries done or outstanding.                                                  |
| GET  | `Todo/Demand`             |                                             | Admin |                | The fulfillment queue: everyone's lists folded into one row per title.                                                         |
| POST | `Todo/Demand/MarkFetched` | body: `OwnerId`, `GapId` pairs              | Admin |                | Marks a title fetched for everyone waiting on it.                                                                              |
| GET  | `AcquisitionConfig`       |                                             | Admin |                | Which acquisition targets are configured, so the report knows which Send buttons to show.                                      |
| GET  | `QualityProfiles`         | `kind`                                      | Admin |                | Radarr's (`Movie`) or Sonarr's (`Series`) quality profiles, for the Send picker.                                               |
| POST | `SendToArr`               | `id`, `qualityProfileId`                    | Admin |                | Sends one gap to Radarr or Sonarr.                                                                                             |
| POST | `SendToArrBulk`           | body: gap ids                               | Admin |                | Sends several.                                                                                                                 |
| POST | `SendToSeerr`             | `id`                                        | Admin |                | Requests one gap through Jellyseerr or Overseerr.                                                                              |
| POST | `SendToSeerrBulk`         | body: gap ids                               | Admin |                | Requests several.                                                                                                              |
| POST | `MintGap`                 | `id`, `dryRun`                              | Admin |                | Mints a virtual placeholder for one gap.                                                                                       |
| POST | `MintGaps`                | `dryRun`, body: gap ids                     | Admin |                | Mints several, in the background.                                                                                              |
| GET  | `MintStatus`              |                                             | Admin |                | The minting pass's progress.                                                                                                   |
| POST | `RemoveMintedMovies`      | `dryRun`                                    | Admin |                | Removes every placeholder the plugin minted.                                                                                   |
| GET  | `Tmdb/AccountStatus`      |                                             | Admin |                | Whether a TMDB account is connected, and as whom.                                                                              |
| POST | `Tmdb/AccountConnect`     |                                             | Admin |                | Starts connecting a TMDB account: answers the address to approve it at.                                                        |
| POST | `Tmdb/AccountFinish`      | body: `RequestToken`                        | Admin |                | Finishes the connection once it has been approved.                                                                             |
| POST | `Tmdb/AccountDisconnect`  |                                             | Admin |                | Forgets the TMDB account.                                                                                                      |

## Files

Fetched by a tag rather than a script, so they need no sign-in.

| Verb | Route              | Parameters | Who    | Switched by | Description                                                                                                                                        |
| ---- | ------------------ | ---------- | ------ | ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| GET  | `Image`            | `u`        | Anyone |             | An image from a provider the plugin reads, served from the server's cache, or a redirect to the provider when the cache is off or cannot serve it. |
| GET  | `Dashboard/{name}` | `v`        | Anyone |             | The dashboard pages' stylesheet and scripts.                                                                                                       |
