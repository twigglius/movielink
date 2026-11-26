using MovieLink.Core;
using MovieLink.Core.Dtos;
using MovieLink.Data;

namespace MovieLink.Api;

public sealed class MatchNotFoundException : Exception { }
public sealed class BadTokenException : Exception { }
public sealed class NoPuzzlesException : Exception
{
    public NoPuzzlesException(int tier) : base($"No published puzzle pool for tier {tier}.") { }
}

/// <summary>
/// The referee. Serves puzzles, keeps the clock, checks every step and computes the authoritative score.
/// Holds no state in memory: everything lives in Postgres, so any number of instances can run.
/// </summary>
public sealed class MatchService
{
    private readonly PuzzleRepository _puzzles;
    private readonly MatchRepository _matches;
    private readonly GraphRepository _graph;
    private readonly MatchTokens _tokens;
    private readonly GameOptions _opts;
    private readonly GameRules _rules;
    private readonly TimeProvider _clock;

    public MatchService(PuzzleRepository puzzles, MatchRepository matches, GraphRepository graph,
        MatchTokens tokens, GameOptions opts, TimeProvider clock)
    {
        _puzzles = puzzles; _matches = matches; _graph = graph; _tokens = tokens; _opts = opts; _clock = clock;
        _rules = new GameRules(opts.MaxLinks, opts.TimeLimitSeconds);
    }

