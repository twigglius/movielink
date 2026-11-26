using System.Collections.Generic;

namespace MovieLink.Core
{
    /// <summary>In-memory credit lookup: used by tests, the offline FakeGameApi and the server's per-match cache.</summary>
    public sealed class CreditSet : ICreditLookup
    {
        private readonly HashSet<(long actor, long movie)> _credits = new HashSet<(long, long)>();

        public CreditSet() { }

        public CreditSet(IEnumerable<(long actorId, long movieId)> credits)
        {
            foreach (var c in credits) Add(c.actorId, c.movieId);
        }

        public int Count => _credits.Count;

        public void Add(long actorId, long movieId) => _credits.Add((actorId, movieId));

        public bool HasCredit(long actorId, long movieId) => _credits.Contains((actorId, movieId));
    }
}
