using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BlueprintTweaks.BlueprintDataSaveEdit;
using HarmonyLib;

namespace CraftingSpeed
{
    /// <summary>
    /// Carries assembler speed limits inside blueprints.
    ///
    /// The limits live next to each BlueprintData object in memory (keyed by its BlueprintBuilding
    /// objects) and are written into the blueprint string through BlueprintTweaks' custom data API,
    /// which appends a keyed block after all of the game's own blueprint data. The game stops
    /// reading before that block, so these blueprints still paste fine without the mod (at 100%).
    ///
    /// Flow:
    ///   copy  : BlueprintUtils.GenerateBlueprintData -> remember limits per building
    ///   string: BlueprintTweaks calls Serializer.Export / Import (also used by the clipboard,
    ///           which round-trips every copied blueprint through a string)
    ///   paste : BuildTool_BlueprintPaste.CreatePrebuilds -> pending limits for new ghosts,
    ///           or set directly on existing assemblers that the blueprint was pasted over
    /// </summary>
    internal static class BlueprintSupport
    {
        public const string DataKey = "CraftingSpeed";

        /// <summary>
        /// Speed limits of one blueprint. Buildings not listed run at 100%. A blueprint without an
        /// entry at all was made without this mod, and pasting it leaves existing limits alone.
        /// </summary>
        private sealed class Limits
        {
            public readonly Dictionary<BlueprintBuilding, int> ByBuilding = new Dictionary<BlueprintBuilding, int>();
        }

        private static readonly ConditionalWeakTable<BlueprintData, Limits> table = new ConditionalWeakTable<BlueprintData, Limits>();

        public static void Register()
        {
            BlueprintTweaks.BlueprintTweaksPlugin.RegisterCustomBlueprintDataSerializer<Serializer>(DataKey);
        }

        private static void Store(BlueprintData blueprint, Limits limits)
        {
            table.Remove(blueprint);
            table.Add(blueprint, limits);
        }

        private static Limits Lookup(BlueprintData blueprint) =>
            blueprint != null && table.TryGetValue(blueprint, out Limits limits) ? limits : null;

        // ------------------------------------------------------------------ copy

        [HarmonyPatch(typeof(BlueprintUtils), nameof(BlueprintUtils.GenerateBlueprintData))]
        internal static class CopyPatch
        {
            [HarmonyPostfix]
            private static void Postfix(BlueprintData _blueprintData, PlanetData _planet, int[] _objIds, int _objCount)
            {
                if (_blueprintData == null) return;
                var limits = new Limits();
                PlanetFactory factory = _planet?.factory;
                BlueprintBuilding[] buildings = _blueprintData.buildings;

                // GenerateBlueprintData creates buildings[i] for _objIds[i], in order.
                if (factory != null && buildings != null && _objIds != null)
                {
                    int count = System.Math.Min(_objCount, buildings.Length);
                    for (int i = 0; i < count; i++)
                    {
                        int? permille = SpeedLimit.GetForObject(factory, _objIds[i]);
                        if (permille.HasValue && permille.Value < SpeedLimit.Max)
                            limits.ByBuilding[buildings[i]] = permille.Value;
                    }
                }
                Store(_blueprintData, limits);
            }
        }

        // ------------------------------------------------------------------ string (BlueprintTweaks)

        /// <summary>
        /// Block format (version 1):
        ///   byte    version
        ///   varint  number of entries
        ///   entries sorted by building position: varint (position - previous position), varint permille
        /// </summary>
        public sealed class Serializer : ICustomBlueprintDataSerializer
        {
            private const byte Version = 1;

            public void Export(BlueprintData blueprint, BinaryWriter w)
            {
                Limits limits = Lookup(blueprint);
                BlueprintBuilding[] buildings = blueprint.buildings;
                if (limits == null || buildings == null) return; // empty block

                var entries = new List<(int position, int permille)>();
                for (int i = 0; i < buildings.Length; i++)
                    if (buildings[i] != null && limits.ByBuilding.TryGetValue(buildings[i], out int permille))
                        entries.Add((i, permille));

                w.Write(Version);
                WriteVarInt(w, entries.Count);
                int previous = 0;
                foreach (var (position, permille) in entries)
                {
                    WriteVarInt(w, position - previous);
                    WriteVarInt(w, permille);
                    previous = position;
                }
            }

            public void Import(BlueprintData blueprint, BinaryReader r)
            {
                byte version = r.ReadByte();
                if (version != Version)
                {
                    Plugin.Log.LogWarning($"Blueprint has crafting speed data version {version}, which this mod version cannot read.");
                    return;
                }
                BlueprintBuilding[] buildings = blueprint.buildings ?? new BlueprintBuilding[0];
                var limits = new Limits();
                int count = ReadVarInt(r);
                int position = 0;
                for (int i = 0; i < count; i++)
                {
                    position += ReadVarInt(r);
                    int permille = ReadVarInt(r);
                    if (position >= 0 && position < buildings.Length && buildings[position] != null)
                        limits.ByBuilding[buildings[position]] = SpeedLimit.Clamp(permille);
                }
                Store(blueprint, limits);
            }

            private static void WriteVarInt(BinaryWriter w, int value)
            {
                uint v = (uint)value;
                while (v >= 0x80)
                {
                    w.Write((byte)(v | 0x80));
                    v >>= 7;
                }
                w.Write((byte)v);
            }

            private static int ReadVarInt(BinaryReader r)
            {
                int result = 0, shift = 0;
                byte b;
                do
                {
                    b = r.ReadByte();
                    result |= (b & 0x7F) << shift;
                    shift += 7;
                } while ((b & 0x80) != 0 && shift < 35);
                return result;
            }
        }

        // ------------------------------------------------------------------ paste

        [HarmonyPatch(typeof(BuildTool_BlueprintPaste), nameof(BuildTool_BlueprintPaste.CreatePrebuilds))]
        internal static class PastePatch
        {
            [HarmonyPostfix]
            private static void Postfix(BuildTool_BlueprintPaste __instance)
            {
                Limits limits = Lookup(__instance.blueprint);
                PlanetFactory factory = __instance.factory;
                BlueprintBuilding[] buildings = __instance.blueprint?.buildings;
                BuildPreview[] pool = __instance.bpPool;
                if (limits == null || factory == null || buildings == null || buildings.Length == 0 || pool == null) return;

                // bpPool holds one preview per blueprint building per pasted copy:
                // preview i belongs to buildings[i % buildings.Length].
                int n = buildings.Length;
                int cursor = System.Math.Min(__instance.bpCursor, pool.Length);
                for (int i = 0; i < cursor; i++)
                {
                    BuildPreview preview = pool[i];
                    if (preview == null || preview.objId == 0 || preview.desc == null || !preview.desc.isAssembler) continue;

                    int permille = limits.ByBuilding.TryGetValue(buildings[i % n], out int p) ? p : SpeedLimit.Max;
                    if (preview.objId < 0)
                        PendingSpeeds.Set(factory.index, -preview.objId, permille);
                    else
                        SpeedLimit.SetForEntity(factory, preview.objId, permille); // pasted over an existing assembler
                }
            }
        }
    }
}
