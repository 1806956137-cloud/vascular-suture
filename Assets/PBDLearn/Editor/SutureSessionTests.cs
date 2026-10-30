using NUnit.Framework;
using PBDLearn.Procedures.VascularSuturing;
using UnityEngine;

namespace PBDLearn.Tests
{
    public sealed class SutureSessionTests
    {
        [Test]
        public void AdvanceFollowsVisibleNeedleAxisAcrossCameraDirections()
        {
            Vector2[] axes = { Vector2.down, Vector2.up, Vector2.left, Vector2.right, new Vector2(3, -4) };
            foreach (Vector2 axis in axes)
            {
                Vector2 drag = axis.normalized * 80f;
                Assert.That(UnityDrivers.NeedleDesktopInput.SignedAdvancePixels(drag, axis), Is.EqualTo(80f).Within(0.001f));
                Assert.That(UnityDrivers.NeedleDesktopInput.SignedAdvancePixels(-drag, axis), Is.EqualTo(-80f).Within(0.001f));
                Vector2 across = new Vector2(-axis.y, axis.x).normalized * 80f;
                Assert.That(UnityDrivers.NeedleDesktopInput.SignedAdvancePixels(across, axis), Is.EqualTo(0f).Within(0.001f));
            }
            Assert.That(UnityDrivers.NeedleDesktopInput.SignedAdvancePixels(Vector2.down * 80, Vector2.zero), Is.EqualTo(80));
        }

        [Test]
        public void PausedSimulatorAlreadyHasGeometricNormalsBeforeFirstStep()
        {
            var go = new GameObject("Paused mesh test");
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up }, triangles = new[] { 0, 1, 2 } };
            var filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            ClothMeshModifier modifier = ClothMeshModifier.CreateFromMeshFilter(filter);
            ClothSimulator simulator = null;
            try
            {
                simulator = new ClothSimulator(modifier, new ClothSetting());
                var normals = simulator.normals;
                for (int i=0;i<3;i++) Assert.That(normals[i].z, Is.EqualTo(1f).Within(0.00001f));
            }
            finally
            {
                simulator?.Dispose(); modifier.Dispose();
                Object.DestroyImmediate(go); Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void NeedlePickingIncludesThinBodyAndRejectsBackground()
        {
            Assert.That(UnityDrivers.NeedleDesktopInput.IsNearScreenSegment(new Vector2(108, 150), new Vector2(100,100), new Vector2(100,200), 18), Is.True);
            Assert.That(UnityDrivers.NeedleDesktopInput.IsNearScreenSegment(new Vector2(150, 150), new Vector2(100,100), new Vector2(100,200), 18), Is.False);
            Assert.That(UnityDrivers.NeedleDesktopInput.IsNearScreenSegment(new Vector2(100, 210), new Vector2(100,100), new Vector2(100,200), 18), Is.True);
        }

        [Test]
        public void SurfaceRingFollowsCurrentMeshAndStaysOnItsSurface()
        {
            var go = new GameObject("Surface test");
            var filter = go.AddComponent<MeshFilter>();
            var mesh = new Mesh { vertices = new[] { new Vector3(-.1f,-.1f,0), new Vector3(.1f,-.1f,0), new Vector3(0,.1f,0) }, triangles = new[] {0,1,2} };
            filter.sharedMesh = mesh;
            var guidance = new Presentation.SutureSurfaceGuidance(go.transform);
            try
            {
                guidance.Read(filter,filter);
                Assert.That(guidance.Draw(0,true,Vector3.zero,.0045f,Color.cyan,out Vector3 center,out _),Is.True);
                Assert.That(center.z,Is.EqualTo(0).Within(1e-6f));
                Vector3[] v=mesh.vertices; for(int i=0;i<v.Length;i++) v[i].z=.01f; mesh.vertices=v;
                guidance.Read(filter,filter);
                Assert.That(guidance.Draw(0,true,new Vector3(0,0,.01f),.0045f,Color.cyan,out center,out _),Is.True);
                Assert.That(center.z,Is.EqualTo(.01f).Within(1e-6f));
                Mesh ringMesh=go.transform.Find("Surface target 0").GetComponent<MeshFilter>().sharedMesh;
                foreach(Vector3 point in ringMesh.vertices) Assert.That(point.z,Is.EqualTo(.01012f).Within(1e-6f));
            }
            finally { guidance.Dispose(); Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); }
        }

