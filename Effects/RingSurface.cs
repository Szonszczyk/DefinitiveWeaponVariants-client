using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal sealed class RingSurface : IDisposable
    {
        internal const string ObjectName = "DWV_FiringRing_Surface";
        internal readonly Renderer Source;
        private readonly Mesh _mesh;
        private readonly GameObject _object;
        private readonly MeshRenderer _renderer;
        private readonly Vector3[] _vertices;
        private readonly List<Vector3> _bakedVertices;
        private readonly List<Vector2> _uvs;
        private readonly float[] _distances;
        private readonly int[] _usedVertices;
        private readonly SkinnedMeshRenderer _skinned;
        private readonly Mesh _baked;

        internal RingSurface(Renderer source, RingMeshData geometry, List<int> triangles, Material material, Mesh baked = null)
        {
            Source = source;
            _vertices = geometry.Vertices;
            _skinned = source as SkinnedMeshRenderer;
            _baked = baked;
            _bakedVertices = _skinned != null ? new List<Vector3>(_vertices.Length) : null;
            _uvs = new List<Vector2>(_vertices.Length);
            _distances = new float[_vertices.Length];
            var used = new HashSet<int>(triangles);
            _usedVertices = new int[used.Count];
            used.CopyTo(_usedVertices);
            for (int i = 0; i < _vertices.Length; i++) _uvs.Add(Vector2.zero);
            _mesh = new Mesh { name = ObjectName, indexFormat = _vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.SetTriangles(triangles, 0);
            var colors = new Color32[_vertices.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
            _mesh.colors32 = colors;
            _object = new GameObject(ObjectName) { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
            _object.transform.SetParent(source.transform, false);
            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _renderer.enabled = false;
        }

        internal bool Project(Transform root, Transform muzzle, Vector3 axis, out float minimum, out float maximum)
        {
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            if (!Source || !_object) return false;
            // Mirror LOD visibility instead of showing every LOD at once. Parenting also
            // makes the overlay follow the source's animated bolt/stock/attachment.
            _renderer.enabled = Source.transform.IsChildOf(root) && Source.enabled && Source.isVisible && Source.gameObject.activeInHierarchy;
            if (!_renderer.enabled) return false;
            _object.layer = Source.gameObject.layer;
            if (_skinned)
            {
                _skinned.BakeMesh(_baked);
                _baked.GetVertices(_bakedVertices);
                if (_bakedVertices.Count != _vertices.Length) { _renderer.enabled = false; return false; }
                _bakedVertices.CopyTo(_vertices);
                _mesh.SetVertices(_bakedVertices);
            }
            RingProjection.Project(_vertices, _usedVertices, muzzle.worldToLocalMatrix * Source.localToWorldMatrix,
                axis, _distances, out minimum, out maximum);
            return true;
        }

        internal void Draw(float center, float width)
        {
            if (!_renderer || !_renderer.enabled) return;
            // Move the sampling coordinates ourselves: Sprite shaders do not all
            // implement _MainTex_ST. This also avoids needing a custom shader bundle.
            for (int i = 0; i < _uvs.Count; i++)
                _uvs[i] = new Vector2((_distances[i] - center) / width + 0.5f, 0.5f);
            _mesh.SetUVs(0, _uvs);
        }

        internal void Hide() { if (_renderer) _renderer.enabled = false; }

        public void Dispose()
        {
            // Unity destroys objects at end of frame: hide and detach immediately so
            // no enabled overlay can survive into a reused pool hierarchy this frame.
            if (_object) { _object.SetActive(false); _object.transform.SetParent(null, false); Object.Destroy(_object); }
            if (_mesh) Object.Destroy(_mesh);
            if (_baked) Object.Destroy(_baked);
        }
    }
}
