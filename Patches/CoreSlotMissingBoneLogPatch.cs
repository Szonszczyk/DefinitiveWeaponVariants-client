using System;
using System.Reflection;
using EFT;
using SPT.Reflection.Patching;

namespace DefinitiveWeaponVariants.Client.Patches
{
    // Keep the game's attachment and async routines intact: a missing core bone is expected.
    internal sealed class CoreSlotMissingBoneLogPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(AbstractLogger).GetMethod(nameof(AbstractLogger.LogError),
                new[] { typeof(string), typeof(object[]) })
                ?? throw new MissingMethodException(typeof(AbstractLogger).FullName, nameof(AbstractLogger.LogError));
        }

        [PatchPrefix]
        private static bool Prefix(AbstractLogger __instance, string __0, object[] __1)
        {
            // AttachMods supplies a complete message and an empty argument array.
            // Match only that diagnostic from the objectPool logger, including the slot boundary.
            return !(__instance is ObjectsFactory.PoolLogger &&
                __1 != null && __1.Length == 0 &&
                __0 != null &&
                __0.StartsWith("bone mod_core not found in GameObject ", StringComparison.Ordinal));
        }
    }
}
