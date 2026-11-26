using Dapper;

namespace MovieLink.Data;

public sealed class MatchRecord
{
    public Guid Id { get; set; }
    public long PuzzleId { get; set; }
    public string Platform { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string? ExternalMatchId { get; set; }
    public int? PuzzleSeed { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public long[] Chain { get; set; } = Array.Empty<long>();
    public int[] StepTimesMs { get; set; } = Array.Empty<int>();
    public int RequestCount { get; set; }
    public int? Score { get; set; }
    public int? Links { get; set; }
    public string Status { get; set; } = "playing";
    public string[] Flags { get; set; } = Array.Empty<string>();
}

public sealed class MatchRepository
{
    private readonly Db _db;
    public MatchRepository(Db db) => _db = db;

    static MatchRepository() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    private const string Columns = @"id, puzzle_id, platform, player_id, external_match_id, puzzle_seed, started_at, finished_at,
        chain, step_times_ms, request_count, score, links, status, flags";

    public async Task CreateAsync(MatchRecord m, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(@"
            INSERT INTO matches (id, puzzle_id, platform, player_id, external_match_id, puzzle_seed, started_at, chain)
            VALUES (@Id, @PuzzleId, @Platform, @PlayerId, @ExternalMatchId, @PuzzleSeed, @StartedAt, @Chain)", m);
    }

    public async Task<MatchRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<MatchRecord>($"SELECT {Columns} FROM matches WHERE id = @id", new { id });
    }

    /// <summary>Saves the chain only if nobody else changed it since we read it (optimistic, by chain length).</summary>
    public async Task<bool> UpdateChainAsync(Guid id, int expectedLength, long[] chain, int[] stepTimesMs, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        int n = await conn.ExecuteAsync(@"
            UPDATE matches SET chain = @chain, step_times_ms = @stepTimesMs, request_count = request_count + 1
            WHERE id = @id AND status = 'playing' AND coalesce(array_length(chain, 1), 0) = @expectedLength",
            new { id, expectedLength, chain, stepTimesMs });
        return n == 1;
    }

    public async Task<bool> FinishAsync(Guid id, string status, int score, int links, DateTime finishedAt, string[] flags, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        int n = await conn.ExecuteAsync(@"
            UPDATE matches SET status = @status, score = @score, links = @links, finished_at = @finishedAt, flags = @flags
            WHERE id = @id AND status = 'playing'",
            new { id, status, score, links, finishedAt, flags });
        return n == 1;
    }
}
