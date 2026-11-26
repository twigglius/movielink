#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieLink.Core;
using MovieLink.Core.Dtos;
using MovieLink.Game.App;
using MovieLink.Game.Net;

namespace MovieLink.Game.Match
{
    /// <summary>
    /// One match, as plain C# (no Unity APIs) so it can be unit tested. The server is the referee:
    /// this class shows its answers, and only pre-checks things it can know locally (kind, repeats).
    /// </summary>
    public sealed class MatchController
    {
        private readonly IGameApi _api;
        private readonly Func<double> _clock;
        private CreateMatchResponse? _match;
        private double _localStart;
        private bool _busy;

        public MatchController(IGameApi api, Func<double> clock)
        {
            _api = api;
            _clock = clock;
        }

        public PuzzleDto? Puzzle => _match?.Puzzle;
        public List<NodeDto> Chain { get; private set; } = new List<NodeDto>();
        public bool IsComplete { get; private set; }
        public bool TimeUp { get; private set; }
        public SubmitResponse? Result { get; private set; }
        public int Links => ChainValidator.CountMovies(ChainRefs());

        /// <summary>Search kind for the next pick: movies after an actor, actors after a movie.</summary>
        public NodeKind NextKind => Chain.Count > 0 && Chain[Chain.Count - 1].Kind == NodeKind.Actor ? NodeKind.Movie : NodeKind.Actor;

        /// <summary>Display-only countdown. The server's clock decides the score.</summary>
        public double SecondsLeft => _match == null ? 0 : Math.Max(0, _match.Puzzle.TimeLimitSeconds - (_clock() - _localStart));

        public event Action? Changed;
        public event Action<string>? Message;
        public event Action<SubmitResponse>? Finished;

        public async Task StartAsync(MatchStartInfo info)
        {
            _match = await _api.CreateMatchAsync(new CreateMatchRequest
            {
                Platform = info.Platform,
                PlayerId = info.PlayerId,
                ExternalMatchId = info.ExternalMatchId,
                Tier = info.Tier,
                PuzzleSeed = info.PuzzleSeed,
            });
            _localStart = _clock();
            Chain = new List<NodeDto> { _match.Puzzle.StartActor };
            IsComplete = false;
            TimeUp = false;
            Result = null;
            Changed?.Invoke();
        }

        public Task<List<NodeDto>> SearchAsync(string text)
        {
            if (_match == null || string.IsNullOrWhiteSpace(text)) return Task.FromResult(new List<NodeDto>());
            return _api.SearchAsync(_match.MatchId, _match.Token, NextKind, text);
        }

        public async Task PickAsync(NodeDto node)
        {
            if (_match == null || _busy || Result != null) return;
            // Local pre-check with a lookup that trusts every credit: catches wrong kind and repeats instantly.
            var local = ChainValidator.ValidateStep(_match.Puzzle.StartActor.Id, _match.Puzzle.TargetActor.Id,
                ChainRefs(), node.ToRef(), TrustAllCredits.Instance, new GameRules(_match.Puzzle.MaxLinks, _match.Puzzle.TimeLimitSeconds));
            if (local != StepError.None && local != StepError.NotInMovie)
            {
                Message?.Invoke(ChainValidator.Describe(local));
                return;
            }

            _busy = true;
            try
            {
                var state = await _api.StepAsync(_match.MatchId, _match.Token, node.Kind, node.Id);
                Apply(state);
                if (!state.Accepted && state.Message != null) Message?.Invoke(state.Message);
                if (state.IsComplete || state.TimeUp) await SubmitAsync();
            }
            catch (GameApiException e) { Message?.Invoke(e.IsNetworkError ? "Connection lost. Try again." : e.Message); }
            finally { _busy = false; }
        }

        public async Task UndoAsync()
        {
            if (_match == null || _busy || Result != null || Chain.Count <= 1) return;
            _busy = true;
            try { Apply(await _api.UndoAsync(_match.MatchId, _match.Token)); }
            catch (GameApiException e) { Message?.Invoke(e.Message); }
            finally { _busy = false; }
        }

        /// <summary>Called on completion, on timer expiry, and if the player gives up. Safe to call twice.</summary>
        public async Task SubmitAsync()
        {
            if (_match == null || Result != null) return;
            try
            {
                Result = await _api.SubmitAsync(_match.MatchId, _match.Token);
                Finished?.Invoke(Result);
            }
            catch (GameApiException e) { Message?.Invoke("Couldn't submit: " + e.Message + " Retrying…"); throw; }
        }

        /// <summary>Call every frame (or second) so the timer expiry submits even if the player does nothing.</summary>
        public void Tick()
        {
            if (_match != null && Result == null && !TimeUp && SecondsLeft <= 0)
            {
                TimeUp = true;
                Changed?.Invoke();
                _ = SubmitSafe();
            }
        }

        private async Task SubmitSafe()
        {
            try { await SubmitAsync(); } catch (GameApiException) { /* message already shown */ }
        }

        private void Apply(ChainStateResponse state)
        {
            Chain = state.Chain;
            IsComplete = state.IsComplete;
            TimeUp = state.TimeUp;
            Changed?.Invoke();
        }

        private List<NodeRef> ChainRefs()
        {
            var list = new List<NodeRef>(Chain.Count);
            foreach (var n in Chain) list.Add(n.ToRef());
            return list;
        }

        private sealed class TrustAllCredits : ICreditLookup
        {
            public static readonly TrustAllCredits Instance = new TrustAllCredits();
            public bool HasCredit(long actorId, long movieId) => true;
        }
    }
}
