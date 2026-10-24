using System;
using System.Collections;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT.Communications;
using UnityEngine;

namespace DefinitiveWeaponVariants.Client.Compatibility
{
    // Read UI-Fixes' existing entry without binding or writing its configuration.
    internal sealed class UiFixesCompatibilityNotice : IDisposable
    {
        private readonly ConfigEntry<bool> _enabled;
        private readonly ManualLogSource _log;
        private ConfigEntryBase _raidSetting;
        private ConfigFile _uiFixesConfig;
        private volatile bool _needsCheck = true;
        private bool _disposed;

        public UiFixesCompatibilityNotice(ConfigFile config, ManualLogSource log)
        {
            _log = log;
            _enabled = config.Bind("Compatibility", "Show UI-Fixes compatibility warnings", true,
                "Warn once per game session if UI-Fixes prevents required mod_core changes on unequipped weapons in raid. " +
                "Use UI-Fixes / C. Gameplay / Modify Weapons In Raid: Always, or With Multitool with a qualifying tool. " +
                "Disabling this setting silences both the notification and its log warning.");
        }

        public IEnumerator Run()
        {
            // Optional dependency ordering ensures UI-Fixes has bound its entries.
            yield return null;
            if (_disposed || !Chainloader.PluginInfos.TryGetValue("com.tyfon.uifixes", out var plugin)) yield break;
            var definition = new ConfigDefinition("C. Gameplay", "Modify Weapons In Raid");
            _uiFixesConfig = plugin.Instance.Config;
            if (!_uiFixesConfig.ContainsKey(definition)) yield break;
            _raidSetting = _uiFixesConfig[definition];
            _uiFixesConfig.SettingChanged += SettingChanged;
            _enabled.SettingChanged += SettingChanged;

            var delay = new WaitForSecondsRealtime(1f);
            bool pending = false;
            while (!_disposed)
            {
                // Settings can be synchronized on another thread. Only this Unity
                // coroutine reads values and interacts with the notification UI.
                if (_needsCheck)
                {
                    _needsCheck = false;
                    pending = _enabled.Value && string.Equals(_raidSetting.GetSerializedValue(), "Never", StringComparison.Ordinal);
                }
                if (pending && Singleton<NotificationManager>.Instantiated)
                {
                    const string message = "DWV: UI-Fixes blocks required mod_core changes on unequipped weapons in raid. " +
                        "Set UI-Fixes > C. Gameplay > Modify Weapons In Raid to Always, or With Multitool and carry a qualifying tool. " +
                        "Equipped weapons still follow UI-Fixes restrictions. " +
                        "Silence this notice in DWV > Compatibility.";
                    _log.LogWarning(message);
                    NotificationManager.DisplayWarningNotification(message, ENotificationDurationType.Long);
                    Dispose(); // At most one notice per game session; no ongoing polling.
                    yield break;
                }
                yield return delay;
            }
        }

        private void SettingChanged(object sender, EventArgs args) => _needsCheck = true;

        public void Dispose()
        {
            _disposed = true;
            if (_raidSetting == null) return;
            _uiFixesConfig.SettingChanged -= SettingChanged;
            _enabled.SettingChanged -= SettingChanged;
        }
    }
}
