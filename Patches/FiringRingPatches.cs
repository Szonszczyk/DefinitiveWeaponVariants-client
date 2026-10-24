using System;
using System.Reflection;
using DefinitiveWeaponVariants.Client.Effects;
using EFT;
using EFT.InventoryLogic;
using EFT.AssetsManager;
using SPT.Reflection.Patching;

namespace DefinitiveWeaponVariants.Client.Patches
{
    internal sealed class FiringRingShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(Player.FirearmController).GetMethod(
            nameof(Player.FirearmController.Shot), new[] { typeof(Weapon), typeof(Ammo), typeof(int), typeof(bool) })
            ?? throw new MissingMethodException("Player.FirearmController.Shot");

        // Per discharge, not per pellet. Dry fire, misfires and launcher shots
        // take separate paths and deliberately do not trigger this prototype.
        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance) => FiringRingEffect.Instance?.OnShot(__instance);
    }

    internal sealed class FiringRingModifiedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(Player.FirearmController).GetMethod(
            nameof(Player.FirearmController.WeaponModified), Type.EmptyTypes);
        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance) => FiringRingEffect.Instance?.WeaponModified(__instance);
    }

    internal sealed class FiringRingDestroyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(Player.FirearmController).GetMethod(
            nameof(Player.FirearmController.Destroy), Type.EmptyTypes);
        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance) => FiringRingEffect.Instance?.Release(__instance);
    }

    internal sealed class FiringRingPoolPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(WeaponPrefab).GetMethod(nameof(WeaponPrefab.ReturnToPool), Type.EmptyTypes);
        [PatchPrefix]
        private static void Prefix(WeaponPrefab __instance) => FiringRingEffect.Instance?.Release(__instance.gameObject);
    }

    internal sealed class FiringRingAttachmentPoolPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(WeaponModPoolObject).GetMethod(nameof(WeaponModPoolObject.ReturnToPool), Type.EmptyTypes);
        [PatchPrefix]
        private static void Prefix(WeaponModPoolObject __instance) => FiringRingEffect.Instance?.ReleaseAttachment(__instance.gameObject);
    }
}
