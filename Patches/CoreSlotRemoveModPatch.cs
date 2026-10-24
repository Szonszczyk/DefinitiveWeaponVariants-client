using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;

namespace DefinitiveWeaponVariants.Client.Patches
{
    internal sealed class CoreSlotRemoveModPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Firearms).GetMethod(nameof(Firearms.RemoveMod), new[] { typeof(Slot) })
                ?? throw new MissingMethodException(typeof(Firearms).FullName, nameof(Firearms.RemoveMod));
        }

        [PatchPrefix]
        private static bool Prefix(Slot __0)
        {
            // RemoveMod only cleans up the attachment model. The hidden core has no
            // SlotView/bone, so let the calling operation continue to its success
            // callback, inventory completion and WeaponModified without visual cleanup.
            return __0 == null || !string.Equals(__0.ID, "mod_core", StringComparison.Ordinal);
        }
    }
}
