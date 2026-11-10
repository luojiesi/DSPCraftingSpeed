using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Mono.Cecil;

namespace CraftingSpeed
{
    /// <summary>
    /// BepInEx preloader patcher. Adds one int field to the game's AssemblerComponent struct so
    /// every assembler can carry its own speed limit, and the value moves with the component
    /// wherever the game copies it.
    ///
    /// Stored value: 0 = no limit (100%), 1..99 = run at that percentage.
    /// </summary>
    public static class Preloader
    {
        public const string FieldName = "speedMultiplier";

        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        public static void Patch(AssemblyDefinition assembly)
        {
            var log = Logger.CreateLogSource("CraftingSpeed Preloader");
            ModuleDefinition module = assembly.MainModule;
            TypeDefinition assembler = module.Types.FirstOrDefault(t => t.FullName == "AssemblerComponent");
            if (assembler == null)
            {
                log.LogError("AssemblerComponent not found; crafting speed limits will be disabled.");
                return;
            }
            if (assembler.Fields.Any(f => f.Name == FieldName))
            {
                log.LogWarning($"AssemblerComponent.{FieldName} already exists; leaving it as is.");
                return;
            }
            assembler.Fields.Add(new FieldDefinition(FieldName, FieldAttributes.Public, module.TypeSystem.Int32));
            log.LogInfo($"Added AssemblerComponent.{FieldName}.");
        }
    }
}
