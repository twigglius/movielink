using System.Linq;
using System.Threading.Tasks;
using MovieLink.Core;
using MovieLink.Core.Dtos;
using MovieLink.Game.App;
using MovieLink.Game.Match;
using MovieLink.Game.Net;
using NUnit.Framework;
using UnityEngine;

namespace MovieLink.Game.Tests
{
    /// <summary>Runs a whole match against FakeGameApi with a fake clock. No server, no scene.</summary>
    public class MatchControllerTests
    {
        private double _now;
        private FakeGameApi _api = null!;

        [SetUp]
        public void SetUp()
        {
            _now = 0;
            var json = Resources.Load<TextAsset>("fake-data").text;
            _api = new FakeGameApi(json, () => _now);
        }

        [Test]
        public async Task Optimal_chain_scores_and_finishes()
        {
            var m = new MatchController(_api, () => _now);
            SubmitResponse finished = null;
            m.Finished += r => finished = r;
            await m.StartAsync(new MatchStartInfo { PlayerId = "t", PuzzleSeed = 0 });

            // Walk the shortest known chain using search results, like a player would.
            var path = await ShortestPath(m);
            foreach (var node in path.Skip(1))
            {
                _now += 4;
                var hits = await m.SearchAsync(node.Name);
                await m.PickAsync(hits.First(h => h.Id == node.Id));
            }

            Assert.IsNotNull(finished);
            Assert.IsTrue(finished.IsComplete);
            int links = (path.Count - 1) / 2;
            Assert.AreEqual(Scoring.Compute(links, 4.0 * (path.Count - 1), GameRules.Default), finished.Score);
        }

        [Test]
        public async Task Wrong_kind_is_caught_locally_and_undo_works()
        {
            var m = new MatchController(_api, () => _now);
            string message = null;
            m.Message += s => message = s;
            await m.StartAsync(new MatchStartInfo { PlayerId = "t", PuzzleSeed = 1 });
            await m.PickAsync(m.Puzzle.TargetActor); // actor after actor
            Assert.AreEqual(ChainValidator.Describe(StepError.WrongKind), message);

            var path = await ShortestPath(m);
            await m.PickAsync(path[1]);
            Assert.AreEqual(2, m.Chain.Count);
            await m.UndoAsync();
            Assert.AreEqual(1, m.Chain.Count);
        }

        [Test]
        public async Task Timer_expiry_submits_zero()
        {
            var m = new MatchController(_api, () => _now);
            SubmitResponse finished = null;
            m.Finished += r => finished = r;
            await m.StartAsync(new MatchStartInfo { PlayerId = "t", PuzzleSeed = 2 });
            _now += 121;
            m.Tick();
            await Task.Yield();
            Assert.IsNotNull(finished);
            Assert.AreEqual(0, finished.Score);
        }

        [Test]
        public async Task Same_seed_same_puzzle()
        {
            var a = new MatchController(_api, () => _now);
            var b = new MatchController(_api, () => _now);
            await a.StartAsync(new MatchStartInfo { PlayerId = "a", PuzzleSeed = 987654 });
            await b.StartAsync(new MatchStartInfo { PlayerId = "b", PuzzleSeed = 987654 });
            Assert.AreEqual(a.Puzzle.StartActor.Id, b.Puzzle.StartActor.Id);
            Assert.AreEqual(a.Puzzle.TargetActor.Id, b.Puzzle.TargetActor.Id);
        }

        [Test]
        public void Initials_for_avatars()
        {
            Assert.AreEqual("LD", UI.Ui.Initials("Leonardo DiCaprio"));
            Assert.AreEqual("SJ", UI.Ui.Initials("Samuel L. Jackson"));
            Assert.AreEqual("C", UI.Ui.Initials("Cher"));
        }

        // The fake API reveals the shortest chain only through a submit, so use a throwaway match on the same seed.
        private async Task<System.Collections.Generic.List<NodeDto>> ShortestPath(MatchController m)
        {
            var probe = await _api.CreateMatchAsync(new CreateMatchRequest { PlayerId = "probe", PuzzleSeed = (int)m.Puzzle.PuzzleId });
            var r = await _api.SubmitAsync(probe.MatchId, probe.Token);
            return r.ShortestKnownChain;
        }
    }
}
