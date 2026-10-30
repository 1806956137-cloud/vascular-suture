using System.Collections;
using PBDLearn.Presentation;
using PBDLearn.Procedures.VascularSuturing;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PBDLearn.UnityDrivers
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class SutureSessionController : MonoBehaviour
    {
        public enum SessionMode { Home, Preparing, Manual, Automatic, Failed }

        [SerializeField] private AutoSutureRuntimeController automatic;
        [SerializeField] private ClothSimulationDriverDoubleLayer simulation;
        [SerializeField] private NeedleSceneControlRig needleRig;

        private static string pendingScene;
        private static SessionMode pendingMode;
        private static bool hasPendingMode;
        private SutureSessionView view;
        private NeedleDesktopInput desktop;
        private ManualSutureGuide guide;
        private bool initialized;
        private bool reloading;
        private bool clockOwned;
        private float previousTimeScale = 1f;
        private float nextRefresh;
        private int requestedPassA;
        private int requestedPassB;
        private float pullRequestedAt;
        private string sessionError;
        private bool hasOperated;

        public SessionMode Mode { get; private set; } = SessionMode.Home;
        public bool Paused { get; private set; }
        public bool GuidanceVisible { get; private set; } = true;
        public bool OverviewVisible { get; private set; }
        public bool AdjustingPlan { get; private set; }
        public bool ModalOpen => view != null && view.ModalOpen;
        public bool Initialized => initialized;
        public AutoSutureRuntimeController Automatic => automatic;
        public NeedleSceneControlRig NeedleRig => needleRig;
        public NeedleDesktopInput Desktop => desktop;
        public ManualSutureGuide Guide => guide;
        public string SessionError => sessionError;
        public bool IsManual => Mode == SessionMode.Manual;
        public bool CanPull => IsManual && guide != null && guide.AwaitingPull &&
            !guide.PullRequested && !Paused && !ModalOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionStatics()
        {
            hasPendingMode = false;
            pendingScene = null;
        }

        private void Awake()
        {
            if (automatic == null || simulation == null || needleRig == null)
            {
                Debug.LogError("[SutureSession] Required scene bindings are missing.", this);
                enabled = false;
                return;
            }
            automatic.ConfigureSessionManaged();
            desktop = gameObject.AddComponent<NeedleDesktopInput>();
            desktop.Initialize(needleRig, automatic);
            view = gameObject.AddComponent<SutureSessionView>();
            view.Build(this);
            AcquireClockPause();
        }

        private IEnumerator Start()
        {
            // Allow the existing vessel Start to create its real runtime incision.
            yield return null;
            yield return new WaitForEndOfFrame();
            initialized = simulation.Simulator != null && simulation.HasSutureSideGuides;
            if (!initialized) sessionError = "血管场景准备失败，请检查初始化日志后重试。";
            if (hasPendingMode && pendingScene == gameObject.scene.path)
            {
                SessionMode target = pendingMode;
                hasPendingMode = false;
                if (target != SessionMode.Home) EnterMode(target);
            }
            view.Refresh();
        }

        public void EnterManual() => EnterMode(SessionMode.Manual);
        public void EnterAutomatic() => EnterMode(SessionMode.Automatic);

        private void EnterMode(SessionMode target)
        {
            if (!initialized || reloading || Mode != SessionMode.Home) return;
            Mode = SessionMode.Preparing;
            automatic.PreviewPlan();
            if (automatic.State != AutoSutureState.PreviewPlan ||
                automatic.plan == null || automatic.plan.Count != 6)
            {
                Mode = SessionMode.Failed;
                sessionError = "未能生成六针计划，请重置场景后重试。";
                view.Refresh();
                return;
            }
            guide = target == SessionMode.Manual ? new ManualSutureGuide() : null;
            Mode = target;
            if (target == SessionMode.Manual) desktop.SelectNeedle();
            else desktop.ClearSelection();
            automatic.showMarkers = target == SessionMode.Automatic;
            automatic.showTrajectoryLine = target == SessionMode.Automatic;
            automatic.showPlanOverviewCamera = OverviewVisible;
            automatic.SetSessionPresentation(true, false);
            automatic.SelectGuidancePass(0, 0);
            SetPaused(false);
            view.Refresh();
        }

        private void Update()
        {
            if (automatic == null || desktop == null) return;
            automatic.ManagedViewport = view.Viewport;
            bool working = Mode == SessionMode.Manual || Mode == SessionMode.Automatic;
            desktop.InputAllowed = IsManual && !Paused && !ModalOpen && guide != null &&
                !guide.IsComplete && !guide.AwaitingPull && string.IsNullOrEmpty(sessionError);
            automatic.SessionInputBlocked = !working || ModalOpen;
            if (working && ModalOpen && Input.GetKeyDown(KeyCode.Escape))
                view.DismissModal();
            else if (working && !ModalOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) view.OpenPauseMenu();
                else if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
            }
            if (IsManual && desktop.AppliedMotionCount > 0)
                hasOperated = true;
            if (Mode == SessionMode.Automatic && automatic.State == AutoSutureState.ErrorPaused)
                Paused = true;
        }

        private void LateUpdate()
        {
            if (automatic == null || view == null) return;
            if (IsManual && !Paused && !ModalOpen && string.IsNullOrEmpty(sessionError))
                UpdateManualEvidence();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.1f;
                view.Refresh();
            }
        }

        private void UpdateManualEvidence()
        {
            if (guide == null || guide.IsComplete) return;
            ClothSimulator simulator = simulation.Simulator;
            int colliderId = needleRig.GetTargetColliderEntityId();
            if (simulator == null || colliderId == 0) return;
            automatic.SelectGuidancePass(guide.PairIndex, guide.PassInPair);
            if (guide.AwaitingPull)
            {
                if (!guide.PullRequested) return;
                for (int i = 0; i < simulator.sutureLineDisplayCount; i++)
                {
                    SutureLineDisplayInfo line = simulator.GetSutureLineDisplayInfo(i);
                    if (line.colliderEntityId != colliderId || line.passIndexA != requestedPassA ||
                        line.passIndexB != requestedPassB || line.enabled == 0) continue;
                    if (line.workflowAdvanceReady == 0 && line.workflowReleased == 0) continue;
                    if (line.workflowReleased == 0 && !simulator.TryReleaseAutoSutureWorkflowForPassPair(
                        colliderId, requestedPassA, requestedPassB, out _, out _)) continue;
                    guide.ObserveClosure(true);
                    return;
                }
                if (Time.unscaledTime - pullRequestedAt > 30f)
                    sessionError = "拉线尚未达到下一针的推进条件。请观察针线状态，必要时重置本轮。";
                return;
            }

            var records = simulation.SuturePassSequenceRecords;
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record.colliderEntityId != colliderId || record.passIndex <= guide.LastAcceptedPass) continue;
                simulator.TryGetNeedlePassLifecycle(colliderId, record.passIndex, out NeedlePassLifecycleInfo lifecycle);
                if (!record.isComplete && lifecycle.hasHist != 0 && lifecycle.hasCurrent == 0) continue;
                guide.ObservePass(record, lifecycle);
                break;
            }
        }

        public bool TryGetTarget(int pair, int pass, out AutoSuturePass target) =>
            automatic.TryGetGuidancePass(pair, pass, out target);

        public void StartAutomatic()
        {
            if (Mode != SessionMode.Automatic || ModalOpen || automatic.IsExecuting ||
                automatic.State != AutoSutureState.PreviewPlan) return;
            SetPaused(false);
            AdjustingPlan = false;
            automatic.SetSessionPresentation(true, false);
            hasOperated = true;
            automatic.StartAutoSuture();
            view.Refresh();
        }

        public void TogglePlanEditing()
        {
            if (Mode != SessionMode.Automatic || Paused || ModalOpen ||
                automatic.State != AutoSutureState.PreviewPlan) return;
            AdjustingPlan = !AdjustingPlan;
            automatic.SetSessionPresentation(true, AdjustingPlan);
            view.Refresh();
        }

        public void PullLine()
        {
            if (!CanPull) return;
            int a = guide.PassA;
            int b = guide.PassB;
            if (!needleRig.RequestAutoSuturePullLine(a, b, false))
            {
                sessionError = "无法提交拉线请求，请检查针具连接。";
                return;
            }
            requestedPassA = a;
            requestedPassB = b;
            guide.RequestPull();
            pullRequestedAt = Time.unscaledTime;
            hasOperated = true;
            view.Refresh();
        }

        public void ToggleGuidance()
        {
            GuidanceVisible = !GuidanceVisible;
            automatic.showMarkers = !IsManual && GuidanceVisible;
            automatic.showTrajectoryLine = !IsManual && GuidanceVisible;
            view.Refresh();
        }

        public void ToggleOverview()
        {
            OverviewVisible = !OverviewVisible;
            automatic.showPlanOverviewCamera = OverviewVisible;
            view.Refresh();
        }

        public void TogglePause()
        {
            if (automatic.State == AutoSutureState.ErrorPaused) return;
            SetPaused(!Paused);
        }

        public void SetPaused(bool pause)
        {
            desktop.CancelDrag();
            Paused = pause;
            if (pause) AcquireClockPause();
            else ReleaseClockPause();
            view.Refresh();
        }

        private void AcquireClockPause()
        {
            if (!clockOwned) { previousTimeScale = Time.timeScale; clockOwned = true; }
            Time.timeScale = 0f;
        }

        private void ReleaseClockPause()
        {
            if (clockOwned) Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
            clockOwned = false;
        }

        public void RequestHome()
        {
            if (!hasOperated) { Reload(SessionMode.Home); return; }
            view.Confirm("返回主页？", "将结束本轮操作。再次进入时从初始针具与血管状态开始。", () => Reload(SessionMode.Home));
        }

        public void RequestReset() => view.Confirm("重置本轮？", "将恢复针具、血管形变、孔位与缝线状态，清除本轮进度。", () => Reload(Mode == SessionMode.Failed ? SessionMode.Home : Mode));

        private void Reload(SessionMode target)
        {
            if (reloading) return;
            reloading = true;
            hasPendingMode = true;
            pendingMode = target;
            pendingScene = gameObject.scene.path;
            desktop.InputAllowed = false;
            needleRig.ClearPendingRequests();
            // Release the session clock before unload; the new session owns its clock.
            ReleaseClockPause();
            automatic.ContinueAutoSuture();
            try
            {
#if UNITY_EDITOR
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    pendingScene, new LoadSceneParameters(LoadSceneMode.Single));
#else
                SceneManager.LoadScene(pendingScene, LoadSceneMode.Single);
#endif
            }
            catch (System.Exception exception)
            {
                hasPendingMode = false;
                reloading = false;
                sessionError = "重载失败：" + exception.Message;
                SetPaused(true);
            }
        }

        private void OnDisable()
        {
            if (desktop != null) desktop.InputAllowed = false;
            ReleaseClockPause();
        }
    }
}
