# Movie Link

Link two actors through the movies they made together, in as few movies as possible. Fewer links always win; time breaks ties. Built for Skillz head-to-head play, with a browser build for testing.

Build plan: [Movie Link Game: Web-first Build Plan](https://claude.ai/artifact/25K6BrQo83hg4ZPreCUJFw).

## What's here

| Folder | What it is |
| --- | --- |
| `shared/MovieLink.Core` | Chain rules, scoring and wire DTOs. .NET Standard 2.1, compiled by the server **and** by Unity (as a local package). |
| `shared/MovieLink.Core.Tests` | Golden tests from JSON fixtures: every chain rule and scoring edge case. |
| `server/src/MovieLink.Api` | ASP.NET Core minimal API: the referee for puzzles, search, steps, scoring. |
| `server/src/MovieLink.Data` | Postgres schema (versioned datasets, frozen puzzle pools, match audit) and Dapper repositories. |
| `tools/MovieLink.Tools` | Offline jobs: Wikidata fetch, ingest, puzzle generator (BFS shortest paths), coverage sample. |
| `tools/fixtures` | Hand-typed dev dataset of 70 famous films, used until the real Wikidata ingest runs. |
| `client/` | Unity 2022.3 LTS project: main screen, match, results, Settings/Account placeholders. |
| `infra/` | docker-compose (Postgres + API) and the API Dockerfile. |
| `docs/` | Testing checklists, asset license register, decisions. |

## Run it locally

Needs .NET 8 SDK and Postgres 16 (or Docker).

```bash
# 1. Database (or: docker compose -f infra/docker-compose.yml up db)
createuser -s movielink; createdb -O movielink movielink   # password: movielink

# 2. Load the dev dataset and build a puzzle pool
dotnet run --project tools/MovieLink.Tools -- ingest --file tools/fixtures/seed-dataset.json
dotnet run --project tools/MovieLink.Tools -- puzzlegen --tier 1 --top-actors 200 --min-paths 1 --publish

# 3. API on http://localhost:5080
dotnet run --project server/src/MovieLink.Api

# 4. Tests (API tests create and drop a throwaway database)
dotnet test MovieLink.sln
```

### Real data from Wikidata

```bash
export WIKIDATA_USER_AGENT="MovieLinkIngest/0.1 (you@example.com)"   # Wikidata asks bots for a contact
dotnet run --project tools/MovieLink.Tools -- wikidata-fetch --from 1970 --to 2025 --out data/wikidata.json
dotnet run --project tools/MovieLink.Tools -- ingest --file data/wikidata.json
dotnet run --project tools/MovieLink.Tools -- puzzlegen --tier 1 --count 500 --publish
dotnet run --project tools/MovieLink.Tools -- coverage-sample --version 2 --top 300 --out data/coverage.csv
```

Each ingest creates a new dataset version; publishing a pool freezes that version, so opponents playing days apart always see the same credits.

## Unity client

1. Install Unity **2022.3 LTS** (pinned by the plan; Skillz requires 2021.3 or 2022.3).
2. Unity Hub > Add > select the `client/` folder. Unity generates `ProjectSettings/` on first open; commit it after.
3. Menu **MovieLink > Create Main Scene**, then press Play. `GameApp` starts itself in any scene.
4. By default it runs offline against `FakeGameApi` (Tier 1 testing). To use the server, edit `client/Assets/_Game/Resources/movielink-config.json`: `"useFakeApi": false`, `"apiBaseUrl": "http://localhost:5080"`.
5. **MovieLink > Build Dev WebGL** builds to `client/Builds/WebGL` with Skillz's recommended WebGL settings.

Skillz: the SDK is not imported yet. `SkillzPlatform` compiles only when `SKILLZ_SDK` is defined, so the web build runs without it. Game code only talks to `IMatchPlatform`.

## API

All match calls after creation send `X-Match-Token`.

| Method | Path | Does |
| --- | --- | --- |
| POST | `/api/matches` | `{platform, playerId, tier, puzzleSeed?}` → puzzle, match id, token. Same `puzzleSeed` = same puzzle. |
| GET | `/api/matches/{id}/search?kind=actor\|movie&q=` | Autocomplete over the match's dataset version. |
| POST | `/api/matches/{id}/steps` | `{kind, id}` → validated chain state. |
| POST | `/api/matches/{id}/undo` | Removes the last step. |
| POST | `/api/matches/{id}/submit` | Authoritative score. Idempotent. |

Score = `1000 × (maxLinks + 1 − links) + floor(999 × (timeLimit − t) / timeLimit)`, 0 if incomplete or out of time.
