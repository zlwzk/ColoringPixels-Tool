using System;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 记录「人工涂色」行为：左键按下到抬起之间，画面上新涂对了多少格。
    /// 这个数字就是等级系统里说的「涂色率」（每击平均涂到的格数），经验记在「人工辅助」那条线上。
    ///
    /// 两种来源分开处理：
    ///   · 手动点击 / 拖涂：按下到抬起之间的增量 → <see cref="UserProfile.RecordManualClick"/>；
    ///   · 人工辅助扫描引擎替你涂掉的：引擎 Running 期间按增量轮询 →
    ///     <see cref="UserProfile.RecordAssistPaint"/>（它在「人工辅助」分区里，所以同样归人工辅助轨）。
    /// 面板上的点击不算。
    /// </summary>
    internal sealed class ManualTracker : MonoBehaviour
    {
        public static ManualTracker Instance;

        private bool _down;
        private int _startPainted;
        private float _downTime;

        // 扫描引擎那条线的采样：引擎跑的时候没法用「按下 / 抬起」切分，只能轮询增量
        private int _assistPainted = -1;
        private float _assistTimer;

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
            if (!GameApi.InLevel(ct))
            {
                _down = false;
                _assistPainted = -1;
                return;
            }

            if (AssistOverlay.Instance != null && AssistOverlay.Instance.Engine != null
                && AssistOverlay.Instance.Engine.Running)
            {
                _down = false;
                TrackAssistPaint(ct);
                return;
            }

            // 引擎刚停：把最后一次增量结掉，再回到手动统计
            if (_assistPainted >= 0) TrackAssistPaint(ct, true);
            _assistPainted = -1;

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

                // 手工把最后几格涂完 → 这张图的「完成」成果记在人工辅助轨上
                if (delta > 0 && GameApi.RemainingPixels(ct) <= 0)
                    UserProfile.RecordImageCompleted(GameApi.TotalPixels(ct), XpTrack.Manual);
            }
        }

        /// <summary>
        /// 人工辅助扫描引擎涂掉的格数：每 0.2 秒看一次增量。
        /// <paramref name="flush"/> 为真时无视节流，把最后一次增量立刻结掉。
        /// </summary>
        private void TrackAssistPaint(ClickTest ct, bool flush = false)
        {
            _assistTimer -= Time.unscaledDeltaTime;
            if (!flush && _assistTimer > 0f) return;
            _assistTimer = 0.2f;

            int now = GameApi.PaintedPixels(ct);
            if (_assistPainted < 0)
            {
                _assistPainted = now;
                return;
            }

            int delta = now - _assistPainted;
            _assistPainted = now;
            if (delta > 0) UserProfile.RecordAssistPaint(delta);
        }
    }
}
