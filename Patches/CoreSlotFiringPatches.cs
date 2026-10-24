using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;

namespace DefinitiveWeaponVariants.Client.Patches
{
    internal static class CoreSlotFiring
    {
        internal static bool HasMissingRequiredCore(Weapon weapon)
        {
            if (weapon?.Slots == null)
            {
                return false;
            }

            foreach (var slot in weapon.Slots)
            {
                if (slot != null && slot.Required && slot.ContainedItem == null &&
                    string.Equals(slot.ID, "mod_core", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class CoreSlotSetTriggerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.FirearmController).GetMethod(nameof(Player.FirearmController.SetTriggerPressed),
                new[] { typeof(bool) })
                ?? throw new MissingMethodException(typeof(Player.FirearmController).FullName, nameof(Player.FirearmController.SetTriggerPressed));
        }

        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance, ref bool __0)
        {
            if (__0 && CoreSlotFiring.HasMissingRequiredCore(__instance.Item))
            {
                // Forward a release to the normal operation instead of just skipping input.
                __0 = false;
            }
        }
    }

    internal sealed class CoreSlotCanPressTriggerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.FirearmController).GetMethod(nameof(Player.FirearmController.CanPressTrigger), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Player.FirearmController).FullName, nameof(Player.FirearmController.CanPressTrigger));
        }

        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance, ref bool __result)
        {
            if (__result && CoreSlotFiring.HasMissingRequiredCore(__instance.Item))
            {
                __result = false;
            }
        }
    }
}
