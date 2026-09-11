using System;
using ColoringPixelsTool.Assist;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 「人工辅助」的屏幕覆盖层。热键全部来自「设置 → 快捷键」（下方为默认值，
    /// 与独立助手 PixelAssist 的按键不是同一套：插件里 F1~F6 已被占用，所以整体后移一位）：
    ///   * F7  开始 / 暂停 / 继续
    ///   * F8  全屏拖拽框选扫描区域（可拖动四角微调）
    ///   * F9  停止
    ///   * F10 只扫当前这一行
    ///   * F11 停止并从头重新整屏扫描
    ///   * F12 框选一个格子，按画布推算扫描行数与采样步长
    ///   * 覆盖层显示 / 隐藏默认不占按键
    ///
    /// 这一层是独立于主面板的，面板关闭时依然存在（因为要一边涂色一边看区域）。
    /// </summary>
    internal sealed class AssistOverlay : MonoBehaviour
    {
        public static AssistOverlay Instance;

        // 热键统一由 Plugin 的配置托管，这样「设置 → 快捷键」里改完立刻生效。
        public static KeyCode RunKey { get { return Plugin.KeyAssistRun.Value; } }
        public static KeyCode SelectKey { get { return Plugin.KeyAssistSelect.Value; } }
        public static KeyCode StopKey { get { return Plugin.KeyAssistStop.Value; } }
        public static KeyCode TestRowKey { get { return Plugin.KeyAssistTestRow.Value; } }
        public static KeyCode RestartKey { get { return Plugin.KeyAssistRestart.Value; } }
        public static KeyCode CalibrateKey { get { return Plugin.KeyAssistCalibrate.Value; } }
        public static KeyCode OverlayKey { get { return Plugin.KeyAssistOverlay.Value; } }

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

        /// <summary>覆盖层当前是否显示（供面板按钮读取）。</summary>
        public bool OverlayVisible
        {
            get { return _showOverlay; }
        }

        /// <summary>供面板按钮调用：显示 / 隐藏覆盖层。</summary>
        public void ToggleOverlayFromUi()
        {
            _showOverlay = !_showOverlay;
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

            if (KeyDown(RunKey)) ToggleRun();
            if (KeyDown(SelectKey)) BeginSelect(0);
            if (KeyDown(StopKey)) StopFromUi();
            if (KeyDown(TestRowKey)) TestRowFromUi();
            if (KeyDown(RestartKey)) RestartFromUi();
            if (KeyDown(CalibrateKey)) BeginSelect(1);
            if (KeyDown(OverlayKey)) _showOverlay = !_showOverlay;
            if (KeyDown(Plugin.KeyVoice.Value)) VoiceColor.Toggle();

            HandleHoldLeft();
            HandleSelection();
            HandleCornerDrag();

            Engine.Tick(dt);

            if (_dirty)
            {
                _saveTimer -= dt;
                if (_saveTimer <= 0f) Save();
            }
        }

        /// <summary>热键为 None 时视为「未绑定」，避免 KeyCode.None 被当成某个真实按键。</summary>
        private static bool KeyDown(KeyCode key)
        {
            return key != KeyCode.None && Input.GetKeyDown(key);
        }

        // ---------------------------------------------------------------- 长按左键

        private bool _holdLeftDown;

        /// <summary>
        /// 「长按左键」热键（默认 Q）：按住时帮玩家一直按着鼠标左键，松开就抬起。
        /// 涂整行、涂大块颜色时不用一直捏着鼠标。
        /// </summary>
        private void HandleHoldLeft()
        {
            KeyCode key = Plugin.KeyHoldLeft != null ? Plugin.KeyHoldLeft.Value : KeyCode.None;

            bool want = key != KeyCode.None
                        && Input.GetKey(key)
                        && !CheatPanel.TextFieldFocused
                        && !(Engine != null && Engine.Running);

            if (want && !_holdLeftDown)
            {
                AssistWin32.LeftDown();
                _holdLeftDown = true;
            }
            else if (!want && _holdLeftDown)
            {
                ReleaseHoldLeft();
            }
        }

        /// <summary>抬起被热键按住的左键（面板关闭、插件卸载、按键改动时都要调）。</summary>
        public void ReleaseHoldLeft()
        {
            if (!_holdLeftDown) return;
            _holdLeftDown = false;
            try { AssistWin32.LeftUp(); } catch (Exception) { }
        }

        public bool HoldLeftActive
        {
            get { return _holdLeftDown; }
        }

        private void OnDestroy()
        {
            ReleaseHoldLeft();
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
            bool timerOnly = !active
                             && Plugin.TimerInHud != null && Plugin.TimerInHud.Value
                             && PaintTimer.Running;
            if (!active && !timerOnly) return;

            float w = 420f;
            float h = timerOnly ? 34f : 58f;
            var box = new Rect((Screen.width - w) * 0.5f, 16f, w, h);
            Ui.Round(box, 12f, new Color(0.06f, 0.07f, 0.11f, 0.90f));
            Ui.RoundOutline(box, 12f, Ui.Accent, new Color(0f, 0f, 0f, 0f), 1.5f);

            if (timerOnly)
            {
                string only = "本图用时 " + PaintTimer.Format(PaintTimer.CurrentSeconds);
                if (VoiceColor.Listening) only += "    语音 " + VoiceStatusText();
                Ui.Text(new Rect(box.x + 16f, box.y + 9f, w - 32f, 18f), only, Ui.Bold, Ui.TextCol);
                return;
            }

            string status = Engine.StateText;
            if (!string.IsNullOrEmpty(Engine.Message)) status += " · " + Engine.Message;

            Ui.Text(new Rect(box.x + 16f, box.y + 8f, w - 32f, 20f),
                "人工辅助 · " + status, Ui.Bold, Ui.TextCol);

            var bar = new Rect(box.x + 16f, box.y + 32f, w - 32f, 8f);
            Ui.ProgressBar(bar, Engine.Progress, Ui.Accent2);

            string info = string.Format("第 {0}/{1} 行   已用 {2:0.0}s   {3} 暂停  {4} 停止",
                Engine.CurrentRow + 1, Mathf.Max(1, Engine.TotalRows), Engine.ElapsedSeconds,
                KeyLabel(RunKey), KeyLabel(StopKey));
            if (Plugin.TimerInHud != null && Plugin.TimerInHud.Value)
                info += "   本图 " + PaintTimer.Format(PaintTimer.CurrentSeconds);
            if (VoiceColor.Listening)
                info += "   语音 " + VoiceStatusText();
            Ui.Text(new Rect(box.x + 16f, box.y + 42f, w - 32f, 14f), info, Ui.MutedSmall);
        }

        private static string VoiceStatusText()
        {
            return string.IsNullOrEmpty(VoiceColor.LastAction) ? "待命" : VoiceColor.LastAction;
        }

        private static string KeyLabel(KeyCode key)
        {
            return key == KeyCode.None ? "—" : key.ToString();
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
