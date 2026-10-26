using System;
using System.Diagnostics;
using UnityEngine;

namespace VCR.Runtime.Application
{
    /// <summary>
    /// Same observable quit path in every diagnostic Player (A/B/C/D).
    /// Inert outside comparison scenes. Never force-kills or hides the window.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(11000)]
    public sealed class ExitComparisonProbe : MonoBehaviour
    {
        [SerializeField] private string variant = "UNSET";
        private Stopwatch _quitClock;
        private bool _quitRequested;
        private bool _autoQuit;

        public void Configure(string comparisonVariant)
        {
            variant = comparisonVariant;
        }

        private void Awake()
        {
            _autoQuit = Array.Exists(Environment.GetCommandLineArgs(), argument =>
                string.Equals(argument, "--vcr-exit-compare-autorun",
                    StringComparison.OrdinalIgnoreCase));
        }

        private void OnEnable()
        {
            UnityEngine.Application.wantsToQuit += OnWantsToQuit;
        }

        private bool OnWantsToQuit()
        {
            if (_quitClock == null)
            {
                _quitClock = Stopwatch.StartNew();
                UnityEngine.Debug.Log("VCR exit: quit requested (t=0 ms)");
                UnityEngine.Debug.Log("VCR exit comparison: variant=" + variant +
                                      ", quit requested");
            }
            return true;
        }

        private void Update()
        {
            if (_autoQuit && !_quitRequested &&
                Time.realtimeSinceStartup >= 3.0f)
            {
                RequestQuit();
            }
            if (!_quitRequested && Input.GetKeyDown(KeyCode.Escape))
            {
                RequestQuit();
            }
        }

        private void OnGUI()
        {
            const int width = 480;
            const int height = 150;
            var rect = new Rect(
                Mathf.Max(0, (Screen.width - width) / 2),
                Mathf.Max(0, (Screen.height - height) / 2), width, height);
            GUI.Box(rect, "Windows 종료 지연 비교 빌드 " + variant);
            GUI.Label(new Rect(rect.x + 24, rect.y + 36, width - 48, 32),
                "같은 조건에서 정상 종료 시간을 측정합니다.");
            if (GUI.Button(new Rect(rect.x + 24, rect.y + 83, width - 48, 45),
                "정상 종료 (Esc)"))
            {
                RequestQuit();
            }
        }

        private void RequestQuit()
        {
            if (_quitRequested) return;
            _quitRequested = true;
            UnityEngine.Application.Quit(0);
        }

        private void OnApplicationQuit()
        {
            UnityEngine.Debug.Log("VCR exit comparison: " + variant +
                " OnApplicationQuit elapsed=" +
                (_quitClock?.ElapsedMilliseconds ?? -1) + " ms");
        }

        private void OnDisable()
        {
            UnityEngine.Application.wantsToQuit -= OnWantsToQuit;
        }
    }
}
