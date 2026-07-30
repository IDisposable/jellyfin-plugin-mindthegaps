# 18. Read personal watchlists over the sites' own APIs

Status: Accepted.

## Context

The strongest signal of what someone wants is the list they already keep: an IMDb watchlist, a JustWatch
watchlist. Both are more deliberate than any recommendation the plugin can compute, so both belong in the
Discover tab beside the TMDB, MDBList, and Trakt lists.

Neither site publishes an API for it. IMDb offers a browser CSV export and nothing else, and puts an AWS WAF
challenge in front of the watchlist page, so page scraping does not work even as a fallback. JustWatch's
documented Content Partner API covers streaming availability only, is contract-gated, and says nothing about
accounts. What both sites do have is the GraphQL endpoint their own web app reads from.

## Decision

Read those endpoints, and only them: `https://api.graphql.imdb.com/` and
`https://apis.justwatch.com/graphql`. No HTML parsing, no CSV import, no third-party mirror.

Both have introspection disabled, so the queries were derived from the validation errors, which still name
fields, arguments, and enum values. The two clients (`ImdbClient`, `JustWatchClient`) post through the shared
`CachedApiClient.PostJsonAsync`, so they inherit the plugin's cache, pacing, retry, and circuit breaker
unchanged. Each caches for 10 minutes rather than the usual 12 hours, because a watchlist is edited between
scans. The sources are `ImdbListGapSource` and `JustWatchListGapSource`, ordinary `IGapSource`s emitting
Recommendation gaps keyed on the ids the lists already carry.

Access differs, and the settings page says so plainly:

- IMDb needs no key, only a client-name header any value satisfies, and serves whatever the account has
  published. A private list answers "permission denied" and is skipped with a warning.
- JustWatch rejects the query without a bearer token even for the guest role. There is no key to issue, so
  the user pastes the token from a signed-in browser session. It expires; the source logs and carries on.

### IMDb people lists

IMDb types its lists (`listType` is `TITLES` or `PEOPLE`) and a list item is a union, so the one query spreads
both the Title and the Name fragment. That makes a second source almost free, and it fills a hole nothing else
covered: `PeopleGapSource` and the Trakt cross-check both seed Creator works from a person already attached to
an owned item, so there was no way to follow a director you own nothing by. `ImdbPeopleListGapSource` reads the
`PEOPLE` lists out of the same id field, resolves each `nm` id to a TMDB person id
(`TmdbClient.FindPersonByImdbIdAsync`, cached 30 days since an IMDb id never changes), and hands it to the same
`FilmographyGapMapper` an owned person goes through, so the vote floor, the billing limit, and the domain split
all apply unchanged. It has its own toggle because one people list is many filmographies.

### The other want-lists

Once IMDb and JustWatch were in, the same question was worth asking of every service the plugin already talks
to. Probing each with a real credential settled it:

| Service | Want-list | Credential | Built |
|---|---|---|---|
| OpenLibrary | "Want to Read" shelf | none, public shelf | yes |
| Discogs | wantlist | the existing token, plus a username | yes |
| MDBList | account watchlist | the existing API key | yes |
| TheTVDB | favorite series | the existing key, plus a subscriber PIN | yes |
| Trakt | watchlist | the existing client id, plus a username | yes |
| TMDB | watchlist and favorites | the user's **own** api key, plus a session | yes |

TheTVDB was the surprise: `/user/favorites` is account data and a key-only token is refused, but a token minted
with a subscriber PIN reads it, and the same token still serves the catalog, so one login path covers both.

Trakt needed a second look. The first probe failed with a bare `Forbidden` on every endpoint, which was read as
the development environment being blocked; it was actually a revoked client id, because Trakt has since made
API apps a VIP feature. With a live client id, `GET /users/{id}/watchlist/movies,shows` answers 200 for a public
profile, no OAuth: the watchlist is as readable as the lists the plugin already reads. The entries are the same
shape a list's are, so `TraktListMapper` serves both. One rough edge is Trakt's, not ours: an empty watchlist, a
private profile, and a username that does not exist all answer 200 with an empty array, and the documented
`X-Private-User` header is not sent, so the source can only report the count it read, never why it read nothing.

### TMDB, and why it needs the user's own key

TMDB is the one service here with a documented, supported account API, and the only reason it looked hard is
the word OAuth. It needs no public address: the approval URL takes an optional `redirect_to`, and omitting it
means there is no callback at all. The user approves in their own browser, comes back to the dashboard, and
the plugin exchanges the token server to server. Every call is outbound. TMDbLib already ships every method
required, and a TMDB session id does not expire, so it is a one-time setup rather than a token to re-paste.

The constraint that does matter is not technical. A TMDB session belongs to the **application** whose api key
minted it, and the key `TmdbClient` falls back to is Jellyfin's own: `4219e299c89411838049ab0dab19ebd5`, a
copy of the constant in the server's `MediaBrowser.Providers/Plugins/Tmdb/TmdbUtils.cs`, registered to the
Jellyfin project and shared by every install (it replaced an inherited Emby key in jellyfin/jellyfin#540, so
it has been rotated once already). Reading the public catalog through it is what it is published for. Minting
account sessions through it would put write-capable credentials for our users under a third party's
application, and would break every user at once if that key were rotated again.

So `TmdbAccountClient` never falls back: no own key, no account features, enforced in the client rather than
only in the toggle. It also builds its own `TMDbClient` instead of sharing `TmdbClient`'s, which is created
once at startup and would otherwise still hold the default key for a user who entered theirs afterwards, and
mint the session against the wrong application without saying so.

## Consequences

- Neither endpoint is contractual. Both clients treat a null payload as "could not read" rather than "empty",
  so an outage or a schema change leaves the previous report's gaps alone instead of clearing them.
- IMDb validates `userId` as the classic `ur...` form. The pseudonymous `p....` ids in today's imdb.com
  profile addresses are not accepted at all, so `ImdbListInput` rejects them at entry, where the message can
  explain where to find a usable id, rather than at fetch time on every scan.
- IMDb's responses carry a disclaimer limiting the data to non-commercial use, which is what this is.
- The JustWatch token is a credential with no rotation story. It is stored like the other secrets, masked in
  the settings form, and never logged.
- Neither source is chip-pickable. An explore id is an `int`; an IMDb id is a zero-padded string, and the
  JustWatch lists are the account's fixed two, so a chip would address the wrong thing or nothing.
- A JustWatch watchlist read cannot be captured for a fixture without a token, so that one test fixture wraps
  real captured nodes in a hand-written envelope (see ADR-0006 for the rule this bends).
- A people list costs two cached TMDB calls per person and yields a whole filmography each, so its cap counts
  people (50 per list per scan), not gaps. A creator muted from the report is keyed on the synthetic owner id
  (`imdbperson-nm...`), so muting works the same as it does for a person the library owns.
- JustWatch has no equivalent. Its list union holds no `Person`, and there is no person-list query, so the
  people idea is IMDb-only.
