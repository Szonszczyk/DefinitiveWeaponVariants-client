using BepInEx;
using BepInEx.Logging;
using System.Collections;
using DefinitiveWeaponVariants.Client.Effects;
using DefinitiveWeaponVariants.Client.Networking;
using DefinitiveWeaponVariants.Client.Patches;
using DefinitiveWeaponVariants.Client.Compatibility;
using SPT.Common.Http;

namespace DefinitiveWeaponVariants.Client
{
    [BepInPlugin("com.szonszczyk.definitiveweaponvariants.client", "DefinitiveWeaponVariants.Client", "1.0.13")]
    [BepInDependency("com.SPT.core")]
    [BepInDependency("com.tyfon.uifixes", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        private FiringRingEffect _firingRing;
        private VariantColorsLoader _variantColorsLoader;
        private ItemColorEffect _itemColors;
        private UiFixesCompatibilityNotice _uiFixesNotice;

        private void Awake()
        {
            LogSource = Logger;
            _uiFixesNotice = new UiFixesCompatibilityNotice(Config, Logger);
            _firingRing = gameObject.AddComponent<FiringRingEffect>();
            _firingRing.Initialize(new RingSettings(Config));
            _itemColors = gameObject.AddComponent<ItemColorEffect>();
            _itemColors.Initialize(new ItemColorSettings(Config));
            new CoreSlotMissingBoneLogPatch().Enable();
            new CoreSlotRemoveModPatch().Enable();
            new CoreSlotAddModPatch().Enable();
            new CoreSlotRaidMovePathPatch().Enable();
            new CoreSlotRaidModMovePatch().Enable();
            new CoreSlotInspectLockPatch().Enable();
            new CoreSlotApplyLockPatch().Enable();
            new CoreSlotSetTriggerPatch().Enable();
            new CoreSlotCanPressTriggerPatch().Enable();
            new FiringRingShotPatch().Enable();
            new FiringRingModifiedPatch().Enable();
            new FiringRingDestroyPatch().Enable();
            new FiringRingPoolPatch().Enable();
            new FiringRingAttachmentPoolPatch().Enable();
            new ItemColorPreviewPatch().Enable();
            new ItemColorIconPatch().Enable();
            new ItemColorIconShaderPatch().Enable();
            new ItemColorGameObjectPoolPatch().Enable();
            new ItemColorAssetPoolPatch().Enable();
            LogSource.LogInfo("DefinitiveWeaponVariants.Client 1.0.13 loaded (mod_core patches, firing/idle ring and static item colors).");
        }

        private IEnumerator Start()
        {
            StartCoroutine(_uiFixesNotice.Run());
            if (_variantColorsLoader != null) yield break;
            StartCoroutine(_itemColors.LoadColors(path => RequestHandler.GetJsonAsync(path)));
            _variantColorsLoader = new VariantColorsLoader(path => RequestHandler.GetJsonAsync(path));
            var load = _variantColorsLoader.LoadOnceAsync();
            // Yield instead of blocking startup. Unity resumes the coroutine on its
            // main thread, even though HTTP/parsing may complete on a worker thread.
            while (!load.IsCompleted) yield return null;
            if (load.IsFaulted || load.IsCanceled)
            {
                string reason = load.Exception?.GetBaseException().Message ?? "Request cancelled.";
                LogSource.LogWarning($"Firing ring: server variant colors unavailable; effects disabled for this game session. {reason}");
                yield break;
            }
            var payload = load.GetAwaiter().GetResult();
            if (!_firingRing) yield break;
            _firingRing.ApplyColors(payload.Colors);
            LogSource.LogInfo($"Firing ring: loaded {payload.Colors.Count} weapon template colors from DWV server {payload.ServerModVersion}.");
        }

        private void OnDestroy()
        {
            _uiFixesNotice?.Dispose();
        }
    }
}
