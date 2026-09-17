using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PBDLearn
{
    public enum AutoSutureState
    {
        Idle = 0,
        PreviewPlan = 1,
        MoveToPreEntry = 2,
        AlignNeedle = 3,
        Puncture = 4,
        WaitPassComplete = 5,
        Retract = 6,
        MoveToNextPass = 7,
        PullLineAfterPair = 8,
        WaitPullLineAdvanceReady = 9,
        Done = 10,
        ErrorPaused = 11,
        AdvanceUntilHist = 12,
        PostHistClearance = 13,
    }

    [Serializable]
    public class AutoSuturePass
    {
        public string label;
        public ClothSimulationDriverDoubleLayer.SutureVesselSide side;
        public ClothSimulationDriverDoubleLayer.SuturePassDirection direction;
        public Vector3 entryPointWorld;
        public Vector3 exitPointWorld;
        public Vector3 entryDirectionWorld = Vector3.forward;
        public Vector3 exitDirectionWorld = Vector3.forward;
        public float entryPitch;
        public float entryYaw;
        public float exitPitch;
        public float exitYaw;
        public float approachDistance = 0.018f;
        public float approachSpeed = 0.045f;
        public float punctureSpeed = 0.028f;
        public float retractSpeed = 0.045f;
        [NonSerialized] public float station01;
        [NonSerialized] public bool complete;
        [NonSerialized] public int completedPassIndex;
        [NonSerialized] public SutureMaterialAnchor ownOuterAnchor;
        [NonSerialized] public SutureMaterialAnchor ownInnerAnchor;
        [NonSerialized] public SutureMaterialAnchor oppositeOuterAnchor;
        [NonSerialized] public SutureMaterialAnchor oppositeInnerAnchor;
        [NonSerialized] public bool materialAnchorsCaptured;
    }

    [Serializable]
    public class AutoSutureStitchPair
    {
        public AutoSuturePass sideAPass = new AutoSuturePass();
        public AutoSuturePass sideBPass = new AutoSuturePass();
        [NonSerialized] public bool pullLineRequested;
        [NonSerialized] public bool pullLineAdvanceReleased;
    }

    [Serializable]
    public class AutoSuturePlan
    {
        public List<AutoSutureStitchPair> stitchPairs = new List<AutoSutureStitchPair>();

        public int Count
        {
            get { return stitchPairs != null ? stitchPairs.Count : 0; }
        }
    }

    [DisallowMultipleComponent]
    public class AutoSutureRuntimeController : MonoBehaviour
    {
        private const float PullLineProgressEpsilon = 0.00005f;
        private const float PullLineDeadlineSafetyScale = 1.35f;
        private const float PullLineDeadlineGraceSeconds = 1.0f;
        private const float PullLineMinimumProgressBudgetSeconds = 2.0f;

        [Header("Refs")]
        public ClothSimulationDriverDoubleLayer driver;
        public NeedleSceneControlRig needleRig;
        public NeedleManualDriver needleDriver;
        public Transform targetNeedle;
        public Camera mainCamera;
        public Camera miniMapCamera;
        public Canvas runtimeCanvas;

        [Header("Plan")]
        public bool autoGeneratePlanOnStart = true;
        [Tooltip("非整圈切口使用的旧线性配对数量。整圈切口自动改用 Carrel 三点法。")]
        [Min(1)] public int pairCount = 4;
        [Range(0f, 0.45f)] public float endInset01 = 0.08f;
        public AutoSuturePlan plan = new AutoSuturePlan();

        [Header("Motion")]
        [Min(0.001f)] public float approachDistance = 0.018f;
        [Min(0.001f)] public float approachSpeed = 0.045f;
        [Tooltip("Move speed from the initial needle pose to the first stitch pre-entry point. This travel remains on the exterior safe route.")]
        [Min(0.001f)] public float firstStitchMoveSpeed = 0.12f;
        [Min(0.001f)] public float punctureSpeed = 0.028f;
        [Min(0.001f)] public float retractSpeed = 0.045f;
        [Tooltip("同一针内，从第一侧出针后移动到第二侧进针前的速度。")]
        [Min(0.001f)] public float betweenPassMoveSpeed = 0.05f;
        [Tooltip("上一针完成后，沿血管外周移动到下一针进针前的速度。")]
        [Min(0.001f)] public float betweenStitchMoveSpeed = 0.12f;
        [Min(0.1f)] public float passTimeout = 8f;
        [Min(0.1f)] public float pullLineTimeout = 10f;
        [Min(10f)] public float angularSpeedDeg = 360f;
        [Min(0.0001f)] public float maxAutoPoseStep = 0.003f;
        [Min(0.1f)] public float maxAutoPoseAngleStepDeg = 12f;
        [Range(8, 96)] public int puncturePathSamples = 32;
        [Min(0.0001f)] public float poseArriveDistance = 0.0008f;
        [Min(0.1f)] public float poseArriveAngleDeg = 4f;
        [Min(0f)] public float biteOffsetFromCut = 0.020f;
        public bool positionDrivenPuncture = true;
        [Min(0.05f)] public float positionDrivenPunctureSpeedMultiplier = 1f;
        [Min(0f)] public float positionPunctureOverdriveDistance = 0.035f;
        public bool clearNeedleTailAfterEachPass = true;
        public bool clearNeedleTailBeforePullLine = true;
        [Min(0f)] public float pullLineNeedleTailClearance = 0.030f;
        [Min(0f)] public float postHistPullDistance = 0.012f;
        [Min(0.001f)] public float maxPullLineNeedleClearDistance = 0.20f;
        [Tooltip("默认关闭：自动缝合按手动普通拉线的渐进强度收紧。仅调试强制硬拉时手动开启。")]
        public bool strongPullLineDrive = false;
        public bool safeTravelAroundVessel = true;
        [Min(0f)] public float safeTravelClearance = 0.07f;
        [Tooltip("整针离壁和转向时，在针长与针半径之外保留的额外净空。")]
        [Min(0f)] public float needleBodyTravelMargin = 0.005f;
        [Tooltip("Keep every non-puncture needle pose on the free side of the static tissue background.")]
        public bool avoidTissueBackground = true;
        [Tooltip("Optional static presentation mesh used as the tissue-background obstacle. Auto-resolved by the TissueSurface name.")]
        public MeshFilter tissueAvoidanceSurface;
        [Tooltip("Extra clearance above the complete tissue mesh, in addition to the full needle extent.")]
        [Min(0f)] public float tissueTravelMargin = 0.008f;
        [Tooltip("Legacy setting retained for scene compatibility. Transport avoidance no longer changes puncture endpoint directions.")]
        [Range(0.05f, 0.9f)] public float tissueEndpointFreeNormalDot = 0.35f;
        [Tooltip("同一针从 A 侧移动到 B 侧时，先把针轴转为血管纵向，再保持该朝向在腔内移动。")]
        public bool useLongitudinalLumenTravelBetweenSides = true;
        [Tooltip("只在非穿刺转运阶段检查整根针与动态血管壁、背景的净空。")]
        public bool enforceWholeNeedleClearanceDuringTravel = true;
        [Tooltip("非穿刺整针检查在真实针半径之外增加的少量余量。")]
        [Min(0f)] public float wholeNeedleTravelCollisionMargin = 0.00025f;

        [Header("UI")]
        [Tooltip("Maximize the Unity Game view on Play. Escape restores the editor layout.")]
        public bool maximizeGameViewOnPlay = true;
        public bool createRuntimeUI = true;
        [Tooltip("Scale of the procedure panel; screenshot resolution is independent of this setting.")]
        [Range(0.6f, 1f)] public float runtimePanelScale = 0.82f;
        public bool createRuntimeCameras = true;
        public bool showMarkers = true;
        public bool showTrajectoryLine = true;
        public bool allowPreviewPointEditing = true;
        public bool useOrthographicPlanCamera = true;
        [Tooltip("Tilt the main orthographic camera toward the tissue to preserve depth and background context.")]
        public bool useObliqueProcedureCamera = true;
        [Tooltip("Main-camera tilt from the exact incision-plan view toward the tissue surface.")]
        [Range(0f, 55f)] public float procedureCameraObliqueAngle = 28f;
        [Tooltip("Shift the main framing center toward the tissue so the background remains visible below the vessel.")]
        [Range(0f, 0.5f)] public float procedureCameraTissueBias = 0.16f;
        [Tooltip("Show an exact incision-plan overview in the upper-right corner while the main view stays oblique.")]
        public bool showPlanOverviewCamera = true;
        [Min(0.01f)] public float planCameraPadding = 1.35f;
        [Min(0.01f)] public float planCameraDistanceScale = 3f;
        [Min(1f)] public float markerPickRadiusPixels = 28f;

        [Header("Game View Scene-like Camera Control")]
        public bool enableSceneLikeCameraControl = true;
        [Tooltip("Right mouse drag orbits around the current observation pivot.")]
        [Range(0.05f, 1f)] public float cameraOrbitDegreesPerPixel = 0.20f;
        [Tooltip("Middle mouse drag pans both the camera and observation pivot.")]
        [Range(0.1f, 3f)] public float cameraPanSensitivity = 1f;
        [Tooltip("Mouse wheel zoom response.")]
        [Range(0.02f, 0.5f)] public float cameraZoomSensitivity = 0.14f;
        [Range(0.02f, 0.8f)] public float cameraMinimumZoomFactor = 0.12f;
        [Range(1f, 12f)] public float cameraMaximumZoomFactor = 6f;
        public KeyCode cameraFocusCurrentKey = KeyCode.F;
        public KeyCode cameraFrameWholeKey = KeyCode.G;
        public KeyCode cameraRestoreAutoKey = KeyCode.R;

        [Header("Runtime")]
        [SerializeField] private AutoSutureState _state = AutoSutureState.Idle;
        [SerializeField] private int _currentPairIndex;
        [SerializeField] private int _currentPassInPair;
        [SerializeField] private string _errorReason = string.Empty;

        private readonly List<GameObject> _markerObjects = new List<GameObject>();
        private readonly List<Renderer> _markerRenderers = new List<Renderer>();
        private LineRenderer _trajectoryLine;
        private LineRenderer _miniMapLineA;
        private LineRenderer _miniMapLineB;
        private Text _statusText;
        private Text _workflowText;
        private Text _hintText;
        // Runtime UnityEvent listeners are lost on domain reload. Rebuild the
        // panel along with its callbacks instead of retaining a dead UI shell.
        [NonSerialized] private RectTransform _runtimePanel;
        private string _screenshotNotice;
        private float _screenshotNoticeUntil;
        private const int ScreenshotWidth = 1920;
        private const int ScreenshotHeight = 1080;
        private const int ScreenshotDpi = 300;
        private bool _paused;
        private bool _ownsTimePause;
        private float _timeScaleBeforePause = 1f;
        private bool _resetRequested;
        private bool _stepRequested;
        private float _stateElapsed;
        private float _passStartTime;
        private float _pullLineStartTime;
        private float _pullLineDeadlineTime;
        private float _lastPullLineRestDistance = float.PositiveInfinity;
        private float _lastPullLineProgressTime;
        private int _passStartPassCount;
        private int _expectedPullPassA;
        private int _expectedPullPassB;
        private int _safeTravelPhase = -1;
        private AutoSutureState _safeTravelState = AutoSutureState.Idle;
        private Vector3 _safeTravelDestination;
        private Vector3 _safeTravelLiftTarget;
        private Vector3 _safeTravelCruiseTarget;
        private int _needleBodyDeparturePhase = -1;
        private AutoSutureState _needleBodyDepartureState = AutoSutureState.Idle;
        private Vector3 _needleBodyDepartureDestination;
        private Vector3 _needleBodyDepartureTarget;
        private Vector3 _needleBodyDepartureSafeLayerTarget;
        private Vector3 _needleBodyDepartureForward = Vector3.forward;
        private Vector3 _needleBodyDepartureTargetForward = Vector3.forward;
        private bool _needleBodyDepartureHasSafeLayer;
        private int _lumenTravelPhase = -1;
        private Vector3 _lumenTravelAxis = Vector3.forward;
        private Vector3 _lumenTravelDestination;
        private Vector3 _lumenTravelEntryDirection = Vector3.forward;
        private readonly List<Vector3> _currentInnerSideA = new List<Vector3>(), _currentInnerSideB = new List<Vector3>();
        private readonly List<Vector3> _lumenWaypoints = new List<Vector3>(), _lumenForwards = new List<Vector3>();
        private readonly List<int> _lumenPhases = new List<int>();
        private int _lumenWaypointIndex;
        private bool _lumenAlignmentValidated;
        private bool _lumenLocalAdjustmentActive;
        private readonly HashSet<int> _wholeNeedleCollisionCandidates = new HashSet<int>();
        private readonly List<Vector3> _exteriorTravelWaypoints = new List<Vector3>();
        private readonly List<Vector3> _exteriorTravelForwards = new List<Vector3>();
        private int _exteriorTravelWaypointIndex;
        private readonly List<Vector3> _currentExteriorSideA = new List<Vector3>();
        private readonly List<Vector3> _currentExteriorSideB = new List<Vector3>();
        private Vector3 _exteriorCenter, _exteriorAxis, _exteriorRouteCenter, _exteriorRouteAxis;
        private Vector3 _exteriorRouteForward;
        private float _exteriorRadius, _exteriorRouteRadius;
        private AutoSuturePass _exteriorFramePass;
        private bool _exteriorAlignmentValidated;
        private TissueSurfaceSnapshot _localTissueSurface;
        private Mesh _localTissueMesh;
        private Matrix4x4 _localTissueTransform;
        private int _localTissueRevision, _exteriorTissueRevision;
        private bool _tissueSafetyFrameCached;
        private TissueSafetyFrame _tissueSafetyFrame;
        private float _lastAutoPullLineReadyLogTime = -999f;
        private Vector3 _punctureStartTip;
        private Vector3 _puncturePostExit;
        private Vector3[] _puncturePathPoints = Array.Empty<Vector3>();
        private Vector3[] _puncturePathForwards = Array.Empty<Vector3>();
        private float[] _puncturePathCumulative = Array.Empty<float>();
        private float _puncturePathLength;
        private float _punctureTravelDistance;
        private float _lifecycleStartTime;
        private float _postHistTravel;
        private Vector3 _postHistForward;
        public bool logExitContactHandoff = true;
        private VesselSurfaceSnapshot _lastCurrentExitSurface;
        private Vector3 _lastCurrentExitTip, _lastCurrentExitForward;
        private NeedlePortalInfo _lastCurrentExitPortal;
        private int _lastCurrentExitPass, _lastCurrentExitFrame;
        private bool _postHistContactAudited;
        private ExitChannelContactSet _postHistChannelContacts;
        private float _lastLifecycleLogTime = -999f;
        private Vector3 _activeExitDirection = Vector3.forward;
        private Vector3 _lastMotionUp = Vector3.up;
        private bool _draggingPlanMarker;
        private int _dragPairIndex = -1;
        private int _dragPassInPair = -1;
        private Plane _dragPlane;
        private bool _manualCameraControl;
        private bool _cameraOrbitDragging;
        private bool _cameraPanDragging;
        private bool _manualCameraFrameInitialized;
        private bool _hasAutomaticCameraPivot;
        private Vector3 _lastCameraPointer;
        private Vector3 _manualCameraPivot;
        private Vector3 _automaticCameraPivot;
        private float _manualCameraFrameSize = 1f;

        private struct ClosedLoopSamplingFrame
        {
            public float phase01;
            public float direction;
        }
        private struct TissueSafetyFrame
        {
            public Vector3 freeNormal;
            public float surfaceMinProjection;
            public float surfaceMaxProjection;
            public float vesselMaxProjection;
            public float obstacleMaxProjection;
            public int vertexCount;
        }
        private Vector3 _dragStartWorld;
        private Vector3 _dragStartEntry;
        private Vector3 _dragStartExit;
        private Font _uiFont;
        private Material _lineMaterial;
        private Material _markerMaterial;
        private const float MarkerScale = 0.0035f;
        private const float DefaultAutoSutureBiteOffsetFromCut = 0.020f;
        private const float MaxAutoSuturePositionPunctureSpeedMultiplier = 1f;
        private const float AutoPullLineNeedleTailClearanceFloor = 0.030f;
        private const float AutoPullLineNeedleClearDistanceCapFloor = 0.20f;

        private struct AutoPullLinePassReadiness
        {
            public int passIndex;
            public int historyEntries;
            public bool hasOuter;
            public bool hasInner;
            public bool hasActive;
            public bool hasHist;
            public bool hasDone;

            public bool IsComplete
            {
                get { return hasOuter && hasInner; }
            }
        }

        public AutoSutureState State
        {
            get { return _state; }
        }

        public string FocusDebugStatusText
        {
            get
            {
                AutoSuturePass pass = GetCurrentPass();
                int pairCount = plan != null ? plan.Count : 0;
                int totalPasses = pairCount * 2;
                int currentPassNumber = totalPasses > 0
                    ? GetFlatPassIndex(_currentPairIndex, _currentPassInPair) + 1
                    : 0;
                string side = pass != null ? GetSideLabel(pass.side) : "-";
                string direction = pass != null ? GetDirectionLabel(pass.direction) : "-";
                string pause = _paused ? "  PAUSED" : string.Empty;
                string route = GetFocusRouteStageLabel();
                string text =
                    $"AUTO SUTURE  {_state}{pause}\n" +
                    $"Pass {currentPassNumber}/{totalPasses}   Pair {_currentPairIndex + 1}/{Mathf.Max(1, pairCount)}   {side} {direction}";
                if (!string.IsNullOrEmpty(route))
                    text += $"\nRoute  {route}";
                if (!string.IsNullOrEmpty(_errorReason))
                    text += $"\nError  {_errorReason}";
                return text;
            }
        }

        private string GetFocusRouteStageLabel()
        {
            if (_state != AutoSutureState.MoveToNextPass ||
                _currentPassInPair != 1 ||
                _lumenTravelPhase < 0)
            {
                return string.Empty;
            }

            switch (_lumenTravelPhase)
            {
                case 0: return "MoveToLumenTurnSite";
                case 1: return "TurnAboutNeedleCenterToAxis";
                case 2: return "LumenAxialTransit";
                case 3: return "TurnAboutNeedleCenterForSideB";
                case 4: return "CoaxialApproachSideB";
                default: return string.Empty;
            }
        }

        private System.Collections.IEnumerator Start()
        {
            ResolveRefs();
            EnsureRuntimeSystems();
            // The vessel builds its runtime incision/guides in Start. Unity does
            // not order different components' Start calls, including after Reset.
            // Wait for that initialization without changing simulation Update order.
            yield return null;
            if (autoGeneratePlanOnStart)
                PreviewPlan();
            else
                SetState(AutoSutureState.Idle);
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Escape))
                RestoreEditorGameView();
#endif
            ResolveRefs();
            EnsureRuntimeSystems();
            UpdateCameraRig();
            HandlePreviewPointEditing();
            UpdateRuntimeUI();
            UpdateMarkers();
            UpdateTrajectoryLine();
            if (Input.GetKeyDown(KeyCode.F12))
                CapturePatentScreenshot();

            if (_state == AutoSutureState.Idle ||
                _state == AutoSutureState.PreviewPlan ||
                _state == AutoSutureState.Done ||
                _state == AutoSutureState.ErrorPaused)
                return;

            if ((_paused && !_stepRequested) || Time.timeScale == 0f)
                return;

            float dt = Time.deltaTime;
            _stateElapsed += dt;
            TickState(dt);
            _stepRequested = false;
        }

#if UNITY_EDITOR
        private static UnityEditor.EditorWindow s_playGameView;
        private static bool s_gameViewWasMaximized;

        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterPlayGameView()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
        }

        private static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
                UnityEditor.EditorApplication.delayCall += MaximizeEditorGameView;
            else if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                UnityEditor.EditorApplication.delayCall -= MaximizeEditorGameView;
                RestoreEditorGameView(restorePrevious: true);
            }
        }

        private static void MaximizeEditorGameView()
        {
            if (!UnityEditor.EditorApplication.isPlaying)
                return;
            var controller = FindObjectOfType<AutoSutureRuntimeController>();
            if (controller == null || !controller.maximizeGameViewOnPlay)
                return;

            Type gameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
                return;
            s_playGameView = UnityEditor.EditorWindow.GetWindow(gameViewType);
            s_gameViewWasMaximized = s_playGameView.maximized;
            s_playGameView.maximized = true;
            s_playGameView.Focus();
        }

        private static void RestoreEditorGameView(bool restorePrevious = false)
        {
            if (s_playGameView == null)
                return;
            s_playGameView.maximized = restorePrevious && s_gameViewWasMaximized;
            s_playGameView = null;
        }
