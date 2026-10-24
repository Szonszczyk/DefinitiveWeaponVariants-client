using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DefinitiveWeaponVariants.Client.Effects
{
    // Static overlay: shares the ring's geometry reader, transparent shader and color parser,
    // but uses an item-wide gradient without moving UVs, band falloff or animation.
    internal sealed class ItemColorModel : MonoBehaviour
    {
        private ItemColorEffect _owner;
        private string _templateId;
        private readonly List<Surface> _surfaces = new List<Surface>();
        private readonly List<(LODGroup Group, LOD[] Original)> _lods = new List<(LODGroup, LOD[])>();
        private bool _projectionValid;
        internal bool HasSurfaces => _surfaces.Count != 0;

        internal void Initialize(ItemColorEffect owner, string templateId)
        {
            if (_owner != owner || _templateId != templateId)
            {
                Release();
                _owner = owner;
                _templateId = templateId;
                owner.Register(this);
            }
            Refresh();
        }

        internal void Refresh()
        {
            if (!_owner) return;
            try
            {
                var material = _owner.MaterialFor(_templateId);
                if (material && !HasSurfaces) Build(material);
                Synchronize();
            }
            catch (Exception e) { ClearSurfaces(); _owner.Failed(_templateId, e); }
        }

        private void Build(Material material)
        {
            // Snapshot before adding our own renderers. Unmodified shared materials are never written.
            foreach (var source in GetComponentsInChildren<Renderer>(true))
            {
                if (!(source is MeshRenderer) && !(source is SkinnedMeshRenderer)) continue;
                Mesh baked = null;
                try
                {
                    RingMeshData data;
                    if (source is SkinnedMeshRenderer skinned)
                    {
                        if (!skinned.sharedMesh) continue;
                        baked = new Mesh();
                        skinned.BakeMesh(baked);
                        data = RingMeshData.Read(baked);
                    }
                    else
                    {
                        var mesh = source.GetComponent<MeshFilter>()?.sharedMesh;
                        if (!mesh) continue;
                        data = _owner.Geometry(mesh);
                    }
                    var triangles = new List<int>();
                    var materials = source.sharedMaterials;
                    for (int s = 0; s < data.Triangles.Length && s < materials.Length; s++)
                        if (SurfaceOverlayMaterial.IsOpaqueBody(materials[s])) triangles.AddRange(data.Triangles[s]);
                    if (triangles.Count != 0) _surfaces.Add(new Surface(source, data, triangles, material));
                }
                finally { if (baked) Object.Destroy(baked); }
            }
            if (!HasSurfaces) throw new InvalidOperationException("No supported opaque item surfaces were found.");

            // Put overlays into the same LOD entries as their source, including the first icon render.
            foreach (var group in GetComponentsInChildren<LODGroup>(true))
            {
                var original = group.GetLODs();
                var updated = (LOD[])original.Clone();
                bool changed = false;
                for (int i = 0; i < updated.Length; i++)
                {
                    var renderers = new List<Renderer>(original[i].renderers);
                    foreach (var surface in _surfaces)
                        if (renderers.Contains(surface.Source)) { renderers.Add(surface.Renderer); changed = true; }
                    updated[i].renderers = renderers.ToArray();
                }
                if (changed) { _lods.Add((group, original)); group.SetLODs(updated); }
            }
        }

        private void LateUpdate() { if (_owner) Synchronize(); }
        internal void PrepareCamera(Camera camera)
        {
            if (!_owner || !HasSurfaces || !gameObject.activeInHierarchy) return;
            bool included = false;
            foreach (var surface in _surfaces)
                if (surface.Renderer && surface.Renderer.enabled &&
                    (camera.cullingMask & (1 << surface.Renderer.gameObject.layer)) != 0) { included = true; break; }
            if (!included) return;
            try
            {
                // Called after the inspect/icon camera has positioned its model, including
                // synchronous Camera.Render(). Each view needs its own vertical projection.
                var viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
                bool changed = !_projectionValid;
                foreach (var surface in _surfaces)
                    changed |= surface.UpdateProjection(viewProjection);
                if (!changed) return;

                float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
                foreach (var surface in _surfaces) surface.Project(ref minimum, ref maximum);
                foreach (var surface in _surfaces) surface.SetGradient(minimum, maximum);
                _projectionValid = true;
            }
            catch (Exception e) { ClearSurfaces(); _owner.Failed(_templateId, e); }
        }

        private void Synchronize()
        {
            foreach (var surface in _surfaces)
            {
                if (!surface.Source || !surface.Renderer) continue;
                surface.Renderer.gameObject.layer = surface.Source.gameObject.layer;
                surface.Renderer.enabled = _owner.IsEnabled && surface.Source.enabled &&
                    !surface.Source.forceRenderingOff;
            }
        }

        internal void SuspendForShaderReplacement(List<Renderer> suspended)
        {
            foreach (var surface in _surfaces)
                if (surface.Renderer && surface.Renderer.enabled)
                {
                    suspended.Add(surface.Renderer);
                    surface.Renderer.enabled = false;
                }
        }

        internal void Release()
        {
            ClearSurfaces();
            if (_owner) _owner.Forget(this);
            _owner = null;
            _templateId = null;
        }
        // Icon capture reparents the model to an inactive camera before enabling it.
        // Disabling is therefore not a pool-return signal. Explicit pool hooks clean up.
        private void OnDestroy() { Release(); }
        private void ClearSurfaces()
        {
            _projectionValid = false;
            foreach (var lod in _lods) if (lod.Group) lod.Group.SetLODs(lod.Original);
            _lods.Clear();
            foreach (var surface in _surfaces) surface.Dispose();
            _surfaces.Clear();
        }

        private sealed class Surface : IDisposable
        {
            internal readonly Renderer Source;
            internal readonly MeshRenderer Renderer;
            internal readonly Vector3[] Vertices;
            internal readonly HashSet<int> Used;
            private readonly Mesh _mesh;
            private readonly GameObject _object;
            private readonly float[] _heights;
            private readonly Vector2[] _uvs;
            private Matrix4x4 _localToClip;
            internal Surface(Renderer source, RingMeshData data, List<int> triangles, Material material)
            {
                Source = source;
                Vertices = data.Vertices;
                Used = new HashSet<int>(triangles);
                _heights = new float[Vertices.Length];
                _uvs = new Vector2[Vertices.Length];
                _mesh = new Mesh { name = "DWV_ItemTint_Surface", hideFlags = HideFlags.DontSave,
                    indexFormat = Vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                try
                {
                    _mesh.vertices = Vertices;
                    _mesh.SetTriangles(triangles, 0);
                    var colors = new Color32[Vertices.Length];
                    for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
                    _mesh.colors32 = colors;
                    _mesh.uv = _uvs;
                    _object = new GameObject("DWV_ItemTint_Surface") { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
                    _object.transform.SetParent(source.transform, false);
                    _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
                    Renderer = _object.AddComponent<MeshRenderer>();
                    Renderer.sharedMaterial = material;
                    Renderer.shadowCastingMode = ShadowCastingMode.Off;
                    Renderer.receiveShadows = false;
                    Renderer.lightProbeUsage = LightProbeUsage.Off;
                    Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                }
                catch { Dispose(); throw; }
            }
            internal bool UpdateProjection(Matrix4x4 viewProjection)
            {
                var localToClip = viewProjection * Source.localToWorldMatrix;
                bool changed = !_localToClip.Equals(localToClip);
                _localToClip = localToClip;
                return changed;
            }
            internal void Project(ref float minimum, ref float maximum)
            {
                foreach (int i in Used)
                {
                    float height = _heights[i] = ItemGradientProjection.Height(Vertices[i], _localToClip);
                    minimum = Math.Min(minimum, height);
                    maximum = Math.Max(maximum, height);
                }
            }
            internal void SetGradient(float minimum, float maximum)
            {
                foreach (int i in Used)
                    _uvs[i] = new Vector2(ItemGradientProjection.Coordinate(_heights[i], minimum, maximum), 0.5f);
                _mesh.uv = _uvs;
            }
            public void Dispose()
            {
                if (_object) { _object.SetActive(false); _object.transform.SetParent(null, false); Object.Destroy(_object); }
                if (_mesh) Object.Destroy(_mesh);
            }
        }
    }
}
