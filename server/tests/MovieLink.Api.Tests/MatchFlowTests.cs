using System.Net;
using System.Net.Http.Json;
using MovieLink.Core;
using MovieLink.Core.Dtos;
using Xunit;

namespace MovieLink.Api.Tests;

public class MatchFlowTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _f;
    private readonly HttpClient _http;

    public MatchFlowTests(ApiFixture f)
    {
        _f = f;
        _http = f.CreateClient();
    }

    private async Task<CreateMatchResponse> Create(int? seed = null, string player = "p1")
    {
        var resp = await _http.PostAsJsonAsync("/api/matches",
            new CreateMatchRequest { PlayerId = player, Platform = "local", PuzzleSeed = seed }, ApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CreateMatchResponse>(ApiFixture.Json))!;
    }

    private async Task<T> Post<T>(CreateMatchResponse m, string path, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{m.MatchId}/{path}")
        {
            Content = body is null ? null : JsonContent.Create(body, options: ApiFixture.Json),
        };
        req.Headers.Add("X-Match-Token", m.Token);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }

    private Task<ChainStateResponse> Step(CreateMatchResponse m, NodeKind kind, long id) =>
        Post<ChainStateResponse>(m, "steps", new StepRequest { Kind = kind, Id = id });

    private long[] PathFor(CreateMatchResponse m) => _f.Pool.Single(p => p.PoolIndex == PoolIndexOf(m)).ExamplePath;
    private int PoolIndexOf(CreateMatchResponse m) =>
        _f.Pool.Single(p => p.StartActorId == m.Puzzle.StartActor.Id && p.TargetActorId == m.Puzzle.TargetActor.Id).PoolIndex;

    [Fact]
    public async Task Same_seed_gives_both_players_the_same_puzzle()
    {
        var a = await Create(seed: 123456, player: "alice");
        var b = await Create(seed: 123456, player: "bob");
        Assert.Equal(a.Puzzle.PuzzleId, b.Puzzle.PuzzleId);
        Assert.Equal(123456 % _f.Pool.Count, PoolIndexOf(a));
        Assert.NotEqual(a.MatchId, b.MatchId);
        var c = await Create(seed: -5, player: "carol"); // negative draws map safely
        Assert.InRange(PoolIndexOf(c), 0, _f.Pool.Count - 1);
    }

    [Fact]
    public async Task Optimal_chain_scores_by_formula_using_server_time()
    {
        var m = await Create(seed: 3);
        long[] path = PathFor(m);
        int links = path.Length / 2;
        ChainStateResponse? s = null;
        for (int i = 1; i < path.Length; i++)
        {
            _f.Clock.Advance(TimeSpan.FromSeconds(5));
            s = await Step(m, i % 2 == 1 ? NodeKind.Movie : NodeKind.Actor, path[i]);
            Assert.True(s.Accepted, s.Message);
        }
        Assert.True(s!.IsComplete);
        _f.Clock.Advance(TimeSpan.FromSeconds(30)); // dawdling before Submit doesn't count

        var r = await Post<SubmitResponse>(m, "submit");
        double raw = 5.0 * (path.Length - 1);
        double allowance = Math.Min((path.Length - 1) * 0.150, 3.0); // 150 ms credited per accepted step
        Assert.True(r.IsComplete);
        Assert.Equal(links, r.Links);
        Assert.Equal(raw - allowance, r.SecondsTaken, 3);
        Assert.Equal(Scoring.Compute(links, raw - allowance, GameRules.Default), r.Score);
        Assert.Equal(links, r.OptimalLinks);
        Assert.Equal(path.Length, r.ShortestKnownChain.Count);
        Assert.Equal(m.Puzzle.StartActor.Name, r.Chain[0].Name);

        var again = await Post<SubmitResponse>(m, "submit"); // idempotent retry
        Assert.Equal(r.Score, again.Score);
    }

    [Fact]
    public async Task Invalid_steps_are_rejected_and_undo_works()
    {
        var m = await Create(seed: 5);
        long start = m.Puzzle.StartActor.Id;
        var movieOfStart = _f.Seed.Credits.First(c => c.ActorId == start).MovieId;
        var notStartsMovie = _f.Seed.Movies.First(mv => !_f.Seed.Credits.Any(c => c.ActorId == start && c.MovieId == mv.Id)).Id;

        var bad = await Step(m, NodeKind.Movie, notStartsMovie);
        Assert.False(bad.Accepted);
        Assert.Equal(StepError.NotInMovie, bad.Error);

        var wrongKind = await Step(m, NodeKind.Actor, m.Puzzle.TargetActor.Id);
        Assert.Equal(StepError.WrongKind, wrongKind.Error);

        var ok = await Step(m, NodeKind.Movie, movieOfStart);
        Assert.True(ok.Accepted);
        Assert.Equal(2, ok.Chain.Count);

        var repeat = await Step(m, NodeKind.Actor, start);
        Assert.Equal(StepError.Repeated, repeat.Error);

        var undone = await Post<ChainStateResponse>(m, "undo");
        Assert.Single(undone.Chain);
        var nothing = await Post<ChainStateResponse>(m, "undo");
        Assert.False(nothing.Accepted);
    }

    [Fact]
    public async Task Timeout_scores_zero()
    {
        var m = await Create(seed: 8);
        long[] path = PathFor(m);
        await Step(m, NodeKind.Movie, path[1]);
        _f.Clock.Advance(TimeSpan.FromSeconds(125));
        var late = await Step(m, NodeKind.Actor, path[2]);
        Assert.True(late.TimeUp);
        Assert.False(late.Accepted);
        var r = await Post<SubmitResponse>(m, "submit");
        Assert.Equal(0, r.Score);
        Assert.False(r.IsComplete);
    }

    [Fact]
    public async Task Bad_or_missing_token_is_refused()
    {
        var m = await Create();
        var other = await Create(player: "p2");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{m.MatchId}/submit");
        req.Headers.Add("X-Match-Token", other.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(req)).StatusCode);
        var noToken = await _http.PostAsync($"/api/matches/{m.MatchId}/submit", null);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
    }

    [Theory]
    [InlineData(NodeKind.Movie, "matrix", "The Matrix")]
    [InlineData(NodeKind.Movie, "amelie", "Amélie")]
    [InlineData(NodeKind.Movie, "ocean", "Ocean's Eleven")]
    [InlineData(NodeKind.Actor, "penelope", "Penélope Cruz")]
    [InlineData(NodeKind.Actor, "hanks", "Tom Hanks")]
    [InlineData(NodeKind.Actor, "leonardo dicapro", "Leonardo DiCaprio")]
    public async Task Search_handles_accents_the_and_partial_names(NodeKind kind, string q, string expected)
    {
        var m = await Create();
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{m.MatchId}/search?kind={kind.ToString().ToLowerInvariant()}&q={Uri.EscapeDataString(q)}");
        req.Headers.Add("X-Match-Token", m.Token);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<SearchResponse>(ApiFixture.Json))!;
        Assert.Equal(expected, body.Results.First().Name);
    }
}
