using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CraftingSpeed
{
    [HarmonyPatch(typeof(AssemblerComponent), nameof(AssemblerComponent.InternalUpdate))]
    internal class AssemblerComponentPatcher
    {
        public static void Prefix(ref AssemblerComponent __instance, out PatchState __state)
        {
            __state = null;

            if (__instance.recipeType != ERecipeType.Assemble || __instance.recipeId == 0)
            {
                return;
            }

            __state = new PatchState
            {
                Speed = __instance.speedOverride,
            };

            if (__instance.speedMultiplier < 1 || __instance.speedMultiplier > 100)
            {
                __instance.speedMultiplier = 100;
            }
            if (__instance.speedMultiplier == 100)
            {
                return;
            }

            __instance.speedOverride = (int)(__instance.speedOverride * __instance.speedMultiplier / 100.0);
        }

        public static void Postfix(ref AssemblerComponent __instance, PatchState __state)
        {
            if (__state == null)
            {
                return;
            }
            __instance.speedOverride = __state.Speed;
        }
    }
}
