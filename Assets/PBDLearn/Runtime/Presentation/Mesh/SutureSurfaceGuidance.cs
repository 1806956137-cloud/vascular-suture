using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDLearn.Presentation
{
    // Geometry decals: every ring vertex is projected onto the final rendered
    // layer. The normal depth test lets vessel tissue occlude hidden targets.
    public sealed class SutureSurfaceGuidance : IDisposable
    {
        private const int Segments = 32;
        private readonly Material material;
        private readonly Transform parent;
        private readonly Ring[] rings = new Ring[24];
        private readonly Surface outer = new Surface();
        private readonly Surface inner = new Surface();
        private sealed class Ring
        {
            public GameObject go;
            public Mesh mesh;
            public readonly Vector3[] vertices = new Vector3[Segments * 2];
            public readonly Color[] colors = new Color[Segments * 2];
        }
        private sealed class Surface
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            private Mesh mesh;
            public void Read(MeshFilter filter)
            {
                if (filter == null || filter.sharedMesh == null) { vertices.Clear(); return; }
                Mesh live = filter.sharedMesh;
                if (mesh != live || vertices.Count != live.vertexCount || triangles.Count != (int)live.GetIndexCount(0))
                    live.GetTriangles(triangles, 0);
                mesh = live;
                live.GetVertices(vertices);
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Count; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            }
        }
        private readonly List<int> patch = new List<int>();
        public SutureSurfaceGuidance(Transform owner)
        {
            parent = owner;
            material = new Material(Shader.Find("Sprites/Default")) { name = "Surface guidance", renderQueue = 3002 };
            for (int i = 0; i < rings.Length; i++)
            {
                var r = new Ring { go = new GameObject("Surface target " + i), mesh = new Mesh { name = "Conforming target ring" } };
                r.go.transform.SetParent(parent, false);
                r.go.AddComponent<MeshFilter>().sharedMesh = r.mesh;
                var renderer = r.go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                int[] indices = new int[Segments * 6];
                for (int j = 0; j < Segments; j++)
                {
                    int a = j * 2, b = ((j + 1) % Segments) * 2, k = j * 6;
                    indices[k] = a; indices[k + 1] = b; indices[k + 2] = a + 1;
                    indices[k + 3] = a + 1; indices[k + 4] = b; indices[k + 5] = b + 1;
                }
                r.mesh.vertices = r.vertices; r.mesh.triangles = indices; r.mesh.MarkDynamic();
                r.go.SetActive(false); rings[i] = r;
            }
        }
        public void Read(MeshFilter outerFilter, MeshFilter innerFilter)
        {
            outer.Read(outerFilter); inner.Read(innerFilter);
        }
        public void HideAll() { foreach (Ring r in rings) r.go.SetActive(false); }
        public bool Draw(int index, bool outerLayer, Vector3 planned, float radius, Color color, out Vector3 center, out Vector3 normal)
        {
            Ring r = rings[index]; Surface surface = outerLayer ? outer : inner;
            r.go.SetActive(false);
            if (!Project(surface, planned, null, out center, out normal) || (center - planned).sqrMagnitude > 0.03f * 0.03f) return false;
            patch.Clear();
            for (int i = 0; i < surface.triangles.Count; i += 3)
            {
                Vector3 a = surface.vertices[surface.triangles[i]], b = surface.vertices[surface.triangles[i+1]], c = surface.vertices[surface.triangles[i+2]];
                Vector3 n = Vector3.Cross(b-a,c-a).normalized;
                if (Vector3.Dot(n, normal) > 0.3f && (ClosestPoint(center,a,b,c)-center).sqrMagnitude < radius * radius * 4f) patch.Add(i);
            }
            if (patch.Count == 0) return false;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(normal,tangent);
            for (int i = 0; i < Segments; i++)
            {
                float theta = i * Mathf.PI * 2f / Segments;
                Vector3 direction = tangent * Mathf.Cos(theta) + bitangent * Mathf.Sin(theta);
                for (int edge = 0; edge < 2; edge++)
                {
                    Vector3 sample = center + direction * radius * (edge == 0 ? 0.78f : 1f);
                    if (!Project(surface,sample,patch,out Vector3 position,out Vector3 n)) return false;
                    r.vertices[i*2+edge] = parent.InverseTransformPoint(position + n * 0.00012f);
                    r.colors[i*2+edge] = color;
                }
            }
            r.mesh.vertices = r.vertices; r.mesh.colors = r.colors; r.mesh.RecalculateBounds();
            r.go.SetActive(true); return true;
        }
        private static bool Project(Surface s, Vector3 point, List<int> patch, out Vector3 hit, out Vector3 normal)
        {
            hit = normal = default;
            float best = float.PositiveInfinity;
            int count = patch != null ? patch.Count : s.triangles.Count / 3;
            for (int j=0; j<count; j++)
            {
                int i = patch != null ? patch[j] : j*3;
                Vector3 a=s.vertices[s.triangles[i]], b=s.vertices[s.triangles[i+1]], c=s.vertices[s.triangles[i+2]];
                Vector3 cross=Vector3.Cross(b-a,c-a);
                if (cross.sqrMagnitude < 1e-16f) continue;
                Vector3 closest=ClosestPoint(point,a,b,c);
                float d=(closest-point).sqrMagnitude;
                if (float.IsNaN(d) || float.IsInfinity(d) || d >= best) continue;
                best=d; hit=closest; normal=cross.normalized;
            }
            return !float.IsPositiveInfinity(best);
        }
        public static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab=b-a, ac=c-a, ap=p-a;
            float d1=Vector3.Dot(ab,ap), d2=Vector3.Dot(ac,ap);
            if(d1<=0f && d2<=0f) return a;
            Vector3 bp=p-b; float d3=Vector3.Dot(ab,bp), d4=Vector3.Dot(ac,bp);
            if(d3>=0f && d4<=d3) return b;
            float vc=d1*d4-d3*d2;
            if(vc<=0f && d1>=0f && d3<=0f) return a+ab*(d1/(d1-d3));
            Vector3 cp=p-c; float d5=Vector3.Dot(ab,cp), d6=Vector3.Dot(ac,cp);
            if(d6>=0f && d5<=d6) return c;
            float vb=d5*d2-d1*d6;
            if(vb<=0f && d2>=0f && d6<=0f) return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;
            if(va<=0f && d4-d3>=0f && d5-d6>=0f) return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
            float inv=1f/(va+vb+vc); return a+ab*(vb*inv)+ac*(vc*inv);
        }
        private static void Release(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
        public void Dispose()
        {
            foreach(Ring r in rings) { Release(r.mesh); Release(r.go); }
            Release(material);
        }
    }
}
