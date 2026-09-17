using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace PBDLearn
{
    /// <summary>
    /// ClothSimulationDriver
    /// - 负责：创建/销毁 ClothSimulator，推进 Step(dt)，管理碰撞体 Proxy、Pin/Attach 等“仿真控制”
    /// - 不负责：Mesh 写回（ClothRenderer）
    /// - 不负责：Gizmos 调试（ClothDebugGizmos）
    /// </summary>
    [DisallowMultipleComponent]
    public class ClothSimulationDriver : MonoBehaviour
    {
        [Header("Time Step")]
        [Range(0.005f, 0.02f)]
        public float dt = 0.01f;

        [Header("Setting")]
        public ClothSetting setting;

        [Header("Colliders (Proxy)")]
        public UnityColliderProxy[] colliderProxies;

        [Header("Mesh")]
        [Tooltip("是否在运行时克隆 Mesh（推荐开）。避免修改导入资源。")]
        public bool cloneMeshAtRuntime = true;

        [Header("Mesh Density (Runtime Subdivide)")]
        [Tooltip("细分次数：0=不细分；1=每个三角拆4个；2=再细分一轮...（建议从1开始）")]
        [Range(0, 3)]
        public int subdivisionLevel = 0;

        [Tooltip("是否在 Awake 时应用细分（一般保持 true）。")]
        public bool applySubdivisionOnAwake = true;

        [Header("Mesh Weld (Seam Fix)")]
        [Tooltip("是否在初始化时焊接重复顶点（例如 0°/360° UV 缝导致的重合点）。")]
        public bool weldDuplicateVertices = true;

        [Tooltip("焊接阈值：距离小于该值的顶点会被合并。")]
        public float weldEpsilon = 1e-4f;

        /// <summary>
        /// 公开给渲染/调试脚本使用
        /// </summary>
        public ClothSimulator Simulator => _simulator;
        public Mesh Mesh => _mesh;
        public MeshFilter MeshFilter => _meshFilter;

        private ClothSimulator _simulator;
        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private ClothMeshModifier _meshModifier;

        // 用 Transform 作为锚点的“跟随式固定”（可选）
        private readonly Dictionary<int, Transform> _attachments = new Dictionary<int, Transform>();

        private float _elapsedTime;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null)
            {
                Debug.LogError("ClothSimulationDriver requires a MeshFilter.");
                enabled = false;
                return;
            }

            // 1) 克隆一份 Mesh，避免修改导入资源（非常重要）
            if (cloneMeshAtRuntime && _meshFilter.sharedMesh != null)
            {
                _meshFilter.sharedMesh = Instantiate(_meshFilter.sharedMesh);
            }

            _mesh = _meshFilter.sharedMesh;
            if (_mesh == null)
            {
                Debug.LogError("MeshFilter.sharedMesh is null.");
                enabled = false;
                return;
            }

            // 2) 运行时细分（提高“质点密度”）
            if (applySubdivisionOnAwake && subdivisionLevel > 0)
            {
                for (int i = 0; i < subdivisionLevel; i++)
                {
                    SubdivideOnce(_mesh);
                }
                _mesh.RecalculateNormals();
                _mesh.RecalculateBounds();

                Debug.Log($"[ClothSimulationDriver] Subdivide x{subdivisionLevel} => verts={_mesh.vertexCount}, tris={_mesh.triangles.Length / 3}");
            }

            // 3) 焊接重复顶点（例如 UV 缝 0°/360°）
            if (weldDuplicateVertices)
            {
                // 你工程里如果 WeldDuplicateVertices 就在 ClothMeshModifier 里，保留这一行即可
                ClothMeshModifier.WeldDuplicateVertices(_mesh, weldEpsilon);
            }

            // 4) 用处理后的 Mesh 创建 PBD 网格数据
            _meshModifier = ClothMeshModifier.CreateFromMeshFilter(_meshFilter);
            _simulator = new ClothSimulator(_meshModifier, setting);

            // 5) 注册碰撞体
            if (colliderProxies != null)
            {
                foreach (var c in colliderProxies)
                {
                    if (c != null) _simulator.AddCollider(c);
                }
            }

            // （保持你之前行为）摆回原点
            transform.localPosition = Vector3.zero;
        }

        private void Start()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                renderer.sharedMaterial.EnableKeyword("_INPUT_WORLD_VERTEX");
            }
        }

        private void OnDestroy()
        {
            if (_simulator != null)
            {
                _simulator.Dispose();
                _simulator = null;
            }

            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                renderer.sharedMaterial.DisableKeyword("_INPUT_WORLD_VERTEX");
            }
        }

        /// <summary>
        /// 把某个顶点绑到一个 Transform 上（端点跟着目标移动）
        /// </summary>
        public void Attach(int vertexIndex, Transform target)
        {
            if (target == null) throw new System.Exception("target can not be null");
            _attachments[vertexIndex] = target;

            if (_simulator != null && vertexIndex >= 0 && vertexIndex < _simulator.positions.Length)
            {
                target.position = _simulator.positions[vertexIndex];
            }
        }

        /// <summary>
        /// 把某个顶点锁死在“当前(初始)位置”。适合做血管两端固定。
        /// </summary>
        public void AttachToInitialPosition(int vertexIndex)
        {
            if (_simulator == null) return;
            if (vertexIndex < 0 || vertexIndex >= _simulator.positions.Length) return;
            _simulator.AddPinConstraint(vertexIndex, _simulator.positions[vertexIndex]);
        }

        private void Update()
        {
            if (Application.isPlaying && Time.timeScale == 0f)
                return;

            if (_simulator == null) return;

            // 跟随式固定（可选）
            foreach (var pair in _attachments)
            {
                var t = pair.Value;
                if (t != null)
                {
                    _simulator.AddPinConstraint(pair.Key, t.position);
                }
            }

            // 不使用风力/场力：置 0
            _simulator.SetFieldForce(float3.zero);

            _elapsedTime += Time.deltaTime;
            int cnt = Mathf.FloorToInt(_elapsedTime / dt);
            for (int i = 0; i < cnt; i++)
            {
                _simulator.Step(dt);
                if (i < cnt - 1)
                {
                    _simulator.ComputeAsyncJobs();
                }
            }

            if (cnt > 0)
            {
                _elapsedTime -= cnt * dt;
            }
        }

        // --------------------------- Mesh Subdivide (internal) ---------------------------

        /// <summary>
        /// 对 Mesh 做一次 1->4 三角细分（与 VesselMeshSubdivider.cs 同逻辑）
        /// </summary>
        private static void SubdivideOnce(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var triangles = mesh.triangles;

            bool hasUV = uvs != null && uvs.Length == vertices.Length;

            var vertList = new List<Vector3>(vertices);
            var uvList = hasUV ? new List<Vector2>(uvs) : null;

            var newTriangles = new List<int>(triangles.Length * 4);

            // 边 -> 中点索引缓存，防止重复创建
            var edgeMidpointIndex = new Dictionary<int, int>(triangles.Length);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                int m01 = GetMidpointIndex(i0, i1, vertList, uvList, hasUV, edgeMidpointIndex);
                int m12 = GetMidpointIndex(i1, i2, vertList, uvList, hasUV, edgeMidpointIndex);
                int m20 = GetMidpointIndex(i2, i0, vertList, uvList, hasUV, edgeMidpointIndex);

                // 4 个子三角
                newTriangles.Add(i0); newTriangles.Add(m01); newTriangles.Add(m20);
                newTriangles.Add(i1); newTriangles.Add(m12); newTriangles.Add(m01);
                newTriangles.Add(i2); newTriangles.Add(m20); newTriangles.Add(m12);
                newTriangles.Add(m01); newTriangles.Add(m12); newTriangles.Add(m20);
            }

            mesh.SetVertices(vertList);
            if (hasUV && uvList != null) mesh.SetUVs(0, uvList);
            mesh.SetTriangles(newTriangles, 0);
        }

        private static int GetMidpointIndex(
            int i0,
            int i1,
            List<Vector3> vertList,
            List<Vector2> uvList,
            bool hasUV,
            Dictionary<int, int> edgeMidpointIndex)
        {
            int key = GetEdgeKey(i0, i1);
            if (edgeMidpointIndex.TryGetValue(key, out int index))
                return index;

            Vector3 v = (vertList[i0] + vertList[i1]) * 0.5f;
            index = vertList.Count;
            vertList.Add(v);

            if (hasUV && uvList != null)
            {
                Vector2 uv = (uvList[i0] + uvList[i1]) * 0.5f;
                uvList.Add(uv);
            }

            edgeMidpointIndex.Add(key, index);
            return index;
        }

        private static int GetEdgeKey(int a, int b)
        {
            if (a < b) return (a << 16) + b;
            return (b << 16) + a;
        }
    }
}
