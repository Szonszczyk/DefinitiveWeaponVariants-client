using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace DefinitiveWeaponVariants.Client.Patches
{
    internal static class CoreSlotInteraction
    {
        internal static bool IsLockedVitalPart(IEnumerable<Slot> vitalParts, Slot slot)
        {
            return !CoreSlotRaidModification.IsWeaponCoreSlot(slot) && vitalParts.Contains(slot);
        }

        internal static IEnumerable<CodeInstruction> RewriteVitalPartLocks(
            IEnumerable<CodeInstruction> instructions, int expectedReads)
        {
            var code = instructions.ToList();
            var contains = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == nameof(Enumerable.Contains) &&
                    method.GetParameters().Length == 2).MakeGenericMethod(typeof(Slot));
            var reads = code.Where(instruction => instruction.opcode == OpCodes.Call &&
                Equals(instruction.operand, contains)).ToList();
            if (reads.Count != expectedReads)
            {
                throw new InvalidOperationException($"Expected {expectedReads} vital-part interaction checks, found {reads.Count}; core interaction patch was not applied.");
            }

            var predicate = typeof(CoreSlotInteraction).GetMethod(nameof(IsLockedVitalPart),
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var instruction in reads)
            {
                instruction.operand = predicate;
            }

            return code;
        }
    }

    internal sealed class CoreSlotInspectLockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(ItemSpecificationPanel).GetMethod(nameof(ItemSpecificationPanel.GetModLockedState),
                new[] { typeof(Slot) })
                ?? throw new MissingMethodException(typeof(ItemSpecificationPanel).FullName, nameof(ItemSpecificationPanel.GetModLockedState));
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // The panel locks vital parts in raid AND while equipped outside a raid.
            return CoreSlotInteraction.RewriteVitalPartLocks(instructions, 2);
        }
    }

    internal sealed class CoreSlotApplyLockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(CompoundItem).GetMethod(nameof(CompoundItem.Apply),
                new[] { typeof(ItemController), typeof(Item), typeof(int), typeof(bool) })
                ?? throw new MissingMethodException(typeof(CompoundItem).FullName, nameof(CompoundItem.Apply));
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Applying/quick-installing a core must not require a multitool.
            return CoreSlotInteraction.RewriteVitalPartLocks(instructions, 1);
        }
    }
}
