using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal sealed class RingMeshData
    {
        internal Vector3[] Vertices;
        internal int[][] Triangles;

        internal static RingMeshData Read(Mesh mesh)
        {
            if (mesh.vertexCount == 0) throw new InvalidOperationException("Empty mesh.");
            var result = new RingMeshData { Triangles = new int[mesh.subMeshCount][] };
            if (mesh.isReadable)
            {
                result.Vertices = mesh.vertices;
                for (int s = 0; s < mesh.subMeshCount; s++)
                    result.Triangles[s] = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetTriangles(s, true) : Array.Empty<int>();
                return result;
            }

            // EFT commonly discards CPU mesh copies. Read the existing GPU buffers once
            // per equipped model, without changing imported meshes or their buffer targets.
            var format = mesh.GetVertexAttributeFormat(VertexAttribute.Position);
            if (mesh.GetVertexAttributeDimension(VertexAttribute.Position) < 3 ||
                (format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16))
                throw new NotSupportedException($"Unsupported position format {format}.");
            int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
            int offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            int stride = mesh.GetVertexBufferStride(stream);
            byte[] vertexBytes;
            using (var buffer = mesh.GetVertexBuffer(stream))
            {
                vertexBytes = new byte[checked(buffer.count * buffer.stride)];
                buffer.GetData(vertexBytes);
            }
            result.Vertices = new Vector3[mesh.vertexCount];
            int componentSize = format == VertexAttributeFormat.Float32 ? 4 : 2;
            float ReadComponent(int index) => componentSize == 4
                ? BitConverter.ToSingle(vertexBytes, index)
                : Mathf.HalfToFloat(BitConverter.ToUInt16(vertexBytes, index));
            for (int i = 0; i < result.Vertices.Length; i++)
            {
                int p = checked(i * stride + offset);
                result.Vertices[i] = new Vector3(ReadComponent(p), ReadComponent(p + componentSize), ReadComponent(p + 2 * componentSize));
            }

            byte[] indexBytes;
            using (var buffer = mesh.GetIndexBuffer())
            {
                indexBytes = new byte[checked(buffer.count * buffer.stride)];
                buffer.GetData(indexBytes);
            }
            int indexSize = mesh.indexFormat == IndexFormat.UInt32 ? 4 : 2;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (sub.topology != MeshTopology.Triangles)
                {
                    result.Triangles[s] = Array.Empty<int>();
                    continue;
                }
                var indices = new int[sub.indexCount];
                for (int j = 0; j < indices.Length; j++)
                {
                    int p = checked((sub.indexStart + j) * indexSize);
                    int index = indexSize == 4 ? checked((int)BitConverter.ToUInt32(indexBytes, p)) : BitConverter.ToUInt16(indexBytes, p);
                    indices[j] = checked(index + sub.baseVertex);
                    if (indices[j] < 0 || indices[j] >= result.Vertices.Length)
                        throw new InvalidOperationException("GPU index points outside the vertex buffer.");
                }
                result.Triangles[s] = indices;
            }
            return result;
        }
    }
}
