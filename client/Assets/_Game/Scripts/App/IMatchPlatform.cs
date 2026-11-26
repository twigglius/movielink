#nullable enable
using System;

namespace MovieLink.Game.App
{
    /// <summary>What a platform hands the game when a match begins.</summary>
    public sealed class MatchStartInfo
    {
        public string Platform = "local";
        public string PlayerId = "";
        public string? ExternalMatchId;
        public int Tier = 1;
        /// <summary>First shared random draw (Skillz). Null in local play.</summary>
        public int? PuzzleSeed;
    }

    /// <summary>
    /// Everything Skillz-specific sits behind this. Game code never calls the Skillz SDK directly.
    /// LocalPlatform: dev web build and the Editor. SkillzPlatform: iOS and Skillz web.
    /// </summary>
    public interface IMatchPlatform
    {
        /// <summary>True when Skillz owns the lobby (our main screen hands off to it).</summary>
        bool IsSkillz { get; }

        /// <summary>Raised when a match is ready to start (local: right away; Skillz: OnMatchWillBegin).</summary>
        event Action<MatchStartInfo> MatchStarting;

        /// <summary>New Game button. Local: starts a practice match. Skillz: LaunchSkillz().</summary>
        void StartNewGame(int tier);

        /// <summary>Report the server-computed score. onDone runs after the platform accepts it (or fails).</summary>
        void ReportScore(int score, Action<bool> onDone);

        /// <summary>Results "Continue". Local: back to our main screen. Skillz: ReturnToSkillz().</summary>
        void ReturnToLobby();

        /// <summary>Player shown on the Account panel. Under Skillz this is Skillz's player.</summary>
        string PlayerDisplayName { get; }
    }
}