    public GameRules Rules => _rules;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<CreateMatchResponse> CreateAsync(CreateMatchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.PlayerId)) throw new ArgumentException("playerId is required");
        int size = await _puzzles.ActivePoolSizeAsync(req.Tier, ct);
        if (size == 0) throw new NoPuzzlesException(req.Tier);

        // Skillz: both opponents send the same first random draw, so they land on the same pool index.
        int index = req.PuzzleSeed is int seed ? (int)((uint)seed % (uint)size) : Random.Shared.Next(size);
        var puzzle = await _puzzles.GetByPoolIndexAsync(req.Tier, index, ct) ?? throw new NoPuzzlesException(req.Tier);

        // Truncate to microseconds: that's what Postgres stores, and the token signs the stored value.
        var started = TruncateToMicros(Now);
        var match = new MatchRecord
        {
            Id = Guid.NewGuid(),
            PuzzleId = puzzle.Id,
            Platform = req.Platform,
            PlayerId = req.PlayerId,
            ExternalMatchId = req.ExternalMatchId,
            PuzzleSeed = req.PuzzleSeed,
            StartedAt = started,
            Chain = new[] { puzzle.StartActorId },
        };
        await _matches.CreateAsync(match, ct);

        var ends = await _graph.DescribeAsync(puzzle.DatasetVersion,
            new[] { NodeRef.Actor(puzzle.StartActorId), NodeRef.Actor(puzzle.TargetActorId) }, ct);
        return new CreateMatchResponse
        {
            MatchId = match.Id,
            Token = _tokens.Issue(match.Id, match.PlayerId, match.PuzzleId, started),
            ServerStartedAt = new DateTimeOffset(started, TimeSpan.Zero),
            Puzzle = new PuzzleDto
            {
                PuzzleId = puzzle.Id,
                DatasetVersion = puzzle.DatasetVersion,
                StartActor = ends[0],
                TargetActor = ends[1],
                MaxLinks = _rules.MaxLinks,
                TimeLimitSeconds = _rules.TimeLimitSeconds,
            },
        };
    }

    private async Task<(MatchRecord match, PuzzleRecord puzzle)> LoadAsync(Guid matchId, string? token, CancellationToken ct)
    {
        var match = await _matches.GetAsync(matchId, ct) ?? throw new MatchNotFoundException();
        var startedUtc = DateTime.SpecifyKind(match.StartedAt, DateTimeKind.Utc);
        if (!_tokens.Verify(token, match.Id, match.PlayerId, match.PuzzleId, startedUtc)) throw new BadTokenException();
        var puzzle = await _puzzles.GetAsync(match.PuzzleId, ct) ?? throw new MatchNotFoundException();
        return (match, puzzle);
    }

    public async Task<int> DatasetVersionForAsync(Guid matchId, string? token, CancellationToken ct) =>
        (await LoadAsync(matchId, token, ct)).puzzle.DatasetVersion;

    /// <summary>Seconds on the match clock, minus the per-request latency allowance.</summary>
    private double Elapsed(MatchRecord m, DateTime at)
    {
        double raw = (at - DateTime.SpecifyKind(m.StartedAt, DateTimeKind.Utc)).TotalSeconds;
        double allowance = Math.Min(m.RequestCount * _opts.LatencyAllowanceMsPerRequest, _opts.MaxLatencyAllowanceMs) / 1000.0;
        return Math.Max(0, raw - allowance);
    }

    private static List<NodeRef> ToChain(long[] ids) =>
        ids.Select((id, i) => i % 2 == 0 ? NodeRef.Actor(id) : NodeRef.Movie(id)).ToList();

    public async Task<ChainStateResponse> StepAsync(Guid matchId, string? token, StepRequest req, CancellationToken ct)
    {
        var (match, puzzle) = await LoadAsync(matchId, token, ct);
        var chain = ToChain(match.Chain);
        var now = Now;
        double elapsed = Elapsed(match, now);

        if (match.Status != "playing" || elapsed >= _rules.TimeLimitSeconds)
            return await StateAsync(match, puzzle, chain, false, StepError.None, "Time's up.", elapsed, timeUp: true, ct);

        var next = new NodeRef(req.Kind, req.Id);
        var movies = chain.Where(n => n.Kind == NodeKind.Movie).Select(n => n.Id).ToList();
        if (next.Kind == NodeKind.Movie) movies.Add(next.Id);
        var credits = await _graph.CreditsForAsync(puzzle.DatasetVersion, movies, ct);

        var err = ChainValidator.ValidateStep(puzzle.StartActorId, puzzle.TargetActorId, chain, next, credits, _rules);
        if (err != StepError.None)
            return await StateAsync(match, puzzle, chain, false, err, ChainValidator.Describe(err), elapsed, false, ct);

        var newChain = match.Chain.Append(next.Id).ToArray();
        var times = match.StepTimesMs.Append((int)Math.Min(int.MaxValue, (now - DateTime.SpecifyKind(match.StartedAt, DateTimeKind.Utc)).TotalMilliseconds)).ToArray();
        if (!await _matches.UpdateChainAsync(match.Id, match.Chain.Length, newChain, times, ct))
        {
            // Another request changed the chain first (double tap). Return the current truth.
            var fresh = await _matches.GetAsync(match.Id, ct) ?? throw new MatchNotFoundException();
            return await StateAsync(fresh, puzzle, ToChain(fresh.Chain), false, StepError.None, "Chain changed; refreshed.", Elapsed(fresh, now), false, ct);
        }
        chain.Add(next);
        return await StateAsync(match, puzzle, chain, true, StepError.None, null, elapsed, false, ct);
    }

    public async Task<ChainStateResponse> UndoAsync(Guid matchId, string? token, CancellationToken ct)
    {
        var (match, puzzle) = await LoadAsync(matchId, token, ct);
        var now = Now;
        double elapsed = Elapsed(match, now);
        var chain = ToChain(match.Chain);
        bool timeUp = match.Status != "playing" || elapsed >= _rules.TimeLimitSeconds;
        if (timeUp || chain.Count <= 1)
            return await StateAsync(match, puzzle, chain, false, StepError.None, timeUp ? "Time's up." : "Nothing to undo.", elapsed, timeUp, ct);

        var newChain = match.Chain[..^1];
        var times = match.StepTimesMs.Length > 0 ? match.StepTimesMs[..^1] : match.StepTimesMs;
        if (await _matches.UpdateChainAsync(match.Id, match.Chain.Length, newChain, times, ct))
            chain.RemoveAt(chain.Count - 1);
        return await StateAsync(match, puzzle, chain, true, StepError.None, null, elapsed, false, ct);
    }

    public async Task<SubmitResponse> SubmitAsync(Guid matchId, string? token, CancellationToken ct)
    {
        var (match, puzzle) = await LoadAsync(matchId, token, ct);
        var chain = ToChain(match.Chain);

        double seconds;
        int score, links;
        bool complete;
        if (match.Status == "playing")
        {
            var now = Now;
            // Score on the time of the last step that completed the chain, not the submit tap.
            var credits = await _graph.CreditsForAsync(puzzle.DatasetVersion, chain.Where(n => n.Kind == NodeKind.Movie).Select(n => n.Id), ct);
            var result = ChainValidator.ValidateChain(puzzle.StartActorId, puzzle.TargetActorId, chain, credits, _rules);
            complete = result.IsValid && result.IsComplete;
            links = result.Links;
            DateTime finishAt = complete && match.StepTimesMs.Length > 0
                ? DateTime.SpecifyKind(match.StartedAt, DateTimeKind.Utc).AddMilliseconds(match.StepTimesMs[^1])
                : now;
            seconds = Elapsed(match, finishAt);
            score = complete ? Scoring.Compute(links, seconds, _rules) : 0;
            string status = seconds >= _rules.TimeLimitSeconds || (!complete && Elapsed(match, now) >= _rules.TimeLimitSeconds) ? "expired" : "submitted";
            var flags = Flags(match, puzzle, complete, links, seconds);
            if (!await _matches.FinishAsync(match.Id, status, score, links, finishAt, flags, ct))
                match = await _matches.GetAsync(match.Id, ct) ?? throw new MatchNotFoundException(); // raced with another submit
            else
            {
                match.Score = score; match.Links = links; match.FinishedAt = finishAt;
            }
        }

        // Idempotent: a repeat submit (retry after a dropped connection) returns the stored result.
        score = match.Score ?? 0;
        links = match.Links ?? 0;
        complete = score > 0;
        seconds = match.FinishedAt is DateTime f ? Elapsed(match, DateTime.SpecifyKind(f, DateTimeKind.Utc)) : 0;

        return new SubmitResponse
        {
            MatchId = match.Id,
            Score = score,
            Links = links,
            IsComplete = complete,
            SecondsTaken = Math.Round(seconds, 3),
            OptimalLinks = puzzle.OptimalLinks,
            Chain = await _graph.DescribeAsync(puzzle.DatasetVersion, chain, ct),
            ShortestKnownChain = await _graph.DescribeAsync(puzzle.DatasetVersion, ToChain(puzzle.ExamplePath), ct),
        };
    }

    /// <summary>Statistical flags for review. They never change the score; they go to a review list and to Skillz.</summary>
    private static string[] Flags(MatchRecord m, PuzzleRecord p, bool complete, int links, double seconds)
    {
        var flags = new List<string>();
        if (complete && links <= p.OptimalLinks && seconds < 2.5 * links) flags.Add("fast_optimal");
        for (int i = 1; i < m.StepTimesMs.Length; i++)
            if (m.StepTimesMs[i] - m.StepTimesMs[i - 1] < 250) { flags.Add("inhuman_step_gap"); break; }
        return flags.ToArray();
    }

    private async Task<ChainStateResponse> StateAsync(MatchRecord match, PuzzleRecord puzzle, List<NodeRef> chain,
        bool accepted, StepError err, string? message, double elapsed, bool timeUp, CancellationToken ct) => new()
    {
        Accepted = accepted,
        Error = err,
        Message = message,
        Chain = await _graph.DescribeAsync(puzzle.DatasetVersion, chain, ct),
        Links = ChainValidator.CountMovies(chain),
        IsComplete = ChainValidator.IsComplete(puzzle.TargetActorId, chain),
        SecondsElapsed = Math.Round(elapsed, 3),
        TimeUp = timeUp,
    };

    private static DateTime TruncateToMicros(DateTime t) => new(t.Ticks - t.Ticks % 10, DateTimeKind.Utc);
}
