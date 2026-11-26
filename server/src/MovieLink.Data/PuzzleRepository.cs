using Dapper;

namespace MovieLink.Data;

public sealed class PuzzleRecord
{
    public long Id { get; set; }
    public int DatasetVersion { get; set; }
    public int Tier { get; set; }
    public int PoolIndex { get; set; }
    public long StartActorId { get; set; }
    public long TargetActorId { get; set; }
    public int OptimalLinks { get; set; }
    public int ShortestPathCount { get; set; }
    public long[] ExamplePath { get; set; } = Array.Empty<long>();
}

public sealed class PuzzleRepository
{
    private readonly Db _db;
    public PuzzleRepository(Db db) => _db = db;

    static PuzzleRepository() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    /// <summary>Writes a pool for (version, tier), replacing any unpublished pool of the same version and tier.</summary>
    public async Task WritePoolAsync(int datasetVersion, int tier, IReadOnlyList<PuzzleRecord> puzzles, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        bool used = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM matches m JOIN puzzles p ON p.id = m.puzzle_id WHERE p.dataset_version = @datasetVersion AND p.tier = @tier)",
            new { datasetVersion, tier }, tx);
        if (used) throw new InvalidOperationException($"Pool v{datasetVersion} tier {tier} has been played and is frozen. Ingest a new dataset version instead.");

        await conn.ExecuteAsync("DELETE FROM puzzles WHERE dataset_version = @datasetVersion AND tier = @tier", new { datasetVersion, tier }, tx);
        for (int i = 0; i < puzzles.Count; i++)
        {
            var p = puzzles[i];
            await conn.ExecuteAsync(@"
                INSERT INTO puzzles (dataset_version, tier, pool_index, start_actor_id, target_actor_id, optimal_links, shortest_path_count, example_path)
                VALUES (@datasetVersion, @tier, @i, @StartActorId, @TargetActorId, @OptimalLinks, @ShortestPathCount, @ExamplePath)",
                new { datasetVersion, tier, i, p.StartActorId, p.TargetActorId, p.OptimalLinks, p.ShortestPathCount, p.ExamplePath }, tx);
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>Makes a pool the live one for its tier and freezes its dataset version.</summary>
    public async Task PublishPoolAsync(int datasetVersion, int tier, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(@"
            INSERT INTO puzzle_pools (tier, dataset_version) VALUES (@tier, @datasetVersion)
            ON CONFLICT (tier) DO UPDATE SET dataset_version = EXCLUDED.dataset_version, published_at = now()",
            new { tier, datasetVersion }, tx);
        await conn.ExecuteAsync("UPDATE dataset_versions SET frozen_at = coalesce(frozen_at, now()) WHERE id = @datasetVersion", new { datasetVersion }, tx);
        await tx.CommitAsync(ct);
    }

    public async Task<int> ActivePoolSizeAsync(int tier, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(@"
            SELECT count(*) FROM puzzles p JOIN puzzle_pools pp ON pp.tier = p.tier AND pp.dataset_version = p.dataset_version
            WHERE p.tier = @tier", new { tier });
    }

    /// <summary>Puzzle at a pool index in the published pool for a tier. Retired puzzles keep their slot
    /// so the index-to-puzzle mapping never shifts during a tournament.</summary>
    public async Task<PuzzleRecord?> GetByPoolIndexAsync(int tier, int poolIndex, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PuzzleRecord>(@"
            SELECT p.id, p.dataset_version, p.tier, p.pool_index, p.start_actor_id, p.target_actor_id,
                   p.optimal_links, p.shortest_path_count, p.example_path
            FROM puzzles p JOIN puzzle_pools pp ON pp.tier = p.tier AND pp.dataset_version = p.dataset_version
            WHERE p.tier = @tier AND p.pool_index = @poolIndex", new { tier, poolIndex });
    }

    public async Task<PuzzleRecord?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PuzzleRecord>(@"
            SELECT id, dataset_version, tier, pool_index, start_actor_id, target_actor_id, optimal_links, shortest_path_count, example_path
            FROM puzzles WHERE id = @id", new { id });
    }
}
