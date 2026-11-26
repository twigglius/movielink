using MovieLink.Data;

namespace MovieLink.Tools;

public sealed class PuzzleGenOptions
{
    public int Count { get; set; } = 500;
    public int MinLinks { get; set; } = 2;
    public int MaxLinks { get; set; } = 3;
    /// <summary>Only the N most popular actors can be a start or target.</summary>
    public int TopActors { get; set; } = 400;
    /// <summary>Require at least this many distinct shortest chains, so a puzzle isn't one obscure film.</summary>
    public int MinShortestPaths { get; set; } = 2;
    /// <summary>Cap how often one actor appears across the pool.</summary>
    public int MaxUsesPerActor { get; set; } = 6;
    public int Seed { get; set; } = 1;
}

public static class PuzzleGenerator
{
    /// <summary>
    /// Builds a deterministic pool: same graph + same options = same pool, in the same order.
    /// pool_index is what the shared Skillz random draw selects, so order matters.
    /// </summary>
    public static List<PuzzleRecord> Generate(
        MovieGraph graph,
        IReadOnlyDictionary<long, double> actorPopularity,
        PuzzleGenOptions o)
    {
        var top = actorPopularity
            .Where(kv => graph.HasActor(kv.Key))
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Take(o.TopActors)
            .Select(kv => kv.Key)
            .ToList();
        var topSet = top.ToHashSet();

        var candidates = new List<PuzzleRecord>();
        var seenPairs = new HashSet<(long, long)>();
        foreach (long start in top)
        {
            var bfs = graph.Bfs(start, o.MaxLinks);
            for (int links = o.MinLinks; links <= o.MaxLinks; links++)
            {
                foreach (long target in graph.ActorsAt(bfs, links))
                {
                    if (!topSet.Contains(target)) continue;
                    var key = start < target ? (start, target) : (target, start);
                    if (!seenPairs.Add(key)) continue;
                    long paths = graph.PathCount(bfs, target);
                    if (paths < o.MinShortestPaths) continue;
                    candidates.Add(new PuzzleRecord
                    {
                        StartActorId = start,
                        TargetActorId = target,
                        OptimalLinks = links,
                        ShortestPathCount = (int)Math.Min(paths, int.MaxValue),
                        ExamplePath = graph.ExamplePath(bfs, target),
                    });
                }
            }
        }

        // Deterministic shuffle, then cap per-actor use for variety.
        var rng = new Random(o.Seed);
        var shuffled = candidates.OrderBy(c => c.StartActorId).ThenBy(c => c.TargetActorId).ToArray();
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        var uses = new Dictionary<long, int>();
        var pool = new List<PuzzleRecord>();
        foreach (var c in shuffled)
        {
            if (pool.Count >= o.Count) break;
            if (uses.GetValueOrDefault(c.StartActorId) >= o.MaxUsesPerActor) continue;
            if (uses.GetValueOrDefault(c.TargetActorId) >= o.MaxUsesPerActor) continue;
            uses[c.StartActorId] = uses.GetValueOrDefault(c.StartActorId) + 1;
            uses[c.TargetActorId] = uses.GetValueOrDefault(c.TargetActorId) + 1;
            // Randomise direction too, so the more famous actor isn't always the start.
            if (rng.Next(2) == 1)
            {
                c.ExamplePath = c.ExamplePath.Reverse().ToArray();
                (c.StartActorId, c.TargetActorId) = (c.TargetActorId, c.StartActorId);
            }
            c.PoolIndex = pool.Count;
            pool.Add(c);
        }
        return pool;
    }
}
