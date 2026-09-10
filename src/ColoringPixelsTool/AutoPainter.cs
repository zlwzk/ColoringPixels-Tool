using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 拟人自动涂色引擎。
    ///
    /// 拟人策略：
    ///   1. 像真人一样「一种颜色一种颜色地涂」——先选定一个颜色，把它的格子全部处理掉，再换下一个。
    ///   2. 同一种颜色内部按「画布分块 + 块间就近移动 + 块内蛇形扫行」生成笔顺，
    ///      避免横跨整张画布乱跳，看起来就是人在一格一格推进。
    ///   3. 每完成一笔会随机停顿；手速带抖动；可选「偶尔点错一格再回头补上」。
    /// </summary>
    internal class AutoPainter : MonoBehaviour
    {
        public static AutoPainter Instance;

        // ---- 运行状态 ----
        public bool Running { get; private set; }
        public string Status { get; private set; } = "待机";
        public int PaintedThisRun { get; private set; }
        public int ColourId { get; private set; }
        public float Elapsed => Running ? Time.unscaledTime - _startTime : _lastElapsed;
        public float CellsPerSecondNow => _actualCps;

        // ---- 可调参数 ----
        public float CellsPerSecond = 45f;
        public int BlockSize = 8;
        public int StrokeLength = 30;
        public float PauseChance = 0.4f;
        public float MistakeChance = 0f;
        public bool LargestColourFirst;
        public bool HighlightColour = true;
        public bool AutoSave = true;
        public int RestrictToColour;

        private ClickTest _ct;
        private CrossLevelStorage _st;
        private readonly Dictionary<int, List<Vector2Int>> _pending = new Dictionary<int, List<Vector2Int>>();
        private readonly List<int> _order = new List<int>();
        private readonly System.Random _rng = new System.Random(Environment.TickCount);
        private List<Vector2Int> _path;
        private int _pathIndex;
        private int _orderIndex;
        private int _cursorX = -1, _cursorY = -1;
        private float _timer;
        private float _interval;
        private float _pauseUntil;
        private int _strokeLeft;
        private float _startTime;
        private float _lastElapsed;
        private float _actualCps;
        private float _cpsWindow;
        private int _cpsCount;
        private bool _finished;

        private void Awake()
        {
            Instance = this;
        }

        // ------------------------------------------------------------ 生命周期

        public void StartRun()
        {
            var ct = GameApi.Ct;
            var st = GameApi.St;
            if (!GameApi.InLevel(ct) || st == null || st.colours == null)
            {
                Status = "不在关卡内";
                return;
            }

            _ct = ct;
            _st = st;
            _pending.Clear();
            _order.Clear();
            _path = null;
            _pathIndex = 0;
            _orderIndex = 0;
            _cursorX = _cursorY = -1;
            _timer = 0f;
            _pauseUntil = 0f;
            _strokeLeft = Mathf.Max(1, StrokeLength);
            _finished = false;
            PaintedThisRun = 0;
            _cpsWindow = 0f;
            _cpsCount = 0;
            _actualCps = 0f;
            _startTime = Time.unscaledTime;

            // 按颜色归类
            var all = GameApi.CollectPending(ct, RestrictToColour);
            if (all.Count == 0)
            {
                Status = "已经没有需要涂的格子了";
                Running = false;
                return;
            }

            foreach (var p in all)
            {
                int c = ct.mainGridValues[p.x, p.y];
                if (!_pending.TryGetValue(c, out var l))
                {
                    l = new List<Vector2Int>();
                    _pending[c] = l;
                }
                l.Add(p);
            }

            foreach (var kv in _pending)
                _order.Add(kv.Key);

            // 排序：按调色板顺序，或按剩余数量从多到少
            if (LargestColourFirst)
                _order.Sort((a, b) => _pending[b].Count.CompareTo(_pending[a].Count));
            else
                _order.Sort();

            Running = true;
            _lastElapsed = 0f;
            Status = "开始涂色";
            NextColour(true);
        }

        public void StopRun(bool rebuild = true)
        {
            if (!Running && !_finished) return;
            Running = false;
            Status = "已停止";
            if (rebuild && GameApi.InLevel(_ct)) GameApi.Rebuild(_ct);
        }

        private void Finish()
        {
            Running = false;
            _finished = true;
            _lastElapsed = Time.unscaledTime - _startTime;
            if (AutoSave) GameApi.SaveNow();
            if (GameApi.InLevel(_ct)) GameApi.Rebuild(_ct);
            Status = $"完成！本次涂了 {PaintedThisRun} 格，用时 {_lastElapsed:0.0}s";
            Log.Info(Status);
        }

        private void Update()
        {
            if (!Running) return;
            if (_ct == null || !GameApi.InLevel(_ct))
            {
                StopRun(false);
                Status = "关卡已关闭";
                return;
            }

            if (Time.unscaledTime < _pauseUntil) return;

            _timer += Time.unscaledDeltaTime;
            if (_timer < _interval) return;
            _timer = 0f;

            // 手速抖动
            float baseInterval = 1f / Mathf.Max(1f, CellsPerSecond);
            _interval = baseInterval * Mathf.Lerp(0.72f, 1.35f, (float)_rng.NextDouble());

            Step();

            _cpsWindow += Time.unscaledDeltaTime;
            _cpsCount++;
            if (_cpsWindow >= 0.5f)
            {
                _actualCps = _cpsCount / _cpsWindow;
                _cpsWindow = 0f;
                _cpsCount = 0;
            }
        }

        // ------------------------------------------------------------ 单步

        private void Step()
        {
            if (_path == null || _pathIndex >= _path.Count)
            {
                if (!NextColour(false))
                {
                    Finish();
                    return;
                }
            }

            var cell = _path[_pathIndex];
            _pathIndex++;

            int target = _ct.mainGridValues[cell.x, cell.y];
            if (target != 0 && _ct.savedGridValues[cell.x, cell.y] != target)
            {
                GameApi.ApplyCell(_ct, _st, cell.x, cell.y, target, true);
                PaintedThisRun++;
                _cursorX = cell.x;
                _cursorY = cell.y;
            }

            if (MistakeChance > 0f && _rng.NextDouble() < MistakeChance)
                DoMistake();

            _strokeLeft--;
            if (_strokeLeft <= 0)
            {
                _strokeLeft = Mathf.Max(1, StrokeLength + _rng.Next(-StrokeLength / 3, StrokeLength / 3 + 1));
                if (_rng.NextDouble() < PauseChance)
                {
                    float pause = Mathf.Lerp(0.12f, 0.55f, (float)_rng.NextDouble());
                    if (_rng.NextDouble() < 0.08f) pause += Mathf.Lerp(0.6f, 1.8f, (float)_rng.NextDouble());
                    _pauseUntil = Time.unscaledTime + pause;
                }
            }
        }

        /// <summary>偶尔手滑：把后面才该涂的格子涂成当前颜色，等轮到它时再补回正确颜色。</summary>
        private void DoMistake()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int oi = _orderIndex + 1 + _rng.Next(Mathf.Max(1, _order.Count - _orderIndex - 1));
                if (oi >= _order.Count) return;
                int c = _order[oi];
                if (!_pending.TryGetValue(c, out var cells) || cells.Count == 0) continue;

                for (int t = 0; t < 5; t++)
                {
                    var p = cells[_rng.Next(cells.Count)];
                    if (_ct.mainGridValues[p.x, p.y] == ColourId) continue;
                    if (_ct.savedGridValues[p.x, p.y] == ColourId) continue;
                    GameApi.ApplyCell(_ct, _st, p.x, p.y, ColourId, false);
                    return;
                }
            }
        }

        // ------------------------------------------------------------ 换颜色

        private bool NextColour(bool first)
        {
            while (_orderIndex < _order.Count)
            {
                int colour = _order[_orderIndex];
                if (!_pending.TryGetValue(colour, out var cells) || cells.Count == 0)
                {
                    _orderIndex++;
                    continue;
                }

                // 该颜色是否还有没涂对的格子
                var todo = new List<Vector2Int>(cells.Count);
                foreach (var p in cells)
                    if (_ct.mainGridValues[p.x, p.y] == colour && _ct.savedGridValues[p.x, p.y] != colour)
                        todo.Add(p);

                if (todo.Count == 0)
                {
                    _orderIndex++;
                    continue;
                }

                ColourId = colour;
                if (HighlightColour) GameApi.HighlightColour(colour);

                _path = BuildPath(todo, _cursorX, _cursorY);
                _pathIndex = 0;
                _orderIndex++;
                _strokeLeft = Mathf.Max(1, StrokeLength + _rng.Next(-StrokeLength / 3, StrokeLength / 3 + 1));

                if (!first)
                    _pauseUntil = Time.unscaledTime + Mathf.Lerp(0.10f, 0.40f, (float)_rng.NextDouble());
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------ 笔顺规划

        /// <summary>分块 → 块间就近移动 → 块内蛇形扫行。</summary>
        private List<Vector2Int> BuildPath(List<Vector2Int> cells, int startX, int startY)
        {
            var path = new List<Vector2Int>(cells.Count);
            int bs = Mathf.Max(2, BlockSize);

            var blocks = new Dictionary<long, List<Vector2Int>>();
            foreach (var p in cells)
            {
                long key = (long)(p.x / bs) * 100000L + (p.y / bs);
                if (!blocks.TryGetValue(key, out var l))
                {
                    l = new List<Vector2Int>();
                    blocks[key] = l;
                }
                l.Add(p);
            }

            // 块间最近邻
            var keys = new List<long>(blocks.Keys);
            var ordered = new List<long>(keys.Count);
            float curX = startX < 0 ? 0f : startX / (float)bs;
            float curY = startY < 0 ? 0f : startY / (float)bs;

            while (keys.Count > 0)
            {
                int best = 0;
                float bestD = float.MaxValue;
                for (int i = 0; i < keys.Count; i++)
                {
                    long k = keys[i];
                    float bx = k / 100000f;
                    float by = k % 100000f;
                    float d = (bx - curX) * (bx - curX) + (by - curY) * (by - curY);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                long chosen = keys[best];
                keys.RemoveAt(best);
                ordered.Add(chosen);
                curX = chosen / 100000f;
                curY = chosen % 100000f;
            }

            // 块内蛇形扫行
            foreach (long key in ordered)
            {
                var list = blocks[key];
                var rows = new SortedDictionary<int, List<Vector2Int>>();
                foreach (var p in list)
                {
                    if (!rows.TryGetValue(p.y, out var r))
                    {
                        r = new List<Vector2Int>();
                        rows[p.y] = r;
                    }
                    r.Add(p);
                }

                int blockCentreX = (int)((key / 100000L) * bs + bs / 2);
                bool flip = startX >= 0 && startX > blockCentreX;

                foreach (var kv in rows)
                {
                    kv.Value.Sort((a, b) => a.x.CompareTo(b.x));
                    if (flip) kv.Value.Reverse();
                    path.AddRange(kv.Value);
                    flip = !flip;
                }

                if (startX >= 0 && path.Count > 0)
                {
                    // 下一次分块以本块最后一格为参考点
                    startX = path[path.Count - 1].x;
                    startY = path[path.Count - 1].y;
                }
            }

            return path;
        }
    }
}
