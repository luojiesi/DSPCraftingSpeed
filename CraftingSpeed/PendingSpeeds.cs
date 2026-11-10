using System.Collections.Generic;
using HarmonyLib;

namespace CraftingSpeed
{
    /// <summary>
    /// Speed limits for assemblers that exist only as prebuilds (ghosts waiting for drones), e.g.
    /// right after pasting a blueprint. When the drone finishes the building, the limit is moved
    /// onto the real assembler. Kept per (factory index, prebuild id) and saved with the game so a
    /// save made mid-construction keeps the limits.
    /// </summary>
    internal static class PendingSpeeds
    {
        private static readonly Dictionary<long, int> pending = new Dictionary<long, int>();
        private static readonly object gate = new object();

        private static long Key(int factoryIndex, int prebuildId) => ((long)factoryIndex << 32) | (uint)prebuildId;

        public static void Set(int factoryIndex, int prebuildId, int permille)
        {
            if (prebuildId <= 0) return;
            permille = SpeedLimit.Clamp(permille);
            lock (gate)
            {
                if (permille == SpeedLimit.Max) pending.Remove(Key(factoryIndex, prebuildId));
                else pending[Key(factoryIndex, prebuildId)] = permille;
            }
        }

        public static bool TryGet(int factoryIndex, int prebuildId, out int permille)
        {
            lock (gate) return pending.TryGetValue(Key(factoryIndex, prebuildId), out permille);
        }

        public static bool TryTake(int factoryIndex, int prebuildId, out int permille)
        {
            lock (gate)
            {
                long key = Key(factoryIndex, prebuildId);
                if (!pending.TryGetValue(key, out permille)) return false;
                pending.Remove(key);
                return true;
            }
        }

        public static void Remove(int factoryIndex, int prebuildId)
        {
            lock (gate) pending.Remove(Key(factoryIndex, prebuildId));
        }

        public static void Clear()
        {
            lock (gate) pending.Clear();
        }

        public static List<(int factoryIndex, int prebuildId, int permille)> Snapshot()
        {
            var list = new List<(int, int, int)>();
            lock (gate)
                foreach (var kv in pending)
                    list.Add(((int)(kv.Key >> 32), (int)(uint)kv.Key, kv.Value));
            return list;
        }
    }

    /// <summary>A prebuild became a real building: hand its pending speed limit to the new assembler.</summary>
    [HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.AddEntityDataWithComponents))]
    internal static class PrebuildCompletedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PlanetFactory __instance, int prebuildId, int __result)
        {
            if (prebuildId <= 0 || __result <= 0) return;
            if (PendingSpeeds.TryTake(__instance.index, prebuildId, out int permille))
                SpeedLimit.SetForEntity(__instance, __result, permille);
        }
    }

    /// <summary>
    /// A prebuild was removed (built, cancelled or dismantled). Forget its entry so a later prebuild
    /// that reuses the same id does not pick it up.
    /// </summary>
    [HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemovePrebuildWithComponents))]
    internal static class PrebuildRemovedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(PlanetFactory __instance, int id)
        {
            if (id > 0) PendingSpeeds.Remove(__instance.index, id);
        }
    }
}
