using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 语音播报（TTS）。
    ///
    /// 走 Windows 自带的 SAPI（<c>SAPI.SpVoice</c>）COM 对象 —— 系统自带、无需安装、
    /// 无需额外引用程序集。Unity 的 Mono 里没有 System.Speech，所以只能晚绑定 COM。
    ///
    /// 播报在一条独立的后台线程里排队执行：SAPI 的 Speak 是阻塞调用，
    /// 直接在游戏主线程上喊会卡住整帧。拿不到 COM 对象时静默降级为「只弹窗不发声」。
    /// </summary>
    internal static class Tts
    {
        private static readonly Queue<string> Pending = new Queue<string>();
        private static readonly object Gate = new object();

        private static Thread _worker;
        private static volatile bool _alive;
        private static volatile bool _failed;

        private static string _status = "未初始化";
        private static int _rate;
        private static int _volume = 100;

        /// <summary>语速调节（-10 ~ 10，0 = 系统默认）。</summary>
        public static int Rate
        {
            get { return _rate; }
            set { _rate = Clamp(value, -10, 10); }
        }

        /// <summary>音量（0 ~ 100）。</summary>
        public static int Volume
        {
            get { return _volume; }
            set { _volume = Clamp(value, 0, 100); }
        }

        public static string Status
        {
            get { return _status; }
        }

        /// <summary>系统是否支持语音播报（还没试过就返回 null 语义的 true，等真正喊一次才知道）。</summary>
        public static bool Available
        {
            get { return !_failed; }
        }

        public static void Init()
        {
            if (_worker != null) return;

            _worker = new Thread(Loop);
            _worker.IsBackground = true;
            _worker.Name = "CPT-Tts";
            _alive = true;
            try
            {
                _worker.Start();
                _status = "待命";
            }
            catch (Exception e)
            {
                _failed = true;
                _status = "线程启动失败：" + e.Message;
                Log.Warn("语音播报线程启动失败：" + e.Message);
            }
        }

        public static void Shutdown()
        {
            _alive = false;
            lock (Gate) Pending.Clear();
        }

        /// <summary>把一句话丢进播报队列（不阻塞、可重复调用）。</summary>
        public static void Speak(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Init();
            if (_failed) return;

            lock (Gate)
            {
                // 队列太长说明卡住了，丢掉最老的，保证最新一句能喊出来
                while (Pending.Count >= 4) Pending.Dequeue();
                Pending.Enqueue(text);
                Monitor.Pulse(Gate);
            }
        }

        /// <summary>清空尚未播报的内容。</summary>
        public static void Clear()
        {
            lock (Gate) Pending.Clear();
        }

        private static void Loop()
        {
            object voice = null;

            while (_alive)
            {
                string text = null;
                lock (Gate)
                {
                    if (Pending.Count > 0) text = Pending.Dequeue();
                    else Monitor.Wait(Gate, 150);
                }
                if (text == null) continue;

                try
                {
                    if (voice == null) voice = CreateVoice();
                    if (voice == null) continue;

                    Type type = voice.GetType();
                    type.InvokeMember("Rate", BindingFlags.SetProperty, null, voice, new object[] { _rate });
                    type.InvokeMember("Volume", BindingFlags.SetProperty, null, voice, new object[] { _volume });
                    // flags = 0：同步朗读（本线程就是后台线程，慢一点无所谓）
                    type.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { text, 0 });
                }
                catch (Exception e)
                {
                    _failed = true;
                    _status = "播报失败：" + e.Message;
                    Log.Warn("语音播报失败（已自动关闭播报）：" + e.Message);
                }
            }

            if (voice != null)
            {
                try { Marshal_Release(voice); } catch (Exception) { }
            }
        }

        private static object CreateVoice()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (type == null)
                {
                    _failed = true;
                    _status = "系统未安装语音组件（SAPI）";
                    Log.Warn("语音播报不可用：找不到 SAPI.SpVoice");
                    return null;
                }

                object voice = Activator.CreateInstance(type);
                _status = "可用";
                Log.Info("语音播报已就绪（SAPI）");
                return voice;
            }
            catch (Exception e)
            {
                _failed = true;
                _status = "创建语音对象失败：" + e.Message;
                Log.Warn("语音播报不可用：" + e.Message);
                return null;
            }
        }

        private static void Marshal_Release(object comObject)
        {
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch (Exception)
            {
            }
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
