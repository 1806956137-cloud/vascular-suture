using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PBDLearn
{
    public class AutoSutureLifecycleTests
    {
        [Test]
        public void RealContactLifecyclePolicy_IsAvailable()
        {
            Type policyType = typeof(AutoSutureRuntimeController).Assembly.GetType(
                "PBDLearn.AutoSutureLifecyclePolicy");

            Assert.That(
                policyType,
                Is.Not.Null,
                "Auto suture needs a dedicated real-contact lifecycle policy before Current/Hist transitions can drive motion.");
        }

        [Test]
        public void CurrentPair_NeverCompletesBeforeHist()
        {
            MethodInfo evaluate = typeof(AutoSutureLifecyclePolicy).GetMethod(
                "Evaluate",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(evaluate, Is.Not.Null, "Lifecycle policy must expose Evaluate.");
            object stage = evaluate.Invoke(null, new object[]
            {
                true,
                true,
                true,
                false,
                0f,
                0.01f,
            });

            Assert.That(stage.ToString(), Is.EqualTo("AdvanceUntilHist"));
        }

        [Test]
        public void HistPair_RequiresConfiguredPostHistTravel()
        {
            object stage = typeof(AutoSutureLifecyclePolicy).GetMethod("Evaluate").Invoke(null, new object[]
            {
                true,
                true,
                false,
                true,
                0.004f,
                0.01f,
            });

            Assert.That(stage.ToString(), Is.EqualTo("PostHistClearance"));
        }

        [Test]
        public void HistPair_BecomesReadyOnlyAfterExtraTravel()
        {
            object stage = typeof(AutoSutureLifecyclePolicy).GetMethod("Evaluate").Invoke(null, new object[]
            {
                true,
                true,
                false,
                true,
                0.012f,
                0.01f,
            });

            Assert.That(stage.ToString(), Is.EqualTo("Ready"));
        }

        [Test]
        public void MotionStep_DropsLongFrameBacklogInsteadOfJumping()
        {
            MethodInfo computeStep = typeof(AutoSutureLifecyclePolicy).GetMethod(
                "ComputeMotionStep",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(computeStep, Is.Not.Null, "Motion policy must expose ComputeMotionStep.");
            float step = (float)computeStep.Invoke(null, new object[] { 0.05f, 0.2f, 0.003f });

            Assert.That(step, Is.EqualTo(0.003f).Within(1e-7f));
        }

        [Test]
        public void PoseUp_StaysContinuousAcrossWorldAxisThreshold()
        {
            MethodInfo transportUp = typeof(AutoSutureLifecyclePolicy).GetMethod(
                "TransportPoseUp",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(transportUp, Is.Not.Null, "Motion needs a continuous up-frame transport.");

            Vector3 previousForward = new Vector3(0f, 0.91f, 0.414608f).normalized;
            Vector3 previousUp = Vector3.ProjectOnPlane(Vector3.up, previousForward).normalized;
            Vector3 nextForward = new Vector3(0f, 0.93f, 0.36756f).normalized;
            Vector3 nextUp = (Vector3)transportUp.Invoke(null, new object[] { previousUp, nextForward });

            Assert.That(Vector3.Dot(previousUp, nextUp), Is.GreaterThan(0f));
            Assert.That(Mathf.Abs(Vector3.Dot(nextForward, nextUp)), Is.LessThan(1e-5f));
        }

        [Test]
        public void CarrelPlan_AddsOneMidpointFillAfterEachOfThreeStaySutures()
        {
            MethodInfo buildStations = typeof(AutoSutureRuntimeController).GetMethod(
                "TryBuildCarrelStations",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(buildStations, Is.Not.Null, "Carrel plan must expose one structural station builder.");

            var stations = new List<float>();
            bool built = (bool)buildStations.Invoke(null, new object[] { stations });

            Assert.That(built, Is.True);
            Assert.That(stations.Count, Is.EqualTo(6));

            // Establish the three 120-degree stays before any sector fill.
            Assert.That(stations[0], Is.EqualTo(0f).Within(1e-7f));
            Assert.That(stations[1], Is.EqualTo(1f / 3f).Within(1e-7f));
            Assert.That(stations[2], Is.EqualTo(2f / 3f).Within(1e-7f));

            // Then place one stitch at the midpoint of each established sector.
            Assert.That(stations[3], Is.EqualTo(1f / 6f).Within(1e-7f));
            Assert.That(stations[4], Is.EqualTo(1f / 2f).Within(1e-7f));
            Assert.That(stations[5], Is.EqualTo(5f / 6f).Within(1e-7f));

            var sortedStations = new List<float>(stations);
            sortedStations.Sort();
            for (int i = 0; i < sortedStations.Count; i++)
            {
                float next = i + 1 < sortedStations.Count
                    ? sortedStations[i + 1]
                    : sortedStations[0] + 1f;
                Assert.That(next - sortedStations[i], Is.EqualTo(1f / 6f).Within(1e-7f));
            }
        }

        [Test]
        public void InterStitchTravel_UsesExteriorArcInsteadOfVesselChord()
        {
            MethodInfo buildExteriorArc = typeof(AutoSutureRuntimeController).GetMethod(
                "TryBuildExteriorArcLocal",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(buildExteriorArc, Is.Not.Null, "Inter-stitch motion needs a structural exterior-route builder.");

            const float outerRadius = 0.05f;
            const float clearance = 0.02f;
            Vector3 center = Vector3.zero;
            Vector3 start = new Vector3(0.055f, 0f, -0.01f);
            float destinationAngle = 120f * Mathf.Deg2Rad;
            Vector3 destination = new Vector3(
                Mathf.Cos(destinationAngle) * 0.055f,
                Mathf.Sin(destinationAngle) * 0.055f,
                0.01f);
            var waypoints = new List<Vector3>();
            object[] args =
            {
                center,
                start,
                destination,
                outerRadius,
                clearance,
                waypoints,
                0f,
                0f,
            };

            bool built = (bool)buildExteriorArc.Invoke(null, args);
            float cruiseRadius = (float)args[6];
            float minimumArcRadius = (float)args[7];

            Assert.That(built, Is.True);
            Assert.That(waypoints.Count, Is.GreaterThan(3));
            Assert.That(cruiseRadius, Is.GreaterThanOrEqualTo(outerRadius + clearance - 1e-6f));
            Assert.That(minimumArcRadius, Is.GreaterThanOrEqualTo(outerRadius - 1e-5f));
            Assert.That(waypoints[waypoints.Count - 1], Is.EqualTo(destination));

            Vector3 directChordMidpoint = (start + destination) * 0.5f;
            Assert.That(
                new Vector2(directChordMidpoint.x, directChordMidpoint.y).magnitude,
                Is.LessThan(outerRadius),
                "The previous straight transfer would cut through the vessel interior for this station spacing.");
        }

        [Test]
        public void InterStitchTravel_TiltedCutPlaneMaintainsExteriorClearance()
        {
            MethodInfo buildExteriorArc = typeof(AutoSutureRuntimeController).GetMethod(
                "TryBuildExteriorArcInPlane",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(buildExteriorArc, Is.Not.Null);

            const float outerRadius = 0.05f;
            const float clearance = 0.02f;
            Vector3 center = new Vector3(0.1f, -0.03f, 0.4f);
            Vector3 normal = new Vector3(0.35f, 0.12f, 0.929f).normalized;
            Vector3 basisX = Vector3.Cross(Vector3.up, normal).normalized;
            Vector3 basisY = Vector3.Cross(normal, basisX).normalized;
            Vector3 start =
                center + basisX * 0.055f - normal * 0.012f;
            float destinationAngle = 120f * Mathf.Deg2Rad;
            Vector3 destination =
                center +
                (Mathf.Cos(destinationAngle) * basisX +
                 Mathf.Sin(destinationAngle) * basisY) *
                0.055f +
                normal * 0.014f;
            var waypoints = new List<Vector3>();
            object[] args =
            {
                center,
                normal,
                start,
                destination,
                outerRadius,
                clearance,
                waypoints,
                0f,
                0f,
            };

            bool built = (bool)buildExteriorArc.Invoke(null, args);
            float cruiseRadius = (float)args[7];
            float minimumArcRadius = (float)args[8];

            Assert.That(built, Is.True);
            Assert.That(waypoints.Count, Is.GreaterThan(3));
            Assert.That(
                cruiseRadius,
                Is.GreaterThanOrEqualTo(
                    outerRadius + clearance - 1e-6f));
            Assert.That(
                minimumArcRadius,
                Is.GreaterThanOrEqualTo(outerRadius - 1e-5f));
            Assert.That(
                waypoints[waypoints.Count - 1],
                Is.EqualTo(destination));
            Assert.That(
                Vector3.Dot(waypoints[0] - center, normal),
                Is.EqualTo(-0.012f).Within(1e-5f));
        }

        [Test]
        public void TissueSafeCorridor_LiftsWholeNeedleBeforeTransitAndRotation()
        {
            MethodInfo buildCorridor = typeof(AutoSutureRuntimeController).GetMethod(
                "TryBuildTissueSafeCorridor",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(buildCorridor, Is.Not.Null);

            Vector3 freeNormal = new Vector3(0.2f, 0.95f, -0.1f).normalized;
            const float tissueSurfaceMaxProjection = 0.03f;
            const float wholeNeedleClearance = 0.085f;
            Vector3 start = freeNormal * 0.045f + Vector3.right * 0.1f;
            Vector3 destination = freeNormal * 0.052f - Vector3.right * 0.12f;
            Vector3 startForward = freeNormal;
            Vector3 destinationForward =
                (Vector3.right - freeNormal * 0.35f).normalized;
            var waypoints = new List<Vector3>();
            var forwards = new List<Vector3>();
            object[] args =
            {
                start,
                destination,
                startForward,
                destinationForward,
                freeNormal,
                tissueSurfaceMaxProjection,
                wholeNeedleClearance,
                waypoints,
                forwards,
                0f,
            };

            bool built = (bool)buildCorridor.Invoke(null, args);
            float safeProjection = (float)args[9];

            Assert.That(built, Is.True);
            Assert.That(waypoints.Count, Is.EqualTo(5));
            Assert.That(forwards.Count, Is.EqualTo(waypoints.Count));
            Assert.That(
                safeProjection,
                Is.EqualTo(
                    tissueSurfaceMaxProjection +
                    wholeNeedleClearance).Within(1e-6f));
            Assert.That(
                Vector3.Dot(waypoints[0], freeNormal),
                Is.GreaterThanOrEqualTo(safeProjection - 1e-6f));
            Assert.That(
                Vector3.Dot(waypoints[2], freeNormal),
                Is.GreaterThanOrEqualTo(safeProjection - 1e-6f));
            Assert.That(
                Mathf.Abs(Vector3.Dot(forwards[1], freeNormal)),
                Is.LessThan(1e-5f),
                "Transit posture must keep the needle parallel to the tissue safe layer.");
            Assert.That(
                waypoints[0],
                Is.EqualTo(waypoints[1]),
                "The first pose change must rotate at clearance rather than sweep near the tissue.");
            Assert.That(
                waypoints[2],
                Is.EqualTo(waypoints[3]),
                "Entry alignment must happen above the target.");
            Assert.That(waypoints[4], Is.EqualTo(destination));
        }

        [Test]
        public void TissueEndpointPose_BiasesHazardousExitTowardFreeSide()
        {
            MethodInfo biasDirection = typeof(AutoSutureRuntimeController).GetMethod(
                "BiasDirectionToTissueFreeSide",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(biasDirection, Is.Not.Null);

            Vector3 freeNormal = new Vector3(0f, 1f, 0f);
            Vector3 towardBackground = new Vector3(0.4f, -0.8f, 0.2f).normalized;
            const float minimumFreeDot = 0.35f;
            Vector3 adjusted = (Vector3)biasDirection.Invoke(
                null,
                new object[]
                {
                    towardBackground,
                    freeNormal,
                    minimumFreeDot,
                });

            Assert.That(adjusted.magnitude, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(
                Vector3.Dot(adjusted, freeNormal),
                Is.GreaterThanOrEqualTo(minimumFreeDot - 1e-6f));
            Assert.That(
                Vector3.Dot(
                    Vector3.ProjectOnPlane(adjusted, freeNormal).normalized,
                    Vector3.ProjectOnPlane(towardBackground, freeNormal).normalized),
                Is.GreaterThan(0.999f),
                "Biasing the exit must preserve the original tangential intent.");
        }

        [Test]
        public void ProcedureCamera_ObliqueFrameLooksTowardTissueAndRemainsOrthonormal()
        {
            MethodInfo buildFrame = typeof(AutoSutureRuntimeController).GetMethod(
                "BuildObliqueProcedureCameraFrame",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(buildFrame, Is.Not.Null);

            Vector3 planRight = Vector3.right;
            Vector3 planUp = Vector3.up;
            Vector3 planForward = Vector3.forward;
            Vector3 tissueFreeNormal = Vector3.up;
            object[] args =
            {
                planRight,
                planUp,
                planForward,
                tissueFreeNormal,
                30f,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
            };

            buildFrame.Invoke(null, args);
            Vector3 screenRight = (Vector3)args[5];
            Vector3 screenUp = (Vector3)args[6];
            Vector3 viewForward = (Vector3)args[7];

            Assert.That(
                Vector3.Dot(viewForward, -tissueFreeNormal),
                Is.GreaterThan(0.45f),
                "The main view must look down toward the tissue rather than remain edge-on.");
            Assert.That(
                Vector3.Dot(screenUp, tissueFreeNormal),
                Is.GreaterThan(0f),
                "Tissue free-side normal must remain visually upward.");
            Assert.That(
                Mathf.Abs(Vector3.Dot(screenRight, screenUp)),
                Is.LessThan(1e-5f));
            Assert.That(
                Mathf.Abs(Vector3.Dot(screenRight, viewForward)),
                Is.LessThan(1e-5f));
            Assert.That(
                Mathf.Abs(Vector3.Dot(screenUp, viewForward)),
                Is.LessThan(1e-5f));
            Assert.That(screenRight.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(screenUp.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(viewForward.magnitude, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void CompactCompletedSutureLabels_HidesSettledHistAndDoneLabels()
        {
            MethodInfo shouldDraw = typeof(ClothDebugGizmos).GetMethod(
                "ShouldDrawPortalLabel",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(shouldDraw, Is.Not.Null);

            var done = new NeedlePortalDisplayInfo
            {
                stitchState = (byte)NeedlePortalStitchState.Done,
            };
            var historical = new NeedlePortalDisplayInfo
            {
                stitchState = (byte)NeedlePortalStitchState.Hist,
            };
            var current = new NeedlePortalDisplayInfo
            {
                stitchState = (byte)NeedlePortalStitchState.Current,
            };

            Assert.That((bool)shouldDraw.Invoke(null, new object[] { done, true, false }), Is.False);
            Assert.That((bool)shouldDraw.Invoke(null, new object[] { historical, true, true }), Is.False);
            Assert.That((bool)shouldDraw.Invoke(null, new object[] { current, true, false }), Is.True);
            Assert.That((bool)shouldDraw.Invoke(null, new object[] { done, false, true }), Is.True);

            MethodInfo shouldDrawLine = typeof(ClothDebugGizmos).GetMethod(
                "ShouldDrawSutureLineLabel",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(shouldDrawLine, Is.Not.Null);
            Assert.That((bool)shouldDrawLine.Invoke(null, new object[] { true, false, true }), Is.False);
            Assert.That((bool)shouldDrawLine.Invoke(null, new object[] { false, false, true }), Is.True);
            Assert.That((bool)shouldDrawLine.Invoke(null, new object[] { true, true, true }), Is.True);
        }

        [Test]
        public void WorkflowAdvanceReady_UsesStableWholeWallClosureWithoutClaimingContact()
        {
            MethodInfo advanceReady = typeof(ClothSimulator).GetMethod(
                "IsSutureLineWorkflowAdvanceReadyCandidate",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo physicalReady = typeof(ClothSimulator).GetMethod(
                "IsSutureLineWorkflowReadyCandidate",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(advanceReady, Is.Not.Null);
            Assert.That(physicalReady, Is.Not.Null);

            object[] fifthStitchClosure =
            {
                0.009028f,
                0.006f,
                0.010506f,
                0.003028f,
                0.006f,
                0.004f,
                0.009631f,
            };
            Assert.That(
                (bool)advanceReady.Invoke(null, fifthStitchClosure),
                Is.True,
                "The measured P9/P10 WholeWall closure is ready for procedural advance.");

            object[] noContactEvidence =
            {
                0.009028f,
                0.006f,
                0.010506f,
                0.003028f,
                0.006f,
                0.004f,
                0.009631f,
                false,
                false,
            };
            Assert.That(
                (bool)physicalReady.Invoke(null, noContactEvidence),
                Is.False,
                "Procedural advance must not masquerade as verified sidewall contact.");
        }

        [Test]
        public void WorkflowRelease_UsesTheSameCompletionHoldAsEarlierStitches()
        {
            MethodInfo release = typeof(ClothSimulator).GetMethod(
                "ApplyAutoSutureWorkflowRelease",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(release, Is.Not.Null);

            var constraint = new SutureLineConstraintInfo
            {
                workflowAdvanceReady = 1,
                workflowAdvanceReadyFrames = 6,
                contactLatched = 0,
                restFrozen = 0,
                reserved0 = SutureLineConstraintInfo.LifecycleActive,
                currentRestDistance = 0.006f,
                targetRestDistance = 0.010506f,
            };
            object[] args = { constraint, 0.009028f };
            release.Invoke(null, args);
            constraint = (SutureLineConstraintInfo)args[0];

            Assert.That(constraint.workflowReleased, Is.EqualTo(1));
            Assert.That(constraint.contactLatched, Is.EqualTo(1));
            Assert.That(constraint.restFrozen, Is.EqualTo(1));
            Assert.That(constraint.reserved0, Is.EqualTo(SutureLineConstraintInfo.LifecycleSettledHold));
            Assert.That(constraint.completedWithoutRealSidewallContact, Is.EqualTo(1));
            Assert.That(constraint.currentRestDistance, Is.EqualTo(0.010506f).Within(1e-7f));
        }

        [Test]
        public void WorkflowRelease_ContactAndNoContactDifferOnlyByAnnotation()
        {
            var withRealContact = new SutureLineConstraintInfo
            {
                workflowAdvanceReady = 1,
                workflowAdvanceReadyFrames = 6,
                workflowReady = 1,
                reserved0 = SutureLineConstraintInfo.LifecycleActive,
                currentRestDistance = 0.010506f,
                targetRestDistance = 0.010506f,
            };
            var withoutRealContact = withRealContact;
            withoutRealContact.workflowReady = 0;

            InvokeWorkflowRelease(ref withRealContact, 0.009028f);
            InvokeWorkflowRelease(ref withoutRealContact, 0.009028f);

            Assert.That(withRealContact.completedWithoutRealSidewallContact, Is.EqualTo(0));
            Assert.That(withoutRealContact.completedWithoutRealSidewallContact, Is.EqualTo(1));

            withoutRealContact.completedWithoutRealSidewallContact = 0;
            Assert.That(withoutRealContact, Is.EqualTo(withRealContact));
        }

        [Test]
        public void WorkflowRelease_PreservesNoRealContactAnnotationWhenRepeated()
        {
            var constraint = new SutureLineConstraintInfo
            {
                workflowAdvanceReady = 1,
                workflowAdvanceReadyFrames = 6,
                workflowReady = 0,
                reserved0 = SutureLineConstraintInfo.LifecycleActive,
                currentRestDistance = 0.010506f,
                targetRestDistance = 0.010506f,
            };

            InvokeWorkflowRelease(ref constraint, 0.009028f);
            InvokeWorkflowRelease(ref constraint, 0.009028f);

            Assert.That(constraint.completedWithoutRealSidewallContact, Is.EqualTo(1));
        }

        private static void InvokeWorkflowRelease(ref SutureLineConstraintInfo constraint, float actualDistance)
        {
            MethodInfo release = typeof(ClothSimulator).GetMethod(
                "ApplyAutoSutureWorkflowRelease",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(release, Is.Not.Null);

            object[] args = { constraint, actualDistance };
            release.Invoke(null, args);
            constraint = (SutureLineConstraintInfo)args[0];
        }

        [Test]
        public void PreEntryTravelSpeed_UsesDedicatedSpeedOnlyForFirstStitch()
        {
            MethodInfo resolveSpeed = typeof(AutoSutureRuntimeController).GetMethod(
                "ResolvePreEntryTravelSpeed",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(resolveSpeed, Is.Not.Null);

            Assert.That(
                (float)resolveSpeed.Invoke(null, new object[] { 0, 0, 0.12f, 0.045f }),
                Is.EqualTo(0.12f).Within(1e-7f));
            Assert.That(
                (float)resolveSpeed.Invoke(null, new object[] { 0, 1, 0.12f, 0.045f }),
                Is.EqualTo(0.045f).Within(1e-7f));
            Assert.That(
                (float)resolveSpeed.Invoke(null, new object[] { 1, 0, 0.12f, 0.045f }),
                Is.EqualTo(0.045f).Within(1e-7f));
        }

        [Test]
        public void AutoPunctureHook_RejectsOppositeLayerCandidate()
        {
            MethodInfo layerAllowed = typeof(ClothSimulator).GetMethod(
                "IsNeedleTipHookCandidateLayerAllowed",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(layerAllowed, Is.Not.Null);

            Assert.That(
                (bool)layerAllowed.Invoke(null, new object[]
                {
                    NeedlePortalWallLayer.Outer,
                    NeedlePortalWallLayer.Inner,
                    true,
                }),
                Is.False,
                "An automatic Outer->Inner pass must not be rewritten as Inner->Outer by candidate fallback.");
            Assert.That(
                (bool)layerAllowed.Invoke(null, new object[]
                {
                    NeedlePortalWallLayer.Outer,
                    NeedlePortalWallLayer.Outer,
                    true,
                }),
                Is.True);
            Assert.That(
                (bool)layerAllowed.Invoke(null, new object[]
                {
                    NeedlePortalWallLayer.Outer,
                    NeedlePortalWallLayer.Inner,
                    false,
                }),
                Is.True,
                "Manual puncture keeps its existing layer-adaptive behavior.");
        }
    }
}
