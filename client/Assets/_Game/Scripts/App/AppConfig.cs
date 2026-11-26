using System;
using Newtonsoft.Json;
using UnityEngine;

namespace MovieLink.Game.App
{
    /// <summary>
    /// Build-time settings, read from Resources/movielink-config.json.
    /// Tier 1 testing (Unity Editor): useFakeApi = true, no server needed.
    /// Tier 2 (dev web build): useFakeApi = false and apiBaseUrl pointing at a local or staging server.
    /// </summary>
    [Serializable]
    public sealed class AppConfig
    {
        public string ApiBaseUrl { get; set; } = "http://localhost:5080";
        public bool UseFakeApi { get; set; } = true;
        public int Tier { get; set; } = 1;

        public static AppConfig Load()
        {
            var asset = Resources.Load<TextAsset>("movielink-config");
            if (asset == null) return new AppConfig();
            try { return JsonConvert.DeserializeObject<AppConfig>(asset.text) ?? new AppConfig(); }
            catch (Exception e)
            {
                Debug.LogWarning($"movielink-config.json is invalid, using defaults: {e.Message}");
                return new AppConfig();
            }
        }
    }
}