        private static ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord Pass(int id, bool firstSide)
        {
            return new ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord
            {
                passIndex = id,
                isComplete = true,
                hasOuter = true,
                hasInner = true,
                outerWorld = Vector3.zero,
                innerWorld = Vector3.up * 0.002f,
                side = firstSide ? ClothSimulationDriverDoubleLayer.SutureVesselSide.SideA : ClothSimulationDriverDoubleLayer.SutureVesselSide.SideB,
                direction = firstSide ? ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner : ClothSimulationDriverDoubleLayer.SuturePassDirection.InnerToOuter,
            };
        }

        private static bool Observe(ManualSutureGuide guide, int pass, bool firstSide, bool history = true)
        {
            var evidence = Pass(pass, firstSide);
            return guide.ObservePass(evidence,
                new NeedlePassLifecycleInfo { hasOuter = 1, hasInner = 1, hasCurrent = (byte)(history ? 0 : 1), hasHist = (byte)(history ? 1 : 0) });
        }

        [Test]
        public void CompleteWallPairMustExitNeedleBeforeAdvancing()
        {
            var guide = new ManualSutureGuide();
            Assert.IsFalse(Observe(guide, 1, true, false));
            Assert.AreEqual(0, guide.AcceptedPasses);
            Assert.AreEqual(1, guide.PendingPass);
            Assert.IsTrue(Observe(guide, 1, true));
            Assert.AreEqual(1, guide.AcceptedPasses);
        }

        [Test]
        public void IncompletePunctureDoesNotCount()
        {
            var guide = new ManualSutureGuide();
            var record = Pass(1, true);
            record.hasInner = false;
            Assert.IsFalse(guide.ObservePass(record, default));
            Assert.AreEqual(0, guide.PendingPass);
            Assert.AreEqual(0, guide.AcceptedPasses);
        }

        [Test]
        public void OffTargetPuncturesWithEitherDirectionAdvanceFromEvents()
        {
            var guide = new ManualSutureGuide();
            var record = Pass(1, false);
            record.outerWorld = Vector3.one * 10;
            record.innerWorld = Vector3.one * -10;
            var exited = new NeedlePassLifecycleInfo { hasOuter=1, hasInner=1, hasHist=1 };
            Assert.IsTrue(guide.ObservePass(record, exited));
            record.passIndex = 2;
            record.direction = ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner;
            Assert.IsTrue(guide.ObservePass(record, exited));
            Assert.AreEqual(2, guide.AcceptedPasses);
            Assert.IsTrue(guide.RequestPull());
        }

        [Test]
        public void DuplicateHistoryDoesNotAdvanceTheNextPass()
        {
            var guide = new ManualSutureGuide();
            Assert.IsTrue(Observe(guide, 1, true));
            Assert.IsFalse(Observe(guide, 1, true));
            Assert.AreEqual(1, guide.AcceptedPasses);
            Assert.IsFalse(guide.RequestPull());
        }

        [Test]
        public void TwoPuncturesNeedAnExplicitPullAndVerifiedClosure()
        {
            var guide = new ManualSutureGuide();
            Observe(guide, 1, true);
            Observe(guide, 2, false);
            Assert.IsTrue(guide.AwaitingPull);
            Assert.AreEqual(1, guide.PassA);
            Assert.AreEqual(2, guide.PassB);
            Assert.IsFalse(guide.ObserveClosure(true));
            Assert.IsTrue(guide.RequestPull());
            Assert.IsFalse(guide.RequestPull());
            Assert.IsFalse(guide.ObserveClosure(false));
            Assert.AreEqual(0, guide.CompletedStitches);
            Assert.IsTrue(guide.ObserveClosure(true));
            Assert.AreEqual(1, guide.CompletedStitches);
        }

        [Test]
        public void SixStitchesRequireTwelveRealPassesAndSixClosures()
        {
            var guide = new ManualSutureGuide();
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(i, guide.PairIndex);
                Assert.IsTrue(Observe(guide, i * 2 + 1, true));
                Assert.IsTrue(Observe(guide, i * 2 + 2, false));
                Assert.IsTrue(guide.RequestPull());
                Assert.IsTrue(guide.ObserveClosure(true));
            }
            Assert.IsTrue(guide.IsComplete);
            Assert.AreEqual(12, guide.AcceptedPasses);
            Assert.IsFalse(guide.RequestPull());
            Assert.IsFalse(Observe(guide, 13, true));
        }
    }
}
