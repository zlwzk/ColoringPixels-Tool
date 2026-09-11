using System;
using ColoringPixelsTool.Assist;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 「人工辅助」的屏幕覆盖层：
    ///   * F6  开始 / 暂停 / 继续（等同独立助手 PixelAssist 的 F6）
    ///   * F7  全屏拖拽框选扫描区域（可拖动四角微调）
    ///   * F8  停止
    ///   * F9  只扫当前这一行
    ///   * F10 停止并从头重新整屏扫描
    ///   * F11 框选一个格子，按画布推算扫描行数与采样步长
    ///   * F12 显示 / 隐藏覆盖层
    ///
    /// 这一层是独立于主面板的，面板关闭时依然存在（因为要一边涂色一边看区域）。
    /// </summary>
    internal sealed class AssistOverlay : MonoBehaviour
    {
        public static AssistOverlay Instance;

        public static KeyCode RunKey = KeyCode.F6;
        public static KeyCode SelectKey = KeyCode.F7;
        public static KeyCode StopKey = KeyCode.F8;
        public static KeyCode TestRowKey = KeyCode.F9;
        public static KeyCode RestartKey = KeyCode.F10;
        public static KeyCode CalibrateKey = KeyCode.F11;
        public static KeyCode OverlayKey = KeyCode.F12;

        public AssistEngine Engine;

        private bool _showOverlay = true;
        private bool _selecting;
        private int _selectMode;   // 0 = 框选区域，1 = 框选一个格子做校准
        private Vector2 _selStart;
        private Vector2 _selNow;
        private bool _dirty;
        private float _saveTimer;

        private int _dragCorner = -1;

        private Texture2D _dim;

        public static void Init(BepInEx.Configuration.ConfigFile config)
        {
            if (config != null)
            {
                // 独立的热键分组：老的 Assist 分组里存过 F8=开始 / F9=停止，
                // 直接复用会把两套语义搅在一起，所以另起一段，保证默认值生效。
                const string S = "AssistHotkeys";
                RunKey = config.Bind(S, "RunKey", KeyCode.F6, "人工辅助：开始/暂停/继续热键").Value;
                SelectKey = config.Bind(S, "SelectKey", KeyCode.F7, "人工辅助：框选区域热键").Value;
                StopKey = config.Bind(S, "StopKey", KeyCode.F8, "人工辅助：停止热键").Value;
                TestRowKey = config.Bind(S, "TestRowKey", KeyCode.F9, "人工辅助：只扫当前这一行").Value;
                RestartKey = config.Bind(S, "RestartKey", KeyCode.F10, "人工辅助：停止并重新整屏扫描").Value;
                CalibrateKey = config.Bind(S, "CalibrateKey", KeyCode.F11, "人工辅助：框选一个格子做校准").Value;
                OverlayKey = config.Bind(S, "OverlayKey", KeyCode.F12, "人工辅助：显示/隐藏覆盖层热键").Value;
            }

            try
            {
                if (config != null && !string.IsNullOrEmpty(config.ConfigFilePath))
                    AssistStore.Dir = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(config.ConfigFilePath), "ColoringPixelsTool.Assist");
            }
            catch (Exception) { }

            if (Instance != null) return;

            var go = new GameObject("ColoringPixelsTool.AssistOverlay");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<AssistOverlay>();
        }

        private void Awake()
        {
            Instance = this;
            Engine = new AssistEngine();

            AssistRegion saved = AssistStore.LoadRegion();
            if (saved != null && saved.HasRegion) Engine.Region.CopyFrom(saved);

            AssistSettings st = AssistStore.LoadSettings();
            if (st != null) CopySettings(st, Engine.S);
        }

        private static void CopySettings(AssistSettings from, AssistSettings to)
        {
            to.Rows = from.Rows;
            to.Speed = from.Speed;
            to.Step = from.Step;
            to.RowPauseMs = from.RowPauseMs;
            to.Snake = from.Snake;
            to.EdgeMargin = from.EdgeMargin;
            to.StartDelayMs = from.StartDelayMs;
            to.HoldButton = from.HoldButton;
            to.AutoStopMinutes = from.AutoStopMinutes;
            to.AutoSwitchEveryRows = from.AutoSwitchEveryRows;
            to.AutoSwitchKeyVk = from.AutoSwitchKeyVk;
            to.AutoSwitchWaitMs = from.AutoSwitchWaitMs;
            to.FailRadius = from.FailRadius;
            to.DetectIntervention = from.DetectIntervention;
        }

        public void Save()
        {
            AssistStore.SaveRegion(Engine.Region);
            AssistStore.SaveSettings(Engine.S);
            _dirty = false;
        }

        /// <summary>参数被改动后调用，几秒后自动落盘，避免拖动滑块时频繁写文件。</summary>
        public void MarkDirty()
        {
            _dirty = true;
            _saveTimer = 3f;
        }

        /// <summary>供面板按钮调用：等同于按下 F7。</summary>
        public void BeginSelectFromUi()
        {
            _showOverlay = true;
            BeginSelect(0);
        }

        /// <summary>供面板按钮调用：等同于按下 F11，框选一个格子做校准。</summary>
        public void BeginCalibrateFromUi()
        {
            _showOverlay = true;
            BeginSelect(1);
        }

        /// <summary>供面板按钮调用：等同于按下 F8。</summary>
        public void StopFromUi()
        {
            Engine.Stop();
            Save();
        }

        /// <summary>供面板按钮调用：等同于按下 F9。</summary>
        public void TestRowFromUi()
        {
            Engine.StartSingleRow(Math.Max(0, Engine.CurrentRow));
        }

        /// <summary>供面板按钮调用：等同于按下 F10。</summary>
        public void RestartFromUi()
        {
            Engine.Stop();
            Engine.Start();
        }

        /// <summary>把一组参数拷贝到另一组（供预设加载使用）。</summary>
        public static void CopyInto(AssistSettings from, AssistSettings to)
        {
            CopySettings(from, to);
        }

        // ---------------------------------------------------------------- 每帧

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (Input.GetKeyDown(RunKey)) ToggleRun();
            if (Input.GetKeyDown(SelectKey)) BeginSelect(0);
            if (Input.GetKeyDown(StopKey)) StopFromUi();
            if (Input.GetKeyDown(TestRowKey)) TestRowFromUi();
            if (Input.GetKeyDown(RestartKey)) RestartFromUi();
            if (Input.GetKeyDown(CalibrateKey)) BeginSelect(1);
            if (Input.GetKeyDown(OverlayKey)) _showOverlay = !_showOverlay;

            HandleSelection();
            HandleCornerDrag();

            Engine.Tick(dt);

            if (_dirty)
            {
                _saveTimer -= dt;
                if (_saveTimer <= 0f) Save();
            }
        }

        public void ToggleRun()
        {
            switch (Engine.State)
            {
                case AssistState.Paused:
                case AssistState.WaitingColour:
                case AssistState.RowPause:
                    Engine.Resume();
                    return;
                case AssistState.Countdown:
                case AssistState.Scanning:
                    Engine.Pause();
                    return;
            }
            Engine.Start();
        }

        private void BeginSelect(int mode)
        {
            _selectMode = mode;
            _selecting = true;
            _selStart = GuiMouse();
            _selNow = _selStart;
        }

        private static Vector2 GuiMouse()
        {
            return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        }

        private void HandleSelection()
        {
            if (!_selecting) return;

            _selNow = GuiMouse();

            if (Input.GetMouseButtonUp(0))
            {
                var r = RectFrom(_selStart, _selNow);
                if (r.width > 8f && r.height > 8f)
                {
                    if (_selectMode == 1) CalibrateFromCell(r);
                    else
                    {
                        Engine.Region.SetRect(r.x, r.y, r.width, r.height);
                        Save();
                    }
                }
                _selecting = false;
            }
        }

        /// <summary>
        /// F11 格子校准：画布区域已经框好的前提下，再框一个格子，
        /// 按 画布高 / 格子高 推出扫描行数，按 格子宽 / 2 推出采样步长。
        /// </summary>
        private void CalibrateFromCell(Rect cell)
        {
            var reg = Engine.Region;
            if (!reg.HasRegion) return;

            double rowsF = reg.ApproxHeight() / Math.Max(1.0, cell.height);
            double stepF = reg.ApproxWidth() / Math.Max(1.0, cell.width) / 2.0;

            int rows = (int)Math.Round(rowsF);
            if (rows < 1) rows = 1;
            if (rows > 400) rows = 400;
            if (stepF < 1) stepF = 1;
            if (stepF > 30) stepF = 30;

            Engine.S.Rows = rows;
            Engine.S.Step = (int)Math.Round(stepF);
            Engine.S.Clamp();

            Save();

            // 面板打开时把结果直接刷到界面上；面板没开就让覆盖层的 HUD 去显示。
            if (CheatPanel.Instance != null)
                CheatPanel.Instance.Toast(string.Format("已校准：{0} 行 / 步长 {1}px", rows, Engine.S.Step));
        }

        private void HandleCornerDrag()
        {
            if (_selecting || !_showOverlay) return;

            var m = GuiMouse();

            if (Input.GetMouseButtonDown(0))
            {
                for (int i = 0; i < 4; i++)
                {
                    var p = new Vector2((float)Engine.Region.X[i], (float)Engine.Region.Y[i]);
                    if (Vector2.Distance(p, m) <= 14f)
                    {
                        _dragCorner = i;
                        break;
                    }
                }
            }

            if (_dragCorner >= 0)
            {
                if (Input.GetMouseButton(0))
                {
                    Engine.Region.X[_dragCorner] = m.x;
                    Engine.Region.Y[_dragCorner] = m.y;
                    Engine.Region.HasRegion = true;
                }
                if (Input.GetMouseButtonUp(0))
                {
                    _dragCorner = -1;
                    Save();
                }
            }
        }

        private static Rect RectFrom(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            return new Rect(x, y, Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        // ---------------------------------------------------------------- 绘制

        private void OnGUI()
        {
            if (!_showOverlay) return;

            Event e = Event.current;
            if (e == null) return;

            Ui.Mouse = GuiMouse();
            Ui.MouseInside = true;

            EnsureDim();

            // 只在框选 / 校准时把画面压暗，方便看清选框；平时绝不遮住游戏画面。
            if (_selecting)
            {
                Color prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.22f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _dim);
                GUI.color = prev;
            }

            if (Engine != null && Engine.Region.HasRegion) DrawRegion();

            if (_selecting) DrawSelectionBox();

            DrawHud();
        }

        private void EnsureDim()
        {
            if (_dim != null) return;
            _dim = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _dim.SetPixel(0, 0, Color.white);
            _dim.Apply();
            _dim.hideFlags = HideFlags.HideAndDontSave;
        }

        private void DrawRegion()
        {
            var r = Engine.Region;

            // 四角连线（用细线段拼出来，不受 Ui 圆角影响）
            int seg = 16;
            for (int edge = 0; edge < 4; edge++)
            {
                double lastX = 0, lastY = 0;
                for (int i = 0; i <= seg; i++)
                {
                    double px, py;
                    r.EdgePoint(edge, i / (double)seg, out px, out py);
                    if (i > 0) Line(lastX, lastY, px, py, Ui.Accent2, 1.6f);
                    lastX = px;
                    lastY = py;
                }
            }

            // 角点
            for (int i = 0; i < 4; i++)
            {
                var c = new Rect((float)r.X[i] - 6f, (float)r.Y[i] - 6f, 12f, 12f);
                Ui.RoundOutline(c, 6f, Color.white, Ui.Accent, 2f);
            }

            // 扫描行预览
            int rows = Mathf.Clamp(Engine.S.Rows, 1, 200);
            for (int i = 0; i <= rows; i++)
            {
                double v = i / (double)rows;
                double x0, y0, x1, y1;
                r.Point(0, v, out x0, out y0);
                r.Point(1, v, out x1, out y1);
                Line(x0, y0, x1, y1, new Color(1f, 1f, 1f, 0.10f), 1f);
            }
        }

        private void DrawSelectionBox()
        {
            var r = RectFrom(_selStart, _selNow);
            Ui.Round(r, 4f, new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.16f));
            Ui.RoundOutline(r, 4f, Ui.Accent, new Color(0f, 0f, 0f, 0f), 2f);
            Ui.Text(new Rect(r.x + 8f, r.y + 6f, 320f, 20f),
                string.Format("{0:0} × {1:0}", r.width, r.height), Ui.Bold, Color.white);
        }

        private void DrawHud()
        {
            if (Engine == null) return;

            bool active = Engine.Running || Engine.State == AssistState.Done || !string.IsNullOrEmpty(Engine.Message);
            string status = Engine.StateText;
            if (!string.IsNullOrEmpty(Engine.Message)) status += " · " + Engine.Message;

            if (!active) return;

            float w = 420f;
            var box = new Rect((Screen.width - w) * 0.5f, 16f, w, 58f);
            Ui.Round(box, 12f, new Color(0.06f, 0.07f, 0.11f, 0.90f));
            Ui.RoundOutline(box, 12f, Ui.Accent, new Color(0f, 0f, 0f, 0f), 1.5f);

            Ui.Text(new Rect(box.x + 16f, box.y + 8f, w - 32f, 20f),
                "人工辅助 · " + status, Ui.Bold, Ui.TextCol);

            var bar = new Rect(box.x + 16f, box.y + 32f, w - 32f, 8f);
            Ui.ProgressBar(bar, Engine.Progress, Ui.Accent2);

            string info = string.Format("第 {0}/{1} 行   已用 {2:0.0}s   F6 暂停  F8 停止  F10 重扫",
                Engine.CurrentRow + 1, Mathf.Max(1, Engine.TotalRows), Engine.ElapsedSeconds);
            Ui.Text(new Rect(box.x + 16f, box.y + 42f, w - 32f, 14f), info, Ui.MutedSmall);
        }

        private void Line(double x0, double y0, double x1, double y1, Color color, float width)
        {
            float dx = (float)(x1 - x0);
            float dy = (float)(y1 - y0);
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;

            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, new Vector2((float)x0, (float)y0));
            Ui.Fill(new Rect((float)x0, (float)y0 - width * 0.5f, len, width), color);
            GUI.matrix = m;
        }
    }
}
