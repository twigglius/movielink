#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MovieLink.Game.App
{
    /// <summary>
    /// Await-able delay driven by Unity's frame loop. Task.Delay relies on thread timers,
    /// which don't run in WebGL, so use this instead everywhere in the client.
    /// </summary>
    public sealed class MainThreadDelay : MonoBehaviour
    {
        private static MainThreadDelay? _instance;
        private readonly List<(float due, TaskCompletionSource<bool> tcs)> _waits = new List<(float, TaskCompletionSource<bool>)>();

        public static Task Seconds(float seconds)
        {
            if (_instance == null)
            {
                var go = new GameObject("[MainThreadDelay]") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<MainThreadDelay>();
            }
            var tcs = new TaskCompletionSource<bool>();
            _instance._waits.Add((Time.realtimeSinceStartup + seconds, tcs));
            return tcs.Task;
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _waits.Count - 1; i >= 0; i--)
            {
                if (_waits[i].due <= now)
                {
                    var tcs = _waits[i].tcs;
                    _waits.RemoveAt(i);
                    tcs.TrySetResult(true);
                }
            }
        }
    }
}
