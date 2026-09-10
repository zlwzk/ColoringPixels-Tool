using System;
using UnityEngine;
using UnityEngine.UI;

namespace ColoringPixelsTool
{
    /// <summary>
    /// 在游戏自带的设置界面里注入一个「推荐预设」按钮。
    ///
    /// 按钮外观通过克隆设置面板里已有的按钮实现，所以形状、颜色、字体、动画
    /// 都和游戏本身保持一致；只有在面板里一个按钮都找不到时才会用代码兜底创建一个。
    /// </summary>
    internal class PresetButtonInjector : MonoBehaviour
    {
        internal const string ButtonName = "CPT_PresetButton";
        private const string NamePrefix = "CPT_";

        private const float Interval = 0.75f;

        private float _next;
        private bool _cleanupDone;
        private string _appliedLabel;

        private void Update()
        {
            bool enabled = Plugin.PresetButtonEnabled == null || Plugin.PresetButtonEnabled.Value;

            if (!enabled)
            {
                if (!_cleanupDone)
                {
                    _cleanupDone = true;
                    RemoveExisting();
                }
                return;
            }

            _cleanupDone = false;

            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;

            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Log.Warn("注入推荐预设按钮失败：" + e.Message);
            }
        }

        private void Tick()
        {
            Transform panel = GameSettingsPanel.Locate();
            if (panel == null) return;

            int up = Plugin.PresetButtonParentUp != null
                ? Mathf.Clamp(Plugin.PresetButtonParentUp.Value, 0, 4)
                : 0;
            for (int i = 0; i < up && panel.parent != null; i++) panel = panel.parent;

            string label = Plugin.PresetButtonLabel != null && !string.IsNullOrEmpty(Plugin.PresetButtonLabel.Value)
                ? Plugin.PresetButtonLabel.Value
                : "推荐预设";

            GameObject existing = FindDeep(panel, ButtonName);
            if (existing != null)
            {
                ApplyLabel(existing, label);
                return;
            }

            Button template = FindTemplate(panel);
            GameObject go = template != null ? CloneTemplate(template) : BuildFallback(panel);
            if (go == null) return;

            go.name = ButtonName;
            go.SetActive(true);

            HookClick(go);
            ApplyLabel(go, label);
            Place(go, template, panel);

            Log.Info(string.Format("已在游戏设置界面注入「{0}」按钮（父节点：{1}，模板：{2}）。",
                label, panel.gameObject.name, template != null ? template.gameObject.name : "代码创建"));
        }

        // ------------------------------------------------------------------ 创建

        private GameObject CloneTemplate(Button template)
        {
            Transform parent = template.transform.parent != null ? template.transform.parent : template.transform;
            return Instantiate(template.gameObject, parent, false);
        }

        private static GameObject BuildFallback(Transform panel)
        {
            var go = new GameObject(ButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(panel, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180f, 40f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.17f, 0.17f, 0.22f, 0.96f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
            btn.colors = cb;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);

            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            var t = labelGo.GetComponent<Text>();
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.fontSize = 18;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            Font f = CjkFont.Get();
            if (f != null) t.font = f;

            return go;
        }

        private static void HookClick(GameObject go)
        {
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();

            // 克隆体会带来原来的监听，这里整体换掉，避免误触发游戏自身的功能。
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(OnPresetClicked);
            btn.interactable = true;
        }

        private static void OnPresetClicked()
        {
            try
            {
                string report = GamePreset.Apply();
                GameToast.Show(report);
            }
            catch (Exception e)
            {
                Log.Error("应用推荐预设失败：" + e.Message);
                GameToast.Show("应用推荐预设失败，详见日志。");
            }
        }

        // ------------------------------------------------------------------ 文案

        private void ApplyLabel(GameObject go, string label)
        {
            Text[] texts = go.GetComponentsInChildren<Text>(true);
            if (texts.Length == 0) return;

            Text target = texts[0];
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && !string.IsNullOrEmpty(texts[i].text))
                {
                    target = texts[i];
                    break;
                }
            }
            if (target == null) return;

            Font f = CjkFont.HasCjk(label) ? CjkFont.Get() : null;

            if (target.text == label && (f == null || target.font == f)) return;

