using UnityEngine;

namespace PBDLearn
{
    /// <summary>
    /// 程序化生成圆柱形单层血管壁（只生成侧面，不生成端面）。
    /// 可以手动指定网格密度，也可以根据碰撞体半径自动计算网格密度：
    /// 让一个网格的小格宽度 < 碰撞体半径 / cellsPerRadius。
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [DefaultExecutionOrder(-110)] // 确保在 ClothRenderer.Awake 之前执行
    public class ProceduralVesselMesh : MonoBehaviour
    {
        [Header("几何尺寸")]
        [Tooltip("血管半径")]
        public float radius = 0.01f;

        [Tooltip("血管长度（沿本地 Z 轴）")]
        public float length = 0.05f;

        [Header("网格密度（基础值）")]
        [Tooltip("轴向细分段数（沿长度方向）")]
        public int axialSegments = 16;   // 长度方向几圈

        [Tooltip("环向细分段数（圆周方向）")]
        public int radialSegments = 32;  // 每圈多少个点

        [Header("根据碰撞体自动调整网格密度")]
        [Tooltip("是否根据指定碰撞体的半径自动调整网格密度")]
        public bool autoAdjustSegmentsByColliderRadius = false;

        [Tooltip("用于参考的碰撞体（上面挂了 SphereCollider 或 CapsuleCollider 的那个器械）")]
        public UnityColliderProxy referenceCollider;

        [Tooltip("希望一个网格的小格宽度约为 colliderRadius / cellsPerRadius。\n" +
                 "例如 1 = 小格宽度 ≲ 碰撞体半径；2 = 小格宽度 ≲ 半径的一半。")]
        public float cellsPerRadius = 1.0f;

        [Tooltip("进入场景/播放时自动生成网格")]
        public bool generateOnAwake = true;

        // 调试用：生成时记录的碰撞体世界半径和目标单元尺寸
        [Header("Debug Info")]
        public float debugColliderWorldRadius = 0f;
        public float debugTargetCellSize = 0f;

        void Awake()
        {
            if (generateOnAwake)
            {
                Generate();
            }
        }

        /// <summary>
        /// 主入口：生成圆柱壳网格。
        /// </summary>
        public void Generate()
        {
            if (radius <= 0f || length <= 0f)
            {
                Debug.LogWarning("ProceduralVesselMesh: radius 或 length 非法。");
                return;
            }

            // 如需要，先根据碰撞体半径自动计算 axialSegments / radialSegments
            if (autoAdjustSegmentsByColliderRadius && referenceCollider != null)
            {
                AutoComputeSegmentsFromCollider();
            }

            if (axialSegments < 1) axialSegments = 1;
            if (radialSegments < 3) radialSegments = 3;

            var mf = GetComponent<MeshFilter>();
            if (mf == null)
            {
                Debug.LogError("ProceduralVesselMesh: 未找到 MeshFilter。");
                return;
            }

            var mesh = new Mesh();
            mesh.name = "ProceduralVessel";

            int vertCount = (axialSegments + 1) * radialSegments;
            Vector3[] vertices = new Vector3[vertCount];
            Vector3[] normals = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];

            int idx = 0;
            for (int i = 0; i <= axialSegments; i++)
            {
                float z = length * (i / (float)axialSegments - 0.5f); // [-L/2, L/2]
                float v = i / (float)axialSegments;

                for (int j = 0; j < radialSegments; j++)
                {
                    float u = j / (float)radialSegments;
                    float theta = u * Mathf.PI * 2.0f;

                    float x = radius * Mathf.Cos(theta);
                    float y = radius * Mathf.Sin(theta);

                    vertices[idx] = new Vector3(x, y, z);
                    normals[idx] = new Vector3(x, y, 0.0f).normalized; // 径向外法线
                    uvs[idx] = new Vector2(u, v);
                    idx++;
                }
            }

            int quadCount = axialSegments * radialSegments;
            int[] triangles = new int[quadCount * 6];

            int t = 0;
            for (int i = 0; i < axialSegments; i++)
            {
                int rowStart = i * radialSegments;
                int nextRowStart = (i + 1) * radialSegments;

                for (int j = 0; j < radialSegments; j++)
                {
                    int jNext = (j + 1) % radialSegments;

                    int i0 = rowStart + j;
                    int i1 = rowStart + jNext;
                    int i2 = nextRowStart + j;
                    int i3 = nextRowStart + jNext;

                    // 两个三角组成一个四边形
                    triangles[t++] = i0;
                    triangles[t++] = i2;
                    triangles[t++] = i1;

                    triangles[t++] = i1;
                    triangles[t++] = i2;
                    triangles[t++] = i3;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
        }

        /// <summary>
        /// 根据 referenceCollider 的世界半径，自动计算轴向/环向段数，
        /// 使得每个网格单元的尺寸 <= colliderRadius / cellsPerRadius。
        /// </summary>
        private void AutoComputeSegmentsFromCollider()
        {
            float worldRadius = ComputeColliderWorldRadius(referenceCollider);
            if (worldRadius <= 0f)
            {
                Debug.LogWarning("ProceduralVesselMesh: 无法从参考碰撞体计算世界半径。");
                return;
            }

            float k = Mathf.Max(1e-4f, cellsPerRadius);
            float targetCellSize = worldRadius / k; // 希望单元宽度 <= 这个值

            debugColliderWorldRadius = worldRadius;
            debugTargetCellSize = targetCellSize;

            // 圆周方向：单位长度是 2πr
            float circumference = 2f * Mathf.PI * radius;
            radialSegments = Mathf.Max(3, Mathf.CeilToInt(circumference / targetCellSize));

            // 轴向方向：单位长度是 length
            axialSegments = Mathf.Max(1, Mathf.CeilToInt(length / targetCellSize));
        }

        /// <summary>
        /// 估算参考碰撞体的世界半径（只考虑 SphereCollider / CapsuleCollider）。
        /// </summary>
        private float ComputeColliderWorldRadius(UnityColliderProxy proxy)
        {
            if (proxy == null) return 0f;

            Transform t = proxy.transform;
            float scale = Mathf.Max(
                Mathf.Abs(t.lossyScale.x),
                Mathf.Abs(t.lossyScale.y),
                Mathf.Abs(t.lossyScale.z)
            );
            if (scale <= 0f) scale = 1f;

            float radius = 0f;

            var sphere = proxy.GetComponent<SphereCollider>();
            if (sphere != null)
            {
                radius = sphere.radius * scale;
            }
            else
            {
                var capsule = proxy.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    radius = capsule.radius * scale;
                }
            }

            return radius;
        }
    }
}
