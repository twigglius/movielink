using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MovieLink.Editor
{
    /// <summary>
    /// Menu: MovieLink > ... Also callable from CI:
    ///   Unity -batchmode -quit -projectPath client -executeMethod MovieLink.Editor.BuildScript.BuildDevWebGL
    /// </summary>
    public static class BuildScript
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        [MenuItem("MovieLink/Create Main Scene")]
        public static void EnsureMainScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                // GameApp creates itself at runtime; the scene just needs a camera for the clear colour.
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
                }
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[MovieLink] Main scene ready at {ScenePath}");
        }

        [MenuItem("MovieLink/Apply WebGL Settings (Skillz guidance)")]
        public static void ApplyWebGLSettings()
        {
            // Per Skillz web best practices: Brotli, data caching on, small textures, 512 MB memory.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.memorySize = 512;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        }

        [MenuItem("MovieLink/Build Dev WebGL")]
        public static void BuildDevWebGL()
        {
            EnsureMainScene();
            ApplyWebGLSettings();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/WebGL",
                target = BuildTarget.WebGL,
                options = BuildOptions.Development,
            });
            Debug.Log($"[MovieLink] WebGL build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
