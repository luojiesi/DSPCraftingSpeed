using BepInEx;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using crecheng.DSPModSave;

namespace CraftingSpeed
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    [BepInDependency(DSPModSavePlugin.MODGUID)]

    public class AssemblerSpeedMod : BaseUnityPlugin, IModCanSave
    {
        public const string pluginGuid = "com.github.jiesiluo.assemblerspeed";
        public const string pluginName = "Assembler Speed";
        public const string pluginVersion = "1.0.0";

        public const int saveVersion = 1;

        private Harmony harmony;

        public void Awake()
        {
            this.harmony = new Harmony(pluginGuid);
            this.harmony.PatchAll();
        }

        public void Import(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version != saveVersion)
                return;

            while (true)
            {
                int planetId = r.ReadInt32();
                int assemblerId = r.ReadInt32();
                int speedMultiplier = r.ReadInt32();
                if (planetId == -1 && assemblerId == -1 && speedMultiplier == -1)
                    break;

                var data = GameMain.data;
                if (planetId < 0 || planetId >= data.factoryCount)
                    break;
                PlanetFactory factory = data.factories[planetId];
                if (factory == null)
                    break;
                if (assemblerId < 0 || assemblerId >= factory.factorySystem.assemblerCursor)
                    break;
                AssemblerComponent assembler = factory.factorySystem.assemblerPool[assemblerId];
                if (assembler.id != assemblerId)
                    break;
                if (speedMultiplier < 1 || speedMultiplier > 100)
                    break;
                if (speedMultiplier == 100)
                    continue;
                data.factories[planetId].factorySystem.assemblerPool[assemblerId].speedMultiplier = speedMultiplier;
            }
        }

        public void Export(BinaryWriter w)
        {
            w.Write(saveVersion);
            var data = GameMain.data;

            for (int i = 0; i < data.factoryCount; i++)
            {
                PlanetFactory factory = data.factories[i];

                if (factory == null)
                {
                    continue;
                }
                for (int j = 1; j < factory.factorySystem.assemblerCursor; j++)
                {
                    var assembler = factory.factorySystem.assemblerPool[j];
                    if (assembler.id != j)
                        continue;
                    if (assembler.speedMultiplier >= 100 || assembler.speedMultiplier < 1)
                        continue;

                    w.Write(i);
                    w.Write(j);
                    w.Write(assembler.speedMultiplier);
                }
            }
            // end of data
            w.Write(-1);
            w.Write(-1);
            w.Write(-1);
        }

        public void IntoOtherSave()
        {
            return;
        }
    }
}
