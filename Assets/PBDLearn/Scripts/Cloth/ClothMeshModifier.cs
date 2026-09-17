using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace PBDLearn
{
    public class ClothMeshModifier
    {
        private NativeList<float3> _vertices;
        private NativeList<float3> _normals;
        private NativeList<float2> _uv;
        private NativeList<int> _indices;

        private List<VertexExtraInfo> _verticesInfo;
        private readonly Dictionary<int, Edge> _edgeDict = new Dictionary<int, Edge>();

        private ClothMeshModifier(
            NativeList<float3> vertices,
            NativeList<float2> uvs,
            NativeList<int> indices,
            NativeList<float3> normals)
        {
            _vertices = vertices;
            _uv = uvs;
            _indices = indices;
            _normals = normals;
            BuildIndexes();
        }

        public void Dispose()
        {
            if (_vertices.IsCreated) _vertices.Dispose();
            if (_uv.IsCreated) _uv.Dispose();
            if (_indices.IsCreated) _indices.Dispose();
            if (_normals.IsCreated) _normals.Dispose();
        }

        public int vertexCount => _vertices.Length;

        private int NewVertex(float3 vertex, float2 uv)
        {
            int index = _vertices.Length;
            _vertices.Add(vertex);
            _uv.Add(uv);
            _verticesInfo.Add(new VertexExtraInfo());
            return index;
        }

        /// <summary>
        /// 缓存一条无向边：
        /// - vIndex0/vIndex1：边的两个端点（内部会排序）
        /// - triangleIndex：该边属于哪个三角形
        /// - oppositeVertex：该三角形中，与这条边相对的第三个顶点（用于 bend 的 vIndex2/vIndex3）
        /// </summary>
        private void CacheEdge(int vIndex0, int vIndex1, int triangleIndex, int oppositeVertex)
        {
            int edgeHash = Edge.GetEdgeHash(vIndex0, vIndex1);

            if (_edgeDict.TryGetValue(edgeHash, out Edge edge))
            {
                // 第二次遇到同一条边：记录第二个三角形及其对顶点
                edge.triangleIndexes.Add(triangleIndex);

                // 只在还没填满时写入 vIndex3
                if (edge.vIndex3 < 0 && oppositeVertex >= 0)
                {
                    edge.vIndex3 = oppositeVertex;
                }
            }
            else
            {
                edge = new Edge(vIndex0, vIndex1);
                edge.triangleIndexes.Add(triangleIndex);

                // 第一次遇到：写入 vIndex2（第一侧的对顶点）
                edge.vIndex2 = oppositeVertex;

                _edgeDict.Add(edgeHash, edge);

                _verticesInfo[edge.vIndex0].edgeHashes.Add(edgeHash);
                _verticesInfo[edge.vIndex1].edgeHashes.Add(edgeHash);
            }
        }

        private void BuildIndexes()
        {
            _verticesInfo = new List<VertexExtraInfo>(_vertices.Length);
            for (int i = 0; i < _vertices.Length; i++)
                _verticesInfo.Add(new VertexExtraInfo());

            int triangleCount = _indices.Length / 3;
            for (int t = 0; t < triangleCount; t++)
            {
                int offset = t * 3;
                int i0 = _indices[offset];
                int i1 = _indices[offset + 1];
                int i2 = _indices[offset + 2];

                // 对每个三角形的三条边：
                // edge(i0,i1) opposite=i2
                // edge(i1,i2) opposite=i0
                // edge(i2,i0) opposite=i1
                CacheEdge(i0, i1, t, i2);
                CacheEdge(i1, i2, t, i0);
                CacheEdge(i2, i0, t, i1);
            }
        }

        private void RemoveEdge(int edgeHash)
        {
            var edge = _edgeDict[edgeHash];
            _verticesInfo[edge.vIndex0].edgeHashes.Remove(edgeHash);
            _verticesInfo[edge.vIndex1].edgeHashes.Remove(edgeHash);
            _edgeDict.Remove(edgeHash);
        }

        private Edge GetEdge(int vIndex0, int vIndex1)
        {
            int hash = Edge.GetEdgeHash(vIndex0, vIndex1);
            if (_edgeDict.TryGetValue(hash, out var e)) return e;
            return null;
        }

        /// <summary>提取一个三角形中，与指定顶点相关的两条边</summary>
        private void GetEdge(int triangleIndex, int vertexIndex, out Edge edge0, out Edge edge1)
        {
            int offset = triangleIndex * 3;
            edge0 = null;
            edge1 = null;

            for (int i = 0; i < 3; i++)
            {
                int vIndex = _indices[offset + i];
                if (vIndex == vertexIndex) continue;

                if (edge0 == null) edge0 = GetEdge(vertexIndex, vIndex);
                else edge1 = GetEdge(vertexIndex, vIndex);
            }
        }

        public int GetTriangleVertexIndexExcept(int triangleIndex, int exceptVertex0, int exceptVertex1)
        {
            for (int i = 0; i < 3; i++)
            {
                int vIndex = _indices[triangleIndex * 3 + i];
                if (vIndex != exceptVertex0 && vIndex != exceptVertex1) return vIndex;
            }
            return -1;
        }

        public IReadOnlyCollection<Edge> edges => _edgeDict.Values;
        public NativeArray<int> indices => _indices;
        public NativeArray<float2> uvs => _uv;
        public NativeArray<float3> vertices => _vertices;
        public NativeArray<float3> normals => _normals;

        public static ClothMeshModifier CreateFromMeshFilter(MeshFilter meshFilter)
        {
            var mesh = meshFilter.sharedMesh;
            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var indices = mesh.triangles;

            var verticesList = new NativeList<float3>(vertices.Length, Allocator.Persistent);
            verticesList.Resize(vertices.Length, NativeArrayOptions.UninitializedMemory);

            var localToWorld = meshFilter.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++)
                verticesList[i] = localToWorld.MultiplyPoint3x4(vertices[i]);

            var normals = new NativeList<float3>(vertices.Length, Allocator.Persistent);
            normals.Resize(vertices.Length, NativeArrayOptions.UninitializedMemory);

            var uvList = new NativeList<float2>(uvs.Length, Allocator.Persistent);
            uvList.Resize(uvs.Length, NativeArrayOptions.UninitializedMemory);
            uvList.AsArray().Reinterpret<Vector2>().CopyFrom(uvs);

            var indicesList = new NativeList<int>(indices.Length, Allocator.Persistent);
            indicesList.Resize(indices.Length, NativeArrayOptions.UninitializedMemory);
            indicesList.AsArray().Reinterpret<int>().CopyFrom(indices);

            return new ClothMeshModifier(verticesList, uvList, indicesList, normals);
        }

        /// <summary>
        /// 从两张 Mesh（内外壁）构建一个“合并后的” ClothMeshModifier。
        /// triangles/edges 仅来自各自网格，不包含跨层拓扑。
        /// </summary>
        public static ClothMeshModifier CreateFromMeshes(
            Mesh outerMesh, Transform outerTransform,
            Mesh innerMesh, Transform innerTransform)
        {
            if (outerMesh == null) throw new System.ArgumentNullException(nameof(outerMesh));
            if (innerMesh == null) throw new System.ArgumentNullException(nameof(innerMesh));
            if (outerTransform == null) throw new System.ArgumentNullException(nameof(outerTransform));
            if (innerTransform == null) throw new System.ArgumentNullException(nameof(innerTransform));

            var ov = outerMesh.vertices;
            var ouv = outerMesh.uv;
            var ot = outerMesh.triangles;

            var iv = innerMesh.vertices;
            var iuv = innerMesh.uv;
            var it = innerMesh.triangles;

            int outerCount = ov != null ? ov.Length : 0;
            int innerCount = iv != null ? iv.Length : 0;
            int totalCount = outerCount + innerCount;

            var verticesList = new NativeList<float3>(totalCount, Allocator.Persistent);
            verticesList.Resize(totalCount, NativeArrayOptions.UninitializedMemory);

            var uvList = new NativeList<float2>(totalCount, Allocator.Persistent);
            uvList.Resize(totalCount, NativeArrayOptions.UninitializedMemory);

            var normals = new NativeList<float3>(totalCount, Allocator.Persistent);
            normals.Resize(totalCount, NativeArrayOptions.UninitializedMemory);

            var oL2W = outerTransform.localToWorldMatrix;
            for (int i = 0; i < outerCount; i++)
            {
                verticesList[i] = oL2W.MultiplyPoint3x4(ov[i]);
                if (ouv != null && ouv.Length == outerCount) uvList[i] = new float2(ouv[i].x, ouv[i].y);
                else uvList[i] = float2.zero;
            }

            var iL2W = innerTransform.localToWorldMatrix;
            for (int i = 0; i < innerCount; i++)
            {
                int dst = outerCount + i;
                verticesList[dst] = iL2W.MultiplyPoint3x4(iv[i]);
                if (iuv != null && iuv.Length == innerCount) uvList[dst] = new float2(iuv[i].x, iuv[i].y);
                else uvList[dst] = float2.zero;
            }

            int outerTriCount = ot != null ? ot.Length : 0;
            int innerTriCount = it != null ? it.Length : 0;

            var indicesList = new NativeList<int>(outerTriCount + innerTriCount, Allocator.Persistent);
            indicesList.Resize(outerTriCount + innerTriCount, NativeArrayOptions.UninitializedMemory);

            // Outer triangles: keep original winding
            for (int i = 0; i < outerTriCount; i++)
                indicesList[i] = ot[i];

            // Inner triangles: flip winding so that its normals are opposite to the outer layer.
            // This is important for correct backface culling when using single-sided materials.
            // Unity triangles are stored as triplets (a,b,c). Flipping winding is swapping b and c.
            for (int i = 0; i + 2 < innerTriCount; i += 3)
            {
                int a = it[i] + outerCount;
                int b = it[i + 1] + outerCount;
                int c = it[i + 2] + outerCount;

                int dst = outerTriCount + i;
                indicesList[dst] = a;
                indicesList[dst + 1] = c;
                indicesList[dst + 2] = b;
            }

            return new ClothMeshModifier(verticesList, uvList, indicesList, normals);
        }

        public static void WeldDuplicateVertices(Mesh mesh, float epsilon = 1e-4f)
        {
            if (mesh == null) return;

            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var triangles = mesh.triangles;

            int vertCount = vertices.Length;
            if (vertCount == 0) return;

            var newVertices = new List<Vector3>(vertCount);
            var newUVs = (uvs != null && uvs.Length == vertCount) ? new List<Vector2>(vertCount) : null;

            var oldToNew = new int[vertCount];
            float eps2 = epsilon * epsilon;

            for (int i = 0; i < vertCount; i++)
            {
                Vector3 v = vertices[i];
                int found = -1;

                for (int j = 0; j < newVertices.Count; j++)
                {
                    if ((newVertices[j] - v).sqrMagnitude <= eps2)
                    {
                        found = j;
                        break;
                    }
                }

                if (found >= 0)
                {
                    oldToNew[i] = found;
                }
                else
                {
                    int newIndex = newVertices.Count;
                    newVertices.Add(v);
                    oldToNew[i] = newIndex;

                    if (newUVs != null) newUVs.Add(uvs[i]);
                }
            }

            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = oldToNew[triangles[i]];

            mesh.Clear();
            mesh.vertices = newVertices.ToArray();
            if (newUVs != null) mesh.uv = newUVs.ToArray();
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        public struct TearResult
        {
            public bool success;
            public int splitVertexIndex;
            public int oppVertexIndex;
            public int newVertexIndex0;
            public int newVertexIndex1;
        }

        /// <summary>
        /// Edge：用于 distance / bend 约束
        /// - vIndex0/vIndex1：边的两个端点（无向、内部排序）
        /// - vIndex2/vIndex3：共边两侧三角形的对顶点（没有第二个三角形则为 -1）
        /// </summary>
        public class Edge
        {
            public int vIndex0;
            public int vIndex1;

            // 两侧对顶点（用于 bend）
            public int vIndex2 = -1;
            public int vIndex3 = -1;

            // 该边相邻的三角形索引（最多 2 个）
            public List<int> triangleIndexes = new List<int>(2);

            public Edge(int vIndex0, int vIndex1)
            {
                this.vIndex0 = math.min(vIndex0, vIndex1);
                this.vIndex1 = math.max(vIndex0, vIndex1);
            }

            public override int GetHashCode() => (vIndex0 << 16) | vIndex1;

            public override bool Equals(object obj)
            {
                if (!(obj is Edge)) return false;
                var e = (Edge)obj;
                return vIndex0 == e.vIndex0 && vIndex1 == e.vIndex1;
            }

            public static int GetEdgeHash(int a, int b)
            {
                if (a < b) return (a << 16) | b;
                return (b << 16) | a;
            }
        }

        private class VertexExtraInfo
        {
            public List<int> edgeHashes = new List<int>();
        }
    }
}
