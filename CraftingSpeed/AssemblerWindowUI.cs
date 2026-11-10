using System;
using System.Globalization;
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
            Text style = window.stateText;

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

            MakeText(row, style, L("限速", "Speed limit"), 64f, TextAnchor.MiddleLeft);
            MakeButton(row, "-", 24f, () => StepBy(-1));
            MakeSlider(row);
            MakeButton(row, "+", 24f, () => StepBy(+1));
            valueText = MakeText(row, style, "100%", 48f, TextAnchor.MiddleCenter);
            MakeButton(row, "100%", 44f, () => Apply(SpeedLimit.Max));
            MakeSpacer(row, 10f);
            MakeText(row, style, L("目标", "Target"), 36f, TextAnchor.MiddleRight);
            MakeTargetInput(row);
            MakeText(row, style, L("/分钟", "/min"), 44f, TextAnchor.MiddleLeft);

            go.SetActive(false);
        }

        private static Text MakeText(Transform parent, Text style, string text, float width, TextAnchor align)
        {
            var go = new GameObject("text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = style.font;
            t.fontSize = style.fontSize;
            t.color = style.color;
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
            ((RectTransform)go.transform).sizeDelta = new Vector2(150f, 16f);

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
            ((RectTransform)go.transform).sizeDelta = new Vector2(64f, ControlHeight);

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
        /// Puts the row where the inventory starts, then moves the inventory down and makes the window
        /// taller by one row. The game resets both every frame, so this is applied every frame too.
        /// </summary>
        private static void PlaceRow(UIAssemblerWindow w)
        {
            RectTransform inv = w.playerInventoryGroup;
            var corners = new Vector3[4];
            inv.GetWorldCorners(corners);
            Vector2 invTopLeft = w.windowTrans.InverseTransformPoint(corners[1]);
            Rect windowRect = w.windowTrans.rect;

            float x = Plugin.RowOffsetX.Value;
            float y = invTopLeft.y - windowRect.yMax + Plugin.RowOffsetY.Value;
            row.anchoredPosition = new Vector2(x, y);
            row.sizeDelta = new Vector2(windowRect.width - x * 2f, RowHeight);

            inv.anchoredPosition += new Vector2(0f, -RowHeight);
            w.windowTrans.sizeDelta += new Vector2(0f, RowHeight);

            if (!layoutLogged)
            {
                layoutLogged = true;
                Plugin.Log.LogInfo($"Window layout: window {windowRect}, inventory at {inv.anchoredPosition + new Vector2(0f, RowHeight)} " +
                                   $"(anchors {inv.anchorMin}-{inv.anchorMax}, pivot {inv.pivot}), speedGroup {w.speedGroup.anchoredPosition}, " +
                                   $"incInfoGroup {w.incInfoGroup.anchoredPosition} active={w.incInfoGroup.gameObject.activeSelf}, row at {row.anchoredPosition}.");
            }
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