#endif

        [ContextMenu("Preview Plan")]
        public void PreviewPlan()
        {
            ClearRuntimeCompletion();
            if (!GeneratePlanFromGuides())
            {
                Error("No Suture Side Guide");
                return;
            }

            _currentPairIndex = 0;
            _currentPassInPair = 0;
            SetState(AutoSutureState.PreviewPlan);
        }

        [ContextMenu("Start Auto Suture")]
        public void StartAutoSuture()
        {
            ClearRuntimeCompletion();
            if (!ResolveRefs())
            {
                Error("Missing driver or needle rig");
                return;
            }

            _lastMotionUp = needleRig != null
                ? NormalizeOrFallback(needleRig.GetSemanticUp(), Vector3.up)
                : Vector3.up;

            if (plan == null || plan.Count == 0)
            {
                if (!GeneratePlanFromGuides())
                {
                    Error("No Suture Side Guide");
                    return;
                }
            }
            else if (!EnsurePlanMaterialAnchors(out string anchorReason))
            {
                Error($"MaterialAnchor capture failed: {anchorReason}");
                return;
            }

            ResumeSimulationClock();
            _paused = false;
            _currentPairIndex = 0;
            _currentPassInPair = 0;
            EnterMoveToPreEntry();
        }

        public void PauseAutoSuture()
        {
            if (!_ownsTimePause)
            {
                _timeScaleBeforePause = Time.timeScale;
                _ownsTimePause = true;
            }
            Time.timeScale = 0f;
            _paused = true;
            // Hold the observation framing, but keep mouse/keyboard camera input alive.
            if (mainCamera != null && enableSceneLikeCameraControl)
                BeginManualCameraControl();
        }

        public void ContinueAutoSuture()
        {
            if (_state != AutoSutureState.ErrorPaused)
            {
                ResumeSimulationClock();
                _paused = false;
            }
        }

        private void ResumeSimulationClock()
        {
            if (!_ownsTimePause)
                return;

            Time.timeScale = _timeScaleBeforePause;
            _ownsTimePause = false;
        }

        private void OnDisable()
        {
            // A scene unload or exiting Play must never leave Unity's clock frozen.
            ResumeSimulationClock();
            _paused = false;
        }

        public void StepAutoSuture()
        {
            _stepRequested = true;
        }

        public void PreviousPreviewPoint()
        {
            SelectPreviewPoint(-1);
        }

        public void NextPreviewPoint()
        {
            SelectPreviewPoint(1);
        }

        public void ResetAutoSuture()
        {
            if (!Application.isPlaying || _resetRequested)
                return;

            Scene scene = gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                Error("Reset requires a saved procedure scene.");
                return;
            }

            // Reload the procedure, including the original meshes, needle rig,
            // simulator velocities, portals, and all accumulated suture constraints.
            // Clearing only the workflow or rebuilding from deformed meshes cannot
            // recover the state before the first puncture.
            PauseAutoSuture();
            _resetRequested = true;
            if (needleRig != null)
                needleRig.ClearPendingRequests();
            Debug.Log($"[AutoSuture][Reset] restoring needle + vessel from scene={scene.path}");
            try
            {
#if UNITY_EDITOR
                // Editor Play scenes need not be listed in Build Settings.
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
                SceneManager.LoadScene(scene.path, LoadSceneMode.Single);
#endif
            }
            catch (Exception exception)
            {
                _resetRequested = false;
                Error($"Reset failed: {exception.Message}");
            }
        }

        public bool GeneratePlanFromGuides()
        {
            return GeneratePlanFromGuidesInternal(true);
        }

        /// <summary>
        /// Builds only the deterministic stitch-hole geometry for editor
        /// diagnostics. Runtime execution must use GeneratePlanFromGuides so
        /// every hole is backed by material anchors.
        /// </summary>
        public bool GeneratePlanGeometryPreviewFromGuides()
        {
            return GeneratePlanFromGuidesInternal(false);
        }

        private bool GeneratePlanFromGuidesInternal(
            bool requireMaterialAnchors)
        {
            ResolveRefs();
            if (driver == null || !driver.HasSutureSideGuides)
                return false;

            var sideAOuter = driver.SutureSideAOuterBoundaryWorld;
            var sideAInner = driver.SutureSideAInnerBoundaryWorld;
            var sideBOuter = driver.SutureSideBOuterBoundaryWorld;
            var sideBInner = driver.SutureSideBInnerBoundaryWorld;
            if (!HasPolyline(sideAOuter) || !HasPolyline(sideBOuter))
                return false;

            if (plan == null)
                plan = new AutoSuturePlan();
            plan.stitchPairs.Clear();

            bool carrelMode = driver.CurrentSutureSideGuideMode ==
                ClothSimulationDriverDoubleLayer.SutureSideGuideMode.CircumferentialProximalDistal;
            var stations = new List<float>();
            ClosedLoopSamplingFrame sideAOuterFrame = default;
            ClosedLoopSamplingFrame sideAInnerFrame = default;
            ClosedLoopSamplingFrame sideBOuterFrame = default;
            ClosedLoopSamplingFrame sideBInnerFrame = default;
            Transform vesselTransform = driver.outerMeshFilter != null
                ? driver.outerMeshFilter.transform
                : driver.transform;

            if (carrelMode)
            {
                if (!TryBuildCarrelStations(stations) ||
                    !TryBuildClosedLoopSamplingFrame(sideAOuter, vesselTransform, out sideAOuterFrame) ||
                    !TryBuildClosedLoopSamplingFrame(sideAInner, vesselTransform, out sideAInnerFrame) ||
                    !TryBuildClosedLoopSamplingFrame(sideBOuter, vesselTransform, out sideBOuterFrame) ||
                    !TryBuildClosedLoopSamplingFrame(sideBInner, vesselTransform, out sideBInnerFrame))
                {
                    Debug.LogWarning("[AutoSuture][CarrelPlan] failed: circumferential guides are not valid closed Outer/Inner loops");
                    return false;
                }
            }
            else
            {
                int count = Mathf.Max(1, pairCount);
                for (int i = 0; i < count; i++)
                {
                    stations.Add(count == 1
                        ? 0.5f
                        : Mathf.Lerp(endInset01, 1f - endInset01, i / Mathf.Max(1f, count - 1f)));
                }
            }

            int anchorMismatchCount = 0;
            float maxAnchorCaptureDistance = 0f;
            var anchorFailurePreview = new List<string>();
            for (int i = 0; i < stations.Count; i++)
            {
                float t = stations[i];
                Vector3 sideAOuterPoint = carrelMode
                    ? SampleCanonicalClosedLoop(sideAOuter, sideAOuterFrame, t)
                    : SamplePolyline(sideAOuter, t);
                Vector3 sideBOuterPoint = carrelMode
                    ? SampleCanonicalClosedLoop(sideBOuter, sideBOuterFrame, t)
                    : SamplePolyline(sideBOuter, t);
                Vector3 sideAInnerPoint = carrelMode
                    ? SampleCanonicalClosedLoop(sideAInner, sideAInnerFrame, t)
                    : SampleInnerOrFallback(sideAInner, sideAOuter, sideBOuter, t);
                Vector3 sideBInnerPoint = carrelMode
                    ? SampleCanonicalClosedLoop(sideBInner, sideBInnerFrame, t)
                    : SampleInnerOrFallback(sideBInner, sideBOuter, sideAOuter, t);
                float biteOffset = GetEffectiveBiteOffsetFromCut();

                AutoSutureStitchPair pair = new AutoSutureStitchPair();
                ConfigurePass(
                    pair.sideAPass,
                    "SideA",
                    ClothSimulationDriverDoubleLayer.SutureVesselSide.SideA,
                    ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner,
                    OffsetAwayFromOpposite(sideAOuterPoint, sideBOuterPoint, biteOffset),
                    OffsetAwayFromOpposite(sideAInnerPoint, sideBInnerPoint, biteOffset),
                    approachDistance,
                    approachSpeed,
                    punctureSpeed,
                    retractSpeed);
                pair.sideAPass.station01 = t;
                if (requireMaterialAnchors &&
                    !TryCapturePassMaterialAnchors(
                        pair.sideAPass,
                        sideAOuterPoint,
                        sideAInnerPoint,
                        sideBOuterPoint,
                        sideBInnerPoint,
                        out string sideAAnchorReason))
                {
                    anchorMismatchCount++;
                    if (anchorFailurePreview.Count < 6)
                        anchorFailurePreview.Add($"pair={i} pass=0 {sideAAnchorReason}");
                }
                else if (requireMaterialAnchors)
                {
                    maxAnchorCaptureDistance = Mathf.Max(
                        maxAnchorCaptureDistance,
                        GetMaxAnchorCaptureDistance(pair.sideAPass));
                }
                ConfigurePass(
                    pair.sideBPass,
                    "SideB",
                    ClothSimulationDriverDoubleLayer.SutureVesselSide.SideB,
                    ClothSimulationDriverDoubleLayer.SuturePassDirection.InnerToOuter,
                    OffsetAwayFromOpposite(sideBInnerPoint, sideAInnerPoint, biteOffset),
                    OffsetAwayFromOpposite(sideBOuterPoint, sideAOuterPoint, biteOffset),
                    approachDistance,
                    approachSpeed,
                    punctureSpeed,
                    retractSpeed);
                pair.sideBPass.station01 = t;
                if (requireMaterialAnchors &&
                    !TryCapturePassMaterialAnchors(
                        pair.sideBPass,
                        sideBOuterPoint,
                        sideBInnerPoint,
                        sideAOuterPoint,
                        sideAInnerPoint,
                        out string sideBAnchorReason))
                {
                    anchorMismatchCount++;
                    if (anchorFailurePreview.Count < 6)
                        anchorFailurePreview.Add($"pair={i} pass=1 {sideBAnchorReason}");
                }
                else if (requireMaterialAnchors)
                {
                    maxAnchorCaptureDistance = Mathf.Max(
                        maxAnchorCaptureDistance,
                        GetMaxAnchorCaptureDistance(pair.sideBPass));
                }
                plan.stitchPairs.Add(pair);
            }

            Debug.Log(
                $"[AutoSuture][MaterialAnchorScan] mode={(requireMaterialAnchors ? "runtime-required" : "geometry-preview")} " +
                $"passes={stations.Count * 2} " +
                $"captured={(requireMaterialAnchors ? stations.Count * 2 - anchorMismatchCount : 0)} " +
                $"mism={anchorMismatchCount} " +
                $"maxCaptureDistance={maxAnchorCaptureDistance:F6} " +
                $"preview={(anchorFailurePreview.Count > 0 ? string.Join(" | ", anchorFailurePreview) : "none")}");
            if (requireMaterialAnchors &&
                anchorMismatchCount > 0)
            {
                plan.stitchPairs.Clear();
                return false;
            }

            if (carrelMode)
            {
                Debug.Log(
                    $"[AutoSuture][CarrelPlan] anchors=3 sectorFills=3 totalPairs={plan.stitchPairs.Count} " +
                    $"anchorClock=11 phaseA={sideAOuterFrame.phase01:F4} " +
                    $"phaseB={sideBOuterFrame.phase01:F4}");
            }

            return true;
        }

        private static bool TryBuildCarrelStations(List<float> stations)
        {
            if (stations == null)
                return false;

            stations.Clear();
            const int sectorCount = 3;

            // Three stay sutures first, 120 degrees apart.
            for (int sector = 0; sector < sectorCount; sector++)
                stations.Add(sector / (float)sectorCount);

            // After all stays have established the three sectors, add one
            // midpoint stitch to each sector. Keep fills after the stays so
            // execution preserves the Carrel load-establishing sequence.
            for (int sector = 0; sector < sectorCount; sector++)
                stations.Add((sector + 0.5f) / sectorCount);

            return stations.Count == 6;
        }

        private static bool TryBuildClosedLoopSamplingFrame(
            IReadOnlyList<Vector3> points,
            Transform vesselTransform,
            out ClosedLoopSamplingFrame frame)
        {
            frame = default;
            int count = GetClosedLoopUniquePointCount(points);
            if (count < 3 || vesselTransform == null)
                return false;

            Vector3 centerWorld = Vector3.zero;
            for (int i = 0; i < count; i++)
                centerWorld += points[i];
            centerWorld /= count;

            Vector3 areaVector = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                Vector3 current = points[i] - centerWorld;
                Vector3 next = points[(i + 1) % count] - centerWorld;
                areaVector += Vector3.Cross(current, next);
            }
            if (areaVector.sqrMagnitude <= 1e-12f)
                return false;

            Vector3 planeNormal = areaVector.normalized;
            Vector3 referenceForward =
                vesselTransform.TransformDirection(Vector3.forward);
            if (referenceForward.sqrMagnitude > 1e-12f &&
                Vector3.Dot(planeNormal, referenceForward) < 0f)
                planeNormal = -planeNormal;

            Vector3 upAxis = Vector3.ProjectOnPlane(
                vesselTransform.TransformDirection(Vector3.up),
                planeNormal);
            if (upAxis.sqrMagnitude <= 1e-12f)
            {
                upAxis = Vector3.ProjectOnPlane(
                    vesselTransform.TransformDirection(Vector3.right),
                    planeNormal);
            }
            if (upAxis.sqrMagnitude <= 1e-12f)
                return false;
            upAxis.Normalize();
            Vector3 rightAxis =
                Vector3.Cross(upAxis, planeNormal).normalized;
            Vector3 elevenOClock =
                (-0.5f * rightAxis + 0.8660254f * upAxis).normalized;

            float totalLength = 0f;
            int anchorIndex = -1;
            float bestAnchorDot = float.NegativeInfinity;
            var cumulative = new float[count];

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                cumulative[i] = totalLength;
                totalLength += Vector3.Distance(points[i], points[next]);

                Vector3 radial = Vector3.ProjectOnPlane(
                    points[i] - centerWorld,
                    planeNormal);
                if (radial.sqrMagnitude <= 1e-12f)
                    continue;
                float anchorDot =
                    Vector3.Dot(radial.normalized, elevenOClock);
                if (anchorDot > bestAnchorDot)
                {
                    bestAnchorDot = anchorDot;
                    anchorIndex = i;
                }
            }

            if (totalLength <= 1e-6f || anchorIndex < 0)
                return false;

            frame.phase01 = cumulative[anchorIndex] / totalLength;
            frame.direction =
                Vector3.Dot(areaVector, planeNormal) >= 0f
                    ? 1f
                    : -1f;
            return true;
        }

        private static Vector3 SampleCanonicalClosedLoop(
            IReadOnlyList<Vector3> points,
            ClosedLoopSamplingFrame frame,
            float canonicalT)
        {
            return SampleClosedPolyline(points, Mathf.Repeat(frame.phase01 + frame.direction * canonicalT, 1f));
        }

        private static int GetClosedLoopUniquePointCount(IReadOnlyList<Vector3> points)
        {
            if (points == null)
                return 0;
            int count = points.Count;
            if (count > 1 && Vector3.SqrMagnitude(points[0] - points[count - 1]) <= 1e-12f)
                count--;
            return count;
        }

        private static Vector3 SampleClosedPolyline(IReadOnlyList<Vector3> points, float t)
        {
            int count = GetClosedLoopUniquePointCount(points);
            if (count <= 0)
                return Vector3.zero;
            if (count == 1)
                return points[0];

            float total = 0f;
            for (int i = 0; i < count; i++)
                total += Vector3.Distance(points[i], points[(i + 1) % count]);
            if (total <= 1e-8f)
                return points[0];

            float target = Mathf.Repeat(t, 1f) * total;
            float cursor = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[(i + 1) % count];
                float segment = Vector3.Distance(a, b);
                if (cursor + segment >= target)
                {
                    float u = segment > 1e-8f ? (target - cursor) / segment : 0f;
                    return Vector3.Lerp(a, b, u);
                }
                cursor += segment;
            }

            return points[0];
        }

        private static void ConfigurePass(
            AutoSuturePass pass,
            string label,
            ClothSimulationDriverDoubleLayer.SutureVesselSide side,
            ClothSimulationDriverDoubleLayer.SuturePassDirection direction,
            Vector3 entry,
            Vector3 exit,
            float approachDistance,
            float approachSpeed,
            float punctureSpeed,
            float retractSpeed)
        {
            pass.label = label;
            pass.side = side;
            pass.direction = direction;
            pass.entryPointWorld = entry;
            pass.exitPointWorld = exit;
            Vector3 dir = exit - entry;
            if (dir.sqrMagnitude < 1e-10f)
                dir = direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.InnerToOuter
                    ? Vector3.up
                    : Vector3.down;
            dir.Normalize();
            pass.entryDirectionWorld = dir;
            pass.exitDirectionWorld = dir;
            pass.approachDistance = approachDistance;
            pass.approachSpeed = approachSpeed;
            pass.punctureSpeed = punctureSpeed;
            pass.retractSpeed = retractSpeed;
            pass.complete = false;
            pass.completedPassIndex = 0;
        }

        private bool TryCapturePassMaterialAnchors(
            AutoSuturePass pass,
            Vector3 ownOuterPoint,
            Vector3 ownInnerPoint,
            Vector3 oppositeOuterPoint,
            Vector3 oppositeInnerPoint,
            out string reason)
        {
            reason = string.Empty;
            ClothSimulator simulator = driver != null ? driver.Simulator : null;
            if (pass == null || simulator == null)
            {
                reason = pass == null ? "pass=null" : "simulator=null";
                return false;
            }

            byte ownSide = pass.side == ClothSimulationDriverDoubleLayer.SutureVesselSide.SideA
                ? SutureSidewallBoundaryEdgeInfo.SideA
                : SutureSidewallBoundaryEdgeInfo.SideB;
            byte oppositeSide = ownSide == SutureSidewallBoundaryEdgeInfo.SideA
                ? SutureSidewallBoundaryEdgeInfo.SideB
                : SutureSidewallBoundaryEdgeInfo.SideA;

            if (!simulator.TryCaptureSutureMaterialAnchor(
                    ownOuterPoint,
                    ownSide,
                    NeedlePortalWallLayer.Outer,
                    out pass.ownOuterAnchor))
            {
                reason = "ownOuter";
                return false;
            }
            if (!simulator.TryCaptureSutureMaterialAnchor(
                    ownInnerPoint,
                    ownSide,
                    NeedlePortalWallLayer.Inner,
                    out pass.ownInnerAnchor))
            {
                reason = "ownInner";
                return false;
            }
            if (!simulator.TryCaptureSutureMaterialAnchor(
                    oppositeOuterPoint,
                    oppositeSide,
                    NeedlePortalWallLayer.Outer,
                    out pass.oppositeOuterAnchor))
            {
                reason = "oppositeOuter";
                return false;
            }
            if (!simulator.TryCaptureSutureMaterialAnchor(
                    oppositeInnerPoint,
                    oppositeSide,
                    NeedlePortalWallLayer.Inner,
                    out pass.oppositeInnerAnchor))
            {
                reason = "oppositeInner";
                return false;
            }

            pass.materialAnchorsCaptured = true;
            return true;
        }

        private bool EnsurePlanMaterialAnchors(out string reason)
        {
            reason = string.Empty;
            if (plan == null || plan.stitchPairs == null || plan.stitchPairs.Count == 0)
            {
                reason = "plan=empty";
                return false;
            }

            for (int i = 0; i < plan.stitchPairs.Count; i++)
            {
                AutoSutureStitchPair pair = plan.stitchPairs[i];
                if (pair == null || pair.sideAPass == null || pair.sideBPass == null)
                {
                    reason = $"pair={i} invalid";
                    return false;
                }

                if (!pair.sideAPass.materialAnchorsCaptured &&
                    !TryCapturePassMaterialAnchorsFromPlanPoint(pair.sideAPass, out reason))
                {
                    reason = $"pair={i} pass=0 {reason}";
                    return false;
                }
                if (!pair.sideBPass.materialAnchorsCaptured &&
                    !TryCapturePassMaterialAnchorsFromPlanPoint(pair.sideBPass, out reason))
                {
                    reason = $"pair={i} pass=1 {reason}";
                    return false;
                }
            }

            return true;
        }

        private bool TryCapturePassMaterialAnchorsFromPlanPoint(
            AutoSuturePass pass,
            out string reason)
        {
            Vector3 ownOuterPoint = pass.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner
                ? pass.entryPointWorld
                : pass.exitPointWorld;
            Vector3 ownInnerPoint = pass.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner
                ? pass.exitPointWorld
                : pass.entryPointWorld;
            return TryCapturePassMaterialAnchors(
                pass,
                ownOuterPoint,
                ownInnerPoint,
                ownOuterPoint,
                ownInnerPoint,
                out reason);
        }

        private bool TryRefreshPassFromMaterialAnchors(
            AutoSuturePass pass,
            bool logRefresh,
            out string reason)
        {
            reason = string.Empty;
            ClothSimulator simulator = driver != null ? driver.Simulator : null;
            if (pass == null || simulator == null || !pass.materialAnchorsCaptured)
            {
                reason = pass == null
                    ? "pass=null"
                    : simulator == null ? "simulator=null" : "anchors=missing";
                return false;
            }

            if (!simulator.TryResolveSutureMaterialAnchor(pass.ownOuterAnchor, out Vector3 ownOuter))
            {
                reason = "ownOuter resolve";
                return false;
            }
            if (!simulator.TryResolveSutureMaterialAnchor(pass.ownInnerAnchor, out Vector3 ownInner))
            {
                reason = "ownInner resolve";
                return false;
            }
            if (!simulator.TryResolveSutureMaterialAnchor(pass.oppositeOuterAnchor, out Vector3 oppositeOuter))
            {
                reason = "oppositeOuter resolve";
                return false;
            }
            if (!simulator.TryResolveSutureMaterialAnchor(pass.oppositeInnerAnchor, out Vector3 oppositeInner))
            {
                reason = "oppositeInner resolve";
                return false;
            }

            Vector3 previousEntry = pass.entryPointWorld;
            Vector3 previousExit = pass.exitPointWorld;
            float biteOffset = GetEffectiveBiteOffsetFromCut();
            Vector3 outerPoint = OffsetAwayFromOpposite(ownOuter, oppositeOuter, biteOffset);
            Vector3 innerPoint = OffsetAwayFromOpposite(ownInner, oppositeInner, biteOffset);
            if (pass.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner)
            {
                pass.entryPointWorld = outerPoint;
                pass.exitPointWorld = innerPoint;
            }
            else
            {
                pass.entryPointWorld = innerPoint;
                pass.exitPointWorld = outerPoint;
            }
            RefreshPassDirections(pass);

            if (logRefresh)
            {
                Debug.Log(
                    $"[AutoSuture][MaterialPassFreeze] pair={_currentPairIndex} pass={_currentPassInPair} " +
                    $"triggerState={_state} frozen=1 " +
                    $"side={pass.side} dir={pass.direction} " +
                    $"entryDrift={Vector3.Distance(previousEntry, pass.entryPointWorld):F6} " +
                    $"exitDrift={Vector3.Distance(previousExit, pass.exitPointWorld):F6} " +
                    $"ownOuterEdge={pass.ownOuterAnchor.sourceEdgeId} ownInnerEdge={pass.ownInnerAnchor.sourceEdgeId} " +
                    $"oppositeOuterEdge={pass.oppositeOuterAnchor.sourceEdgeId} oppositeInnerEdge={pass.oppositeInnerAnchor.sourceEdgeId}");
            }

            return true;
        }

        private static float GetMaxAnchorCaptureDistance(AutoSuturePass pass)
        {
            if (pass == null || !pass.materialAnchorsCaptured)
                return 0f;
            return Mathf.Max(
                Mathf.Max(pass.ownOuterAnchor.captureDistance, pass.ownInnerAnchor.captureDistance),
                Mathf.Max(pass.oppositeOuterAnchor.captureDistance, pass.oppositeInnerAnchor.captureDistance));
        }

        private void TickState(float dt)
        {
            AutoSuturePass pass = GetCurrentPass();
            if (pass == null)
            {
                Error("Invalid current pass");
                return;
            }

            switch (_state)
            {
                case AutoSutureState.MoveToPreEntry:
                    float initialTravelSpeed = ResolvePreEntryTravelSpeed(
                        _currentPairIndex,
                        _currentPassInPair,
                        firstStitchMoveSpeed,
                        pass.approachSpeed);
                    if (MoveNeedleSafelyToPreEntry(pass, initialTravelSpeed, dt))
                        SetState(AutoSutureState.AlignNeedle);
                    break;
                case AutoSutureState.AlignNeedle:
                    if (AlignNeedleBeforePuncture(pass, dt))
                        EnterPuncture(pass);
                    break;
                case AutoSutureState.Puncture:
                    TickPuncture(pass, dt);
                    break;
                case AutoSutureState.WaitPassComplete:
                    TickWaitPassComplete(pass);
                    break;
                case AutoSutureState.Retract:
                    Error("Legacy Retract state is not valid for real-contact auto suture");
                    break;
                case AutoSutureState.AdvanceUntilHist:
                    TickAdvanceUntilHist(pass, dt);
                    break;
                case AutoSutureState.PostHistClearance:
                    TickPostHistClearance(pass, dt);
                    break;
                case AutoSutureState.MoveToNextPass:
                    float travelSpeed = IsBetweenStitchTravel()
                        ? betweenStitchMoveSpeed
                        : betweenPassMoveSpeed;
                    if (MoveNeedleSafelyToPreEntry(pass, travelSpeed, dt))
                        SetState(AutoSutureState.AlignNeedle);
                    break;
                case AutoSutureState.PullLineAfterPair:
                    TickPullLineAfterPair();
                    break;
                case AutoSutureState.WaitPullLineAdvanceReady:
                    TickWaitPullLineAdvanceReady();
                    break;
            }
        }

        private void EnterMoveToPreEntry()
        {
            AutoSuturePass pass = GetCurrentPass();
            if (pass == null)
            {
                Error("Invalid current pass");
                return;
            }

            _passStartPassCount = GetNeedlePassCount();
            if (!TryRefreshPassFromMaterialAnchors(pass, true, out string reason))
            {
                Error($"MaterialAnchor refresh failed: {reason}");
                return;
            }
            SetState(AutoSutureState.MoveToPreEntry);
        }

        private void EnterMoveToNextPass()
        {
            AutoSuturePass pass = GetCurrentPass();
            if (pass == null)
            {
                Error("Invalid current pass");
                return;
            }

            _passStartPassCount = GetNeedlePassCount();
            if (!TryRefreshPassFromMaterialAnchors(pass, true, out string reason))
            {
                Error($"MaterialAnchor refresh failed: {reason}");
                return;
            }
            SetState(AutoSutureState.MoveToNextPass);
        }

        private bool AlignNeedleBeforePuncture(AutoSuturePass pass, float dt)
        {
            if (pass.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.InnerToOuter &&
                useLongitudinalLumenTravelBetweenSides && !_lumenAlignmentValidated)
            {
                if (!TryRefreshPassFromMaterialAnchors(pass, false, out string refreshReason))
                {
                    Error($"LumenHandoff: {refreshReason}");
                    return false;
                }
                Vector3 currentTip = needleRig.GetSemanticTipPoint(), arrivedForward = needleRig.GetSemanticTipForward();
                Vector3 destination = GetPreEntry(pass), direction = GetEntryDirection(pass);
                if (!TryCaptureExteriorVesselSurface(out VesselSurfaceSnapshot surface, out refreshReason))
                {
                    Error($"LumenHandoff: {refreshReason}");
                    return false;
                }
                // Connect the actual arrived pose to the refreshed preparation
                // pose before considering another trip through the center lane.
                if (!TryBuildLumenLocalPoseRoute(currentTip, arrivedForward, destination, direction,
                        EstimateNeedleWorldLength(), EstimateNeedleWorldRadius(), GetWholeNeedlePosePlanningMargin(false),
                        surface, _lumenWaypoints, _lumenForwards, out refreshReason))
                {
                    Debug.Log($"[AutoSuture][LumenReplan] pair={_currentPairIndex} mode=FullRoute reason=LocalSweepBlocked detail={refreshReason}");
                    SetState(AutoSutureState.MoveToNextPass);
                    return false;
                }
                _lumenWaypointIndex = 0;
                _lumenLocalAdjustmentActive = true;
                _lumenAlignmentValidated = true;
                Debug.Log($"[AutoSuture][LumenReplan] pair={_currentPairIndex} mode=LocalPose validation=CurrentVesselMesh " +
                    $"distance={Vector3.Distance(currentTip, destination):F6} angle={Vector3.Angle(arrivedForward, direction):F4} " +
                    $"waypoints={_lumenWaypoints.Count} frozen=1 scope=CurrentPuncture");
            }
            if (_lumenLocalAdjustmentActive)
            {
                if (!MoveNeedleAlongPlannedPose(_lumenWaypoints[_lumenWaypointIndex], _lumenForwards[_lumenWaypointIndex],
                        pass.approachSpeed, dt, "LumenLocalPreEntryAdjustment", false)) return false;
                _lumenWaypointIndex++;
                if (_lumenWaypointIndex < _lumenWaypoints.Count) return false;
                _lumenLocalAdjustmentActive = false;
                return true;
            }
            if (UsesCurrentExteriorGeometry(pass) && !_exteriorAlignmentValidated)
            {
                if (!TryRefreshNextExteriorGeometry(pass, out string refreshReason))
                {
                    Error($"Next-pass geometry unavailable before puncture: {refreshReason}");
                    return false;
                }
                if (ExteriorGeometryChanged(GetPreEntry(pass), GetEntryDirection(pass)))
                {
                    Debug.Log($"[AutoSuture][ExteriorReplan] pair={_currentPairIndex} pass={_currentPassInPair} reason=DeformedBeforePuncture");
                    SetState(_currentPairIndex == 0 ? AutoSutureState.MoveToPreEntry : AutoSutureState.MoveToNextPass);
                    return false;
                }
                _exteriorAlignmentValidated = true;
                Debug.Log($"[AutoSuture][MaterialPassFreeze] pair={_currentPairIndex} pass={_currentPassInPair} " +
                    $"triggerState=AlignNeedle frozen=1 scope=CurrentPuncture entry={FormatVec(pass.entryPointWorld)} " +
                    $"exit={FormatVec(pass.exitPointWorld)}");
            }
            Vector3 tip = needleRig.GetSemanticTipPoint();
            Vector3 entryDirection = GetEntryDirection(pass);
            if (pass.direction != ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner)
                return MoveNeedleAlongPlannedPose(tip, entryDirection, pass.approachSpeed, dt, "LumenFinalAlignment", false);

            // Transport arrival has a tolerance. Finish ALL remaining rotation
            // at the exterior pre-entry pose, under the same capsule checks,
            // instead of consuming that rotation while advancing into tissue.
            if (!MoveNeedleTowardWithWholeNeedleCheck(tip, entryDirection,
                    pass.approachSpeed, dt, "ExteriorAlignBeforePuncture"))
                return false;
            Vector3 currentForward = needleRig.GetSemanticTipForward();
            if (Vector3.Angle(currentForward, entryDirection) > 0.05f)
                return false;
            tip = needleRig.GetSemanticTipPoint();
            if (!TryValidateWholeNeedleTravel(tip, currentForward, tip, entryDirection,
                    true, out string reason))
            {
                Error($"Puncture pose handoff blocked before entry: {reason}");
                return false;
            }
            // Remove only numerical residual; do not carry an angular backlog
            // across the state transition into the first puncture translation.
            return needleRig.TrySetAutoSutureTipPose(tip, entryDirection, GetContinuousPoseUp(entryDirection));
        }

        private void EnterPuncture(AutoSuturePass pass)
        {
            _punctureStartTip = needleRig != null ? needleRig.GetSemanticTipPoint() : GetPreEntry(pass);
            _puncturePostExit = GetPostExit(pass);
            BuildPunctureDistancePath(pass);
            if (!ValidateAutomaticPunctureDirections(pass, _puncturePathForwards, out string reason))
            {
                Error($"Puncture pose handoff invalid before entry: {reason}");
                return;
            }
            _punctureTravelDistance = 0f;
            _passStartTime = Time.time;
            SetState(AutoSutureState.Puncture);
        }

        private void TickPuncture(AutoSuturePass pass, float dt)
        {
            float step = AutoSutureLifecyclePolicy.ComputeMotionStep(
                GetPositionDrivenPunctureSpeed(pass),
                dt,
                GetEffectiveMaxAutoPoseStep());
            _punctureTravelDistance = Mathf.Min(
                _puncturePathLength,
                _punctureTravelDistance + step);
            SamplePunctureDistancePath(
                _punctureTravelDistance,
                out Vector3 tip,
                out Vector3 desiredForward);
            Vector3 forward = RotateForwardToward(desiredForward, dt);
            needleRig.TrySetAutoSutureTipPose(tip, forward, GetContinuousPoseUp(forward), true);

            if (_punctureTravelDistance >= _puncturePathLength - 1e-6f)
                SetState(AutoSutureState.WaitPassComplete);
        }

        private void TickWaitPassComplete(AutoSuturePass pass)
        {
            int expectedPassIndex = _passStartPassCount + 1;
            ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord record;
            if (TryGetCurrentPassRecord(out record))
            {
                int passCount = GetNeedlePassCount();
                if (record.passIndex != expectedPassIndex || passCount > expectedPassIndex)
                {
                    Error($"Unexpected pass registration: pair={_currentPairIndex} pass={_currentPassInPair} start={_passStartPassCount} selected={record.passIndex} passNow={passCount} expected={expectedPassIndex}");
                    return;
                }

                pass.completedPassIndex = record.passIndex;
                NeedlePassLifecycleInfo lifecycle;
                if (TryGetPassLifecycle(expectedPassIndex, out lifecycle))
                {
                    AutoSutureLifecycleStage stage = EvaluateLifecycle(lifecycle, 0f);
                    if (stage == AutoSutureLifecycleStage.AdvanceUntilHist)
                    {
                        BeginAdvanceUntilHist(lifecycle);
                        return;
                    }
                    if (stage == AutoSutureLifecycleStage.PostHistClearance ||
                        stage == AutoSutureLifecycleStage.Ready)
                    {
                        BeginPostHistClearance(lifecycle);
                        return;
                    }
                }
            }

            HoldPuncturePathEnd(Time.deltaTime);

            if (Time.time - _passStartTime > passTimeout)
            {
                LogPassTimeoutProbe(pass);
                NeedlePassLifecycleInfo lifecycle;
                string lifecycleText = TryGetPassLifecycle(expectedPassIndex, out lifecycle)
                    ? FormatLifecycle(lifecycle)
                    : "none";
                Error(
                    $"RealContact timeout: pair={_currentPairIndex} pass={_currentPassInPair} " +
                    $"expected=P{expectedPassIndex} lifecycle={lifecycleText}");
            }
        }

        private void HoldPuncturePathEnd(float dt)
        {
            if (needleRig == null || _puncturePathPoints == null || _puncturePathPoints.Length == 0)
                return;

            Vector3 holdTip = _puncturePathPoints[_puncturePathPoints.Length - 1];
            Vector3 desiredForward = _puncturePathForwards[_puncturePathForwards.Length - 1];
            Vector3 forward = RotateForwardToward(desiredForward, dt);
            needleRig.TrySetAutoSutureTipPose(holdTip, forward, GetContinuousPoseUp(forward), true);
        }

        private void BeginAdvanceUntilHist(NeedlePassLifecycleInfo lifecycle)
        {
            _lastCurrentExitSurface = null;
            _lastCurrentExitPass = 0;
            _activeExitDirection = GetLifecycleExitDirection(lifecycle);
            _lifecycleStartTime = Time.time;
            _lastLifecycleLogTime = -999f;
            Debug.Log(
                $"[AutoSuture][PortalCommitted] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"{FormatLifecycle(lifecycle)} exitDir={FormatVec(_activeExitDirection)}");
            SetState(AutoSutureState.AdvanceUntilHist);
        }

        private void TickAdvanceUntilHist(AutoSuturePass pass, float dt)
        {
            int passIndex = pass != null ? pass.completedPassIndex : 0;
            NeedlePassLifecycleInfo lifecycle;
            if (!TryGetPassLifecycle(passIndex, out lifecycle))
            {
                if (Time.time - _lifecycleStartTime > passTimeout)
                    Error($"Tail lifecycle unavailable: pass={passIndex}");
                return;
            }

            AutoSutureLifecycleStage stage = EvaluateLifecycle(lifecycle, 0f);
            if (stage == AutoSutureLifecycleStage.PostHistClearance ||
                stage == AutoSutureLifecycleStage.Ready)
            {
                BeginPostHistClearance(lifecycle);
                return;
            }
            if (stage != AutoSutureLifecycleStage.AdvanceUntilHist)
            {
                if (Time.time - _lifecycleStartTime > passTimeout)
                    Error($"Tail lifecycle invalid before Hist: {FormatLifecycle(lifecycle)}");
                return;
            }

            _activeExitDirection = GetLifecycleExitDirection(lifecycle);
            float step = AutoSutureLifecyclePolicy.ComputeMotionStep(
                GetPostPunctureRetractSpeed(pass),
                dt,
                GetEffectiveMaxAutoPoseStep());
            CaptureCurrentExitContactSnapshot(lifecycle);
            AdvanceNeedleAlongExit(_activeExitDirection, step, dt);
            LogLifecycleProgress("AdvanceUntilHist", lifecycle, step, 0f);

            if (Time.time - _lifecycleStartTime > passTimeout)
                Error($"Tail did not become Hist: {FormatLifecycle(lifecycle)}");
        }

        private void BeginPostHistClearance(NeedlePassLifecycleInfo lifecycle)
        {
            _postHistContactAudited = false;
            _postHistChannelContacts = new ExitChannelContactSet(null, new HashSet<int>());
            if (_lastCurrentExitSurface != null && _lastCurrentExitPass == lifecycle.passIndex &&
                _lastCurrentExitPortal.triangleIndex == lifecycle.exitPortal.triangleIndex &&
                _lastCurrentExitPortal.v0 == lifecycle.exitPortal.v0 && _lastCurrentExitPortal.v1 == lifecycle.exitPortal.v1 &&
                _lastCurrentExitPortal.v2 == lifecycle.exitPortal.v2)
            {
                var point = _lastCurrentExitPortal.currentPoint;
                _postHistChannelContacts = _lastCurrentExitSurface.CaptureExitChannelContacts(
                    _lastCurrentExitTip, _lastCurrentExitForward, EstimateNeedleWorldLength(), EstimateNeedleWorldRadius(),
                    Mathf.Max(0f, wholeNeedleTravelCollisionMargin), new Vector3(point.x, point.y, point.z),
                    _lastCurrentExitPortal.triangleIndex, lifecycle.outerTriangleCount, _lastCurrentExitPortal.relaxRadius);
            }
            _activeExitDirection = GetLifecycleExitDirection(lifecycle);
            _postHistForward = needleRig.GetSemanticTipForward().normalized;
            _postHistTravel = 0f;
            _lifecycleStartTime = Time.time;
            _lastLifecycleLogTime = -999f;
            Debug.Log(
                $"[AutoSuture][HistReached] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"{FormatLifecycle(lifecycle)} extraRequired={GetRequiredPostHistTravel():F5}");
            SetState(AutoSutureState.PostHistClearance);
        }

        private void TickPostHistClearance(AutoSuturePass pass, float dt)
        {
            int passIndex = pass != null ? pass.completedPassIndex : 0;
            NeedlePassLifecycleInfo lifecycle;
            if (!TryGetPassLifecycle(passIndex, out lifecycle))
            {
                Error($"Hist lifecycle unavailable during post clearance: pass={passIndex}");
                return;
            }

            if (lifecycle.hasOuter == 0 || lifecycle.hasInner == 0 || lifecycle.hasCurrent != 0 || lifecycle.hasHist == 0 || lifecycle.hasExitPortal == 0)
            {
                Error($"PostHistGeometry: invalid lifecycle {FormatLifecycle(lifecycle)}");
                return;
            }
            Vector3 exitPoint = new Vector3(lifecycle.exitPortalPoint.x, lifecycle.exitPortalPoint.y, lifecycle.exitPortalPoint.z);
            Vector3 exitDirection = GetLifecycleExitDirection(lifecycle);
            if (Vector3.Angle(_postHistForward, exitDirection) > 0.1f)
            {
                Error("PostHistGeometry: exit direction changed; cannot rotate while leaving the channel");
                return;
            }
            if (!TryCaptureExteriorVesselSurface(out VesselSurfaceSnapshot surface, out string reason))
            {
                Error($"PostHistGeometry: {reason}");
                return;
            }
            bool exteriorExit = pass.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.InnerToOuter;
            if (exteriorExit && !TryRefreshLocalTissueSurface(false, out reason))
            {
                Error($"PostHistGeometry: {reason}");
                return;
            }
            float length = EstimateNeedleWorldLength(), radius = EstimateNeedleWorldRadius();
            float budget = Mathf.Max(GetRequiredPostHistTravel(), length);
            Vector3 currentTip = needleRig.GetSemanticTipPoint();
            surface.RetireClearedExitContacts(_postHistChannelContacts, currentTip, _postHistForward, length, radius,
                Mathf.Max(0f, wholeNeedleTravelCollisionMargin));
            bool exitPlanned = surface.TryPlanAxialExit(currentTip, _postHistForward, exitPoint, exitDirection,
                    length, radius, Mathf.Max(0f, wholeNeedleTravelCollisionMargin),
                    Mathf.Max(0f, GetRequiredPostHistTravel() - _postHistTravel),
                    Mathf.Max(0f, budget - _postHistTravel),
                    exteriorExit ? _localTissueSurface : null, Mathf.Max(0f, tissueTravelMargin),
                    out float travelToClear, out reason, GetWholeNeedlePosePlanningMargin(exteriorExit) * 0.5f, _postHistChannelContacts);
            if (logExitContactHandoff && (!_postHistContactAudited || !exitPlanned))
            {
                LogExitContactHandoff(surface, lifecycle, exitPlanned ? "HistHandoff" : "ExitBlocked");
                _postHistContactAudited = true;
            }
            if (!exitPlanned)
            {
                Error($"PostHistGeometry: pass={passIndex} travel={_postHistTravel:F5} {reason}");
                return;
            }
            if (travelToClear <= 1e-6f)
            {
                pass.complete = true;
                Debug.Log($"[AutoSuture][PostHistClearance] pair={_currentPairIndex} pass={_currentPassInPair} " +
                    $"passIndex={passIndex} travel={_postHistTravel:F5} ready=1 geometry=CurrentVesselMesh tailBeyondExit=1 " +
                    $"handoffGap={GetWholeNeedlePosePlanningMargin(exteriorExit) * 0.5f:F5}");
                CompleteRetract();
                return;
            }
            float step = Mathf.Min(travelToClear, AutoSutureLifecyclePolicy.ComputeMotionStep(
                GetPostPunctureRetractSpeed(pass), dt, GetEffectiveMaxAutoPoseStep()));
            // Still in the original channel: translate coaxially, never consume
            // a rotation backlog or switch to lateral transport before release.
            if (!needleRig.TrySetAutoSutureTipPose(currentTip + _postHistForward * step,
                    _postHistForward, GetContinuousPoseUp(_postHistForward), true))
            {
                Error("PostHistGeometry: pose application failed");
                return;
            }
            _postHistTravel += step;
            LogLifecycleProgress("PostHistClearance", lifecycle, step, _postHistTravel);
            if (Time.time - _lifecycleStartTime > passTimeout)
                Error($"PostHistGeometry: clearance timeout travel={_postHistTravel:F5}");
        }

        private void CaptureCurrentExitContactSnapshot(NeedlePassLifecycleInfo lifecycle)
        {
            // Provenance is required for motion even when logging is disabled.
            if (lifecycle.hasExitPortal == 0) return;
            Vector3 tip = needleRig.GetSemanticTipPoint(), forward = needleRig.GetSemanticTipForward();
            Vector3 exit = new Vector3(lifecycle.exitPortalPoint.x, lifecycle.exitPortalPoint.y, lifecycle.exitPortalPoint.z);
            float tailAxial = Vector3.Dot(tip - forward * EstimateNeedleWorldLength() - exit, GetLifecycleExitDirection(lifecycle));
            // Keep only the last near-exit Current frame, not the full motion history.
            if (tailAxial < -Mathf.Max(0.01f, GetEffectiveMaxAutoPoseStep() * 2f)) return;
            if (!TryCaptureExteriorVesselSurface(out VesselSurfaceSnapshot snapshot, out _)) return;
            _lastCurrentExitSurface = snapshot;
            _lastCurrentExitTip = tip; _lastCurrentExitForward = forward;
            _lastCurrentExitPortal = lifecycle.exitPortal;
            _lastCurrentExitPass = lifecycle.passIndex; _lastCurrentExitFrame = Time.frameCount;
        }

        private void LogExitContactHandoff(VesselSurfaceSnapshot surface, NeedlePassLifecycleInfo lifecycle, string stage)
        {
            bool hasPrevious = _lastCurrentExitSurface != null && _lastCurrentExitPass == lifecycle.passIndex;
            NeedlePortalInfo portal = lifecycle.exitPortal;
            Vector3 exit = new Vector3(lifecycle.exitPortalPoint.x, lifecycle.exitPortalPoint.y, lifecycle.exitPortalPoint.z);
            float probe = Mathf.Max(0.0001f, GetEffectiveMaxAutoPoseStep());
            string scan = surface.DescribeExitContactHandoff(needleRig.GetSemanticTipPoint(), _postHistForward,
                EstimateNeedleWorldLength(), EstimateNeedleWorldRadius(), Mathf.Max(0f, wholeNeedleTravelCollisionMargin),
                exit, portal.triangleIndex, lifecycle.outerTriangleCount, probe,
                hasPrevious ? _lastCurrentExitSurface : null, _lastCurrentExitTip, _lastCurrentExitForward);
            Debug.Log($"[AutoSuture][ExitContactHandoff] pair={_currentPairIndex} pass={_currentPassInPair} passIndex={lifecycle.passIndex} " +
                $"stage={stage} frame={Time.frameCount} previousCurrentFrame={(hasPrevious ? _lastCurrentExitFrame : -1)} " +
                $"exitTri={portal.triangleIndex} portalVertices=[{portal.v0},{portal.v1},{portal.v2}] layer={portal.wallLayer} " +
                $"exitPoint={FormatVec(exit)} relaxRadius={portal.relaxRadius:F6} captureDepth={portal.captureDepth:F6} " +
                $"current={lifecycle.hasCurrent} hist={lifecycle.hasHist} probeTravel={probe:F6} " +
                $"inheritedContacts={_postHistChannelContacts?.triangles.Count ?? 0} source=CurrentPortalContactSet diagnosticOnly=1 {scan}");
        }

        private bool TryGetPassLifecycle(int passIndex, out NeedlePassLifecycleInfo lifecycle)
        {
            lifecycle = default;
            ClothSimulator simulator = driver != null ? driver.Simulator : null;
            int colliderId = needleRig != null ? needleRig.GetTargetColliderEntityId() : 0;
            return simulator != null && colliderId != 0 && passIndex > 0 &&
                simulator.TryGetNeedlePassLifecycle(colliderId, passIndex, out lifecycle);
        }

        private AutoSutureLifecycleStage EvaluateLifecycle(
            NeedlePassLifecycleInfo lifecycle,
            float postHistTravel)
        {
            return AutoSutureLifecyclePolicy.Evaluate(
                lifecycle.hasOuter != 0,
                lifecycle.hasInner != 0,
                lifecycle.hasCurrent != 0,
                lifecycle.hasHist != 0,
                postHistTravel,
                GetRequiredPostHistTravel());
        }

        private Vector3 GetLifecycleExitDirection(NeedlePassLifecycleInfo lifecycle)
        {
            Vector3 direction = new Vector3(
                lifecycle.exitDirection.x,
                lifecycle.exitDirection.y,
                lifecycle.exitDirection.z);
            if (lifecycle.hasExitPortal == 0 || direction.sqrMagnitude < 1e-8f)
            {
                AutoSuturePass pass = GetCurrentPass();
                direction = pass != null ? GetExitDirection(pass) : _activeExitDirection;
            }
            return direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
        }

        private void AdvanceNeedleAlongExit(Vector3 direction, float distance, float dt)
        {
            if (needleRig == null || distance <= 0f)
                return;

            Vector3 forward = RotateForwardToward(direction, dt);
            Vector3 nextTip = needleRig.GetSemanticTipPoint() + direction * distance;
            needleRig.TrySetAutoSutureTipPose(nextTip, forward, GetContinuousPoseUp(forward), true);
        }

        private void LogLifecycleProgress(
            string phase,
            NeedlePassLifecycleInfo lifecycle,
            float step,
            float travel)
        {
            if (Time.time - _lastLifecycleLogTime < 0.35f)
                return;

            _lastLifecycleLogTime = Time.time;
            Debug.Log(
                $"[AutoSuture][{phase}] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"{FormatLifecycle(lifecycle)} step={step:F6} travel={travel:F6} " +
                $"exitDir={FormatVec(_activeExitDirection)}");
        }

        private static string FormatLifecycle(NeedlePassLifecycleInfo lifecycle)
        {
            return
                $"P{lifecycle.passIndex}[entries={lifecycle.historyEntries}," +
                $"outer={lifecycle.hasOuter},inner={lifecycle.hasInner}," +
                $"current={lifecycle.hasCurrent},hist={lifecycle.hasHist},done={lifecycle.hasDone}]";
        }

        private float GetRequiredPostHistTravel()
        {
            return Mathf.Max(0f, postHistPullDistance);
        }

        private void CompleteRetract()
        {
            if (_currentPassInPair == 0)
            {
                _currentPassInPair = 1;
                EnterMoveToNextPass();
                return;
            }

            Debug.Log($"[AutoSuture] pair={_currentPairIndex} action=NeedleTailClearBeforePull verified=1 postHistTravel={_postHistTravel:F5}");
            SetState(AutoSutureState.PullLineAfterPair);
        }

        private void TickPullLineAfterPair()
        {
            AutoSutureStitchPair pair = GetCurrentPair();
            if (pair == null || !pair.sideAPass.complete || !pair.sideBPass.complete)
            {
                Error("PullLine requested before two complete passes");
                return;
            }

            if (!ValidatePairPassIndicesBeforePull(pair))
                return;

            _expectedPullPassA = pair.sideAPass.completedPassIndex;
            _expectedPullPassB = pair.sideBPass.completedPassIndex;
            string readyReason;
            if (!AreAutoPullLineHistPassesReady(_expectedPullPassA, _expectedPullPassB, out readyReason))
            {
                if (Time.time - _lastAutoPullLineReadyLogTime > 0.35f)
                {
                    _lastAutoPullLineReadyLogTime = Time.time;
                    Debug.Log($"[AutoSuture] pair={_currentPairIndex} action=WaitAutoPullLineReady passA={_expectedPullPassA} passB={_expectedPullPassB} reason={readyReason}");
                }

                if (_stateElapsed > pullLineTimeout)
                    Error($"AutoPullLine hist pair not ready: passA={_expectedPullPassA} passB={_expectedPullPassB} reason={readyReason}");
                return;
            }

            bool requested = false;
            if (needleRig != null)
                requested = needleRig.RequestAutoSuturePullLine(_expectedPullPassA, _expectedPullPassB, strongPullLineDrive);
            if (!requested && strongPullLineDrive && needleRig != null)
                requested = needleRig.RequestAutoSuturePullLine(_expectedPullPassA, _expectedPullPassB, false);
            if (!requested)
            {
                Error("RequestPullLine failed");
                return;
            }

            pair.pullLineRequested = true;
            _pullLineStartTime = Time.time;
            _pullLineDeadlineTime = _pullLineStartTime + Mathf.Max(0.1f, pullLineTimeout);
            _lastPullLineRestDistance = float.PositiveInfinity;
            _lastPullLineProgressTime = _pullLineStartTime;
            _lastAutoPullLineReadyLogTime = -999f;
            Debug.Log($"[AutoSuture] pair={_currentPairIndex} action=PullLine passA={_expectedPullPassA} passB={_expectedPullPassB} strongPull={(strongPullLineDrive ? 1 : 0)}");
            SetState(AutoSutureState.WaitPullLineAdvanceReady);
        }

        private bool ValidatePairPassIndicesBeforePull(AutoSutureStitchPair pair)
        {
            if (pair == null)
            {
                Error("PullLine requested without current pair");
                return false;
            }

            int passA = pair.sideAPass != null ? pair.sideAPass.completedPassIndex : 0;
            int passB = pair.sideBPass != null ? pair.sideBPass.completedPassIndex : 0;
            if (passA <= 0 || passB <= 0)
            {
                Error($"PullLine missing completed pass index: passA={passA} passB={passB}");
                return false;
            }

            if (passB != passA + 1)
            {
                Error($"PullLine pass indices are not consecutive: passA={passA} passB={passB} expectedB={passA + 1}");
                return false;
            }

            int passCount = GetNeedlePassCount();
            if (passCount > passB)
            {
                Error($"Unexpected extra pass before PullLine: passA={passA} passB={passB} passNow={passCount}");
                return false;
            }

            return true;
        }

        private bool AreAutoPullLineHistPassesReady(int passA, int passB, out string reason)
        {
            reason = "ready";
            ClothSimulator simulator = driver != null ? driver.Simulator : null;
            if (simulator == null)
            {
                reason = "no-simulator";
                return false;
            }

            int colliderId = needleRig != null ? needleRig.GetTargetColliderEntityId() : 0;
            if (colliderId == 0)
            {
                reason = "no-collider";
                return false;
            }

            AutoPullLinePassReadiness a = BuildAutoPullLinePassReadiness(simulator, colliderId, passA);
            AutoPullLinePassReadiness b = BuildAutoPullLinePassReadiness(simulator, colliderId, passB);
            if (a.historyEntries <= 0 || b.historyEntries <= 0)
            {
                reason = $"missing-history A={FormatAutoPullLinePassReadiness(a)} B={FormatAutoPullLinePassReadiness(b)}";
                return false;
            }

            if (!a.IsComplete || !b.IsComplete)
            {
                reason = $"incomplete-wall-pair A={FormatAutoPullLinePassReadiness(a)} B={FormatAutoPullLinePassReadiness(b)}";
                return false;
            }

            if (a.hasActive || b.hasActive)
            {
                reason = $"current-still-active A={FormatAutoPullLinePassReadiness(a)} B={FormatAutoPullLinePassReadiness(b)}";
                return false;
            }

            if (!a.hasHist || !b.hasHist)
            {
                reason = $"not-hist-yet A={FormatAutoPullLinePassReadiness(a)} B={FormatAutoPullLinePassReadiness(b)}";
                return false;
            }

            if (a.hasDone || b.hasDone)
            {
                reason = $"already-done A={FormatAutoPullLinePassReadiness(a)} B={FormatAutoPullLinePassReadiness(b)}";
                return false;
            }

            return true;
        }

        private static AutoPullLinePassReadiness BuildAutoPullLinePassReadiness(ClothSimulator simulator, int colliderId, int passIndex)
        {
            AutoPullLinePassReadiness readiness = new AutoPullLinePassReadiness()
            {
                passIndex = passIndex,
            };
            if (simulator == null || colliderId == 0 || passIndex <= 0)
                return readiness;

            int historyCount = simulator.portalHistoryCount;
            for (int i = 0; i < historyCount; i++)
            {
                NeedlePortalHistoryEntry entry = simulator.GetPortalHistory(i);
                if (entry.colliderEntityId != colliderId || entry.passIndex != passIndex || entry.portal.enabled == 0)
                    continue;

                readiness.historyEntries++;
                if (entry.portal.wallLayer == (byte)NeedlePortalWallLayer.Outer)
                    readiness.hasOuter = true;
                else if (entry.portal.wallLayer == (byte)NeedlePortalWallLayer.Inner)
                    readiness.hasInner = true;

                byte stitchState = GetPortalStitchState(entry);
                readiness.hasActive |= entry.isActive != 0 || stitchState == (byte)NeedlePortalStitchState.Current;
                readiness.hasHist |= stitchState == (byte)NeedlePortalStitchState.Hist;
                readiness.hasDone |= stitchState == (byte)NeedlePortalStitchState.Done;
            }

            return readiness;
        }

        private static byte GetPortalStitchState(NeedlePortalHistoryEntry entry)
        {
            if (entry.stitchState != 0)
                return entry.stitchState;
            if (entry.portal.stitchState != 0)
                return entry.portal.stitchState;
            return entry.isActive != 0 ? (byte)NeedlePortalStitchState.Current : (byte)NeedlePortalStitchState.Hist;
        }

        private static string FormatAutoPullLinePassReadiness(AutoPullLinePassReadiness readiness)
        {
            return $"P{readiness.passIndex}[entries={readiness.historyEntries},outer={(readiness.hasOuter ? 1 : 0)},inner={(readiness.hasInner ? 1 : 0)},active={(readiness.hasActive ? 1 : 0)},hist={(readiness.hasHist ? 1 : 0)},done={(readiness.hasDone ? 1 : 0)}]";
        }

        private float GetPositionDrivenPunctureSpeed(AutoSuturePass pass)
        {
            if (pass == null)
                return Mathf.Max(0.001f, punctureSpeed);

            float baseSpeed = Mathf.Max(0.001f, pass.punctureSpeed);
            return baseSpeed * GetEffectivePositionDrivenPunctureSpeedMultiplier();
        }

        private float GetEffectivePositionDrivenPunctureSpeedMultiplier()
        {
            return Mathf.Clamp(positionDrivenPunctureSpeedMultiplier, 0.05f, MaxAutoSuturePositionPunctureSpeedMultiplier);
        }

        private float GetPostPunctureRetractSpeed(AutoSuturePass pass)
        {
            float speed = pass != null ? pass.retractSpeed : retractSpeed;
            if (positionDrivenPuncture)
                speed = Mathf.Min(speed, GetPositionDrivenPunctureSpeed(pass));
            return Mathf.Max(0.001f, speed);
        }

        private void TickWaitPullLineAdvanceReady()
        {
            bool hasPullLineInfo = TryGetPullLineInfo(
                _expectedPullPassA,
                _expectedPullPassB,
                out SutureLineDisplayInfo pullLineInfo);
            if (hasPullLineInfo)
                UpdatePullLineProgressDeadline(pullLineInfo);

            bool advanceReady = hasPullLineInfo &&
                (pullLineInfo.workflowAdvanceReady != 0 ||
                 pullLineInfo.workflowReleased != 0 ||
                 pullLineInfo.settled != 0);
            if (advanceReady)
            {
                if (!TryReleasePullLineBeforeNextPass(_expectedPullPassA, _expectedPullPassB, out int releasedLineIndex, out string releaseReason))
                {
                    if (Time.time - _lastAutoPullLineReadyLogTime > 0.35f)
                    {
                        _lastAutoPullLineReadyLogTime = Time.time;
                        Debug.Log($"[AutoSuture] pair={_currentPairIndex} action=WaitWorkflowRelease passA={_expectedPullPassA} passB={_expectedPullPassB} reason={releaseReason}");
                    }
                    if (HasPullLineDeadlineExpired())
                        Error(BuildPullLineTimeoutReason("release", releaseReason, hasPullLineInfo, pullLineInfo));
                    return;
                }

                AutoSutureStitchPair pair = GetCurrentPair();
                if (pair != null)
                    pair.pullLineAdvanceReleased = true;

                int nextPair = _currentPairIndex + 1;
                Debug.Log(
                    $"[AutoSuture] pair={_currentPairIndex} workflowAdvanceReleased=1 releasedLine={releasedLineIndex} " +
                    $"physicalSettled={pullLineInfo.settled} physicalReady={pullLineInfo.workflowReady} " +
                    $"advanceFrames={pullLineInfo.workflowAdvanceReadyFrames} nextPair={nextPair}");
                if (nextPair >= plan.Count)
                {
                    SetState(AutoSutureState.Done);
                    return;
                }

                _currentPairIndex = nextPair;
                _currentPassInPair = 0;
                EnterMoveToNextPass();
                return;
            }

            if (HasPullLineDeadlineExpired())
                Error(BuildPullLineTimeoutReason("ready", string.Empty, hasPullLineInfo, pullLineInfo));
        }

        private void UpdatePullLineProgressDeadline(SutureLineDisplayInfo info)
        {
            float currentRest = info.currentRestDistance;
            float targetRest = info.targetRestDistance;
            if (float.IsNaN(currentRest) || float.IsInfinity(currentRest) ||
                float.IsNaN(targetRest) || float.IsInfinity(targetRest))
                return;

            bool firstSample = float.IsInfinity(_lastPullLineRestDistance);
            bool madeProgress = firstSample ||
                currentRest < _lastPullLineRestDistance - PullLineProgressEpsilon;
            if (!madeProgress)
                return;

            _lastPullLineRestDistance = currentRest;
            _lastPullLineProgressTime = Time.time;

            float remainingRest = Mathf.Max(0f, currentRest - targetRest);
            float effectiveSpeed = GetConservativePullLineTightenSpeed();
            float remainingBudget = PullLineMinimumProgressBudgetSeconds;
            if (effectiveSpeed > 1e-6f)
            {
                remainingBudget = Mathf.Max(
                    remainingBudget,
                    remainingRest / effectiveSpeed * PullLineDeadlineSafetyScale +
                    PullLineDeadlineGraceSeconds);
            }

            _pullLineDeadlineTime = Mathf.Max(
                _pullLineDeadlineTime,
                Time.time + remainingBudget);
        }

        private float GetConservativePullLineTightenSpeed()
        {
            ClothSetting clothSetting = driver != null ? driver.setting : null;
            if (clothSetting == null)
                return 0f;

            float baseSpeed = Mathf.Max(0f, clothSetting.sutureLineTightenSpeed);
            float approachScale = Mathf.Max(0f, clothSetting.sutureLineApproachTightenSpeedScale);
            float latchedScale = Mathf.Max(0f, clothSetting.sutureLineLatchedTightenSpeedScale);
            float conservativeScale;
            if (approachScale <= 1e-6f)
                conservativeScale = latchedScale;
            else if (latchedScale <= 1e-6f)
                conservativeScale = approachScale;
            else
                conservativeScale = Mathf.Min(approachScale, latchedScale);
            return baseSpeed * conservativeScale;
        }

        private bool HasPullLineDeadlineExpired()
        {
            float deadline = _pullLineDeadlineTime > _pullLineStartTime
                ? _pullLineDeadlineTime
                : _pullLineStartTime + Mathf.Max(0.1f, pullLineTimeout);
            return Time.time > deadline;
        }

        private string BuildPullLineTimeoutReason(
            string phase,
            string detail,
            bool hasInfo,
            SutureLineDisplayInfo info)
        {
            float elapsed = Mathf.Max(0f, Time.time - _pullLineStartTime);
            float stalled = Mathf.Max(0f, Time.time - _lastPullLineProgressTime);
            string suffix = string.IsNullOrEmpty(detail) ? string.Empty : $" reason={detail}";
            if (!hasInfo)
            {
                return $"PullLine {phase} timeout: passA={_expectedPullPassA} passB={_expectedPullPassB} " +
                    $"elapsed={elapsed:F2} no-line-info{suffix}";
            }

            float remainingRest = Mathf.Max(0f, info.currentRestDistance - info.targetRestDistance);
            return $"PullLine {phase} timeout: passA={_expectedPullPassA} passB={_expectedPullPassB} " +
                $"elapsed={elapsed:F2} stalled={stalled:F2} currentRest={info.currentRestDistance:F6} " +
                $"targetRest={info.targetRestDistance:F6} remainingRest={remainingRest:F6} " +
                $"contactState={info.contactState} workflowReady={info.workflowReady} " +
                $"workflowFrames={info.workflowReadyFrames} advanceReady={info.workflowAdvanceReady} " +
                $"advanceFrames={info.workflowAdvanceReadyFrames} released={info.workflowReleased}{suffix}";
        }

        private bool TryReleasePullLineBeforeNextPass(int passA, int passB, out int lineIndex, out string reason)
        {
            lineIndex = 0;
            reason = "ready";
            ClothSimulator simulator = driver != null ? driver.Simulator : null;
            if (simulator == null)
            {
                reason = "no-simulator";
                return false;
            }

            int colliderId = needleRig != null ? needleRig.GetTargetColliderEntityId() : 0;
            if (colliderId == 0)
            {
                reason = "no-collider";
                return false;
            }

            return simulator.TryReleaseAutoSutureWorkflowForPassPair(colliderId, passA, passB, out lineIndex, out reason);
        }

        private bool TryGetCurrentPassRecord(out ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord record)
        {
            record = default;
            if (driver == null || !driver.HasSuturePassSequence)
                return false;

            AutoSuturePass pass = GetCurrentPass();
            int colliderId = needleRig != null ? needleRig.GetTargetColliderEntityId() : 0;
            int bestIndex = int.MaxValue;
            bool foundAnyComplete = false;
            ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord firstComplete = default;

            var records = driver.SuturePassSequenceRecords;
            for (int i = 0; i < records.Count; i++)
            {
                var candidate = records[i];
                if (colliderId != 0 && candidate.colliderEntityId != colliderId)
                    continue;
                if (!candidate.isComplete || candidate.passIndex <= _passStartPassCount)
                    continue;

                if (!foundAnyComplete || candidate.passIndex < firstComplete.passIndex)
                {
                    foundAnyComplete = true;
                    firstComplete = candidate;
                }

                if (candidate.side == pass.side &&
                    candidate.direction == pass.direction &&
                    candidate.passIndex < bestIndex)
                {
                    bestIndex = candidate.passIndex;
                    record = candidate;
                }
            }

            if (bestIndex < int.MaxValue)
                return true;

            if (foundAnyComplete)
            {
                Error($"Pass semantic mismatch: expected {GetSideLabel(pass.side)} {GetDirectionLabel(pass.direction)} got {GetSideLabel(firstComplete.side)} {GetDirectionLabel(firstComplete.direction)}");
            }

            return false;
        }

        private bool TryGetPullLineInfo(int passA, int passB, out SutureLineDisplayInfo matchedInfo)
        {
            matchedInfo = default;
            ClothSimulator simulator = driver != null ? driver.Simulator : null;
            if (simulator == null)
                return false;

            int colliderId = needleRig != null ? needleRig.GetTargetColliderEntityId() : 0;
            int minPass = Mathf.Min(passA, passB);
            int maxPass = Mathf.Max(passA, passB);
            for (int i = 0; i < simulator.sutureLineDisplayCount; i++)
            {
                SutureLineDisplayInfo info = simulator.GetSutureLineDisplayInfo(i);
                if (colliderId != 0 && info.colliderEntityId != colliderId)
                    continue;
                if (Mathf.Min(info.passIndexA, info.passIndexB) != minPass ||
                    Mathf.Max(info.passIndexA, info.passIndexB) != maxPass)
                    continue;

                matchedInfo = info;
                return true;
            }

            return false;
        }

        private bool MoveNeedleToward(Vector3 targetTip, Vector3 targetForward, float speed, float dt)
        {
            return MoveNeedleTowardInternal(
                targetTip,
                targetForward,
                speed,
                dt,
                false,
                string.Empty);
        }

        private bool MoveNeedleTowardWithWholeNeedleCheck(
            Vector3 targetTip,
            Vector3 targetForward,
            float speed,
            float dt,
            string travelStage,
            bool checkTissueBackground = true)
        {
            return MoveNeedleTowardInternal(
                targetTip,
                targetForward,
                speed,
                dt,
                true,
                travelStage,
                checkTissueBackground);
        }

        private bool MoveNeedleTowardInternal(
            Vector3 targetTip,
            Vector3 targetForward,
            float speed,
            float dt,
            bool checkWholeNeedle,
            string travelStage,
            bool checkTissueBackground = true)
        {
            if (needleRig == null)
                return false;

            Vector3 currentTip = needleRig.GetSemanticTipPoint();
            Vector3 currentForward = needleRig.GetSemanticTipForward();
            float linearStep = AutoSutureLifecyclePolicy.ComputeMotionStep(
                Mathf.Max(0.001f, speed),
                dt,
                GetEffectiveMaxAutoPoseStep());
            Vector3 nextTip = Vector3.MoveTowards(currentTip, targetTip, linearStep);
            float angularStep = Mathf.Min(
                angularSpeedDeg * Mathf.Max(0f, dt),
                Mathf.Max(0.1f, maxAutoPoseAngleStepDeg));
            Vector3 nextForward = Vector3.RotateTowards(
                currentForward.sqrMagnitude > 1e-8f ? currentForward.normalized : targetForward.normalized,
                targetForward.normalized,
                Mathf.Deg2Rad * angularStep,
                0f);
            if (nextForward.sqrMagnitude < 1e-8f)
                nextForward = targetForward.normalized;

            if (checkWholeNeedle &&
                !TryValidateWholeNeedleTravel(
                    currentTip,
                    currentForward,
                    nextTip,
                    nextForward,
                    checkTissueBackground,
                    out string blockedReason))
            {
                Error(
                    $"Whole-needle travel blocked: stage={travelStage} " +
                    blockedReason);
                return false;
            }

            needleRig.TrySetAutoSutureTipPose(nextTip, nextForward, GetContinuousPoseUp(nextForward));
            float distance = Vector3.Distance(nextTip, targetTip);
            float angle = Vector3.Angle(nextForward, targetForward);
            return distance <= poseArriveDistance && angle <= poseArriveAngleDeg;
        }

        internal static float ResolvePreEntryTravelSpeed(
            int pairIndex,
            int passInPair,
            float firstStitchSpeed,
            float passApproachSpeed)
        {
            return pairIndex == 0 && passInPair == 0
                ? Mathf.Max(0.001f, firstStitchSpeed)
                : Mathf.Max(0.001f, passApproachSpeed);
        }

        private bool MoveNeedleSafelyToPreEntry(AutoSuturePass pass, float speed, float dt)
        {
            if (pass == null)
                return false;
            if (IsSameStitchSideTransition() && useLongitudinalLumenTravelBetweenSides)
                return MoveNeedleThroughLongitudinalLumen(pass, speed, dt);
            if (!safeTravelAroundVessel || safeTravelClearance <= 1e-6f)
                return MoveNeedleTowardWithWholeNeedleCheck(
                    GetPreEntry(pass),
                    GetEntryDirection(pass),
                    speed,
                    dt,
                    "DirectPreEntry");
            if (needleRig == null)
                return false;

            Vector3 destination = GetPreEntry(pass);
            // Exterior ring travel owns departure as well as transit. Lifting a
            // bottom-side needle toward a global safe layer crosses the vessel.
            // Keep the lumen branch above this dispatch unchanged.
            if (pass.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner &&
                IsCircumferentialSuturePlan())
                return MoveNeedleAroundVesselExterior(pass, speed, dt);

            if (IsTravelBetweenSutureHoles() &&
                !MoveNeedleBodyToSafeDeparture(pass, destination, speed, dt))
            {
                return false;
            }

            if (TryGetTissueSafetyFrame(out TissueSafetyFrame tissueFrame))
            {
                return MoveNeedleThroughTissueSafeCorridor(
                    pass,
                    destination,
                    tissueFrame,
                    speed,
                    dt);
            }

            if (IsBetweenStitchTravel() && IsCircumferentialSuturePlan())
                return MoveNeedleAroundVesselExterior(pass, speed, dt);

            EnsureSafeTravelRoute(pass, destination);
            Vector3 entryDir = GetEntryDirection(pass);

            if (_safeTravelPhase <= 0)
            {
                if (!MoveNeedleTowardWithWholeNeedleCheck(
                        _safeTravelLiftTarget,
                        entryDir,
                        speed,
                        dt,
                        "SafeTravelLift"))
                    return false;
                _safeTravelPhase = 1;
                return false;
            }

            if (_safeTravelPhase == 1)
            {
                if (!MoveNeedleTowardWithWholeNeedleCheck(
                        _safeTravelCruiseTarget,
                        entryDir,
                        speed,
                        dt,
                        "SafeTravelCruise"))
                    return false;
                _safeTravelPhase = 2;
                return false;
            }

            if (!MoveNeedleTowardWithWholeNeedleCheck(
                    destination,
                    entryDir,
                    speed,
                    dt,
                    "SafeTravelApproach"))
                return false;

            ResetSafeTravelRoute();
            return true;
        }

        private bool MoveNeedleThroughLongitudinalLumen(AutoSuturePass sideBPass, float speed, float dt)
        {
            if (sideBPass == null || needleRig == null) return false;
            if (_lumenTravelPhase < 0)
            {
                if (!TryRefreshPassFromMaterialAnchors(sideBPass, false, out string reason) ||
                    !driver.Simulator.TryCopyCurrentSutureInnerBoundary(_currentInnerSideA, _currentInnerSideB))
                {
                    Error($"LumenRoutePlanning: current Inner material boundary unavailable {reason}");
                    return false;
                }
                if (!TryGetClosedLoopCenter(_currentInnerSideA, out Vector3 centerA, out _) ||
                    !TryGetClosedLoopCenter(_currentInnerSideB, out Vector3 centerB, out _) ||
                    (centerB - centerA).sqrMagnitude < 1e-12f)
                {
                    Error("LumenRoutePlanning: current Inner centers are degenerate");
                    return false;
                }
                if (!TryCaptureExteriorVesselSurface(out VesselSurfaceSnapshot surface, out reason))
                {
                    Error($"LumenRoutePlanning: {reason}");
                    return false;
                }
                _lumenTravelAxis = (centerB - centerA).normalized;
                _lumenTravelDestination = GetPreEntry(sideBPass);
                _lumenTravelEntryDirection = GetEntryDirection(sideBPass);
                Vector3 start = needleRig.GetSemanticTipPoint(), forward = needleRig.GetSemanticTipForward();
                if (!TryBuildLumenPoseRoute(centerA, _lumenTravelAxis, start, forward,
                        _lumenTravelDestination, _lumenTravelEntryDirection, EstimateNeedleWorldLength(), EstimateNeedleWorldRadius(),
                        GetWholeNeedlePosePlanningMargin(false), surface,
                        _lumenWaypoints, _lumenForwards, _lumenPhases, out int candidate, out reason))
                {
                    Error($"LumenRoutePlanning: pair={_currentPairIndex} {reason}");
                    return false;
                }
                _lumenWaypointIndex = 0;
                _lumenTravelPhase = _lumenPhases[0];
                Debug.Log($"[AutoSuture][LumenTravel] pair={_currentPairIndex} scope=NextPass geometry=CurrentInnerMaterialBoundary " +
                    $"validation=CurrentVesselMesh pivot=CapsuleCenter candidate={candidate} waypoints={_lumenWaypoints.Count} " +
                    $"axis={FormatVec(_lumenTravelAxis)} centerA={FormatVec(centerA)} centerB={FormatVec(centerB)} " +
                    $"start={FormatVec(start)} destination={FormatVec(_lumenTravelDestination)}");
            }
            int phase = _lumenPhases[_lumenWaypointIndex];
            if (_lumenTravelPhase != phase)
            {
                _lumenTravelPhase = phase;
                Debug.Log($"[AutoSuture][LumenTravel] pair={_currentPairIndex} phase={phase} stage={GetFocusRouteStageLabel()}");
            }
            if (!MoveNeedleAlongPlannedPose(_lumenWaypoints[_lumenWaypointIndex], _lumenForwards[_lumenWaypointIndex],
                    speed, dt, $"LumenPhase{phase}Waypoint{_lumenWaypointIndex}", false)) return false;
            _lumenWaypointIndex++;
            return _lumenWaypointIndex >= _lumenWaypoints.Count;
        }

        private static bool TryBuildLumenLocalPoseRoute(Vector3 startTip, Vector3 startForward,
            Vector3 destination, Vector3 destinationForward, float length, float radius, float margin,
            VesselSurfaceSnapshot surface, List<Vector3> tips, List<Vector3> forwards, out string reason)
        {
            tips.Clear(); forwards.Clear();
            reason = string.Empty;
            if (surface == null || startForward.sqrMagnitude < 0.5f || destinationForward.sqrMagnitude < 0.5f ||
                radius <= 0f || length < 2f * radius)
            {
                reason = "invalid local lumen pose geometry";
                return false;
            }
            AddPoseWaypoint(tips, forwards, destination, destinationForward.normalized);
            if (ValidateExteriorPoseRoute(Vector3.zero, Vector3.forward, 0f, length, radius, margin, Vector3.zero, 0f,
                    startTip, startForward.normalized, tips, forwards, out reason, null, 0f, surface)) return true;
            tips.Clear(); forwards.Clear();
            return false;
        }

        private static bool TryBuildLumenPoseRoute(Vector3 center, Vector3 axis, Vector3 startTip, Vector3 startForward,
            Vector3 destination, Vector3 destinationForward, float length, float radius, float margin,
            VesselSurfaceSnapshot surface, List<Vector3> tips, List<Vector3> forwards, List<int> phases,
            out int chosenCandidate, out string reason)
        {
            tips.Clear(); forwards.Clear(); phases.Clear();
            chosenCandidate = -1; reason = string.Empty;
            if (surface == null || axis.sqrMagnitude < 1e-10f || startForward.sqrMagnitude < 0.5f ||
                destinationForward.sqrMagnitude < 0.5f || radius <= 0f || length < 2f * radius)
            {
                reason = "invalid current lumen geometry or capsule";
                return false;
            }
            axis.Normalize(); startForward.Normalize(); destinationForward.Normalize();
            float half = length * .5f;
            Vector3 startCenter = startTip - startForward * half;
            Vector3 endCenter = destination - destinationForward * half;
            Vector3 laneStart = ProjectPointToLine(startCenter, center, axis);
            Vector3 laneEnd = ProjectPointToLine(endCenter, center, axis);
            Vector3 preferredAxis = Vector3.Dot(startForward + destinationForward, axis) < 0f ? -axis : axis;
            var failures = new List<string>();
            // Independently validate both axial orientations and turn locations.
            // A candidate must pass the entire sweep before it can be executed.
            for (int candidate = 0; candidate < 8; candidate++)
            {
                Vector3 transit = candidate < 4 ? preferredAxis : -preferredAxis;
                bool turnAtCurrent = (candidate & 1) != 0, turnNearDestination = (candidate & 2) != 0;
                Vector3 rotationStart = turnAtCurrent ? startCenter : laneStart;
                Vector3 rotationEnd = turnNearDestination ? endCenter : laneEnd;
                var route = new List<Vector3>(); var facing = new List<Vector3>(); var stages = new List<int>();
                AddLumenPose(route, facing, stages, rotationStart, startForward, half, 0);
                AddLumenTurn(route, facing, stages, rotationStart, startForward, transit, half, 1);
                AddLumenPose(route, facing, stages, rotationEnd, transit, half, 2);
                AddLumenTurn(route, facing, stages, rotationEnd, transit, destinationForward, half, 3);
                AddLumenPose(route, facing, stages, endCenter, destinationForward, half, 4);
                if (!ValidateExteriorPoseRoute(center, axis, 0f, length, radius, margin, Vector3.zero, 0f,
                        startTip, startForward, route, facing, out string failure, null, 0f, surface))
                {
                    failures.Add($"candidate={candidate} {failure}");
                    continue;
                }
                tips.AddRange(route); forwards.AddRange(facing); phases.AddRange(stages);
                chosenCandidate = candidate;
                return true;
            }
            reason = "all 8 pose candidates blocked: " + string.Join("; ", failures);
            return false;
        }

        private static void AddLumenPose(List<Vector3> tips, List<Vector3> forwards, List<int> phases,
            Vector3 center, Vector3 forward, float halfLength, int phase)
        {
            int count = tips.Count;
            AddPoseWaypoint(tips, forwards, center + forward * halfLength, forward);
            if (tips.Count > count) phases.Add(phase);
        }

        private static void AddLumenTurn(List<Vector3> tips, List<Vector3> forwards, List<int> phases,
            Vector3 center, Vector3 from, Vector3 to, float halfLength, int phase)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(from, to) / 3f));
            for (int i = 1; i <= steps; i++)
                AddLumenPose(tips, forwards, phases, center, InterpolateExteriorForward(from, to, i / (float)steps), halfLength, phase);
        }

        private static bool TryGetClosedLoopCenter(
            IReadOnlyList<Vector3> points,
            out Vector3 center,
            out int count)
        {
            center = Vector3.zero;
            count = GetClosedLoopUniquePointCount(points);
            if (count < 3)
                return false;
            for (int i = 0; i < count; i++)
                center += points[i];
            center /= count;
            return true;
        }

        private static Vector3 ProjectPointToLine(
            Vector3 point,
            Vector3 linePoint,
            Vector3 lineDirection)
        {
            Vector3 direction = NormalizeOrFallback(
                lineDirection,
                Vector3.forward);
            return linePoint + direction * Vector3.Dot(
                point - linePoint,
                direction);
        }

        private static float GetMinimumLoopRadius(
            IReadOnlyList<Vector3> points,
            int count,
            Vector3 center,
            Vector3 axis)
        {
            float result = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                float radius = Vector3.ProjectOnPlane(
                    points[i] - center,
                    axis).magnitude;
                result = Mathf.Min(result, radius);
            }
            return float.IsInfinity(result) ? 0f : result;
        }

        private bool MoveNeedleBodyToSafeDeparture(
            AutoSuturePass pass,
            Vector3 destination,
            float speed,
            float dt)
        {
            if (pass == null || needleRig == null)
                return false;

            if (_needleBodyDeparturePhase < 0 ||
                _needleBodyDepartureState != _state ||
                Vector3.Distance(_needleBodyDepartureDestination, destination) > 0.0002f)
            {
                Vector3 currentTip = needleRig.GetSemanticTipPoint();
                _needleBodyDepartureForward = NormalizeOrFallback(
                    needleRig.GetSemanticTipForward(),
                    GetEntryDirection(pass));
                float bodyClearance = GetNeedleBodyTravelClearance();
                _needleBodyDepartureTargetForward = GetEntryDirection(pass);
                _needleBodyDepartureTarget =
                    currentTip +
                    _needleBodyDepartureForward *
                    bodyClearance;
                _needleBodyDepartureSafeLayerTarget =
                    _needleBodyDepartureTarget;
                _needleBodyDepartureHasSafeLayer = false;
                if (TryGetTissueSafetyFrame(out TissueSafetyFrame tissueFrame))
                {
                    float safeTipProjection =
                        tissueFrame.obstacleMaxProjection +
                        bodyClearance +
                        Mathf.Max(0f, tissueTravelMargin);
                    _needleBodyDepartureSafeLayerTarget =
                        LiftPointToProjection(
                            _needleBodyDepartureTarget,
                            tissueFrame.freeNormal,
                            safeTipProjection);
                    _needleBodyDepartureHasSafeLayer =
                        Vector3.SqrMagnitude(
                            _needleBodyDepartureSafeLayerTarget -
                            _needleBodyDepartureTarget) >
                        1e-12f;
                    _needleBodyDepartureTargetForward =
                        GetTissueTransitForward(
                            GetEntryDirection(pass),
                            tissueFrame.freeNormal);
                }
                _needleBodyDepartureState = _state;
                _needleBodyDepartureDestination = destination;
                _needleBodyDeparturePhase = 0;
                Debug.Log(
                    $"[AutoSuture][NeedleBodyDeparture] pair={_currentPairIndex} pass={_currentPassInPair} " +
                    $"phase=LeaveWall clearance={bodyClearance:F5} " +
                    $"needleLength={EstimateNeedleWorldLength():F5} " +
                    $"needleRadius={EstimateNeedleWorldRadius():F5} margin={Mathf.Max(0f, needleBodyTravelMargin):F5} " +
                    $"from={FormatVec(currentTip)} wallClearTarget={FormatVec(_needleBodyDepartureTarget)} " +
                    $"safeLayerTarget={FormatVec(_needleBodyDepartureSafeLayerTarget)} " +
                    $"poseForward={FormatVec(_needleBodyDepartureForward)} " +
                    $"transitForward={FormatVec(_needleBodyDepartureTargetForward)}");
            }

            if (_needleBodyDeparturePhase == 0)
            {
                if (!MoveNeedleTowardWithWholeNeedleCheck(
                        _needleBodyDepartureTarget,
                        _needleBodyDepartureForward,
                        speed,
                        dt,
                        "DepartureLeaveWall"))
                {
                    return false;
                }

                _needleBodyDeparturePhase =
                    _needleBodyDepartureHasSafeLayer
                        ? 1
                        : 2;
                Debug.Log(
                    $"[AutoSuture][NeedleBodyDeparture] pair={_currentPairIndex} pass={_currentPassInPair} " +
                    $"phase={(_needleBodyDepartureHasSafeLayer ? "LiftToSafeLayer" : "RotateAtClearance")}");
                return false;
            }

            if (_needleBodyDeparturePhase == 1)
            {
                if (!MoveNeedleTowardWithWholeNeedleCheck(
                        _needleBodyDepartureSafeLayerTarget,
                        _needleBodyDepartureForward,
                        speed,
                        dt,
                        "DepartureLiftToSafeLayer"))
                {
                    return false;
                }

                _needleBodyDeparturePhase = 2;
                Debug.Log(
                    $"[AutoSuture][NeedleBodyDeparture] pair={_currentPairIndex} pass={_currentPassInPair} " +
                    "phase=RotateAtSafeLayer");
                return false;
            }

            if (_needleBodyDeparturePhase == 2)
            {
                Vector3 currentTip = needleRig.GetSemanticTipPoint();
                if (!MoveNeedleTowardWithWholeNeedleCheck(
                        currentTip,
                        _needleBodyDepartureTargetForward,
                        speed,
                        dt,
                        "DepartureRotateAtSafeLayer"))
                {
                    return false;
                }

                _needleBodyDeparturePhase = 3;
                Debug.Log(
                    $"[AutoSuture][NeedleBodyDeparture] pair={_currentPairIndex} pass={_currentPassInPair} " +
                    "phase=ReadyForTransit wholeNeedleClear=1");
            }

            return true;
        }

        private bool MoveNeedleThroughTissueSafeCorridor(
            AutoSuturePass pass,
            Vector3 destination,
            TissueSafetyFrame tissueFrame,
            float speed,
            float dt)
        {
            if (_safeTravelPhase < 0 ||
                _safeTravelState != _state ||
                Vector3.Distance(
                    _safeTravelDestination,
                    destination) > 0.0002f ||
                _exteriorTravelWaypoints.Count == 0 ||
                _exteriorTravelWaypoints.Count !=
                _exteriorTravelForwards.Count)
            {
                float wholeNeedleClearance =
                    GetNeedleBodyTravelClearance() +
                    Mathf.Max(0f, tissueTravelMargin);
                if (!TryBuildTissueSafeCorridor(
                        needleRig.GetSemanticTipPoint(),
                        destination,
                        needleRig.GetSemanticTipForward(),
                        GetEntryDirection(pass),
                        tissueFrame.freeNormal,
                        tissueFrame.obstacleMaxProjection,
                        wholeNeedleClearance,
                        _exteriorTravelWaypoints,
                        _exteriorTravelForwards,
                        out float safeTipProjection,
                        out Vector3 approachStandOff))
                {
                    _tissueSafetyFrameCached = false;
                    return false;
                }

                _exteriorTravelWaypointIndex = 0;
                _safeTravelState = _state;
                _safeTravelDestination = destination;
                _safeTravelPhase = 0;
                Debug.Log(
                    $"[AutoSuture][TissueSafeRoute] pair={_currentPairIndex} pass={_currentPassInPair} " +
                    $"state={_state} stage=DepartureTransitApproach " +
                    $"surface={tissueAvoidanceSurface.name} vertices={tissueFrame.vertexCount} " +
                    $"freeNormal={FormatVec(tissueFrame.freeNormal)} " +
                    $"surfaceProjection=[{tissueFrame.surfaceMinProjection:F5},{tissueFrame.surfaceMaxProjection:F5}] " +
                    $"vesselMaxProjection={tissueFrame.vesselMaxProjection:F5} " +
                    $"obstacleMaxProjection={tissueFrame.obstacleMaxProjection:F5} " +
                    $"safeTipProjection={safeTipProjection:F5} wholeNeedleClearance={wholeNeedleClearance:F5} " +
                    $"approachStandOff={FormatVec(approachStandOff)} " +
                    $"waypoints={_exteriorTravelWaypoints.Count}");
            }

            if (_exteriorTravelWaypointIndex >=
                _exteriorTravelWaypoints.Count)
            {
                ResetSafeTravelRoute();
                return true;
            }

            Vector3 target =
                _exteriorTravelWaypoints[_exteriorTravelWaypointIndex];
            Vector3 targetForward =
                _exteriorTravelForwards[_exteriorTravelWaypointIndex];
            string travelStage = GetTissueSafeTravelStage(
                _exteriorTravelWaypointIndex,
                _exteriorTravelWaypoints.Count);
            if (!MoveNeedleTowardWithWholeNeedleCheck(
                    target,
                    targetForward,
                    speed,
                    dt,
                    travelStage))
            {
                return false;
            }

            Debug.Log(
                $"[AutoSuture][TissueSafePose] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"waypoint={_exteriorTravelWaypointIndex + 1}/{_exteriorTravelWaypoints.Count} stage={travelStage} " +
                $"tip={FormatVec(target)} forward={FormatVec(targetForward)} " +
                $"tipProjection={Vector3.Dot(target, tissueFrame.freeNormal):F5}");
            _exteriorTravelWaypointIndex++;
            if (_exteriorTravelWaypointIndex <
                _exteriorTravelWaypoints.Count)
            {
                return false;
            }

            ResetSafeTravelRoute();
            return true;
        }

        private bool MoveNeedleAroundVesselExterior(AutoSuturePass pass, float speed, float dt)
        {
            if (pass == null || needleRig == null)
                return false;

            // Plan this next-pass transfer once from the latest material shape.
            // Replanning from phase zero every frame can starve orbit/rotation
            // progress while the cloth settles. Recheck at the pre-entry handoff;
            // every executed step still checks live vessel triangle positions.
            if ((_safeTravelPhase < 0 || _safeTravelState != _state || _exteriorFramePass != pass) &&
                !TryRefreshNextExteriorGeometry(pass, out string refreshReason))
            {
                Error($"Next-pass exterior geometry unavailable: {refreshReason}");
                return false;
            }
            Vector3 destination = GetPreEntry(pass);
            if (!TryEnsureExteriorTravelRoute(pass, destination, out string failureReason))
            {
                Error($"Exterior inter-stitch travel unavailable: {failureReason}");
                return false;
            }

            if (_exteriorTravelWaypointIndex >= _exteriorTravelWaypoints.Count)
            {
                ResetSafeTravelRoute();
                return true;
            }

            Vector3 target = _exteriorTravelWaypoints[_exteriorTravelWaypointIndex];
            if (!MoveNeedleAlongExteriorPose(
                    target,
                    _exteriorTravelForwards[_exteriorTravelWaypointIndex],
                    speed,
                    dt,
                    $"ExteriorWaypoint{_exteriorTravelWaypointIndex + 1}"))
                return false;

            _exteriorTravelWaypointIndex++;
            if (_exteriorTravelWaypointIndex < _exteriorTravelWaypoints.Count)
                return false;

            ResetSafeTravelRoute();
            return true;
        }

        private bool MoveNeedleAlongExteriorPose(Vector3 targetTip, Vector3 targetForward,
            float speed, float dt, string stage)
        {
            return MoveNeedleAlongPlannedPose(targetTip, targetForward, speed, dt, stage, true);
        }

        private bool MoveNeedleAlongPlannedPose(Vector3 targetTip, Vector3 targetForward,
            float speed, float dt, string stage, bool checkTissue)
        {
            Vector3 currentTip = needleRig.GetSemanticTipPoint();
            Vector3 currentForward = needleRig.GetSemanticTipForward();
            float linearStep = AutoSutureLifecyclePolicy.ComputeMotionStep(
                Mathf.Max(0.001f, speed), dt, GetEffectiveMaxAutoPoseStep());
            float angularStep = Mathf.Min(angularSpeedDeg * Mathf.Max(0f, dt), Mathf.Max(0.1f, maxAutoPoseAngleStepDeg));
            float fraction = ExteriorPoseStepFraction(Vector3.Distance(currentTip, targetTip),
                Vector3.Angle(currentForward, targetForward), linearStep, angularStep);
            Vector3 nextTip = Vector3.Lerp(currentTip, targetTip, fraction);
            Vector3 nextForward = InterpolateExteriorForward(currentForward.normalized, targetForward.normalized, fraction);
            if (!TryValidateWholeNeedleTravel(currentTip, currentForward, nextTip, nextForward, checkTissue, out string reason))
            {
                Error($"Whole-needle travel blocked: stage={stage} {reason}");
                return false;
            }
            if (!needleRig.TrySetAutoSutureTipPose(nextTip, nextForward, GetContinuousPoseUp(nextForward)))
            {
                Error($"Planned pose application failed: stage={stage}");
                return false;
            }
            // Advance only at the planned pose, not the general 4-degree travel
            // tolerance (larger than the planner's 3-degree waypoint spacing).
            return fraction >= 1f;
        }

        private static float ExteriorPoseStepFraction(float distance, float angle, float linearStep, float angularStep)
        {
            float positionFraction = distance > 1e-7f ? Mathf.Max(0f, linearStep) / distance : 1f;
            float rotationFraction = angle > 0.001f ? Mathf.Max(0f, angularStep) / angle : 1f;
            return Mathf.Clamp01(Mathf.Min(positionFraction, rotationFraction));
        }

        private bool TryEnsureExteriorTravelRoute(AutoSuturePass pass, Vector3 destination, out string failureReason)
        {
            failureReason = string.Empty;
            if (_safeTravelPhase >= 0 &&
                _safeTravelState == _state &&
                !ExteriorGeometryChanged(destination, GetEntryDirection(pass)) &&
                _exteriorTissueRevision == _localTissueRevision &&
                _exteriorTravelWaypoints.Count > 0 &&
                _exteriorTravelWaypoints.Count == _exteriorTravelForwards.Count)
                return true;

            if (safeTravelClearance <= 1e-6f)
            {
                failureReason = "safeTravelClearance must be positive";
                return false;
            }

            if (_exteriorFramePass != pass)
            {
                failureReason = "missing current-pass material boundary snapshot";
                return false;
            }
            Vector3 centerWorld = _exteriorCenter;
            Vector3 planeNormalWorld = _exteriorAxis;
            float outerRadiusWorld = _exteriorRadius;

            float needleLength = EstimateNeedleWorldLength();
            float needleRadius = EstimateNeedleWorldRadius();
            if (needleLength <= 1e-6f || needleRadius <= 1e-6f)
            {
                failureReason = "needle CapsuleCollider dimensions are unavailable";
                return false;
            }
            if (!TryRefreshLocalTissueSurface(true, out failureReason))
                return false;
            if (!TryCaptureExteriorVesselSurface(out VesselSurfaceSnapshot vesselSurface, out failureReason))
                return false;
            Vector3 startTip = needleRig.GetSemanticTipPoint();
            Vector3 startForward = needleRig.GetSemanticTipForward().normalized;
            Vector3 capsuleA = startTip - startForward * needleRadius;
            Vector3 capsuleB = startTip - startForward * (needleLength - needleRadius);
            float startCylinderGap = MinimumSegmentPlaneRadialDistance(capsuleA, capsuleB, centerWorld, planeNormalWorld) - outerRadiusWorld - needleRadius;
            float startMeshGap = vesselSurface.CapsuleGap(capsuleA, capsuleB, needleRadius,
                Mathf.Max(needleBodyTravelMargin, wholeNeedleTravelCollisionMargin), true,
                out int nearestTriangle, out int nearTriangleCount);
            // Diagnostic only: keep existing travel geometry unchanged, but
            // expose any disagreement with the collider used by portal/Hist.
            float proxyLength = float.NaN, proxyMeshGap = float.NaN;
            Transform needleTransform = targetNeedle != null ? targetNeedle : needleRig.targetNeedle;
            UnityCapsuleColliderProxy proxy = needleTransform != null
                ? needleTransform.GetComponent<UnityCapsuleColliderProxy>() : null;
            if (proxy != null)
            {
                var desc = proxy.desc;
                Vector3 proxyA = new Vector3(desc.c0.x, desc.c0.y, desc.c0.z);
                Vector3 proxyB = proxyA + new Vector3(desc.axis.x, desc.axis.y, desc.axis.z) * desc.axis.w;
                proxyLength = desc.axis.w + desc.radius * 2f;
                proxyMeshGap = vesselSurface.CapsuleGap(proxyA, proxyB, desc.radius,
                    wholeNeedleTravelCollisionMargin, true, out _, out _);
            }
            Debug.Log($"[AutoSuture][ExteriorStartGeometry] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"tip={FormatVec(startTip)} forward={FormatVec(startForward)} " +
                $"length={needleLength:F5} radius={needleRadius:F5} tailCenter={FormatVec(capsuleB)} " +
                $"cylinderGap={startCylinderGap:F5} meshGap={startMeshGap:F5} " +
                $"nearestTri={nearestTriangle} nearTriangles={nearTriangleCount} triangles={vesselSurface.TriangleCount} " +
                $"axis={FormatVec(planeNormalWorld)} center={FormatVec(centerWorld)} outerRadius={outerRadiusWorld:F5} " +
                $"proxyAvailable={(proxy != null ? 1 : 0)} proxyLength={proxyLength:F5} proxyMeshGap={proxyMeshGap:F5}");
            bool hasTissue = _localTissueSurface != null;
            var worldWaypoints = new List<Vector3>();
            var worldForwards = new List<Vector3>();
            if (!TryBuildExteriorPoseRouteCore(
                    centerWorld,
                    planeNormalWorld,
                    startTip,
                    startForward,
                    destination,
                    GetEntryDirection(pass),
                    outerRadiusWorld,
                    needleLength,
                    needleRadius,
                    safeTravelClearance,
                    GetWholeNeedlePosePlanningMargin(true),
                    hasTissue ? _localTissueSurface.normal : Vector3.zero,
                    0f,
                    worldWaypoints,
                    worldForwards,
                    out float arcDegrees,
                    out failureReason, _localTissueSurface, Mathf.Max(0f, tissueTravelMargin), vesselSurface))
            {
                return false;
            }

            _exteriorTravelWaypoints.Clear();
            _exteriorTravelWaypoints.AddRange(worldWaypoints);
            _exteriorTravelForwards.Clear();
            _exteriorTravelForwards.AddRange(worldForwards);

            _exteriorTravelWaypointIndex = 0;
            _safeTravelState = _state;
            _safeTravelDestination = destination;
            _safeTravelPhase = 0;
            _exteriorRouteCenter = centerWorld;
            _exteriorRouteAxis = planeNormalWorld;
            _exteriorRouteRadius = outerRadiusWorld;
            _exteriorRouteForward = GetEntryDirection(pass);
            _exteriorTissueRevision = _localTissueRevision;
            Debug.Log(
                $"[AutoSuture][ExteriorTravel] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"stage=AxisOrbit axis={FormatVec(planeNormalWorld)} center={FormatVec(centerWorld)} " +
                $"outerRadiusWorld={outerRadiusWorld:F5} arcDeg={arcDegrees:F1} " +
                $"tissueConstraint={(hasTissue ? 1 : 0)} tissueMode=LocalMesh posture=Longitudinal " +
                $"waypoints={_exteriorTravelWaypoints.Count} scope=NextPass geometry=CurrentMaterialBoundary validation=CurrentVesselMesh " +
                $"entry={FormatVec(pass.entryPointWorld)} dest={FormatVec(destination)} lumenUnchanged=1");
            return true;
        }

        private bool UsesCurrentExteriorGeometry(AutoSuturePass pass)
        {
            return pass != null && safeTravelAroundVessel && safeTravelClearance > 1e-6f && IsCircumferentialSuturePlan() &&
                pass.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner;
        }

        private bool ExteriorGeometryChanged(Vector3 destination, Vector3 forward)
        {
            return HasExteriorGeometryChanged(_safeTravelDestination, _exteriorRouteForward,
                _exteriorRouteCenter, _exteriorRouteAxis, _exteriorRouteRadius,
                destination, forward, _exteriorCenter, _exteriorAxis, _exteriorRadius);
        }

        private static bool HasExteriorGeometryChanged(Vector3 oldDestination, Vector3 oldForward,
            Vector3 oldCenter, Vector3 oldAxis, float oldRadius,
            Vector3 destination, Vector3 forward, Vector3 center, Vector3 axis, float radius)
        {
            // This is cache invalidation, not collision tolerance. Collision
            // checks still use current triangles on every executed motion step.
            return Vector3.Distance(oldDestination, destination) > 0.0002f ||
                Vector3.Angle(oldForward, forward) > 0.1f ||
                Vector3.Distance(oldCenter, center) + Mathf.Abs(oldRadius - radius) +
                Vector3.Angle(oldAxis, axis) * Mathf.Deg2Rad * Mathf.Max(radius, oldRadius) > 0.0002f;
        }

        private bool TryRefreshNextExteriorGeometry(AutoSuturePass pass, out string reason)
        {
            _exteriorFramePass = null;
            if (!TryRefreshLocalTissueSurface(false, out reason))
                return false;
            if (!TryRefreshPassFromMaterialAnchors(pass, false, out reason))
                return false;
            if (!driver.Simulator.TryCopyCurrentSutureOuterBoundary(_currentExteriorSideA, _currentExteriorSideB))
            {
                reason = "current material Outer boundary is missing or invalid";
                return false;
            }
            if (!TryCalculateExteriorOuterFrame(_currentExteriorSideA, _currentExteriorSideB,
                    out _exteriorCenter, out _exteriorAxis, out _exteriorRadius))
            {
                reason = "current material Outer boundary frame is degenerate";
                return false;
            }
            _exteriorFramePass = pass;
            return true;
        }

        private static bool TryCalculateExteriorOuterFrame(IReadOnlyList<Vector3> sideA,
            IReadOnlyList<Vector3> sideB, out Vector3 center, out Vector3 axis, out float radius)
        {
            center = axis = Vector3.zero;
            radius = 0f;
            if (sideA.Count < 3 || sideB.Count < 3)
                return false;
            Vector3 centerA = Vector3.zero, centerB = Vector3.zero;
            for (int i = 0; i < sideA.Count; i++) centerA += sideA[i];
            for (int i = 0; i < sideB.Count; i++) centerB += sideB[i];
            centerA /= sideA.Count;
            centerB /= sideB.Count;
            center = (centerA + centerB) * 0.5f;
            axis = centerB - centerA;
            if (axis.sqrMagnitude <= 1e-12f)
                return false;
            axis.Normalize();
            // Measure about the same axis the planner uses, including any
            // lateral displacement of either bank during pull-line deformation.
            for (int i = 0; i < sideA.Count; i++)
                radius = Mathf.Max(radius, Vector3.ProjectOnPlane(sideA[i] - center, axis).magnitude);
            for (int i = 0; i < sideB.Count; i++)
                radius = Mathf.Max(radius, Vector3.ProjectOnPlane(sideB[i] - center, axis).magnitude);
            return radius > 1e-6f;
        }

        // Plan capsule centers, not just tips: a tip-only arc can sweep the tail
        // through the wall. All candidate edges (including departure/arrival)
        // use the cylinder for routing, but actual deformed vessel triangles
        // decide wall clearance. A maximum-radius envelope is not the wall.
        private static bool TryBuildExteriorPoseRoute(
            Vector3 center, Vector3 axis, Vector3 startTip, Vector3 startForward,
            Vector3 destinationTip, Vector3 destinationForward,
            float outerRadius, float needleLength, float needleRadius,
            float preferredClearance, float margin, Vector3 tissueNormal,
            float tissueProjection, List<Vector3> tips, List<Vector3> forwards,
            out float arcDegrees, out string reason)
        {
            return TryBuildExteriorPoseRouteCore(center, axis, startTip, startForward,
                destinationTip, destinationForward, outerRadius, needleLength, needleRadius,
                preferredClearance, margin, tissueNormal, tissueProjection, tips, forwards,
                out arcDegrees, out reason, null, 0f);
        }

        private static bool TryBuildExteriorPoseRouteCore(
            Vector3 center, Vector3 axis, Vector3 startTip, Vector3 startForward,
            Vector3 destinationTip, Vector3 destinationForward,
            float outerRadius, float needleLength, float needleRadius,
            float preferredClearance, float margin, Vector3 tissueNormal,
            float tissueProjection, List<Vector3> tips, List<Vector3> forwards,
            out float arcDegrees, out string reason, TissueSurfaceSnapshot tissueSurface, float tissueMargin,
            VesselSurfaceSnapshot vesselSurface = null)
        {
            arcDegrees = 0f;
            reason = string.Empty;
            tips.Clear();
            forwards.Clear();
            if (axis.sqrMagnitude < 1e-10f || outerRadius <= 0f ||
                needleLength < needleRadius * 2f || needleRadius <= 0f ||
                startForward.sqrMagnitude < 1e-10f || destinationForward.sqrMagnitude < 1e-10f)
            {
                reason = "invalid exterior axis or capsule dimensions/pose";
                return false;
            }
            axis.Normalize();
            tissueNormal.Normalize();
            startForward.Normalize();
            destinationForward.Normalize();
            float halfLength = needleLength * 0.5f;
            Vector3 startCenter = startTip - startForward * halfLength;
            // End at a stand-off behind the pre-entry pose; the last translation
            // is coaxial with entry, so no sideways sweep happens at the wall.
            Vector3 standOffTip = destinationTip - destinationForward * Mathf.Max(needleRadius * 2f, margin);
            Vector3 endCenter = standOffTip - destinationForward * halfLength;
            Vector3 startRadial = Vector3.ProjectOnPlane(startCenter - center, axis);
            Vector3 endRadial = Vector3.ProjectOnPlane(endCenter - center, axis);
            if (startRadial.sqrMagnitude < 1e-10f || endRadial.sqrMagnitude < 1e-10f)
            {
                reason = "exterior capsule center lies on vessel axis";
                return false;
            }
            Vector3 basisX = startRadial.normalized;
            Vector3 basisY = Vector3.Cross(axis, basisX).normalized;
            float angle = Mathf.Atan2(Vector3.Dot(endRadial, basisY), Vector3.Dot(endRadial, basisX));
            Vector3 transitForward = Vector3.Dot(startForward + destinationForward, axis) < 0f ? -axis : axis;
            float startAxial = Vector3.Dot(startCenter - center, axis);
            float endAxial = Vector3.Dot(endCenter - center, axis);
            float radius = outerRadius + Mathf.Max(preferredClearance, needleRadius + margin);
            var failures = new List<string>();
            // Evaluate the short and long arcs under the SAME constraints.
            // A rejected arc never becomes an unchecked straight-line fallback.
            for (int candidate = 0; candidate < 2; candidate++)
            {
                float delta = candidate == 0 ? angle : angle + (angle >= 0f ? -2f : 2f) * Mathf.PI;
                var candidateTips = new List<Vector3>();
                var candidateForwards = new List<Vector3>();
                string failure = string.Empty;
                bool valid = true;
                for (int phase = 0; phase < 3 && valid; phase++)
                {
                    Vector3 from = phase == 0 ? startForward : transitForward;
                    Vector3 to = phase == 2 ? destinationForward : transitForward;
                    float phaseAngle = phase == 1 ? Mathf.Abs(delta) * Mathf.Rad2Deg : Vector3.Angle(from, to);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(phaseAngle / 3f));
                    for (int step = 0; step <= steps && valid; step++)
                    {
                        float t = step / (float)steps;
                        float theta = phase == 0 ? 0f : (phase == 1 ? delta * t : delta);
                        float axial = phase == 0 ? startAxial : (phase == 1 ? Mathf.Lerp(startAxial, endAxial, t) : endAxial);
                        Vector3 radial = basisX * Mathf.Cos(theta) + basisY * Mathf.Sin(theta);
                        Vector3 forward = InterpolateExteriorForward(from, to, t);
                        Vector3 axisPoint = center + axis * axial;
                        // Support-plane bound encloses the entire capsule,
                        // including intermediate rotation poses at the lower side.
                        float halfSegment = halfLength - needleRadius;
                        float requiredRadius = outerRadius + needleRadius + margin +
                            halfSegment * Mathf.Abs(Vector3.Dot(forward, radial));
                        float poseRadius = Mathf.Max(radius, requiredRadius);
                        if (!TryResolveExteriorTissueRadius(axisPoint, radial, forward,
                                halfSegment, needleRadius, margin, requiredRadius, ref poseRadius,
                                tissueNormal, tissueProjection, tissueSurface, tissueMargin, out string gapReason))
                        {
                            valid = false;
                            failure = $"phase={phase} angle={theta * Mathf.Rad2Deg:F1} {gapReason}";
                            break;
                        }
                        AddPoseWaypoint(candidateTips, candidateForwards,
                            axisPoint + radial * poseRadius + forward * halfLength, forward);
                    }
                }
                if (valid)
                {
                    AddPoseWaypoint(candidateTips, candidateForwards, standOffTip, destinationForward);
                    AddPoseWaypoint(candidateTips, candidateForwards, destinationTip, destinationForward);
                    valid = ValidateExteriorPoseRoute(center, axis, outerRadius,
                        needleLength, needleRadius, margin, tissueNormal, tissueProjection,
                        startTip, startForward, candidateTips, candidateForwards, out failure, tissueSurface, tissueMargin, vesselSurface);
                }
                if (!valid)
                {
                    failures.Add($"arc={delta * Mathf.Rad2Deg:F1}: {failure}");
                    continue;
                }
                tips.AddRange(candidateTips);
                forwards.AddRange(candidateForwards);
                arcDegrees = delta * Mathf.Rad2Deg;
                return true;
            }
            reason = "ExteriorRoutePlanning: " + string.Join("; ", failures);
            return false;
        }

        private static Vector3 InterpolateExteriorForward(Vector3 from, Vector3 to, float t)
        {
            float dot = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
            if (dot > 0.9999f)
                return Vector3.Lerp(from, to, t).normalized;
            Vector3 tangent = to - from * dot;
            if (tangent.sqrMagnitude < 1e-10f)
            {
                tangent = Vector3.Cross(from, Vector3.right);
                if (tangent.sqrMagnitude < 1e-10f)
                    tangent = Vector3.Cross(from, Vector3.up);
            }
            tangent.Normalize();
            float angle = Mathf.Acos(dot) * t;
            return (from * Mathf.Cos(angle) + tangent * Mathf.Sin(angle)).normalized;
        }

        private float GetWholeNeedlePosePlanningMargin(bool exterior)
        {
            // The sweep validator uses half this budget as its required gap.
            // Post-Hist release must satisfy the same gap before transport.
            return Mathf.Max(needleBodyTravelMargin, exterior
                ? wholeNeedleTravelCollisionMargin + poseArriveDistance * 2f
                : wholeNeedleTravelCollisionMargin * 2f);
        }

        private static bool ValidateExteriorPoseRoute(
            Vector3 center, Vector3 axis, float outerRadius,
            float length, float radius, float margin, Vector3 tissueNormal, float tissueProjection,
            Vector3 startTip, Vector3 startForward, List<Vector3> tips, List<Vector3> forwards,
            out string reason, TissueSurfaceSnapshot tissueSurface = null, float tissueMargin = 0f,
            VesselSurfaceSnapshot vesselSurface = null)
        {
            reason = string.Empty;
            Vector3 previousTip = startTip;
            Vector3 previousForward = startForward;
            for (int edge = 0; edge < tips.Count; edge++)
            {
                float sweep = Vector3.Distance(previousTip, tips[edge]) +
                    Vector3.Angle(previousForward, forwards[edge]) * Mathf.Deg2Rad * length;
                // Bound spatial sampling error well below the planning margin.
                int samples = Mathf.Max(1, Mathf.CeilToInt(sweep / Mathf.Max(0.0001f, Mathf.Min(radius * 0.25f, margin * 0.25f))));
                if (samples > 12000)
                {
                    reason = $"edge={edge} excessive travel extent";
                    return false;
                }
                for (int sample = 0; sample <= samples; sample++)
                {
                    float t = sample / (float)samples;
                    Vector3 tip = Vector3.Lerp(previousTip, tips[edge], t);
                    Vector3 forward = InterpolateExteriorForward(previousForward, forwards[edge], t);
                    Vector3 a = tip - forward * radius;
                    Vector3 b = tip - forward * (length - radius);
                    float cylinderGap = MinimumSegmentPlaneRadialDistance(a, b, center, axis) - outerRadius - radius;
                    int vesselTriangle = -1;
                    float wallGap = vesselSurface != null
                        ? vesselSurface.CapsuleGap(a, b, radius, margin * 0.5f, false, out vesselTriangle, out _)
                        : cylinderGap; // Analytic cylinder fixtures; runtime always supplies a mesh snapshot.
                    float tissueGap = tissueSurface != null
                        ? tissueSurface.CapsuleGap(a, b, radius, tissueMargin, margin * 0.5f)
                        : tissueNormal.sqrMagnitude > 0.5f
                            ? Mathf.Min(Vector3.Dot(a, tissueNormal), Vector3.Dot(b, tissueNormal)) - radius - tissueProjection
                            : float.PositiveInfinity;
                    if (wallGap < margin * 0.5f || tissueGap < margin * 0.5f)
                    {
                        reason = $"edge={edge} sample={sample}/{samples} wallGap={wallGap:F5} tissueGap={tissueGap:F5} " +
                            $"wallGeometry={(vesselSurface != null ? "CurrentVesselMesh" : "AnalyticCylinder")} " +
                            $"vesselTri={vesselTriangle} cylinderGap={cylinderGap:F5}";
                        return false;
                    }
                }
                previousTip = tips[edge];
                previousForward = forwards[edge];
            }
            return true;
        }

        private bool TryGetCircumferentialOuterFrame(
            out Vector3 centerWorld,
            out Vector3 planeNormalWorld,
            out float outerRadiusWorld,
            out string failureReason)
        {
            centerWorld = Vector3.zero;
            planeNormalWorld = Vector3.zero;
            outerRadiusWorld = 0f;
            failureReason = string.Empty;
            if (driver == null)
            {
                failureReason = "missing driver";
                return false;
            }

            IReadOnlyList<Vector3> sideA = driver.SutureSideAOuterBoundaryWorld;
            IReadOnlyList<Vector3> sideB = driver.SutureSideBOuterBoundaryWorld;
            int countA = GetClosedLoopUniquePointCount(sideA);
            int countB = GetClosedLoopUniquePointCount(sideB);
            if (countA < 3 || countB < 3)
            {
                failureReason = $"invalid Outer guide loops: sideA={countA} sideB={countB}";
                return false;
            }

            Vector3 centerA = Vector3.zero;
            Vector3 centerB = Vector3.zero;
            for (int i = 0; i < countA; i++)
                centerA += sideA[i];
            for (int i = 0; i < countB; i++)
                centerB += sideB[i];
            centerA /= countA;
            centerB /= countB;
            centerWorld = 0.5f * (centerA + centerB);
            planeNormalWorld = centerB - centerA;
            if (planeNormalWorld.sqrMagnitude <= 1e-12f)
            {
                failureReason =
                    "Outer guide bank centers do not define a cut-plane normal";
                return false;
            }
            planeNormalWorld.Normalize();

            for (int i = 0; i < countA; i++)
            {
                float radial = Vector3.ProjectOnPlane(
                    sideA[i] - centerA,
                    planeNormalWorld).magnitude;
                outerRadiusWorld = Mathf.Max(outerRadiusWorld, radial);
            }
            for (int i = 0; i < countB; i++)
            {
                float radial = Vector3.ProjectOnPlane(
                    sideB[i] - centerB,
                    planeNormalWorld).magnitude;
                outerRadiusWorld = Mathf.Max(outerRadiusWorld, radial);
            }

            if (outerRadiusWorld <= 1e-6f)
            {
                failureReason = "Outer guide radius is degenerate";
                return false;
            }

            return true;
        }

        private static bool TryBuildExteriorArcInPlane(
            Vector3 centerWorld,
            Vector3 planeNormalWorld,
            Vector3 startWorld,
            Vector3 destinationWorld,
            float outerRadiusWorld,
            float clearanceWorld,
            List<Vector3> waypoints,
            out float cruiseRadiusWorld,
            out float minimumArcRadiusWorld)
        {
            cruiseRadiusWorld = 0f;
            minimumArcRadiusWorld = 0f;
            if (waypoints == null ||
                outerRadiusWorld <= 1e-6f ||
                clearanceWorld <= 1e-6f ||
                planeNormalWorld.sqrMagnitude <= 1e-12f)
                return false;

            waypoints.Clear();
            Vector3 normal = planeNormalWorld.normalized;
            Vector3 startOffset = startWorld - centerWorld;
            Vector3 destinationOffset = destinationWorld - centerWorld;
            Vector3 startRadial =
                Vector3.ProjectOnPlane(startOffset, normal);
            Vector3 destinationRadial =
                Vector3.ProjectOnPlane(destinationOffset, normal);
            if (startRadial.sqrMagnitude <= 1e-10f ||
                destinationRadial.sqrMagnitude <= 1e-10f)
                return false;

            Vector3 basisX = startRadial.normalized;
            Vector3 basisY = Vector3.Cross(normal, basisX).normalized;
            float destinationAngle = Mathf.Atan2(
                Vector3.Dot(destinationRadial, basisY),
                Vector3.Dot(destinationRadial, basisX));
            float deltaAngle = Mathf.DeltaAngle(
                0f,
                destinationAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            cruiseRadiusWorld = Mathf.Max(
                outerRadiusWorld + clearanceWorld,
                startRadial.magnitude,
                destinationRadial.magnitude);

            float safeRatio = Mathf.Clamp(
                outerRadiusWorld / cruiseRadiusWorld,
                0f,
                1f);
            float maximumSafeSegmentAngle =
                2f * Mathf.Acos(safeRatio) * 0.9f;
            float segmentAngle = Mathf.Min(
                15f * Mathf.Deg2Rad,
                maximumSafeSegmentAngle);
            if (segmentAngle <= 1e-6f)
                return false;

            int arcSegmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    Mathf.Abs(deltaAngle) / segmentAngle));
            if (arcSegmentCount > 720)
                return false;

            float startAxial = Vector3.Dot(startOffset, normal);
            float destinationAxial =
                Vector3.Dot(destinationOffset, normal);
            for (int segment = 0;
                 segment <= arcSegmentCount;
                 segment++)
            {
                float t = segment / (float)arcSegmentCount;
                float angle = deltaAngle * t;
                Vector3 radial =
                    (Mathf.Cos(angle) * basisX +
                     Mathf.Sin(angle) * basisY) *
                    cruiseRadiusWorld;
                float axial =
                    Mathf.Lerp(startAxial, destinationAxial, t);
                waypoints.Add(
                    centerWorld +
                    radial +
                    normal * axial);
            }
            waypoints.Add(destinationWorld);

            minimumArcRadiusWorld = float.PositiveInfinity;
            for (int i = 1; i <= arcSegmentCount; i++)
            {
                float segmentMinimum =
                    MinimumSegmentPlaneRadialDistance(
                        waypoints[i - 1],
                        waypoints[i],
                        centerWorld,
                        normal);
                minimumArcRadiusWorld =
                    Mathf.Min(
                        minimumArcRadiusWorld,
                        segmentMinimum);
            }
            return minimumArcRadiusWorld >=
                outerRadiusWorld - 1e-5f;
        }

        private static float MinimumSegmentPlaneRadialDistance(
            Vector3 start,
            Vector3 end,
            Vector3 center,
            Vector3 planeNormal)
        {
            Vector3 a =
                Vector3.ProjectOnPlane(start - center, planeNormal);
            Vector3 b =
                Vector3.ProjectOnPlane(end - center, planeNormal);
            Vector3 ab = b - a;
            float denominator = ab.sqrMagnitude;
            float t = denominator > 1e-12f
                ? Mathf.Clamp01(-Vector3.Dot(a, ab) / denominator)
                : 0f;
            return (a + ab * t).magnitude;
        }

        private static bool TryBuildTissueSafeCorridor(
            Vector3 start,
            Vector3 destination,
            Vector3 startForward,
            Vector3 destinationForward,
            Vector3 tissueFreeNormal,
            float tissueSurfaceMaxProjection,
            float wholeNeedleClearance,
            List<Vector3> waypoints,
            List<Vector3> forwards,
            out float safeTipProjection,
            out Vector3 approachStandOff)
        {
            safeTipProjection = 0f;
            approachStandOff = destination;
            if (waypoints == null ||
                forwards == null ||
                tissueFreeNormal.sqrMagnitude <= 1e-12f ||
                wholeNeedleClearance <= 1e-6f)
            {
                return false;
            }

            waypoints.Clear();
            forwards.Clear();

            Vector3 freeNormal = tissueFreeNormal.normalized;
            Vector3 startPoseForward =
                NormalizeOrFallback(startForward, destinationForward);
            Vector3 destinationPoseForward =
                NormalizeOrFallback(destinationForward, startPoseForward);
            Vector3 transitForward =
                Vector3.ProjectOnPlane(destinationPoseForward, freeNormal);
            if (transitForward.sqrMagnitude <= 1e-10f)
                transitForward = Vector3.ProjectOnPlane(startPoseForward, freeNormal);
            if (transitForward.sqrMagnitude <= 1e-10f)
            {
                transitForward = Vector3.Cross(freeNormal, Vector3.right);
                if (transitForward.sqrMagnitude <= 1e-10f)
                    transitForward = Vector3.Cross(freeNormal, Vector3.forward);
            }
            transitForward.Normalize();

            safeTipProjection =
                tissueSurfaceMaxProjection +
                Mathf.Max(0f, wholeNeedleClearance);
            approachStandOff =
                destination -
                destinationPoseForward * Mathf.Max(
                    0.001f,
                    wholeNeedleClearance);
            Vector3 liftedStart = LiftPointToProjection(
                start,
                freeNormal,
                safeTipProjection);
            Vector3 liftedApproachStandOff = LiftPointToProjection(
                approachStandOff,
                freeNormal,
                safeTipProjection);

            // Translation away from the background preserves the current pose.
            AddPoseWaypoint(
                waypoints,
                forwards,
                liftedStart,
                startPoseForward);
            // Rotation happens only after the whole needle reaches the safe layer.
            AddPoseWaypoint(
                waypoints,
                forwards,
                liftedStart,
                transitForward);
            AddPoseWaypoint(
                waypoints,
                forwards,
                liftedApproachStandOff,
                transitForward);
            // Align while the complete needle is still on the safe layer.
            AddPoseWaypoint(
                waypoints,
                forwards,
                liftedApproachStandOff,
                destinationPoseForward);
            // Descend to a stand-off point behind the entry axis.  The final
            // approach then advances coaxially instead of sweeping sideways
            // across the vessel wall.
            AddPoseWaypoint(
                waypoints,
                forwards,
                approachStandOff,
                destinationPoseForward);
            AddPoseWaypoint(
                waypoints,
                forwards,
                destination,
                destinationPoseForward);
            return true;
        }

        private static string GetTissueSafeTravelStage(
            int waypointIndex,
            int waypointCount)
        {
            if (waypointCount == 6)
            {
                switch (waypointIndex)
                {
                    case 0: return "ExteriorLiftPreservePose";
                    case 1: return "ExteriorAlignForTransit";
                    case 2: return "ExteriorTraverseToEntryLane";
                    case 3: return "ExteriorAlignWithEntryAxis";
                    case 4: return "ExteriorDescendToEntryStandOff";
                    case 5: return "ExteriorApproachAlongEntryAxis";
                }
            }
            return $"TissueSafeWaypoint{waypointIndex + 1}";
        }

        private static Vector3 LiftPointToProjection(
            Vector3 point,
            Vector3 normalizedFreeNormal,
            float minimumProjection)
        {
            float lift = Mathf.Max(
                0f,
                minimumProjection -
                Vector3.Dot(point, normalizedFreeNormal));
            return point + normalizedFreeNormal * lift;
        }

        private static void AddPoseWaypoint(
            List<Vector3> waypoints,
            List<Vector3> forwards,
            Vector3 point,
            Vector3 forward)
        {
            waypoints.Add(point);
            forwards.Add(NormalizeOrFallback(forward, Vector3.forward));
        }

        private static Vector3 BiasDirectionToTissueFreeSide(
            Vector3 direction,
            Vector3 tissueFreeNormal,
            float minimumFreeNormalDot)
        {
            Vector3 freeNormal =
                NormalizeOrFallback(tissueFreeNormal, Vector3.up);
            Vector3 original =
                NormalizeOrFallback(direction, -freeNormal);
            float minimumDot = Mathf.Clamp(
                minimumFreeNormalDot,
                0.01f,
                0.95f);
            float currentDot = Vector3.Dot(original, freeNormal);
            if (currentDot >= minimumDot)
                return original;

            Vector3 tangent = Vector3.ProjectOnPlane(
                original,
                freeNormal);
            if (tangent.sqrMagnitude <= 1e-10f)
            {
                tangent = Vector3.Cross(freeNormal, Vector3.right);
                if (tangent.sqrMagnitude <= 1e-10f)
                    tangent = Vector3.Cross(freeNormal, Vector3.forward);
            }
            tangent.Normalize();

            float tangentWeight =
                Mathf.Sqrt(Mathf.Max(0f, 1f - minimumDot * minimumDot));
            return NormalizeOrFallback(
                tangent * tangentWeight +
                freeNormal * minimumDot,
                freeNormal);
        }

        private static bool TryBuildExteriorArcLocal(
            Vector3 centerLocal,
            Vector3 startLocal,
            Vector3 destinationLocal,
            float outerRadiusLocal,
            float clearanceLocal,
            List<Vector3> waypoints,
            out float cruiseRadiusLocal,
            out float minimumArcRadiusLocal)
        {
            cruiseRadiusLocal = 0f;
            minimumArcRadiusLocal = 0f;
            if (waypoints == null || outerRadiusLocal <= 1e-6f || clearanceLocal <= 1e-6f)
                return false;

            waypoints.Clear();
            Vector2 startRadial = new Vector2(
                startLocal.x - centerLocal.x,
                startLocal.y - centerLocal.y);
            Vector2 destinationRadial = new Vector2(
                destinationLocal.x - centerLocal.x,
                destinationLocal.y - centerLocal.y);
            if (startRadial.sqrMagnitude <= 1e-10f || destinationRadial.sqrMagnitude <= 1e-10f)
                return false;

            cruiseRadiusLocal = Mathf.Max(
                outerRadiusLocal + clearanceLocal,
                startRadial.magnitude,
                destinationRadial.magnitude);
            float startAngle = Mathf.Atan2(startRadial.y, startRadial.x);
            float destinationAngle = Mathf.Atan2(destinationRadial.y, destinationRadial.x);
            float deltaAngle = Mathf.DeltaAngle(
                startAngle * Mathf.Rad2Deg,
                destinationAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            float safeRatio = Mathf.Clamp(outerRadiusLocal / cruiseRadiusLocal, 0f, 1f);
            float maximumSafeSegmentAngle = 2f * Mathf.Acos(safeRatio) * 0.9f;
            float segmentAngle = Mathf.Min(15f * Mathf.Deg2Rad, maximumSafeSegmentAngle);
            if (segmentAngle <= 1e-6f)
                return false;

            int arcSegmentCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(deltaAngle) / segmentAngle));
            if (arcSegmentCount > 720)
                return false;

            for (int segment = 0; segment <= arcSegmentCount; segment++)
            {
                float t = segment / (float)arcSegmentCount;
                float angle = startAngle + deltaAngle * t;
                waypoints.Add(new Vector3(
                    centerLocal.x + Mathf.Cos(angle) * cruiseRadiusLocal,
                    centerLocal.y + Mathf.Sin(angle) * cruiseRadiusLocal,
                    Mathf.Lerp(startLocal.z, destinationLocal.z, t)));
            }
            waypoints.Add(destinationLocal);

            minimumArcRadiusLocal = float.PositiveInfinity;
            for (int i = 1; i <= arcSegmentCount; i++)
            {
                float segmentMinimum = MinimumSegmentRadialDistanceLocal(
                    waypoints[i - 1],
                    waypoints[i],
                    centerLocal);
                minimumArcRadiusLocal = Mathf.Min(minimumArcRadiusLocal, segmentMinimum);
            }

            return minimumArcRadiusLocal >= outerRadiusLocal - 1e-5f;
        }

        private static float MinimumSegmentRadialDistanceLocal(
            Vector3 start,
            Vector3 end,
            Vector3 center)
        {
            Vector2 a = new Vector2(start.x - center.x, start.y - center.y);
            Vector2 b = new Vector2(end.x - center.x, end.y - center.y);
            Vector2 ab = b - a;
            float denominator = ab.sqrMagnitude;
            float t = denominator > 1e-12f
                ? Mathf.Clamp01(-Vector2.Dot(a, ab) / denominator)
                : 0f;
            return (a + ab * t).magnitude;
        }

        private static float RadialDistanceLocal(Vector3 point, Vector3 center)
        {
            float dx = point.x - center.x;
            float dy = point.y - center.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private bool IsBetweenStitchTravel()
        {
            return _state == AutoSutureState.MoveToNextPass &&
                _currentPassInPair == 0 &&
                _currentPairIndex > 0;
        }

        private bool IsTravelBetweenSutureHoles()
        {
            return _state == AutoSutureState.MoveToNextPass &&
                (_currentPairIndex > 0 || _currentPassInPair > 0);
        }

        private bool IsSameStitchSideTransition()
        {
            return _state == AutoSutureState.MoveToNextPass &&
                _currentPassInPair == 1;
        }

        private Vector3 GetVesselLongitudinalDirection(out string axisSource)
        {
            if (IsCircumferentialSuturePlan() &&
                TryGetCircumferentialOuterFrame(
                    out _,
                    out Vector3 proximalDistalAxis,
                    out _,
                    out _))
            {
                axisSource = "CircumferentialRingCenters";
                return NormalizeOrFallback(
                    proximalDistalAxis,
                    transform.forward);
            }

            axisSource = "OpenGuidePolylines";
            Vector3 axis = Vector3.zero;
            AccumulateAlignedDirection(
                GetPolylineDirection(driver != null ? driver.SutureSideAOuterBoundaryWorld : null),
                ref axis);
            AccumulateAlignedDirection(
                GetPolylineDirection(driver != null ? driver.SutureSideAInnerBoundaryWorld : null),
                ref axis);
            AccumulateAlignedDirection(
                GetPolylineDirection(driver != null ? driver.SutureSideBOuterBoundaryWorld : null),
                ref axis);
            AccumulateAlignedDirection(
                GetPolylineDirection(driver != null ? driver.SutureSideBInnerBoundaryWorld : null),
                ref axis);
            if (axis.sqrMagnitude < 1e-8f)
            {
                AutoSuturePass currentA = GetPass(_currentPairIndex, 0);
                AutoSuturePass nextA = GetPass(_currentPairIndex + 1, 0);
                if (currentA != null && nextA != null)
                {
                    axisSource = "AdjacentPassEntries";
                    axis = nextA.entryPointWorld - currentA.entryPointWorld;
                }
            }
            if (axis.sqrMagnitude < 1e-8f)
                axisSource = "TransformForward";
            return NormalizeOrFallback(axis, transform.forward);
        }

        private static void AccumulateAlignedDirection(Vector3 direction, ref Vector3 sum)
        {
            if (direction.sqrMagnitude < 1e-8f)
                return;
            direction.Normalize();
            if (sum.sqrMagnitude > 1e-8f && Vector3.Dot(sum, direction) < 0f)
                direction = -direction;
            sum += direction;
        }

        private bool IsCircumferentialSuturePlan()
        {
            return driver != null &&
                driver.CurrentSutureSideGuideMode ==
                ClothSimulationDriverDoubleLayer.SutureSideGuideMode.CircumferentialProximalDistal;
        }

        private void EnsureSafeTravelRoute(AutoSuturePass pass, Vector3 destination)
        {
            if (_safeTravelPhase >= 0 &&
                _safeTravelState == _state &&
                Vector3.Distance(_safeTravelDestination, destination) <= 0.0002f)
                return;

            Vector3 currentTip = needleRig != null ? needleRig.GetSemanticTipPoint() : destination;
            Vector3 safeDir = GetSafeTravelAwayDirection(pass);
            float clearance = GetNeedleBodyTravelClearance();
            bool wholeNeedleAlreadyClear =
                _needleBodyDeparturePhase >= 2 &&
                _needleBodyDepartureState == _state &&
                Vector3.Distance(_needleBodyDepartureDestination, destination) <= 0.0002f;
            _safeTravelState = _state;
            _safeTravelDestination = destination;
            _safeTravelLiftTarget = wholeNeedleAlreadyClear
                ? currentTip
                : currentTip + safeDir * clearance;
            _safeTravelCruiseTarget = destination + safeDir * clearance;
            _safeTravelPhase = 0;
            Debug.Log(
                $"[AutoSuture][SafeTravel] pair={_currentPairIndex} pass={_currentPassInPair} state={_state} " +
                $"clearance={clearance:F4} wholeNeedlePreCleared={(wholeNeedleAlreadyClear ? 1 : 0)} " +
                $"current={FormatVec(currentTip)} lift={FormatVec(_safeTravelLiftTarget)} " +
                $"cruise={FormatVec(_safeTravelCruiseTarget)} dest={FormatVec(destination)}");
        }

        private Vector3 GetSafeTravelAwayDirection(AutoSuturePass pass)
        {
            if (pass != null)
                return -GetEntryDirection(pass);
            return Vector3.up;
        }

        private void ResetSafeTravelRoute()
        {
            _lumenLocalAdjustmentActive = false;
            _lumenAlignmentValidated = false;
            _lumenWaypoints.Clear();
            _lumenForwards.Clear();
            _lumenPhases.Clear();
            _lumenWaypointIndex = 0;
            _exteriorAlignmentValidated = false;
            _safeTravelPhase = -1;
            _safeTravelState = AutoSutureState.Idle;
            _exteriorTravelWaypoints.Clear();
            _exteriorTravelForwards.Clear();
            _exteriorTravelWaypointIndex = 0;
            _needleBodyDeparturePhase = -1;
            _needleBodyDepartureState = AutoSutureState.Idle;
            _lumenTravelPhase = -1;
        }

        private void LogPassTimeoutProbe(AutoSuturePass pass)
        {
            int colliderId = needleRig != null ? needleRig.GetTargetColliderEntityId() : 0;
            int passCount = GetNeedlePassCount();
            int recordCount = driver != null && driver.SuturePassSequenceRecords != null
                ? driver.SuturePassSequenceRecords.Count
                : 0;
            Vector3 tip = needleRig != null ? needleRig.GetSemanticTipPoint() : Vector3.zero;
            bool poseLocked = NeedleSceneControlRig.IsAutoSuturePositionPoseLocked(colliderId);
            bool punctureContactAllowed = NeedleSceneControlRig.IsAutoSuturePositionPunctureContactAllowed(colliderId);
            Debug.LogWarning(
                $"[AutoSuture][PassProbe] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"collider={colliderId} passStart={_passStartPassCount} passNow={passCount} records={recordCount} " +
                $"tipEntryDist={Vector3.Distance(tip, pass.entryPointWorld):F5} tipExitDist={Vector3.Distance(tip, pass.exitPointWorld):F5} " +
                $"tip={FormatVec(tip)} entry={FormatVec(pass.entryPointWorld)} exit={FormatVec(pass.exitPointWorld)} " +
                $"poseLocked={(poseLocked ? 1 : 0)} punctureContactAllowed={(punctureContactAllowed ? 1 : 0)} " +
                $"biteOffset={GetEffectiveBiteOffsetFromCut():F5} configuredBiteOffset={biteOffsetFromCut:F5} " +
                $"positionDrive={(positionDrivenPuncture ? 1 : 0)} positionSpeed={GetPositionDrivenPunctureSpeed(pass):F5} " +
                $"configuredSpeedMul={positionDrivenPunctureSpeedMultiplier:F2} " +
                $"effectiveSpeedMul={GetEffectivePositionDrivenPunctureSpeedMultiplier():F2}");
        }

        private void BuildPunctureDistancePath(AutoSuturePass pass)
        {
            int sampleCount = Mathf.Clamp(puncturePathSamples, 8, 96);
            var points = new List<Vector3>(sampleCount + 4);
            var forwards = new List<Vector3>(sampleCount + 4);
            AddPuncturePathSample(points, forwards, _punctureStartTip, GetEntryDirection(pass));
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = i / (float)sampleCount;
                EvaluatePuncturePath(pass, t, out Vector3 point, out Vector3 forward);
                AddPuncturePathSample(points, forwards, point, forward);
            }

            if (positionDrivenPuncture && positionPunctureOverdriveDistance > 1e-6f)
            {
                Vector3 exitDirection = GetExitDirection(pass);
                AddPuncturePathSample(
                    points,
                    forwards,
                    _puncturePostExit + exitDirection * positionPunctureOverdriveDistance,
                    exitDirection);
            }

            if (points.Count < 2)
            {
                Vector3 exitDirection = GetExitDirection(pass);
                AddPuncturePathSample(
                    points,
                    forwards,
                    _punctureStartTip + exitDirection * 1e-5f,
                    exitDirection);
            }

            _puncturePathPoints = points.ToArray();
            _puncturePathForwards = forwards.ToArray();
            _puncturePathCumulative = new float[_puncturePathPoints.Length];
            for (int i = 1; i < _puncturePathPoints.Length; i++)
            {
                _puncturePathCumulative[i] =
                    _puncturePathCumulative[i - 1] +
                    Vector3.Distance(_puncturePathPoints[i - 1], _puncturePathPoints[i]);
            }
            _puncturePathLength = _puncturePathCumulative[_puncturePathCumulative.Length - 1];
            Debug.Log(
                $"[AutoSuture][MotionPath] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"samples={_puncturePathPoints.Length} length={_puncturePathLength:F5} " +
                $"maxStep={GetEffectiveMaxAutoPoseStep():F5}");
            Debug.Log(
                $"[AutoSuture][PuncturePose] pair={_currentPairIndex} pass={_currentPassInPair} " +
                $"source=PlannedPass transportBias=0 " +
                $"entryForward={FormatVec(GetEntryDirection(pass))} exitForward={FormatVec(GetExitDirection(pass))} " +
                $"entryExitAngle={Vector3.Angle(GetEntryDirection(pass), GetExitDirection(pass)):F3} " +
                $"startAngle={Vector3.Angle(needleRig.GetSemanticTipForward(), GetEntryDirection(pass)):F3}");
        }

        private static bool ValidateAutomaticPunctureDirections(
            AutoSuturePass pass, Vector3[] forwards, out string reason)
        {
            reason = string.Empty;
            // Preserve intentionally edited puncture angles. Neither generated
            // pass direction may acquire a bend from the transport posture.
            if (pass.entryPitch != 0f || pass.entryYaw != 0f || pass.exitPitch != 0f || pass.exitYaw != 0f)
                return true;
            Vector3 axis = pass.exitPointWorld - pass.entryPointWorld;
            if (axis.sqrMagnitude <= 1e-10f || forwards == null || forwards.Length < 2)
            {
                reason = "degenerate planned puncture segment";
                return false;
            }
            axis.Normalize();
            float maximumAngle = 0f;
            int mismatches = 0;
            for (int i = 0; i < forwards.Length; i++)
            {
                float angle = forwards[i].sqrMagnitude <= 1e-10f ? 180f : Vector3.Angle(axis, forwards[i]);
                maximumAngle = Mathf.Max(maximumAngle, angle);
                if (angle > 0.1f)
                    mismatches++;
            }
            if (mismatches == 0)
                return true;
            reason = $"automatic wall path changes pose: samples={forwards.Length} mismatches={mismatches} maxAngle={maximumAngle:F3}";
            return false;
        }

        private static void AddPuncturePathSample(
            List<Vector3> points,
            List<Vector3> forwards,
            Vector3 point,
            Vector3 forward)
        {
            Vector3 normalized = forward.sqrMagnitude > 1e-8f
                ? forward.normalized
                : Vector3.forward;
            if (points.Count > 0 && Vector3.SqrMagnitude(points[points.Count - 1] - point) <= 1e-12f)
            {
                forwards[forwards.Count - 1] = normalized;
                return;
            }
            points.Add(point);
            forwards.Add(normalized);
        }

        private void SamplePunctureDistancePath(
            float distance,
            out Vector3 point,
            out Vector3 forward)
        {
            if (_puncturePathPoints == null || _puncturePathPoints.Length < 2 ||
                _puncturePathCumulative == null ||
                _puncturePathCumulative.Length != _puncturePathPoints.Length)
            {
                point = _puncturePostExit;
                AutoSuturePass pass = GetCurrentPass();
                forward = pass != null ? GetExitDirection(pass) : Vector3.forward;
                return;
            }

            float clamped = Mathf.Clamp(distance, 0f, _puncturePathLength);
            int segment = 0;
            while (segment + 1 < _puncturePathCumulative.Length - 1 &&
                   _puncturePathCumulative[segment + 1] < clamped)
            {
                segment++;
            }

            float start = _puncturePathCumulative[segment];
            float end = _puncturePathCumulative[segment + 1];
            float u = end - start > 1e-7f ? Mathf.Clamp01((clamped - start) / (end - start)) : 0f;
            point = Vector3.Lerp(
                _puncturePathPoints[segment],
                _puncturePathPoints[segment + 1],
                u);
            forward = Vector3.Slerp(
                _puncturePathForwards[segment],
                _puncturePathForwards[segment + 1],
                u);
            if (forward.sqrMagnitude < 1e-8f)
                forward = _puncturePathForwards[segment];
            forward.Normalize();
        }

        private float GetEffectiveMaxAutoPoseStep()
        {
            return Mathf.Max(0.0001f, maxAutoPoseStep);
        }

        private Vector3 RotateForwardToward(Vector3 targetForward, float dt)
        {
            Vector3 target = targetForward.sqrMagnitude > 1e-8f
                ? targetForward.normalized
                : Vector3.forward;
            if (needleRig == null)
                return target;

            Vector3 current = needleRig.GetSemanticTipForward();
            if (current.sqrMagnitude < 1e-8f)
                current = target;
            float angleStep = Mathf.Min(
                angularSpeedDeg * Mathf.Max(0f, dt),
                Mathf.Max(0.1f, maxAutoPoseAngleStepDeg));
            return Vector3.RotateTowards(
                current.normalized,
                target,
                Mathf.Deg2Rad * angleStep,
                0f).normalized;
        }

        private Vector3 GetContinuousPoseUp(Vector3 forward)
        {
            _lastMotionUp = AutoSutureLifecyclePolicy.TransportPoseUp(_lastMotionUp, forward);
            return _lastMotionUp;
        }

        private void EvaluatePuncturePath(AutoSuturePass pass, float t, out Vector3 tip, out Vector3 forward)
        {
            float firstPhase;
            float lastPhase;
            ComputePuncturePhaseFractions(pass, out firstPhase, out lastPhase);
            Vector3 entryDir = GetEntryDirection(pass);
            Vector3 exitDir = GetExitDirection(pass);

            if (firstPhase > 1e-5f && t <= firstPhase)
            {
                float u = Mathf.Clamp01(t / firstPhase);
                tip = Vector3.Lerp(_punctureStartTip, pass.entryPointWorld, u);
                forward = entryDir;
                return;
            }

            if (t <= lastPhase)
            {
                float u = Mathf.Clamp01((t - firstPhase) / (lastPhase - firstPhase));
                float distance = Vector3.Distance(pass.entryPointWorld, pass.exitPointWorld);
                Vector3 tangentA = entryDir * Mathf.Max(distance, 0.002f);
                Vector3 tangentB = exitDir * Mathf.Max(distance, 0.002f);
                tip = Hermite(pass.entryPointWorld, pass.exitPointWorld, tangentA, tangentB, u);
                forward = HermiteDerivative(pass.entryPointWorld, pass.exitPointWorld, tangentA, tangentB, u);
                if (forward.sqrMagnitude < 1e-8f)
                    forward = Vector3.Slerp(entryDir, exitDir, u);
                forward.Normalize();
                return;
            }

            float v = Mathf.Clamp01((t - lastPhase) / (1f - lastPhase));
            tip = Vector3.Lerp(pass.exitPointWorld, _puncturePostExit, v);
            forward = exitDir;
        }

        private void ComputePuncturePhaseFractions(AutoSuturePass pass, out float firstPhase, out float lastPhase)
        {
            float preDistance = Vector3.Distance(_punctureStartTip, pass.entryPointWorld);
            float wallDistance = Vector3.Distance(pass.entryPointWorld, pass.exitPointWorld);
            float postDistance = Vector3.Distance(pass.exitPointWorld, _puncturePostExit);
            float total = Mathf.Max(1e-6f, preDistance + wallDistance + postDistance);

            firstPhase = preDistance > 1e-6f ? Mathf.Clamp01(preDistance / total) : 0f;
            float wallPhase = wallDistance > 1e-6f ? Mathf.Clamp01(wallDistance / total) : 0f;
            lastPhase = Mathf.Clamp(firstPhase + wallPhase, firstPhase + 1e-4f, 0.9999f);
        }

        private static Vector3 Hermite(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 +
                   (t3 - 2f * t2 + t) * m0 +
                   (-2f * t3 + 3f * t2) * p1 +
                   (t3 - t2) * m1;
        }

        private static Vector3 HermiteDerivative(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float t)
        {
            float t2 = t * t;
            return (6f * t2 - 6f * t) * p0 +
                   (3f * t2 - 4f * t + 1f) * m0 +
                   (-6f * t2 + 6f * t) * p1 +
                   (3f * t2 - 2f * t) * m1;
        }

        private AutoSutureStitchPair GetCurrentPair()
        {
            if (plan == null || plan.stitchPairs == null ||
                _currentPairIndex < 0 || _currentPairIndex >= plan.stitchPairs.Count)
                return null;
            return plan.stitchPairs[_currentPairIndex];
        }

        private AutoSuturePass GetCurrentPass()
        {
            AutoSutureStitchPair pair = GetCurrentPair();
            if (pair == null)
                return null;
            return _currentPassInPair == 0 ? pair.sideAPass : pair.sideBPass;
        }

        private AutoSuturePass GetPass(int pairIndex, int passIndex)
        {
            if (plan == null || plan.stitchPairs == null ||
                pairIndex < 0 || pairIndex >= plan.stitchPairs.Count)
                return null;

            AutoSutureStitchPair pair = plan.stitchPairs[pairIndex];
            if (pair == null)
                return null;
            return passIndex == 0 ? pair.sideAPass : pair.sideBPass;
        }

        private void SelectPreviewPoint(int step)
        {
            if (plan == null || plan.Count == 0)
            {
                PreviewPlan();
                return;
            }

            int total = plan.Count * 2;
            int flat = GetFlatPassIndex(_currentPairIndex, _currentPassInPair);
            flat = (flat + step) % total;
            if (flat < 0)
                flat += total;
            _currentPairIndex = flat / 2;
            _currentPassInPair = flat % 2;
            if (_state == AutoSutureState.Idle || _state == AutoSutureState.Done)
                SetState(AutoSutureState.PreviewPlan);
        }

        private static int GetFlatPassIndex(int pairIndex, int passIndex)
        {
            return Mathf.Max(0, pairIndex) * 2 + Mathf.Clamp(passIndex, 0, 1);
        }

        private static void RefreshPassDirections(AutoSuturePass pass)
        {
            if (pass == null)
                return;

            Vector3 dir = pass.exitPointWorld - pass.entryPointWorld;
            if (dir.sqrMagnitude < 1e-10f)
                return;
            dir.Normalize();
            pass.entryDirectionWorld = dir;
            pass.exitDirectionWorld = dir;
        }

        private Vector3 GetPreEntry(AutoSuturePass pass)
        {
            Vector3 direction = GetEntryDirection(pass);
            float distance = Mathf.Max(0.001f, pass.approachDistance);
            if (_exteriorFramePass == pass && UsesCurrentExteriorGeometry(pass))
            {
                distance = ResolveExteriorPreEntryDistance(pass.entryPointWorld, -direction,
                    _exteriorCenter, _exteriorAxis, _exteriorRadius, EstimateNeedleWorldRadius(),
                    Mathf.Max(needleBodyTravelMargin, wholeNeedleTravelCollisionMargin + poseArriveDistance * 2f), distance);
            }
            else if (pass.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner &&
                IsCircumferentialSuturePlan() &&
                TryGetCircumferentialOuterFrame(out Vector3 center, out Vector3 axis, out float radius, out _))
            {
                distance = ResolveExteriorPreEntryDistance(pass.entryPointWorld, -direction,
                    center, axis, radius, EstimateNeedleWorldRadius(),
                    Mathf.Max(needleBodyTravelMargin, wholeNeedleTravelCollisionMargin + poseArriveDistance * 2f), distance);
            }
            return pass.entryPointWorld - direction * distance;
        }

        private static float ResolveExteriorPreEntryDistance(
            Vector3 entry, Vector3 outward, Vector3 center, Vector3 axis,
            float outerRadius, float needleRadius, float margin, float configuredDistance)
        {
            // Solve the ray/cylinder intersection for the front capsule sphere,
            // not a fixed tip setback. A shallow lower-side pose needs a longer
            // axial approach to retain the same radial clearance.
            Vector3 radial = Vector3.ProjectOnPlane(entry - center, axis);
            Vector3 velocity = Vector3.ProjectOnPlane(outward, axis);
            float a = velocity.sqrMagnitude;
            float b = Vector3.Dot(radial, velocity);
            if (a <= 1e-10f || b <= 0f)
                return configuredDistance; // The planner will reject a non-outward endpoint.
            float targetRadius = outerRadius + needleRadius + margin;
            float discriminant = b * b + a * (targetRadius * targetRadius - radial.sqrMagnitude);
            float sphereDistance = (-b + Mathf.Sqrt(Mathf.Max(0f, discriminant))) / a;
            return Mathf.Max(configuredDistance, sphereDistance - needleRadius);
        }

        private Vector3 GetPostExit(AutoSuturePass pass)
        {
            return pass.exitPointWorld + GetExitDirection(pass) * Mathf.Max(0.001f, pass.approachDistance);
        }

        private Vector3 GetRetractTarget(AutoSuturePass pass)
        {
            bool shouldClearTail = clearNeedleTailAfterEachPass ||
                (clearNeedleTailBeforePullLine && _currentPassInPair != 0);
            if (!shouldClearTail)
                return GetPostExit(pass);

            return pass.exitPointWorld + GetExitDirection(pass) * GetPullLineNeedleClearDistance();
        }

        private float GetPullLineNeedleClearDistance()
        {
            float effectiveClearance = Mathf.Max(pullLineNeedleTailClearance, AutoPullLineNeedleTailClearanceFloor);
            float effectiveMaxDistance = Mathf.Max(maxPullLineNeedleClearDistance, AutoPullLineNeedleClearDistanceCapFloor);
            float minimum = Mathf.Max(approachDistance, effectiveClearance);
            float estimatedNeedleLength = EstimateNeedleWorldLength();
            if (estimatedNeedleLength > 1e-5f)
                minimum = Mathf.Max(minimum, estimatedNeedleLength + effectiveClearance);

            return Mathf.Clamp(minimum, Mathf.Max(0.001f, approachDistance), Mathf.Max(0.001f, effectiveMaxDistance));
        }

        private float EstimateNeedleWorldLength()
        {
            Transform needleTransform = targetNeedle;
            if (needleTransform == null && needleRig != null)
                needleTransform = needleRig.targetNeedle;
            if (needleTransform == null)
                return 0f;

            CapsuleCollider capsule = needleTransform.GetComponent<CapsuleCollider>();
            if (capsule == null)
                return 0f;

            NeedleCapsuleGeometry.GetDimensions(capsule, out float length, out _);
            return length;
        }

        private float EstimateNeedleWorldRadius()
        {
            Transform needleTransform = targetNeedle;
            if (needleTransform == null && needleRig != null)
                needleTransform = needleRig.targetNeedle;
            if (needleTransform == null)
                return 0f;

            CapsuleCollider capsule = needleTransform.GetComponent<CapsuleCollider>();
            if (capsule == null)
                return 0f;

            NeedleCapsuleGeometry.GetDimensions(capsule, out _, out float radius);
            return radius;
        }

        private float GetNeedleBodyTravelClearance()
        {
            return ResolveNeedleBodyTravelClearance(
                safeTravelClearance,
                EstimateNeedleWorldLength(),
                EstimateNeedleWorldRadius(),
                needleBodyTravelMargin);
        }

        private bool TryValidateWholeNeedleTravel(
            Vector3 startTip,
            Vector3 startForward,
            Vector3 endTip,
            Vector3 endForward,
            bool checkTissueBackground,
            out string reason)
        {
            reason = string.Empty;
            if (!enforceWholeNeedleClearanceDuringTravel)
                return true;

            float length = EstimateNeedleWorldLength();
            float radius = EstimateNeedleWorldRadius();
            if (length <= 1e-6f || radius <= 1e-6f)
            {
                reason = "needle CapsuleCollider dimensions are unavailable";
                return false;
            }

            Vector3 fromForward = NormalizeOrFallback(startForward, endForward);
            Vector3 toForward = NormalizeOrFallback(endForward, fromForward);
            float linearTravel = Vector3.Distance(startTip, endTip);
            float angularTravel = Mathf.Deg2Rad *
                                  Vector3.Angle(fromForward, toForward) *
                                  length;
            float sampleSpacing = Mathf.Max(0.00035f, radius * 0.45f);
            int samples = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(linearTravel, angularTravel) / sampleSpacing),
                1,
                48);
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 tip = Vector3.Lerp(startTip, endTip, t);
                Vector3 forward = Vector3.Slerp(fromForward, toForward, t);
                if (!TryValidateWholeNeedlePose(
                        tip,
                        forward,
                        length,
                        radius,
                        checkTissueBackground,
                        out reason))
                {
                    reason = $"sample={i}/{samples} {reason}";
                    return false;
                }
            }
            return true;
        }

        private bool TryValidateWholeNeedlePose(
            Vector3 tip,
            Vector3 forward,
            float length,
            float radius,
            bool checkTissueBackground,
            out string reason)
        {
            reason = string.Empty;
            forward = NormalizeOrFallback(forward, Vector3.forward);
            float effectiveLength = Mathf.Max(length, radius * 2f);
            Vector3 capsuleTipCenter = tip - forward * radius;
            Vector3 capsuleTailCenter =
                tip - forward * (effectiveLength - radius);
            float clearance = radius +
                              Mathf.Max(0f, wholeNeedleTravelCollisionMargin);

            if (checkTissueBackground && avoidTissueBackground)
            {
                if (!TryRefreshLocalTissueSurface(false, out reason))
                    return false;
                float gap = _localTissueSurface.CapsuleGap(capsuleTipCenter, capsuleTailCenter,
                    radius, Mathf.Max(0f, tissueTravelMargin), Mathf.Max(0f, wholeNeedleTravelCollisionMargin));
                if (gap < Mathf.Max(0f, wholeNeedleTravelCollisionMargin))
                {
                    reason = $"background mode=LocalMesh gapAfterTissueMargin={gap:F5} " +
                        $"required={Mathf.Max(0f, wholeNeedleTravelCollisionMargin):F5}";
                    return false;
                }
            }

            ClothSimulator sim = driver != null ? driver.Simulator : null;
            if (sim == null)
                return true;

            var positions = sim.predictPositions;
            var indices = sim.indices;
            if (!positions.IsCreated || !indices.IsCreated || indices.Length < 3)
                return true;

            Vector3 expand = Vector3.one * (clearance + 0.00005f);
            Vector3 min = Vector3.Min(capsuleTipCenter, capsuleTailCenter) - expand;
            Vector3 max = Vector3.Max(capsuleTipCenter, capsuleTailCenter) + expand;
            var query = new AABB(min, max);
            _wholeNeedleCollisionCandidates.Clear();
            List<int> candidates = sim.TriangleBVH.GetAllObjects(query);
            for (int i = 0; i < candidates.Count; i++)
                _wholeNeedleCollisionCandidates.Add(candidates[i]);

            float clearanceSq = clearance * clearance;
            foreach (int triangleIndex in _wholeNeedleCollisionCandidates)
            {
                int offset = triangleIndex * 3;
                if (offset < 0 || offset + 2 >= indices.Length)
                    continue;
                int ia = indices[offset];
                int ib = indices[offset + 1];
                int ic = indices[offset + 2];
                if ((uint)ia >= (uint)positions.Length ||
                    (uint)ib >= (uint)positions.Length ||
                    (uint)ic >= (uint)positions.Length)
                {
                    continue;
                }

                Vector3 a = new Vector3(
                    positions[ia].x,
                    positions[ia].y,
                    positions[ia].z);
                Vector3 b = new Vector3(
                    positions[ib].x,
                    positions[ib].y,
                    positions[ib].z);
                Vector3 c = new Vector3(
                    positions[ic].x,
                    positions[ic].y,
                    positions[ic].z);
                float distanceSq = SqrDistanceSegmentTriangle(
                    capsuleTipCenter,
                    capsuleTailCenter,
                    a,
                    b,
                    c);
                if (distanceSq > clearanceSq)
                    continue;

                reason =
                    $"vessel tri={triangleIndex} " +
                    $"distance={Mathf.Sqrt(Mathf.Max(0f, distanceSq)):F5} " +
                    $"required={clearance:F5}";
                return false;
            }
            return true;
        }

        private static float SqrDistanceSegmentTriangle(
            Vector3 p0,
            Vector3 p1,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            if (SegmentIntersectsTriangle(p0, p1, a, b, c))
                return 0f;

            float best = Mathf.Min(
                SqrDistancePointTriangle(p0, a, b, c),
                SqrDistancePointTriangle(p1, a, b, c));
            best = Mathf.Min(best, SqrDistancePointSegment(a, p0, p1));
            best = Mathf.Min(best, SqrDistancePointSegment(b, p0, p1));
            best = Mathf.Min(best, SqrDistancePointSegment(c, p0, p1));
            best = Mathf.Min(best, SqrDistanceSegmentSegment(p0, p1, a, b));
            best = Mathf.Min(best, SqrDistanceSegmentSegment(p0, p1, b, c));
            best = Mathf.Min(best, SqrDistanceSegmentSegment(p0, p1, c, a));
            return best;
        }

        private static bool SegmentIntersectsTriangle(
            Vector3 p0,
            Vector3 p1,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            Vector3 direction = p1 - p0;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 h = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, h);
            if (Mathf.Abs(determinant) < 1e-10f)
                return false;
            float inverse = 1f / determinant;
            Vector3 s = p0 - a;
            float u = inverse * Vector3.Dot(s, h);
            if (u < 0f || u > 1f)
                return false;
            Vector3 q = Vector3.Cross(s, edge1);
            float v = inverse * Vector3.Dot(direction, q);
            if (v < 0f || u + v > 1f)
                return false;
            float t = inverse * Vector3.Dot(edge2, q);
            return t >= 0f && t <= 1f;
        }

        private static float SqrDistancePointTriangle(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
                return ap.sqrMagnitude;

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
                return bp.sqrMagnitude;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / Mathf.Max(1e-12f, d1 - d3);
                return (point - (a + ab * v)).sqrMagnitude;
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
                return cp.sqrMagnitude;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / Mathf.Max(1e-12f, d2 - d6);
                return (point - (a + ac * w)).sqrMagnitude;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) /
                          Mathf.Max(1e-12f, (d4 - d3) + (d5 - d6));
                return (point - (b + (c - b) * w)).sqrMagnitude;
            }

            float denominator = Mathf.Max(1e-12f, va + vb + vc);
            float baryV = vb / denominator;
            float baryW = vc / denominator;
            Vector3 closest = a + ab * baryV + ac * baryW;
            return (point - closest).sqrMagnitude;
        }

        private static float SqrDistancePointSegment(
            Vector3 point,
            Vector3 a,
            Vector3 b)
        {
            Vector3 ab = b - a;
            float denominator = ab.sqrMagnitude;
            if (denominator <= 1e-12f)
                return (point - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denominator);
            return (point - (a + ab * t)).sqrMagnitude;
        }

        private static float SqrDistanceSegmentSegment(
            Vector3 p1,
            Vector3 q1,
            Vector3 p2,
            Vector3 q2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float s;
            float t;
            if (a <= 1e-12f && e <= 1e-12f)
                return (p1 - p2).sqrMagnitude;
            if (a <= 1e-12f)
            {
                s = 0f;
                t = Mathf.Clamp01(f / Mathf.Max(e, 1e-12f));
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-12f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denominator = a * e - b * b;
                    s = denominator > 1e-12f
                        ? Mathf.Clamp01((b * f - c * e) / denominator)
                        : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }
            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            return (c1 - c2).sqrMagnitude;
        }

        internal static float ResolveNeedleBodyTravelClearance(
            float configuredClearance,
            float needleLength,
            float needleRadius,
            float bodyMargin)
        {
            float configured = Mathf.Max(0f, configuredClearance);
            float wholeNeedle = Mathf.Max(0f, needleLength) +
                Mathf.Max(0f, needleRadius) +
                Mathf.Max(0f, bodyMargin);
            return Mathf.Max(configured, wholeNeedle);
        }

        private Vector3 GetEntryDirection(AutoSuturePass pass)
        {
            // A transit clearance posture is not a puncture tangent. Changing
            // only the Outer tangent made Hermite rotate the needle in the wall.
            return GetPlannedPunctureDirection(pass.entryDirectionWorld, pass.entryPitch, pass.entryYaw);
        }

        private Vector3 GetExitDirection(AutoSuturePass pass)
        {
            return GetPlannedPunctureDirection(pass.exitDirectionWorld, pass.exitPitch, pass.exitYaw);
        }

        private static Vector3 GetPlannedPunctureDirection(Vector3 direction, float pitch, float yaw)
        {
            return pitch == 0f && yaw == 0f
                ? NormalizeOrFallback(direction, Vector3.forward)
                : NormalizeOrFallback(ApplyPitchYaw(direction, pitch, yaw), Vector3.forward);
        }

        private Vector3 GetTissueTransitForward(
            Vector3 desiredForward,
            Vector3 tissueFreeNormal)
        {
            Vector3 transit = Vector3.ProjectOnPlane(
                desiredForward,
                tissueFreeNormal);
            if (transit.sqrMagnitude <= 1e-10f && needleRig != null)
            {
                transit = Vector3.ProjectOnPlane(
                    needleRig.GetSemanticTipForward(),
                    tissueFreeNormal);
            }
            if (transit.sqrMagnitude <= 1e-10f)
            {
                transit = Vector3.Cross(
                    tissueFreeNormal,
                    Vector3.right);
                if (transit.sqrMagnitude <= 1e-10f)
                {
                    transit = Vector3.Cross(
                        tissueFreeNormal,
                        Vector3.forward);
                }
            }
            return NormalizeOrFallback(transit, Vector3.forward);
        }

        private bool TryCaptureExteriorVesselSurface(out VesselSurfaceSnapshot surface, out string reason)
        {
            surface = null;
            reason = string.Empty;
            ClothSimulator simulator = driver != null ? driver.Simulator : null;
            if (simulator == null)
            {
                reason = "ExteriorVesselGeometry: missing simulator";
                return false;
            }
            var positions = simulator.predictPositions;
            var indices = simulator.indices;
            if (!positions.IsCreated || !indices.IsCreated)
            {
                reason = "ExteriorVesselGeometry: current mesh is unavailable";
                return false;
            }
            Vector3[] points = new Vector3[positions.Length];
            int[] triangles = new int[indices.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector3(positions[i].x, positions[i].y, positions[i].z);
            for (int i = 0; i < triangles.Length; i++) triangles[i] = indices[i];
            try
            {
                surface = new VesselSurfaceSnapshot(points, triangles);
                return true;
            }
            catch (ArgumentException exception)
            {
                reason = "ExteriorVesselGeometry: " + exception.Message;
                return false;
            }
        }

        // Read-only current geometry for ONE next-pass route. Triangle AABBs
        // are broad-phase only; segment/triangle distance is the verdict, using
        // the same capsule definition as the live whole-needle travel guard.
        private sealed class ExitChannelContactSet
        {
            private readonly int[] _sourceIndices;
            public readonly HashSet<int> triangles;
            public ExitChannelContactSet(int[] sourceIndices, HashSet<int> contacts)
            {
                _sourceIndices = sourceIndices; triangles = contacts;
            }
            public bool Allows(int triangle, int[] currentIndices)
            {
                int i = triangle * 3;
                return triangles.Contains(triangle) && _sourceIndices != null && i >= 0 &&
                    i + 2 < _sourceIndices.Length && i + 2 < currentIndices.Length &&
                    _sourceIndices[i] == currentIndices[i] && _sourceIndices[i + 1] == currentIndices[i + 1] &&
                    _sourceIndices[i + 2] == currentIndices[i + 2];
            }
        }

        private sealed class VesselSurfaceSnapshot
        {
            private readonly Vector3[] _points, _min, _max;
            private readonly int[] _indices;
            public int TriangleCount => _indices.Length / 3;

            public VesselSurfaceSnapshot(Vector3[] points, int[] indices)
            {
                if (points == null || points.Length < 3 || indices == null || indices.Length < 3 || indices.Length % 3 != 0)
                    throw new ArgumentException("invalid current vessel mesh dimensions");
                _points = points;
                _indices = indices;
                _min = new Vector3[TriangleCount];
                _max = new Vector3[TriangleCount];
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 p = points[i];
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
                        float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
                        throw new ArgumentException("non-finite current vessel vertex");
                }
                for (int t = 0; t < TriangleCount; t++)
                {
                    int a = indices[t * 3], b = indices[t * 3 + 1], c = indices[t * 3 + 2];
                    if ((uint)a >= (uint)points.Length || (uint)b >= (uint)points.Length || (uint)c >= (uint)points.Length)
                        throw new ArgumentException("invalid current vessel triangle index");
                    _min[t] = Vector3.Min(points[a], Vector3.Min(points[b], points[c]));
                    _max[t] = Vector3.Max(points[a], Vector3.Max(points[b], points[c]));
                }
            }

            public float CapsuleGap(Vector3 a, Vector3 b, float radius, float margin, bool scanAll,
                out int nearestTriangle, out int nearTriangleCount)
            {
                float clearance = radius + Mathf.Max(0f, margin);
                Vector3 expand = Vector3.one * (clearance + 0.00005f);
                Vector3 min = Vector3.Min(a, b) - expand, max = Vector3.Max(a, b) + expand;
                float minimumSquared = float.PositiveInfinity;
                nearestTriangle = -1;
                nearTriangleCount = 0;
                for (int t = 0; t < TriangleCount; t++)
                {
                    if (!scanAll && (_max[t].x < min.x || _min[t].x > max.x ||
                        _max[t].y < min.y || _min[t].y > max.y || _max[t].z < min.z || _min[t].z > max.z))
                        continue;
                    float distanceSquared = SqrDistanceSegmentTriangle(a, b,
                        _points[_indices[t * 3]], _points[_indices[t * 3 + 1]], _points[_indices[t * 3 + 2]]);
                    if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared))
                    {
                        nearestTriangle = t;
                        return float.NegativeInfinity; // Invalid distance is never a clear route.
                    }
                    if (distanceSquared <= clearance * clearance) nearTriangleCount++;
                    if (distanceSquared < minimumSquared)
                    {
                        minimumSquared = distanceSquared;
                        nearestTriangle = t;
                    }
                }
                return Mathf.Sqrt(Mathf.Max(0f, minimumSquared)) - radius;
            }

            public ExitChannelContactSet CaptureExitChannelContacts(Vector3 tip, Vector3 forward, float length, float radius,
                float margin, Vector3 exitPoint, int exitTriangle, int outerTriangleCount, float portalRadius)
            {
                var contacts = new HashSet<int>();
                if (exitTriangle < 0 || exitTriangle >= TriangleCount) return new ExitChannelContactSet(_indices, contacts);
                forward.Normalize();
                Vector3 a = tip - forward * radius, b = tip - forward * (length - radius);
                float requiredSquared = (radius + margin) * (radius + margin);
                var candidates = new HashSet<int>();
                for (int t = 0; t < TriangleCount; t++)
                {
                    if ((t < outerTriangleCount) != (exitTriangle < outerTriangleCount)) continue;
                    Vector3 p = _points[_indices[t * 3]], q = _points[_indices[t * 3 + 1]], r = _points[_indices[t * 3 + 2]];
                    // Inherit only occupied material in the existing portal's
                    // physical support, never an arbitrary nearby wall.
                    if (SqrDistanceSegmentTriangle(a, b, p, q, r) < requiredSquared &&
                        (t == exitTriangle || (portalRadius > 0f && SqrDistancePointTriangle(exitPoint, p, q, r) <= portalRadius * portalRadius)))
                        candidates.Add(t);
                }
                var connected = new HashSet<int> { exitTriangle };
                var queue = new Queue<int>(); queue.Enqueue(exitTriangle);
                while (queue.Count > 0)
                {
                    int seed = queue.Dequeue();
                    foreach (int t in candidates)
                    {
                        if (connected.Contains(t)) continue;
                        int shared = 0;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int v = _indices[t * 3 + corner];
                            if (v == _indices[seed * 3] || v == _indices[seed * 3 + 1] || v == _indices[seed * 3 + 2]) shared++;
                        }
                        if (shared < 2) continue;
                        connected.Add(t); queue.Enqueue(t);
                    }
                }
                foreach (int t in candidates) if (connected.Contains(t)) contacts.Add(t);
                return new ExitChannelContactSet(_indices, contacts);
            }

            public void RetireClearedExitContacts(ExitChannelContactSet channel, Vector3 tip, Vector3 forward,
                float length, float radius, float margin)
            {
                if (channel == null) return;
                Vector3 a = tip - forward * radius, b = tip - forward * (length - radius);
                float threshold = (radius + margin) * (radius + margin);
                channel.triangles.RemoveWhere(t => !channel.Allows(t, _indices) ||
                    SqrDistanceSegmentTriangle(a, b, _points[_indices[t * 3]], _points[_indices[t * 3 + 1]], _points[_indices[t * 3 + 2]]) >= threshold);
            }

            public string DescribeExitContactHandoff(Vector3 tip, Vector3 forward, float length, float radius, float margin,
                Vector3 exitPoint, int exitTriangle, int outerTriangleCount, float probeTravel,
                VesselSurfaceSnapshot previous, Vector3 previousTip, Vector3 previousForward)
            {
                // Audit every contact, not only the first triangle rejected by
                // the motion gate. None of these categories authorizes motion.
                if (exitTriangle < 0 || exitTriangle >= TriangleCount) return "auditUnavailable=exitTriangle";
                forward.Normalize();
                Vector3 a = tip - forward * radius, b = tip - forward * (length - radius);
                Vector3 oldA = previousTip - previousForward * radius, oldB = previousTip - previousForward * (length - radius);
                Vector3 delta = forward * probeTravel;
                int e0 = _indices[exitTriangle * 3], e1 = _indices[exitTriangle * 3 + 1], e2 = _indices[exitTriangle * 3 + 2];
                float required = radius + margin;
                int count = 0, sameTriangle = 0, sharedEdge = 0, sharedVertex = 0, disconnected = 0;
                int oldContacts = 0, newContacts = 0, unknownPrevious = 0, otherLayer = 0, pointGateRejected = 0;
                int improving = 0, deepening = 0, unchanged = 0;
                var details = new List<string>();
                for (int t = 0; t < TriangleCount; t++)
                {
                    int i0 = _indices[t * 3], i1 = _indices[t * 3 + 1], i2 = _indices[t * 3 + 2];
                    Vector3 p = _points[i0], q = _points[i1], r = _points[i2];
                    float distance = Mathf.Sqrt(SqrDistanceSegmentTriangle(a, b, p, q, r));
                    if (distance >= required) continue;
                    count++;
                    int shared = (i0 == e0 || i0 == e1 || i0 == e2 ? 1 : 0) +
                        (i1 == e0 || i1 == e1 || i1 == e2 ? 1 : 0) + (i2 == e0 || i2 == e1 || i2 == e2 ? 1 : 0);
                    string relation;
                    if (t == exitTriangle) { sameTriangle++; relation = "ExitTriangle"; }
                    else if (shared >= 2) { sharedEdge++; relation = "SharedEdge"; }
                    else if (shared == 1) { sharedVertex++; relation = "SharedVertex"; }
                    else { disconnected++; relation = "NoSharedVertex"; }
                    if ((t < outerTriangleCount) != (exitTriangle < outerTriangleCount)) otherLayer++;
                    float exitDistance = Mathf.Sqrt(SqrDistancePointTriangle(exitPoint, p, q, r));
                    bool rejected = exitDistance > required;
                    if (rejected) pointGateRejected++;
                    float nextDistance = Mathf.Sqrt(SqrDistanceSegmentTriangle(a + delta, b + delta, p, q, r));
                    float forwardDelta = nextDistance - distance;
                    if (forwardDelta > 1e-6f) improving++;
                    else if (forwardDelta < -1e-6f) deepening++;
                    else unchanged++;
                    float oldDistance = float.NaN, meshOnlyDelta = float.NaN;
                    string history = "Unknown";
                    // Compare only identical material triangles across frames.
                    bool sameIdentity = previous != null && t < previous.TriangleCount &&
                        previous._indices[t * 3] == i0 && previous._indices[t * 3 + 1] == i1 && previous._indices[t * 3 + 2] == i2;
                    if (sameIdentity)
                    {
                        oldDistance = Mathf.Sqrt(SqrDistanceSegmentTriangle(oldA, oldB,
                            previous._points[i0], previous._points[i1], previous._points[i2]));
                        meshOnlyDelta = Mathf.Sqrt(SqrDistanceSegmentTriangle(oldA, oldB, p, q, r)) - oldDistance;
                        if (oldDistance < required) { oldContacts++; history = "AlreadyCurrent"; }
                        else { newContacts++; history = "NewSinceCurrent"; }
                    }
                    else unknownPrevious++;
                    if (details.Count < 16)
                        details.Add($"tri={t} vertices=[{i0},{i1},{i2}] layer={(t < outerTriangleCount ? "Outer" : "Inner")} " +
                            $"relation={relation} distance={distance:F6} bodyGap={distance - radius:F6} exitPointDistance={exitDistance:F6} " +
                            $"pointGateRejected={(rejected ? 1 : 0)} history={history} previousDistance={oldDistance:F6} " +
                            $"meshOnlyDelta={meshOnlyDelta:F6} forwardDelta={forwardDelta:F6} " +
                            $"points=[{FormatVec(p)};{FormatVec(q)};{FormatVec(r)}]");
                }
                return $"tip={FormatVec(tip)} forward={FormatVec(forward)} length={length:F6} radius={radius:F6} required={required:F6} " +
                    $"meshExitVertices=[{e0},{e1},{e2}] count={count} byRelation=[exit:{sameTriangle},edge:{sharedEdge},vertex:{sharedVertex},none:{disconnected}] " +
                    $"otherLayer={otherLayer} pointGateRejected={pointGateRejected} alreadyCurrent={oldContacts} newSinceCurrent={newContacts} " +
                    $"unknownPrevious={unknownPrevious} trend=[improving:{improving},deepening:{deepening},unchanged:{unchanged}] " +
                    $"previewCount={details.Count} preview=[{string.Join(" | ", details)}]";
            }

            public bool TryPlanAxialExit(Vector3 tip, Vector3 forward, Vector3 exitPoint, Vector3 exitDirection,
                float length, float radius, float margin, float minimumTravel, float maximumTravel,
                TissueSurfaceSnapshot tissue, float tissueMargin, out float distance, out string reason,
                float releaseMargin = 0f, ExitChannelContactSet originalChannel = null)
            {
                distance = 0f;
                reason = string.Empty;
                if (length < 2f * radius || radius <= 0f || minimumTravel > maximumTravel + 1e-6f ||
                    forward.sqrMagnitude < 0.5f || exitDirection.sqrMagnitude < 0.5f || Vector3.Angle(forward, exitDirection) > 0.1f)
                {
                    reason = "invalid axial exit geometry/budget";
                    return false;
                }
                forward.Normalize(); exitDirection.Normalize();
                float clearance = radius + margin;
                float threshold = clearance * clearance;
                releaseMargin = Mathf.Max(margin, releaseMargin);
                float releaseThreshold = (radius + releaseMargin) * (radius + releaseMargin);
                Vector3 a = tip - forward * radius, b = tip - forward * (length - radius);
                var previousDistances = new float[TriangleCount];
                var channelContact = new bool[TriangleCount];
                Vector3 endA = a + forward * maximumTravel, endB = b + forward * maximumTravel;
                Vector3 expand = Vector3.one * (radius + releaseMargin + 0.00005f);
                Vector3 sweepMin = Vector3.Min(Vector3.Min(a, b), Vector3.Min(endA, endB)) - expand;
                Vector3 sweepMax = Vector3.Max(Vector3.Max(a, b), Vector3.Max(endA, endB)) + expand;
                var candidates = new List<int>();
                for (int t = 0; t < TriangleCount; t++)
                {
                    if (_max[t].x < sweepMin.x || _min[t].x > sweepMax.x || _max[t].y < sweepMin.y ||
                        _min[t].y > sweepMax.y || _max[t].z < sweepMin.z || _min[t].z > sweepMax.z) continue;
                    candidates.Add(t);
                    Vector3 p = _points[_indices[t * 3]], q = _points[_indices[t * 3 + 1]], r = _points[_indices[t * 3 + 2]];
                    float d = SqrDistanceSegmentTriangle(a, b, p, q, r);
                    previousDistances[t] = d;
                    // Only an already occupied exit-channel triangle may remain
                    // in contact during coaxial departure. No new wall contact.
                    channelContact[t] = d < threshold && originalChannel != null && originalChannel.Allows(t, _indices);
                    if (float.IsNaN(d) || (d < threshold && !channelContact[t]))
                    {
                        reason = $"start contact outside exit channel tri={t} distance={Mathf.Sqrt(d):F5}";
                        return false;
                    }
                }
                float spacing = Mathf.Max(0.00005f, Mathf.Min(radius * 0.25f, Mathf.Max(margin, 0.0001f) * 0.5f));
                int samples = Mathf.Max(1, Mathf.CeilToInt(maximumTravel / spacing));
                if (samples > 12000) { reason = "axial exit extent excessive"; return false; }
                for (int sample = 0; sample <= samples; sample++)
                {
                    float travel = maximumTravel * sample / samples;
                    Vector3 pa = a + forward * travel, pb = b + forward * travel;
                    bool clear = true;
                    foreach (int t in candidates)
                    {
                        float d = SqrDistanceSegmentTriangle(pa, pb, _points[_indices[t * 3]], _points[_indices[t * 3 + 1]], _points[_indices[t * 3 + 2]]);
                        if (float.IsNaN(d) || (d < threshold && (!channelContact[t] || d + 1e-10f < previousDistances[t])))
                        {
                            reason = $"axial sweep blocked tri={t} sample={sample} newOrDeepeningContact=1";
                            return false;
                        }
                        if (d < releaseThreshold) clear = false;
                        if (d >= threshold) channelContact[t] = false;
                        previousDistances[t] = d;
                    }
                    if (tissue != null)
                    {
                        float tissueGap = tissue.CapsuleGap(pa, pb, radius, tissueMargin, releaseMargin);
                        if (tissueGap < margin)
                        {
                            reason = $"axial sweep blocked by background sample={sample}";
                            return false;
                        }
                        if (tissueGap < releaseMargin) clear = false;
                    }
                    float tailBeyondExit = Mathf.Min(Vector3.Dot(pa - exitPoint, exitDirection), Vector3.Dot(pb - exitPoint, exitDirection)) - radius;
                    if (clear && travel + 1e-7f >= minimumTravel && tailBeyondExit >= releaseMargin)
                    {
                        distance = travel;
                        return true;
                    }
                }
                reason = "axial exit budget exhausted before whole-body clearance";
                return false;
            }
        }

        private bool TryRefreshLocalTissueSurface(bool refreshVertices, out string reason)
        {
            reason = string.Empty;
            if (!avoidTissueBackground)
            {
                if (_localTissueSurface != null) _localTissueRevision++;
                _localTissueSurface = null;
                return true;
            }
            if (tissueAvoidanceSurface != null &&
                tissueAvoidanceSurface.transform.localToWorldMatrix != _localTissueTransform)
                _tissueSafetyFrameCached = false;
            if (!TryGetTissueSafetyFrame(out TissueSafetyFrame frame))
            {
                reason = "LocalTissueGeometry: TissueSurface mesh is unavailable";
                return false;
            }
            Mesh mesh = tissueAvoidanceSurface.sharedMesh;
            Matrix4x4 matrix = tissueAvoidanceSurface.transform.localToWorldMatrix;
            if (!refreshVertices && _localTissueSurface != null && mesh == _localTissueMesh && matrix == _localTissueTransform)
                return true;
            if (!mesh.isReadable)
            {
                reason = "LocalTissueGeometry: TissueSurface mesh must be readable";
                return false;
            }
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            try
            {
                _localTissueSurface = new TissueSurfaceSnapshot(vertices, mesh.triangles, frame.freeNormal);
            }
            catch (ArgumentException exception)
            {
                reason = "LocalTissueGeometry: " + exception.Message;
                return false;
            }
            _localTissueMesh = mesh;
            _localTissueTransform = matrix;
            _localTissueRevision++;
            return true;
        }

        private static bool TryResolveExteriorTissueRadius(Vector3 axisPoint, Vector3 radial,
            Vector3 forward, float halfSegment, float needleRadius, float margin,
            float minimumRadius, ref float poseRadius, Vector3 normal, float projection,
            TissueSurfaceSnapshot surface, float tissueMargin, out string reason)
        {
            reason = string.Empty;
            if (normal.sqrMagnitude < 0.5f)
                return true;
            float lower = minimumRadius, upper = poseRadius;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                float localProjection = surface == null ? projection :
                    surface.ProjectionForRadiusInterval(axisPoint, radial, forward, halfSegment,
                        lower, upper, needleRadius + margin + tissueMargin) + tissueMargin;
                if (float.IsNegativeInfinity(localProjection))
                    return true; // No mesh projects into this complete capsule footprint.
                float requiredHeight = localProjection + needleRadius + margin +
                    halfSegment * Mathf.Abs(Vector3.Dot(forward, normal));
                float radialDot = Vector3.Dot(radial, normal);
                float axisHeight = Vector3.Dot(axisPoint, normal);
                if (radialDot < -1e-6f)
                    poseRadius = Mathf.Min(poseRadius, (axisHeight - requiredHeight) / -radialDot);
                else if (radialDot > 1e-6f)
                    poseRadius = Mathf.Max(poseRadius, (requiredHeight - axisHeight) / radialDot);
                else if (axisHeight < requiredHeight)
                {
                    reason = $"LocalTissueClearance: no clearance parallel to surface localProjection={localProjection:F5}";
                    return false;
                }
                if (poseRadius < minimumRadius)
                {
                    reason = $"LocalTissueClearance: vessel/background gap too narrow " +
                        $"localProjection={localProjection:F5} minRadius={minimumRadius:F5} availableRadius={poseRadius:F5}";
                    return false;
                }
                // The height bound is valid over the entire queried radius
                // interval, including the capsule body and its safety margins.
                if (poseRadius <= upper)
                    return true;
                upper = poseRadius;
            }
            reason = "LocalTissueClearance: radial envelope did not converge";
            return false;
        }

        // Background is a triangle surface, not a plane at the highest vertex
        // anywhere in the scene. Query a conservative height envelope only over
        // the current capsule/turn footprint; clip crossing triangles as well as
        // checking vertices so a local ridge cannot be skipped.
        private sealed class TissueSurfaceSnapshot
        {
            public readonly Vector3 normal;
            private readonly Vector3 _u, _v;
            private readonly Vector3[] _points;
            private readonly int[] _indices;
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly HashSet<int> _visited = new HashSet<int>();
            private readonly Vector3[] _clipA = new Vector3[12], _clipB = new Vector3[12];
            private readonly Vector3 _min, _max;
            private const float CellSize = 0.1f;

            public TissueSurfaceSnapshot(Vector3[] vertices, int[] indices, Vector3 freeNormal)
            {
                if (vertices == null || vertices.Length < 3 || indices == null ||
                    indices.Length < 3 || indices.Length % 3 != 0 || freeNormal.sqrMagnitude < 0.5f)
                    throw new ArgumentException("invalid tissue mesh dimensions or normal");
                normal = freeNormal.normalized;
                _u = Vector3.Cross(normal, Vector3.forward);
                if (_u.sqrMagnitude < 1e-8f) _u = Vector3.Cross(normal, Vector3.right);
                _u.Normalize();
                _v = Vector3.Cross(_u, normal).normalized;
                _points = new Vector3[vertices.Length];
                _indices = indices;
                Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = Project(vertices[i]);
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
                        float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
                        throw new ArgumentException("non-finite tissue vertex");
                    _points[i] = p;
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                _min = min;
                _max = max;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    if ((uint)indices[i] >= (uint)_points.Length || (uint)indices[i + 1] >= (uint)_points.Length ||
                        (uint)indices[i + 2] >= (uint)_points.Length)
                        throw new ArgumentException("invalid tissue triangle index");
                    Vector3 lo = Vector3.Min(_points[indices[i]], Vector3.Min(_points[indices[i + 1]], _points[indices[i + 2]]));
                    Vector3 hi = Vector3.Max(_points[indices[i]], Vector3.Max(_points[indices[i + 1]], _points[indices[i + 2]]));
                    for (int x = Cell(lo.x); x <= Cell(hi.x); x++)
                    for (int z = Cell(lo.z); z <= Cell(hi.z); z++)
                    {
                        long key = Key(x, z);
                        if (!_cells.TryGetValue(key, out List<int> triangles))
                            _cells.Add(key, triangles = new List<int>());
                        triangles.Add(i);
                    }
                }
            }

            private Vector3 Project(Vector3 p) => new Vector3(Vector3.Dot(p, _u), Vector3.Dot(p, normal), Vector3.Dot(p, _v));
            private static int Cell(float value) => Mathf.FloorToInt(value / CellSize);
            private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

            public float CapsuleGap(Vector3 a, Vector3 b, float radius, float tissueMargin, float collisionMargin)
            {
                Vector3 pa = Project(a), pb = Project(b);
                float padding = radius + tissueMargin + collisionMargin;
                float height = MaximumProjection(Vector3.Min(pa, pb) - Vector3.one * padding,
                    Vector3.Max(pa, pb) + Vector3.one * padding);
                return Mathf.Min(pa.y, pb.y) - radius - tissueMargin - height;
            }

            public float ProjectionForRadiusInterval(Vector3 axisPoint, Vector3 radial, Vector3 forward,
                float halfSegment, float lower, float upper, float padding)
            {
                Vector3 a = Project(axisPoint + radial * lower - forward * halfSegment);
                Vector3 b = Project(axisPoint + radial * lower + forward * halfSegment);
                Vector3 c = Project(axisPoint + radial * upper - forward * halfSegment);
                Vector3 d = Project(axisPoint + radial * upper + forward * halfSegment);
                return MaximumProjection(Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d)) - Vector3.one * padding,
                    Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d)) + Vector3.one * padding);
            }

            private float MaximumProjection(Vector3 min, Vector3 max)
            {
                float height = float.NegativeInfinity;
                if (max.x < _min.x || min.x > _max.x || max.z < _min.z || min.z > _max.z)
                    return height;
                _visited.Clear();
                int x0 = Cell(Mathf.Max(min.x, _min.x)), x1 = Cell(Mathf.Min(max.x, _max.x));
                int z0 = Cell(Mathf.Max(min.z, _min.z)), z1 = Cell(Mathf.Min(max.z, _max.z));
                for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!_cells.TryGetValue(Key(x, z), out List<int> triangles)) continue;
                    foreach (int offset in triangles)
                    {
                        if (!_visited.Add(offset)) continue;
                        _clipA[0] = _points[_indices[offset]];
                        _clipA[1] = _points[_indices[offset + 1]];
                        _clipA[2] = _points[_indices[offset + 2]];
                        Vector3[] input = _clipA, output = _clipB;
                        int count = 3;
                        for (int plane = 0; plane < 4 && count > 0; plane++)
                        {
                            int nextCount = 0;
                            Vector3 previous = input[count - 1];
                            float previousDistance = ClipDistance(previous, plane, min, max);
                            for (int i = 0; i < count; i++)
                            {
                                Vector3 current = input[i];
                                float distance = ClipDistance(current, plane, min, max);
                                if ((distance >= 0f) != (previousDistance >= 0f))
                                    output[nextCount++] = Vector3.Lerp(previous, current, previousDistance / (previousDistance - distance));
                                if (distance >= 0f) output[nextCount++] = current;
                                previous = current;
                                previousDistance = distance;
                            }
                            count = nextCount;
                            Vector3[] swap = input; input = output; output = swap;
                        }
                        for (int i = 0; i < count; i++) height = Mathf.Max(height, input[i].y);
                    }
                }
                return height;
            }

            private static float ClipDistance(Vector3 p, int plane, Vector3 min, Vector3 max)
            {
                switch (plane)
                {
                    case 0: return p.x - min.x;
                    case 1: return max.x - p.x;
                    case 2: return p.z - min.z;
                    default: return max.z - p.z;
                }
            }
        }

        private bool TryGetTissueSafetyFrame(
            out TissueSafetyFrame frame)
        {
            frame = default;
            if (!avoidTissueBackground)
                return false;
            if (_tissueSafetyFrameCached)
            {
                frame = _tissueSafetyFrame;
                return true;
            }

            if (tissueAvoidanceSurface == null)
            {
                MeshFilter[] meshFilters =
                    FindObjectsOfType<MeshFilter>();
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    MeshFilter candidate = meshFilters[i];
                    if (candidate != null &&
                        candidate.name == "TissueSurface" &&
                        candidate.sharedMesh != null)
                    {
                        tissueAvoidanceSurface = candidate;
                        break;
                    }
                }
            }

            if (tissueAvoidanceSurface == null ||
                tissueAvoidanceSurface.sharedMesh == null)
            {
                return false;
            }

            Transform surfaceTransform =
                tissueAvoidanceSurface.transform;
            Vector3 freeNormal =
                NormalizeOrFallback(
                    surfaceTransform.up,
                    Vector3.up);
            Vector3[] vertices =
                tissueAvoidanceSurface.sharedMesh.vertices;
            if (vertices == null || vertices.Length == 0)
                return false;

            Vector3 boundsCenter =
                surfaceTransform.TransformPoint(
                    tissueAvoidanceSurface.sharedMesh.bounds.center);
            MeshFilter vesselOuter =
                driver != null
                    ? driver.outerMeshFilter
                    : null;
            Vector3 vesselReference =
                vesselOuter != null
                    ? vesselOuter.transform.TransformPoint(
                        vesselOuter.sharedMesh != null
                            ? vesselOuter.sharedMesh.bounds.center
                            : Vector3.zero)
                    : (driver != null
                        ? 0.5f *
                            (driver.SutureSideAGuideCenterWorld +
                             driver.SutureSideBGuideCenterWorld)
                        : transform.position);
            if (Vector3.Dot(
                    vesselReference - boundsCenter,
                    freeNormal) < 0f)
            {
                freeNormal = -freeNormal;
            }

            float minimumProjection =
                float.PositiveInfinity;
            float maximumProjection =
                float.NegativeInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                float projection = Vector3.Dot(
                    surfaceTransform.TransformPoint(vertices[i]),
                    freeNormal);
                minimumProjection =
                    Mathf.Min(minimumProjection, projection);
                maximumProjection =
                    Mathf.Max(maximumProjection, projection);
            }

            if (float.IsInfinity(minimumProjection) ||
                float.IsInfinity(maximumProjection) ||
                float.IsNaN(minimumProjection) ||
                float.IsNaN(maximumProjection))
            {
                return false;
            }

            float vesselMaximumProjection =
                float.NegativeInfinity;
            if (vesselOuter != null &&
                vesselOuter.sharedMesh != null)
            {
                Vector3[] vesselVertices =
                    vesselOuter.sharedMesh.vertices;
                Transform vesselTransform =
                    vesselOuter.transform;
                for (int i = 0; i < vesselVertices.Length; i++)
                {
                    vesselMaximumProjection = Mathf.Max(
                        vesselMaximumProjection,
                        Vector3.Dot(
                            vesselTransform.TransformPoint(
                                vesselVertices[i]),
                            freeNormal));
                }
            }
            if (float.IsNegativeInfinity(
                    vesselMaximumProjection))
            {
                vesselMaximumProjection =
                    maximumProjection;
            }

            _tissueSafetyFrame = new TissueSafetyFrame
            {
                freeNormal = freeNormal,
                surfaceMinProjection = minimumProjection,
                surfaceMaxProjection = maximumProjection,
                vesselMaxProjection = vesselMaximumProjection,
                obstacleMaxProjection = Mathf.Max(
                    maximumProjection,
                    vesselMaximumProjection),
                vertexCount = vertices.Length,
            };
            _tissueSafetyFrameCached = true;
            frame = _tissueSafetyFrame;
            return true;
        }

        private static Vector3 ApplyPitchYaw(Vector3 direction, float pitchDeg, float yawDeg)
        {
            Vector3 fwd = NormalizeOrFallback(direction, Vector3.forward);
            Vector3 up = BuildPoseUp(fwd);
            Vector3 right = Vector3.Cross(up, fwd).normalized;
            Quaternion yaw = Quaternion.AngleAxis(yawDeg, up);
            Quaternion pitch = Quaternion.AngleAxis(pitchDeg, right);
            return NormalizeOrFallback(yaw * pitch * fwd, fwd);
        }

        private static Vector3 BuildPoseUp(Vector3 forward)
        {
            Vector3 fwd = NormalizeOrFallback(forward, Vector3.forward);
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(fwd, up)) > 0.92f)
                up = Vector3.right;
            up = Vector3.ProjectOnPlane(up, fwd);
            return NormalizeOrFallback(up, Vector3.up);
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude > 1e-8f)
                return value.normalized;
            if (fallback.sqrMagnitude > 1e-8f)
                return fallback.normalized;
            return Vector3.forward;
        }

        private int GetNeedlePassCount()
        {
            ClothSimulator simulator = driver != null ? driver.Simulator : null;
            int colliderId = needleRig != null ? needleRig.GetTargetColliderEntityId() : 0;
            return simulator != null && colliderId != 0 ? simulator.GetNeedlePassCount(colliderId) : 0;
        }

        private bool ResolveRefs()
        {
            if (driver == null)
                driver = FindObjectOfType<ClothSimulationDriverDoubleLayer>();
            if (needleRig == null)
                needleRig = FindObjectOfType<NeedleSceneControlRig>();
            if (needleRig != null)
            {
                if (targetNeedle == null)
                    targetNeedle = needleRig.targetNeedle;
                if (needleDriver == null && needleRig.needleDriver != null)
                    needleDriver = needleRig.needleDriver;
                if (needleDriver == null && targetNeedle != null)
                    needleDriver = targetNeedle.GetComponent<NeedleManualDriver>();
            }
            return driver != null && needleRig != null;
        }

        private void EnsureRuntimeSystems()
        {
            if (createRuntimeCameras)
                EnsureRuntimeCameras();
            if (createRuntimeUI)
                EnsureRuntimeUI();
            EnsureLineRenderers();
        }

        private void EnsureRuntimeCameras()
        {
            if (mainCamera == null)
            {
                GameObject go = new GameObject("MainSutureCamera");
                go.transform.SetParent(transform, false);
                mainCamera = go.AddComponent<Camera>();
                mainCamera.depth = 0f;
                mainCamera.fieldOfView = 35f;
                mainCamera.orthographic = true;
                mainCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }

            if (miniMapCamera == null)
            {
                GameObject go = new GameObject("MiniMapCamera");
                go.transform.SetParent(transform, false);
                miniMapCamera = go.AddComponent<Camera>();
                miniMapCamera.depth = 1f;
                miniMapCamera.orthographic = true;
                miniMapCamera.clearFlags = CameraClearFlags.Depth;
            }
            miniMapCamera.rect =
                new Rect(0.73f, 0.69f, 0.25f, 0.28f);
            miniMapCamera.enabled =
                showPlanOverviewCamera &&
                useOrthographicPlanCamera;
        }

        private void EnsureRuntimeUI()
        {
            if (runtimeCanvas == null)
            {
                GameObject canvasGo = new GameObject("AutoSutureRuntimeCanvas");
                canvasGo.transform.SetParent(transform, false);
                runtimeCanvas = canvasGo.AddComponent<Canvas>();
                runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            if (FindObjectOfType<EventSystem>() == null)
            {
                GameObject eventGo = new GameObject("AutoSutureEventSystem");
                eventGo.transform.SetParent(transform, false);
                eventGo.AddComponent<EventSystem>();
                eventGo.AddComponent<StandaloneInputModule>();
            }

            if (_runtimePanel != null && _statusText != null && _workflowText != null && _hintText != null)
                return;

            _uiFont = _uiFont != null ? _uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Transform oldPanel = runtimeCanvas.transform.Find("AutoSuturePanel");
            if (oldPanel != null)
                Destroy(oldPanel.gameObject);
            // Play-mode hot reload can retain the old Text references while
            // newly introduced controls have not been created yet.
            Transform oldFlowPanel = runtimeCanvas.transform.Find("AutoSutureFlowPanel");
            if (oldFlowPanel != null)
            {
                oldFlowPanel.gameObject.SetActive(false);
                Destroy(oldFlowPanel.gameObject);
            }

            GameObject panel = new GameObject("AutoSutureFlowPanel");
            panel.transform.SetParent(runtimeCanvas.transform, false);
            RectTransform rect = panel.AddComponent<RectTransform>();
            _runtimePanel = rect;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(14f, -14f);
            rect.sizeDelta = new Vector2(660f, 142f);
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.06f, 0.74f);

            _workflowText = CreateText(panel.transform, "Workflow", new Vector2(14f, -12f), new Vector2(418f, 24f), 14, TextAnchor.UpperLeft);
            _statusText = CreateText(panel.transform, "Status", new Vector2(14f, -38f), new Vector2(632f, 24f), 14, TextAnchor.UpperLeft);
            _hintText = CreateText(panel.transform, "Hint", new Vector2(14f, -64f), new Vector2(632f, 22f), 12, TextAnchor.UpperLeft);
            _hintText.color = new Color(0.82f, 0.9f, 1f, 0.95f);

            CreateButton(panel.transform, "Screenshot [F12]", new Vector2(438f, -8f), new Vector2(132f, 28f), CapturePatentScreenshot);
            CreateButton(panel.transform, "Folder", new Vector2(578f, -8f), new Vector2(68f, 28f), OpenScreenshotFolder);

            CreateButton(panel.transform, "Preview", new Vector2(14f, -98f), new Vector2(78f, 30f), PreviewPlan);
            CreateButton(panel.transform, "Prev Point", new Vector2(100f, -98f), new Vector2(92f, 30f), PreviousPreviewPoint);
            CreateButton(panel.transform, "Next Point", new Vector2(200f, -98f), new Vector2(92f, 30f), NextPreviewPoint);
            CreateButton(panel.transform, "Start", new Vector2(306f, -98f), new Vector2(74f, 30f), StartAutoSuture);
            CreateButton(panel.transform, "Pause", new Vector2(388f, -98f), new Vector2(74f, 30f), PauseAutoSuture);
            CreateButton(panel.transform, "Continue", new Vector2(470f, -98f), new Vector2(84f, 30f), ContinueAutoSuture);
            CreateButton(panel.transform, "Reset", new Vector2(562f, -98f), new Vector2(74f, 30f), ResetAutoSuture);
        }

        private Text CreateText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor anchor)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Text text = go.AddComponent<Text>();
            text.font = _uiFont;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private void CreateButton(Transform parent, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
        {
            CreateButton(parent, label, anchoredPosition, new Vector2(106f, 34f), action);
        }

        private void CreateButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            GameObject go = new GameObject(label + "Button");
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.22f, 0.26f, 0.94f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(action);
            Text text = CreateText(go.transform, "Label", Vector2.zero, rect.sizeDelta, 13, TextAnchor.MiddleCenter);
            text.text = label;
            text.raycastTarget = false;
        }

        private void EnsureLineRenderers()
        {
            _lineMaterial = _lineMaterial != null ? _lineMaterial : CreateLineMaterial(new Color(1f, 0.72f, 0.05f, 1f));
            _markerMaterial = _markerMaterial != null ? _markerMaterial : CreateLineMaterial(Color.white);
            if (_trajectoryLine == null)
                _trajectoryLine = CreateLineRenderer("AutoSutureTrajectory", new Color(1f, 0.72f, 0.05f, 1f), 0.0008f);
            if (_miniMapLineA == null)
                _miniMapLineA = CreateLineRenderer("AutoSutureSideAOverview", new Color(0.1f, 0.45f, 1f, 0.8f), 0.001f);
            if (_miniMapLineB == null)
                _miniMapLineB = CreateLineRenderer("AutoSutureSideBOverview", new Color(1f, 0.25f, 0.15f, 0.8f), 0.001f);
        }

        private Material CreateLineMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            Material material = new Material(shader);
            material.color = color;
            return material;
        }

        private LineRenderer CreateLineRenderer(string name, Color color, float width)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.material = CreateLineMaterial(color);
            line.startWidth = width;
            line.endWidth = width;
            line.useWorldSpace = true;
            line.positionCount = 0;
            return line;
        }

        private void UpdateRuntimeUI()
        {
            if (_runtimePanel != null)
            {
                RectTransform canvasRect = runtimeCanvas.transform as RectTransform;
                float fitScale = canvasRect != null ? Mathf.Min((canvasRect.rect.width - 28f) / 660f,
                    (canvasRect.rect.height - 28f) / 142f) : 1f;
                _runtimePanel.localScale = Vector3.one * Mathf.Max(0.1f, Mathf.Min(runtimePanelScale, fitScale));
            }
            if (_statusText == null)
                return;

            AutoSuturePass pass = GetCurrentPass();
            string side = pass != null ? GetSideLabel(pass.side) : "-";
            string direction = pass != null ? GetDirectionLabel(pass.direction) : "-";
            int totalPasses = plan != null ? plan.Count * 2 : 0;
            int currentPassNumber = totalPasses > 0 ? GetFlatPassIndex(_currentPairIndex, _currentPassInPair) + 1 : 0;
            string mode = _state == AutoSutureState.PreviewPlan
                ? "Preview / Adjust"
                : (_paused ? "Paused / Observe" : _state.ToString());
            if (_workflowText != null)
                _workflowText.text = "Preview > Adjust > Start > Two Punctures > Pull";
            _statusText.text =
                $"Mode: {mode}    Point: {currentPassNumber}/{Mathf.Max(0, totalPasses)}    Pair: {_currentPairIndex + 1}/{Mathf.Max(1, plan != null ? plan.Count : 1)}    {side} {direction}";
            if (_hintText != null)
            {
                if (Time.unscaledTime < _screenshotNoticeUntil)
                    _hintText.text = _screenshotNotice;
                else if (_state == AutoSutureState.PreviewPlan)
                    _hintText.text = "Click or drag a marker to adjust it. The highlighted marker is the next puncture point.";
                else if (_paused)
                    _hintText.text = "Needle + vessel frozen. Camera controls stay active. Continue resumes; Reset starts over.";
                else if (!string.IsNullOrEmpty(_errorReason))
                    _hintText.text = "Error: " + _errorReason;
                else
                    _hintText.text = "Start runs each pair as two punctures followed by one pull-line step.";
            }
        }

        private static string ScreenshotDirectory
        {
            get
            {
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PatentScreenshots", "Manual"));
#else
                return Path.Combine(Application.persistentDataPath, "PatentScreenshots", "Manual");
#endif
            }
        }

        private void SetScreenshotNotice(string message)
        {
            _screenshotNotice = message;
            _screenshotNoticeUntil = Time.unscaledTime + 8f;
            UpdateRuntimeUI();
        }

        public void OpenScreenshotFolder()
        {
            try
            {
                Directory.CreateDirectory(ScreenshotDirectory);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                // Pass the native Unicode directory directly to the Windows
                // shell; file URLs can silently fail to open local folders.
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ScreenshotDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                })) { }
#else
                Application.OpenURL(new Uri(ScreenshotDirectory + Path.DirectorySeparatorChar).AbsoluteUri);
