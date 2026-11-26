using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MovieLink.Core;
using Xunit;

namespace MovieLink.Core.Tests;

public class GoldenTests
{
    private static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))).RootElement;

    private static NodeRef Parse(string s) =>
        s[0] == 'A' ? NodeRef.Actor(long.Parse(s[1..])) : NodeRef.Movie(long.Parse(s[1..]));

    private static CreditSet Credits(JsonElement root) =>
        new(root.GetProperty("credits").EnumerateArray().Select(c => (c[0].GetInt64(), c[1].GetInt64())));

    public static IEnumerable<object[]> ChainCases() => CaseNames("cases");
    public static IEnumerable<object[]> LimitCases() => CaseNames("limitCases");

    private static IEnumerable<object[]> CaseNames(string group) =>
        Load("chain-cases.json").GetProperty(group).EnumerateArray()
            .Select(c => new object[] { group, c.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(ChainCases))]
    [MemberData(nameof(LimitCases))]
    public void Chain_matches_golden(string group, string name)
    {
        var root = Load("chain-cases.json");
        var c = root.GetProperty(group).EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        int maxLinks = c.TryGetProperty("maxLinks", out var ml) ? ml.GetInt32() : root.GetProperty("rules").GetProperty("maxLinks").GetInt32();
        var rules = new GameRules(maxLinks, root.GetProperty("rules").GetProperty("timeLimitSeconds").GetInt32());
        var chain = c.GetProperty("chain").EnumerateArray().Select(x => Parse(x.GetString()!)).ToList();

        var result = ChainValidator.ValidateChain(
            c.GetProperty("start").GetInt64(), c.GetProperty("target").GetInt64(), chain, Credits(root), rules);

        Assert.Equal(Enum.Parse<StepError>(c.GetProperty("error").GetString()!), result.Error);
        Assert.Equal(c.TryGetProperty("errorIndex", out var ei) ? ei.GetInt32() : -1, result.ErrorIndex);
        Assert.Equal(c.GetProperty("links").GetInt32(), result.Links);
        Assert.Equal(c.GetProperty("complete").GetBoolean(), result.IsComplete);
    }

    public static IEnumerable<object[]> ScoreCases() =>
        Load("score-cases.json").GetProperty("cases").EnumerateArray()
            .Select(c => new object[] { c.GetProperty("links").GetInt32(), c.GetProperty("seconds").GetDouble(), c.GetProperty("score").GetInt32() });

    [Theory]
    [MemberData(nameof(ScoreCases))]
    public void Score_matches_golden(int links, double seconds, int expected) =>
        Assert.Equal(expected, Scoring.Compute(links, seconds, GameRules.Default));

    [Fact]
    public void Fewer_links_always_beat_faster_time()
    {
        var rules = GameRules.Default;
        for (int l = 1; l < rules.MaxLinks; l++)
        {
            int slowestShorter = Scoring.Compute(l, rules.TimeLimitSeconds - 0.0001, rules);
            int fastestLonger = Scoring.Compute(l + 1, 0, rules);
            Assert.True(slowestShorter > fastestLonger, $"L={l}: {slowestShorter} <= {fastestLonger}");
        }
    }

    [Theory]
    [InlineData("Amélie", "amelie")]
    [InlineData("The Matrix", "matrix")]
    [InlineData("  Penélope   Cruz ", "penelope cruz")]
    [InlineData("Ocean's Eleven", "oceans eleven")]
    [InlineData("Spider-Man: No Way Home", "spider man no way home")]
    [InlineData("Dr. Strangelove", "dr strangelove")]
    [InlineData("", "")]
    public void Normalizes_for_search(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.ForSearch(input));
}
