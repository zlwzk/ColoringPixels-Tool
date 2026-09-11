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

            ApplySpeedPreset(speedPreset);

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

        private void ApplySpeedPreset(int preset)
        {
            // 0 = 使用 Auto 页签里的自定义速度，1-3 = 预设
            if (preset == 0) return;

            switch (preset)
            {
                case 1: // 慢（更拟人，停顿多）
                    _painter.CellsPerSecond = 28f;
                    _painter.StrokeLength = 22;
                    _painter.PauseChance = 0.55f;
                    break;
                case 2: // 中
                    _painter.CellsPerSecond = 55f;
                    _painter.StrokeLength = 30;
                    _painter.PauseChance = 0.38f;
                    break;
                case 3: // 快
                    _painter.CellsPerSecond = 110f;
                    _painter.StrokeLength = 45;
                    _painter.PauseChance = 0.18f;
                    break;
            }
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
