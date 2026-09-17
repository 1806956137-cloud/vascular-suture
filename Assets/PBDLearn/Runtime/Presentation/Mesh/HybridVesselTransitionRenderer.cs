using System;
using System.Collections.Generic;
using UnityEngine;

namespace PBDLearn
{
    /// <summary>
    /// Presentation-only collar. Material coordinates transport the displacement
    /// of its verified inlet ring; the branch-side boundary stays at rest.
    /// Runs after ClothRendererSlice so both surfaces use the same final frame.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public sealed class HybridVesselTransitionRenderer : MonoBehaviour
    {
        public MeshFilter simulationOuter;
        public Mesh simulationRestMesh;
        public MeshFilter staticBranch;

        private const float IdentityTolerance = 1e-5f;
        private MeshFilter _filter;
        private Mesh _source, _runtime, _boundOuter;
        private Vector3[] _rest, _authoredNormals, _restGeometryNormals, _vertices;
        private Vector2[] _uv, _seamMaterial;
        private int[] _inlet, _outlet, _liveInlet;
        private Vector3[] _displacements, _liveNormals;
        private Quaternion[] _seamRotations;
        private readonly List<Vector3> _outerVertices = new List<Vector3>();
        private readonly List<Vector3> _outerNormals = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private int _boundCount;
        private float _nextAuditTime;

        public float MaxInletGap { get; private set; }
        public float MaxInletNormalAngle { get; private set; }
        public float MaxOutletDisplacement { get; private set; }

        private void LateUpdate()
        {
            try
            {
                UpdatePresentation();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[HybridTransition][InterfaceConformality] FAIL: {exception.Message}", this);
                enabled = false;
            }
        }

        // Also callable by the editor regression fixture without advancing PBD.
        public void UpdatePresentation()
        {
            if (_runtime == null) Initialize();
            Mesh outer = simulationOuter.sharedMesh;
            Require(outer != null, "Missing live Outer mesh.");
            if (_boundOuter != outer || _boundCount != outer.vertexCount)
                BindLiveInlet(outer);
            outer.GetVertices(_outerVertices);
            outer.GetNormals(_outerNormals);
            Require(_outerNormals.Count == _outerVertices.Count, "Live Outer normals are missing.");

            Matrix4x4 outerToCollar = transform.worldToLocalMatrix * simulationOuter.transform.localToWorldMatrix;
            Matrix4x4 normalToCollar = outerToCollar.inverse.transpose;
            for (int i = 0; i < _inlet.Length; i++)
            {
                Vector3 position = outerToCollar.MultiplyPoint3x4(_outerVertices[_liveInlet[i]]);
                Vector3 normal = normalToCollar.MultiplyVector(_outerNormals[_liveInlet[i]]).normalized;
                Require(Finite(position) && Finite(normal) && normal.sqrMagnitude > 0.5f,
                    $"Invalid live inlet sample {i}.");
                _displacements[i] = position - _rest[_inlet[i]];
                _liveNormals[i] = normal;
            }

            for (int i = 0; i < _vertices.Length; i++)
            {
                Bracket(_uv[i].x, out int a, out int b, out float t);
                _vertices[i] = _rest[i] + Fade(_uv[i].y) * Vector3.Lerp(_displacements[a], _displacements[b], t);
            }
            // Assign exact endpoints, independent of interpolation roundoff.
            for (int i = 0; i < _inlet.Length; i++)
                _vertices[_inlet[i]] = outerToCollar.MultiplyPoint3x4(_outerVertices[_liveInlet[i]]);
            foreach (int id in _outlet) _vertices[id] = _rest[id];

            _runtime.vertices = _vertices;
            _runtime.RecalculateNormals();
            _runtime.GetNormals(_normals);
            for (int i = 0; i < _normals.Count; i++)
            {
                // Keep the authored shading at rest; transport it with the
                // actual deformed geometry rather than resetting the whole collar.
                _normals[i] = Quaternion.FromToRotation(_restGeometryNormals[i], _normals[i]) * _authoredNormals[i];
            }
            for (int i = 0; i < _inlet.Length; i++)
                _seamRotations[i] = Quaternion.FromToRotation(_normals[_inlet[i]], _liveNormals[i]);
            for (int i = 0; i < _normals.Count; i++)
            {
                Bracket(_uv[i].x, out int a, out int b, out float t);
                Quaternion correction = Quaternion.Slerp(_seamRotations[a], _seamRotations[b], t);
                _normals[i] = (Quaternion.Slerp(Quaternion.identity, correction, Fade(_uv[i].y)) * _normals[i]).normalized;
            }
            for (int i = 0; i < _inlet.Length; i++) _normals[_inlet[i]] = _liveNormals[i];
            foreach (int id in _outlet) _normals[id] = _authoredNormals[id];
            _runtime.SetNormals(_normals);
            _runtime.RecalculateBounds();

            Audit(outerToCollar);
            if (Application.isPlaying && Time.unscaledTime >= _nextAuditTime)
            {
                _nextAuditTime = Time.unscaledTime + 5f;
                Debug.Log($"[HybridTransition][InterfaceConformality] PASS pairs={_inlet.Length}/{_outlet.Length} " +
                    $"inletGap={MaxInletGap:E3} normalAngle={MaxInletNormalAngle:F5} " +
                    $"outletMove={MaxOutletDisplacement:E3} source=rendered-final-outer", this);
            }
        }

        private void Initialize()
        {
            Require(simulationOuter != null && simulationRestMesh != null && staticBranch != null,
                "Missing explicit Outer/rest/branch references.");
            _filter = GetComponent<MeshFilter>();
            _source = _filter.sharedMesh;
            Require(_source != null && staticBranch.sharedMesh != null, "Missing collar or branch mesh.");
            _rest = _source.vertices;
            _authoredNormals = _source.normals;
            _uv = _source.uv;
            Require(_uv.Length == _rest.Length && _authoredNormals.Length == _rest.Length,
                "Collar material coordinates or authored normals are missing.");
            var inlet = new List<int>();
            var outlet = new List<int>();
            for (int i = 0; i < _uv.Length; i++)
            {
                Require(Finite(_rest[i]) && Finite(_authoredNormals[i]) &&
                    _uv[i].x >= 0f && _uv[i].x < 1f && _uv[i].y >= 0f && _uv[i].y <= 1f,
                    $"Invalid collar sample/material coordinate at {i}.");
                if (_uv[i].y == 0f) inlet.Add(i);
                if (_uv[i].y == 1f) outlet.Add(i);
            }
            Require(inlet.Count >= 3 && outlet.Count >= 3, "Missing collar boundary rings.");
            inlet.Sort((a, b) => _uv[a].x.CompareTo(_uv[b].x));
            _inlet = inlet.ToArray();
            _outlet = outlet.ToArray();
            _seamMaterial = new Vector2[_inlet.Length];
            Vector3[] outerRest = simulationRestMesh.vertices;
            Vector2[] outerUv = simulationRestMesh.uv;
            Require(outerUv.Length == outerRest.Length, "Outer rest material coordinates are missing.");
            var outerEnd = new List<int>();
            for (int i = 0; i < outerUv.Length; i++)
                if (outerUv[i].y == 1f) outerEnd.Add(i);
            Matrix4x4 outerToCollar = transform.worldToLocalMatrix * simulationOuter.transform.localToWorldMatrix;
            int[] restIds = MatchBoundary(_inlet, outerEnd, outerRest, outerToCollar);
            for (int i = 0; i < _inlet.Length; i++)
            {
                Require(i == 0 || _uv[_inlet[i]].x > _uv[_inlet[i - 1]].x, "Repeated inlet material identity.");
                _seamMaterial[i] = outerUv[restIds[i]];
            }
            Dictionary<ulong, int> collarEdges = Edges(_source.triangles);
            ValidateInterface(_inlet, restIds, collarEdges, Edges(simulationRestMesh.triangles));
            var branchEdges = Edges(staticBranch.sharedMesh.triangles);
            var branchBoundary = new HashSet<int>();
            foreach (var edge in branchEdges)
                if (edge.Value == 1) { branchBoundary.Add((int)(edge.Key >> 32)); branchBoundary.Add((int)(uint)edge.Key); }
            int[] branchIds = MatchBoundary(_outlet, new List<int>(branchBoundary), staticBranch.sharedMesh.vertices,
                transform.worldToLocalMatrix * staticBranch.transform.localToWorldMatrix);
            ValidateInterface(_outlet, branchIds, collarEdges, branchEdges);
            var allBoundary = new HashSet<int>(_inlet);
            allBoundary.UnionWith(_outlet);
            foreach (var edge in collarEdges)
                if (edge.Value == 1)
                    Require(allBoundary.Contains((int)(edge.Key >> 32)) && allBoundary.Contains((int)(uint)edge.Key),
                        "Unexpected open boundary inside collar.");

            _vertices = new Vector3[_rest.Length];
            _displacements = new Vector3[_inlet.Length];
            _liveNormals = new Vector3[_inlet.Length];
            _seamRotations = new Quaternion[_inlet.Length];
            _runtime = Instantiate(_source);
            _runtime.name = _source.name + " (following Outer)";
            _runtime.MarkDynamic();
            _runtime.RecalculateNormals();
            _restGeometryNormals = _runtime.normals;
            _filter.sharedMesh = _runtime;
        }

        private void BindLiveInlet(Mesh mesh)
        {
            Vector2[] uv = mesh.uv;
            Require(uv.Length == mesh.vertexCount, "Live Outer material identities are missing.");
            _liveInlet = new int[_seamMaterial.Length];
            for (int i = 0; i < _seamMaterial.Length; i++)
            {
                int found = -1;
                for (int j = 0; j < uv.Length; j++)
                {
                    if (uv[j] != _seamMaterial[i]) continue;
                    Require(found < 0, $"Duplicate live Outer inlet material identity {i}.");
                    found = j;
                }
                Require(found >= 0, $"Missing live Outer inlet material identity {i}.");
                _liveInlet[i] = found;
            }
            ValidateInterface(_inlet, _liveInlet, Edges(_source.triangles), Edges(mesh.triangles));
            _boundOuter = mesh;
            _boundCount = mesh.vertexCount;
        }

        private int[] MatchBoundary(int[] collar, List<int> candidates, Vector3[] vertices, Matrix4x4 toCollar)
        {
            Require(collar.Length == candidates.Count, $"Boundary sample counts differ: {collar.Length}/{candidates.Count}.");
            var result = new int[collar.Length];
            var used = new HashSet<int>();
            for (int i = 0; i < collar.Length; i++)
            {
                int match = -1;
                foreach (int id in candidates)
                {
                    if (Vector3.Distance(_rest[collar[i]], toCollar.MultiplyPoint3x4(vertices[id])) > IdentityTolerance) continue;
                    Require(match < 0, $"Ambiguous rest boundary identity at {collar[i]}.");
                    match = id;
                }
                Require(match >= 0 && used.Add(match), $"Missing/repeated rest boundary identity at {collar[i]}.");
                result[i] = match;
            }
            return result;
        }

        private static Dictionary<ulong, int> Edges(int[] triangles)
        {
            var edges = new Dictionary<ulong, int>();
            for (int i = 0; i < triangles.Length; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    ulong key = Edge(triangles[i + j], triangles[i + (j + 1) % 3]);
                    edges.TryGetValue(key, out int count);
                    Require(count < 2, "Non-manifold interface mesh.");
                    edges[key] = count + 1;
                }
            return edges;
        }

        private static void ValidateInterface(int[] collar, int[] other, Dictionary<ulong, int> collarEdges, Dictionary<ulong, int> otherEdges)
        {
            var map = new Dictionary<int, int>();
            var degree = new Dictionary<int, int>();
            for (int i = 0; i < collar.Length; i++) { map.Add(collar[i], other[i]); degree.Add(collar[i], 0); }
            int count = 0;
            foreach (var edge in collarEdges)
            {
                int a = (int)(edge.Key >> 32), b = (int)(uint)edge.Key;
                if (edge.Value != 1 || !map.ContainsKey(a) || !map.ContainsKey(b)) continue;
                Require(otherEdges.TryGetValue(Edge(map[a], map[b]), out int uses) && uses == 1,
                    $"Interface edge mismatch {a}-{b} -> {map[a]}-{map[b]}.");
                degree[a]++; degree[b]++; count++;
            }
            Require(count == collar.Length, "Incomplete interface edge loop.");
            foreach (int d in degree.Values) Require(d == 2, "Open/branched interface boundary.");
        }

        private void Bracket(float u, out int a, out int b, out float t)
        {
            a = _inlet.Length - 1;
            for (int i = 0; i + 1 < _inlet.Length; i++)
                if (u >= _uv[_inlet[i]].x && u < _uv[_inlet[i + 1]].x) { a = i; break; }
            b = (a + 1) % _inlet.Length;
            float from = _uv[_inlet[a]].x, to = _uv[_inlet[b]].x;
            if (b == 0) { to += 1f; if (u < from) u += 1f; }
            t = (u - from) / (to - from);
        }

        private void Audit(Matrix4x4 outerToCollar)
        {
            MaxInletGap = MaxInletNormalAngle = MaxOutletDisplacement = 0f;
            for (int i = 0; i < _inlet.Length; i++)
            {
                MaxInletGap = Mathf.Max(MaxInletGap, Vector3.Distance(_vertices[_inlet[i]],
                    outerToCollar.MultiplyPoint3x4(_outerVertices[_liveInlet[i]])));
                MaxInletNormalAngle = Mathf.Max(MaxInletNormalAngle, Vector3.Angle(_normals[_inlet[i]], _liveNormals[i]));
            }
            foreach (int id in _outlet)
                MaxOutletDisplacement = Mathf.Max(MaxOutletDisplacement, Vector3.Distance(_vertices[id], _rest[id]));
            Require(MaxInletGap <= IdentityTolerance && MaxOutletDisplacement <= IdentityTolerance && MaxInletNormalAngle < 0.05f,
                "Rendered interface continuity failed.");
        }

        // Quintic falloff has zero first and second derivatives at both ends.
        private static float Fade(float s) => 1f - s * s * s * (s * (s * 6f - 15f) + 10f);
        private static ulong Edge(int a, int b) => ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private void OnDisable()
        {
            if (_runtime != null)
            {
                if (_filter != null && _filter.sharedMesh == _runtime) _filter.sharedMesh = _source;
                if (Application.isPlaying) Destroy(_runtime); else DestroyImmediate(_runtime);
            }
            _runtime = null;
            _boundOuter = null;
            _boundCount = 0;
        }
    }
}
