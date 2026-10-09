# Storage: JSON files or SQLite?

> Evaluation of moving the plugin's JSON stores to a SQLite database, measured against a real install's stores
> (82,958 gaps). The plan it recommends is on the [roadmap](roadmap.md#storage).

## What is stored, and how often it is written

| Store                                    | Real size              | Written when                                                                                                                       |
| ---------------------------------------- | ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Gap report, `gaps-{domain}.json` + meta  | 245 MB (movies 212 MB) | Every scan (whole), every 5 s during a scan and an availability pass (whole), every verify, re-check swap and explore (one domain) |
| `scan-cursors.json`                      | 4.8 MB                 | When a rotating source finishes its run                                                                                            |
| `resolutions.json`                       | 174 KB                 | On a resolve, snooze or clear                                                                                                      |
| `scan-durations.json`                    | 0.6 KB                 | Once per scan                                                                                                                      |
| `watchlists/`, `notinterested/` per user | 62 KB, 16 KB           | On a user's add or remove                                                                                                          |

Only the gap report is big or written often. Everything else is small and written on a user's click, which a
whole-file rewrite handles in a millisecond or two.

## What the real report costs (movies domain, 60,954 gaps)

The movies file is two thirds availability: 449,388 offers, about 11 per gap with offers. Those offers carry only
344 distinct (service, monetization, quality) combinations and 262 distinct logos, and every offer of a gap repeats
the same TMDB watch URL, so most of those bytes say the same thing again.

| Operation                                   | Today (string I/O) | Stream I/O | Stream, compact JSON | Stream, compact, offers without URLs | SQLite, one row per gap |
| ------------------------------------------- | ------------------ | ---------- | -------------------- | ------------------------------------ | ----------------------- |
| Write the movies file                       | 1,735 ms           | 466 ms     | 427 ms               | 318 ms                               | (whole report) 4,183 ms |
| Heap left behind by that write              | 512 MB             | 0          | 0                    | 0                                    |                         |
| Movies file on disk                         | 161 MB             | 161 MB     | 123 MB               | 84 MB                                | (whole report) 271 MB   |
| Read the movies file                        | 1,814 ms           | 1,694 ms   |                      | 481 ms                               | (all rows) 209 ms raw   |
| Heap while loaded                           | 1,338 MB           | 314 MB     |                      |                                      |                         |
| Heap left behind by that read               | 1,024 MB           | 0          |                      |                                      |                         |
| Remove one gap (rewrites the movies file)   | 749 ms             |            |                      |                                      | 2.6 ms                  |
| Mid-scan checkpoint (rewrites every domain) | 931 ms             |            |                      |                                      | new rows only           |
| Update 200 gaps (one availability batch)    | a whole rewrite    |            |                      |                                      | 1 ms                    |
| Find one gap by id                          | 3.7 ms (scan)      |            |                      |                                      | 0.7 ms                  |

The SQLite figures store each gap's compact JSON in a row (`id` primary key, `domain` indexed, WAL mode,
`synchronous=NORMAL`), with the offers' URLs written out in full. The raw read is the row strings only, before
any deserialization, which a store keeping today's in-memory model would still pay.

## Findings

1. **The memory problem is string I/O, not JSON.** Reading a domain file with `File.ReadAllText` and
   deserializing the string held 1.3 GB while loading and left 1 GB in the serializer's pooled buffers afterwards;
   each whole-report write left another 512 MB. Reading and writing through a stream holds 314 MB for the same
   movies data and leaves nothing. This is fixed in the working tree (`GapReportFiles.ReadJson`/`WriteJson`).
2. **The disk and write-time problem is mostly the offers.** Dropping the two repeated URLs from each offer, and
   writing unindented, halves the movies file and makes a write about five times faster than today.
3. **The latency problem is whole-file rewrites.** A verify, a re-check swap, and an explore each rewrite a whole
   domain file (about 0.75 s for movies before the fixes above), and a bulk re-check does that once per set it
   swaps. During a scan, the five-second checkpoint rewrites every domain each time, which on this install is a
   200 MB write roughly every five seconds for the length of the scan. A row store makes each of these a few rows.
4. **SQLite is slower and bigger for a whole save.** Writing every gap in one transaction took 4.2 s against
   0.4 s for the streamed compact JSON, and the file was 271 MB against 123 MB. A full scan save gets slower.
5. **Reads would not get faster on their own.** Every read path (the per-domain snapshot, the summary facts,
   `FindById`, the scan's carry-forward) works on the in-memory report. SQLite only speeds reads if those move to
   queries, which is a rewrite of the store's whole read side and of the engine's carry-forward, not a storage
   swap.

## SQLite: for and against

For:

- An edit costs a few rows, not a domain file: verify, re-check swaps, explore merges, availability batches,
  and mid-scan checkpoints (which would only insert what is new).
- Crash safety comes from the database rather than temp-then-replace per file, and a mid-scan crash loses at most
  the uncommitted rows.
- A path to querying without holding the report in memory, if the read side is ever rewritten to use it.

Against:

- **A dependency on the host's SQLite.** The host ships `Microsoft.Data.Sqlite` (9.0.x on the 10.11 line, 10.0.x
  on 12.0) and its native `e_sqlite3`. The plugin would reference it compile-only, pinned per ABI from
  `JellyfinVersion` like the Jellyfin packages, and use the host's at runtime. That couples the plugin to a host
  implementation detail core does not promise, and the test project would need it copy-local. (The benchmark's
  restore flagged a high-severity advisory on `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which is the host's to update,
  but the plugin would inherit whatever the host ships.)
- **A whole save gets slower and the file bigger** (finding 4), and a scan ends with a whole save.
- **A schema to migrate**, alongside the contracts the JSON already carries (ADR-0008 ids, ADR-0021 tokens).
- **Harder to inspect and support.** Today a zip of the data folder is readable in any editor; a database needs
  a tool, and a copy taken while the server runs needs a WAL checkpoint first to be complete.
- **Backups.** The server's backup copies the data folder; a live WAL database copied mid-write is not
  guaranteed consistent, where a JSON file is replaced atomically.
- **No memory win** unless the read side is rewritten too (finding 5).

## Recommendation

Do not move the small stores: they are tiny, written on a click, and their readability is worth more than
anything a database would save.

For the gap report, take the cheap fixes first and decide on SQLite with them measured:

1. Stream I/O (done): about 1 GB less memory held, writes about four times faster.
2. Leaner offers (done) and unindented files: half the disk, about five times faster writes than today.
3. Write less often (done): the writes nothing waits on (scan checkpoints, availability saves, a bulk re-check's
   swaps) are paced by `WriteThrottle`, due after the longer of two minutes and twenty times the last write, with
   a forced write when a source finishes and when a batch ends. On this report that is about 90 timed writes in a
   three-hour scan instead of about 2,160.

With the first two in place, the real report measured: files 131 MB (movies 102 MB, from 164 MB), cold load
1.26 s (from 2.96 s), 205 MB of heap held for all 82,958 gaps (about 2.6 KB a gap, from about 1.6 GB), and a
one-gap removal 437 ms (from 749 ms). Every one of the 497,504 offers kept its page, and every one of the 496,148
that had a logo kept it.

If a verify or a re-check still feels slow after that, SQLite for the gap report alone is the next step, as a
storage swap that keeps the in-memory model (writes become row deltas, reads are unchanged). Moving the reads to
queries is a separate, larger decision, worth it only if holding the report in memory becomes the problem.
