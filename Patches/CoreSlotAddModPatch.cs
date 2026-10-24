using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace DefinitiveWeaponVariants.Client.Patches
{
    internal sealed class CoreSlotAddModPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.FirearmController.AddModOperation).GetMethod(
                nameof(Player.FirearmController.AddModOperation.OnModChanged), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Player.FirearmController.AddModOperation).FullName, "OnModChanged");
        }

        [PatchPrefix]
        [HarmonyBefore("FixAddModFirearmOperationPatch")]
        private static bool Prefix(Player.FirearmController.AddModOperation __instance, out Callback __state)
        {
            __state = null;
            if (__instance._slot == null ||
                !string.Equals(__instance._slot.ID, "mod_core", StringComparison.Ordinal))
            {
                return true;
            }

            // Consume only the hidden-core event before UI-Fixes can create a
            // model for it. Ordinary attachments continue through its handler.
            if (__instance._onModChanged)
            {
                return false;
            }

            __instance._onModChanged = true;
            var controller = __instance.Controller;
            var player = __instance.Player;
            var weapon = __instance.Weapon;
            var animator = __instance.FirearmsAnimator;
            var callback = __instance._callback;
            animator.SetupMod(false);
            animator.Fold(weapon.Folded);
            __instance.State = Player.EOperationState.Finished;
            player.BodyAnimatorCommon.SetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH,
                (float)weapon.CalculateCellSize().X);
            player.UpdateFirstPersonGrip(GripPose.EGripType.Common, controller.HandsHierarchy);
            controller.WeaponModified();
            controller.InitiateOperation<Player.FirearmController.Idling>().Start(null);
            // HarmonyX still calls later prefixes. Leave the event guard intact
            // until UI-Fixes has seen it, before success can reuse this operation.
            __state = callback;
            return false;
        }

        [PatchPostfix]
        private static void Postfix(Callback __state)
        {
            // The controller is idle and every prefix has completed. Success may
            // synchronously start another action; do not touch operation fields.
            if (__state != null) __state.Succeed();
        }
    }
}
