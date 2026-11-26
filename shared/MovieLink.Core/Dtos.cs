#nullable enable
using System;
using System.Collections.Generic;

// Wire contracts between the Unity client and the server. Plain classes with public get/set
// so both System.Text.Json (server) and Newtonsoft.Json (Unity) bind them the same way.
// JSON uses camelCase property names and string enums ("actor", "movie").
// The match token travels in the X-Match-Token header, not in bodies.
namespace MovieLink.Core.Dtos
{
    public sealed class NodeDto
    {
        public NodeKind Kind { get; set; }
        public long Id { get; set; }
        public string Name { get; set; } = "";
        /// <summary>Disambiguation line, e.g. "b. 1974" for actors, "2002" for movies.</summary>
        public string? Subtitle { get; set; }

        public NodeRef ToRef() => new NodeRef(Kind, Id);
    }

    public sealed class PuzzleDto
    {
        public long PuzzleId { get; set; }
        public int DatasetVersion { get; set; }
        public NodeDto StartActor { get; set; } = new NodeDto();
        public NodeDto TargetActor { get; set; } = new NodeDto();
        public int MaxLinks { get; set; }
        public int TimeLimitSeconds { get; set; }
    }

    public sealed class CreateMatchRequest
    {
        /// <summary>"local" for dev builds, "skillz" for Skillz builds.</summary>
        public string Platform { get; set; } = "local";
        public string PlayerId { get; set; } = "";
        /// <summary>Skillz match id when on Skillz; free text locally.</summary>
        public string? ExternalMatchId { get; set; }
        public int Tier { get; set; } = 1;
        /// <summary>
        /// The first Skillz random draw. Both opponents send the same value, so both get the same puzzle.
        /// Null in local play: the server picks at random.
        /// </summary>
        public int? PuzzleSeed { get; set; }
    }

    public sealed class CreateMatchResponse
    {
        public Guid MatchId { get; set; }
        public string Token { get; set; } = "";
        public PuzzleDto Puzzle { get; set; } = new PuzzleDto();
        public DateTimeOffset ServerStartedAt { get; set; }
    }

    public sealed class SearchResponse
    {
        public List<NodeDto> Results { get; set; } = new List<NodeDto>();
    }

    /// <summary>Every match call after creation sends the match token in the X-Match-Token header.</summary>
    public sealed class StepRequest
    {
        public NodeKind Kind { get; set; }
        public long Id { get; set; }
    }

    public sealed class ChainStateResponse
    {
        public bool Accepted { get; set; }
        public StepError Error { get; set; }
        public string? Message { get; set; }
        public List<NodeDto> Chain { get; set; } = new List<NodeDto>();
        public int Links { get; set; }
        public bool IsComplete { get; set; }
        public double SecondsElapsed { get; set; }
        /// <summary>The round timer ran out; the client should submit (score 0 unless already complete).</summary>
        public bool TimeUp { get; set; }
    }

    public sealed class SubmitResponse
    {
        public Guid MatchId { get; set; }
        public int Score { get; set; }
        public int Links { get; set; }
        public bool IsComplete { get; set; }
        public double SecondsTaken { get; set; }
        public int OptimalLinks { get; set; }
        public List<NodeDto> Chain { get; set; } = new List<NodeDto>();
        public List<NodeDto> ShortestKnownChain { get; set; } = new List<NodeDto>();
    }
}
