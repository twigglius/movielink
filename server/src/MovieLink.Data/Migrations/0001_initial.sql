-- Movie graph, versioned. Ids are the numeric part of the Wikidata QID (Q38111 -> 38111)
-- so they stay stable across dataset versions. Dev fixtures use ids >= 900000000.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE TABLE dataset_versions (
    id           SERIAL PRIMARY KEY,
    source       TEXT NOT NULL,                 -- 'wikidata', 'fixture', later 'tmdb'
    notes        TEXT,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    frozen_at    TIMESTAMPTZ                    -- set once a puzzle pool is published from it
);

CREATE TABLE actors (
    dataset_version INT    NOT NULL REFERENCES dataset_versions(id) ON DELETE CASCADE,
    id              BIGINT NOT NULL,
    wikidata_id     TEXT,
    name            TEXT   NOT NULL,
    search_name     TEXT   NOT NULL,
    birth_year      INT,
    popularity      DOUBLE PRECISION NOT NULL DEFAULT 0,
    PRIMARY KEY (dataset_version, id)
);
CREATE INDEX actors_search_trgm ON actors USING gin (search_name gin_trgm_ops);
CREATE INDEX actors_popularity ON actors (dataset_version, popularity DESC);

CREATE TABLE movies (
    dataset_version INT    NOT NULL REFERENCES dataset_versions(id) ON DELETE CASCADE,
    id              BIGINT NOT NULL,
    wikidata_id     TEXT,
    title           TEXT   NOT NULL,
    search_title    TEXT   NOT NULL,
    year            INT,
    popularity      DOUBLE PRECISION NOT NULL DEFAULT 0,
    PRIMARY KEY (dataset_version, id)
);
CREATE INDEX movies_search_trgm ON movies USING gin (search_title gin_trgm_ops);

CREATE TABLE credits (
    dataset_version INT    NOT NULL,
    actor_id        BIGINT NOT NULL,
    movie_id        BIGINT NOT NULL,
    billing_order   INT,
    character_name  TEXT,
    PRIMARY KEY (dataset_version, actor_id, movie_id),
    FOREIGN KEY (dataset_version, actor_id) REFERENCES actors(dataset_version, id) ON DELETE CASCADE,
    FOREIGN KEY (dataset_version, movie_id) REFERENCES movies(dataset_version, id) ON DELETE CASCADE
);
CREATE INDEX credits_by_movie ON credits (dataset_version, movie_id);

-- Hand fixes applied on top of every ingest (missing or wrong credits found by players or curation).
CREATE TABLE credits_overrides (
    id           SERIAL PRIMARY KEY,
    actor_id     BIGINT NOT NULL,
    movie_id     BIGINT NOT NULL,
    action       TEXT   NOT NULL CHECK (action IN ('add', 'remove')),
    reason       TEXT,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (actor_id, movie_id)
);

-- Frozen puzzle pools: pool_index is what the shared Skillz random draw selects.
CREATE TABLE puzzles (
    id              BIGSERIAL PRIMARY KEY,
    dataset_version INT    NOT NULL REFERENCES dataset_versions(id),
    tier            INT    NOT NULL,
    pool_index      INT    NOT NULL,
    start_actor_id  BIGINT NOT NULL,
    target_actor_id BIGINT NOT NULL,
    optimal_links   INT    NOT NULL,
    shortest_path_count INT NOT NULL,
    example_path    BIGINT[] NOT NULL,          -- actor, movie, actor, ... ids
    status          TEXT   NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'retired', 'review')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (dataset_version, tier, pool_index)
);

CREATE TABLE puzzle_pools (
    tier            INT PRIMARY KEY,
    dataset_version INT NOT NULL REFERENCES dataset_versions(id),
    published_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE matches (
    id               UUID PRIMARY KEY,
    puzzle_id        BIGINT NOT NULL REFERENCES puzzles(id),
    platform         TEXT   NOT NULL,
    player_id        TEXT   NOT NULL,
    external_match_id TEXT,
    puzzle_seed      INT,
    started_at       TIMESTAMPTZ NOT NULL,
    finished_at      TIMESTAMPTZ,
    chain            BIGINT[] NOT NULL DEFAULT '{}',  -- start actor, movie, actor, ...
    step_times_ms    INT[]    NOT NULL DEFAULT '{}',  -- ms since start for each step after the start actor
    request_count    INT      NOT NULL DEFAULT 0,
    score            INT,
    links            INT,
    status           TEXT   NOT NULL DEFAULT 'playing' CHECK (status IN ('playing', 'submitted', 'expired')),
    flags            TEXT[] NOT NULL DEFAULT '{}'
);
CREATE INDEX matches_player ON matches (player_id, started_at DESC);
CREATE INDEX matches_flagged ON matches USING gin (flags);
