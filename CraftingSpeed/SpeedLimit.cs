using System;
using System.Globalization;
using HarmonyLib;

namespace CraftingSpeed
{
    /// <summary>
    /// Reads and writes the per-assembler speed limit stored in AssemblerComponent.speedMultiplier
    /// (a field the preloader adds at startup, so it is reached through a ref accessor rather than
    /// compiled against directly).
    ///
    /// Unit used everywhere in this mod: permille (1 = 0.1%, 1000 = 100%).
    /// Stored value: 0 means 100%, 1..999 means that many permille.
    /// </summary>
    internal static class SpeedLimit
    {
        public const int Min = 1;
        public const int Max = 1000;
        public const string FieldName = "speedMultiplier";

        private static AccessTools.StructFieldRef<AssemblerComponent, int> field;

        public static bool Available => field != null;

        public static void Init()
        {
            try
            {
                field = AccessTools.StructFieldRefAccess<AssemblerComponent, int>(FieldName);
            }
            catch (Exception e)
            {
                field = null;
                Plugin.Log.LogError(
                    $"AssemblerComponent.{FieldName} is missing, so the preloader did not run " +
                    $"(is CraftingSpeedPreloader.dll in BepInEx/patchers?). Speed limits are disabled.\n{e.Message}");
            }
        }

        public static int Clamp(int permille) => permille < Min ? Min : permille > Max ? Max : permille;

        /// <summary>"50%", "12.5%" (one decimal only when needed).</summary>
        public static string Format(int permille) =>
            (permille % 10 == 0 ? (permille / 10).ToString(CultureInfo.InvariantCulture)
                                : (permille / 10.0).ToString("0.0", CultureInfo.InvariantCulture)) + "%";

        public static int Get(ref AssemblerComponent assembler)
        {
            int raw = field(ref assembler);
            return raw >= Min && raw < Max ? raw : Max;
        }

        public static void Set(ref AssemblerComponent assembler, int permille)
        {
            permille = Clamp(permille);
            field(ref assembler) = permille == Max ? 0 : permille;
        }

        /// <summary>Speed limit of the assembler behind an entity, or null if it is not an assembler.</summary>
        public static int? GetForEntity(PlanetFactory factory, int entityId)
        {
            if (!Available || factory == null || entityId <= 0 || entityId >= factory.entityCursor) return null;
            int assemblerId = factory.entityPool[entityId].assemblerId;
            if (assemblerId <= 0) return null;
            ref AssemblerComponent assembler = ref factory.factorySystem.assemblerPool[assemblerId];
            if (assembler.id != assemblerId) return null;
            return Get(ref assembler);
        }

        public static bool SetForEntity(PlanetFactory factory, int entityId, int permille)
        {
            if (!Available || factory == null || entityId <= 0 || entityId >= factory.entityCursor) return false;
            int assemblerId = factory.entityPool[entityId].assemblerId;
            if (assemblerId <= 0) return false;
            ref AssemblerComponent assembler = ref factory.factorySystem.assemblerPool[assemblerId];
            if (assembler.id != assemblerId) return false;
            Set(ref assembler, permille);
            return true;
        }

        /// <summary>
        /// Speed limit of any factory object: an assembler entity (objId &gt; 0) or an assembler that is
        /// still waiting to be built (objId &lt; 0, tracked by <see cref="PendingSpeeds"/>).
        /// </summary>
        public static int? GetForObject(PlanetFactory factory, int objId)
        {
            if (objId > 0) return GetForEntity(factory, objId);
            if (objId < 0 && factory != null && PendingSpeeds.TryGet(factory.index, -objId, out int pending)) return pending;
            return null;
        }

        // ---- Called from the transpiled AssemblerComponent.InternalUpdate (hot path, no allocations) ----

        public static int ScaledSpeedOverride(ref AssemblerComponent assembler) => Scale(assembler.speedOverride, field(ref assembler));

        public static int ScaledExtraSpeed(ref AssemblerComponent assembler) => Scale(assembler.extraSpeed, field(ref assembler));

        private static int Scale(int value, int raw) => raw >= Min && raw < Max ? (int)((long)value * raw / Max) : value;
    }
}
