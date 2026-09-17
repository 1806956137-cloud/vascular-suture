using UnityEngine;

namespace PBDLearn
{
    /// <summary>
    /// ClothRenderer：只负责把 ClothSimulator 的结果写回 Mesh。
    /// - 不推进仿真（仿真由 ClothSimulationDriver 负责）
    /// - 不画调试 Gizmos（调试由 ClothDebugGizmos 负责）
    ///
    /// 兼容层：
    /// - Attach / AttachToInitialPosition 仅做转发，保证旧脚本（如 VesselSingleLayerSetup）不改也能跑
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public class ClothRenderer : MonoBehaviour
    {
        [Tooltip("如果为空，会自动在同一物体上查找 ClothSimulationDriver。")]
        public ClothSimulationDriver simulation;

        [Tooltip("每帧写回 Mesh.bounds，避免只渲染一半/裁剪。通常建议开。")]
        public bool recalcBounds = true;

        private Mesh _mesh;

        private void Awake()
        {
            if (simulation == null)
            {
                simulation = GetComponent<ClothSimulationDriver>();
            }
        }

        private void LateUpdate()
        {
            if (simulation == null) return;
            var sim = simulation.Simulator;
            if (sim == null) return;

            // 等待仿真 Job 完成，确保 positions/normals 可读
            sim.ComputeAsyncJobs();

            if (_mesh == null)
            {
                _mesh = simulation.Mesh;
                if (_mesh == null) return;
            }

            _mesh.SetVertices(sim.positions);
            _mesh.SetNormals(sim.normals);

            if (recalcBounds)
            {
                _mesh.RecalculateBounds();
            }
        }

        // --------------------------------------------------------------------
        // 兼容层：把旧版 ClothRenderer 上的控制接口转发到 ClothSimulationDriver
        // --------------------------------------------------------------------

        /// <summary>
        /// 旧接口兼容：把某个顶点绑到一个 Transform 上
        /// </summary>
        public void Attach(int vertexIndex, Transform target)
        {
            if (simulation == null) simulation = GetComponent<ClothSimulationDriver>();
            if (simulation == null)
            {
                Debug.LogError("ClothRenderer.Attach failed: ClothSimulationDriver not found on the same GameObject.");
                return;
            }
            simulation.Attach(vertexIndex, target);
        }

        /// <summary>
        /// 旧接口兼容：把某个顶点锁死在初始位置（适合做血管两端固定）
        /// </summary>
        public void AttachToInitialPosition(int vertexIndex)
        {
            if (simulation == null) simulation = GetComponent<ClothSimulationDriver>();
            if (simulation == null)
            {
                Debug.LogError("ClothRenderer.AttachToInitialPosition failed: ClothSimulationDriver not found on the same GameObject.");
                return;
            }
            simulation.AttachToInitialPosition(vertexIndex);
        }
    }
}
