# 6. Test parsers against real captured API responses

Status: Accepted.

## Context

Several sources hand-roll HTTP and JSON parsing (TVmaze, TheTVDB, Trakt, TMDB availability), and the
rest map provider DTOs into `GapItem`s. The real risk in all of them is drift from the actual API
shapes, which a synthetic test would not catch.

## Decision

Extract each source's parsing and mapping into a pure, public mapper, then test those mappers against
real responses captured from the live APIs and stored as fixtures under `TestData/`. TMDB fixtures are
deserialized through TMDbLib (Newtonsoft) exactly as the runtime does. The Trakt API is gated behind a
per-user client id, so its captured-data test is skipped with the capture command in its header until
a fixture is supplied.

## Consequences

- Parsing and mapping are validated against genuine shapes, offline and deterministically in CI.
- Fixtures are a snapshot: if an API changes shape, the relevant fixture must be recaptured. The
  capture commands live with the tests so that is a one-liner.
- Library-mutating code (the minter) is not unit-tested for the same reason the live HTTP clients are
  not: it needs a running server, not a fixture.
