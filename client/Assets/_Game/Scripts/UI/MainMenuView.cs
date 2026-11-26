#nullable enable
using System;
using MovieLink.Game.App;
using UnityEngine.UIElements;

namespace MovieLink.Game.UI
{
    /// <summary>Game name, Settings and Account placeholders, New Game. Kept thin: under Skillz, Skillz is the lobby.</summary>
    public sealed class MainMenuView
    {
        private readonly PlatformStorage _storage;
        private readonly IMatchPlatform _platform;

        public VisualElement Root { get; }
        public event Action? NewGameClicked;
        public event Action<VisualElement>? OverlayRequested;

        public MainMenuView(PlatformStorage storage, IMatchPlatform platform)
        {
            _storage = storage;
            _platform = platform;
            Root = Ui.Load("MainMenu");
            Root.Q<Button>("new-game-button").clicked += () => NewGameClicked?.Invoke();
            Root.Q<Button>("settings-button").clicked += () => OverlayRequested?.Invoke(SettingsPanel());
            Root.Q<Button>("account-button").clicked += () => OverlayRequested?.Invoke(AccountPanel());
            Root.Q<Button>("how-to-play-button").clicked += () => OverlayRequested?.Invoke(HowToPlayPanel());
            Root.Q<Button>("credits-button").clicked += () => OverlayRequested?.Invoke(CreditsPanel());
            Root.Q<Button>("account-button").text = Ui.Initials(_platform.PlayerDisplayName);
        }

        private VisualElement SettingsPanel() => Ui.Overlay("Settings", p =>
        {
            p.Add(Toggle("Sound", _storage.SoundOn, v => _storage.SoundOn = v));
            p.Add(Toggle("Haptics", _storage.HapticsOn, v => _storage.HapticsOn = v));
            p.Add(Toggle("Dark theme", _storage.DarkTheme, v => _storage.DarkTheme = v));
            var posters = Toggle("Show posters", false, _ => { });
            posters.SetEnabled(false); // off until art rights are clear
            p.Add(posters);
            p.Add(Ui.Text("Placeholder: settings are saved but not wired to sound, haptics or theme yet.", "placeholder-note"));
        });

        private VisualElement AccountPanel() => Ui.Overlay("Account", p =>
        {
            p.Add(Ui.Text(_platform.PlayerDisplayName, "panel-text"));
            p.Add(Ui.Text($"Matches played: {_storage.MatchesPlayed}", "panel-text"));
            p.Add(Ui.Text($"Best score: {_storage.BestScore}", "panel-text"));
            p.Add(Ui.Text(_platform.IsSkillz
                ? "Your Skillz account manages identity and balances."
                : "Placeholder: local stats only. Under Skillz this shows your Skillz player info.", "placeholder-note"));
        });

        private static VisualElement HowToPlayPanel() => Ui.Overlay("How to play", p =>
        {
            p.Add(Ui.Text("Link the start actor to the target actor through movies they were in together.", "panel-text"));
            p.Add(Ui.Text("Pick a movie the current actor was in, then a co-star from that movie. Repeat until you reach the target.", "panel-text"));
            p.Add(Ui.Text("Fewer movies always wins. Time only breaks ties. You have 2 minutes.", "panel-text"));
        });

        private static VisualElement CreditsPanel() => Ui.Overlay("Credits", p =>
        {
            p.Add(Ui.Text("Movie and cast data: Wikidata (CC0).", "panel-text"));
            p.Add(Ui.Text("Development builds use a small hand-made sample dataset.", "panel-text"));
            p.Add(Ui.Text("Art, fonts and sounds: see docs/asset-licenses.md.", "panel-text"));
        });

        private static Toggle Toggle(string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = value };
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }
    }
}
