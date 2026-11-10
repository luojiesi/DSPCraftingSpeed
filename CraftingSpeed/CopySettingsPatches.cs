using HarmonyLib;

namespace CraftingSpeed
{
    /// <summary>
    /// Makes the game's own "copy / paste building settings" also carry the speed limit
    /// between assemblers (and onto assemblers that are still ghosts).
    /// </summary>
    internal static class CopySettingsPatches
    {
        private static int? clipboard;

        [HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.CopyBuildingSetting))]
        internal static class CopyPatch
        {
            [HarmonyPostfix]
            private static void Postfix(PlanetFactory __instance, int objectId)
            {
                if (BuildingParameters.clipboard.type == BuildingType.Assembler)
                    clipboard = SpeedLimit.GetForObject(__instance, objectId) ?? SpeedLimit.Max;
                else
                    clipboard = null;
            }
        }

        [HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.PasteBuildingSetting))]
        internal static class PastePatch
        {
            [HarmonyPostfix]
            private static void Postfix(PlanetFactory __instance, int objectId)
            {
                if (!clipboard.HasValue || BuildingParameters.clipboard.type != BuildingType.Assembler) return;
                if (objectId > 0)
                    SpeedLimit.SetForEntity(__instance, objectId, clipboard.Value);
                else if (objectId < 0 && LDB.models.Select(__instance.prebuildPool[-objectId].modelIndex)?.prefabDesc?.isAssembler == true)
                    PendingSpeeds.Set(__instance.index, -objectId, clipboard.Value);
            }
        }
    }
}
