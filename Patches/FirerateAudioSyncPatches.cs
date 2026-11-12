using System;
using System.Reflection;
using DefinitiveWeaponVariants.Client.Audio;
using EFT;
using SPT.Reflection.Patching;

namespace DefinitiveWeaponVariants.Client.Patches
{
    internal sealed class FirerateAudioSyncInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(WeaponSoundPlayer).GetMethod(
            nameof(WeaponSoundPlayer.Init), new[] { typeof(BifacialTransform) })
            ?? throw new MissingMethodException("WeaponSoundPlayer.Init(BifacialTransform)");

        // Restore any previous pooled binding before vanilla recomputes its timing.
        [PatchPrefix]
        private static void Prefix(WeaponSoundPlayer __instance) => FirerateAudioSync.Instance?.Untrack(__instance);
        [PatchPostfix]
        private static void Postfix(WeaponSoundPlayer __instance) => FirerateAudioSync.Instance?.Track(__instance);
    }

    internal sealed class FirerateAudioSyncShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(WeaponSoundPlayer).GetMethod(nameof(WeaponSoundPlayer.FireBullet));
        [PatchPrefix]
        private static void Prefix(WeaponSoundPlayer __instance, bool multiShot) => FirerateAudioSync.Instance?.BeforeShot(__instance, multiShot);
    }

    internal sealed class FirerateAudioSyncDisablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(WeaponSoundPlayer).GetMethod(nameof(WeaponSoundPlayer.OnDisable), Type.EmptyTypes);
        // Vanilla stops the loop using its current beat before we restore the banks.
        [PatchPostfix]
        private static void Postfix(WeaponSoundPlayer __instance) => FirerateAudioSync.Instance?.Untrack(__instance);
    }

    internal sealed class FirerateAudioSyncDestroyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(WeaponSoundPlayer).GetMethod(nameof(WeaponSoundPlayer.OnDestroy), Type.EmptyTypes);
        [PatchPostfix]
        private static void Postfix(WeaponSoundPlayer __instance) => FirerateAudioSync.Instance?.Untrack(__instance);
    }
}
