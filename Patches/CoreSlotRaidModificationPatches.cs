using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace DefinitiveWeaponVariants.Client.Patches
{
    internal static class CoreSlotRaidModification
    {
        internal static bool IsWeaponCoreSlot(Slot slot)
        {
            return slot != null && slot.ParentItem is Weapon &&
                string.Equals(slot.ID, "mod_core", StringComparison.Ordinal);
        }

        // This predicate is used only by the two raid movement checks. Never change
        // Slot.Required: completeness and missing-vital-part checks still need it.
        internal static bool IsLockedRequiredSlot(Slot slot)
        {
            return slot.Required && !IsWeaponCoreSlot(slot);
        }

        internal static IEnumerable<CodeInstruction> RewriteRequiredChecks(
            IEnumerable<CodeInstruction> instructions, int expectedReads)
        {
            var code = instructions.ToList();
            var requiredField = typeof(Slot).GetField(nameof(Slot.Required));
            var reads = code.Where(instruction => instruction.opcode == OpCodes.Ldfld &&
                Equals(instruction.operand, requiredField)).ToList();
            if (reads.Count != expectedReads)
            {
                throw new InvalidOperationException($"Expected {expectedReads} required-slot checks, found {reads.Count}; core raid modification patch was not applied.");
            }

            var predicate = typeof(CoreSlotRaidModification).GetMethod(nameof(IsLockedRequiredSlot),
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var instruction in reads)
            {
                // The stack signature is unchanged; retain branch labels and exception blocks.
                instruction.opcode = OpCodes.Call;
                instruction.operand = predicate;
            }

            return code;
        }
    }

    internal sealed class CoreSlotRaidMovePathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(ItemManipulator).GetMethod(nameof(ItemManipulator.MovePathCheck),
                new[] { typeof(Item), typeof(ItemAddress), typeof(ItemController) })
                ?? throw new MissingMethodException(typeof(ItemManipulator).FullName, nameof(ItemManipulator.MovePathCheck));
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Both the source and destination may be required slots.
            return CoreSlotRaidModification.RewriteRequiredChecks(instructions, 2);
        }
    }

    internal sealed class CoreSlotRaidModMovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Mod).GetMethod(nameof(Mod.CanBeMoved), new[] { typeof(IContainer) })
                ?? throw new MissingMethodException(typeof(Mod).FullName, nameof(Mod.CanBeMoved));
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return CoreSlotRaidModification.RewriteRequiredChecks(instructions, 1);
        }
    }
}
