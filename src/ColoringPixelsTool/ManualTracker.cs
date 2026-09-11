using System;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 记录「人工涂色」行为：左键按下到抬起之间，画面上新涂对了多少格。
    /// 这个数字就是等级系统里说的「涂色率」（每击平均涂到的格数）。
    ///
    /// 只统计真正由鼠标点出来的进度：面板上的点击、人工辅助扫描产生的
    /// 合成点击都会被排除。
    /// </summary>
    internal sealed class ManualTracker : MonoBehaviour
    {
        public static ManualTracker Instance;

        private bool _down;
        private int _startPainted;
        private float _downTime;

        public static void Ensure(Transform parent)
        {
            if (Instance != null) return;
            var go = new GameObject("ColoringPixelsTool.ManualTracker");
            if (parent != null) go.transform.SetParent(parent, false);
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<ManualTracker>();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            var ct = GameApi.Ct;
            if (!GameApi.InLevel(ct)) { _down = false; return; }

            if (AssistOverlay.Instance != null && AssistOverlay.Instance.Engine != null
                && AssistOverlay.Instance.Engine.Running)
            {
                _down = false;
                return;
            }

            if (Input.GetMouseButtonDown(0) && !CheatPanel.IsMouseOverPanel)
            {
                _down = true;
                _startPainted = GameApi.PaintedPixels(ct);
                _downTime = Time.unscaledTime;
                return;
            }

            if (_down && Input.GetMouseButtonUp(0))
            {
                _down = false;
                int now = GameApi.PaintedPixels(ct);
                int delta = now - _startPainted;
                if (delta < 0) delta = 0;
                if (delta > 3000) delta = 3000;   // 防止把自动模块的成果算进来
                UserProfile.RecordManualClick(delta, Time.unscaledTime - _downTime);
            }
        }
    }
}
