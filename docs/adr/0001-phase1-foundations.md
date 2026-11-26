# ADR 0001: Phase 1 foundations

Date: 2026-11-26

- **Ids** are the numeric part of the Wikidata QID, so they stay stable across dataset versions. Dev fixture ids start at 900000000.
- **Dataset versions are immutable.** Ingest always creates a new version; `credits_overrides` is re-applied on each ingest. Publishing a pool freezes its version, and a pool that has been played can't be regenerated.
- **Puzzle choice** = `puzzleSeed mod poolSize` over the published pool for the tier. Retired puzzles keep their slot, so indices never shift mid-tournament.
- **Match state lives in Postgres**, not memory. The API is stateless; the HMAC token binds match, player, puzzle and start time.
- **Server clock scores.** Time stops at the step that completed the chain, minus 150 ms per accepted request (capped at 3 s) for latency.
- **Search** normalises text in C# (`TextNormalizer`: accents, punctuation, leading "The") into `search_name` / `search_title` columns with `pg_trgm` indexes, so it doesn't need the `unaccent` extension.
- **Popularity** is Wikidata sitelink count for now. Wikipedia page views are the planned upgrade.
- **Redis** is deferred to Phase 2; per-step checks load only the chain's movies' credits from Postgres.
- **Unity shares Core as a local UPM package** (`file:../../shared/MovieLink.Core`). `Directory.Build.props` sends dotnet build output to `/artifacts`, so no `bin/` or `obj/` lands inside the package.
