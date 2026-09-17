using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDLearn
{
    public class WallFillRenderingTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly ClothSimulationDriverDoubleLayer Driver;
            public readonly Mesh Outer, Inner;
            public readonly Material Source;
            public readonly ClothSimulator Sim;

            public Fixture()
            {
                Root = new GameObject("Wall rendering test") { hideFlags = HideFlags.HideAndDontSave };
                Root.SetActive(false);
                Root.transform.SetPositionAndRotation(new Vector3(0.2f, -0.3f, 0.4f), Quaternion.Euler(21, 36, 11));
                Root.transform.localScale = new Vector3(1.3f, 0.8f, 1.1f);
                Driver = Root.AddComponent<ClothSimulationDriverDoubleLayer>();
                Outer = MakeMesh(0f);
                Inner = MakeMesh(-0.2f);
                Source = new Material(Shader.Find("Custom/Cloth"));
                Source.EnableKeyword("_INPUT_WORLD_VERTEX");
                Source.SetFloat("_Cull", 0f);
                Driver.outerMeshFilter = AddLayer("Outer", Outer);
                Driver.innerMeshFilter = AddLayer("Inner", Inner);
                Driver.middleWallMaterialOverride = Source;
                Driver.enableMiddleWallFillRender = true;
                Driver.previewMiddleWallInEditMode = true;
                Set(Driver, "_outerMesh", Outer);
                Set(Driver, "_innerMesh", Inner);
                Set(Driver, "_outerCount", 4);
                Set(Driver, "_innerCount", 4);
                Set(Driver, "_middleWallOuterToInnerLocal", new[] { 0, 1, 2, 3 });
                Sim = new ClothSimulator(ClothMeshModifier.CreateFromMeshes(Outer, Driver.outerMeshFilter.transform,
                    Inner, Driver.innerMeshFilter.transform), new ClothSetting());
                Set(Driver, "_simulator", Sim);
                Call(Driver, "BuildMiddleWallBoundaryCache");
            }

            private MeshFilter AddLayer(string name, Mesh mesh)
            {
                var go = new GameObject(name);
                go.transform.SetParent(Root.transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = Source;
                return filter;
            }

            private static Mesh MakeMesh(float z)
            {
                var mesh = new Mesh();
                mesh.vertices = new[] { new Vector3(0, 0, z), new Vector3(1, 0, z), new Vector3(1, 1, z), new Vector3(0, 1, z) };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateNormals();
                return mesh;
            }

            public void Dispose()
            {
                Call(Driver, "DestroyMiddleWallFillRenderer");
                Call(Driver, "DestroyIncisionSidewallRenderer");
                Set(Driver, "_simulator", null);
                Sim.Dispose();
                Object.DestroyImmediate(Root);
                Object.DestroyImmediate(Outer);
                Object.DestroyImmediate(Inner);
                Object.DestroyImmediate(Source);
            }
        }

        [Test]
        public void FillFollowsBothDeformedLayersWhileSourceMeshesStayAtRest()
        {
            using (var f = new Fixture())
            {
                Call(f.Driver, "UpdateMiddleWallFillMesh");
                var before = Get<Mesh>(f.Driver, "_middleWallFillMesh").vertices;
                // Deform the layers independently, leaving both MeshFilters and
                // predicted positions stale. The display must use final positions.
                var positions = f.Sim.positions;
                for (int i = 0; i < positions.Length; i++)
                    positions[i] += new Unity.Mathematics.float3(0.03f * i, 0.02f * (i % 3), 0.08f + 0.015f * i);
                Call(f.Driver, "UpdateMiddleWallFillMesh");
                Assert.That(AuditContact(f.Driver), Is.LessThan(1e-5f));
                var after = Get<Mesh>(f.Driver, "_middleWallFillMesh").vertices;
                Assert.That(Vector3.Distance(before[0], after[0]), Is.GreaterThan(0.01f));
                Assert.That(f.Outer.vertices[0], Is.EqualTo(Vector3.zero));
                Assert.That(f.Inner.vertices[0], Is.EqualTo(new Vector3(0, 0, -0.2f)));
                var renderer = Get<MeshRenderer>(f.Driver, "_middleWallFillRenderer");
                Assert.That(renderer.sharedMaterial.IsKeywordEnabled("_INPUT_WORLD_VERTEX"), Is.False);
                Assert.That(renderer.sharedMaterial.GetFloat("_Cull"), Is.EqualTo(2f));
                Assert.That(f.Source.IsKeywordEnabled("_INPUT_WORLD_VERTEX"), Is.True);
                Assert.That(f.Source.GetFloat("_Cull"), Is.EqualTo(0f));
            }
        }

        [Test]
        public void WarpedSidewallUsesSameDiagonalOnFrontAndBack()
        {
            using (var f = new Fixture())
            {
                var a = new Vector3(0, 0, 0);
                var b = new Vector3(1, 0, 0);
                var c = new Vector3(1, 1, 0.4f);
                var d = new Vector3(0, 1, -0.1f);
                Call(f.Driver, "AddIncisionSidewallQuadDoubleSided", f.Root.transform.worldToLocalMatrix,
                    a, b, c, d, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                var v = Get<List<Vector3>>(f.Driver, "_incisionSidewallVertices");
                Assert.That(v.Count, Is.EqualTo(12));
                Assert.That(v[0], Is.EqualTo(v[6]));
                Assert.That(v[1], Is.EqualTo(v[8]));
                Assert.That(v[2], Is.EqualTo(v[7]));
                Assert.That(v[3], Is.EqualTo(v[9]));
                Assert.That(v[4], Is.EqualTo(v[11]));
                Assert.That(v[5], Is.EqualTo(v[10]));
            }
        }

        // Read-only audit: compare every rendered cap endpoint to the particles
        // that also feed the outer/inner renderers, including non-identity roots.
        private static float AuditContact(ClothSimulationDriverDoubleLayer driver)
        {
            Assert.That(Get<bool>(driver, "_middleWallBoundaryValid"), Is.True);
            var edges = Get<IList>(driver, "_middleWallBoundaryEdges");
            var mesh = Get<Mesh>(driver, "_middleWallFillMesh");
            var filter = Get<MeshFilter>(driver, "_middleWallFillFilter");
            var vertices = mesh.vertices;
            var map = Get<int[]>(driver, "_middleWallOuterToInnerLocal");
            var positions = driver.Simulator.positions;
            int offset = vertices.Length - edges.Count * 12;
            Assert.That(offset, Is.GreaterThanOrEqualTo(0), "Missing boundary triangles");
            float error = 0f;
            int[] corners = { 0, 1, 2, 0, 2, 3, 0, 3, 2, 0, 2, 1 };
            for (int e = 0; e < edges.Count; e++)
            {
                int a = Get<int>(edges[e], "a"), b = Get<int>(edges[e], "b");
                int[] ids = { driver.OuterStart + a, driver.OuterStart + b, driver.InnerStart + map[b], driver.InnerStart + map[a] };
                for (int j = 0; j < 12; j++)
                {
                    var actual = filter.transform.TransformPoint(vertices[offset + e * 12 + j]);
                    error = Mathf.Max(error, Vector3.Distance(actual, (Vector3)positions[ids[corners[j]]]));
                }
            }
            Assert.That(Get<MeshRenderer>(driver, "_middleWallFillRenderer").enabled, Is.True);
            return error;
        }

        private static Vector3[] previousPositions;
        private static int previousDriver;

        [MenuItem("PBDLearn/Diagnostics/Verify Wall Fill _F8")]
        public static void Verify()
        {
            if (!Application.isPlaying)
            {
                var tests = new WallFillRenderingTests();
                tests.FillFollowsBothDeformedLayersWhileSourceMeshesStayAtRest();
                tests.WarpedSidewallUsesSameDiagonalOnFrontAndBack();
                Assert.That(ShaderUtil.ShaderHasError(Shader.Find("Custom/Cloth")), Is.False);
                Debug.Log("[MiddleWall][Tests] PASS: live final positions, independent layer deformation, transformed root, material isolation, matching two-sided diagonal, shader import.");
                return;
            }
            foreach (var driver in Object.FindObjectsOfType<ClothSimulationDriverDoubleLayer>())
            {
                if (!driver.enableMiddleWallFillRender || driver.Simulator == null) continue;
                float error = AuditContact(driver);
                Assert.That(error, Is.LessThan(1e-5f));
                var positions = driver.Simulator.positions;
                float change = 0f;
                bool comparable = previousDriver == driver.GetInstanceID() && previousPositions?.Length == positions.Length;
                for (int i = 0; comparable && i < positions.Length; i++)
                    change = Mathf.Max(change, Vector3.Distance(previousPositions[i], (Vector3)positions[i]));
                previousDriver = driver.GetInstanceID();
                previousPositions = new Vector3[positions.Length];
                for (int i = 0; i < positions.Length; i++) previousPositions[i] = positions[i];
                Debug.Log($"[MiddleWall][RuntimeAudit] PASS edges={Get<IList>(driver, "_middleWallBoundaryEdges").Count} " +
                    $"vertices={Get<Mesh>(driver, "_middleWallFillMesh").vertexCount} maxContactError={error:E3} " +
                    $"particleChange={(comparable ? change.ToString("E3") : "new scene")} timeScale={Time.timeScale}");
            }
        }
    }
}
