using System;
using System.Collections;
using UnityEngine;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 自动化调度器：按用户设定的总时长，自动一张张完成拟人涂色。
    /// 与首页计时器联动，提供 elapsed / remaining / completed 状态。
    /// </summary>
    public class AutoScheduler : MonoBehaviour
    {
        public static AutoScheduler Instance { get; private set; }

        public bool Running { get; private set; }
        public float TotalSeconds { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float RemainingSeconds => Mathf.Max(0f, TotalSeconds - ElapsedSeconds);
        public bool Continuous { get; set; }
        public float PauseBetweenImages { get; set; } = 3f;
        public int ImagesCompleted { get; private set; }
        public int ImagesFailed { get; private set; }
        public string Status { get; private set; } = "就绪";

        public Action<string> OnStatusChanged;
        public Action OnTick;

        // ---- 本次会话的涂色速度（自动化页签的「预设 / 自定义」） ----
        // 面板每帧都会把「拟人涂色」页签的参数同步给引擎，所以这里必须留一份会话级的
        // 速度，让面板知道「现在该听谁的」——否则预设刚设好就被下一帧覆盖，等于没设。
        public bool SpeedOverrideActive { get; private set; }
        public float SpeedCellsPerSecond { get; private set; }
        public int SpeedStrokeLength { get; private set; }
        public float SpeedPauseChance { get; private set; }

        private AutoPainter _painter;
        private float _imagePauseTimer;
        private bool _waitingForNextImage;

        // ---- 切图状态 ----
        private string _levelKey;
        private bool _paintedThisLevel;
        private bool _switching;
        private string _switchFrom;
        private float _switchTimeout;

        private void Awake()
        {
            Instance = this;
            _painter = gameObject.GetComponent<AutoPainter>();
            if (_painter == null) _painter = gameObject.AddComponent<AutoPainter>();
        }

        public void StartSession(float totalMinutes, bool continuous, float pauseBetweenImages, int speedPreset)
        {
            if (Running) StopSession("重新启动");

            TotalSeconds = totalMinutes * 60f;
            Continuous = continuous;
            PauseBetweenImages = pauseBetweenImages;
            ElapsedSeconds = 0f;
            ImagesCompleted = 0;
            ImagesFailed = 0;
            _waitingForNextImage = false;
            _switching = false;
            _switchFrom = null;
            _levelKey = GameApi.LevelKey;
            _paintedThisLevel = false;
            Running = true;

            ResolveSpeed(speedPreset);

            SetStatus("自动化已开始");
            StartCoroutine(RunLoop());
            Log.Info($"AutoScheduler 启动：总时长 {totalMinutes:F1} 分钟，连续={continuous}");
        }

        public void StopSession(string reason = "用户停止")
        {
            if (!Running) return;
            Running = false;
            if (_painter != null && _painter.Running) _painter.StopRun();
            SetStatus("已停止：" + reason);
            Log.Info("AutoScheduler 停止：" + reason);
        }

        /// <summary>把「涂色速度预设」换算成具体参数：0 = 自定义，1-3 = 慢 / 中 / 快。</summary>
        private void ResolveSpeed(int preset)
        {
            switch (preset)
            {
                case 1: // 慢（更拟人，停顿多）
                    SpeedCellsPerSecond = 28f;
                    SpeedStrokeLength = 22;
                    SpeedPauseChance = 0.55f;
                    break;
                case 2: // 中
                    SpeedCellsPerSecond = 55f;
                    SpeedStrokeLength = 30;
                    SpeedPauseChance = 0.38f;
                    break;
                case 3: // 快
                    SpeedCellsPerSecond = 110f;
                    SpeedStrokeLength = 45;
                    SpeedPauseChance = 0.18f;
                    break;
                default: // 0 = 自定义：用自动化页签里单独设定的三项，不再借「拟人涂色」页签的值
                    SpeedCellsPerSecond = Plugin.AutoCustomSpeed.Value;
                    SpeedStrokeLength = Plugin.AutoCustomStroke.Value;
                    SpeedPauseChance = Plugin.AutoCustomPause.Value;
                    break;
            }

            SpeedOverrideActive = true;
            ApplySpeed();
        }

        /// <summary>把会话速度写进引擎（面板关闭时也能立即生效）。</summary>
        private void ApplySpeed()
        {
            if (_painter == null) return;
            _painter.CellsPerSecond = SpeedCellsPerSecond;
            _painter.StrokeLength = SpeedStrokeLength;
            _painter.PauseChance = SpeedPauseChance;
        }

        private void SetStatus(string s)
        {
            if (Status == s) return;
            Status = s;
            if (OnStatusChanged != null) OnStatusChanged(s);
        }

        private IEnumerator RunLoop()
        {
            while (Running)
            {
                ElapsedSeconds += Time.unscaledDeltaTime;
                if (ElapsedSeconds >= TotalSeconds)
                {
                    StopSession("时间到");
                    yield break;
                }

                if (OnTick != null) OnTick();

                // 切图后场景会重新加载，等关卡索引真正变化再继续。
                if (_switching)
                {
                    _switchTimeout -= Time.unscaledDeltaTime;
                    string now = GameApi.LevelKey;
                    if (now != _switchFrom)
                    {
                        _switching = false;
                        _levelKey = now;
                        _paintedThisLevel = false;
                    }
                    else if (_switchTimeout <= 0f)
                    {
                        _switching = false;
                        _levelKey = now;
                        _paintedThisLevel = false;
                        SetStatus("切图超时，继续当前关卡");
                    }
                    else
                    {
                        SetStatus("正在载入下一张图……");
                        yield return null;
                        continue;
                    }
                }

                var ct = GameApi.Ct;
                if (ct == null || !GameApi.InLevel(ct))
                {
                    SetStatus("等待进入关卡……");
                    yield return null;
                    continue;
                }

                // 换图后重置计数状态（手动换图也能被识别）。
                string key = GameApi.LevelKey;
                if (key != _levelKey)
                {
                    _levelKey = key;
                    _paintedThisLevel = false;
                }

                if (_waitingForNextImage)
                {
                    _imagePauseTimer -= Time.unscaledDeltaTime;
                    if (_imagePauseTimer > 0f)
                    {
                        SetStatus(string.Format("下一张图 {0:F1}s", _imagePauseTimer));
                        yield return null;
                        continue;
                    }
                    _waitingForNextImage = false;
                }

                if (!_painter.Running)
                {
                    int remaining = GameApi.RemainingPixels(ct);
                    if (remaining <= 0)
                    {
                        // 本来就涂满的关卡（比如已完成过）不计入成绩，直接跳过。
                        if (_paintedThisLevel)
                        {
                            ImagesCompleted++;
                            int pixels = GameApi.InLevel(ct) ? GameApi.TotalPixels(ct) : 0;
                            UserProfile.RecordImageCompleted(pixels, XpTrack.Auto);
                            GameApi.SaveNow();
                            SetStatus(string.Format("已完成 {0} 张图", ImagesCompleted));
                        }
                        else
                        {
                            GameApi.SaveNow();
                        }

                        if (!Continuous)
                        {
                            StopSession("当前图片完成");
                            yield break;
                        }

                        string detail;
                        if (GameApi.LoadNextLevel(out detail))
                        {
                            _switchFrom = _levelKey;
                            _switching = true;
                            _switchTimeout = 8f;
                            _waitingForNextImage = true;
                            _imagePauseTimer = PauseBetweenImages;
                            SetStatus(string.Format("已完成 {0} 张图 · 正在切到{1}", ImagesCompleted, detail));
                        }
                        else
                        {
                            ImagesFailed++;
                            SetStatus("无法自动切图：" + detail);
                            // 停在原地等一会儿再试，避免死循环刷屏。
                            yield return new WaitForSeconds(2f);
                        }
                    }
                    else
                    {
                        _paintedThisLevel = true;
                        _painter.StartRun();
                    }
                }

                yield return null;
            }
        }
    }
}
