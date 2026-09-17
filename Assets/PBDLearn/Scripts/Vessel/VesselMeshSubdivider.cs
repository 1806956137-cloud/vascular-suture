using System.Collections.Generic;
using UnityEngine;

namespace PBDLearn
{
    /// <summary>
    /// 在运行前对当前 Mesh 做可控次数的三角形细分，
    /// 用于给 PBD 血管“多打点质点”。
    /// </summary>
    [DefaultExecutionOrder(-100)]                 // 确保在 ClothRenderer 之前执行
    [RequireComponent(typeof(MeshFilter))]
    public class VesselMeshSubdivider : MonoBehaviour
    {
        [Tooltip("细分次数：0 = 不细分；1 = 每个三角拆成4个；2 = 再在上一层基础上拆。")]
        [Range(0, 3)]
        public int subdivisionLevel = 1;

        [Tooltip("只在第一次运行时应用一次（编辑器下调参），避免重复细分。")]
        public bool applyOnAwake = true;

        void Awake()
        {
            if (!applyOnAwake || subdivisionLevel <= 0) return;

            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            // 克隆一份，避免改到资源文件
            mf.sharedMesh = Instantiate(mf.sharedMesh);
            mf.sharedMesh.name = mf.sharedMesh.name + "_Subdivided";

            var mesh = mf.sharedMesh;

            for (int i = 0; i < subdivisionLevel; i++)
            {
                SubdivideOnce(mesh);
            }
        }

        /// <summary>
        /// 对 Mesh 做一次 1->4 三角形细分。
        /// </summary>
        private void SubdivideOnce(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var triangles = mesh.triangles;

            bool hasUV = uvs != null && uvs.Length == vertices.Length;

            var vertList = new List<Vector3>(vertices);
            var uvList = hasUV ? new List<Vector2>(uvs) : null;

            var newTriangles = new List<int>(triangles.Length * 4);

            // 边 -> 中点顶点索引 的缓存，防止重复创建
            var edgeMidpointIndex = new Dictionary<int, int>();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                int m01 = GetMidpointIndex(i0, i1, vertList, uvList, hasUV, edgeMidpointIndex);
                int m12 = GetMidpointIndex(i1, i2, vertList, uvList, hasUV, edgeMidpointIndex);
                int m20 = GetMidpointIndex(i2, i0, vertList, uvList, hasUV, edgeMidpointIndex);

                // 原三角 (i0, i1, i2) 拆成 4 个三角
                newTriangles.Add(i0); newTriangles.Add(m01); newTriangles.Add(m20);
                newTriangles.Add(i1); newTriangles.Add(m12); newTriangles.Add(m01);
                newTriangles.Add(i2); newTriangles.Add(m20); newTriangles.Add(m12);
                newTriangles.Add(m01); newTriangles.Add(m12); newTriangles.Add(m20);
            }

            mesh.SetVertices(vertList);
            if (hasUV)
            {
                mesh.SetUVs(0, uvList);
            }
            mesh.SetTriangles(newTriangles, 0);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// 获取边(i0,i1)的中点顶点索引，如不存在则创建新顶点。
        /// </summary>
        private int GetMidpointIndex(
            int i0,
            int i1,
            List<Vector3> vertList,
            List<Vector2> uvList,
            bool hasUV,
            Dictionary<int, int> edgeMidpointIndex
        )
        {
            int key = GetEdgeKey(i0, i1);

            int index;
            if (edgeMidpointIndex.TryGetValue(key, out index))
            {
                return index;
            }

            // 位置插值
            Vector3 v = (vertList[i0] + vertList[i1]) * 0.5f;
            index = vertList.Count;
            vertList.Add(v);

            // UV 插值（如有的话）
            if (hasUV && uvList != null)
            {
                Vector2 uv = (uvList[i0] + uvList[i1]) * 0.5f;
                uvList.Add(uv);
            }

            edgeMidpointIndex.Add(key, index);
            return index;
        }

        /// <summary>
        /// 用两个顶点索引生成无序边的哈希键（假设单个 Mesh 顶点数 < 65535）。
        /// </summary>
        private int GetEdgeKey(int a, int b)
        {
            if (a < b) return (a << 16) + b;
            return (b << 16) + a;
        }
    }
}
