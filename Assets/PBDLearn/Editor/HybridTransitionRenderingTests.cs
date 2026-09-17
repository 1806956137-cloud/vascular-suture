using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDLearn
{
    public class HybridTransitionRenderingTests
    {
        private const string PrefabPath = "Assets/PBDLearn/Content/Vascular/Prefabs/HybridBranchedVessel.prefab";
        private const string RequestPath = "Temp/hybrid-transition-tests.request";
        private const string ResultPath = "Temp/hybrid-transition-tests.result.txt";

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly HybridVesselTransitionRenderer Follow;
            public readonly MeshFilter Collar;
            public readonly Mesh CollarAsset, OuterAsset;
            public Mesh Outer;

            public Fixture()
            {
                Root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                Root.hideFlags = HideFlags.HideAndDontSave;
                Root.SetActive(false);
                Root.transform.SetPositionAndRotation(new Vector3(0.4f, -0.2f, 1.1f), Quaternion.Euler(17f, 38f, -11f));
                Root.transform.localScale = new Vector3(1.2f, 0.8f, 1.4f);
                Follow = Root.GetComponentInChildren<HybridVesselTransitionRenderer>(true);
                Assert.That(Follow, Is.Not.Null, "Prefab must wire up the runtime follower.");
                Collar = Follow.GetComponent<MeshFilter>();
                CollarAsset = Collar.sharedMesh;
                OuterAsset = Follow.simulationRestMesh;
                Outer = Object.Instantiate(OuterAsset);
                Follow.simulationOuter.sharedMesh = Outer;
            }

            public void Deform()
            {
                Vector3[] v = Outer.vertices, n = Outer.normals;
                Vector2[] uv = Outer.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    float angle = uv[i].x * Mathf.PI * 2f;
                    v[i] += uv[i].y * new Vector3(0.009f + 0.004f * Mathf.Cos(angle),
                        0.012f + 0.003f * Mathf.Sin(angle), -0.006f);
                    n[i] = Quaternion.AngleAxis(12f, Vector3.forward) * n[i];
                }
                Outer.vertices = v;
                Outer.normals = n;
            }

            public void Dispose()
            {
                // Inactive test objects have not received OnEnable/OnDisable.
                Release();
                Object.DestroyImmediate(Root);
                Object.DestroyImmediate(Outer);
            }

            public void Release() => typeof(HybridVesselTransitionRenderer)
                .GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Follow, null);
        }

        [Test]
        public void RealPrefabFollowsEveryInletSampleAndKeepsBranchFixed()
        {
            using (var f = new Fixture())
            {
                Vector3[] assetBefore = f.CollarAsset.vertices;
                f.Deform();
                f.Follow.UpdatePresentation();
                AuditActualMeshes(f);
                Assert.That(f.Collar.sharedMesh, Is.Not.SameAs(f.CollarAsset));
                CollectionAssert.AreEqual(assetBefore, f.CollarAsset.vertices);
                Vector3[] v = f.Collar.sharedMesh.vertices;
                Vector2[] uv = f.CollarAsset.uv;
                float minMiddleMove = float.PositiveInfinity, maxMiddleMove = 0f;
                for (int i = 0; i < v.Length; i++)
                    if (Mathf.Abs(uv[i].y - 0.5f) < 1e-6f)
                    {
                        float move = Vector3.Distance(v[i], assetBefore[i]);
                        minMiddleMove = Mathf.Min(minMiddleMove, move);
                        maxMiddleMove = Mathf.Max(maxMiddleMove, move);
                    }
                Assert.That(minMiddleMove, Is.GreaterThan(0.004f), "Interior must actually deform.");
                Assert.That(maxMiddleMove, Is.LessThan(0.011f), "Interior must attenuate the inlet displacement.");
                AssertNoFold(f.CollarAsset, f.Collar.sharedMesh);
                // Updating the same frame repeatedly must not accumulate motion.
                for (int i = 0; i < 10; i++) f.Follow.UpdatePresentation();
                CollectionAssert.AreEqual(v, f.Collar.sharedMesh.vertices);
                f.Release();
                Assert.That(f.Collar.sharedMesh, Is.SameAs(f.CollarAsset));
                f.Outer.vertices = f.OuterAsset.vertices;
                f.Outer.normals = f.OuterAsset.normals;
                f.Follow.UpdatePresentation();
                AuditActualMeshes(f);
                var restored = f.Collar.sharedMesh.vertices;
                for (int i = 0; i < restored.Length; i++)
                    Assert.That(Vector3.Distance(assetBefore[i], restored[i]), Is.LessThan(1e-6f));
            }
        }

        [Test]
        public void ReplacedAndReindexedOuterKeepsMaterialIdentity()
        {
            using (var f = new Fixture())
            {
                f.Follow.UpdatePresentation();
                f.Deform();
                Mesh old = f.Outer;
                f.Outer = Object.Instantiate(old);
                Vector3[] v = f.Outer.vertices, n = f.Outer.normals;
                Vector2[] uv = f.Outer.uv;
                int[] triangles = f.Outer.triangles;
                Array.Reverse(v); Array.Reverse(n); Array.Reverse(uv);
                for (int i = 0; i < triangles.Length; i++) triangles[i] = v.Length - 1 - triangles[i];
                f.Outer.vertices = v; f.Outer.normals = n; f.Outer.uv = uv; f.Outer.triangles = triangles;
                f.Follow.simulationOuter.sharedMesh = f.Outer;
                Object.DestroyImmediate(old);
                f.Follow.UpdatePresentation();
                AuditActualMeshes(f);
            }
        }

        [Test]
        public void DuplicateInletIdentityFailsBeforeFollowing()
        {
            using (var f = new Fixture())
            {
                Vector2[] uv = f.Outer.uv;
                uv[0] = uv[uv.Length - 1];
                f.Outer.uv = uv;
                var failure = Assert.Throws<InvalidOperationException>(() => f.Follow.UpdatePresentation());
                StringAssert.Contains("Duplicate live Outer inlet material identity", failure.Message);
            }
        }

        private static void AuditActualMeshes(Fixture f)
        {
            var actual = f.Collar.sharedMesh.vertices;
            var normals = f.Collar.sharedMesh.normals;
            var uv = f.Collar.sharedMesh.uv;
            var outer = f.Outer.vertices;
            var outerNormals = f.Outer.normals;
            var outerUv = f.Outer.uv;
            var rest = f.CollarAsset.vertices;
            var restNormals = f.CollarAsset.normals;
            Matrix4x4 collarNormal = f.Collar.transform.localToWorldMatrix.inverse.transpose;
            Matrix4x4 outerNormal = f.Follow.simulationOuter.transform.localToWorldMatrix.inverse.transpose;
            int inlet = 0, outlet = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                if (uv[i].y == 0f)
                {
                    int j = Array.FindIndex(outerUv, x => x == new Vector2(uv[i].x, 1f));
                    Assert.That(j, Is.GreaterThanOrEqualTo(0));
                    Assert.That(Vector3.Distance(f.Collar.transform.TransformPoint(actual[i]),
                        f.Follow.simulationOuter.transform.TransformPoint(outer[j])), Is.LessThan(1e-5f));
                    Assert.That(Vector3.Distance(collarNormal.MultiplyVector(normals[i]).normalized,
                        outerNormal.MultiplyVector(outerNormals[j]).normalized), Is.LessThan(1e-5f));
                    inlet++;
                }
                if (uv[i].y == 1f)
                {
                    Assert.That(actual[i], Is.EqualTo(rest[i]));
                    Assert.That(normals[i], Is.EqualTo(restNormals[i]));
                    outlet++;
                }
            }
            Assert.That(inlet, Is.EqualTo(24));
            Assert.That(outlet, Is.EqualTo(33));
            Assert.That(f.Follow.MaxInletGap, Is.LessThan(1e-5f));
            Assert.That(f.Follow.MaxOutletDisplacement, Is.Zero);
        }

        private static void AssertNoFold(Mesh rest, Mesh live)
        {
            Vector3[] a = rest.vertices, b = live.vertices;
            int[] triangles = rest.triangles;
            CollectionAssert.AreEqual(triangles, live.triangles);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int x = triangles[i], y = triangles[i + 1], z = triangles[i + 2];
                Vector3 before = Vector3.Cross(a[y] - a[x], a[z] - a[x]);
                Vector3 after = Vector3.Cross(b[y] - b[x], b[z] - b[x]);
                Assert.That(after.magnitude, Is.GreaterThan(before.magnitude * 0.1f));
                Assert.That(Vector3.Dot(before.normalized, after.normalized), Is.GreaterThan(0.5f));
            }
        }

        [MenuItem("PBDLearn/Diagnostics/Verify Hybrid Transition")]
        public static void Verify()
        {
            var tests = new HybridTransitionRenderingTests();
            tests.RealPrefabFollowsEveryInletSampleAndKeepsBranchFixed();
            tests.ReplacedAndReindexedOuterKeepsMaterialIdentity();
            tests.DuplicateInletIdentityFailsBeforeFollowing();
            Debug.Log("[HybridTransition][Tests] PASS: real prefab; 24 inlet/33 outlet identities; " +
                "nonuniform motion and normals; transformed root; interior attenuation; no folded triangles; " +
                "no drift; source isolation; disable/reset; reindexed mesh; duplicate identity rejection.");
        }

        // Explicit opt-in edit-mode test request, also usable while another scene
        // is open. No scene loading, saving, play-mode toggling or simulation step.
        [InitializeOnLoadMethod]
        private static void CheckTestRequest()
        {
            if (!File.Exists(RequestPath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(RequestPath)) return;
                File.Delete(RequestPath);
                try { Verify(); File.WriteAllText(ResultPath, "PASS\n" + DateTime.UtcNow.ToString("O")); }
                catch (Exception exception) { File.WriteAllText(ResultPath, "FAIL\n" + exception); Debug.LogException(exception); }
            };
        }
    }
}
