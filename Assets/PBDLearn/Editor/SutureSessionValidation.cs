using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PBDLearn.Tests;
using PBDLearn.UnityDrivers;
using UnityEditor;
using UnityEngine;

namespace PBDLearn.EditorTools
{
    public static class SutureSessionValidation
    {
        [Serializable]
        private sealed class CheckResult
        {
            public string timestamp;
            public int passed;
            public int failed;
            public string failures;
        }

        [Serializable]
        private sealed class RuntimeSnapshot
        {
            public string mode;
            public bool paused;
            public bool inputAllowed;
            public int appliedMotionCount;
            public bool needleSelected;
            public string tool;
            public float speed;
            public Vector3 needleForward;
            public Vector3 needleScreenA;
            public Vector3 needleScreenB;
            public bool transitionEnabled;
            public float timeScale;
            public Vector3 needleTip;
            public int completedStitches;
            public int acceptedPasses;
            public string manualError;
            public string sessionError;
            public string automaticState;
            public int portalHistoryCount;
            public string screenshot;
        }

        [MenuItem("PBDLearn/Session/Run guidance checks %#F9")]
        public static void RunGuidanceChecks()
        {
            var result = new CheckResult { timestamp = DateTime.Now.ToString("O") };
            var tests = new SutureSessionTests();
            foreach (MethodInfo method in typeof(SutureSessionTests).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!method.IsDefined(typeof(TestAttribute), false)) continue;
                try { method.Invoke(tests, null); result.passed++; }
                catch (Exception exception)
                {
                    result.failed++;
                    result.failures += method.Name + ": " + (exception.InnerException ?? exception).Message + "\n";
                }
            }
            Directory.CreateDirectory("Temp/SessionValidation");
            File.WriteAllText("Temp/SessionValidation/guidance-checks.json", JsonUtility.ToJson(result, true));
            if (result.failed == 0) Debug.Log($"[SutureSession][Checks] passed={result.passed} failed=0");
            else Debug.LogError($"[SutureSession][Checks] passed={result.passed} failed={result.failed}\n{result.failures}");
        }

        [MenuItem("PBDLearn/Session/Capture Game and state %#F10")]
        public static void CaptureGameAndState()
        {
            SutureSessionController session = UnityEngine.Object.FindObjectOfType<SutureSessionController>();
            if (!Application.isPlaying || session == null)
            {
                Debug.LogWarning("[SutureSession] Enter Play before capturing a session.");
                return;
            }
            Directory.CreateDirectory("Temp/SessionValidation");
            string prefix = "Temp/SessionValidation/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + session.Mode;
            session.Desktop.TryGetNeedleScreenSegment(out Vector3 screenA, out Vector3 screenB);
            var transition = UnityEngine.Object.FindObjectOfType<HybridVesselTransitionRenderer>();
            var result = new RuntimeSnapshot
            {
                mode = session.Mode.ToString(),
                paused = session.Paused,
                inputAllowed = session.Desktop.InputAllowed,
                appliedMotionCount = session.Desktop.AppliedMotionCount,
                needleSelected = session.Desktop.NeedleSelected,
                tool = session.Desktop.Tool.ToString(),
                speed = session.Desktop.Sensitivity,
                needleForward = session.NeedleRig.GetSemanticTipForward(),
                needleScreenA = screenA,
                needleScreenB = screenB,
                transitionEnabled = transition != null && transition.enabled,
                timeScale = Time.timeScale,
                needleTip = session.NeedleRig.GetSemanticTipPoint(),
                completedStitches = session.Guide != null ? session.Guide.CompletedStitches : session.Automatic.State == AutoSutureState.Done ? 6 : session.Automatic.CurrentPairIndex,
                acceptedPasses = session.Guide != null ? session.Guide.AcceptedPasses : 0,
                manualError = "",
                sessionError = session.SessionError,
                automaticState = session.Automatic.State.ToString(),
                portalHistoryCount = session.Automatic.driver.Simulator.portalHistoryCount,
                screenshot = prefix + ".png",
            };
            File.WriteAllText(prefix + ".json", JsonUtility.ToJson(result, true));
            ScreenCapture.CaptureScreenshot(prefix + ".png");
            Debug.Log("[SutureSession][Capture] " + prefix);
        }
    }
}
