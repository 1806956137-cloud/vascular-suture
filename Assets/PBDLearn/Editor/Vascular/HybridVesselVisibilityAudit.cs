using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PBDLearn
{
    internal static class HybridVesselVisibilityAudit
    {
        private const string PrefabPath =
            "Assets/PBDLearn/Content/Vascular/Prefabs/HybridBranchedVessel.prefab";
        private const int TissueGridSegments = 96;
        private const float OuterRadius = 0.16886057f;
        private const float VisibleClearance = 0.004f;
        private const float FloatingTipClearance = 0.07f;
        private const float TipSeparation = OuterRadius * 2.2f;
        private const int TipLimit = 32;

        private sealed class VisibleComponent
        {
            public int id;
            public int triangleCount;
            public float area;
            public float maxClearance;
            public Bounds bounds;
            public bool containsEntrance;
        }

        public static void RunBatch()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Hybrid vessel prefab was not found at {PrefabPath}.");
            }

            Transform tissueTransform =
                prefab.transform.Find("TissueSurface");
            Transform branchTransform =
                prefab.transform.Find(
                    "StaticBranchAppearance/TripoBranchesTrimmed");
            Mesh tissue =
                tissueTransform
                    ?.GetComponent<MeshFilter>()
                    ?.sharedMesh;
            Mesh branch =
                branchTransform
                    ?.GetComponent<MeshFilter>()
                    ?.sharedMesh;
            if (tissue == null ||
                branch == null ||
                branchTransform == null)
            {
                throw new InvalidOperationException(
                    "Hybrid vessel visibility-audit assets are incomplete.");
            }

            Matrix4x4 branchToRoot =
                prefab.transform.worldToLocalMatrix *
                branchTransform.localToWorldMatrix;
            Vector3[] sourceVertices = branch.vertices;
            var rootVertices =
                new Vector3[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                rootVertices[i] =
                    branchToRoot.MultiplyPoint3x4(
                        sourceVertices[i]);
            }

            Vector3[] tissueVertices = tissue.vertices;
            float[] clearance =
                ComputeTissueClearance(
                    rootVertices,
                    tissueVertices);
            int[] triangles = branch.triangles;
            List<int>[] vertexAdjacency =
                BuildVertexAdjacency(
                    sourceVertices.Length,
                    triangles);
            HashSet<int> entranceVertices =
                FindBoundaryVertices(
                    triangles);
            float[] entranceDistance =
                ComputeDistances(
                    rootVertices,
                    vertexAdjacency,
                    entranceVertices);

            int[] triangleComponent;
            List<VisibleComponent> components =
                BuildVisibleComponents(
                    rootVertices,
                    triangles,
                    clearance,
                    entranceVertices,
                    out triangleComponent);
            List<int> tips =
                FindTerminalVertices(
                    rootVertices,
                    entranceDistance,
                    vertexAdjacency);

            int mainComponent = -1;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i].containsEntrance)
                {
                    mainComponent = components[i].id;
                    break;
                }
            }
            if (mainComponent < 0 && components.Count > 0)
            {
                components.Sort(
                    (a, b) =>
                        b.triangleCount.CompareTo(
                            a.triangleCount));
                mainComponent = components[0].id;
            }

            int orphanCount = 0;
            int orphanTriangleCount = 0;
            int materialOrphanCount = 0;
            int materialOrphanTriangleCount = 0;
            var componentPreview = new List<string>();
            for (int i = 0; i < components.Count; i++)
            {
                VisibleComponent component = components[i];
                if (component.id != mainComponent)
                {
                    orphanCount++;
                    orphanTriangleCount +=
                        component.triangleCount;
                    if (component.area >= 0.02f &&
                        component.maxClearance >= 0.03f)
                    {
                        materialOrphanCount++;
                        materialOrphanTriangleCount +=
                            component.triangleCount;
                    }
                }
                if (componentPreview.Count < 16)
                {
                    componentPreview.Add(
                        $"#{component.id}:tris={component.triangleCount}," +
                        $"area={component.area:F5}," +
                        $"maxGap={component.maxClearance:F4}," +
                        $"entrance={component.containsEntrance}," +
                        $"center={component.bounds.center:F3}," +
                        $"size={component.bounds.size:F3}");
                }
            }

            int floatingTipCount = 0;
            int orphanTipCount = 0;
            var tipPreview = new List<string>();
            for (int tipIndex = 0;
                 tipIndex < tips.Count;
                 tipIndex++)
            {
                int tip = tips[tipIndex];
                Vector3 direction =
                    EstimateTerminalDirection(
                        tip,
                        rootVertices,
                        entranceDistance);
                float downDot =
                    Vector3.Dot(
                        direction,
                        Vector3.down);
                int component =
                    FindVertexVisibleComponent(
                        tip,
                        triangles,
                        triangleComponent);
                bool floating =
                    clearance[tip] >
                    FloatingTipClearance &&
                    downDot < 0.35f;
                bool orphan =
                    component >= 0 &&
                    component != mainComponent;
                if (floating)
                    floatingTipCount++;
                if (orphan)
                    orphanTipCount++;
                tipPreview.Add(
                    $"v={tip},gap={clearance[tip]:F4}," +
                    $"down={downDot:F3}," +
                    $"component={component}," +
                    $"floating={floating}," +
                    $"orphan={orphan}," +
                    $"p={rootVertices[tip]:F3}");
            }

            Debug.Log(
                $"[HybridVesselAudit] visibility components=" +
                $"{components.Count} main={mainComponent} " +
                $"orphans={orphanCount} " +
                $"orphanTriangles={orphanTriangleCount} " +
                $"materialOrphans={materialOrphanCount} " +
                $"materialOrphanTriangles=" +
                $"{materialOrphanTriangleCount} " +
                $"preview=[{string.Join("; ", componentPreview)}]");
            Debug.Log(
                $"[HybridVesselAudit] terminals total={tips.Count} " +
                $"floating={floatingTipCount} " +
                $"orphan={orphanTipCount} " +
                $"preview=[{string.Join("; ", tipPreview)}]");
            if (materialOrphanCount > 0 ||
                orphanTipCount > 0)
            {
                throw new InvalidOperationException(
                    $"Hybrid vessel visible continuity failed. " +
                    $"materialOrphans={materialOrphanCount} " +
                    $"materialOrphanTriangles=" +
                    $"{materialOrphanTriangleCount} " +
                    $"orphanTips={orphanTipCount}.");
            }
        }

        private static float[] ComputeTissueClearance(
            Vector3[] points,
            Vector3[] tissueVertices)
        {
            int columns = TissueGridSegments + 1;
            int topVertexCount = columns * columns;
            if (tissueVertices.Length < topVertexCount)
            {
                throw new InvalidOperationException(
                    "Hybrid tissue mesh does not contain the expected top grid.");
            }

            float minX = tissueVertices[0].x;
            float maxX =
                tissueVertices[TissueGridSegments].x;
            float minZ = tissueVertices[0].z;
            float maxZ =
                tissueVertices[
                    TissueGridSegments *
                    columns].z;
            var clearance = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float gridX =
                    Mathf.Clamp01(
                        Mathf.InverseLerp(
                            minX,
                            maxX,
                            points[i].x)) *
                    TissueGridSegments;
                float gridZ =
                    Mathf.Clamp01(
                        Mathf.InverseLerp(
                            minZ,
                            maxZ,
                            points[i].z)) *
                    TissueGridSegments;
                int column =
                    Mathf.Min(
                        TissueGridSegments - 1,
                        Mathf.FloorToInt(gridX));
                int row =
                    Mathf.Min(
                        TissueGridSegments - 1,
                        Mathf.FloorToInt(gridZ));
                float tx = gridX - column;
                float tz = gridZ - row;
                int a = row * columns + column;
                int b = a + 1;
                int c = a + columns;
                int d = c + 1;
                float front =
                    Mathf.Lerp(
                        tissueVertices[a].y,
                        tissueVertices[b].y,
                        tx);
                float back =
                    Mathf.Lerp(
                        tissueVertices[c].y,
                        tissueVertices[d].y,
                        tx);
                float tissueY =
                    Mathf.Lerp(
                        front,
                        back,
                        tz);
                clearance[i] = points[i].y - tissueY;
            }
            return clearance;
        }

        private static List<int>[] BuildVertexAdjacency(
            int vertexCount,
            int[] triangles)
        {
            var adjacency =
                new List<int>[vertexCount];
            for (int i = 0; i < adjacency.Length; i++)
                adjacency[i] = new List<int>(8);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddNeighbor(
                    adjacency,
                    triangles[i],
                    triangles[i + 1]);
                AddNeighbor(
                    adjacency,
                    triangles[i + 1],
                    triangles[i + 2]);
                AddNeighbor(
                    adjacency,
                    triangles[i + 2],
                    triangles[i]);
            }
            return adjacency;
        }

        private static void AddNeighbor(
            List<int>[] adjacency,
            int a,
            int b)
        {
            if (!adjacency[a].Contains(b))
                adjacency[a].Add(b);
            if (!adjacency[b].Contains(a))
                adjacency[b].Add(a);
        }

        private static HashSet<int> FindBoundaryVertices(
            int[] triangles)
        {
            var multiplicity =
                new Dictionary<ulong, int>();
            for (int i = 0;
                 i + 2 < triangles.Length;
                 i += 3)
            {
                AddEdge(
                    multiplicity,
                    triangles[i],
                    triangles[i + 1]);
                AddEdge(
                    multiplicity,
                    triangles[i + 1],
                    triangles[i + 2]);
                AddEdge(
                    multiplicity,
                    triangles[i + 2],
                    triangles[i]);
            }

            var boundary = new HashSet<int>();
            foreach (KeyValuePair<ulong, int> pair in multiplicity)
            {
                if (pair.Value != 1)
                    continue;
                boundary.Add((int)(pair.Key >> 32));
                boundary.Add((int)(pair.Key & uint.MaxValue));
            }
            if (boundary.Count < 3)
            {
                throw new InvalidOperationException(
                    "Static branch entrance boundary was not found.");
            }
            return boundary;
        }

        private static void AddEdge(
            Dictionary<ulong, int> multiplicity,
            int a,
            int b)
        {
            uint min = (uint)Mathf.Min(a, b);
            uint max = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            multiplicity.TryGetValue(
                key,
                out int count);
            multiplicity[key] = count + 1;
        }

        private static float[] ComputeDistances(
            Vector3[] vertices,
            List<int>[] adjacency,
            HashSet<int> sources)
        {
            var distances =
                new float[vertices.Length];
            var inQueue =
                new bool[vertices.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < distances.Length; i++)
                distances[i] = float.PositiveInfinity;
            foreach (int source in sources)
            {
                distances[source] = 0f;
                inQueue[source] = true;
                queue.Enqueue(source);
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                inQueue[current] = false;
                List<int> neighbors =
                    adjacency[current];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];
                    float candidate =
                        distances[current] +
                        Vector3.Distance(
                            vertices[current],
                            vertices[neighbor]);
                    if (candidate >=
                        distances[neighbor] - 1e-7f)
                    {
                        continue;
                    }
                    distances[neighbor] = candidate;
                    if (inQueue[neighbor])
                        continue;
                    inQueue[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
            return distances;
        }

        private static List<VisibleComponent>
            BuildVisibleComponents(
                Vector3[] rootVertices,
                int[] triangles,
                float[] clearance,
                HashSet<int> entranceVertices,
                out int[] triangleComponent)
        {
            int triangleCount = triangles.Length / 3;
            var active = new bool[triangleCount];
            var adjacency =
                new List<int>[triangleCount];
            var edgeOwners =
                new Dictionary<ulong, int>();
            for (int triangle = 0;
                 triangle < triangleCount;
                 triangle++)
            {
                adjacency[triangle] = new List<int>(3);
                int start = triangle * 3;
                int a = triangles[start];
                int b = triangles[start + 1];
                int c = triangles[start + 2];
                active[triangle] =
                    Mathf.Max(
                        clearance[a],
                        clearance[b],
                        clearance[c]) >
                    VisibleClearance;
                if (!active[triangle])
                    continue;
                AddVisibleEdge(
                    edgeOwners,
                    adjacency,
                    triangle,
                    a,
                    b,
                    clearance);
                AddVisibleEdge(
                    edgeOwners,
                    adjacency,
                    triangle,
                    b,
                    c,
                    clearance);
                AddVisibleEdge(
                    edgeOwners,
                    adjacency,
                    triangle,
                    c,
                    a,
                    clearance);
            }

            triangleComponent =
                new int[triangleCount];
            for (int i = 0;
                 i < triangleComponent.Length;
                 i++)
            {
                triangleComponent[i] = -1;
            }
            var components =
                new List<VisibleComponent>();
            var queue = new Queue<int>();
            for (int seed = 0;
                 seed < triangleCount;
                 seed++)
            {
                if (!active[seed] ||
                    triangleComponent[seed] >= 0)
                {
                    continue;
                }

                int componentId =
                    components.Count;
                var component =
                    new VisibleComponent
                    {
                        id = componentId,
                        bounds = new Bounds(
                            rootVertices[
                                triangles[seed * 3]],
                            Vector3.zero),
                    };
                triangleComponent[seed] =
                    componentId;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int triangle = queue.Dequeue();
                    int start = triangle * 3;
                    int a = triangles[start];
                    int b = triangles[start + 1];
                    int c = triangles[start + 2];
                    component.triangleCount++;
                    component.area +=
                        Vector3.Cross(
                            rootVertices[b] -
                            rootVertices[a],
                            rootVertices[c] -
                            rootVertices[a]).magnitude *
                        0.5f;
                    component.maxClearance =
                        Mathf.Max(
                            component.maxClearance,
                            clearance[a],
                            clearance[b],
                            clearance[c]);
                    component.bounds.Encapsulate(
                        rootVertices[a]);
                    component.bounds.Encapsulate(
                        rootVertices[b]);
                    component.bounds.Encapsulate(
                        rootVertices[c]);
                    component.containsEntrance |=
                        entranceVertices.Contains(a) ||
                        entranceVertices.Contains(b) ||
                        entranceVertices.Contains(c);
                    List<int> neighbors =
                        adjacency[triangle];
                    for (int i = 0;
                         i < neighbors.Count;
                         i++)
                    {
                        int neighbor = neighbors[i];
                        if (triangleComponent[neighbor] >= 0)
                            continue;
                        triangleComponent[neighbor] =
                            componentId;
                        queue.Enqueue(neighbor);
                    }
                }
                components.Add(component);
            }
            return components;
        }

        private static void AddVisibleEdge(
            Dictionary<ulong, int> edgeOwners,
            List<int>[] adjacency,
            int triangle,
            int a,
            int b,
            float[] clearance)
        {
            if ((clearance[a] + clearance[b]) * 0.5f <=
                VisibleClearance)
            {
                return;
            }
            uint min = (uint)Mathf.Min(a, b);
            uint max = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            if (!edgeOwners.TryGetValue(
                    key,
                    out int owner))
            {
                edgeOwners[key] = triangle;
                return;
            }
            adjacency[owner].Add(triangle);
            adjacency[triangle].Add(owner);
        }

        private static List<int> FindTerminalVertices(
            Vector3[] rootVertices,
            float[] entranceDistance,
            List<int>[] adjacency)
        {
            var maxima = new List<int>();
            for (int vertex = 0;
                 vertex < rootVertices.Length;
                 vertex++)
            {
                bool isMaximum = true;
                List<int> neighbors =
                    adjacency[vertex];
                for (int i = 0;
                     i < neighbors.Count;
                     i++)
                {
                    if (entranceDistance[neighbors[i]] >
                        entranceDistance[vertex] + 1e-6f)
                    {
                        isMaximum = false;
                        break;
                    }
                }
                if (isMaximum)
                    maxima.Add(vertex);
            }
            maxima.Sort(
                (a, b) =>
                    entranceDistance[b].CompareTo(
                        entranceDistance[a]));

            var tips = new List<int>();
            for (int candidateIndex = 0;
                 candidateIndex < maxima.Count &&
                 tips.Count < TipLimit;
                 candidateIndex++)
            {
                int candidate =
                    maxima[candidateIndex];
                bool overlaps = false;
                for (int tipIndex = 0;
                     tipIndex < tips.Count;
                     tipIndex++)
                {
                    if (Vector3.Distance(
                            rootVertices[candidate],
                            rootVertices[tips[tipIndex]]) <
                        TipSeparation)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                    tips.Add(candidate);
            }
            return tips;
        }

        private static Vector3 EstimateTerminalDirection(
            int tip,
            Vector3[] rootVertices,
            float[] entranceDistance)
        {
            float tipDistance =
                entranceDistance[tip];
            float minBehind =
                tipDistance - OuterRadius * 3.0f;
            float maxBehind =
                tipDistance - OuterRadius * 1.1f;
            Vector3 sum = Vector3.zero;
            float weightSum = 0f;
            for (int vertex = 0;
                 vertex < rootVertices.Length;
                 vertex++)
            {
                float distance =
                    entranceDistance[vertex];
                if (distance < minBehind ||
                    distance > maxBehind)
                {
                    continue;
                }
                float spatial =
                    Vector3.Distance(
                        rootVertices[tip],
                        rootVertices[vertex]);
                if (spatial > OuterRadius * 4f)
                    continue;
                float weight =
                    1f /
                    Mathf.Max(spatial, 1e-3f);
                sum += rootVertices[vertex] * weight;
                weightSum += weight;
            }
            if (weightSum <= 1e-6f)
                return Vector3.zero;
            Vector3 direction =
                rootVertices[tip] -
                sum / weightSum;
            return direction.sqrMagnitude > 1e-12f
                ? direction.normalized
                : Vector3.zero;
        }

        private static int FindVertexVisibleComponent(
            int vertex,
            int[] triangles,
            int[] triangleComponent)
        {
            for (int triangle = 0;
                 triangle < triangleComponent.Length;
                 triangle++)
            {
                if (triangleComponent[triangle] < 0)
                    continue;
                int start = triangle * 3;
                if (triangles[start] == vertex ||
                    triangles[start + 1] == vertex ||
                    triangles[start + 2] == vertex)
                {
                    return triangleComponent[triangle];
                }
            }
            return -1;
        }
    }
}
