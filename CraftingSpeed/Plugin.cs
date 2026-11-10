using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using crecheng.DSPModSave;
using HarmonyLib;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CraftingSpeed.InGameTests")]

namespace CraftingSpeed
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(DSPModSavePlugin.MODGUID)]
    [BepInDependency(BlueprintTweaks.BlueprintTweaksPlugin.MODGUID)]
    public class Plugin : BaseUnityPlugin, IModCanSave
    {
        // Keep the GUID unchanged: DSPModSave stores this mod's save data under it.
        public const string Guid = "com.github.jiesiluo.assemblerspeed";
        public const string Name = "Crafting Speed";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> RowNudgeX;
        internal static ConfigEntry<float> RowNudgeY;

        private void Awake()
        {
            Log = Logger;
            RowNudgeX = Config.Bind("UI", "RowNudgeX", 0f, "Move the speed limit row in the assembler window right (positive) or left, in pixels.");
            RowNudgeY = Config.Bind("UI", "RowNudgeY", 0f, "Move the speed limit row up (positive) or down, in pixels.");
            SpeedLimit.Init();
            if (!SpeedLimit.Available) return;

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(AssemblerUpdatePatch));
            harmony.PatchAll(typeof(NewAssemblerPatch));
            harmony.PatchAll(typeof(PrebuildCompletedPatch));
            harmony.PatchAll(typeof(PrebuildRemovedPatch));
            harmony.PatchAll(typeof(AssemblerWindowUI));
            harmony.PatchAll(typeof(CopySettingsPatches.CopyPatch));
            harmony.PatchAll(typeof(CopySettingsPatches.PastePatch));
            harmony.PatchAll(typeof(BlueprintSupport.CopyPatch));
            harmony.PatchAll(typeof(BlueprintSupport.PastePatch));
            BlueprintSupport.Register();
        }

        public void Export(BinaryWriter w) => SaveData.Export(w);

        public void Import(BinaryReader r) => SaveData.Import(r);

        public void IntoOtherSave() => SaveData.IntoOtherSave();
    }
}
