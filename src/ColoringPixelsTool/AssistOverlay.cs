using System;
using ColoringPixelsTool.Assist;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 「人工辅助」的屏幕覆盖层。热键全部来自「设置 → 快捷键」（下方为默认值，
    /// 插件里 F1~F6 已被占用，所以人工辅助整体后移一位）：
    ///   * F7  开始 / 暂停 / 继续
    ///   * F8  全屏拖拽框选扫描区域（可拖动四角微调）
    ///   * F9  停止
    ///   * F10 只扫当前这一行
    ///   * F11 停止并从头重新整屏扫描
    ///   * F12 框选一个格子，按画布推算扫描行数与采样步长
    ///   * 覆盖层显示 / 隐藏默认不占按键
    ///
    /// 除了手动框选，面板上还有一个「识别画布」按钮（见 <see cref="DetectFromUi"/>）：
    /// 按游戏内部的画布几何直接把区域、行数和采样步长一次算准，不用手动对格子。
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

        // 绘图可视框默认关闭：它是一层盖在游戏画面上的特效（柔光填充 + 网格 + 信息牌），
        // 平时不涂的时候挡视线。用户主动框选 / 校准 / 识别画布时（见下面几个方法）会自动
        // 打开，按 F7 或点面板上的「显示覆盖层」也能随时打开。
        private bool _showOverlay = false;
        private bool _selecting;
        private int _selectMode;   // 0 = 框选区域，1 = 框选一个格子做校准
        private Vector2 _selStart;
        private Vector2 _selNow;
        private bool _dirty;
        private float _saveTimer;

        private int _dragCorner = -1;

        private Texture2D _dim;

        /// <summary>
        /// 游戏客户区左上角在桌面上的偏移。全屏 / 无边框时是 (0,0)，窗口化时不为 0。
        /// 区域一律按**桌面坐标**存（因为最终要移动真实光标），绘制时再减掉这个偏移。
        /// </summary>
        private float _originX;
        private float _originY;

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
            if (st != null)
            {
                CopySettings(st, Engine.S);
                // 格子大小是本机的量测结果，单独恢复（不跟预设走）
                Engine.S.CellWidth = st.CellWidth;
                Engine.S.CellHeight = st.CellHeight;
            }
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
            // 注意：格子大小（CellWidth / CellHeight）是「识别画布」算出来的量测结果，
            // 跟本机当前缩放绑定，所以不随预设搬运，只在启动时从本机存档恢复。
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

        /// <summary>
        /// 供面板按钮调用：一键识别画布区域与格子大小。
        ///
        /// 用户只要把游戏画面缩放到想用的样子，点一下这里，区域 / 行数 / 采样步长
        /// 就全部按画布的真实几何自动填好，不需要再手动框选和校准格子。
        /// 返回一句给用户看的结果说明（成功或失败原因）。
        /// </summary>
        public string DetectFromUi()
        {
            _showOverlay = true;
            _selecting = false;
            _dragCorner = -1;

            var det = AssistAutoDetect.Detect(_originX, _originY);
            if (!det.Ok) return det.Message;

            var r = Engine.Region;
            for (int i = 0; i < 4; i++)
            {
                r.X[i] = det.X[i];
                r.Y[i] = det.Y[i];
                r.Bend[i] = 0;
            }
            r.HasRegion = true;

            Engine.S.Rows = det.Rows;
            // 每个格子采样两次：既保证每格都覆盖到，又不会把路径切得过碎
            Engine.S.Step = det.CellWidth / 2.0;
            // 只内缩 1px：识别出来的区域和画布边界完全重合，不缩一点的话每条扫描线的
            // 起止点正好落在多边形边上，「是否在区域内」的判定会含糊，可能整格漏掉。
            // 缩太多又会让扫描行偏离格子中线，所以 1px 刚好。
            Engine.S.EdgeMargin = 1;
            Engine.S.CellWidth = det.CellWidth;
            Engine.S.CellHeight = det.CellHeight;
            Engine.S.Clamp();
            Save();

            return "已识别画布：" + det.Message;
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

            RefreshClientOrigin();

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

        /// <summary>客户区原点（每帧更新一次，绘制与命中测试共用）。</summary>
        private void RefreshClientOrigin()
        {
            int ox, oy;
            if (AssistWin32.TryGetClientOrigin(Screen.width, Screen.height, out ox, out oy))
            {
                _originX = ox;
                _originY = oy;
            }
            else
            {
                // 全屏 / 无边框，或者前台窗口不是游戏：按全屏处理
                _originX = 0f;
                _originY = 0f;
            }
        }

        /// <summary>游戏内鼠标位置换算成桌面坐标（区域的存储单位）。</summary>
        private Vector2 DesktopMouse()
        {
            Vector2 g = GuiMouse();
            return new Vector2(g.x + _originX, g.y + _originY);
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
                        // 选框是在客户区里画的，落到区域里要换算成桌面坐标
                        Engine.Region.SetRect(r.x + _originX, r.y + _originY, r.width, r.height);
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
            // 手动校准也顺带记下格子大小，覆盖层的格子预览 / 信息牌才有内容
            Engine.S.CellWidth = cell.width;
            Engine.S.CellHeight = cell.height;
            Engine.S.Clamp();

            Save();

            // 面板打开时把结果直接刷到界面上；面板没开就让覆盖层的 HUD 去显示。
            if (CheatPanel.Instance != null)
                CheatPanel.Instance.Toast(string.Format("已校准：{0} 行 / 步长 {1}px", rows, Engine.S.Step));
        }

        private void HandleCornerDrag()
        {
            if (_selecting || !_showOverlay) return;

            var gm = GuiMouse();

            if (Input.GetMouseButtonDown(0))
            {
                for (int i = 0; i < 4; i++)
                {
                    // 区域存的是桌面坐标，命中测试要换算到客户区
                    var p = new Vector2((float)Engine.Region.X[i] - _originX,
                        (float)Engine.Region.Y[i] - _originY);
                    if (Vector2.Distance(p, gm) <= 16f)
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
                    var dm = DesktopMouse();
                    Engine.Region.X[_dragCorner] = dm.x;
                    Engine.Region.Y[_dragCorner] = dm.y;
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

            // 区域存的是桌面坐标；这里在游戏客户区里画，所以整体减掉客户区原点
            var v = new Vector2[4];
            for (int i = 0; i < 4; i++)
                v[i] = new Vector2((float)r.X[i] - _originX, (float)r.Y[i] - _originY);

            bool straight = true;
            for (int i = 0; i < 4; i++)
                if (Mathf.Abs((float)r.Bend[i]) > 0.001f) straight = false;

            Rect box = Bounds(v);
            bool axis = straight && TryAxisRect(v, out box);
            Rect bounds = box;

            if (axis)
            {
                DrawAreaFill(box);
                DrawCellPreview(box);
                DrawBorder(box);
            }
            else
            {
                // 弯边 / 斜放画布：填充会很难看，只描一圈发光线
                DrawEdges(r, Ui.Alpha(Ui.Accent2, 0.16f), 8f);
                DrawEdges(r, Ui.Accent2, 1.8f);
            }

            DrawCurrentRow(r, axis ? box : bounds, axis);

            for (int i = 0; i < 4; i++) DrawHandle(v[i], i == _dragCorner);

            DrawRegionBadge(bounds);
        }

        /// <summary>四角是否构成一个水平的矩形（一键识别出来的区域都是这种）。</summary>
        private static bool TryAxisRect(Vector2[] v, out Rect box)
        {
            box = new Rect();
            if (Mathf.Abs(v[0].y - v[1].y) > 0.6f) return false;
            if (Mathf.Abs(v[3].y - v[2].y) > 0.6f) return false;
            if (Mathf.Abs(v[0].x - v[3].x) > 0.6f) return false;
            if (Mathf.Abs(v[1].x - v[2].x) > 0.6f) return false;

            float x0 = Mathf.Min(v[0].x, v[1].x);
            float x1 = Mathf.Max(v[0].x, v[1].x);
            float y0 = Mathf.Min(v[0].y, v[3].y);
            float y1 = Mathf.Max(v[0].y, v[3].y);
            if (x1 - x0 < 2f || y1 - y0 < 2f) return false;

            box = new Rect(x0, y0, x1 - x0, y1 - y0);
            return true;
        }

        private static Rect Bounds(Vector2[] v)
        {
            float x0 = v[0].x, x1 = v[0].x, y0 = v[0].y, y1 = v[0].y;
            for (int i = 1; i < v.Length; i++)
            {
                x0 = Mathf.Min(x0, v[i].x);
                x1 = Mathf.Max(x1, v[i].x);
                y0 = Mathf.Min(y0, v[i].y);
                y1 = Mathf.Max(y1, v[i].y);
            }
            return new Rect(x0, y0, Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
        }

        /// <summary>区域内部：一层很淡的主色玻璃罩 + 顶边细高光，能看清范围又不遮住画面。</summary>
        private static void DrawAreaFill(Rect box)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.7f);
            UiFx.Gradient(box,
                Ui.Alpha(Ui.Accent, 0.075f + 0.020f * pulse),
                Ui.Alpha(Ui.Accent2, 0.028f));

            if (box.width > 48f)
                Ui.Fill(new Rect(box.x + 12f, box.y + 1.5f, box.width - 24f, 1f),
                    new Color(1f, 1f, 1f, 0.09f));
        }

        /// <summary>
        /// 格子网格预览：横向是每条扫描行覆盖的格子边界，纵向是真实格子列边界
        /// （识别到格子大小之后才有）。用户一眼就能看出区域和格子有没有对歪。
        /// </summary>
        private void DrawCellPreview(Rect box)
        {
            Color faint = new Color(1f, 1f, 1f, 0.05f);
            Color strong = new Color(1f, 1f, 1f, 0.10f);

            int rows = Mathf.Clamp(Engine.S.Rows, 1, 120);
            for (int i = 1; i < rows; i++)
            {
                float y = box.y + box.height * i / rows;
                Ui.Fill(new Rect(box.x + 2f, Mathf.Round(y), box.width - 4f, 1f),
                    i % 5 == 0 ? strong : faint);
            }

            int cols = ColsOf(box.width);
            for (int j = 1; j < cols; j++)
            {
                float x = box.x + box.width * j / cols;
                Ui.Fill(new Rect(Mathf.Round(x), box.y + 2f, 1f, box.height - 4f),
                    j % 5 == 0 ? strong : faint);
            }
        }

        /// <summary>按已识别的格子宽度反推有多少列（没识别过就返回 0）。</summary>
        private int ColsOf(float regionWidth)
        {
            double cw = Engine.S.CellWidth;
            if (cw < 1) return 0;
            int n = Mathf.RoundToInt(regionWidth / (float)cw);
            return n > 1 && n <= 200 ? n : 0;
        }

        /// <summary>矩形区域的描边：外发光 + 一圈从紫到青的霓虹边 + 跑动的光点。</summary>
        private void DrawBorder(Rect box)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.7f);

            float[] off = { 2f, 4f, 6.5f };
            float[] alpha = { 0.15f, 0.08f, 0.038f };
            for (int k = 0; k < 3; k++)
            {
                Color c = Ui.Alpha(Ui.Accent2, alpha[k] * (0.72f + 0.28f * pulse));
                float o = off[k];
                Ui.Fill(new Rect(box.x - o, box.y - o, box.width + o * 2f, 1.5f), c);
                Ui.Fill(new Rect(box.x - o, box.yMax + o - 1.5f, box.width + o * 2f, 1.5f), c);
                Ui.Fill(new Rect(box.x - o, box.y - o, 1.5f, box.height + o * 2f), c);
                Ui.Fill(new Rect(box.xMax + o - 1.5f, box.y - o, 1.5f, box.height + o * 2f), c);
            }

            const float t = 2.4f;
            GradH(new Rect(box.x, box.y - t * 0.5f, box.width, t), Ui.Accent, Ui.Accent2);
            GradH(new Rect(box.x, box.yMax - t * 0.5f, box.width, t), Ui.Accent2, Ui.Accent);
            GradV(new Rect(box.x - t * 0.5f, box.y, t, box.height), Ui.Accent, Ui.Accent2);
            GradV(new Rect(box.xMax - t * 0.5f, box.y, t, box.height), Ui.Accent2, Ui.Accent);

            if (!Engine.Running)
                UiFx.BorderBeam(box, Ui.Accent2, 0.16f, 64f, 0.7f);
        }

        /// <summary>弯边 / 斜放区域：按边采样成折线描边（glow 宽线在下、细亮线在上）。</summary>
        private void DrawEdges(AssistRegion r, Color color, float width)
        {
            const int seg = 14;
            for (int edge = 0; edge < 4; edge++)
            {
                double lx = 0, ly = 0;
                for (int i = 0; i <= seg; i++)
                {
                    double px, py;
                    r.EdgePoint(edge, i / (double)seg, out px, out py);
                    if (i > 0)
                        Line(lx - _originX, ly - _originY, px - _originX, py - _originY, color, width);
                    lx = px;
                    ly = py;
                }
            }
        }

        /// <summary>正在扫描的那一行：一条会呼吸的亮带，让人知道现在扫到哪了。</summary>
        private void DrawCurrentRow(AssistRegion r, Rect box, bool axis)
        {
            if (!Engine.Running) return;

            int rows = Mathf.Max(1, Engine.TotalRows);
            int row = Mathf.Clamp(Engine.CurrentRow, 0, rows - 1);
            double v = (row + 0.5) / rows;

            float sweep = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);

            if (axis)
            {
                float y = box.y + box.height * (float)v;
                float h = Mathf.Max(2.5f, box.height / rows * 0.9f);
                Ui.Fill(new Rect(box.x + 1f, y - h * 0.5f, box.width - 2f, h),
                    Ui.Alpha(Ui.Accent2, 0.12f + 0.10f * sweep));
                Ui.Fill(new Rect(box.x + 1f, y - 1f, box.width - 2f, 2f), Ui.Alpha(Ui.Accent2, 0.7f));
            }
            else
            {
                double x0, y0, x1, y1;
                r.Point(0, v, out x0, out y0);
                r.Point(1, v, out x1, out y1);
                Line(x0 - _originX, y0 - _originY, x1 - _originX, y1 - _originY,
                    Ui.Alpha(Ui.Accent2, 0.7f), 2.5f);
            }
        }

        /// <summary>角点手柄：可拖动微调；悬停时变大并透出一圈柔光。</summary>
        private void DrawHandle(Vector2 p, bool active)
        {
            bool hover = Vector2.Distance(p, Ui.Mouse) <= 16f;
            float k = UiFx.To("assist-handle", hover || active, 22f);
            float rad = Mathf.Lerp(6f, 9f, k);

            var q = new Rect(p.x - rad, p.y - rad, rad * 2f, rad * 2f);
            if (k > 0.02f) UiFx.Spotlight(q, p, Ui.Accent2, 0.30f * k, 40f);

            Ui.Round(q, rad, new Color(0.04f, 0.05f, 0.09f, 0.72f));
            Ui.RoundOutline(q, rad, Color.white, Ui.Alpha(Ui.Accent, 0.72f + 0.28f * k), 1.6f);
        }

        /// <summary>区域左上角的信息牌：画布尺寸、格子数、格子像素大小。</summary>
        private void DrawRegionBadge(Rect bounds)
        {
            AssistSettings s = Engine.S;
            int rows = Mathf.Max(1, s.Rows);
            int cols = ColsOf(bounds.width);

            string l1 = string.Format("画布 {0:0} × {1:0} px", bounds.width, bounds.height);
            string l2 = s.CellWidth >= 1
                ? string.Format("{0} × {1} 格 · 格子 {2:0.#} × {3:0.#} px", cols, rows, s.CellWidth, s.CellHeight)
                : string.Format("扫描 {0} 行 · 格子大小未识别", rows);

            const float w = 244f, h = 46f;
            var pill = new Rect(bounds.x, bounds.y - h - 8f, w, h);
            if (pill.y < 6f) pill.y = Mathf.Min(bounds.y + 10f, Screen.height - h - 6f);
            pill.x = Mathf.Clamp(pill.x, 6f, Mathf.Max(6f, Screen.width - w - 6f));

            Ui.Round(pill, 11f, new Color(0.045f, 0.055f, 0.095f, 0.90f));
            Ui.RoundOutline(pill, 11f, Ui.Alpha(Ui.Accent2, 0.50f), Ui.Alpha(Ui.Accent2, 0.09f), 1f);

            Ui.StatusDot(new Rect(pill.x + 15f, pill.center.y - 3.5f, 7f, 7f),
                Engine.Running ? Ui.Good : Ui.Accent2, Engine.Running);
            Ui.Text(new Rect(pill.x + 30f, pill.y + 7f, w - 42f, 17f), l1, Ui.Bold, Color.white);
            Ui.Text(new Rect(pill.x + 30f, pill.y + 25f, w - 42f, 15f), l2, Ui.MutedSmall, Ui.Accent2);
        }

        private static void GradH(Rect r, Color left, Color right)
        {
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, UiFx.GradTex(left, right, false), ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        private static void GradV(Rect r, Color top, Color bottom)
        {
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, UiFx.GradTex(bottom, top, true), ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        private void DrawSelectionBox()
        {
            var r = RectFrom(_selStart, _selNow);

            Ui.Round(r, 5f, new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0.14f));
            Ui.RoundOutline(r, 5f, Color.white, Color.clear, 1.5f);

            // 四角小刻度，让选框看起来是「对齐」的
            const float tick = 14f;
            foreach (var c in new[]
            {
                new Vector2(r.x, r.y), new Vector2(r.xMax, r.y),
                new Vector2(r.xMax, r.yMax), new Vector2(r.x, r.yMax)
            })
            {
                Ui.Fill(new Rect(c.x - 1.5f, c.y - tick * 0.5f, 3f, tick), Ui.Accent2);
                Ui.Fill(new Rect(c.x - tick * 0.5f, c.y - 1.5f, tick, 3f), Ui.Accent2);
            }

            string text = _selectMode == 1
                ? string.Format("框住一个格子    {0:0} × {1:0} px", r.width, r.height)
                : string.Format("{0:0} × {1:0} px", r.width, r.height);

            const float pw = 260f, ph = 30f;
            var pill = new Rect(r.center.x - pw * 0.5f, r.yMax + 10f, pw, ph);
            pill.x = Mathf.Clamp(pill.x, 6f, Mathf.Max(6f, Screen.width - pw - 6f));
            pill.y = Mathf.Clamp(pill.y, 6f, Mathf.Max(6f, Screen.height - ph - 6f));

            Ui.Round(pill, ph * 0.5f, new Color(0.045f, 0.055f, 0.095f, 0.92f));
            Ui.RoundOutline(pill, ph * 0.5f, Ui.Alpha(Ui.Accent2, 0.6f), Ui.Alpha(Ui.Accent2, 0.10f), 1f);
            Ui.Text(pill, text, Ui.Center, Color.white);
        }

        private void DrawHud()
        {
            if (Engine == null) return;

            // 还没区域、也没有任何提示时，给一句「下一步做什么」，省得对着空屏幕发呆
            if (!Engine.Region.HasRegion && string.IsNullOrEmpty(Engine.Message))
            {
                DrawNoRegionHint();
                return;
            }

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

        /// <summary>还没区域时的引导条：「先缩放好画面，再去面板点识别」。</summary>
        private void DrawNoRegionHint()
        {
            const float w = 560f, h = 40f;
            var box = new Rect((Screen.width - w) * 0.5f, 16f, w, h);

            Ui.Round(box, 12f, new Color(0.055f, 0.065f, 0.105f, 0.88f));
            Ui.RoundOutline(box, 12f, Ui.Alpha(Ui.Accent2, 0.45f), Ui.Alpha(Ui.Accent2, 0.08f), 1f);

            Ui.Text(new Rect(box.x + 16f, box.y, w - 32f, h),
                string.Format("人工辅助 · 缩放好画布后，在面板「人工辅助」里点「识别画布」自动贴合；也可按 {0} 手动框选",
                    KeyLabel(SelectKey)), Ui.Small, Ui.TextCol);
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
