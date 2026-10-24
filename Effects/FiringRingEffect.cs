using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal sealed class FiringRingEffect : MonoBehaviour
    {
        internal static FiringRingEffect Instance { get; private set; }
        private readonly RingAnimation _motion = new RingAnimation();
        private readonly List<RingSurface> _surfaces = new List<RingSurface>();
        private readonly Dictionary<Mesh, RingMeshData> _geometry = new Dictionary<Mesh, RingMeshData>();
        private Dictionary<string, RingColor> _colors = new Dictionary<string, RingColor>(StringComparer.Ordinal);
        private RingSettings _settings;
        private float _nextPrepareCheck;
        private Player.FirearmController _controller;
        private GameObject _root, _failedRoot;
        private bool _dirty, _includedAttachments;
        private Material _material;
        private Texture2D _gradient;
        private Color _textureColor = new Color(-1f, -1f, -1f, -1f);
        private string _lastLoggedTemplate;

        internal void Initialize(RingSettings settings)
        {
            Instance = this;
            _settings = settings;
        }

        // Startup coroutine calls this on the Unity thread only after full validation.
        internal void ApplyColors(IReadOnlyDictionary<string, RingColor> colors)
        {
            var replacement = new Dictionary<string, RingColor>(StringComparer.Ordinal);
            foreach (var entry in colors) replacement.Add(entry.Key, entry.Value);
            ClearWeapon();
            _failedRoot = null;
            _colors = replacement;
        }

        private void Update()
        {
            if (_settings == null) return;
            // Prepare once after equipping a mapped weapon, rather than normally
            // doing the synchronous GPU readback on its first gunshot.
            if (_settings.Enabled.Value && _colors.Count != 0 && Time.unscaledTime >= _nextPrepareCheck)
            {
                _nextPrepareCheck = Time.unscaledTime + 0.25f;
                var controller = Singleton<GameWorld>.Instantiated
                    ? Singleton<GameWorld>.Instance.MainPlayer?.HandsController as Player.FirearmController : null;
                try { PrepareWeapon(controller); }
                catch (Exception e) { Fail(e, controller?.ControllerGameObject); }
            }
        }

        internal void OnShot(Player.FirearmController controller)
        {
            try
            {
                if (_settings == null || !_settings.Enabled.Value || !IsEligible(controller)) return;
                string template = controller.Item.StringTemplateId;
                if (_settings.DebugLogging.Value && template != _lastLoggedTemplate)
                {
                    _lastLoggedTemplate = template;
                    Plugin.LogSource.LogInfo($"Firing ring weapon template: {template} (instance {controller.Item.Id}).");
                }
                if (!_colors.TryGetValue(template, out _)) return;
                if (PrepareWeapon(controller)) _motion.Fire(_settings.AnimationOptions);
            }
            catch (Exception e) { Fail(e, controller?.ControllerGameObject); }
        }

        private bool PrepareWeapon(Player.FirearmController controller)
        {
            if (!IsEligible(controller) || !_colors.ContainsKey(controller.Item.StringTemplateId) ||
                controller.CurrentFireport?.Original == null) return false;
            var root = controller.ControllerGameObject;
            if (!root || root == _failedRoot) return false;
            if (_controller != controller || _root != root || _dirty || _includedAttachments != _settings.IncludeAttachments.Value)
            {
                if (_controller != controller || _root != root) ClearWeapon();
                else ClearSurfaces();
                _controller = controller;
                _root = root;
                _includedAttachments = _settings.IncludeAttachments.Value;
                BuildSurfaces();
            }
            return _surfaces.Count != 0;
        }

        internal void WeaponModified(Player.FirearmController controller)
        {
            if (_controller == controller)
            {
                _dirty = true;
                HideSurfaces();
            }
        }

        internal void Release(Player.FirearmController controller)
        {
            if (_controller == controller) ClearWeapon();
        }

        internal void Release(GameObject root)
        {
            if (_root == root) ClearWeapon();
            if (_failedRoot == root) _failedRoot = null;
        }

        internal void ReleaseAttachment(GameObject attachment)
        {
            if (!attachment) return;
            bool removed = false;
            for (int i = _surfaces.Count - 1; i >= 0; i--)
            {
                var source = _surfaces[i].Source;
                if (source && source.transform.IsChildOf(attachment.transform))
                {
                    _surfaces[i].Dispose();
                    _surfaces.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed)
            {
                _dirty = true;
                HideSurfaces();
            }
        }

        private static bool IsEligible(Player.FirearmController controller)
        {
            var player = controller?._player;
            return player != null && !player.Destroyed && !controller.Destroyed && player.IsYourPlayer && player.FirstPersonPointOfView &&
                ReferenceEquals(player.HandsController, controller) && controller.Item != null;
        }

        private void LateUpdate()
        {
            if (_controller == null) return;
            try
            {
                if (!_root || !_root.activeInHierarchy || !IsEligible(_controller)) { ClearWeapon(); return; }
                if (!_settings.Enabled.Value || !_colors.TryGetValue(_controller.Item.StringTemplateId, out RingColor color))
                {
                    StopRing();
                    return;
                }
                var options = _settings.AnimationOptions;
                // Hidden rings still need the inactivity clock. A reload/attachment
                // rebuild hides surfaces temporarily without resetting that clock.
                _motion.Advance(Time.deltaTime, options);
                if (!_motion.Active || _dirty) { HideSurfaces(); return; }
                UpdateTexture(color);
                // Preparation can run before the initial equip animation settles.
                // Use the actual muzzle frame now, not an early snapshot of the
                // controller wrapper's axis/origin/bounds. Reload is never needed
                // to repair cached placement, and recoil/equip movement stays aligned.
                var muzzle = _controller.CurrentFireport?.Original;
                if (!muzzle) { HideSurfaces(); return; }
                Vector3 axis = -muzzle.InverseTransformDirection(_controller.WeaponDirection).normalized;
                if (axis.sqrMagnitude < 0.5f) { HideSurfaces(); return; }
                float start = float.PositiveInfinity, end = float.NegativeInfinity;
                foreach (var surface in _surfaces)
                {
                    if (surface.Project(_root.transform, muzzle, axis, out float minimum, out float maximum))
                    {
                        start = Mathf.Min(start, minimum);
                        end = Mathf.Max(end, maximum);
                    }
                }
                if (float.IsInfinity(start) || float.IsInfinity(end) || float.IsNaN(start) || float.IsNaN(end))
                {
                    HideSurfaces();
                    return;
                }
                start = Mathf.Min(0f, start);
                end = Mathf.Max(start + 0.05f, end);
                float width = Mathf.Max(0.001f, (end - start) * options.BandWidth);
                float center = start + (end - start) * _motion.Center(options);
                foreach (var surface in _surfaces) surface.Draw(center, width);
            }
            catch (Exception e) { Fail(e, _root); }
        }

        private void EnsureMaterial()
        {
            if (_material) return;
            // These unlit shaders have ordinary depth testing and no soft-particle
            // intersection fade (which would erase an overlay sitting on a surface).
            _gradient = new Texture2D(128, 1, TextureFormat.RGBA32, false)
            {
                name = "DWV_FiringRing_Gradient", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            _material = SurfaceOverlayMaterial.Create("DWV_FiringRing", _gradient);
            if (_settings.DebugLogging.Value) Plugin.LogSource.LogInfo($"Firing ring overlay shader: {_material.shader.name}");
        }

        private void UpdateTexture(RingColor color)
        {
            var tint = new Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f * _settings.Opacity.Value);
            if (tint == _textureColor) return;
            var pixels = new Color[128];
            for (int i = 0; i < pixels.Length; i++)
            {
                float distance = Mathf.Abs(i / 127f - 0.5f) * 2f;
                pixels[i] = new Color(tint.r, tint.g, tint.b, tint.a * (1f - Mathf.SmoothStep(0f, 1f, distance)));
            }
            _gradient.SetPixels(pixels);
            _gradient.Apply(false, false);
            _textureColor = tint;
        }

        private void BuildSurfaces()
        {
            EnsureMaterial();
            int skipped = 0;
            // Snapshot before creating children, so our own meshes cannot enter the scan.
            var renderers = _root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (!_includedAttachments && renderer.GetComponentInParent<WeaponModPoolObject>()) continue;
                Mesh baked = null;
                try
                {
                    Mesh mesh;
                    RingMeshData data;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        if (!skinned.sharedMesh) continue;
                        baked = new Mesh { name = "DWV_FiringRing_Baked" };
                        skinned.BakeMesh(baked);
                        mesh = baked;
                        data = RingMeshData.Read(mesh);
                    }
                    else
                    {
                        mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (!mesh) continue;
                        if (!_geometry.TryGetValue(mesh, out data))
                        {
                            data = RingMeshData.Read(mesh);
                            _geometry.Add(mesh, data);
                        }
                    }
                    var triangles = new List<int>();
                    Material[] materials = renderer.sharedMaterials;
                    for (int s = 0; s < data.Triangles.Length && s < materials.Length; s++)
                        if (SurfaceOverlayMaterial.IsOpaqueBody(materials[s])) triangles.AddRange(data.Triangles[s]);
                    if (triangles.Count == 0) { if (baked) Object.Destroy(baked); continue; }
                    _surfaces.Add(new RingSurface(renderer, data, triangles, _material, baked));
                    baked = null; // Surface owns this mesh now.
                }
                catch (Exception e)
                {
                    skipped++;
                    if (baked) Object.Destroy(baked);
                    if (_settings.DebugLogging.Value) Plugin.LogSource.LogWarning($"Ring skipped {renderer.name}: {e.Message}");
                }
            }
            _dirty = false;
            if (_surfaces.Count == 0)
                throw new InvalidOperationException($"No supported opaque weapon surfaces found ({skipped} mesh errors). Enable Debug logging for details.");
            if (_settings.DebugLogging.Value)
                Plugin.LogSource.LogInfo($"Ring ready: {_controller.Item.StringTemplateId}, {_surfaces.Count} surfaces, {skipped} skipped.");
        }

        private void StopRing()
        {
            _motion.Reset();
            HideSurfaces();
        }

        private void HideSurfaces()
        {
            foreach (var surface in _surfaces) surface.Hide();
        }

        private void ClearWeapon()
        {
            _motion.Reset();
            ClearSurfaces();
            _controller = null;
            _root = null;
        }

        private void ClearSurfaces()
        {
            foreach (var surface in _surfaces) surface.Dispose();
            _surfaces.Clear();
            _geometry.Clear();
            _dirty = false;
        }

        private void Fail(Exception exception, GameObject root)
        {
            Plugin.LogSource.LogWarning($"Variant firing ring stopped: {exception.Message}");
            ClearWeapon();
            _failedRoot = root;
        }

        private void OnDestroy()
        {
            ClearWeapon();
            if (_material) Object.Destroy(_material);
            if (_gradient) Object.Destroy(_gradient);
            if (Instance == this) Instance = null;
        }
    }
}
