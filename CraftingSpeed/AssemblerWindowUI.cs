using System;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CraftingSpeed
{
    /// <summary>
    /// Speed-limit row in the assembler window, inserted between the proliferator info and the
    /// player inventory (the window is made taller and the inventory moved down to make room):
    ///
    ///   Speed limit  [-] [=====slider=====] [+]  50%  [100%]    Target [  1.5 ] /min
    ///
    /// Buttons and the mouse wheel step by 1% (Shift: 10%, Ctrl: 0.1%). The target box sets the
    /// limit from a desired output per minute. The window's own "per minute" readout shows the
    /// limited rate, and the status line says when a machine is limited.
    /// </summary>
    [HarmonyPatch(typeof(UIAssemblerWindow))]
    internal static class AssemblerWindowUI
    {
        private const float RowHeight = 34f;
        private const float ControlHeight = 22f;
        private const int Step = 10, BigStep = 100, SmallStep = 1; // permille

        private static readonly Color LimitedColor = new Color(1f, 0.68f, 0.28f, 1f);
        private static readonly Color FillNormal = new Color(1f, 1f, 1f, 0.28f);
        private static readonly Color LabelColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color FillLimited = new Color(1f, 0.68f, 0.28f, 0.6f);

        private static RectTransform row;
        private static Slider slider;
        private static Image sliderFill;
        private static Text valueText;
        private static InputField targetInput;
        private static Color speedTextColor;
        private static bool updatingFromGame;
        private static bool layoutLogged;

        private static string L(string zh, string en) => Localization.isZHCN ? zh : en;

        // ------------------------------------------------------------------ build

        [HarmonyPostfix]
        [HarmonyPatch("_OnInit")]
        private static void OnInit(UIAssemblerWindow __instance)
        {
            if (row != null) return;
            try
            {
                Build(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not build the speed limit controls: {e}");
                if (row != null) Object.Destroy(row.gameObject);
                row = null;
            }
        }

        private static void Build(UIAssemblerWindow window)
        {
            speedTextColor = window.speedText.color;
            // Same look as the "增产效果" label on the line above, falling back to the status text.
            Text style = window.incInfoGroup.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "inc-label") ?? window.stateText;

            var go = new GameObject("crafting-speed-row", typeof(RectTransform));
            row = (RectTransform)go.transform;
            row.SetParent(window.windowTrans, false);
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 1f); // positioned from the window's top-left
            row.sizeDelta = new Vector2(window.windowTrans.rect.width, RowHeight);

            // Transparent background so the mouse wheel works anywhere on the row.
            var hitArea = go.AddComponent<Image>();
            hitArea.color = Color.clear;
            go.AddComponent<WheelStepper>();

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 6f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            MakeText(row, style, L("限速", "Limit"), 40f, TextAnchor.MiddleLeft);
            MakeButton(row, "-", 22f, () => StepBy(-1));
            MakeSlider(row);
            MakeButton(row, "+", 22f, () => StepBy(+1));
            valueText = MakeText(row, style, "100%", 46f, TextAnchor.MiddleCenter);
            MakeButton(row, L("重置", "Reset"), 40f, () => Apply(SpeedLimit.Max));
            MakeSpacer(row, 8f);
            MakeText(row, style, L("目标", "Target"), 32f, TextAnchor.MiddleRight);
            MakeTargetInput(row);
            MakeText(row, style, L("/分钟", "/min"), 40f, TextAnchor.MiddleLeft);

            go.SetActive(false);
        }

        private static Text MakeText(Transform parent, Text style, string text, float width, TextAnchor align)
        {
            var go = new GameObject("text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = style.font;
            t.fontSize = style.fontSize;
            t.color = LabelColor; // the source text is still grey when the window is built; the game brightens it later
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, ControlHeight);
            return t;
        }

        private static void MakeSpacer(Transform parent, float width)
        {
            var go = new GameObject("spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, ControlHeight);
        }

        private static void MakeButton(Transform parent, string label, float width, Action onClick)
        {
            var go = new GameObject("button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, ControlHeight);

            var image = go.AddComponent<Image>();
            image.color = Color.white;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.12f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.24f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.36f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            Text text = MakeText(go.transform, UIRoot.instance.uiGame.assemblerWindow.stateText, label, width, TextAnchor.MiddleCenter);
            var rt = (RectTransform)text.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            text.color = Color.white;
        }

        private static void MakeSlider(Transform parent)
        {
            Slider source = UIRoot.instance?.optionWindow?.audioVolumeComp;
            if (source == null) throw new InvalidOperationException("options window slider not found");

            GameObject go = Object.Instantiate(source.gameObject, parent, false);
            go.name = "slider";
            go.SetActive(true);
            ((RectTransform)go.transform).sizeDelta = new Vector2(120f, 16f);

            slider = go.GetComponent<Slider>();
            slider.onValueChanged = new Slider.SliderEvent(); // drop everything wired to the options window
            slider.minValue = SpeedLimit.Min;
            slider.maxValue = SpeedLimit.Max;
            slider.wholeNumbers = true;
            slider.value = SpeedLimit.Max;
            slider.onValueChanged.AddListener(v => { if (!updatingFromGame) Apply(Mathf.RoundToInt(v)); });

            // The value is shown next to the slider instead of on the small handle.
            Text handleText = slider.handleRect != null ? slider.handleRect.Find("Text")?.GetComponent<Text>() : null;
            if (handleText != null) handleText.gameObject.SetActive(false);
            if (slider.handleRect != null)
            {
                // Without its number the handle is just a wide dark box; make it a thin bright bar.
                slider.handleRect.sizeDelta = new Vector2(6f, slider.handleRect.sizeDelta.y);
                Image handle = slider.handleRect.GetComponent<Image>();
                if (handle != null) handle.color = new Color(1f, 1f, 1f, 0.85f);
            }

            Image background = slider.transform.Find("Background")?.GetComponent<Image>();
            if (background != null) background.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            sliderFill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
        }

        private static void MakeTargetInput(Transform parent)
        {
            InputField source = UIRoot.instance?.uiGame?.beltWindow?.iconTagCountInput;
            if (source == null)
            {
                Plugin.Log.LogWarning("Belt window input field not found; the target-per-minute box is not available.");
                return;
            }
            GameObject go = Object.Instantiate(source.gameObject, parent, false);
            go.name = "target";
            go.SetActive(true);
            ((RectTransform)go.transform).sizeDelta = new Vector2(56f, ControlHeight);

            targetInput = go.GetComponent<InputField>();
            targetInput.onEndEdit = new InputField.EndEditEvent();
            targetInput.onValueChanged = new InputField.OnChangeEvent();
            targetInput.contentType = InputField.ContentType.DecimalNumber;
            targetInput.characterLimit = 8;
            targetInput.onEndEdit.AddListener(OnTargetEntered);
        }

        // ------------------------------------------------------------------ per frame

        [HarmonyPostfix]
        [HarmonyPatch("_OnUpdate")]
        private static void OnUpdate(UIAssemblerWindow __instance)
        {
            if (row == null || !__instance.active) return;
            UIAssemblerWindow w = __instance;

            if (!TryGetAssembler(w, out AssemblerComponent assembler, out int permille))
            {
                if (row.gameObject.activeSelf) row.gameObject.SetActive(false);
                w.speedText.color = speedTextColor;
                return;
            }
            if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);

            PlaceRow(w);

            bool limited = permille < SpeedLimit.Max;
            if (Mathf.RoundToInt(slider.value) != permille)
            {
                updatingFromGame = true;
                slider.value = permille;
                updatingFromGame = false;
            }
            valueText.text = SpeedLimit.Format(permille);
            valueText.color = limited ? LimitedColor : w.stateText.color;
            if (sliderFill != null) sliderFill.color = limited ? FillLimited : FillNormal;

            // The game's own readout ignores the limit; replace it with the real rate.
            double shown = DisplayedRate(w, ref assembler) * permille / SpeedLimit.Max;
            w.speedText.text = shown.ToString("0.0") + "每分钟".Translate();
            w.speedText.color = limited ? LimitedColor : speedTextColor;
            if (limited)
                w.stateText.text += "  ·  " + L("限速 ", "limited to ") + SpeedLimit.Format(permille);

            if (targetInput != null && !targetInput.isFocused)
            {
                double target = FullRate(ref assembler) * permille / SpeedLimit.Max;
                targetInput.text = target > 0 ? target.ToString("0.##", CultureInfo.InvariantCulture) : "";
            }
        }

        /// <summary>
        /// Puts the row where the inventory section starts (including its title line, which is drawn
        /// above the group's own rectangle), then moves the inventory down and makes the window taller
        /// by one row. The game resets both every frame, so this is applied every frame too.
        /// </summary>
        private static void PlaceRow(UIAssemblerWindow w)
        {
            RectTransform inv = w.playerInventoryGroup;
            Rect windowRect = w.windowTrans.rect;
            Bounds invBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(w.windowTrans, inv);

            // Line up with the left edge of the content above (the "增产效果" label), keep the same margin on the right.
            float x = LeftMargin(w, windowRect) + Plugin.RowNudgeX.Value;
            float y = invBounds.max.y - windowRect.yMax + Plugin.RowNudgeY.Value;
            row.anchoredPosition = new Vector2(x, y);
            row.sizeDelta = new Vector2(windowRect.width - x * 2f, RowHeight);

            inv.anchoredPosition += new Vector2(0f, -RowHeight);
            w.windowTrans.sizeDelta += new Vector2(0f, RowHeight);

            if (!layoutLogged)
            {
                layoutLogged = true;
                LogLayout(w, invBounds, windowRect);
            }
        }

        private static float LeftMargin(UIAssemblerWindow w, Rect windowRect)
        {
            if (w.incInfoGroup.gameObject.activeInHierarchy)
            {
                Bounds inc = RectTransformUtility.CalculateRelativeRectTransformBounds(w.windowTrans, w.incInfoGroup);
                if (inc.size.x > 1f) return inc.min.x - windowRect.xMin;
            }
            return 39f;
        }

        /// <summary>One-time layout dump to BepInEx's log, to tune placement from a screenshot.</summary>
        private static void LogLayout(UIAssemblerWindow w, Bounds invBounds, Rect windowRect)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"Window layout: window {windowRect}; inventory group pos {w.playerInventoryGroup.anchoredPosition} (before shift +{RowHeight}), ");
            sb.Append($"bounds y {invBounds.min.y:0}..{invBounds.max.y:0}; row at {row.anchoredPosition}; ");
            Bounds inc = RectTransformUtility.CalculateRelativeRectTransformBounds(w.windowTrans, w.incInfoGroup);
            sb.Append($"incInfoGroup active={w.incInfoGroup.gameObject.activeSelf} y {inc.min.y:0}..{inc.max.y:0}.");
            foreach (Text t in w.windowTrans.GetComponentsInChildren<Text>(false))
            {
                if (t.transform.IsChildOf(row)) continue;
                Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(w.windowTrans, t.transform);
                if (b.max.y < invBounds.max.y - 60 || b.min.y > invBounds.max.y + 80) continue; // only texts around the inventory top
                sb.Append($"\n  text '{t.text}' at y {b.min.y:0}..{b.max.y:0}, x {b.min.x:0}..{b.max.x:0}, path {PathOf(t.transform, w.windowTrans)}");
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnClose")]
        private static void OnClose(UIAssemblerWindow __instance)
        {
            if (row != null) row.gameObject.SetActive(false);
            __instance.speedText.color = speedTextColor;
        }

        // ------------------------------------------------------------------ actions

        private static void Apply(int permille)
        {
            UIAssemblerWindow w = UIRoot.instance?.uiGame?.assemblerWindow;
            if (w == null || !TryGetAssembler(w, out _, out _)) return;
            SpeedLimit.Set(ref w.factorySystem.assemblerPool[w.assemblerId], permille);
        }

        private static void StepBy(int direction)
        {
            UIAssemblerWindow w = UIRoot.instance?.uiGame?.assemblerWindow;
            if (w == null || !TryGetAssembler(w, out _, out int permille)) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            int step = shift ? BigStep : ctrl ? SmallStep : Step;
            Apply(permille + direction * step);
        }

        private static void OnTargetEntered(string text)
        {
            UIAssemblerWindow w = UIRoot.instance?.uiGame?.assemblerWindow;
            if (w == null || !TryGetAssembler(w, out AssemblerComponent assembler, out _)) return;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double target) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out target)) return;
            double full = FullRate(ref assembler);
            if (full <= 0 || target <= 0) return;
            Apply((int)Math.Round(target / full * SpeedLimit.Max));
        }

        internal static void OnWheel(float delta)
        {
            if (delta > 0) StepBy(+1);
            else if (delta < 0) StepBy(-1);
        }

        // ------------------------------------------------------------------ helpers

        private static bool TryGetAssembler(UIAssemblerWindow w, out AssemblerComponent assembler, out int permille)
        {
            assembler = default;
            permille = SpeedLimit.Max;
            if (!SpeedLimit.Available || !w.active || w.factorySystem == null) return false;
            int id = w.assemblerId;
            if (id <= 0 || id >= w.factorySystem.assemblerCursor) return false;
            ref AssemblerComponent a = ref w.factorySystem.assemblerPool[id];
            if (a.id != id || a.recipeId == 0 || a.recipeType != ERecipeType.Assemble || a.recipeExecuteData == null) return false;
            permille = SpeedLimit.Get(ref a);
            assembler = a;
            return true;
        }

        /// <summary>What the game would show as "per minute" right now (same formula as UIAssemblerWindow._OnUpdate).</summary>
        private static double DisplayedRate(UIAssemblerWindow w, ref AssemblerComponent a)
        {
            if (!a.replicating || a.speedOverride <= 0) return 0;
            PowerConsumerComponent consumer = w.powerSystem.consumerPool[a.pcId];
            PowerNetwork network = w.powerSystem.netPool[consumer.networkId];
            double power = network != null && consumer.networkId > 0 ? network.consumerRatio : 0;
            double speed = 0.0001 * a.speedOverride * power;
            if (speed < 1e-5) return 0;
            return CyclesPerMinuteAtBaseSpeed(a) * speed * (1 + a.extraSpeed * 0.1 / a.speedOverride);
        }

        /// <summary>Output per minute at 100% with full power and the current proliferator effect.</summary>
        private static double FullRate(ref AssemblerComponent a)
        {
            int so = a.speedOverride > 0 ? a.speedOverride : a.speed;
            if (so <= 0) return 0;
            return CyclesPerMinuteAtBaseSpeed(a) * 0.0001 * so * (1 + a.extraSpeed * 0.1 / so);
        }

        private static double CyclesPerMinuteAtBaseSpeed(AssemblerComponent a)
        {
            int timeSpend = a.recipeExecuteData.timeSpend;
            return timeSpend > 0 ? 60.0 / (timeSpend / 600000.0) : 0;
        }
    }

    /// <summary>Mouse wheel anywhere on the speed row steps the limit.</summary>
    internal sealed class WheelStepper : MonoBehaviour, IScrollHandler
    {
        public void OnScroll(PointerEventData eventData) => AssemblerWindowUI.OnWheel(eventData.scrollDelta.y);
    }
}