            target.text = label;
            if (f != null && target.font != f)
            {
                target.font = f;
                target.material = null;
                target.SetAllDirty();
            }

            _appliedLabel = label;
        }

        // ------------------------------------------------------------------ 定位

        private void Place(GameObject go, Button template, Transform panel)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;

            int placement = Plugin.PresetButtonPlacement != null ? Plugin.PresetButtonPlacement.Value : 0;
            float ox = Plugin.PresetButtonOffsetX != null ? Plugin.PresetButtonOffsetX.Value : 0f;
            float oy = Plugin.PresetButtonOffsetY != null ? Plugin.PresetButtonOffsetY.Value : 0f;

            // 自动模式：优先跟着模板按钮走
            if (placement == 0 && template != null)
            {
                Transform tp = template.transform.parent;
                LayoutGroup layout = tp != null ? tp.GetComponent<LayoutGroup>() : null;
                if (layout != null)
                {
                    go.transform.SetSiblingIndex(Mathf.Min(template.transform.GetSiblingIndex() + 1, tp.childCount - 1));
                    return;
                }

                var trt = template.transform as RectTransform;
                if (trt != null)
                {
                    rt.anchorMin = trt.anchorMin;
                    rt.anchorMax = trt.anchorMax;
                    rt.pivot = trt.pivot;

                    float h = trt.rect.height > 1f ? trt.rect.height : 36f;
                    rt.anchoredPosition = trt.anchoredPosition + new Vector2(ox, -h - 8f + oy);
                    rt.localScale = Vector3.one;
                    rt.localRotation = Quaternion.identity;
                    return;
                }
            }

            // 手动锚定到面板
            Vector2 anchor;
            if (placement == 2) anchor = new Vector2(0.5f, 0.5f);      // 居中
            else if (placement == 3) anchor = new Vector2(0.5f, 1f);   // 顶部
            else anchor = new Vector2(0.5f, 0f);                       // 底部（默认）

            rt.SetParent(panel, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = new Vector2(ox, oy);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.SetAsLastSibling();
        }

        private static Button FindTemplate(Transform panel)
        {
            string want = Plugin.PresetButtonTemplate != null ? Plugin.PresetButtonTemplate.Value : null;
            if (!string.IsNullOrEmpty(want))
            {
                GameObject go = FindDeep(panel, want);
                if (go != null && go.name != ButtonName)
                {
                    var b = go.GetComponent<Button>();
                    if (b != null) return b;
                }
                Log.Warn("没有找到模板按钮「" + want + "」，将自动挑选。");
            }

            Button[] buttons = panel.GetComponentsInChildren<Button>(true);
            Button spriteFallback = null;

            for (int i = 0; i < buttons.Length; i++)
            {
                Button b = buttons[i];
                if (b == null) continue;
                if (b.gameObject.name == ButtonName) continue;
                if (b.gameObject.name.StartsWith(NamePrefix, StringComparison.Ordinal)) continue;
                if (!b.interactable) continue;

                var img = b.GetComponent<Image>();
                if (img == null || img.sprite == null) continue;

                if (spriteFallback == null) spriteFallback = b;

                Text t = b.GetComponentInChildren<Text>(true);
                if (t != null && t.text != null && t.text.Trim() == "Apply")
                    return b;
            }

            if (spriteFallback != null) return spriteFallback;

            for (int i = 0; i < buttons.Length; i++)
            {
                Button b = buttons[i];
                if (b == null) continue;
                if (b.gameObject.name == ButtonName) continue;
                if (b.gameObject.name.StartsWith(NamePrefix, StringComparison.Ordinal)) continue;
                if (!b.interactable) continue;
                return b;
            }

            return null;
        }

        private static GameObject FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.gameObject.name == name) return root.gameObject;

            for (int i = 0; i < root.childCount; i++)
            {
                GameObject r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private static void RemoveExisting()
        {
            try
            {
                Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null) continue;
                    if (t.gameObject.name != ButtonName) continue;
                    if (t.hideFlags != HideFlags.None) continue;
                    if (!t.gameObject.scene.IsValid()) continue;
                    Destroy(t.gameObject);
                }
            }
            catch (Exception e)
            {
                Log.Warn("移除预设按钮失败：" + e.Message);
            }
        }
    }
}
