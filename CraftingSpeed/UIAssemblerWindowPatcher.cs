using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.EventSystems.EventTrigger;


namespace CraftingSpeed
{
    [HarmonyPatch(typeof(UIAssemblerWindow))]
    internal class Patch_UIAssemblerWindow
    {
        public static MySlider slider;

        private static ManualLogSource logger;

        [HarmonyPostfix]
        [HarmonyPatch("_OnInit")]
        public static void Patch_OnInit(UIAssemblerWindow __instance)
        {
            Patch_UIAssemblerWindow.logger = BepInEx.Logging.Logger.CreateLogSource("Crafting Speed Mod");

            UIAssemblerWindow assemblerWindow = UIRoot.instance.uiGame.assemblerWindow;
            GameObject sliderObject = new GameObject("slider");
            Patch_UIAssemblerWindow.slider = MySlider.CreateSlider(assemblerWindow.windowTrans);
            Patch_UIAssemblerWindow.HideSlider(__instance);

            Patch_UIAssemblerWindow.slider.OnValueChanged += () =>
            {
                Patch_UIAssemblerWindow.UpdateSpeedMultiplierPerSlider(__instance);
            };
        }

        private static bool ShouldShowSlider(UIAssemblerWindow __instance)
        {
            if (__instance == null || !__instance.active || __instance.assemblerId == 0 || __instance.factory == null) 
                return false;
            AssemblerComponent assemblerComponent = __instance.factorySystem.assemblerPool[__instance.assemblerId];
            if (assemblerComponent.id != __instance.assemblerId) 
                return false;
            if (assemblerComponent.recipeType != ERecipeType.Assemble || assemblerComponent.recipeId == 0)
                return false;

            // correct some common edge cases when showing the slider
            if (assemblerComponent.speedMultiplier < 1 || assemblerComponent.speedMultiplier > 100)
            {
                __instance.factorySystem.assemblerPool[__instance.assemblerId].speedMultiplier = 100;
            }
            int value = (int)Patch_UIAssemblerWindow.slider.Value;
            if (value < 1 || value > 100)
            {
                Patch_UIAssemblerWindow.slider.Value = 100;
            }
            Patch_UIAssemblerWindow.slider.gameObject.SetActive(true);
            return true;
        }

        private static void UpdateSpeedMultiplierPerSlider(UIAssemblerWindow __instance)
        {
            int value = (int)Patch_UIAssemblerWindow.slider.Value;
            __instance.factorySystem.assemblerPool[__instance.assemblerId].speedMultiplier = value;
        }

        private static void UpdateSliderPerSpeedMultiplier(UIAssemblerWindow __instance)
        {
            int value = (int)__instance.factorySystem.assemblerPool[__instance.assemblerId].speedMultiplier;
            Patch_UIAssemblerWindow.slider.Value = __instance.factorySystem.assemblerPool[__instance.assemblerId].speedMultiplier;
        }

        private static void HideSlider(UIAssemblerWindow __instance)
        {
            Patch_UIAssemblerWindow.slider.gameObject.SetActive(false);
        }

        private static void ShowSlider(UIAssemblerWindow __instance)
        {
            Patch_UIAssemblerWindow.slider.gameObject.SetActive(true);
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnClose")]
        public static void Patch_OnClose(UIAssemblerWindow __instance)
        {
            Patch_UIAssemblerWindow.slider.Value = 100;
            Patch_UIAssemblerWindow.HideSlider(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnServingBoxChange")]
        public static void Patch_OnServingBoxChange(UIAssemblerWindow __instance)
        {
            if (__instance == null)
                return;
            Patch_UIAssemblerWindow.HideSlider(__instance);
            if (!Patch_UIAssemblerWindow.ShouldShowSlider(__instance))
                return;
            Patch_UIAssemblerWindow.ShowSlider(__instance);
            Patch_UIAssemblerWindow.UpdateSliderPerSpeedMultiplier(__instance);
        }
    }
}
