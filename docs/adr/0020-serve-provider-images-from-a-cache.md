# 20. Serve provider images from a cache on the server, behind a fixed allowlist

Status: Accepted.

## Context

Every poster, cover and streaming-service logo on the report and the web UI surfaces is loaded by the browser
straight from its provider (TMDB, Cover Art Archive, OpenLibrary and others). A report of thousands of rows asks
the same providers for the same files from every browser that opens it, hands each provider the address of every
viewer, and depends on each provider being up when the page loads.

None of it is specific to a user or to the library, so it can be kept once on the server and served with public
cache headers. The obstacle is that an image tag cannot send the auth header, so the route that serves it has to
be anonymous, and an anonymous route that fetches an address it is given is a way to make the server request
anything.

## Decision

`GET MindTheGaps/Image?u=<address>` serves an image from a cache under the server's cache path. It fetches an
address only when `ImageHosts` allows it: https on the default port, no credentials in it, and a host that is a
listed provider domain or a subdomain of one. Where a redirect ended is checked the same way. It keeps only what
the first bytes name a JPEG, PNG, GIF, WebP or AVIF (SVG is refused, since served from the server's own origin
it could run script), at most 5 MB each.

Files are named by a hash of the address, so a changed source is a new file and nothing needs invalidating. The
server's daily task already deletes cache files not written for thirty days and sets no size cap, so the plugin
adds one (500 MB by default, `ImageCacheMaxMegabytes`). A file's age is when it was fetched and serving it does not
change that, so the sweep also refreshes what is kept: an image is deleted a month after it was fetched and the next
request fetches it again, which bounds how long an image a provider has replaced at the same address stays stale.
The tag sent with it carries that fetch time as well as the address, so a browser holding the old copy replaces it.
A response that says `Cache-Control: no-store` or `private` is not kept; the provider's other caching rules are not
read, since what each states is longer than the sweep's month. The cap is enforced by a scheduled task (`ImageCacheTrimTask`, at startup and daily), which
deletes the oldest files down to nine tenths of it, and never by a request: serving an image must not wait on a
pass over the folder. Between runs the cache can grow, so past twice the cap (judged against the folder's size as
the last trim measured it, plus what has been stored since) nothing more is fetched until the next trim.

The request path waits on nothing but its own fetch, and gives up the moment an image cannot be had at once. One
fetch is shared by every caller asking for the same image, at most six run together and a seventh does not queue,
a failed address is remembered for thirty minutes, and a host that fails five fetches in a row (a refusal, a
server error, a timeout, not a missing file) is left alone for ten minutes, on a list kept in memory only. In each
of those cases, and when the cache is off or a file has gone from the folder since it was found, the route answers
a temporary redirect to the provider's own address, so a provider that has blocked the server still shows its
images to a browser that can reach it.

The cache is never load-bearing. The report's images carry the provider's address beside the server's and load
it when the server's fails; a card's background image is probed the same way. A host the list does not know
(which the route answers 404, since it will not redirect to an address it does not allow), a file the cache will
not keep, or a server that cannot be reached therefore shows the same image it always did. `ImageCacheEnabled` is
off by default, since it spends the server's bandwidth and disk on what a browser does for itself today.

## Consequences

The route is open to anyone who can reach the server, and what it can be made to do is bounded by the list, the
size limits, the parallelism cap and the total cap. A source that takes its image address from a new host has to
add that host to `ImageHosts`; until it does, its images load direct, and `ImageHostsTests` pins the hosts the
clients build addresses on.

The cache costs the server the bandwidth and the disk it saves each browser. Only the thumbnails the plugin's own
pages show go through it, not Jellyfin's images.