#endif
            }
            catch (Exception exception)
            {
                SetScreenshotNotice("Cannot open screenshot folder. See Console for the path.");
                Debug.LogError($"[PatentScreenshot] Folder: {ScreenshotDirectory}\n{exception}", this);
            }
        }

        // Render synchronously without changing the simulation's pause state.
        // Each request produces exactly one JPEG file.
        public void CapturePatentScreenshot()
        {
            Camera source = mainCamera != null ? mainCamera : Camera.main;
            if (source == null)
            {
                SetScreenshotNotice("Screenshot failed: no main camera is available.");
                Debug.LogError("[PatentScreenshot] No main camera is available.", this);
                return;
            }

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = null;
            Texture2D pixels = null;
            GameObject captureObject = null;
            bool canvasEnabled = runtimeCanvas != null && runtimeCanvas.enabled;
            try
            {
                // Fill the fixed output with a centered crop of the current view.
                // Scale clip-space axes to preserve geometry proportions without
                // adding margins or changing the live camera's projection.
                float outputAspect = (float)ScreenshotWidth / ScreenshotHeight;
                Matrix4x4 crop = Matrix4x4.Scale(new Vector3(
                    Mathf.Max(1f, source.aspect / outputAspect),
                    Mathf.Max(1f, outputAspect / source.aspect), 1f));
                target = RenderTexture.GetTemporary(ScreenshotWidth, ScreenshotHeight, 24, RenderTextureFormat.ARGB32);
                captureObject = new GameObject("PatentScreenshotCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera capture = captureObject.AddComponent<Camera>();
                capture.CopyFrom(source);
                capture.enabled = false;
                capture.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                capture.targetTexture = target;
                capture.rect = new Rect(0f, 0f, 1f, 1f);
                capture.projectionMatrix = crop * source.projectionMatrix;
                if (runtimeCanvas != null) runtimeCanvas.enabled = false;
                RenderTexture.active = target;
                GL.Clear(true, true, source.backgroundColor);
                capture.Render();

                pixels = new Texture2D(ScreenshotWidth, ScreenshotHeight, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, ScreenshotWidth, ScreenshotHeight), 0, 0, false);
                pixels.Apply(false, false);
                byte[] jpg = AddJpegPrintResolution(pixels.EncodeToJPG(100));
                Directory.CreateDirectory(ScreenshotDirectory);
                string stem = $"Capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{_state}_P{_currentPairIndex + 1}_{Guid.NewGuid():N}";
                string path = Path.Combine(ScreenshotDirectory, stem);
                File.WriteAllBytes(path + ".jpg", jpg);
                SetScreenshotNotice("Saved one JPG: 1920 x 1080, 300 DPI. Click Folder to view.");
                Debug.Log($"[PatentScreenshot] Saved {ScreenshotWidth}x{ScreenshotHeight} at {ScreenshotDpi} DPI; " +
                    $"main view, centered crop, no margins/panel/overview; state={_state}, paused={_paused}\n{path}.jpg", this);
            }
            catch (Exception exception)
            {
                SetScreenshotNotice("Screenshot failed. See Console for details and save path.");
                Debug.LogError($"[PatentScreenshot] Failed to save in {ScreenshotDirectory}\n{exception}", this);
            }
            finally
            {
                if (runtimeCanvas != null) runtimeCanvas.enabled = canvasEnabled;
                RenderTexture.active = previousActive;
                if (captureObject != null)
                {
                    captureObject.GetComponent<Camera>().targetTexture = null;
                    Destroy(captureObject);
                }
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (pixels != null) Destroy(pixels);
            }
        }

        private static byte[] AddJpegPrintResolution(byte[] jpg)
        {
            // Update an existing JFIF header, or supply one when the Unity
            // encoder has omitted it. Density is metadata, not image resampling.
            for (int offset = 2; offset + 17 < jpg.Length && jpg[offset] == 0xff;)
            {
                byte marker = jpg[offset + 1];
                if (marker == 0xda || marker == 0xd9) break;
                int length = (jpg[offset + 2] << 8) | jpg[offset + 3];
                if (length < 2 || offset + length + 2 > jpg.Length) break;
                if (marker == 0xe0 && length >= 16 && jpg[offset + 4] == 'J' && jpg[offset + 5] == 'F' &&
                    jpg[offset + 6] == 'I' && jpg[offset + 7] == 'F' && jpg[offset + 8] == 0)
                {
                    jpg[offset + 11] = 1;
                    jpg[offset + 12] = jpg[offset + 14] = (byte)(ScreenshotDpi >> 8);
                    jpg[offset + 13] = jpg[offset + 15] = (byte)(ScreenshotDpi & 0xff);
                    return jpg;
                }
                offset += length + 2;
            }
            byte[] header = { 0xff, 0xe0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0,
                1, 1, 1, (byte)(ScreenshotDpi >> 8), (byte)(ScreenshotDpi & 0xff),
                (byte)(ScreenshotDpi >> 8), (byte)(ScreenshotDpi & 0xff), 0, 0 };
            byte[] result = new byte[jpg.Length + header.Length];
            Buffer.BlockCopy(jpg, 0, result, 0, 2);
            Buffer.BlockCopy(header, 0, result, 2, header.Length);
            Buffer.BlockCopy(jpg, 2, result, 2 + header.Length, jpg.Length - 2);
            return result;
        }

        private void HandlePreviewPointEditing()
        {
            if (!allowPreviewPointEditing || _state != AutoSutureState.PreviewPlan || mainCamera == null || plan == null || plan.Count <= 0)
            {
                _draggingPlanMarker = false;
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (IsPointerOverRuntimeUI())
                    return;

                int pairIndex;
                int passIndex;
                if (FindNearestPlanMarker(Input.mousePosition, out pairIndex, out passIndex))
                    BeginPreviewPointDrag(pairIndex, passIndex);
            }

            if (!_draggingPlanMarker)
                return;

            if (Input.GetMouseButton(0))
                UpdatePreviewPointDrag();

            if (Input.GetMouseButtonUp(0))
                EndPreviewPointDrag();
        }

        private bool IsPointerOverRuntimeUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private bool FindNearestPlanMarker(Vector2 screenPosition, out int pairIndex, out int passIndex)
        {
            pairIndex = -1;
            passIndex = -1;
            if (mainCamera == null || plan == null || plan.stitchPairs == null)
                return false;

            float bestSqr = markerPickRadiusPixels * markerPickRadiusPixels;
            for (int i = 0; i < plan.stitchPairs.Count; i++)
            {
                TryPickPassMarker(screenPosition, i, 0, plan.stitchPairs[i].sideAPass, ref bestSqr, ref pairIndex, ref passIndex);
                TryPickPassMarker(screenPosition, i, 1, plan.stitchPairs[i].sideBPass, ref bestSqr, ref pairIndex, ref passIndex);
            }

            return pairIndex >= 0;
        }

        private void TryPickPassMarker(Vector2 screenPosition, int pairIndex, int passIndex, AutoSuturePass pass, ref float bestSqr, ref int bestPair, ref int bestPass)
        {
            if (pass == null)
                return;

            Vector3 screen = mainCamera.WorldToScreenPoint(pass.entryPointWorld);
            if (screen.z < 0f)
                return;

            float sqr = ((Vector2)screen - screenPosition).sqrMagnitude;
            if (sqr > bestSqr)
                return;

            bestSqr = sqr;
            bestPair = pairIndex;
            bestPass = passIndex;
        }

        private void BeginPreviewPointDrag(int pairIndex, int passIndex)
        {
            AutoSuturePass pass = GetPass(pairIndex, passIndex);
            if (pass == null || mainCamera == null)
                return;

            _currentPairIndex = pairIndex;
            _currentPassInPair = passIndex;
            Vector3 dragPlaneNormal =
                mainCamera.transform.forward;
            if (useOrthographicPlanCamera &&
                useObliqueProcedureCamera)
            {
                BuildPlanCameraFrame(
                    out _,
                    out _,
                    out dragPlaneNormal);
            }
            _dragPlane = new Plane(
                dragPlaneNormal,
                pass.entryPointWorld);
            if (!TryGetDragPlanePoint(Input.mousePosition, out _dragStartWorld))
                return;

            _draggingPlanMarker = true;
            _dragPairIndex = pairIndex;
            _dragPassInPair = passIndex;
            _dragStartEntry = pass.entryPointWorld;
            _dragStartExit = pass.exitPointWorld;
        }

        private void UpdatePreviewPointDrag()
        {
            AutoSuturePass pass = GetPass(_dragPairIndex, _dragPassInPair);
            if (pass == null)
                return;

            Vector3 world;
            if (!TryGetDragPlanePoint(Input.mousePosition, out world))
                return;

            Vector3 delta = world - _dragStartWorld;
            pass.entryPointWorld = _dragStartEntry + delta;
            pass.exitPointWorld = _dragStartExit + delta;
            RefreshPassDirections(pass);
        }

        private void EndPreviewPointDrag()
        {
            AutoSuturePass pass = GetPass(_dragPairIndex, _dragPassInPair);
            if (pass != null)
            {
                pass.materialAnchorsCaptured = false;
                if (!TryCapturePassMaterialAnchorsFromPlanPoint(pass, out string reason))
                    Error($"MaterialAnchor drag recapture failed: {reason}");
                else if (!TryRefreshPassFromMaterialAnchors(pass, true, out reason))
                    Error($"MaterialAnchor drag refresh failed: {reason}");
            }
            _draggingPlanMarker = false;
            _dragPairIndex = -1;
            _dragPassInPair = -1;
        }

        private bool TryGetDragPlanePoint(Vector2 screenPosition, out Vector3 world)
        {
            world = Vector3.zero;
            if (mainCamera == null)
                return false;

            Ray ray = mainCamera.ScreenPointToRay(screenPosition);
            float enter;
            if (!_dragPlane.Raycast(ray, out enter))
                return false;

            world = ray.GetPoint(enter);
            return true;
        }

        private void UpdateMarkers()
        {
            if (!showMarkers || plan == null || plan.stitchPairs == null)
            {
                SetMarkerCount(0);
                return;
            }

            int required = plan.stitchPairs.Count * 2 + 1;
            SetMarkerCount(required);
            int markerIndex = 0;
            for (int i = 0; i < plan.stitchPairs.Count; i++)
            {
                UpdatePassMarker(markerIndex++, i, 0, plan.stitchPairs[i].sideAPass);
                UpdatePassMarker(markerIndex++, i, 1, plan.stitchPairs[i].sideBPass);
            }

            if (needleRig != null && markerIndex < _markerObjects.Count)
            {
                _markerObjects[markerIndex].transform.position = needleRig.GetSemanticTipPoint();
                _markerObjects[markerIndex].transform.localScale = Vector3.one * MarkerScale * 1.1f;
                SetMarkerColor(markerIndex, new Color(0.85f, 0f, 0.45f, 1f));
            }
        }

        private void UpdatePassMarker(int markerIndex, int pairIndex, int passIndex, AutoSuturePass pass)
        {
            if (markerIndex >= _markerObjects.Count || pass == null)
                return;

            _markerObjects[markerIndex].transform.position = pass.entryPointWorld;
            bool isCurrent = pairIndex == _currentPairIndex && passIndex == _currentPassInPair;
            bool isDragging = _draggingPlanMarker && pairIndex == _dragPairIndex && passIndex == _dragPassInPair;
            _markerObjects[markerIndex].transform.localScale = Vector3.one * MarkerScale * (isCurrent ? 1.65f : 1f);
            Color color = new Color(0.2f, 0.65f, 1f, 0.95f);
            if (pass.complete)
                color = new Color(0.2f, 1f, 0.35f, 0.95f);
            if (isDragging)
                color = new Color(1f, 0.1f, 0.82f, 1f);
            else if (_state == AutoSutureState.ErrorPaused && isCurrent)
                color = new Color(1f, 0.05f, 0.03f, 0.95f);
            else if (isCurrent && _state == AutoSutureState.PreviewPlan)
                color = new Color(1f, 0.92f, 0.12f, 1f);
            else if (isCurrent && _state != AutoSutureState.Idle)
                color = new Color(1f, 0.52f, 0.05f, 0.95f);
            SetMarkerColor(markerIndex, color);
        }

        private void SetMarkerCount(int count)
        {
            while (_markerObjects.Count < count)
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "AutoSutureMarker";
                marker.transform.SetParent(transform, false);
                Collider collider = marker.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
                Renderer renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material = new Material(_markerMaterial);
                _markerObjects.Add(marker);
                _markerRenderers.Add(renderer);
            }

            for (int i = 0; i < _markerObjects.Count; i++)
                _markerObjects[i].SetActive(i < count);
        }

        private void SetMarkerColor(int index, Color color)
        {
            if ((uint)index >= (uint)_markerRenderers.Count || _markerRenderers[index] == null)
                return;
            _markerRenderers[index].material.color = color;
        }

        private void UpdateTrajectoryLine()
        {
            if (_trajectoryLine == null)
                return;
            if (!showTrajectoryLine || GetCurrentPass() == null)
            {
                _trajectoryLine.positionCount = 0;
                return;
            }

            AutoSuturePass pass = GetCurrentPass();
            const int steps = 24;
            _trajectoryLine.positionCount = steps;
            Vector3 oldStart = _punctureStartTip;
            Vector3 oldPost = _puncturePostExit;
            _punctureStartTip = GetPreEntry(pass);
            _puncturePostExit = GetPostExit(pass);
            for (int i = 0; i < steps; i++)
            {
                Vector3 tip;
                Vector3 forward;
                EvaluatePuncturePath(pass, i / (steps - 1f), out tip, out forward);
                _trajectoryLine.SetPosition(i, tip);
            }
            _punctureStartTip = oldStart;
            _puncturePostExit = oldPost;

            UpdatePolylineRenderer(_miniMapLineA, driver != null ? driver.SutureSideAOuterBoundaryWorld : null);
            UpdatePolylineRenderer(_miniMapLineB, driver != null ? driver.SutureSideBOuterBoundaryWorld : null);
        }

        private static void UpdatePolylineRenderer(LineRenderer line, IReadOnlyList<Vector3> points)
        {
            if (line == null)
                return;
            if (!HasPolyline(points))
            {
                line.positionCount = 0;
                return;
            }

            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
                line.SetPosition(i, points[i]);
        }

        private void UpdateCameraRig()
        {
            HandleGameViewCameraInput();
            if (_manualCameraControl)
            {
                UpdateMiniMapWhileMainCameraIsManual();
                return;
            }

            if (useOrthographicPlanCamera)
            {
                UpdateOrthographicPlanCamera();
                return;
            }

            AutoSuturePass pass = GetCurrentPass();
            Vector3 center = pass != null
                ? (pass.entryPointWorld + pass.exitPointWorld) * 0.5f
                : GetPlanCenter();
            _automaticCameraPivot = center;
            _hasAutomaticCameraPivot = true;
            Vector3 forward = pass != null ? GetEntryDirection(pass) : Vector3.forward;
            Vector3 up = BuildPoseUp(forward);
            Vector3 right = Vector3.Cross(up, forward).normalized;

            if (mainCamera != null)
            {
                Vector3 pos = center - forward * 0.05f + up * 0.028f + right * 0.018f;
                mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, pos, 0.18f);
                Quaternion rot = Quaternion.LookRotation(center - mainCamera.transform.position, up);
                mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, rot, 0.22f);
            }

            if (miniMapCamera != null)
            {
                Bounds b = GetPlanBounds();
                float radius = Mathf.Max(0.04f, b.extents.magnitude);
                Vector3 pos = b.center + Vector3.up * radius * 2.6f + Vector3.back * radius * 0.75f;
                miniMapCamera.transform.position = pos;
                miniMapCamera.transform.rotation = Quaternion.LookRotation(b.center - pos, Vector3.up);
                miniMapCamera.orthographicSize = Mathf.Max(0.025f, radius * 1.25f);
            }
        }

        private void HandleGameViewCameraInput()
        {
            if (!enableSceneLikeCameraControl || mainCamera == null)
            {
                RestoreAutomaticCameraControl();
                return;
            }

            if (Input.GetKeyDown(cameraRestoreAutoKey))
            {
                RestoreAutomaticCameraControl();
                return;
            }

            if (Input.GetKeyDown(cameraFocusCurrentKey))
                FrameManualCamera(GetCurrentCameraFocusBounds());
            if (Input.GetKeyDown(cameraFrameWholeKey))
                FrameManualCamera(GetPlanBounds());

            Vector3 pointer = Input.mousePosition;
            bool pointerBlocked = IsPointerOverRuntimeUI() ||
                                  IsPointerOverMiniMap(pointer);
            bool dragStarted = false;

            if (Input.GetMouseButtonDown(1))
            {
                _cameraOrbitDragging = !pointerBlocked;
                if (_cameraOrbitDragging)
                    BeginManualCameraControl();
                dragStarted = true;
            }
            if (Input.GetMouseButtonUp(1))
                _cameraOrbitDragging = false;

            if (Input.GetMouseButtonDown(2))
            {
                _cameraPanDragging = !pointerBlocked;
                if (_cameraPanDragging)
                    BeginManualCameraControl();
                dragStarted = true;
            }
            if (Input.GetMouseButtonUp(2))
                _cameraPanDragging = false;

            Vector3 pointerDelta = dragStarted
                ? Vector3.zero
                : pointer - _lastCameraPointer;
            if (_cameraOrbitDragging && Input.GetMouseButton(1))
                OrbitManualCamera(pointerDelta);
            if (_cameraPanDragging && Input.GetMouseButton(2))
                PanManualCamera(pointerDelta);

            float scroll = Input.mouseScrollDelta.y;
            if (!pointerBlocked && Mathf.Abs(scroll) > 1e-4f)
            {
                BeginManualCameraControl();
                ZoomManualCamera(scroll);
            }

            _lastCameraPointer = pointer;
        }

        private void BeginManualCameraControl()
        {
            if (!_manualCameraControl)
            {
                _manualCameraControl = true;
                _manualCameraFrameInitialized = false;
            }
            EnsureManualCameraFrame();
        }

        private void RestoreAutomaticCameraControl()
        {
            _manualCameraControl = false;
            _cameraOrbitDragging = false;
            _cameraPanDragging = false;
            _manualCameraFrameInitialized = false;
        }

        private void EnsureManualCameraFrame()
        {
            if (_manualCameraFrameInitialized || mainCamera == null)
                return;

            _manualCameraPivot = _hasAutomaticCameraPivot
                ? _automaticCameraPivot
                : GetPlanCenter();
            float distance = Vector3.Distance(
                mainCamera.transform.position,
                _manualCameraPivot);
            _manualCameraFrameSize = mainCamera.orthographic
                ? Mathf.Max(0.001f, mainCamera.orthographicSize)
                : Mathf.Max(0.001f, distance);
            _manualCameraFrameInitialized = true;
        }

        private void OrbitManualCamera(Vector3 pointerDelta)
        {
            EnsureManualCameraFrame();
            Vector3 offset =
                mainCamera.transform.position - _manualCameraPivot;
            if (offset.sqrMagnitude < 1e-8f)
            {
                offset = -mainCamera.transform.forward *
                         Mathf.Max(0.08f, _manualCameraFrameSize * 3f);
            }

            Quaternion yaw = Quaternion.AngleAxis(
                pointerDelta.x * cameraOrbitDegreesPerPixel,
                Vector3.up);
            Vector3 yawed = yaw * offset;
            Quaternion pitch = Quaternion.AngleAxis(
                -pointerDelta.y * cameraOrbitDegreesPerPixel,
                mainCamera.transform.right);
            Vector3 candidate = pitch * yawed;
            if (candidate.sqrMagnitude > 1e-8f &&
                Mathf.Abs(Vector3.Dot(
                    candidate.normalized,
                    Vector3.up)) > 0.985f)
            {
                candidate = yawed;
            }

            mainCamera.transform.position =
                _manualCameraPivot + candidate;
            Vector3 forward =
                _manualCameraPivot - mainCamera.transform.position;
            if (forward.sqrMagnitude > 1e-8f)
            {
                mainCamera.transform.rotation = Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up);
            }
        }

        private void PanManualCamera(Vector3 pointerDelta)
        {
            EnsureManualCameraFrame();
            float worldPerPixel;
            if (mainCamera.orthographic)
            {
                worldPerPixel =
                    mainCamera.orthographicSize * 2f /
                    Mathf.Max(1f, Screen.height);
            }
            else
            {
                float distance = Mathf.Max(
                    0.001f,
                    Vector3.Distance(
                        mainCamera.transform.position,
                        _manualCameraPivot));
                worldPerPixel =
                    2f * distance *
                    Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) /
                    Mathf.Max(1f, Screen.height);
            }

            Vector3 worldDelta =
                (-mainCamera.transform.right * pointerDelta.x -
                 mainCamera.transform.up * pointerDelta.y) *
                worldPerPixel *
                cameraPanSensitivity;
            mainCamera.transform.position += worldDelta;
            _manualCameraPivot += worldDelta;
        }

        private void ZoomManualCamera(float scroll)
        {
            EnsureManualCameraFrame();
            float minFactor = Mathf.Max(0.001f, cameraMinimumZoomFactor);
            float maxFactor = Mathf.Max(minFactor, cameraMaximumZoomFactor);
            if (mainCamera.orthographic)
            {
                float next =
                    mainCamera.orthographicSize *
                    Mathf.Exp(-scroll * cameraZoomSensitivity);
                mainCamera.orthographicSize = Mathf.Clamp(
                    next,
                    _manualCameraFrameSize * minFactor,
                    _manualCameraFrameSize * maxFactor);
                return;
            }

            Vector3 offset =
                mainCamera.transform.position - _manualCameraPivot;
            if (offset.sqrMagnitude < 1e-8f)
                return;
            float distance =
                offset.magnitude *
                Mathf.Exp(-scroll * cameraZoomSensitivity);
            distance = Mathf.Clamp(
                distance,
                _manualCameraFrameSize * minFactor,
                _manualCameraFrameSize * maxFactor);
            mainCamera.transform.position =
                _manualCameraPivot + offset.normalized * distance;
        }

        private bool IsPointerOverMiniMap(Vector3 pointer)
        {
            return miniMapCamera != null &&
                   miniMapCamera.enabled &&
                   miniMapCamera.pixelRect.Contains(pointer);
        }

        private Bounds GetCurrentCameraFocusBounds()
        {
            AutoSuturePass pass = GetCurrentPass();
            if (pass == null)
                return GetPlanBounds();

            Bounds whole = GetPlanBounds();
            Bounds focus = new Bounds(
                pass.entryPointWorld,
                Vector3.one * 0.001f);
            focus.Encapsulate(pass.exitPointWorld);
            float padding = Mathf.Max(
                0.006f,
                whole.extents.magnitude * 0.12f);
            focus.Expand(Vector3.one * padding * 2f);
            return focus;
        }

        private void FrameManualCamera(Bounds bounds)
        {
            if (mainCamera == null)
                return;

            Vector3 forward = NormalizeOrFallback(
                mainCamera.transform.forward,
                Vector3.forward);
            Vector3 right = NormalizeOrFallback(
                mainCamera.transform.right,
                Vector3.right);
            Vector3 up = NormalizeOrFallback(
                mainCamera.transform.up,
                Vector3.up);
            float size = ComputePlanCameraSize(
                bounds,
                bounds.center,
                right,
                up);
            float distance = Mathf.Max(
                0.08f,
                bounds.extents.magnitude * planCameraDistanceScale);

            _manualCameraControl = true;
            _manualCameraPivot = bounds.center;
            _manualCameraFrameSize = mainCamera.orthographic
                ? size
                : distance;
            _manualCameraFrameInitialized = true;
            if (mainCamera.orthographic)
                mainCamera.orthographicSize = size;
            mainCamera.transform.position =
                _manualCameraPivot - forward * distance;
            mainCamera.nearClipPlane =
                Mathf.Max(0.001f, distance * 0.02f);
            mainCamera.farClipPlane =
                Mathf.Max(10f, distance * 6f);
        }

        private void UpdateMiniMapWhileMainCameraIsManual()
        {
            if (miniMapCamera == null)
                return;

            if (!useOrthographicPlanCamera)
            {
                Bounds b = GetPlanBounds();
                float radius = Mathf.Max(0.04f, b.extents.magnitude);
                Vector3 pos =
                    b.center +
                    Vector3.up * radius * 2.6f +
                    Vector3.back * radius * 0.75f;
                miniMapCamera.transform.position = pos;
                miniMapCamera.transform.rotation = Quaternion.LookRotation(
                    b.center - pos,
                    Vector3.up);
                miniMapCamera.orthographicSize =
                    Mathf.Max(0.025f, radius * 1.25f);
                return;
            }

            miniMapCamera.enabled = showPlanOverviewCamera;
            if (!miniMapCamera.enabled)
                return;

            Bounds bounds = GetPlanBounds();
            Vector3 center = bounds.center;
            BuildPlanCameraFrame(
                out Vector3 planRight,
                out Vector3 planUp,
                out Vector3 viewForward);
            float size = ComputePlanCameraSize(
                bounds,
                center,
                planRight,
                planUp);
            float distance = Mathf.Max(
                0.08f,
                bounds.extents.magnitude * planCameraDistanceScale);
            Vector3 position = center - viewForward * distance;
            miniMapCamera.orthographic = true;
            miniMapCamera.orthographicSize = size;
            miniMapCamera.transform.position = position;
            miniMapCamera.transform.rotation = Quaternion.LookRotation(
                viewForward,
                planUp);
            miniMapCamera.nearClipPlane =
                Mathf.Max(0.001f, distance * 0.02f);
            miniMapCamera.farClipPlane =
                Mathf.Max(10f, distance * 6f);
        }

        private void UpdateOrthographicPlanCamera()
        {
            if (mainCamera == null)
                return;

            Bounds bounds = GetPlanBounds();
            Vector3 center = bounds.center;
            Vector3 planRight;
            Vector3 planUp;
            Vector3 viewForward;
            BuildPlanCameraFrame(out planRight, out planUp, out viewForward);
            Vector3 tissueFreeNormal =
                Vector3.up;
            if (TryGetTissueSafetyFrame(
                    out TissueSafetyFrame tissueFrame))
            {
                tissueFreeNormal =
                    tissueFrame.freeNormal;
            }

            Vector3 mainRight = planRight;
            Vector3 mainUp = planUp;
            Vector3 mainForward = viewForward;
            if (useObliqueProcedureCamera)
            {
                BuildObliqueProcedureCameraFrame(
                    planRight,
                    planUp,
                    viewForward,
                    tissueFreeNormal,
                    procedureCameraObliqueAngle,
                    out mainRight,
                    out mainUp,
                    out mainForward);
            }

            float contextRadius =
                Mathf.Max(0.02f, bounds.extents.magnitude);
            Vector3 framingCenter =
                center -
                tissueFreeNormal *
                contextRadius *
                Mathf.Clamp01(procedureCameraTissueBias);
            _automaticCameraPivot = framingCenter;
            _hasAutomaticCameraPivot = true;
            float size = ComputePlanCameraSize(
                bounds,
                framingCenter,
                mainRight,
                mainUp);
            float distance = Mathf.Max(0.08f, bounds.extents.magnitude * planCameraDistanceScale);
            Vector3 pos =
                framingCenter -
                mainForward * distance;

            mainCamera.orthographic = true;
            mainCamera.orthographicSize = size;
            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, pos, 0.24f);
            Quaternion rot =
                Quaternion.LookRotation(
                    mainForward,
                    mainUp);
            mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, rot, 0.24f);
            mainCamera.nearClipPlane =
                Mathf.Max(0.001f, distance * 0.02f);
            mainCamera.farClipPlane =
                Mathf.Max(10f, distance * 6f);

            if (miniMapCamera != null)
            {
                miniMapCamera.enabled =
                    showPlanOverviewCamera;
                if (miniMapCamera.enabled)
                {
                    float overviewSize =
                        ComputePlanCameraSize(
                            bounds,
                            center,
                            planRight,
                            planUp);
                    Vector3 overviewPosition =
                        center -
                        viewForward * distance;
                    miniMapCamera.orthographic = true;
                    miniMapCamera.orthographicSize =
                        overviewSize;
                    miniMapCamera.transform.position =
                        overviewPosition;
                    miniMapCamera.transform.rotation =
                        Quaternion.LookRotation(
                            viewForward,
                            planUp);
                    miniMapCamera.nearClipPlane =
                        Mathf.Max(0.001f, distance * 0.02f);
                    miniMapCamera.farClipPlane =
                        Mathf.Max(10f, distance * 6f);
                }
            }
        }

        private static void BuildObliqueProcedureCameraFrame(
            Vector3 planRight,
            Vector3 planUp,
            Vector3 planForward,
            Vector3 tissueFreeNormal,
            float obliqueAngleDeg,
            out Vector3 screenRight,
            out Vector3 screenUp,
            out Vector3 viewForward)
        {
            Vector3 baseForward =
                NormalizeOrFallback(
                    planForward,
                    Vector3.forward);
            Vector3 freeNormal =
                NormalizeOrFallback(
                    tissueFreeNormal,
                    planUp);
            Vector3 towardTissue =
                Vector3.ProjectOnPlane(
                    -freeNormal,
                    baseForward);
            if (towardTissue.sqrMagnitude <= 1e-10f)
            {
                towardTissue =
                    Vector3.ProjectOnPlane(
                        -planUp,
                        baseForward);
            }
            towardTissue =
                NormalizeOrFallback(
                    towardTissue,
                    -Vector3.up);

            float angle =
                Mathf.Clamp(
                    obliqueAngleDeg,
                    0f,
                    55f) *
                Mathf.Deg2Rad;
            viewForward =
                NormalizeOrFallback(
                    baseForward * Mathf.Cos(angle) +
                    towardTissue * Mathf.Sin(angle),
                    baseForward);
            screenRight =
                Vector3.ProjectOnPlane(
                    planRight,
                    viewForward);
            if (screenRight.sqrMagnitude <= 1e-10f)
            {
                screenRight =
                    Vector3.Cross(
                        freeNormal,
                        viewForward);
            }
            screenRight =
                NormalizeOrFallback(
                    screenRight,
                    Vector3.right);
            screenUp =
                NormalizeOrFallback(
                    Vector3.Cross(
                        viewForward,
                        screenRight),
                    freeNormal);
            if (Vector3.Dot(
                    screenUp,
                    freeNormal) < 0f)
            {
                screenRight = -screenRight;
                screenUp = -screenUp;
            }
        }

        private void BuildPlanCameraFrame(out Vector3 planRight, out Vector3 planUp, out Vector3 viewForward)
        {
            Vector3 sideA = GetPolylineCenter(driver != null ? driver.SutureSideAOuterBoundaryWorld : null);
            Vector3 sideB = GetPolylineCenter(driver != null ? driver.SutureSideBOuterBoundaryWorld : null);
            planRight = sideB - sideA;
            if (planRight.sqrMagnitude < 1e-8f)
                planRight = Vector3.right;
            planRight.Normalize();

            Vector3 railDir = GetPolylineDirection(driver != null ? driver.SutureSideAOuterBoundaryWorld : null);
            if (railDir.sqrMagnitude < 1e-8f)
                railDir = GetPolylineDirection(driver != null ? driver.SutureSideBOuterBoundaryWorld : null);
            if (railDir.sqrMagnitude < 1e-8f)
                railDir = Vector3.up;
            planUp = Vector3.ProjectOnPlane(railDir, planRight);
            if (planUp.sqrMagnitude < 1e-8f)
                planUp = Vector3.ProjectOnPlane(Vector3.up, planRight);
            planUp = NormalizeOrFallback(planUp, Vector3.up);
            if (Vector3.Dot(planUp, Vector3.up) < 0f)
                planUp = -planUp;

            viewForward = Vector3.Cross(planRight, planUp);
            viewForward = NormalizeOrFallback(viewForward, Vector3.forward);
            if (Vector3.Dot(viewForward, Vector3.forward) < 0f)
            {
                viewForward = -viewForward;
                planRight = -planRight;
            }
            planRight = NormalizeOrFallback(Vector3.Cross(planUp, viewForward), planRight);
        }

        private float ComputePlanCameraSize(Bounds bounds, Vector3 center, Vector3 planRight, Vector3 planUp)
        {
            float maxUp = 0.02f;
            float maxRight = 0.02f;
            for (int xi = -1; xi <= 1; xi += 2)
            {
                for (int yi = -1; yi <= 1; yi += 2)
                {
                    for (int zi = -1; zi <= 1; zi += 2)
                    {
                        Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(xi, yi, zi));
                        Vector3 d = corner - center;
                        maxUp = Mathf.Max(maxUp, Mathf.Abs(Vector3.Dot(d, planUp)));
                        maxRight = Mathf.Max(maxRight, Mathf.Abs(Vector3.Dot(d, planRight)));
                    }
                }
            }

            float aspect = mainCamera != null && mainCamera.aspect > 1e-4f ? mainCamera.aspect : 1.777f;
            float verticalSize = Mathf.Max(maxUp, maxRight / aspect);
            return Mathf.Max(0.025f, verticalSize * Mathf.Max(1f, planCameraPadding));
        }

        private Vector3 GetPlanCenter()
        {
            Bounds b = GetPlanBounds();
            return b.center;
        }

        private Bounds GetPlanBounds()
        {
            bool hasPoint = false;
            Bounds bounds = new Bounds(transform.position, Vector3.one * 0.01f);
            AccumulateBounds(driver != null ? driver.SutureSideAOuterBoundaryWorld : null, ref bounds, ref hasPoint);
            AccumulateBounds(driver != null ? driver.SutureSideBOuterBoundaryWorld : null, ref bounds, ref hasPoint);
            if (plan != null && plan.stitchPairs != null)
            {
                for (int i = 0; i < plan.stitchPairs.Count; i++)
                {
                    Encapsulate(plan.stitchPairs[i].sideAPass.entryPointWorld, ref bounds, ref hasPoint);
                    Encapsulate(plan.stitchPairs[i].sideBPass.entryPointWorld, ref bounds, ref hasPoint);
                }
            }
            if (needleRig != null)
                Encapsulate(needleRig.GetSemanticTipPoint(), ref bounds, ref hasPoint);
            return bounds;
        }

        private static void AccumulateBounds(IReadOnlyList<Vector3> points, ref Bounds bounds, ref bool hasPoint)
        {
            if (points == null)
                return;
            for (int i = 0; i < points.Count; i++)
                Encapsulate(points[i], ref bounds, ref hasPoint);
        }

        private static void Encapsulate(Vector3 point, ref Bounds bounds, ref bool hasPoint)
        {
            if (!hasPoint)
            {
                bounds = new Bounds(point, Vector3.one * 0.01f);
                hasPoint = true;
                return;
            }
            bounds.Encapsulate(point);
        }

        private static Vector3 GetPolylineCenter(IReadOnlyList<Vector3> points)
        {
            if (!HasPolyline(points))
                return Vector3.zero;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
                sum += points[i];
            return sum / Mathf.Max(1, points.Count);
        }

        private static Vector3 GetPolylineDirection(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 2)
                return Vector3.zero;

            Vector3 dir = points[points.Count - 1] - points[0];
            if (dir.sqrMagnitude > 1e-8f)
                return dir.normalized;

            for (int i = 1; i < points.Count; i++)
            {
                dir = points[i] - points[i - 1];
                if (dir.sqrMagnitude > 1e-8f)
                    return dir.normalized;
            }

            return Vector3.zero;
        }

        private void SetState(AutoSutureState next)
        {
            if (_state != next)
                ResetSafeTravelRoute();
            _state = next;
            _stateElapsed = 0f;
            Debug.Log($"[AutoSuture] pair={_currentPairIndex} pass={_currentPassInPair} state={next}");
        }

        private void Error(string reason)
        {
            _errorReason = reason;
            PauseAutoSuture();
            _state = AutoSutureState.ErrorPaused;
            Debug.LogWarning($"[AutoSuture][Error] pair={_currentPairIndex} pass={_currentPassInPair} state=ErrorPaused reason={reason}");
        }

        private void ClearRuntimeCompletion()
        {
            _pullLineStartTime = 0f;
            _pullLineDeadlineTime = 0f;
            _lastPullLineRestDistance = float.PositiveInfinity;
            _lastPullLineProgressTime = 0f;

            if (plan == null || plan.stitchPairs == null)
                return;
            for (int i = 0; i < plan.stitchPairs.Count; i++)
            {
                AutoSutureStitchPair pair = plan.stitchPairs[i];
                if (pair == null)
                    continue;
                pair.pullLineRequested = false;
                pair.pullLineAdvanceReleased = false;
                ClearPass(pair.sideAPass);
                ClearPass(pair.sideBPass);
            }
        }

        private static void ClearPass(AutoSuturePass pass)
        {
            if (pass == null)
                return;
            pass.complete = false;
            pass.completedPassIndex = 0;
        }

        private static bool HasPolyline(IReadOnlyList<Vector3> points)
        {
            return points != null && points.Count > 0;
        }

        public static Vector3 SamplePolyline(IReadOnlyList<Vector3> points, float t)
        {
            if (points == null || points.Count == 0)
                return Vector3.zero;
            if (points.Count == 1)
                return points[0];

            float total = 0f;
            for (int i = 1; i < points.Count; i++)
                total += Vector3.Distance(points[i - 1], points[i]);
            if (total <= 1e-8f)
                return points[Mathf.Clamp(Mathf.RoundToInt(t * (points.Count - 1)), 0, points.Count - 1)];

            float target = Mathf.Clamp01(t) * total;
            float cursor = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float segment = Vector3.Distance(points[i - 1], points[i]);
                if (cursor + segment >= target)
                {
                    float u = segment > 1e-8f ? (target - cursor) / segment : 0f;
                    return Vector3.Lerp(points[i - 1], points[i], u);
                }
                cursor += segment;
            }
            return points[points.Count - 1];
        }

        private static Vector3 SampleInnerOrFallback(
            IReadOnlyList<Vector3> inner,
            IReadOnlyList<Vector3> outer,
            IReadOnlyList<Vector3> oppositeOuter,
            float t)
        {
            if (HasPolyline(inner))
                return SamplePolyline(inner, t);
            Vector3 outerPoint = SamplePolyline(outer, t);
            Vector3 opposite = SamplePolyline(oppositeOuter, t);
            Vector3 inward = opposite - outerPoint;
            if (inward.sqrMagnitude < 1e-10f)
                inward = Vector3.down * 0.01f;
            return outerPoint + inward.normalized * 0.01f;
        }

        private static Vector3 OffsetAwayFromOpposite(Vector3 point, Vector3 oppositePoint, float offset)
        {
            if (offset <= 1e-7f)
                return point;

            Vector3 away = point - oppositePoint;
            if (away.sqrMagnitude < 1e-10f)
                return point;

            return point + away.normalized * offset;
        }

        private float GetEffectiveBiteOffsetFromCut()
        {
            return Mathf.Max(DefaultAutoSutureBiteOffsetFromCut, biteOffsetFromCut);
        }

        private static string GetSideLabel(ClothSimulationDriverDoubleLayer.SutureVesselSide side)
        {
            if (side == ClothSimulationDriverDoubleLayer.SutureVesselSide.SideA) return "SideA";
            if (side == ClothSimulationDriverDoubleLayer.SutureVesselSide.SideB) return "SideB";
            return "Unknown";
        }

        private static string GetDirectionLabel(ClothSimulationDriverDoubleLayer.SuturePassDirection direction)
        {
            if (direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner) return "OuterToInner";
            if (direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.InnerToOuter) return "InnerToOuter";
            return "Unknown";
        }

        private static string FormatVec(Vector3 value)
        {
            return $"({value.x:F4},{value.y:F4},{value.z:F4})";
        }
    }
}
