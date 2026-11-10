using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CraftingSpeed
{
    /// <summary>
    /// Slows assemblers down by scaling the two places in AssemblerComponent.InternalUpdate that add
    /// crafting progress each tick:
    ///     time      += power * speedOverride
    ///     extraTime += power * extraSpeed      (proliferator "extra products" progress)
    /// Every read of those two fields inside InternalUpdate is replaced by a call that returns the
    /// value scaled by the assembler's speed limit. Nothing is written back, so proliferator speed
    /// changes and the power check (power &lt; 0.1) keep working exactly as in the base game.
    /// </summary>
    [HarmonyPatch(typeof(AssemblerComponent), nameof(AssemblerComponent.InternalUpdate))]
    internal static class AssemblerUpdatePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo speedOverride = AccessTools.Field(typeof(AssemblerComponent), nameof(AssemblerComponent.speedOverride));
            FieldInfo extraSpeed = AccessTools.Field(typeof(AssemblerComponent), nameof(AssemblerComponent.extraSpeed));
            MethodInfo scaledSpeed = AccessTools.Method(typeof(SpeedLimit), nameof(SpeedLimit.ScaledSpeedOverride));
            MethodInfo scaledExtra = AccessTools.Method(typeof(SpeedLimit), nameof(SpeedLimit.ScaledExtraSpeed));

            int replacedSpeed = 0, replacedExtra = 0;
            foreach (CodeInstruction ci in instructions)
            {
                // `this` is already on the stack as `ref AssemblerComponent`, which is exactly the
                // argument the replacement methods take. Labels and blocks stay on the instruction.
                if (ci.opcode == OpCodes.Ldfld && ci.operand is FieldInfo f)
                {
                    if (f == speedOverride) { ci.opcode = OpCodes.Call; ci.operand = scaledSpeed; replacedSpeed++; }
                    else if (f == extraSpeed) { ci.opcode = OpCodes.Call; ci.operand = scaledExtra; replacedExtra++; }
                }
                yield return ci;
            }

            if (replacedSpeed == 0)
                Plugin.Log.LogError("Could not find the crafting progress code in AssemblerComponent.InternalUpdate; " +
                                    "the game has probably changed and speed limits will have no effect.");
            else
                Plugin.Log.LogInfo($"Patched AssemblerComponent.InternalUpdate (speed reads: {replacedSpeed}, extra-product reads: {replacedExtra}).");
        }
    }

    /// <summary>
    /// The game reuses assembler slots and its SetEmpty() does not know about our field, so a new
    /// assembler could inherit the old one's limit. Clear it whenever a slot is handed out.
    /// </summary>
    [HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.NewAssemblerComponent))]
    internal static class NewAssemblerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FactorySystem __instance, int __result)
        {
            if (__result > 0)
                SpeedLimit.Set(ref __instance.assemblerPool[__result], SpeedLimit.Max);
        }
    }
}
