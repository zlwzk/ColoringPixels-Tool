using System;
using System.Collections;
using System.Reflection;
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

                var ct = GameApi.Ct;
                if (ct == null || !GameApi.InLevel(ct))
                {
                    SetStatus("等待进入关卡……");
                    yield return null;
                    continue;
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
                        ImagesCompleted++;
                        GameApi.SaveNow();
                        SetStatus(string.Format("已完成 {0} 张图", ImagesCompleted));

                        if (Continuous)
                        {
                            if (TryLoadNextLevel())
                            {
                                _waitingForNextImage = true;
                                _imagePauseTimer = PauseBetweenImages;
                            }
                            else
                            {
                                ImagesFailed++;
                                SetStatus("找不到自动切图入口，请手动打开下一张图");
                                yield return new WaitForSeconds(1f);
                            }
                        }
                        else
                        {
                            StopSession("当前图片完成");
                            yield break;
                        }
                    }
                    else
                    {
                        _painter.StartRun();
                    }
                }

                yield return null;
            }
        }

        /// <summary>
        /// 通过反射寻找并调用游戏的「下一关/下一张图」方法。
        /// 因为不同版本的游戏方法名可能不同，这里用一组常见名字做启发式匹配。
        /// </summary>
        private bool TryLoadNextLevel()
        {
            try
            {
                string[] instanceNames = new string[] { "NextLevel", "NextImage", "LoadNextLevel", "AdvanceLevel", "Next" };
                string[] staticNames = new string[] { "NextLevel", "LoadNextLevel", "AdvanceLevel", "LoadNextImage", "NextImage" };

                var ct = GameApi.Ct;
                if (ct != null)
                {
                    Type type = ct.GetType();
                    foreach (var name in instanceNames)
                    {
                        MethodInfo m = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (m != null && m.GetParameters().Length == 0)
                        {
                            m.Invoke(ct, null);
                            Log.Info("AutoScheduler 调用下一关: ClickTest." + name);
                            return true;
                        }
                    }
                }

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var t in asm.GetTypes())
                    {
                        foreach (var name in staticNames)
                        {
                            MethodInfo m = t.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                            if (m != null && m.GetParameters().Length == 0)
                            {
                                m.Invoke(null, null);
                                Log.Info("AutoScheduler 调用下一关: " + t.FullName + "." + name);
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("自动切图失败: " + e.Message);
            }
            return false;
        }
    }
}
