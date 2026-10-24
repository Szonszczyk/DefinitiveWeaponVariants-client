using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using DefinitiveWeaponVariants.Client.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal sealed class ItemColorEffect : MonoBehaviour
    {
        internal static ItemColorEffect Instance { get; private set; }
        private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>();
        private readonly Dictionary<string, TintMaterial> _materials = new Dictionary<string, TintMaterial>(StringComparer.Ordinal);
        private readonly Dictionary<Mesh, RingMeshData> _geometry = new Dictionary<Mesh, RingMeshData>();
        private readonly HashSet<ItemColorModel> _models = new HashSet<ItemColorModel>();
        private readonly HashSet<string> _failed = new HashSet<string>(StringComparer.Ordinal);
        private IReadOnlyDictionary<string, ItemPalette> _colors = new Dictionary<string, ItemPalette>(StringComparer.Ordinal);
        private ItemColorSettings _settings;
        private ItemColorsLoader _loader;
        private bool _dirty;
        internal Task Ready => _ready.Task;
        internal bool IsEnabled => _settings.Enabled.Value && _settings.Opacity.Value > 0f;

        internal void Initialize(ItemColorSettings settings)
        {
            Instance = this;
            _settings = settings;
            settings.Enabled.SettingChanged += SettingsChanged;
            settings.Opacity.SettingChanged += SettingsChanged;
            Camera.onPreCull += PrepareCamera;
        }

        internal IEnumerator LoadColors(Func<string, Task<string>> getJson)
        {
            if (_loader != null) yield break;
            _loader = new ItemColorsLoader(getJson);
            var load = _loader.LoadOnceAsync();
            while (!load.IsCompleted) yield return null;
            if (load.IsCanceled || load.IsFaulted)
                Plugin.LogSource.LogWarning($"Item colors unavailable; item tints disabled for this session. {load.Exception?.GetBaseException().Message ?? "Request cancelled."}");
            else
            {
                var payload = load.GetAwaiter().GetResult();
                _colors = payload.Colors;
                Plugin.LogSource.LogInfo($"Item colors: loaded {_colors.Count} item template palettes from DWV server {payload.ServerModVersion}.");
            }
            // Runs on Unity's thread. Icons waiting for the catalog cannot race an unstyled render.
            _ready.TrySetResult(true);
            Refresh();
        }

        internal bool Contains(string templateId) => templateId != null && _colors.ContainsKey(templateId);

        internal bool Attach(GameObject root, string templateId)
        {
            if (!root || templateId == null) return false;
            // Track a preview opened during bootstrap; it receives its tint when colors arrive.
            if (_ready.Task.IsCompleted && !Contains(templateId)) return false;
            var model = root.GetComponent<ItemColorModel>() ?? root.AddComponent<ItemColorModel>();
            model.Initialize(this, templateId);
            return model.HasSurfaces && IsEnabled;
        }

        internal void Register(ItemColorModel model) => _models.Add(model);
        internal void Forget(ItemColorModel model) => _models.Remove(model);
        internal RingMeshData Geometry(Mesh mesh)
        {
            if (!_geometry.TryGetValue(mesh, out var data)) _geometry.Add(mesh, data = RingMeshData.Read(mesh));
            return data;
        }

        internal Material MaterialFor(string templateId)
        {
            if (!Contains(templateId) || !IsEnabled || _failed.Contains(templateId)) return null;
            if (!_materials.TryGetValue(templateId, out var tint))
            {
                tint = new TintMaterial(templateId, _colors[templateId], _settings.Opacity.Value);
                _materials.Add(templateId, tint);
            }
            return tint.Material;
        }

        internal void Failed(string templateId, Exception error)
        {
            if (_failed.Add(templateId)) Plugin.LogSource.LogWarning($"Item colors: could not tint '{templateId}': {error.Message}");
        }

        private void SettingsChanged(object sender, EventArgs args) { _dirty = true; }
        private void PrepareCamera(Camera camera)
        {
            if (!camera || !IsEnabled || _models.Count == 0) return;
            foreach (var model in _models) if (model) model.PrepareCamera(camera);
        }
        private void LateUpdate() { if (_dirty) { _dirty = false; Refresh(); } }
        private void Refresh()
        {
            foreach (var entry in _materials) entry.Value.Update(_colors[entry.Key], _settings.Opacity.Value);
            foreach (var model in new List<ItemColorModel>(_models)) if (model) model.Refresh();
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= PrepareCamera;
            _ready.TrySetResult(false);
            if (_settings != null)
            {
                _settings.Enabled.SettingChanged -= SettingsChanged;
                _settings.Opacity.SettingChanged -= SettingsChanged;
            }
            foreach (var model in new List<ItemColorModel>(_models)) if (model) model.Release();
            foreach (var tint in _materials.Values) tint.Dispose();
            _materials.Clear();
            _geometry.Clear();
            if (Instance == this) Instance = null;
        }

        private sealed class TintMaterial : IDisposable
        {
            internal readonly Material Material;
            private readonly Texture2D _texture;
            internal TintMaterial(string id, ItemPalette palette, float opacity)
            {
                _texture = new Texture2D(128, 1, TextureFormat.RGBA32, false)
                {
                    name = "DWV_ItemGradient_" + id, wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
                };
                try { Material = SurfaceOverlayMaterial.Create("DWV_ItemTint_" + id, _texture); Update(palette, opacity); }
                catch { Dispose(); throw; }
            }
            internal void Update(ItemPalette palette, float opacity)
            {
                var pixels = new Color[128];
                for (int i = 0; i < pixels.Length; i++)
                {
                    palette.Sample(i / 127f, opacity, out float r, out float g, out float b, out float a);
                    pixels[i] = new Color(r, g, b, a);
                }
                _texture.SetPixels(pixels);
                _texture.Apply(false, false);
            }
            public void Dispose() { if (Material) Object.Destroy(Material); if (_texture) Object.Destroy(_texture); }
        }
    }
}
