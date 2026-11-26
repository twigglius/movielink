#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MovieLink.Core;
using MovieLink.Core.Dtos;
using Newtonsoft.Json;

namespace MovieLink.Game.Net
{
    /// <summary>
    /// Offline stand-in for the server (Tier 1 testing in the Editor). Uses the same MovieLink.Core rules
    /// and scoring as the server, over a small bundled dataset (Resources/fake-data.json).
    /// </summary>
    public sealed class FakeGameApi : IGameApi
    {
        public sealed class FakeData
        {
            public List<FakeActor> Actors { get; set; } = new List<FakeActor>();
            public List<FakeMovie> Movies { get; set; } = new List<FakeMovie>();
            public List<long[]> Credits { get; set; } = new List<long[]>();
            public List<FakePuzzle> Puzzles { get; set; } = new List<FakePuzzle>();
        }
        public sealed class FakeActor { public long Id { get; set; } public string Name { get; set; } = ""; public int? BirthYear { get; set; } }
        public sealed class FakeMovie { public long Id { get; set; } public string Title { get; set; } = ""; public int? Year { get; set; } }
        public sealed class FakePuzzle
        {
            public long Start { get; set; }
            public long Target { get; set; }
            public int OptimalLinks { get; set; }
            public long[] ExamplePath { get; set; } = Array.Empty<long>();
        }

        private sealed class FakeMatch
        {
            public Guid Id;
            public string Token = "";
            public FakePuzzle Puzzle = new FakePuzzle();
            public double StartedAt;
            public double? FinishedAt;
            public List<NodeRef> Chain = new List<NodeRef>();
            public SubmitResponse? Result;
        }

        private readonly FakeData _data;
        private readonly CreditSet _credits;
        private readonly Dictionary<long, FakeActor> _actors;
        private readonly Dictionary<long, FakeMovie> _movies;
        private readonly Dictionary<Guid, FakeMatch> _matches = new Dictionary<Guid, FakeMatch>();
        private readonly Func<double> _clock;
        private readonly GameRules _rules;
        private readonly Random _random = new Random();

        /// <param name="clock">Seconds, monotonic. The Editor passes Time.realtimeSinceStartupAsDouble.</param>
        public FakeGameApi(string json, Func<double> clock, GameRules? rules = null)
        {
            _data = JsonConvert.DeserializeObject<FakeData>(json, HttpGameApi.Json) ?? new FakeData();
            _credits = new CreditSet(_data.Credits.Select(c => (c[0], c[1])));
            _actors = _data.Actors.ToDictionary(a => a.Id);
            _movies = _data.Movies.ToDictionary(m => m.Id);
            _clock = clock;
            _rules = rules ?? GameRules.Default;
        }

