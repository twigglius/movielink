#nullable enable
using System;

namespace MovieLink.Game.App
{
    /// <summary>Dev builds without Skillz: New Game starts a practice match immediately.</summary>
    public sealed class LocalPlatform : IMatchPlatform
    {
        private readonly PlatformStorage _storage;
        private readonly Action _showMainMenu;

        public LocalPlatform(PlatformStorage storage, Action showMainMenu)
        {
            _storage = storage;
            _showMainMenu = showMainMenu;
        }

        public bool IsSkillz => false;
        public event Action<MatchStartInfo>? MatchStarting;
        public string PlayerDisplayName => _storage.DisplayName;

        public void StartNewGame(int tier) => MatchStarting?.Invoke(new MatchStartInfo
        {
            Platform = "local",
            PlayerId = _storage.LocalPlayerId,
            Tier = tier,
            PuzzleSeed = null,
        });

        public void ReportScore(int score, Action<bool> onDone)
        {
            _storage.RecordResult(score);
            onDone(true);
        }

        public void ReturnToLobby() => _showMainMenu();
    }
}
