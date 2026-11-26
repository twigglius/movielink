namespace MovieLink.Tools;

/// <summary>Bipartite actor-movie graph for one dataset version, held in memory with dense indices.</summary>
public sealed class MovieGraph
{
    private readonly Dictionary<long, int> _actorIndex = new();
    private readonly Dictionary<long, int> _movieIndex = new();
    private readonly List<long> _actorIds = new();
    private readonly List<long> _movieIds = new();
    private readonly List<List<int>> _actorMovies = new();
    private readonly List<List<int>> _movieActors = new();

    public MovieGraph(IEnumerable<(long ActorId, long MovieId)> credits)
    {
        foreach (var (a, m) in credits)
        {
            int ai = Intern(a, _actorIndex, _actorIds, _actorMovies);
            int mi = Intern(m, _movieIndex, _movieIds, _movieActors);
            if (!_actorMovies[ai].Contains(mi))
            {
                _actorMovies[ai].Add(mi);
                _movieActors[mi].Add(ai);
            }
        }
        // Stable order so BFS results (and example paths) are deterministic.
        foreach (var l in _actorMovies) l.Sort();
        foreach (var l in _movieActors) l.Sort();
    }

    private static int Intern(long id, Dictionary<long, int> index, List<long> ids, List<List<int>> adj)
    {
        if (!index.TryGetValue(id, out int i))
        {
            i = ids.Count;
            index[id] = i;
            ids.Add(id);
            adj.Add(new List<int>());
        }
        return i;
    }

    public int ActorCount => _actorIds.Count;
    public int MovieCount => _movieIds.Count;
    public bool HasActor(long id) => _actorIndex.ContainsKey(id);

    public sealed class BfsResult
    {
        internal BfsResult(int[] dist, long[] pathCount, int[] parentMovie, int[] parentActor)
        {
            Dist = dist; PathCount = pathCount; ParentMovie = parentMovie; ParentActor = parentActor;
        }
        /// <summary>Links (movies) from the start actor, by actor index; -1 = unreached.</summary>
        internal int[] Dist { get; }
        /// <summary>Number of distinct shortest chains, by actor index (saturating).</summary>
        internal long[] PathCount { get; }
        internal int[] ParentMovie { get; }
        internal int[] ParentActor { get; }
    }

    /// <summary>
    /// BFS over actors, one movie = one link, up to <paramref name="maxLinks"/>.
    /// Counts shortest chains as distinct (movie, actor) sequences.
    /// </summary>
    public BfsResult Bfs(long startActorId, int maxLinks)
    {
        int n = _actorIds.Count;
        var dist = new int[n];
        var count = new long[n];
        var pMovie = new int[n];
        var pActor = new int[n];
        Array.Fill(dist, -1);
        Array.Fill(pMovie, -1);
        Array.Fill(pActor, -1);
        int s = _actorIndex[startActorId];
        dist[s] = 0;
        count[s] = 1;

        var movieSeen = new bool[_movieIds.Count];
        var frontier = new List<int> { s };
        for (int d = 0; d < maxLinks && frontier.Count > 0; d++)
        {
            var next = new List<int>();
            // Movies first reached at this depth: chains may not reuse a movie, and any shortest
            // chain uses each movie at most once, so a movie seen at an earlier depth is never shorter.
            var levelMovies = new HashSet<int>();
            foreach (int a in frontier)
                foreach (int m in _actorMovies[a])
                    if (!movieSeen[m]) levelMovies.Add(m);

            foreach (int a in frontier)
            {
                foreach (int m in _actorMovies[a])
                {
                    if (!levelMovies.Contains(m)) continue;
                    foreach (int b in _movieActors[m])
                    {
                        if (dist[b] == -1)
                        {
                            dist[b] = d + 1;
                            pMovie[b] = m;
                            pActor[b] = a;
                            next.Add(b);
                        }
                        if (dist[b] == d + 1)
                            count[b] = SatAdd(count[b], count[a]);
                    }
                }
            }
            foreach (int m in levelMovies) movieSeen[m] = true;
            frontier = next;
        }
        return new BfsResult(dist, count, pMovie, pActor);
    }

    private static long SatAdd(long x, long y) => x > long.MaxValue - y ? long.MaxValue : x + y;

    public int Distance(BfsResult r, long actorId) => _actorIndex.TryGetValue(actorId, out int i) ? r.Dist[i] : -1;
    public long PathCount(BfsResult r, long actorId) => _actorIndex.TryGetValue(actorId, out int i) ? r.PathCount[i] : 0;

    /// <summary>One shortest chain to the target as ids: actor, movie, actor, ..., target.</summary>
    public long[] ExamplePath(BfsResult r, long targetActorId)
    {
        int t = _actorIndex[targetActorId];
        if (r.Dist[t] < 0) return Array.Empty<long>();
        var rev = new List<long> { _actorIds[t] };
        int cur = t;
        while (r.ParentActor[cur] >= 0)
        {
            rev.Add(_movieIds[r.ParentMovie[cur]]);
            cur = r.ParentActor[cur];
            rev.Add(_actorIds[cur]);
        }
        rev.Reverse();
        return rev.ToArray();
    }

    public IEnumerable<long> ActorsAt(BfsResult r, int links)
    {
        for (int i = 0; i < r.Dist.Length; i++)
            if (r.Dist[i] == links) yield return _actorIds[i];
    }
}
