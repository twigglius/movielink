#nullable enable
// Compiled only when the Skillz SDK is imported and SKILLZ_SDK is added to Scripting Define Symbols
// (Player Settings > Other Settings). The dev web build never defines it, so it runs without Skillz.
//
// TODO(phase 3): check every call below against the pinned Skillz SDK version (v2026.0.x) before use.
// The names follow the Skillz Unity docs: SkillzCrossPlatform.LaunchSkillz, SkillzMatchDelegate,
// SkillzCrossPlatform.Random, SubmitScore and ReturnToSkillz.
#if SKILLZ_SDK
using System;
using MovieLink.Game.App;
using UnityEngine;

namespace MovieLink.Game.Skillz
{
    public sealed class SkillzPlatform : IMatchPlatform, SkillzMatchDelegate
    {
        private int _tier = 1;

        public bool IsSkillz => true;
        public event Action<MatchStartInfo>? MatchStarting;

        public string PlayerDisplayName
        {
            get
            {
                try { return SkillzCrossPlatform.GetPlayer()?.DisplayName ?? "Player"; }
                catch { return "Player"; }
            }
        }

        public void StartNewGame(int tier)
        {
            _tier = tier;
            SkillzCrossPlatform.LaunchSkillz(this);
        }

        // --- SkillzMatchDelegate ---
        public void OnMatchWillBegin(SkillzSDK.Match matchInfo)
        {
            // The FIRST Skillz random call of the match picks the puzzle. Both opponents get the same
            // sequence, so they get the same puzzle. Nothing else may call Skillz random before this.
            int seed = SkillzCrossPlatform.Random.Range(0, int.MaxValue);
            int tier = _tier;
            // Tournament templates can pass the tier as a gameplay parameter.
            if (matchInfo.GameParams != null && matchInfo.GameParams.TryGetValue("tier", out var t) && int.TryParse(t, out int parsed))
                tier = parsed;

            MatchStarting?.Invoke(new MatchStartInfo
            {
                Platform = "skillz",
                PlayerId = SkillzCrossPlatform.GetPlayer()?.ID.ToString() ?? "",
                ExternalMatchId = matchInfo.ID?.ToString(),
                Tier = tier,
                PuzzleSeed = seed,
            });
        }

        public void OnSkillzWillExit()
        {
            // Back to our main screen; nothing to clean up yet.
        }

        public void OnProgressionRoomEnter()
        {
            // Progression rooms are not used.
        }

        public void ReportScore(int score, Action<bool> onDone)
        {
            SkillzCrossPlatform.SubmitScore(score,
                () => onDone(true),
                error =>
                {
                    Debug.LogWarning($"Skillz SubmitScore failed: {error}");
                    onDone(false);
                });
        }

        public void ReturnToLobby() => SkillzCrossPlatform.ReturnToSkillz();
    }
}
#endif
