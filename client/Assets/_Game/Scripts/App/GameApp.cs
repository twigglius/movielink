#nullable enable
using MovieLink.Core.Dtos;
using MovieLink.Game.Match;
using MovieLink.Game.Net;
using MovieLink.Game.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MovieLink.Game.App
{
    /// <summary>
    /// Composition root. Picks the platform (Local or Skillz) and the API (HTTP or Fake), builds the
    /// UI Toolkit panel in code, and routes between screens. Created automatically on Play in any scene.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        private AppConfig _config = new AppConfig();
        private PlatformStorage _storage = new PlatformStorage();
        private IMatchPlatform _platform = null!;
        private IGameApi _api = null!;
        private UIDocument _doc = null!;
        private VisualElement _column = null!;
        private MatchController? _match;
        private MatchView? _matchView;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindObjectOfType<GameApp>() != null) return;
            var go = new GameObject("MovieLinkGame");
            DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            _config = AppConfig.Load();
            _storage = new PlatformStorage();

#if SKILLZ_SDK
            _platform = new MovieLink.Game.Skillz.SkillzPlatform();
#else
            _platform = new LocalPlatform(_storage, ShowMainMenu);
#endif
            _platform.MatchStarting += OnMatchStarting;

            if (_config.UseFakeApi)
            {
                var data = Resources.Load<TextAsset>("fake-data");
                _api = new FakeGameApi(data != null ? data.text : "{}", () => Time.realtimeSinceStartupAsDouble);
                Debug.Log("[MovieLink] Using FakeGameApi (offline).");
            }
            else
            {
                _api = new HttpGameApi(_config.ApiBaseUrl);
                Debug.Log($"[MovieLink] Using server at {_config.ApiBaseUrl}.");
            }

            BuildPanel();
            ShowMainMenu();
        }

        private void BuildPanel()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/DefaultTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1080, 1920);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f; // scale by height: portrait layout stays the same on a wide browser window

            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = settings;

            var root = _doc.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Game"));
            root.AddToClassList("app-root");
            _column = new VisualElement();
            _column.AddToClassList("app-column");
            root.Add(_column);
        }

        private void Show(VisualElement screen)
        {
            _column.Clear();
            _column.Add(screen);
        }

        private void ShowMainMenu()
        {
            _matchView = null;
            _match = null;
            var view = new MainMenuView(_storage, _platform);
            view.NewGameClicked += () => _platform.StartNewGame(_config.Tier);
            view.OverlayRequested += overlay => _doc.rootVisualElement.Add(overlay);
            Show(view.Root);
        }

        private async void OnMatchStarting(MatchStartInfo info)
        {
            var controller = new MatchController(_api, () => Time.realtimeSinceStartupAsDouble);
            _match = controller;
            _matchView = new MatchView(controller);
            Show(_matchView.Root);
            controller.Finished += OnFinished;
            try
            {
                await controller.StartAsync(info);
            }
            catch (GameApiException e)
            {
                Debug.LogWarning($"[MovieLink] Could not start match: {e.Message}");
                _matchView.ShowError("Couldn't start the match. " + (e.IsNetworkError ? "Check your connection." : e.Message));
            }
        }

        private void OnFinished(SubmitResponse result)
        {
            // The client reports exactly the server's score. Never compute it here.
            _platform.ReportScore(result.Score, ok =>
            {
                if (!ok) Debug.LogWarning("[MovieLink] Platform did not accept the score.");
            });
            var view = new ResultsView(result);
            view.ContinueClicked += () => _platform.ReturnToLobby();
            Show(view.Root);
            _matchView = null;
        }

        private void Update()
        {
            _match?.Tick();
            _matchView?.Tick();
        }
    }
}
