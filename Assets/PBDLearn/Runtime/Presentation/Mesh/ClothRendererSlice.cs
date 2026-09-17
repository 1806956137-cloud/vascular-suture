using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace PBDLearn
{
    /// <summary>
    /// ClothRendererSlice：只负责把 DoubleLayer Driver 的某一层 slice 写回到本 Mesh。
    /// - 不推进仿真
    /// - 不创建/销毁 Simulator
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public class ClothRendererSlice : MonoBehaviour
    {
        public ClothSimulationDriverDoubleLayer simulation;
        public ClothSimulationDriverDoubleLayer.VesselLayer layer = ClothSimulationDriverDoubleLayer.VesselLayer.Outer;

        [Tooltip("每帧写回 Mesh.bounds，避免只渲染一半/裁剪。通常建议开。")]
        public bool recalcBounds = true;

        private MeshFilter _meshFilter;
        private Mesh _mesh;

        private readonly List<Vector3> _verts = new List<Vector3>(4096);
        private readonly List<Vector3> _norms = new List<Vector3>(4096);

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
        }

        private void LateUpdate()
        {
            if (simulation == null) return;
            var sim = simulation.Simulator;
            if (sim == null) return;

            // 确保仿真 Job 完成
            sim.ComputeAsyncJobs();

            if (_mesh != _meshFilter.sharedMesh)
            {
                _mesh = _meshFilter.sharedMesh;
                if (_mesh == null) return;
            }
            if (_mesh == null) return;

            simulation.GetLayerSlice(layer, out int start, out int count);
            if (count <= 0) return;

            var w2l = _meshFilter.transform.worldToLocalMatrix;

            _verts.Clear();
            _norms.Clear();
            if (_verts.Capacity < count) _verts.Capacity = count;
            if (_norms.Capacity < count) _norms.Capacity = count;

            var positions = sim.positions;
            var normals = sim.normals;

            for (int i = 0; i < count; i++)
            {
                int gi = start + i;

                // sim.positions / sim.normals 是世界坐标（Driver 合并网格时即转为 world）
                float3 wp = positions[gi];
                float3 wn = normals[gi];

                Vector3 lp = w2l.MultiplyPoint3x4(new Vector3(wp.x, wp.y, wp.z));
                Vector3 ln = w2l.MultiplyVector(new Vector3(wn.x, wn.y, wn.z)).normalized;

                _verts.Add(lp);
                _norms.Add(ln);
            }

            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_norms);

            if (recalcBounds)
            {
                _mesh.RecalculateBounds();
            }
        }
    }
}
