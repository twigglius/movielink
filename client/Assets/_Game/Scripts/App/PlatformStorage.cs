using System;
using UnityEngine;

namespace MovieLink.Game.App
{
    /// <summary>
    /// The only place that touches local storage. PlayerPrefs maps to IndexedDB on WebGL and to
    /// NSUserDefaults on iOS, so no browser-only API is needed.
    /// </summary>
    public sealed class PlatformStorage
    {
        private const string PlayerIdKey = "ml.playerId";
        public string LocalPlayerId
        {
            get
            {
                string id = PlayerPrefs.GetString(PlayerIdKey, "");
                if (string.IsNullOrEmpty(id))
                {
                    id = "local-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    PlayerPrefs.SetString(PlayerIdKey, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }

        public string DisplayName
        {
            get => PlayerPrefs.GetString("ml.displayName", "Player");
            set { PlayerPrefs.SetString("ml.displayName", value); PlayerPrefs.Save(); }
        }

        public bool SoundOn { get => GetBool("ml.sound", true); set => SetBool("ml.sound", value); }
        public bool HapticsOn { get => GetBool("ml.haptics", true); set => SetBool("ml.haptics", value); }
        public bool DarkTheme { get => GetBool("ml.dark", true); set => SetBool("ml.dark", value); }
        /// <summary>Off until art rights are clear (see the plan's Assets section).</summary>
        public bool ShowPosters { get => GetBool("ml.posters", false); set => SetBool("ml.posters", value); }

        public int MatchesPlayed => PlayerPrefs.GetInt("ml.matches", 0);
        public int BestScore => PlayerPrefs.GetInt("ml.best", 0);

        public void RecordResult(int score)
        {
            PlayerPrefs.SetInt("ml.matches", MatchesPlayed + 1);
            if (score > BestScore) PlayerPrefs.SetInt("ml.best", score);
            PlayerPrefs.Save();
        }

        private static bool GetBool(string k, bool def) => PlayerPrefs.GetInt(k, def ? 1 : 0) == 1;
        private static void SetBool(string k, bool v) { PlayerPrefs.SetInt(k, v ? 1 : 0); PlayerPrefs.Save(); }
    }
}
