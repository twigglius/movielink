using System.Text.Json;
using MovieLink.Core;
using MovieLink.Tools;
using Xunit;

namespace MovieLink.Tools.Tests;

public class PuzzleGenTests
{
    // 1 -100- 2 -101- 3 -102- 4, and a shortcut 1 -103- 5 -104- 4, plus 1 -105- 6 -106- 4.
    private static readonly (long, long)[] Credits =
    {
        (1, 100), (2, 100), (2, 101), (3, 101), (3, 102), (4, 102),
        (1, 103), (5, 103), (5, 104), (4, 104),
        (1, 105), (6, 105), (6, 106), (4, 106),
        (7, 107), (8, 107),
    };

    [Fact]
    public void Bfs_finds_shortest_links_and_counts_paths()
    {
        var g = new MovieGraph(Credits);
        var r = g.Bfs(1, 6);
        Assert.Equal(0, g.Distance(r, 1));
        Assert.Equal(1, g.Distance(r, 2));
        Assert.Equal(2, g.Distance(r, 3));
        Assert.Equal(2, g.Distance(r, 4));       // via 5 or 6, not the 3-link route
        Assert.Equal(2, g.PathCount(r, 4));
        Assert.Equal(-1, g.Distance(r, 7));      // disconnected
    }

    [Fact]
    public void Example_path_is_a_valid_complete_chain()
    {
        var g = new MovieGraph(Credits);
        var r = g.Bfs(1, 6);
        long[] path = g.ExamplePath(r, 4);
        var chain = path.Select((id, i) => i % 2 == 0 ? NodeRef.Actor(id) : NodeRef.Movie(id)).ToList();
        var result = ChainValidator.ValidateChain(1, 4, chain, new CreditSet(Credits), GameRules.Default);
        Assert.True(result.IsValid);
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Links);
    }

    [Fact]
    public void Generator_is_deterministic_and_respects_filters()
    {
        var g = new MovieGraph(Credits);
        var pop = Enumerable.Range(1, 8).ToDictionary(i => (long)i, i => 10.0 - i);
        var o = new PuzzleGenOptions { Count = 100, MinLinks = 2, MaxLinks = 3, TopActors = 8, MinShortestPaths = 1, Seed = 42 };
        var a = PuzzleGenerator.Generate(g, pop, o);
        var b = PuzzleGenerator.Generate(g, pop, o);
        Assert.NotEmpty(a);
        Assert.Equal(a.Select(p => (p.StartActorId, p.TargetActorId)), b.Select(p => (p.StartActorId, p.TargetActorId)));
        Assert.All(a, p => Assert.InRange(p.OptimalLinks, 2, 3));
        Assert.Equal(Enumerable.Range(0, a.Count), a.Select(p => p.PoolIndex));
        // Every example path is valid from its (possibly flipped) start to its target.
        var credits = new CreditSet(Credits);
        foreach (var p in a)
        {
            var chain = p.ExamplePath.Select((id, i) => i % 2 == 0 ? NodeRef.Actor(id) : NodeRef.Movie(id)).ToList();
            var res = ChainValidator.ValidateChain(p.StartActorId, p.TargetActorId, chain, credits, GameRules.Default);
            Assert.True(res.IsComplete, $"{p.StartActorId}->{p.TargetActorId}");
            Assert.Equal(p.OptimalLinks, res.Links);
        }
        // No pair of actors who share a movie.
        Assert.DoesNotContain(a, p => p.OptimalLinks < 2);
    }

    [Fact]
    public void Min_paths_filter_drops_single_route_puzzles()
    {
        var g = new MovieGraph(Credits);
        var pop = Enumerable.Range(1, 8).ToDictionary(i => (long)i, i => 1.0);
        var pool = PuzzleGenerator.Generate(g, pop, new PuzzleGenOptions { MinShortestPaths = 2, TopActors = 8, MinLinks = 2, MaxLinks = 3 });
        Assert.All(pool, p => Assert.True(p.ShortestPathCount >= 2));
        Assert.Contains(pool, p => (p.StartActorId, p.TargetActorId) is (1, 4) or (4, 1));
    }

    [Fact]
    public void Wikidata_rows_become_a_dataset()
    {
        const string sparql = """
        {"results":{"bindings":[
          {"film":{"value":"http://www.wikidata.org/entity/Q100"},"filmLabel":{"value":"Film A"},"date":{"value":"2002-12-25T00:00:00Z"},"filmLinks":{"value":"40"},
           "actor":{"value":"http://www.wikidata.org/entity/Q1"},"actorLabel":{"value":"Actor One"},"actorLinks":{"value":"60"},"birth":{"value":"1974-11-11T00:00:00Z"},"ord":{"value":"1"}},
          {"film":{"value":"http://www.wikidata.org/entity/Q100"},"filmLabel":{"value":"Film A"},"date":{"value":"2003-02-01T00:00:00Z"},"filmLinks":{"value":"40"},
           "actor":{"value":"http://www.wikidata.org/entity/Q1"},"actorLabel":{"value":"Actor One"},"actorLinks":{"value":"60"}},
          {"film":{"value":"http://www.wikidata.org/entity/Q100"},"filmLabel":{"value":"Film A"},"date":{"value":"2002-12-25T00:00:00Z"},"filmLinks":{"value":"40"},
           "actor":{"value":"http://www.wikidata.org/entity/Q77"},"actorLabel":{"value":"Q77"},"actorLinks":{"value":"20"}}
        ]}}
        """;
        var rows = WikidataImporter.ParseSparql(JsonDocument.Parse(sparql));
        var file = WikidataImporter.Build(rows, "test");
        var actor = Assert.Single(file.Actors);       // unlabelled Q77 dropped
        Assert.Equal(1, actor.Id);
        Assert.Equal(1974, actor.BirthYear);
        var movie = Assert.Single(file.Movies);
        Assert.Equal(2002, movie.Year);               // earliest release
        var credit = Assert.Single(file.Credits);     // duplicate rows merged
        Assert.Equal(1, credit.BillingOrder);
    }
}
