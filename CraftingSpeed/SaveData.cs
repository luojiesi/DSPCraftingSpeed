using System.IO;

namespace CraftingSpeed
{
    /// <summary>
    /// Save-file data (through DSPModSave, in the .moddsv next to the game save).
    ///
    /// Version 1 (mod 1.0.x): repeated (factoryIndex, assemblerId, permille 1..100), ended by (-1, -1, -1).
    /// Version 2: repeated (factoryIndex, assemblerId, permille 1..1000), ended by (-1, -1, -1), then
    ///            repeated (factoryIndex, prebuildId, permille) for assemblers still under
    ///            construction, also ended by (-1, -1, -1).
    /// Only limits below 100% are written.
    /// </summary>
    internal static class SaveData
    {
        private const int Version = 2;

        public static void Export(BinaryWriter w)
        {
            w.Write(Version);
            GameData data = GameMain.data;

            if (SpeedLimit.Available)
            {
                for (int f = 0; f < data.factoryCount; f++)
                {
                    PlanetFactory factory = data.factories[f];
                    if (factory == null) continue;
                    FactorySystem system = factory.factorySystem;
                    for (int id = 1; id < system.assemblerCursor; id++)
                    {
                        ref AssemblerComponent assembler = ref system.assemblerPool[id];
                        if (assembler.id != id) continue;
                        int permille = SpeedLimit.Get(ref assembler);
                        if (permille == SpeedLimit.Max) continue;
                        w.Write(f);
                        w.Write(id);
                        w.Write(permille);
                    }
                }
            }
            WriteEnd(w);

            foreach (var (factoryIndex, prebuildId, permille) in PendingSpeeds.Snapshot())
            {
                w.Write(factoryIndex);
                w.Write(prebuildId);
                w.Write(permille);
            }
            WriteEnd(w);
        }

        public static void Import(BinaryReader r)
        {
            PendingSpeeds.Clear();
            int version = r.ReadInt32();
            if (version < 1 || version > Version)
            {
                Plugin.Log.LogWarning($"Unknown save data version {version}; speed limits not loaded.");
                return;
            }

            GameData data = GameMain.data;
            int loaded = 0, skipped = 0;
            while (ReadEntry(r, out int f, out int id, out int value))
            {
                int permille = version == 1 ? value * 10 : value; // 1.0.x stored whole permilles
                // A bad entry is skipped on its own instead of dropping everything after it.
                if (SpeedLimit.Available && f >= 0 && f < data.factoryCount && data.factories[f] != null
                    && permille >= SpeedLimit.Min && permille <= SpeedLimit.Max)
                {
                    FactorySystem system = data.factories[f].factorySystem;
                    if (id > 0 && id < system.assemblerCursor && system.assemblerPool[id].id == id)
                    {
                        SpeedLimit.Set(ref system.assemblerPool[id], permille);
                        loaded++;
                        continue;
                    }
                }
                skipped++;
            }

            if (version >= 2)
            {
                while (ReadEntry(r, out int f, out int prebuildId, out int permille))
                {
                    if (f >= 0 && f < data.factoryCount && data.factories[f] != null
                        && prebuildId > 0 && prebuildId < data.factories[f].prebuildCursor
                        && data.factories[f].prebuildPool[prebuildId].id == prebuildId)
                        PendingSpeeds.Set(f, prebuildId, permille);
                    else
                        skipped++;
                }
            }

            Plugin.Log.LogInfo($"Loaded {loaded} assembler speed limits" + (skipped > 0 ? $" ({skipped} stale entries skipped)." : "."));
        }

        /// <summary>A new game or a save without our data: nothing pending may leak into it.</summary>
        public static void IntoOtherSave() => PendingSpeeds.Clear();

        private static void WriteEnd(BinaryWriter w)
        {
            w.Write(-1);
            w.Write(-1);
            w.Write(-1);
        }

        private static bool ReadEntry(BinaryReader r, out int a, out int b, out int c)
        {
            a = r.ReadInt32();
            b = r.ReadInt32();
            c = r.ReadInt32();
            return !(a == -1 && b == -1 && c == -1);
        }
    }
}