        public Task<CreateMatchResponse> CreateMatchAsync(CreateMatchRequest request)
        {
            if (_data.Puzzles.Count == 0) throw new GameApiException("No puzzles in fake-data.json.");
            int index = request.PuzzleSeed is int s ? (int)((uint)s % (uint)_data.Puzzles.Count) : _random.Next(_data.Puzzles.Count);
            var p = _data.Puzzles[index];
            var m = new FakeMatch { Id = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"), Puzzle = p, StartedAt = _clock() };
            m.Chain.Add(NodeRef.Actor(p.Start));
            _matches[m.Id] = m;
            return Task.FromResult(new CreateMatchResponse
            {
                MatchId = m.Id,
                Token = m.Token,
                ServerStartedAt = DateTimeOffset.UtcNow,
                Puzzle = new PuzzleDto
                {
                    PuzzleId = index,
                    StartActor = Describe(NodeRef.Actor(p.Start)),
                    TargetActor = Describe(NodeRef.Actor(p.Target)),
                    MaxLinks = _rules.MaxLinks,
                    TimeLimitSeconds = _rules.TimeLimitSeconds,
                },
            });
        }

        public Task<List<NodeDto>> SearchAsync(Guid matchId, string token, NodeKind kind, string query)
        {
            Get(matchId, token);
            string q = TextNormalizer.ForSearch(query);
            if (q.Length < 2) return Task.FromResult(new List<NodeDto>());
            IEnumerable<(string norm, NodeDto dto)> pool = kind == NodeKind.Actor
                ? _data.Actors.Select(a => (TextNormalizer.ForSearch(a.Name), Describe(NodeRef.Actor(a.Id))))
                : _data.Movies.Select(mv => (TextNormalizer.ForSearch(mv.Title), Describe(NodeRef.Movie(mv.Id))));
            var results = pool
                .Where(x => x.norm.Contains(q))
                .OrderByDescending(x => x.norm.StartsWith(q))
                .ThenByDescending(x => x.norm.Contains(" " + q))
                .ThenBy(x => x.dto.Name)
                .Take(10)
                .Select(x => x.dto)
                .ToList();
            return Task.FromResult(results);
        }

        public Task<ChainStateResponse> StepAsync(Guid matchId, string token, NodeKind kind, long id)
        {
            var m = Get(matchId, token);
            double elapsed = _clock() - m.StartedAt;
            if (m.Result != null || elapsed >= _rules.TimeLimitSeconds)
                return Task.FromResult(State(m, false, StepError.None, "Time's up.", elapsed, true));

            var next = new NodeRef(kind, id);
            var err = ChainValidator.ValidateStep(m.Puzzle.Start, m.Puzzle.Target, m.Chain, next, _credits, _rules);
            if (err != StepError.None)
                return Task.FromResult(State(m, false, err, ChainValidator.Describe(err), elapsed, false));
            m.Chain.Add(next);
            if (ChainValidator.IsComplete(m.Puzzle.Target, m.Chain)) m.FinishedAt = _clock();
            return Task.FromResult(State(m, true, StepError.None, null, elapsed, false));
        }

        public Task<ChainStateResponse> UndoAsync(Guid matchId, string token)
        {
            var m = Get(matchId, token);
            double elapsed = _clock() - m.StartedAt;
            bool ok = m.Result == null && m.Chain.Count > 1 && elapsed < _rules.TimeLimitSeconds;
            if (ok)
            {
                m.Chain.RemoveAt(m.Chain.Count - 1);
                m.FinishedAt = null;
            }
            return Task.FromResult(State(m, ok, StepError.None, ok ? null : "Nothing to undo.", elapsed, elapsed >= _rules.TimeLimitSeconds));
        }

        public Task<SubmitResponse> SubmitAsync(Guid matchId, string token)
        {
            var m = Get(matchId, token);
            if (m.Result == null)
            {
                var r = ChainValidator.ValidateChain(m.Puzzle.Start, m.Puzzle.Target, m.Chain, _credits, _rules);
                bool complete = r.IsValid && r.IsComplete;
                double seconds = (m.FinishedAt ?? _clock()) - m.StartedAt;
                m.Result = new SubmitResponse
                {
                    MatchId = m.Id,
                    Score = complete ? Scoring.Compute(r.Links, seconds, _rules) : 0,
                    Links = r.Links,
                    IsComplete = complete && seconds < _rules.TimeLimitSeconds,
                    SecondsTaken = Math.Round(seconds, 3),
                    OptimalLinks = m.Puzzle.OptimalLinks,
                    Chain = m.Chain.Select(Describe).ToList(),
                    ShortestKnownChain = m.Puzzle.ExamplePath
                        .Select((pid, i) => Describe(i % 2 == 0 ? NodeRef.Actor(pid) : NodeRef.Movie(pid))).ToList(),
                };
            }
            return Task.FromResult(m.Result);
        }

        private FakeMatch Get(Guid id, string token)
        {
            if (!_matches.TryGetValue(id, out var m) || m.Token != token) throw new GameApiException("bad_token", 401);
            return m;
        }

        private ChainStateResponse State(FakeMatch m, bool accepted, StepError err, string? msg, double elapsed, bool timeUp) =>
            new ChainStateResponse
            {
                Accepted = accepted,
                Error = err,
                Message = msg,
                Chain = m.Chain.Select(Describe).ToList(),
                Links = ChainValidator.CountMovies(m.Chain),
                IsComplete = ChainValidator.IsComplete(m.Puzzle.Target, m.Chain),
                SecondsElapsed = Math.Round(elapsed, 3),
                TimeUp = timeUp,
            };

        private NodeDto Describe(NodeRef n)
        {
            if (n.Kind == NodeKind.Actor && _actors.TryGetValue(n.Id, out var a))
                return new NodeDto { Kind = n.Kind, Id = n.Id, Name = a.Name, Subtitle = a.BirthYear is int y ? $"b. {y}" : null };
            if (n.Kind == NodeKind.Movie && _movies.TryGetValue(n.Id, out var mv))
                return new NodeDto { Kind = n.Kind, Id = n.Id, Name = mv.Title, Subtitle = mv.Year?.ToString() };
            return new NodeDto { Kind = n.Kind, Id = n.Id, Name = "Unknown" };
        }
    }
}
