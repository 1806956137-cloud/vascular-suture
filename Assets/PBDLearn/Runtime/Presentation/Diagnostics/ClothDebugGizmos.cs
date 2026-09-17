using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace PBDLearn
{
    [DisallowMultipleComponent]
    public class ClothDebugGizmos : MonoBehaviour
    {
        public enum IncisionTriangleStageFilter
        {
            All = 0,
            CorridorFamily = 1,
            MiddleBandOnly = 2,
            StripInteriorOnly = 3,
            RemovedOnly = 4,
            TargetTraceOnly = 5,
            FocusOnly = 6,
        }

        public enum PrecommitFailedInterfaceEdgeFilter
        {
            All = 0,
            SxOnly = 1,
            OsOnly = 2,
            LbRbOnly = 3,
            LsRsOnly = 4,
            TipOnly = 5,
        }

        [Header("Ref (optional)")]
        [Tooltip("单层 Driver（可为空，会自动在场景里找）。")]
        public ClothSimulationDriver simulation;

        [Tooltip("双层 Driver（可为空，会自动在场景里找）。")]
        public ClothSimulationDriverDoubleLayer simulationDouble;

        [Header("Jobs Sync (IMPORTANT)")]
        [Tooltip("默认不建议在 Gizmos 里 Complete jobs（避免和主循环抢）。\n如果你确认主循环每帧都已 Complete，才打开它。")]
        public bool forceCompleteJobsInGizmos = false;

        [Header("Master Switch")]
        public bool draw = true;
        [Tooltip("只显示可读的语义焦点：血管表面由正常 Renderer 保留；Gizmos 仅补充切口主边界、当前针孔、缝线和当前选择。关闭后恢复完整调试绘制。")]
        public bool focus = true;

        [Header("Play Mode Performance")]
        [Tooltip("播放时最多抽样绘制多少条普通距离约束。高应变边和虚拟侧壁边始终完整绘制；该预算固定生效，不再作为独立模式。")]
        [Min(0)] public int maxNormalDistanceEdgesDuringPlay = 600;
        [Tooltip("播放时最多绘制多少条双层厚度连接；统计仍遍历全部连接。")]
        [Min(0)] public int maxCrossLayerLinksDuringPlay = 256;
        [Tooltip("每条预提交语义链在播放时最多显示的文字标签数；首尾标签始终保留。")]
        [Min(2)] public int maxPrecommitLabelsPerChainDuringPlay = 12;
        [Tooltip("每条预提交语义链在播放时最多显示的点标记数；线条本身始终完整绘制。")]
        [Min(2)] public int maxPrecommitPointsPerChainDuringPlay = 64;

        [Header("Particles")]
        public bool drawParticles = true;
        [Range(0.001f, 0.1f)] public float particleRadius = 0.02f;
        public Color normalParticleColor = Color.green;
        public Color pinParticleColor = Color.black;
        public Color collisionParticleColor = Color.blue;
        public bool preferCollisionOverPin = true;

        [Header("Distance Edges")]
        public bool drawDistanceEdges = true;
        [Range(1.0f, 3.0f)] public float edgeHighStrainThreshold = 1.8f;
        [Range(1.0f, 2.0f)] public float edgeMidStrainThreshold = 1.1f;
        public Color edgeNormalColor = Color.white;
        public Color edgeMidStrainColor = Color.yellow;
        public Color edgeHighStrainColor = Color.red;

        [Header("Cross-layer Thickness Links")]
        public bool drawCrossLayerLinks = false;
        [Tooltip("每隔多少帧打印一次 thickness link 统计（0=不打印）。")]
        public int thicknessLogEveryNFrames = 10;
        public float thicknessRestEpsilon = 1e-6f;
        public Color thicknessCompressColor = new Color(0.2f, 0.6f, 1f, 1f);
        public Color thicknessStretchColor = new Color(1f, 0.4f, 0.2f, 1f);
        public Color thicknessNeutralColor = Color.gray;

        [Header("BVH Common")]
        public bool onlyDrawLeafNodes = false;
        public bool drawLeafMarker = true;
        public float leafMarkerSize = 0.008f;

        [Range(0, 64)] public int revealDepth = 12;
        public bool autoReveal = false;
        public float autoRevealInterval = 0.08f;
        public int autoRevealMaxDepth = 32;

        [Header("Query Selection")]
        public bool useFirstColliderOnly = true;
        [Tooltip("对于当前模式下生成的 query 列表，选择第几个 query 来画。")]
        public int firstColliderIndex = 0;
        [Tooltip("给 query AABB 额外外扩的量；纯调试参数。")]
        public float queryExtra = 0.0f;

        [Header("Particle BVH")]
        public bool drawParticleBVH = true;
        public bool drawParticleQueryAABB = true;
        public bool drawParticleHitNodes = true;
        [Tooltip("画出 particle BVH 粗筛出来的候选质点。")]
        public bool drawParticleCandidates = false;
        [Tooltip("优先画 ClothSimulator 当前帧真正收集到的候选质点；关闭则改为按当前 query 现场遍历 BVH 生成。")]
        public bool useRuntimeParticleCandidates = true;
        public Color particleQueryColor = Color.green;
        public Color particleCandidatesColor = Color.cyan;
        [Range(0.001f, 0.1f)] public float particleCandidatesRadius = 0.02f;
        public Color particleNodeNearColor = new Color(1f, 1f, 0f, 1f);
        public Color particleNodeFarColor = new Color(1f, 0.35f, 0f, 1f);

        [Header("Triangle BVH")]
        public bool drawTriangleBVH = true;
        public bool drawTriangleQueryAABB = true;
        public bool drawTriangleHitNodes = true;
        [Tooltip("画出 triangle BVH 粗筛出来的候选三角面。")]
        public bool drawTriangleCandidates = true;
        [Tooltip("优先画 ClothSimulator 当前帧真正收集到的候选三角面；关闭则改为按当前 query 现场遍历 BVH 生成。")]
        public bool useRuntimeTriangleCandidates = true;
        [Tooltip("候选三角面是否画边。")]
        public bool drawTriangleCandidateWire = true;
        [Tooltip("候选三角面是否画中心点。")]
        public bool drawTriangleCandidateCenters = false;
        [Range(0.001f, 0.05f)] public float triangleCandidateCenterRadius = 0.01f;
        public Color triangleQueryColor = new Color(0.2f, 1f, 1f, 1f);
        public Color triangleCandidatesColor = new Color(1f, 0.2f, 1f, 1f);
        public Color triangleNodeNearColor = new Color(0.4f, 1f, 1f, 1f);
        public Color triangleNodeFarColor = new Color(0.9f, 0.3f, 1f, 1f);

        [Header("Needle Puncture Portal")]
        public bool drawNeedlePuncturePortals = true;
        [Tooltip("画出已经建立过的 portal 历史记录（用于看第几次穿刺/缝合孔位）。")]
        public bool drawNeedlePortalHistory = true;
        [Tooltip("历史 portal 默认不重复画当前 active 的 portal。")]
        public bool includeActivePortalsInHistory = false;
        public bool drawPortalWire = true;
        public bool drawPortalNormal = true;
        public bool drawPortalTriangle = true;
        public bool drawPortalLabel = true;
        [Tooltip("隐藏已稳定缝针的全部 portal、历史和缝线文字；当前针及失败针诊断仍显示。")]
        public bool compactCompletedSutureLabels = true;
        [Range(0.0005f, 0.03f)] public float portalPointRadius = 0.004f;
        public Color portalCenterColor = new Color(1f, 0.35f, 0.15f, 1f);
        public Color portalWireColor = new Color(1f, 0.65f, 0.2f, 1f);
        public Color portalNormalColor = new Color(1f, 0.9f, 0.2f, 1f);
        public Color portalTriangleColor = new Color(1f, 0.25f, 0.15f, 1f);
        public bool colorPortalByLayer = true;
        public Color portalOuterColor = new Color(1f, 0.4f, 0.15f, 1f);
        public Color portalInnerColor = new Color(0.2f, 0.85f, 1f, 1f);
        [Tooltip("Draw weak suture line segments created by Pull Line.")]
        public bool drawSutureLines = true;
        public Color sutureLineColor = new Color(1f, 0.95f, 0.18f, 1f);
        [Range(0.0005f, 0.02f)] public float sutureLinePointRadius = 0.003f;

        [Header("Focus Local Influence")]
        [Tooltip("Focus 下用真实 Hook / Portal / Suture patch 绘制局部受影响表面。")]
        public bool drawFocusInfluenceSurface = true;
        [Tooltip("在 Focus 热区内绘制完整三角网格；关闭后只保留热区外轮廓。")]
        public bool drawFocusInfluenceInteriorMesh = true;
        [Range(0.05f, 1f)] public float focusInfluenceInteriorMeshAlpha = 0.68f;
        [Tooltip("在 Scene 左上角显示当前自动缝合状态卡。")]
        public bool drawFocusAutoSutureStatusCard = true;
        [Range(0.02f, 0.65f)] public float focusInfluenceMinAlpha = 0.08f;
        [Range(0.05f, 0.85f)] public float focusInfluenceMaxAlpha = 0.32f;
        [Min(0.0001f)] public float focusInfluenceDirectionLength = 0.012f;
        public Color focusInfluenceMidColor = new Color(1f, 0.86f, 0.08f, 1f);
        public Color focusInfluenceHighColor = new Color(1f, 0.12f, 0.04f, 1f);

        [Header("Incision Debug")]
        public bool drawIncisionDebug = true;
        public bool drawIncisionTargetStroke = true;
        public bool drawIncisionMasterChain = true;
        public bool drawIncisionCenterCurve = true;
        public bool drawIncisionStations = true;
        public bool drawIncisionBoundaries = true;
        public bool drawIncisionPatchRegion = false;
        public bool drawIncisionExcisionRegion = true;
        public bool drawIncisionLayerSummaries = true;

        [Tooltip("画出运行时真实切口 ribbon 的左右边界。")]
        [HideInInspector] public bool drawIncisionSlitBoundary = true;
        [Tooltip("是否同时画出内层边界；关闭时只看外层两条边界。")]
        public bool drawIncisionInnerBoundary = true;
        [Tooltip("是否画每个 frame 的横向 short bar（left-right）。")]
        [HideInInspector] public bool drawIncisionSlitCrossbars = false;
        [Tooltip("是否画 centerline/center 点。")]
        [HideInInspector] public bool drawIncisionSlitCenters = false;

        [HideInInspector] public bool drawIncisionCenterline = false;
        [HideInInspector] public bool drawIncisionDeleteSummaries = false;
        [HideInInspector] public bool drawIncisionLipPairs = false;
        [HideInInspector] public bool drawIncisionPairStages = false;
        [HideInInspector] public bool drawIncisionPatchTriangles = false;
        [HideInInspector] public bool drawIncisionRemovedTriangles = false;
        [HideInInspector] public bool drawIncisionInsertedVertices = false;
        [HideInInspector] public bool drawIncisionRenderSamples = false;
        public bool drawIncisionLabels = false;
        [Range(0.0003f, 0.02f)] public float incisionPointRadius = 0.0022f;
        public Color incisionTargetStrokeColor = new Color(1f, 0.2f, 0.2f, 1f);
        public Color incisionMasterChainColor = new Color(0.03f, 0.03f, 0.03f, 1f);
        public Color incisionStationOuterColor = new Color(1f, 0.72f, 0.12f, 1f);
        public Color incisionStationInnerColor = new Color(0.15f, 0.95f, 1f, 1f);
        public Color incisionOuterLeftBoundaryColor = new Color(0.12f, 0.82f, 1f, 1f);
        public Color incisionOuterRightBoundaryColor = new Color(0.0f, 0.45f, 1f, 1f);
        public Color incisionInnerLeftBoundaryColor = new Color(0.15f, 1f, 0.55f, 1f);
        public Color incisionInnerRightBoundaryColor = new Color(0.0f, 0.72f, 0.35f, 1f);
        public Color incisionSlitCrossbarColor = new Color(0.4f, 0.9f, 1f, 0.45f);
        public Color incisionCenterlineColor = new Color(1f, 0.6f, 0.15f, 1f);
        public Color incisionDeleteSummaryOuterColor = new Color(1f, 0.45f, 0.15f, 1f);
        public Color incisionDeleteSummaryInnerColor = new Color(0.2f, 0.85f, 1f, 1f);
        public Color incisionOuterLipColor = new Color(1f, 0.2f, 0.2f, 1f);
        public Color incisionInnerLipColor = new Color(0.2f, 0.85f, 1f, 1f);
        public Color incisionPatchCrossedColor = new Color(1f, 0.2f, 0.2f, 0.75f);
        public Color incisionPatchRefinementColor = new Color(0.2f, 1f, 1f, 0.65f);
        public Color incisionPatchCutColor = new Color(1f, 0.92f, 0.2f, 0.85f);
        public Color incisionRemovedDegenerateColor = new Color(1f, 0.2f, 0.95f, 0.85f);
        public Color incisionRemovedBridgeColor = new Color(0.95f, 0.15f, 0.15f, 0.95f);
        public Color incisionStrictCandidateColor = new Color(0.15f, 0.55f, 1f, 0.95f);
        public Color incisionBridgeCandidateColor = new Color(0.95f, 0.25f, 0.95f, 0.95f);
        public Color incisionCoreCandidateColor = new Color(0.15f, 1f, 0.35f, 0.95f);
        public Color incisionCorridorCandidateColor = new Color(1f, 0.9f, 0.15f, 0.92f);
        public Color incisionMiddleBandCandidateColor = new Color(1f, 0.55f, 0.1f, 0.95f);
        public Color incisionStripInteriorCandidateColor = new Color(0.15f, 1f, 0.75f, 0.98f);
        public Color incisionRemovedStripInteriorColor = new Color(0.15f, 0.95f, 0.45f, 0.98f);

        [Header("Incision Left/Right Boundary Debug")]
        [Tooltip("即使当前三角过滤模式不是 All，也始终把 Left / Right 边界线画出来。这样在 FocusOnly / StripInteriorOnly 下也不会看不到蓝线。")]
        [HideInInspector] public bool alwaysDrawIncisionSlitBoundaryInFilteredModes = true;
        [Tooltip("把 Left / Right 边界内部的三角统一按黄色高亮。")]
        [HideInInspector] public bool highlightLeftRightBoundaryInteriorAsYellow = true;
        [Tooltip("在 Scene 里给 Left / Right 边界内部三角额外画一层半透明黄面。")]
        [HideInInspector] public bool fillLeftRightBoundaryInteriorTriangles = true;
        public Color incisionLeftRightBoundaryInteriorColor = new Color(1f, 0.92f, 0.12f, 0.98f);
        public Color incisionLeftRightBoundaryInteriorFillColor = new Color(1f, 0.92f, 0.12f, 0.18f);
        [Min(1f)] public float incisionBoundaryAAWidth = 3f;
        [Min(0f)] public float incisionBoundaryDepthBias = 0.00075f;
        [HideInInspector] public bool drawIncisionBoundaryLineLabels = true;
        public Color incisionTargetTraceFocusColor = new Color(1f, 0.1f, 0.1f, 1f);
        public Color incisionTargetTraceNeighborColor = new Color(0.95f, 0.95f, 0.15f, 0.98f);
        public Color incisionInsertedSubdivisionColor = new Color(0.2f, 1f, 0.2f, 0.9f);
        public Color incisionInsertedRefineColor = new Color(0.15f, 0.75f, 1f, 0.9f);
        public Color incisionInsertedSplitColor = new Color(1f, 0.8f, 0.2f, 0.9f);
        public Color incisionPairRawColor = new Color(0.75f, 0.75f, 0.75f, 0.6f);
        public Color incisionPairCompactedColor = new Color(1f, 0.55f, 0.1f, 0.95f);
        public Color incisionPairRejectedColor = new Color(1f, 0.15f, 0.15f, 0.9f);
        public Color incisionMarkerInputColor = new Color(0.35f, 1f, 0.35f, 0.75f);
        public Color incisionMarkerPrunedColor = new Color(0.1f, 0.85f, 0.1f, 0.95f);
        public Color incisionSidewallOuterInputColor = new Color(1f, 0.55f, 0.15f, 0.65f);
        public Color incisionSidewallOuterPrunedColor = new Color(1f, 0.78f, 0.15f, 0.95f);
        public Color incisionSidewallInnerInputColor = new Color(0.25f, 0.7f, 1f, 0.65f);
        public Color incisionSidewallInnerPrunedColor = new Color(0.1f, 0.95f, 1f, 0.95f);

        [Header("Virtual Sidewall Constraints")]
        public bool drawVirtualSidewallConstraintMarkers = true;
        public bool drawVirtualSidewallConstraintLabels = false;
        [Range(0.0003f, 0.02f)] public float virtualSidewallParticleRadius = 0.0032f;
        public Color virtualSidewallParticleColor = new Color(0.05f, 0.95f, 1f, 0.95f);
        public Color virtualSidewallDistanceConstraintColor = new Color(0.05f, 0.95f, 1f, 0.95f);
        public Color virtualSidewallShearConstraintColor = new Color(1f, 0.85f, 0.12f, 0.95f);

        [Header("Incision PreCommit Geometry")]
        public bool drawIncisionPrecommitGeometry = true;
        public bool drawPrecommitMasterChain = true;
        public bool drawPrecommitCenterCurve = true;
        public bool drawPrecommitVisibleBoundaryChains = true;
        public bool drawPrecommitSupportBoundaryChains = true;
        public bool drawPrecommitFailedInterfaceEdges = true;
        public PrecommitFailedInterfaceEdgeFilter precommitFailedInterfaceEdgeFilter = PrecommitFailedInterfaceEdgeFilter.All;
        public bool drawPrecommitPoints = true;
        public bool drawPrecommitLabels = true;
        [HideInInspector] public bool drawPrecommitCanonicalChains = true;
        [Range(0.0003f, 0.02f)] public float precommitPointRadius = 0.0018f;
        [Range(0f, 0.02f)] public float precommitLiftOffset = 0.0012f;
        [Range(1f, 18f)] public float precommitMasterChainWidth = 8f;
        [Range(1f, 18f)] public float precommitCenterCurveWidth = 7f;
        [Range(1f, 18f)] public float precommitVisibleBoundaryWidth = 10f;
        [Range(1f, 18f)] public float precommitSupportBoundaryWidth = 4f;
        [Range(1f, 18f)] public float precommitFailedEdgeWidth = 4f;
        public Color precommitMasterChainColor = Color.black;
        public Color precommitRowCenterColor = Color.yellow;
        public Color precommitCenterCurveColor = Color.yellow;
        public Color precommitVisibleBoundaryColor = new Color(0.75f, 0.2f, 1f, 1f);
        public Color precommitSupportBoundaryColor = new Color(0.1f, 0.45f, 1f, 1f);
        public Color precommitFailedInterfaceEdgeColor = new Color(1f, 0.35f, 0.05f, 1f);
        [HideInInspector] public Color precommitOsChainColor = Color.cyan;
        [HideInInspector] public Color precommitSxChainColor = new Color(1f, 0.5f, 0f, 1f);

        [Header("Incision Runtime Trace / Simplify")]
        [Tooltip("是否把这份 Debug 组件里的切口追踪/抽稀参数同步回 ClothSimulationDriverDoubleLayer。这样 VesselSim Inspector 可以只保留 Sim 与切口面板。")]
        [HideInInspector] public bool syncIncisionRuntimeDebugToDriver = true;
        [HideInInspector] public bool enableIncisionTargetTrace = true;
        [HideInInspector, Range(1, 12)] public int incisionTargetTraceCount = 1;
        [HideInInspector, Min(0f)] public float incisionTargetTraceNeighborRadius = 0.0045f;
        [HideInInspector] public bool incisionTargetTraceLogToConsole = true;
        [HideInInspector, Min(0f)] public float incisionPairMinSpacing = 0.0025f;
        [HideInInspector, Min(0f)] public float incisionDebugMinSpacing = 0f;
        [HideInInspector, Range(1, 8)] public int incisionDebugDrawStride = 1;

        [Header("Incision Focus Audit")]
        [Tooltip("切口三角调试过滤模式。FocusOnly 最适合把满屏候选压成单个失败案例。")]
        [HideInInspector] public IncisionTriangleStageFilter incisionTriangleStageFilter = IncisionTriangleStageFilter.FocusOnly;
        [Tooltip("同步到 Driver 时，是否强制只保留 1 个 target trace focus。")]
        [HideInInspector] public bool forceSingleTargetTraceFromDriver = true;
        [HideInInspector, Min(0)] public int selectedTargetTraceFocusIndex = 0;
        [Tooltip("是否把当前 focus 三角的判定账单直接贴在 Scene 里。")]
        [HideInInspector] public bool showSelectedTargetTraceLedger = true;
        [Tooltip("只把 TargetTrace / Focus 审计三角压成“当前 focus + 它附近的邻居”。不再影响 StripInterior / RemovedOnly / CorridorFamily 这类阶段过滤。")]
        [HideInInspector] public bool drawOnlySelectedFocusNeighborhood = true;
        [Tooltip("当当前阶段过滤不是 All 时，自动给当前可见三角打 tri# 标签，方便你直接定位同一类三角。")]
        [HideInInspector] public bool alwaysLabelStageFilteredTriangles = true;
        [HideInInspector] public bool includeVertexIdsInStageFilteredLabels = true;
        [HideInInspector, Min(0f)] public float selectedFocusLedgerYOffset = 0.004f;

        [Header("Incision Triangle Picking")]
        [Tooltip("在 Scene 里按住 Shift + 左键，直接点当前可见的切口调试三角，锁定它的 triIndex / 顶点索引 / failHint / removedCandidate。")]
        public bool enableIncisionTrianglePicking = true;
        [Tooltip("只允许点选 Left / Right 边界内部相关三角。关闭后，当前阶段过滤下可见的切口调试三角都可点。")]
        public bool pickOnlyLeftRightBoundaryInteriorTriangles = true;
        [Tooltip("点选后，把选中的三角用更醒目的颜色单独再描一遍。")]
        public bool highlightPickedIncisionTriangle = true;
        public Color pickedIncisionTriangleColor = new Color(1f, 0.1f, 0.1f, 1f);
        public Color pickedIncisionTriangleFillColor = new Color(1f, 0.1f, 0.1f, 0.12f);
        [Tooltip("把当前点中的三角账单直接贴在 Scene 里。")]
        public bool showPickedIncisionTriangleLedger = true;
        [Tooltip("每次 Shift + 左键点中一个 runtime mesh 三角时，把同一份账单打印一次到 Console。")]
        public bool logPickedIncisionTriangleToConsole = true;
        [Min(0f)] public float pickedIncisionTriangleLedgerYOffset = 0.006f;
        [SerializeField, HideInInspector] private int pickedIncisionTriangleLayer = -1;
        [SerializeField, HideInInspector] private int pickedIncisionTriangleIndex = -1;
        [SerializeField, HideInInspector] private int pickedIncisionTriangleIa = -1;
        [SerializeField, HideInInspector] private int pickedIncisionTriangleIb = -1;
        [SerializeField, HideInInspector] private int pickedIncisionTriangleIc = -1;
        [SerializeField, HideInInspector] private int pickedIncisionTriangleKind = -1;
        [SerializeField, HideInInspector] private Vector3 pickedIncisionTriangleCenter = Vector3.zero;
        private readonly HashSet<int> _focusConstraintVertexIds = new HashSet<int>();
        private readonly List<int> _focusLocalDistanceConstraintIndices = new List<int>(1024);
        private ClothSimulator _focusConstraintCacheSimulator;
        private ClothSimulationDriverDoubleLayer _focusConstraintCacheDriver;
        private int _focusConstraintCacheDistanceCount = -1;
        private int _focusConstraintCacheOuterCount = -1;
        private int _focusConstraintCacheInnerCount = -1;
        private int _focusConstraintCacheSutureRegionSignature;
        private int _focusConstraintCachePickedLayer = int.MinValue;
        private int _focusConstraintCachePickedTriangle = int.MinValue;
        private readonly Dictionary<int, float> _focusAffectedVertexWeights = new Dictionary<int, float>(64);
        private readonly Dictionary<int, List<int>> _focusIncidentTriangles = new Dictionary<int, List<int>>(1024);
        private readonly HashSet<int> _focusAffectedTriangles = new HashSet<int>();
        private readonly Dictionary<long, int> _focusBoundaryEdgeCounts = new Dictionary<long, int>(128);
        private ClothSimulator _focusTopologyCacheSimulator;
        private int _focusTopologyCacheIndexCount = -1;
        private int _focusTopologyCacheVertexCount = -1;

        private float _autoTimerParticle;
        private float _autoTimerTriangle;
        private AABB _lastParticleQuery;
        private AABB _lastTriangleQuery;
        private bool _hasLastParticleQuery;
        private bool _hasLastTriangleQuery;

        private void Awake()
        {
            AutoFindDrivers();
            SyncIncisionRuntimeDebugToDriver();
        }

        private void OnEnable()
        {
            AutoFindDrivers();
            SyncIncisionRuntimeDebugToDriver();
        }

        private void OnValidate()
        {
            AutoFindDrivers();
            SyncIncisionRuntimeDebugToDriver();
        }

        private void AutoFindDrivers()
        {
            if (simulationDouble == null)
                simulationDouble = FindObjectOfType<ClothSimulationDriverDoubleLayer>();

            if (simulation == null)
                simulation = FindObjectOfType<ClothSimulationDriver>();
        }

        private void OnDrawGizmos()
        {
            if (!draw) return;

            if (simulationDouble == null && simulation == null)
                AutoFindDrivers();

            SyncIncisionRuntimeDebugToDriver();

            if (focus)
            {
                DrawFocusView();
                return;
            }

            if (drawIncisionDebug && simulationDouble != null)
                DrawIncisionDebug(simulationDouble);

            if (!Application.isPlaying) return;

            if (simulationDouble == null && simulation == null)
                return;

            var sim = simulationDouble != null ? simulationDouble.Simulator : simulation.Simulator;
            if (sim == null) return;

            if (forceCompleteJobsInGizmos)
            {
                sim.ComputeAsyncJobs();
            }

            if (drawParticles) DrawParticles(sim);
            if (drawDistanceEdges) DrawDistanceEdges(sim);
            if (drawVirtualSidewallConstraintMarkers) DrawVirtualSidewallConstraintMarkers(sim);
            if (drawCrossLayerLinks) DrawCrossLayerLinks(sim);

            if (drawParticleBVH)
                DrawParticleBVH(sim);

            if (drawTriangleBVH)
                DrawTriangleBVH(sim);

            if (drawNeedlePuncturePortals)
                DrawNeedlePuncturePortals(sim);
            if (drawNeedlePortalHistory)
                DrawNeedlePortalHistory(sim);
            if (drawSutureLines)
                DrawSutureLines(sim);
        }

        private void DrawFocusView()
        {
            _focusConstraintVertexIds.Clear();
            if (simulationDouble != null)
            {
                DrawFocusIncisionBoundaries(simulationDouble);
                DrawFocusPickedTriangleNeighborhood(simulationDouble);
            }

            if (!Application.isPlaying)
                return;

            var sim = simulationDouble != null
                ? simulationDouble.Simulator
                : simulation != null
                    ? simulation.Simulator
                    : null;
            if (sim == null)
                return;

            DrawFocusLocalInfluence(sim, simulationDouble);
            DrawFocusNeedleInfluenceIndicators(sim);
            DrawSutureLines(sim, false, false);
        }

        private void DrawFocusLocalInfluence(
            ClothSimulator sim,
            ClothSimulationDriverDoubleLayer driver)
        {
            if (!drawFocusInfluenceSurface || sim == null || driver == null)
                return;

            var positions = sim.predictPositions;
            var indices = sim.indices;
            if (!positions.IsCreated || !indices.IsCreated ||
                positions.Length == 0 || indices.Length < 3)
            {
                return;
            }

            _focusAffectedVertexWeights.Clear();
            CollectFocusNeedleAffectedVertices(sim, positions.Length, _focusAffectedVertexWeights);
            CollectFocusSutureAffectedVertexWeights(sim, positions.Length, _focusAffectedVertexWeights);
            if (_focusAffectedVertexWeights.Count == 0)
                return;

            EnsureFocusTriangleAdjacency(sim, positions.Length, indices);
            _focusAffectedTriangles.Clear();
            foreach (var pair in _focusAffectedVertexWeights)
            {
                if (!_focusIncidentTriangles.TryGetValue(pair.Key, out List<int> incident))
                    continue;
                for (int i = 0; i < incident.Count; i++)
                    _focusAffectedTriangles.Add(incident[i]);
            }

            _focusBoundaryEdgeCounts.Clear();
            foreach (int triangleIndex in _focusAffectedTriangles)
            {
                int baseIndex = triangleIndex * 3;
                if (baseIndex < 0 || baseIndex + 2 >= indices.Length)
                    continue;

                int a = indices[baseIndex];
                int b = indices[baseIndex + 1];
                int c = indices[baseIndex + 2];
                if (!_focusAffectedVertexWeights.TryGetValue(a, out float wa) ||
                    !_focusAffectedVertexWeights.TryGetValue(b, out float wb) ||
                    !_focusAffectedVertexWeights.TryGetValue(c, out float wc))
                {
                    continue;
                }

                float weight = Mathf.Clamp01((wa + wb + wc) / 3f);
                Color color = GetFocusInfluenceColor(driver, a, weight);
                Vector3 pa = ToVector3(positions[a]);
                Vector3 pb = ToVector3(positions[b]);
                Vector3 pc = ToVector3(positions[c]);
#if UNITY_EDITOR
                UnityEditor.Handles.color = color;
                UnityEditor.Handles.DrawAAConvexPolygon(pa, pb, pc);
#endif
                AddFocusBoundaryEdge(a, b);
                AddFocusBoundaryEdge(b, c);
                AddFocusBoundaryEdge(c, a);
            }

            foreach (var pair in _focusBoundaryEdgeCounts)
            {
                bool boundaryEdge = pair.Value == 1;
                if (!boundaryEdge && !drawFocusInfluenceInteriorMesh)
                    continue;
                int a = (int)(pair.Key >> 32);
                int b = (int)(pair.Key & 0xffffffffL);
                if ((uint)a >= (uint)positions.Length || (uint)b >= (uint)positions.Length)
                    continue;
                float wa = _focusAffectedVertexWeights.TryGetValue(a, out float valueA) ? valueA : 0f;
                float wb = _focusAffectedVertexWeights.TryGetValue(b, out float valueB) ? valueB : 0f;
                Color edgeColor = GetFocusInfluenceColor(driver, a, Mathf.Max(wa, wb));
                edgeColor.a = boundaryEdge
                    ? 0.95f
                    : Mathf.Clamp01(focusInfluenceInteriorMeshAlpha);
                Gizmos.color = edgeColor;
                Gizmos.DrawLine(ToVector3(positions[a]), ToVector3(positions[b]));
            }
        }

        private void EnsureFocusTriangleAdjacency(
            ClothSimulator sim,
            int vertexCount,
            Unity.Collections.NativeArray<int> indices)
        {
            if (_focusTopologyCacheSimulator == sim &&
                _focusTopologyCacheIndexCount == indices.Length &&
                _focusTopologyCacheVertexCount == vertexCount)
            {
                return;
            }

            _focusIncidentTriangles.Clear();
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int triangleIndex = i / 3;
                AddFocusIncidentTriangle(indices[i], triangleIndex, vertexCount);
                AddFocusIncidentTriangle(indices[i + 1], triangleIndex, vertexCount);
                AddFocusIncidentTriangle(indices[i + 2], triangleIndex, vertexCount);
            }
            _focusTopologyCacheSimulator = sim;
            _focusTopologyCacheIndexCount = indices.Length;
            _focusTopologyCacheVertexCount = vertexCount;
        }

        private void AddFocusIncidentTriangle(int vertex, int triangleIndex, int vertexCount)
        {
            if ((uint)vertex >= (uint)vertexCount)
                return;
            if (!_focusIncidentTriangles.TryGetValue(vertex, out List<int> incident))
            {
                incident = new List<int>(8);
                _focusIncidentTriangles.Add(vertex, incident);
            }
            incident.Add(triangleIndex);
        }

        private void AddFocusBoundaryEdge(int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            long key = ((long)min << 32) | (uint)max;
            _focusBoundaryEdgeCounts.TryGetValue(key, out int count);
            _focusBoundaryEdgeCounts[key] = count + 1;
        }

        private Color GetFocusInfluenceColor(
            ClothSimulationDriverDoubleLayer driver,
            int vertex,
            float weight)
        {
            bool inner = vertex >= driver.InnerStart &&
                         vertex < driver.InnerStart + driver.InnerCount;
            Color layerColor = inner ? portalInnerColor : portalOuterColor;
            float w = Mathf.Clamp01(weight);
            Color color = w < 0.72f
                ? Color.Lerp(layerColor, focusInfluenceMidColor, w / 0.72f)
                : Color.Lerp(focusInfluenceMidColor, focusInfluenceHighColor, (w - 0.72f) / 0.28f);
            color.a = Mathf.Lerp(focusInfluenceMinAlpha, focusInfluenceMaxAlpha, w);
            return color;
        }

        private static void CollectFocusNeedleAffectedVertices(
            ClothSimulator sim,
            int vertexCount,
            Dictionary<int, float> weights)
        {
            for (int i = 0; i < sim.activeNeedleTipHookConstraintCount; i++)
            {
                NeedleTipHookSoftConstraintInfo hook = sim.GetActiveNeedleTipHookConstraint(i);
                if (hook.enabled == 0)
                    continue;
                int count = Mathf.Min(hook.patchCount, NeedleTipHookPatchUtil.MaxPatchVertices);
                for (int p = 0; p < count; p++)
                    AddFocusAffectedWeight(
                        NeedleTipHookPatchUtil.GetPatchVertex(hook, p),
                        NeedleTipHookPatchUtil.GetPatchWeight(hook, p),
                        vertexCount,
                        weights);
            }

            for (int i = 0; i < sim.activeNeedlePortalCount; i++)
            {
                NeedlePortalInfo portal = sim.GetActiveNeedlePortal(i);
                if (portal.enabled == 0)
                    continue;
                int count = Mathf.Min(portal.patchCount, NeedlePortalPatchUtil.MaxPatchVertices);
                for (int p = 0; p < count; p++)
                    AddFocusAffectedWeight(
                        NeedlePortalPatchUtil.GetPatchVertex(portal, p),
                        NeedlePortalPatchUtil.GetPatchWeight(portal, p),
                        vertexCount,
                        weights);
            }
        }

        private static void CollectFocusSutureAffectedVertexWeights(
            ClothSimulator sim,
            int vertexCount,
            Dictionary<int, float> weights)
        {
            for (int i = 0; i < sim.sutureLineDisplayCount; i++)
            {
                SutureLineDisplayInfo line = sim.GetSutureLineDisplayInfo(i);
                if (line.enabled == 0 || line.settled != 0)
                    continue;
                AddFocusSuturePatchWeights(sim, line.patchStartA, line.patchCountA, vertexCount, weights);
                AddFocusSuturePatchWeights(sim, line.patchStartB, line.patchCountB, vertexCount, weights);
                AddFocusSutureContactWeights(sim, line.contactTriStartA, line.contactTriCountA, vertexCount, weights);
                AddFocusSutureContactWeights(sim, line.contactTriStartB, line.contactTriCountB, vertexCount, weights);
            }
        }

        private static void AddFocusSuturePatchWeights(
            ClothSimulator sim,
            int start,
            int count,
            int vertexCount,
            Dictionary<int, float> weights)
        {
            if (start < 0 || count <= 0)
                return;
            for (int i = 0; i < count; i++)
            {
                SutureLinePatchVertexInfo patch = sim.GetSutureLinePatchVertexInfo(start + i);
                AddFocusAffectedWeight(patch.vertexIndex, patch.weight, vertexCount, weights);
            }
        }

        private static void AddFocusSutureContactWeights(
            ClothSimulator sim,
            int start,
            int count,
            int vertexCount,
            Dictionary<int, float> weights)
        {
            if (start < 0 || count <= 0)
                return;
            for (int i = 0; i < count; i++)
            {
                SutureLineContactTriangleInfo tri = sim.GetSutureLineContactTriangleInfo(start + i);
                AddFocusAffectedWeight(tri.v0, 0.45f, vertexCount, weights);
                AddFocusAffectedWeight(tri.v1, 0.45f, vertexCount, weights);
                AddFocusAffectedWeight(tri.v2, 0.45f, vertexCount, weights);
            }
        }

        private static void AddFocusAffectedWeight(
            int vertex,
            float weight,
            int vertexCount,
            Dictionary<int, float> weights)
        {
            if ((uint)vertex >= (uint)vertexCount)
                return;
            float clamped = Mathf.Clamp01(weight);
            if (!weights.TryGetValue(vertex, out float current) || clamped > current)
                weights[vertex] = clamped;
        }

        private void DrawFocusNeedleInfluenceIndicators(ClothSimulator sim)
        {
            for (int i = 0; i < sim.activeNeedleTipHookConstraintCount; i++)
            {
                NeedleTipHookSoftConstraintInfo hook = sim.GetActiveNeedleTipHookConstraint(i);
                if (hook.enabled == 0)
                    continue;
                Color color = hook.wallLayer == (byte)NeedlePortalWallLayer.Inner
                    ? portalInnerColor
                    : portalOuterColor;
                DrawFocusInfluenceRingAndArrow(
                    ToVector3(hook.hookPoint),
                    ToVector3(hook.surfaceNormal),
                    ToVector3(hook.insertDirection),
                    Mathf.Max(portalPointRadius * 1.6f, hook.radius),
                    color);
            }

            for (int i = 0; i < sim.activeNeedlePortalCount; i++)
            {
                NeedlePortalInfo portal = sim.GetActiveNeedlePortal(i);
                if (portal.enabled == 0)
                    continue;
                DrawFocusInfluenceRingAndArrow(
                    ToVector3(portal.currentPoint),
                    ToVector3(portal.entryNormal),
                    ToVector3(portal.insertDirection),
                    Mathf.Max(portalPointRadius * 1.6f, portal.relaxRadius),
                    GetPortalLayerColor(portal, portalCenterColor));
            }
        }

        private void DrawFocusInfluenceRingAndArrow(
            Vector3 center,
            Vector3 normal,
            Vector3 direction,
            float radius,
            Color color)
        {
            normal = normal.sqrMagnitude > 1e-10f ? normal.normalized : Vector3.up;
            direction = direction.sqrMagnitude > 1e-10f ? direction.normalized : -normal;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.92f
                ? Vector3.up
                : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            Gizmos.color = color;
            const int segments = 24;
            Vector3 previous = center + tangent * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = center + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            float length = Mathf.Max(radius * 1.25f, focusInfluenceDirectionLength);
            Vector3 end = center + direction * length;
            Gizmos.DrawLine(center, end);
            Vector3 arrowSide = Vector3.Cross(direction, normal);
            if (arrowSide.sqrMagnitude < 1e-10f)
                arrowSide = tangent;
            arrowSide.Normalize();
            float head = Mathf.Min(length * 0.32f, radius * 0.55f);
            Gizmos.DrawLine(end, end - direction * head + arrowSide * head * 0.55f);
            Gizmos.DrawLine(end, end - direction * head - arrowSide * head * 0.55f);
        }

        private void DrawFocusIncisionBoundaries(
            ClothSimulationDriverDoubleLayer driver)
        {
            if (driver == null)
                return;

            DrawIncisionPolyline(
                driver.IncisionOuterLeftBoundaryWorld,
                incisionOuterLeftBoundaryColor,
                0f,
                false);
            DrawIncisionPolyline(
                driver.IncisionOuterRightBoundaryWorld,
                incisionOuterRightBoundaryColor,
                0f,
                false);

            if (!drawIncisionInnerBoundary)
                return;

            DrawIncisionPolyline(
                driver.IncisionInnerLeftBoundaryWorld,
                WithAlpha(incisionInnerLeftBoundaryColor, 0.72f),
                0f,
                false);
            DrawIncisionPolyline(
                driver.IncisionInnerRightBoundaryWorld,
                WithAlpha(incisionInnerRightBoundaryColor, 0.72f),
                0f,
                false);
        }

        private void DrawFocusPickedTriangleNeighborhood(
            ClothSimulationDriverDoubleLayer driver)
        {
            if (!TryGetPickedIncisionTriangle(driver, out var picked))
                return;

            bool inner = picked.layer ==
                         (int)ClothSimulationDriverDoubleLayer.VesselLayer.Inner;
            MeshFilter filter = inner
                ? driver.innerMeshFilter
                : driver.outerMeshFilter;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int selectedBase = picked.triIndex * 3;
            if (vertices == null || triangles == null ||
                selectedBase < 0 || selectedBase + 2 >= triangles.Length)
            {
                return;
            }

            int selectedA = triangles[selectedBase];
            int selectedB = triangles[selectedBase + 1];
            int selectedC = triangles[selectedBase + 2];
            Transform tf = filter.transform;
            int globalOffset = inner ? driver.InnerStart : driver.OuterStart;
            AddFocusConstraintVertex(globalOffset, selectedA);
            AddFocusConstraintVertex(globalOffset, selectedB);
            AddFocusConstraintVertex(globalOffset, selectedC);

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (t == selectedBase)
                    continue;

                int a = triangles[t];
                int b = triangles[t + 1];
                int c = triangles[t + 2];
                int shared = CountSharedTriangleVertices(
                    selectedA,
                    selectedB,
                    selectedC,
                    a,
                    b,
                    c);
                if (shared < 2)
                    continue;

                AddFocusConstraintVertex(globalOffset, a);
                AddFocusConstraintVertex(globalOffset, b);
                AddFocusConstraintVertex(globalOffset, c);
                Vector3 wa = tf.TransformPoint(vertices[a]);
                Vector3 wb = tf.TransformPoint(vertices[b]);
                Vector3 wc = tf.TransformPoint(vertices[c]);
#if UNITY_EDITOR
                UnityEditor.Handles.color =
                    new Color(0.18f, 0.72f, 1f, 0.055f);
                UnityEditor.Handles.DrawAAConvexPolygon(wa, wb, wc);
#endif
                DrawFocusTriangleOutline(
                    wa,
                    wb,
                    wc,
                    new Color(0.18f, 0.72f, 1f, 0.82f));
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color = pickedIncisionTriangleFillColor;
            UnityEditor.Handles.DrawAAConvexPolygon(
                picked.a,
                picked.b,
                picked.c);
#endif
            DrawFocusTriangleOutline(
                picked.a,
                picked.b,
                picked.c,
                pickedIncisionTriangleColor);
            Gizmos.color = pickedIncisionTriangleColor;
            Gizmos.DrawSphere(
                picked.center,
                Mathf.Max(0.0002f, incisionPointRadius * 0.8f));
        }

        private void AddFocusConstraintVertex(int globalOffset, int localIndex)
        {
            if (localIndex >= 0)
                _focusConstraintVertexIds.Add(globalOffset + localIndex);
        }

        private static int CountSharedTriangleVertices(
            int sa,
            int sb,
            int sc,
            int a,
            int b,
            int c)
        {
            int shared = 0;
            if (a == sa || a == sb || a == sc) shared++;
            if (b == sa || b == sb || b == sc) shared++;
            if (c == sa || c == sb || c == sc) shared++;
            return shared;
        }

        private static void DrawFocusTriangleOutline(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, a);
        }

        private void EnsureFocusDistanceConstraintCache(
            ClothSimulator sim,
            ClothSimulationDriverDoubleLayer driver)
        {
            if (sim == null || driver == null)
            {
                _focusLocalDistanceConstraintIndices.Clear();
                return;
            }

            int distanceCount = sim.distanceConstraintCount;
            int sutureRegionSignature = ComputeFocusSutureRegionSignature(sim);
            if (_focusConstraintCacheSimulator == sim &&
                _focusConstraintCacheDriver == driver &&
                _focusConstraintCacheDistanceCount == distanceCount &&
                _focusConstraintCacheOuterCount == driver.OuterCount &&
                _focusConstraintCacheInnerCount == driver.InnerCount &&
                _focusConstraintCacheSutureRegionSignature == sutureRegionSignature &&
                _focusConstraintCachePickedLayer == pickedIncisionTriangleLayer &&
                _focusConstraintCachePickedTriangle == pickedIncisionTriangleIndex)
            {
                return;
            }

            RebuildFocusDistanceConstraintCache(sim, driver, distanceCount);
            _focusConstraintCacheSimulator = sim;
            _focusConstraintCacheDriver = driver;
            _focusConstraintCacheDistanceCount = distanceCount;
            _focusConstraintCacheOuterCount = driver.OuterCount;
            _focusConstraintCacheInnerCount = driver.InnerCount;
            _focusConstraintCacheSutureRegionSignature = sutureRegionSignature;
            _focusConstraintCachePickedLayer = pickedIncisionTriangleLayer;
            _focusConstraintCachePickedTriangle = pickedIncisionTriangleIndex;
        }

        private void RebuildFocusDistanceConstraintCache(
            ClothSimulator sim,
            ClothSimulationDriverDoubleLayer driver,
            int distanceCount)
        {
            _focusLocalDistanceConstraintIndices.Clear();
            var positions = sim.positions;
            if (!positions.IsCreated || positions.Length == 0 || distanceCount <= 0)
                return;

            var regionVertices = new HashSet<int>();
            CollectActiveSutureAffectedVertices(
                sim,
                positions.Length,
                regionVertices);

            foreach (int selectedVertex in _focusConstraintVertexIds)
            {
                if (selectedVertex >= 0 && selectedVertex < positions.Length)
                    regionVertices.Add(selectedVertex);
            }

            for (int i = 0; i < distanceCount; i++)
            {
                DistanceConstraintInfo info = sim.GetDistanceConstraintInfo(i);
                if (!IsSameLayerSurfaceConstraint(info, driver, positions.Length))
                    continue;

                if (regionVertices.Contains(info.vIndex0) &&
                    regionVertices.Contains(info.vIndex1))
                {
                    _focusLocalDistanceConstraintIndices.Add(i);
                }
            }
        }

        private static void CollectActiveSutureAffectedVertices(
            ClothSimulator sim,
            int vertexCount,
            HashSet<int> affectedVertices)
        {
            int lineCount = sim.sutureLineDisplayCount;
            for (int i = 0; i < lineCount; i++)
            {
                SutureLineDisplayInfo line = sim.GetSutureLineDisplayInfo(i);
                if (line.enabled == 0 || line.settled != 0)
                    continue;

                AddSuturePatchVertices(
                    sim,
                    line.patchStartA,
                    line.patchCountA,
                    vertexCount,
                    affectedVertices);
                AddSuturePatchVertices(
                    sim,
                    line.patchStartB,
                    line.patchCountB,
                    vertexCount,
                    affectedVertices);
                AddSutureContactTriangleVertices(
                    sim,
                    line.contactTriStartA,
                    line.contactTriCountA,
                    vertexCount,
                    affectedVertices);
                AddSutureContactTriangleVertices(
                    sim,
                    line.contactTriStartB,
                    line.contactTriCountB,
                    vertexCount,
                    affectedVertices);
            }
        }

        private static void AddSuturePatchVertices(
            ClothSimulator sim,
            int start,
            int count,
            int vertexCount,
            HashSet<int> affectedVertices)
        {
            if (start < 0 || count <= 0)
                return;

            for (int i = 0; i < count; i++)
            {
                SutureLinePatchVertexInfo patch =
                    sim.GetSutureLinePatchVertexInfo(start + i);
                if (patch.vertexIndex >= 0 && patch.vertexIndex < vertexCount)
                    affectedVertices.Add(patch.vertexIndex);
            }
        }

        private static void AddSutureContactTriangleVertices(
            ClothSimulator sim,
            int start,
            int count,
            int vertexCount,
            HashSet<int> affectedVertices)
        {
            if (start < 0 || count <= 0)
                return;

            for (int i = 0; i < count; i++)
            {
                SutureLineContactTriangleInfo triangle =
                    sim.GetSutureLineContactTriangleInfo(start + i);
                AddValidFocusVertex(triangle.v0, vertexCount, affectedVertices);
                AddValidFocusVertex(triangle.v1, vertexCount, affectedVertices);
                AddValidFocusVertex(triangle.v2, vertexCount, affectedVertices);
            }
        }

        private static void AddValidFocusVertex(
            int vertex,
            int vertexCount,
            HashSet<int> affectedVertices)
        {
            if (vertex >= 0 && vertex < vertexCount)
                affectedVertices.Add(vertex);
        }

        private static bool IsSameLayerSurfaceConstraint(
            DistanceConstraintInfo info,
            ClothSimulationDriverDoubleLayer driver,
            int vertexCount)
        {
            if (driver == null ||
                info.vIndex0 < 0 || info.vIndex1 < 0 ||
                info.vIndex0 >= vertexCount || info.vIndex1 >= vertexCount ||
                (info.flags & DistanceConstraintInfo.FlagVirtualSidewall) != 0)
            {
                return false;
            }

            int outerEnd = driver.OuterStart + driver.OuterCount;
            int innerEnd = driver.InnerStart + driver.InnerCount;
            bool outer = info.vIndex0 >= driver.OuterStart &&
                         info.vIndex0 < outerEnd &&
                         info.vIndex1 >= driver.OuterStart &&
                         info.vIndex1 < outerEnd;
            bool inner = info.vIndex0 >= driver.InnerStart &&
                         info.vIndex0 < innerEnd &&
                         info.vIndex1 >= driver.InnerStart &&
                         info.vIndex1 < innerEnd;
            return outer || inner;
        }

        private static int ComputeFocusSutureRegionSignature(ClothSimulator sim)
        {
            unchecked
            {
                int hash = 17;
                int count = sim.sutureLineDisplayCount;
                hash = hash * 31 + count;
                for (int i = 0; i < count; i++)
                {
                    SutureLineDisplayInfo line = sim.GetSutureLineDisplayInfo(i);
                    hash = hash * 31 + line.lineIndex;
                    hash = hash * 31 + line.enabled;
                    hash = hash * 31 + line.settled;
                    hash = hash * 31 + line.patchStartA;
                    hash = hash * 31 + line.patchCountA;
                    hash = hash * 31 + line.patchStartB;
                    hash = hash * 31 + line.patchCountB;
                    hash = hash * 31 + line.contactTriStartA;
                    hash = hash * 31 + line.contactTriCountA;
                    hash = hash * 31 + line.contactTriStartB;
                    hash = hash * 31 + line.contactTriCountB;
                }
                return hash;
            }
        }

        private void DrawFocusDistanceConstraints(ClothSimulator sim)
        {
            if (sim == null || _focusLocalDistanceConstraintIndices.Count == 0)
                return;

            var positions = sim.positions;
            int distanceCount = sim.distanceConstraintCount;
            for (int i = 0; i < _focusLocalDistanceConstraintIndices.Count; i++)
            {
                int constraintIndex = _focusLocalDistanceConstraintIndices[i];
                if (constraintIndex < 0 || constraintIndex >= distanceCount)
                    continue;

                DistanceConstraintInfo info =
                    sim.GetDistanceConstraintInfo(constraintIndex);
                if (info.vIndex0 < 0 || info.vIndex1 < 0 ||
                    info.vIndex0 >= positions.Length ||
                    info.vIndex1 >= positions.Length)
                {
                    continue;
                }

                Vector3 p0 = ToVector3(positions[info.vIndex0]);
                Vector3 p1 = ToVector3(positions[info.vIndex1]);
                float ratio = GetDistanceConstraintStrainRatio(info, p0, p1);
                DrawDistanceConstraintEdge(info, p0, p1, ratio);
            }
        }

        public string BuildIncisionRuntimeDebugSummary()
        {
            int syncedTraceCount = forceSingleTargetTraceFromDriver && enableIncisionTargetTrace ? 1 : Mathf.Max(1, incisionTargetTraceCount);
            return $"targetTraceEnabled={enableIncisionTargetTrace}, targetTraceCount={syncedTraceCount}, neighborRadius={incisionTargetTraceNeighborRadius:F6}, logToConsole={incisionTargetTraceLogToConsole}, pairMinSpacing={incisionPairMinSpacing:F6}, debugMinSpacing={incisionDebugMinSpacing:F6}, drawStride={Mathf.Max(1, incisionDebugDrawStride)}, singleFocus={forceSingleTargetTraceFromDriver}";
        }

        public bool TrySyncIncisionRuntimeDebugToDriverNow(bool verbose, out string summary)
        {
            AutoFindDrivers();

            summary = BuildIncisionRuntimeDebugSummary();
            if (!syncIncisionRuntimeDebugToDriver)
            {
                summary = "syncDisabled, " + summary;
                return false;
            }

            var driver = simulationDouble;
            if (driver == null)
            {
                summary = "driverMissing, " + summary;
                return false;
            }

            driver.enableIncisionTargetTrace = enableIncisionTargetTrace;
            driver.incisionTargetTraceCount = forceSingleTargetTraceFromDriver && enableIncisionTargetTrace ? 1 : Mathf.Max(1, incisionTargetTraceCount);
            driver.incisionTargetTraceNeighborRadius = incisionTargetTraceNeighborRadius;
            driver.incisionTargetTraceLogToConsole = incisionTargetTraceLogToConsole;
            driver.incisionPairMinSpacing = incisionPairMinSpacing;
            driver.incisionDebugMinSpacing = incisionDebugMinSpacing;
            driver.incisionDebugDrawStride = Mathf.Max(1, incisionDebugDrawStride);

            if (verbose)
                Debug.Log($"[ClothDebugGizmos] SyncIncisionRuntimeDebugToDriver -> {driver.name}: {summary}");

            return true;
        }

        private void SyncIncisionRuntimeDebugToDriver()
        {
            TrySyncIncisionRuntimeDebugToDriverNow(verbose: false, out _);
        }

        private void DrawParticles(ClothSimulator sim)
        {
            var positions = sim.positions;
            var types = sim.ConstraintTypes;

            for (int i = 0; i < positions.Length; i++)
            {
                Color c = normalParticleColor;
                if (types.IsCreated && types.Length == positions.Length)
                {
                    var t = types[i];
                    bool hasPin = (t & ConstraintType.Pin) == ConstraintType.Pin;
                    bool hasCollision = (t & ConstraintType.Collision) == ConstraintType.Collision;

                    if (preferCollisionOverPin)
                    {
                        if (hasCollision) c = collisionParticleColor;
                        else if (hasPin) c = pinParticleColor;
                    }
                    else
                    {
                        if (hasPin) c = pinParticleColor;
                        else if (hasCollision) c = collisionParticleColor;
                    }
                }

                Gizmos.color = c;
                Gizmos.DrawSphere(ToVector3(positions[i]), particleRadius);
            }
        }

        private void DrawDistanceEdges(ClothSimulator sim)
        {
            int count = sim.distanceConstraintCount;
            int normalEdgeStride = GetPlayModeDrawStride(count, maxNormalDistanceEdgesDuringPlay);
            for (int i = 0; i < count; i++)
            {
                var info = sim.GetDistanceConstraintInfo(i);
                Vector3 p0 = ToVector3(sim.positions[info.vIndex0]);
                Vector3 p1 = ToVector3(sim.positions[info.vIndex1]);

                float ratio = GetDistanceConstraintStrainRatio(info, p0, p1);

                bool isSemanticOrStrainedEdge =
                    (info.flags & DistanceConstraintInfo.FlagVirtualSidewall) != 0 ||
                    ratio > edgeMidStrainThreshold;
                if (!isSemanticOrStrainedEdge && !ShouldDrawPlayModeSample(i, normalEdgeStride, maxNormalDistanceEdgesDuringPlay))
                    continue;

                DrawDistanceConstraintEdge(info, p0, p1, ratio);
            }
        }

        private static float GetDistanceConstraintStrainRatio(
            DistanceConstraintInfo info,
            Vector3 p0,
            Vector3 p1)
        {
            float restLength = Mathf.Max(1e-6f, info.restLength);
            return Vector3.Distance(p0, p1) / restLength;
        }

        private void DrawDistanceConstraintEdge(
            DistanceConstraintInfo info,
            Vector3 p0,
            Vector3 p1,
            float ratio)
        {
            if ((info.flags & DistanceConstraintInfo.FlagVirtualSidewallShear) != 0)
                Gizmos.color = virtualSidewallShearConstraintColor;
            else if ((info.flags & DistanceConstraintInfo.FlagVirtualSidewallDistance) != 0)
                Gizmos.color = virtualSidewallDistanceConstraintColor;
            else if (ratio > edgeHighStrainThreshold)
                Gizmos.color = edgeHighStrainColor;
            else if (ratio > edgeMidStrainThreshold)
                Gizmos.color = edgeMidStrainColor;
            else
                Gizmos.color = edgeNormalColor;

            Gizmos.DrawLine(p0, p1);
        }

        private void DrawVirtualSidewallConstraintMarkers(ClothSimulator sim)
        {
            var positions = sim.positions;
            var types = sim.ConstraintTypes;
            if (!positions.IsCreated || !types.IsCreated || positions.Length != types.Length)
                return;

            Gizmos.color = virtualSidewallParticleColor;
            for (int i = 0; i < positions.Length; i++)
            {
                if ((types[i] & ConstraintType.VirtualSidewall) == 0)
                    continue;

                Vector3 p = ToVector3(positions[i]);
                Gizmos.DrawSphere(p, virtualSidewallParticleRadius);
#if UNITY_EDITOR
                if (drawVirtualSidewallConstraintLabels)
                    UnityEditor.Handles.Label(p + Vector3.up * (virtualSidewallParticleRadius * 1.5f), $"VSW-P {i}");
#endif
            }

            int constraintCount = sim.distanceConstraintCount;
            for (int i = 0; i < constraintCount; i++)
            {
                var info = sim.GetDistanceConstraintInfo(i);
                if ((info.flags & DistanceConstraintInfo.FlagVirtualSidewall) == 0 ||
                    info.vIndex0 < 0 || info.vIndex1 < 0 ||
                    info.vIndex0 >= positions.Length || info.vIndex1 >= positions.Length)
                {
                    continue;
                }

                bool shear = (info.flags & DistanceConstraintInfo.FlagVirtualSidewallShear) != 0;
                Gizmos.color = shear ? virtualSidewallShearConstraintColor : virtualSidewallDistanceConstraintColor;
                Vector3 p0 = ToVector3(positions[info.vIndex0]);
                Vector3 p1 = ToVector3(positions[info.vIndex1]);
                Gizmos.DrawLine(p0, p1);
#if UNITY_EDITOR
                if (drawVirtualSidewallConstraintLabels)
                {
                    string tag = shear ? "VSW-S" : "VSW-D";
                    UnityEditor.Handles.Label((p0 + p1) * 0.5f, $"{tag} e{info.sourceEdgeId} side={info.sourceSide}");
                }
#endif
            }
        }

        private void DrawCrossLayerLinks(ClothSimulator sim)
        {
            int count = sim.crossLayerLinkCount;
            if (count <= 0) return;

            var pos = sim.predictPositions;
            int drawStride = GetPlayModeDrawStride(count, maxCrossLayerLinksDuringPlay);

            float maxCompress = 0f;
            float maxStretch = 0f;
            float sumAbs = 0f;

            for (int i = 0; i < count; i++)
            {
                var link = sim.GetCrossLayerLinkInfo(i);
                Vector3 pa = ToVector3(pos[link.a]);
                Vector3 pb = ToVector3(pos[link.b]);
                float len = (pb - pa).magnitude;
                float rest = Mathf.Max(thicknessRestEpsilon, link.restLength);
                float diff = len - rest;

                sumAbs += Mathf.Abs(diff);
                if (diff < 0) maxCompress = Mathf.Max(maxCompress, -diff);
                else maxStretch = Mathf.Max(maxStretch, diff);

                if (!ShouldDrawPlayModeSample(i, drawStride, maxCrossLayerLinksDuringPlay))
                    continue;

                if (Mathf.Abs(diff) <= rest * 0.01f)
                    Gizmos.color = thicknessNeutralColor;
                else
                    Gizmos.color = (diff < 0) ? thicknessCompressColor : thicknessStretchColor;

                Gizmos.DrawLine(pa, pb);
            }

            if (thicknessLogEveryNFrames > 0 && (Time.frameCount % thicknessLogEveryNFrames) == 0)
            {
                float avgAbs = sumAbs / Mathf.Max(1, count);
                Debug.Log($"[ThicknessLinks] count={count}, maxCompress={maxCompress:F6}, maxStretch={maxStretch:F6}, avgAbs={avgAbs:F6}");
            }
        }

        private void DrawParticleBVH(ClothSimulator sim)
        {
            var bvh = sim.ParticleBVH;
            if (bvh == null || bvh.rootAABB == null) return;

            var queries = new List<AABB>(8);
            BuildParticleQueries(sim.ColliderDescGroups, queryExtra, queries);
            if (queries.Count == 0) return;

            ApplyQuerySelection(queries);
            UpdateAutoReveal(queries[0], true);

            if (drawParticleQueryAABB)
            {
                Gizmos.color = particleQueryColor;
                for (int i = 0; i < queries.Count; i++)
                    DrawWireAABB(queries[i]);
            }

            if (drawParticleHitNodes)
            {
                for (int qi = 0; qi < queries.Count; qi++)
                {
                    var hitByDepth = new List<List<AABBNode<int>>>(32);
                    CollectHitNodesByDepth(bvh.rootAABB, queries[qi], 0, hitByDepth);

                    int maxDepthToDraw = Mathf.Min(revealDepth, hitByDepth.Count - 1);
                    for (int d = 0; d <= maxDepthToDraw; d++)
                    {
                        var nodes = hitByDepth[d];
                        if (nodes == null) continue;

                        for (int i = 0; i < nodes.Count; i++)
                        {
                            var node = nodes[i];
                            if (node == null || node.box == null) continue;
                            if (onlyDrawLeafNodes && !node.IsLeaf) continue;

                            Gizmos.color = ColorForDepth(d, node.IsLeaf, particleNodeNearColor, particleNodeFarColor);
                            DrawWireAABB(node.box);

                            if (drawLeafMarker && node.IsLeaf)
                            {
                                var c = (node.box.Min + node.box.Max) * 0.5f;
                                Gizmos.DrawCube(c, Vector3.one * leafMarkerSize);
                            }
                        }
                    }
                }
            }

            if (drawParticleCandidates)
            {
                var candidates = new HashSet<int>();
                if (useRuntimeParticleCandidates)
                    CollectRuntimeParticleCandidates(sim, candidates);
                else
                    CollectBVHCandidatesFromQueries(bvh, queries, candidates);

                if (candidates.Count > 0)
                {
                    var positions = sim.positions;
                    Gizmos.color = particleCandidatesColor;
                    foreach (var idx in candidates)
                    {
                        if (idx < 0 || idx >= positions.Length) continue;
                        Gizmos.DrawSphere(ToVector3(positions[idx]), particleCandidatesRadius);
                    }
                }
            }
        }

        private void DrawTriangleBVH(ClothSimulator sim)
        {
            var bvh = sim.TriangleBVH;
            if (bvh == null || bvh.rootAABB == null) return;

            var queries = new List<AABB>(8);
            BuildTriangleQueries(sim.ColliderDescGroups, queryExtra, queries);
            if (queries.Count == 0) return;

            ApplyQuerySelection(queries);
            UpdateAutoReveal(queries[0], false);

            if (drawTriangleQueryAABB)
            {
                Gizmos.color = triangleQueryColor;
                for (int i = 0; i < queries.Count; i++)
                    DrawWireAABB(queries[i]);
            }

            if (drawTriangleHitNodes)
            {
                for (int qi = 0; qi < queries.Count; qi++)
                {
                    var hitByDepth = new List<List<AABBNode<int>>>(32);
                    CollectHitNodesByDepth(bvh.rootAABB, queries[qi], 0, hitByDepth);

                    int maxDepthToDraw = Mathf.Min(revealDepth, hitByDepth.Count - 1);
                    for (int d = 0; d <= maxDepthToDraw; d++)
                    {
                        var nodes = hitByDepth[d];
                        if (nodes == null) continue;

                        for (int i = 0; i < nodes.Count; i++)
                        {
                            var node = nodes[i];
                            if (node == null || node.box == null) continue;
                            if (onlyDrawLeafNodes && !node.IsLeaf) continue;

                            Gizmos.color = ColorForDepth(d, node.IsLeaf, triangleNodeNearColor, triangleNodeFarColor);
                            DrawWireAABB(node.box);

                            if (drawLeafMarker && node.IsLeaf)
                            {
                                var c = (node.box.Min + node.box.Max) * 0.5f;
                                Gizmos.DrawCube(c, Vector3.one * leafMarkerSize);
                            }
                        }
                    }
                }
            }

            if (drawTriangleCandidates)
            {
                var candidates = new HashSet<int>();
                if (useRuntimeTriangleCandidates)
                    CollectRuntimeTriangleCandidates(sim, candidates);
                else
                    CollectBVHCandidatesFromQueries(bvh, queries, candidates);

                if (candidates.Count > 0)
                {
                    DrawTriangleCandidates(sim, candidates);
                }
            }
        }


        private static string GetPortalPassLabel(NeedlePortalDisplayInfo info)
        {
            if (info.passIndex > 0 && info.portalIndex > 0)
                return $"P{info.passIndex}#{info.portalIndex}";
            if (info.passIndex > 0)
                return $"P{info.passIndex}";
            if (info.portalIndex > 0)
                return $"#{info.portalIndex}";
            return "P?";
        }

        private static string BuildPortalLabel(NeedlePortalInfo portal, NeedlePortalDisplayInfo info, bool historical, bool compactDone)
        {
            if (compactDone && info.stitchState == (byte)NeedlePortalStitchState.Done)
                return $"{GetPortalPassLabel(info)} Done";

            string layer = GetPortalLayerLabel(portal);
            string io = info.isExitPortal != 0 ? "Out" : "In";
            string prefix = GetPortalStitchStateLabel(info, historical);
            string semantic = GetPortalSemanticLabel(info);
            string wall = info.wallEventOwner == (byte)NeedlePortalWallEventOwner.WholeWall
                ? $" E:{GetPortalLayerLabel(info.entryLayer)} B:{GetPortalLayerLabel(info.breakthroughLayer)}"
                : string.Empty;
            string fx = info.suppressPunctureFx != 0 ? " fxOff" : string.Empty;
            string stitch = info.stitchIndex > 0 ? $" L{info.stitchIndex}" : string.Empty;
            return $"{prefix}{stitch} {GetPortalPassLabel(info)} {layer} {semantic} {io}{wall}{fx} entity={portal.colliderEntityId} tri={portal.triangleIndex}";
        }

        private static bool ShouldDrawPortalLabel(
            NeedlePortalDisplayInfo info,
            bool compactDone,
            bool belongsToSettledLine)
        {
            return !compactDone ||
                (!belongsToSettledLine && info.stitchState != (byte)NeedlePortalStitchState.Done);
        }

        private static bool ShouldDrawSutureLineLabel(
            bool settledLine,
            bool completedWithoutRealSidewallContact,
            bool compactDone)
        {
            return completedWithoutRealSidewallContact || !settledLine || !compactDone;
        }

        private static bool IsPassInSettledSutureLine(
            ClothSimulator sim,
            int colliderEntityId,
            int passIndex)
        {
            if (sim == null || passIndex <= 0)
                return false;

            for (int i = 0; i < sim.sutureLineDisplayCount; i++)
            {
                SutureLineDisplayInfo line = sim.GetSutureLineDisplayInfo(i);
                if (line.enabled == 0 || line.settled == 0)
                    continue;
                if (colliderEntityId != 0 && line.colliderEntityId != colliderEntityId)
                    continue;
                if (line.passIndexA == passIndex || line.passIndexB == passIndex)
                    return true;
            }

            return false;
        }

        private static string GetPortalSemanticLabel(NeedlePortalDisplayInfo info)
        {
            if (info.isProvisional != 0) return "PreChannel";
            if (info.isBreakthroughEvent != 0) return "Breakthrough";
            if (info.passComplete != 0 && info.wallEventOwner == (byte)NeedlePortalWallEventOwner.WholeWall) return "WholeWall Done";
            return "Committed";
        }

        private static string GetPortalStitchStateLabel(NeedlePortalDisplayInfo info, bool historical)
        {
            if (info.stitchState == (byte)NeedlePortalStitchState.Done) return "Done";
            if (info.stitchState == (byte)NeedlePortalStitchState.Hist) return "Hist";
            if (info.stitchState == (byte)NeedlePortalStitchState.Current) return "Current";
            return historical ? "Hist" : "Current";
        }

        private static string GetPortalLayerLabel(NeedlePortalInfo portal)
        {
            if (portal.wallLayer == (byte)NeedlePortalWallLayer.Outer) return "Outer";
            if (portal.wallLayer == (byte)NeedlePortalWallLayer.Inner) return "Inner";
            return "Unknown";
        }

        private static string GetPortalLayerLabel(byte wallLayer)
        {
            if (wallLayer == (byte)NeedlePortalWallLayer.Outer) return "Outer";
            if (wallLayer == (byte)NeedlePortalWallLayer.Inner) return "Inner";
            return "Unknown";
        }

        private Color GetPortalLayerColor(NeedlePortalInfo portal, Color fallback)
        {
            if (!colorPortalByLayer) return fallback;
            if (portal.wallLayer == (byte)NeedlePortalWallLayer.Inner) return portalInnerColor;
            if (portal.wallLayer == (byte)NeedlePortalWallLayer.Outer) return portalOuterColor;
            return fallback;
        }

        private void DrawNeedlePuncturePortals(
            ClothSimulator sim,
            bool includeLabels = true)
        {
            int count = sim.activeNeedlePortalCount;
            if (count <= 0) return;

            var pos = sim.predictPositions;

            for (int i = 0; i < count; i++)
            {
                var portal = sim.GetActiveNeedlePortal(i);
                var display = sim.GetActiveNeedlePortalDisplayInfo(i);
                if (portal.enabled == 0) continue;
                if (display.isHistorical != 0) continue;

                Vector3 center = ToVector3(portal.currentPoint);
                Vector3 normal = ToVector3(math.normalizesafe(portal.entryNormal, new float3(0, 0, 1)));
                Vector3 insertDir = ToVector3(math.normalizesafe(portal.insertDirection, -portal.entryNormal));
                float rr = Mathf.Max(0.0001f, portal.relaxRadius);

                Color layerColor = GetPortalLayerColor(portal, portalCenterColor);
                if (display.isProvisional != 0)
                    layerColor.a = 0.48f;
                float pointRadius = display.isProvisional != 0
                    ? Mathf.Max(0.00008f, portalPointRadius * 0.68f)
                    : portalPointRadius;

                Gizmos.color = layerColor;
                if (display.isProvisional != 0)
                    Gizmos.DrawWireSphere(center, pointRadius * 1.8f);
                else
                    Gizmos.DrawSphere(center, pointRadius);

                if (drawPortalWire)
                {
                    Gizmos.color = GetPortalLayerColor(portal, portalWireColor);
                    Gizmos.DrawWireSphere(center, display.isProvisional != 0 ? rr * 0.72f : rr);
                }

                if (drawPortalNormal)
                {
                    Gizmos.color = portalNormalColor;
                    Gizmos.DrawLine(center, center + normal * rr * 1.2f);
                    Gizmos.DrawLine(center, center + insertDir * rr * 1.2f);
                }

                if (drawPortalTriangle)
                {
                    if (portal.v0 >= 0 && portal.v1 >= 0 && portal.v2 >= 0 &&
                        portal.v0 < pos.Length && portal.v1 < pos.Length && portal.v2 < pos.Length)
                    {
                        Gizmos.color = GetPortalLayerColor(portal, portalTriangleColor);
                        Vector3 p0 = ToVector3(pos[portal.v0]);
                        Vector3 p1 = ToVector3(pos[portal.v1]);
                        Vector3 p2 = ToVector3(pos[portal.v2]);
                        Gizmos.DrawLine(p0, p1);
                        Gizmos.DrawLine(p1, p2);
                        Gizmos.DrawLine(p2, p0);
                    }
                }

#if UNITY_EDITOR
                bool belongsToSettledLine = IsPassInSettledSutureLine(
                    sim,
                    display.colliderEntityId,
                    display.passIndex);
                if (includeLabels && drawPortalLabel && ShouldDrawPortalLabel(
                        display,
                        compactCompletedSutureLabels,
                        belongsToSettledLine))
                {
                    UnityEditor.Handles.color = layerColor;
                    UnityEditor.Handles.Label(center, BuildPortalLabel(portal, display, false, compactCompletedSutureLabels));
                }
#endif
            }
        }


        private void DrawNeedlePortalHistory(ClothSimulator sim)
        {
            int count = sim.portalHistoryCount;
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                var history = sim.GetPortalHistory(i);
                if (history.portal.enabled == 0)
                    continue;
                if (!includeActivePortalsInHistory && history.isActive != 0)
                    continue;

                var portal = history.portal;
                Vector3 center = ToVector3(portal.currentPoint);
                float rr = Mathf.Max(0.0001f, portal.relaxRadius * 0.82f);
                Color historyColor = GetPortalLayerColor(portal, new Color(0.75f, 0.75f, 0.75f, 0.95f));
                historyColor.a = 0.85f;

                Gizmos.color = historyColor;
                Gizmos.DrawWireSphere(center, rr);
                Gizmos.DrawSphere(center, Mathf.Max(0.00008f, portalPointRadius * 0.75f));

#if UNITY_EDITOR
                if (drawPortalLabel)
                {
                    var info = new NeedlePortalDisplayInfo()
                    {
                        portalIndex = history.portalIndex,
                        passIndex = history.passIndex,
                        pairedPortalIndex = history.pairedPortalIndex,
                        colliderEntityId = history.colliderEntityId,
                        stitchIndex = history.stitchIndex,
                        isExitPortal = history.isExitPortal,
                        isConstraintPortal = 0,
                        isHistorical = 1,
                        stitchState = history.stitchState,
                        reserved0 = 0,
                        isProvisional = portal.isProvisional,
                        suppressPunctureFx = portal.suppressPunctureFx,
                        isBreakthroughEvent = portal.isBreakthroughEvent,
                        wallEventOwner = portal.wallEventOwner,
                        entryLayer = portal.entryLayer,
                        breakthroughLayer = portal.breakthroughLayer,
                        passComplete = portal.passComplete,
                    };
                    bool belongsToSettledLine = IsPassInSettledSutureLine(
                        sim,
                        info.colliderEntityId,
                        info.passIndex);
                    if (ShouldDrawPortalLabel(
                            info,
                            compactCompletedSutureLabels,
                            belongsToSettledLine))
                    {
                        UnityEditor.Handles.color = historyColor;
                        UnityEditor.Handles.Label(center + Vector3.up * (portalPointRadius * 1.4f), BuildPortalLabel(portal, info, true, compactCompletedSutureLabels));
                    }
                }
#endif
            }
        }

        private void DrawSutureLines(
            ClothSimulator sim,
            bool includeLabels = true,
            bool includeContactRoi = true)
        {
            int count = sim.sutureLineDisplayCount;
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                SutureLineDisplayInfo line = sim.GetSutureLineDisplayInfo(i);
                if (line.enabled == 0)
                    continue;

                Vector3 a = ToVector3(line.pointA);
                Vector3 b = ToVector3(line.pointB);
                Vector3 mid = (a + b) * 0.5f;

                Gizmos.color = sutureLineColor;
                Gizmos.DrawLine(a, b);
                Gizmos.DrawSphere(a, Mathf.Max(0.00008f, sutureLinePointRadius));
                Gizmos.DrawSphere(b, Mathf.Max(0.00008f, sutureLinePointRadius));
                bool settledLine = line.settled != 0;
                bool completedWithoutRealSidewallContact = line.completedWithoutRealSidewallContact != 0;
                if (includeContactRoi && !settledLine)
                    DrawSutureLineContactRoi(sim, line);

#if UNITY_EDITOR
                if (includeLabels && drawPortalLabel && ShouldDrawSutureLineLabel(
                        settledLine,
                        completedWithoutRealSidewallContact,
                        compactCompletedSutureLabels))
                {
                    UnityEditor.Handles.color = sutureLineColor;
                    string wallState = completedWithoutRealSidewallContact
                        ? "NoRealSidewallContact"
                        : line.contactState == 1 ? "Latched" : (line.contactState == 2 ? "PreLatch" : "Approach");
                    string foldState = line.compressionFoldActive != 0 ? " Fold" : string.Empty;
                    UnityEditor.Handles.Label(mid, $"L{line.lineIndex} P{line.passIndexA}<->P{line.passIndexB} {wallState}{foldState} TT={line.triPairs} VT={line.vtPairs} EE={line.eePairs} ROI={line.roiTriA}/{line.roiTriB} VW={line.virtualSidewallTriA}/{line.virtualSidewallTriB}");
                }
#endif
            }
        }

        private void DrawSutureLineContactRoi(ClothSimulator sim, SutureLineDisplayInfo line)
        {
            var pos = sim.predictPositions;
            var indices = sim.indices;
            Gizmos.color = new Color(1f, 0.9f, 0.05f, 0.55f);
            DrawSutureLinePatchVertices(sim, pos, line.patchStartA, line.patchCountA);
            DrawSutureLinePatchVertices(sim, pos, line.patchStartB, line.patchCountB);

            DrawSutureLineContactTriangles(sim, pos, indices, line.contactTriStartA, line.contactTriCountA);
            DrawSutureLineContactTriangles(sim, pos, indices, line.contactTriStartB, line.contactTriCountB);
            DrawSutureLinePenetrationHits(sim, pos, line);
        }

        private void DrawSutureLinePatchVertices(ClothSimulator sim, Unity.Collections.NativeArray<float3> pos, int start, int count)
        {
            if (start < 0 || count <= 0) return;
            int limit = Mathf.Min(count, 96);
            float radius = Mathf.Max(0.00005f, sutureLinePointRadius * 0.55f);
            for (int i = 0; i < limit; i++)
            {
                SutureLinePatchVertexInfo pv = sim.GetSutureLinePatchVertexInfo(start + i);
                if (pv.vertexIndex < 0 || pv.vertexIndex >= pos.Length)
                    continue;
                Gizmos.DrawSphere(ToVector3(pos[pv.vertexIndex]), radius);
            }
        }

        private void DrawSutureLineContactTriangles(ClothSimulator sim, Unity.Collections.NativeArray<float3> pos, Unity.Collections.NativeArray<int> indices, int start, int count)
        {
            if (start < 0 || count <= 0) return;
            int limit = Mathf.Min(count, 96);
            for (int i = 0; i < limit; i++)
            {
                SutureLineContactTriangleInfo tri = sim.GetSutureLineContactTriangleInfo(start + i);
                if (tri.v0 < 0 || tri.v1 < 0 || tri.v2 < 0 ||
                    tri.v0 >= pos.Length || tri.v1 >= pos.Length || tri.v2 >= pos.Length)
                    continue;
                Gizmos.color = (tri.flags & SutureLineContactTriangleInfo.FlagVirtualSidewall) != 0
                    ? new Color(0.0f, 0.9f, 1.0f, 0.75f)
                    : new Color(1f, 0.55f, 0.05f, 0.45f);
                Vector3 p0 = ToVector3(pos[tri.v0]);
                Vector3 p1 = ToVector3(pos[tri.v1]);
                Vector3 p2 = ToVector3(pos[tri.v2]);
                Gizmos.DrawLine(p0, p1);
                Gizmos.DrawLine(p1, p2);
                Gizmos.DrawLine(p2, p0);
            }
        }

        private void DrawSutureLinePenetrationHits(ClothSimulator sim, Unity.Collections.NativeArray<float3> pos, SutureLineDisplayInfo line)
        {
            if (line.selfContactThickness <= 1e-7f ||
                line.contactTriStartA < 0 || line.contactTriStartB < 0 ||
                line.contactTriCountA <= 0 || line.contactTriCountB <= 0)
                return;

            int limitA = Mathf.Min(line.contactTriCountA, 48);
            int limitB = Mathf.Min(line.contactTriCountB, 48);
            int hitBudget = 64;
            float radius = Mathf.Max(0.00006f, sutureLinePointRadius * 0.45f);
            Gizmos.color = new Color(1f, 0.02f, 0.55f, 0.95f);

            for (int ai = 0; ai < limitA && hitBudget > 0; ai++)
            {
                SutureLineContactTriangleInfo triA = sim.GetSutureLineContactTriangleInfo(line.contactTriStartA + ai);
                if (!IsValidSutureLineDebugTriangle(triA, pos.Length))
                    continue;

                for (int bi = 0; bi < limitB && hitBudget > 0; bi++)
                {
                    SutureLineContactTriangleInfo triB = sim.GetSutureLineContactTriangleInfo(line.contactTriStartB + bi);
                    if (!IsValidSutureLineDebugTriangle(triB, pos.Length) ||
                        SutureLineDebugTrianglesShareVertex(triA, triB))
                        continue;

                    DrawSutureLineVTHit(pos, triA.v0, triB, line.selfContactThickness, radius, ref hitBudget);
                    DrawSutureLineVTHit(pos, triA.v1, triB, line.selfContactThickness, radius, ref hitBudget);
                    DrawSutureLineVTHit(pos, triA.v2, triB, line.selfContactThickness, radius, ref hitBudget);
                    DrawSutureLineVTHit(pos, triB.v0, triA, line.selfContactThickness, radius, ref hitBudget);
                    DrawSutureLineVTHit(pos, triB.v1, triA, line.selfContactThickness, radius, ref hitBudget);
                    DrawSutureLineVTHit(pos, triB.v2, triA, line.selfContactThickness, radius, ref hitBudget);
                    DrawSutureLineEEHits(pos, triA, triB, line.selfContactThickness, radius, ref hitBudget);
                }
            }
        }

        private void DrawSutureLineVTHit(
            Unity.Collections.NativeArray<float3> pos,
            int vertex,
            SutureLineContactTriangleInfo tri,
            float thickness,
            float radius,
            ref int hitBudget)
        {
            if (hitBudget <= 0 || vertex < 0 || vertex >= pos.Length)
                return;

            float3 p = pos[vertex];
            float3 closest = ClosestPointOnTriangleForGizmo(p, pos[tri.v0], pos[tri.v1], pos[tri.v2]);
            float dist = math.distance(p, closest);
            if (dist > thickness)
                return;

            Gizmos.DrawSphere(ToVector3(p), radius);
            Gizmos.DrawLine(ToVector3(p), ToVector3(closest));
            hitBudget--;
        }

        private void DrawSutureLineEEHits(
            Unity.Collections.NativeArray<float3> pos,
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float radius,
            ref int hitBudget)
        {
            DrawSutureLineEEHit(pos, triA.v0, triA.v1, triB.v0, triB.v1, thickness, radius, ref hitBudget);
            DrawSutureLineEEHit(pos, triA.v0, triA.v1, triB.v1, triB.v2, thickness, radius, ref hitBudget);
            DrawSutureLineEEHit(pos, triA.v0, triA.v1, triB.v2, triB.v0, thickness, radius, ref hitBudget);
            DrawSutureLineEEHit(pos, triA.v1, triA.v2, triB.v0, triB.v1, thickness, radius, ref hitBudget);
            DrawSutureLineEEHit(pos, triA.v1, triA.v2, triB.v1, triB.v2, thickness, radius, ref hitBudget);
            DrawSutureLineEEHit(pos, triA.v1, triA.v2, triB.v2, triB.v0, thickness, radius, ref hitBudget);
            DrawSutureLineEEHit(pos, triA.v2, triA.v0, triB.v0, triB.v1, thickness, radius, ref hitBudget);
            DrawSutureLineEEHit(pos, triA.v2, triA.v0, triB.v1, triB.v2, thickness, radius, ref hitBudget);
            DrawSutureLineEEHit(pos, triA.v2, triA.v0, triB.v2, triB.v0, thickness, radius, ref hitBudget);
        }

        private void DrawSutureLineEEHit(
            Unity.Collections.NativeArray<float3> pos,
            int a0,
            int a1,
            int b0,
            int b1,
            float thickness,
            float radius,
            ref int hitBudget)
        {
            if (hitBudget <= 0 ||
                a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1)
                return;

            ClosestPointsOnSegmentsForGizmo(pos[a0], pos[a1], pos[b0], pos[b1], out float3 pa, out float3 pb);
            float dist = math.distance(pa, pb);
            if (dist > thickness)
                return;

            Gizmos.DrawSphere(ToVector3(pa), radius);
            Gizmos.DrawSphere(ToVector3(pb), radius);
            Gizmos.DrawLine(ToVector3(pa), ToVector3(pb));
            hitBudget--;
        }

        private static bool IsValidSutureLineDebugTriangle(SutureLineContactTriangleInfo tri, int vertexCount)
        {
            return tri.v0 >= 0 && tri.v1 >= 0 && tri.v2 >= 0 &&
                   tri.v0 < vertexCount && tri.v1 < vertexCount && tri.v2 < vertexCount &&
                   tri.v0 != tri.v1 && tri.v1 != tri.v2 && tri.v2 != tri.v0;
        }

        private static bool SutureLineDebugTrianglesShareVertex(SutureLineContactTriangleInfo a, SutureLineContactTriangleInfo b)
        {
            return a.v0 == b.v0 || a.v0 == b.v1 || a.v0 == b.v2 ||
                   a.v1 == b.v0 || a.v1 == b.v1 || a.v1 == b.v2 ||
                   a.v2 == b.v0 || a.v2 == b.v1 || a.v2 == b.v2;
        }

        private void CollectRuntimeParticleCandidates(ClothSimulator sim, HashSet<int> outSet)
        {
            int count = sim.collisionCandidateCount;
            for (int i = 0; i < count; i++)
            {
                outSet.Add(sim.GetCollisionCandidate(i));
            }
        }

        private void CollectRuntimeTriangleCandidates(ClothSimulator sim, HashSet<int> outSet)
        {
            int count = sim.triangleSurfaceContactCandidateCount;
            for (int i = 0; i < count; i++)
            {
                outSet.Add(sim.GetTriangleSurfaceContactCandidate(i));
            }
        }

        private void CollectBVHCandidatesFromQueries(AABBTree<int> bvh, List<AABB> queries, HashSet<int> outSet)
        {
            for (int qi = 0; qi < queries.Count; qi++)
            {
                var hitNodes = bvh.Traverse(box => box.IntersectsAABB(queries[qi]));
                for (int i = 0; i < hitNodes.Count; i++)
                {
                    var node = hitNodes[i];
                    if (node == null || !node.IsLeaf || node.gobjects == null) continue;
                    for (int k = 0; k < node.gobjects.Count; k++)
                        outSet.Add(node.gobjects[k]);
                }
            }
        }

        private void DrawTriangleCandidates(ClothSimulator sim, HashSet<int> triangles)
        {
            var indices = sim.indices;
            var pos = sim.predictPositions;

            Gizmos.color = triangleCandidatesColor;

            foreach (var tri in triangles)
            {
                int offset = tri * 3;
                if (offset < 0 || offset + 2 >= indices.Length) continue;

                int i0 = indices[offset];
                int i1 = indices[offset + 1];
                int i2 = indices[offset + 2];

                if (i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= pos.Length || i1 >= pos.Length || i2 >= pos.Length)
                    continue;

                Vector3 p0 = ToVector3(pos[i0]);
                Vector3 p1 = ToVector3(pos[i1]);
                Vector3 p2 = ToVector3(pos[i2]);

                if (drawTriangleCandidateWire)
                {
                    Gizmos.DrawLine(p0, p1);
                    Gizmos.DrawLine(p1, p2);
                    Gizmos.DrawLine(p2, p0);
                }

                if (drawTriangleCandidateCenters)
                {
                    Vector3 c = (p0 + p1 + p2) / 3f;
                    Gizmos.DrawSphere(c, triangleCandidateCenterRadius);
                }
            }
        }

        private void CollectHitNodesByDepth(AABBNode<int> node, AABB query, int depth, List<List<AABBNode<int>>> outByDepth)
        {
            if (node == null || node.box == null) return;
            if (!node.box.IntersectsAABB(query)) return;

            while (outByDepth.Count <= depth)
                outByDepth.Add(new List<AABBNode<int>>(64));

            outByDepth[depth].Add(node);

            if (node.IsLeaf) return;

            CollectHitNodesByDepth(node.left, query, depth + 1, outByDepth);
            CollectHitNodesByDepth(node.right, query, depth + 1, outByDepth);
        }

        private void BuildParticleQueries(CollidersGroup group, float extra, List<AABB> outQueries)
        {
            var spheres = group.spheres;
            for (int i = 0; i < spheres.Length; i++)
                outQueries.Add(ColliderAABBUtil.FromSphere(spheres[i].collider, extra));

            var boxes = group.boxes;
            for (int i = 0; i < boxes.Length; i++)
                outQueries.Add(ColliderAABBUtil.FromBox(boxes[i].collider, extra));

            var capsules = group.capsules;
            for (int i = 0; i < capsules.Length; i++)
                outQueries.Add(ColliderAABBUtil.FromCapsule(capsules[i].collider, extra));
        }

        private void BuildTriangleQueries(CollidersGroup group, float extra, List<AABB> outQueries)
        {
            var capsules = group.capsules;
            for (int i = 0; i < capsules.Length; i++)
            {
                var capsule = capsules[i].collider;
                if (capsule.hasNeedleTip == 0)
                    continue;

                outQueries.Add(ColliderAABBUtil.FromNeedleTipProbe(capsule, extra));
            }
        }

        private void ApplyQuerySelection(List<AABB> queries)
        {
            if (!useFirstColliderOnly || queries.Count <= 1)
                return;

            int idx = Mathf.Clamp(firstColliderIndex, 0, queries.Count - 1);
            var q = queries[idx];
            queries.Clear();
            queries.Add(q);
        }

        private void UpdateAutoReveal(AABB currentQuery, bool particleMode)
        {
            if (!autoReveal)
                return;

            if (particleMode)
            {
                bool changed = !_hasLastParticleQuery || !ApproximatelyAABB(currentQuery, _lastParticleQuery, 1e-6f);
                if (changed)
                {
                    revealDepth = 0;
                    _autoTimerParticle = 0f;
                    _lastParticleQuery = currentQuery;
                    _hasLastParticleQuery = true;
                }
                else
                {
                    _autoTimerParticle += Time.deltaTime;
                    if (_autoTimerParticle >= autoRevealInterval)
                    {
                        _autoTimerParticle = 0f;
                        revealDepth = Mathf.Min(revealDepth + 1, autoRevealMaxDepth);
                    }
                }
            }
            else
            {
                bool changed = !_hasLastTriangleQuery || !ApproximatelyAABB(currentQuery, _lastTriangleQuery, 1e-6f);
                if (changed)
                {
                    revealDepth = 0;
                    _autoTimerTriangle = 0f;
                    _lastTriangleQuery = currentQuery;
                    _hasLastTriangleQuery = true;
                }
                else
                {
                    _autoTimerTriangle += Time.deltaTime;
                    if (_autoTimerTriangle >= autoRevealInterval)
                    {
                        _autoTimerTriangle = 0f;
                        revealDepth = Mathf.Min(revealDepth + 1, autoRevealMaxDepth);
                    }
                }
            }
        }

        private void DrawWireAABB(AABB aabb)
        {
            Vector3 center = (aabb.Min + aabb.Max) * 0.5f;
            Vector3 size = (aabb.Max - aabb.Min);
            Gizmos.DrawWireCube(center, size);
        }

        private Color ColorForDepth(int depth, bool isLeaf, Color nearColor, Color farColor)
        {
            float t = Mathf.Clamp01(depth / 12f);
            Color c = Color.Lerp(nearColor, farColor, t);
            if (isLeaf)
                c = Color.Lerp(c, Color.white, 0.15f);
            return c;
        }

        private bool ApproximatelyAABB(AABB a, AABB b, float eps)
        {
            return (a.Min - b.Min).sqrMagnitude < eps * eps &&
                   (a.Max - b.Max).sqrMagnitude < eps * eps;
        }

        private bool TryGetSelectedTargetTraceRecord(
            ClothSimulationDriverDoubleLayer driver,
            out ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord record,
            out int selectedIndex,
            out int totalCount)
        {
            record = default;
            selectedIndex = -1;
            totalCount = 0;
            if (driver == null)
                return false;

            var records = driver.IncisionTargetTraceDebugRecords;
            if (records == null || records.Count == 0)
                return false;

            totalCount = records.Count;
            selectedIndex = Mathf.Clamp(selectedTargetTraceFocusIndex, 0, totalCount - 1);
            record = records[selectedIndex];
            return true;
        }

        private bool IsIncisionRemovedTriangleKind(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedDegenerateArea ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedDegenerateEdge ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedBridge ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior;
        }

        private bool IsIncisionTargetTraceTriangleKind(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.TargetTraceFocus ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.TargetTraceNeighbor;
        }

        private bool IsIncisionCorridorFamilyTriangleKind(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.CorridorCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.MiddleBandCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.StripInteriorCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.BridgeCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedBridge ||
                   IsIncisionTargetTraceTriangleKind(kind);
        }

        private bool IsTriangleSelectedFocusTriangle(
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord selectedFocusRecord)
        {
            if (tri.triIndex >= 0 && selectedFocusRecord.triIndex >= 0 && tri.triIndex == selectedFocusRecord.triIndex && tri.layer == selectedFocusRecord.layer)
                return true;

            return tri.layer == selectedFocusRecord.layer &&
                   tri.ia == selectedFocusRecord.ia &&
                   tri.ib == selectedFocusRecord.ib &&
                   tri.ic == selectedFocusRecord.ic;
        }

        private bool SharesVertexWithSelectedFocus(
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord selectedFocusRecord)
        {
            if (tri.layer != selectedFocusRecord.layer)
                return false;

            int a = tri.ia;
            int b = tri.ib;
            int c = tri.ic;
            return a == selectedFocusRecord.ia || a == selectedFocusRecord.ib || a == selectedFocusRecord.ic ||
                   b == selectedFocusRecord.ia || b == selectedFocusRecord.ib || b == selectedFocusRecord.ic ||
                   c == selectedFocusRecord.ia || c == selectedFocusRecord.ib || c == selectedFocusRecord.ic;
        }

        private bool IsTriangleWithinSelectedFocusNeighborhood(
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            bool hasSelectedFocus,
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord selectedFocusRecord,
            float neighborRadiusSq)
        {
            var kind = (ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind)tri.kind;
            if (!hasSelectedFocus)
                return IsIncisionTargetTraceTriangleKind(kind);

            if (IsTriangleSelectedFocusTriangle(tri, selectedFocusRecord))
                return true;

            if (SharesVertexWithSelectedFocus(tri, selectedFocusRecord))
                return true;

            if ((tri.center - selectedFocusRecord.center).sqrMagnitude <= neighborRadiusSq)
                return true;

            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.TargetTraceNeighbor;
        }

        private bool MatchesStageFamily(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            switch (incisionTriangleStageFilter)
            {
                case IncisionTriangleStageFilter.CorridorFamily:
                    return IsIncisionCorridorFamilyTriangleKind(kind);
                case IncisionTriangleStageFilter.MiddleBandOnly:
                    return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.MiddleBandCandidate;
                case IncisionTriangleStageFilter.StripInteriorOnly:
                    return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.StripInteriorCandidate ||
                           kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior;
                case IncisionTriangleStageFilter.RemovedOnly:
                    return IsIncisionRemovedTriangleKind(kind);
                case IncisionTriangleStageFilter.TargetTraceOnly:
                    return IsIncisionTargetTraceTriangleKind(kind);
                case IncisionTriangleStageFilter.FocusOnly:
                    return true;
                default:
                    return true;
            }
        }

        private bool ShouldDrawIncisionTriangleByStageFilter(
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            bool hasSelectedFocus,
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord selectedFocusRecord,
            float neighborRadiusSq)
        {
            var kind = (ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind)tri.kind;
            bool matchesStage = MatchesStageFamily(kind);
            if (!matchesStage)
                return false;

            if (incisionTriangleStageFilter == IncisionTriangleStageFilter.FocusOnly)
                return IsTriangleWithinSelectedFocusNeighborhood(tri, hasSelectedFocus, selectedFocusRecord, neighborRadiusSq);

            if (drawOnlySelectedFocusNeighborhood && IsIncisionTargetTraceTriangleKind(kind))
                return IsTriangleWithinSelectedFocusNeighborhood(tri, hasSelectedFocus, selectedFocusRecord, neighborRadiusSq);

            return true;
        }

        private struct PickedRuntimeMeshTriangle
        {
            public Vector3 a;
            public Vector3 b;
            public Vector3 c;
            public Vector3 center;
            public int layer;
            public int triIndex;
            public int ia;
            public int ib;
            public int ic;
        }

        private bool IsSameIncisionTriangle(
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            int layer,
            int triIndex,
            int ia,
            int ib,
            int ic)
        {
            if (tri.layer != layer)
                return false;

            if (tri.triIndex >= 0 && triIndex >= 0)
                return tri.triIndex == triIndex;

            return tri.ia == ia && tri.ib == ib && tri.ic == ic;
        }

        private bool TryGetPickedIncisionTriangle(
            ClothSimulationDriverDoubleLayer driver,
            out PickedRuntimeMeshTriangle pickedTriangle)
        {
            pickedTriangle = default;
            if (driver == null)
                return false;

            MeshFilter meshFilter = pickedIncisionTriangleLayer == (int)ClothSimulationDriverDoubleLayer.VesselLayer.Inner
                ? driver.innerMeshFilter
                : driver.outerMeshFilter;
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return false;

            Mesh mesh = meshFilter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || triangles == null || triangles.Length < 3)
                return false;

            int baseIndex = pickedIncisionTriangleIndex * 3;
            int ia = -1;
            int ib = -1;
            int ic = -1;
            int triIndex = -1;

            if (pickedIncisionTriangleIndex >= 0 && baseIndex >= 0 && baseIndex + 2 < triangles.Length)
            {
                triIndex = pickedIncisionTriangleIndex;
                ia = triangles[baseIndex];
                ib = triangles[baseIndex + 1];
                ic = triangles[baseIndex + 2];
            }
            else if (pickedIncisionTriangleIa >= 0 && pickedIncisionTriangleIb >= 0 && pickedIncisionTriangleIc >= 0)
            {
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    if (triangles[t] == pickedIncisionTriangleIa && triangles[t + 1] == pickedIncisionTriangleIb && triangles[t + 2] == pickedIncisionTriangleIc)
                    {
                        triIndex = t / 3;
                        ia = triangles[t];
                        ib = triangles[t + 1];
                        ic = triangles[t + 2];
                        break;
                    }
                }
            }

            if (triIndex < 0 || ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                return false;

            Transform tf = meshFilter.transform;
            Vector3 a = tf.TransformPoint(vertices[ia]);
            Vector3 b = tf.TransformPoint(vertices[ib]);
            Vector3 c = tf.TransformPoint(vertices[ic]);
            pickedTriangle = new PickedRuntimeMeshTriangle
            {
                a = a,
                b = b,
                c = c,
                center = (a + b + c) / 3f,
                layer = pickedIncisionTriangleLayer,
                triIndex = triIndex,
                ia = ia,
                ib = ib,
                ic = ic,
            };
            return true;
        }

        private bool TryGetPickedIncisionTriangleTraceRecord(
            ClothSimulationDriverDoubleLayer driver,
            PickedRuntimeMeshTriangle tri,
            out ClothSimulationDriverDoubleLayer.IncisionFinalTriangleAuditRecord record)
        {
            record = default;
            if (driver == null)
                return false;

            var allRecords = driver.IncisionFinalTriangleAuditRecords;
            if (allRecords == null || allRecords.Count == 0)
                return false;

            for (int i = 0; i < allRecords.Count; i++)
            {
                var rec = allRecords[i];
                if (rec.layer != tri.layer)
                    continue;

                bool triMatch = (tri.triIndex >= 0 && rec.triIndex >= 0 && tri.triIndex == rec.triIndex) ||
                                (tri.ia == rec.ia && tri.ib == rec.ib && tri.ic == rec.ic);
                if (triMatch)
                {
                    record = rec;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetLayerIncisionSummary(
            ClothSimulationDriverDoubleLayer driver,
            int layer,
            out ClothSimulationDriverDoubleLayer.IncisionDeleteDebugSummary summary)
        {
            summary = default;
            if (driver == null)
                return false;

            var summaries = driver.IncisionDeleteDebugSummaries;
            if (summaries == null || summaries.Count == 0)
                return false;

            for (int i = summaries.Count - 1; i >= 0; i--)
            {
                var s = summaries[i];
                if (s.layer == layer)
                {
                    summary = s;
                    return true;
                }
            }
            return false;
        }

        private string BuildIncisionSummaryLedgerText(ClothSimulationDriverDoubleLayer.IncisionDeleteDebugSummary s)
        {
            string layerText = s.layer == 0 ? "Outer" : (s.layer == 1 ? "Inner" : "Global");
            string head = $"{layerText} success={(s.success ? 1 : 0)} stage={s.failStage}";
            string entry = $"stroke={s.strokePointCount} guideLen={s.guideLength:F5} open={s.openWidth:F5} sub={s.localSubdivisionPasses} avgEdge={s.avgTriangleEdgeLength:F5}";
            string chain = $"masterPathVerts={s.pathCount} masterLen={s.pathLength:F5} centerCurvePts={s.centerlineCount} stations={s.denseStationsCount}";
            string patch = $"patchTri={s.preparedTriangleCount} patchComp={s.patchComponentCount} excisionTri={s.removedCount} excisionComp={s.excisionComponentCount}";
            string strip = $"rows={s.stripRowCount} supportQuads={s.supportQuadCount} excisionQuads={s.excisionQuadCount} boundaryViol={s.boundaryContinuityViolationCount}";
            string quality = $"realizedOpen={s.averageRealizedOpenWidth:F5} maxAspect={s.maxPatchTriangleAspect:F2} taper={s.endTaperLengthWorld:F5} fallback={(s.usedFallbackPath ? 1 : 0)}";
            string reason = string.IsNullOrEmpty(s.failReason) ? string.Empty : ("\nreason=" + s.failReason);
            return head + "\n" + entry + "\n" + chain + "\n" + patch + "\n" + strip + "\n" + quality + reason;
        }

        private string BuildPickedIncisionTriangleLedgerText(
            PickedRuntimeMeshTriangle tri,
            bool hasTraceRecord,
            ClothSimulationDriverDoubleLayer.IncisionFinalTriangleAuditRecord traceRecord,
            bool hasLayerSummary,
            ClothSimulationDriverDoubleLayer.IncisionDeleteDebugSummary layerSummary)
        {
            string head = $"Picked runtime tri#{tri.triIndex} L{tri.layer}\nverts=[{tri.ia},{tri.ib},{tri.ic}]";
            string summaryText = hasLayerSummary ? ("\n" + BuildIncisionSummaryLedgerText(layerSummary)) : string.Empty;
            if (!hasTraceRecord)
                return head + "\nno final-triangle-audit record" + summaryText;

            return head + "\n" +
                   $"area3D={traceRecord.area3D:F6} area2D={traceRecord.area2D:F6}\n" +
                   $"ribbon frame={traceRecord.nearestRibbonFrameIndex} lat3D={traceRecord.signedLateral3D:F5} norm3D={traceRecord.normalizedLateral3D:F3} hw3D={traceRecord.halfWidth3D:F5} gap3D={traceRecord.ribbonGap3D:F5} surf3D={traceRecord.ribbonSurfaceOffset3D:F5}\n" +
                   $"flags overlap3D={(traceRecord.overlapRibbon3D ? 1 : 0)} band3D={(traceRecord.insideRibbonBand3D ? 1 : 0)} core3D={(traceRecord.insideRibbonCore3D ? 1 : 0)} removed={(traceRecord.removedCandidate ? 1 : 0)}\n" +
                   $"ref2D dist={traceRecord.centerlineDistance2D:F5} hits corridor={traceRecord.corridorHits} middle={traceRecord.centralHits}\n" +
                   traceRecord.failHint + summaryText;
        }

        private string BuildSelectedTargetTraceLedgerText(
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord record,
            int selectedIndex,
            int totalCount)
        {
            return $"Focus {selectedIndex + 1}/{Mathf.Max(1, totalCount)}  tri#{record.triIndex} L{record.layer}\n" +
                   $"verts=[{record.ia},{record.ib},{record.ic}]  score={record.score:F2}\n" +
                   $"area2D={record.area2D:F6}  dist={record.centerlineDistance2D:F5}\n" +
                   $"hits corridor={record.corridorHits}  middle={record.centralHits}\n" +
                   $"flags overlap={(record.overlapCandidate ? 1 : 0)} strict={(record.strictCandidate ? 1 : 0)} core={(record.coreCandidate ? 1 : 0)}\n" +
                   $"flags corridor={(record.corridorCandidate ? 1 : 0)} bridge={(record.bridgeCandidate ? 1 : 0)} middle={(record.middleBandCandidate ? 1 : 0)} strip={(record.stripInteriorCandidate ? 1 : 0)} removed={(record.removedCandidate ? 1 : 0)}\n" +
                   record.failHint;
        }

#if UNITY_EDITOR
        public void LogPickedIncisionTriangleToConsoleNow()
        {
            if (!logPickedIncisionTriangleToConsole)
                return;

            ClothSimulationDriverDoubleLayer driver = simulationDouble != null ? simulationDouble : FindObjectOfType<ClothSimulationDriverDoubleLayer>();
            if (driver == null)
                return;

            if (!TryGetPickedIncisionTriangle(driver, out var tri))
                return;

            bool hasTraceRecord = TryGetPickedIncisionTriangleTraceRecord(driver, tri, out var traceRecord);
            bool hasLayerSummary = TryGetLayerIncisionSummary(driver, tri.layer, out var layerSummary);
            string text = BuildPickedIncisionTriangleLedgerText(tri, hasTraceRecord, traceRecord, hasLayerSummary, layerSummary);
            Debug.Log("[Incision][PickedTriangle]\n" + text, this);
        }
#endif

        private void DrawIncisionDebug(ClothSimulationDriverDoubleLayer driver)
        {
            if (driver == null) return;

            float pointRadius = Mathf.Max(incisionPointRadius, 0.00035f);

            if (drawIncisionTargetStroke)
                DrawIncisionPolyline(driver.IncisionTargetStrokeWorld, incisionTargetStrokeColor, pointRadius * 0.8f, false);

            if (drawIncisionMasterChain && !drawIncisionPrecommitGeometry)
                DrawIncisionPolyline(driver.IncisionMasterChainWorld, incisionMasterChainColor, pointRadius * 0.9f, true);

            if (drawIncisionCenterCurve && !drawIncisionPrecommitGeometry)
                DrawIncisionPolyline(driver.IncisionCenterCurveWorld, incisionCenterlineColor, pointRadius * 0.85f, true);

            if (drawIncisionPrecommitGeometry)
            {
                float precommitRadius = Mathf.Max(precommitPointRadius, pointRadius * 0.55f);
                bool drawInnerPrecommit = drawIncisionInnerBoundary;
                Color innerRowColor = WithAlpha(precommitRowCenterColor, 0.45f);
                Color innerVisibleColor = WithAlpha(precommitVisibleBoundaryColor, 0.45f);
                Color innerSupportColor = WithAlpha(precommitSupportBoundaryColor, 0.45f);
                Color innerFailedColor = WithAlpha(precommitFailedInterfaceEdgeColor, 0.55f);

                if (drawPrecommitMasterChain)
                {
                    DrawPrecommitPolyline(driver.IncisionMasterChainWorld, precommitMasterChainColor, precommitMasterChainWidth, precommitRadius * 0.85f, "MC", 1f, drawPrecommitPoints, drawPrecommitLabels);
                }

                if (drawPrecommitCenterCurve)
                {
                    DrawPrecommitPolyline(driver.IncisionCenterCurveWorld, precommitCenterCurveColor, precommitCenterCurveWidth, 0f, null, 2f, false, false);
                    if (drawPrecommitPoints)
                        DrawPrecommitPolyline(driver.RuntimePrecommitRowCentersOuter, precommitRowCenterColor, 0f, precommitRadius, "C", 2.4f, true, drawPrecommitLabels);
                    else if (drawPrecommitLabels)
                        DrawPrecommitPolyline(driver.RuntimePrecommitRowCentersOuter, precommitRowCenterColor, 0f, 0f, "C", 2.4f, false, true);

                    if (drawInnerPrecommit)
                        DrawPrecommitPolyline(driver.RuntimePrecommitRowCentersInner, innerRowColor, 0f, precommitRadius * 0.8f, "IC", 2.8f, drawPrecommitPoints, drawPrecommitLabels);
                }

                if (drawPrecommitVisibleBoundaryChains)
                {
                    DrawPrecommitPolyline(driver.RuntimePrecommitLeftBoundaryOuter, precommitVisibleBoundaryColor, precommitVisibleBoundaryWidth, precommitRadius * 0.95f, "L", 3f, drawPrecommitPoints, drawPrecommitLabels);
                    DrawPrecommitPolyline(driver.RuntimePrecommitRightBoundaryOuter, precommitVisibleBoundaryColor, precommitVisibleBoundaryWidth, precommitRadius * 0.95f, "R", 3.2f, drawPrecommitPoints, drawPrecommitLabels);
                    if (drawInnerPrecommit)
                    {
                        DrawPrecommitPolyline(driver.RuntimePrecommitLeftBoundaryInner, innerVisibleColor, Mathf.Max(1f, precommitVisibleBoundaryWidth - 2f), precommitRadius * 0.8f, "IL", 3.45f, drawPrecommitPoints, drawPrecommitLabels);
                        DrawPrecommitPolyline(driver.RuntimePrecommitRightBoundaryInner, innerVisibleColor, Mathf.Max(1f, precommitVisibleBoundaryWidth - 2f), precommitRadius * 0.8f, "IR", 3.65f, drawPrecommitPoints, drawPrecommitLabels);
                    }
                }

                if (drawPrecommitSupportBoundaryChains)
                {
                    DrawPrecommitPolyline(driver.RuntimePrecommitLeftSupportOuter, precommitSupportBoundaryColor, precommitSupportBoundaryWidth, precommitRadius * 0.72f, "LS", 4f, drawPrecommitPoints, drawPrecommitLabels);
                    DrawPrecommitPolyline(driver.RuntimePrecommitRightSupportOuter, precommitSupportBoundaryColor, precommitSupportBoundaryWidth, precommitRadius * 0.72f, "RS", 4.2f, drawPrecommitPoints, drawPrecommitLabels);
                    if (drawInnerPrecommit)
                    {
                        DrawPrecommitPolyline(driver.RuntimePrecommitLeftSupportInner, innerSupportColor, Mathf.Max(1f, precommitSupportBoundaryWidth - 1f), precommitRadius * 0.6f, "ILS", 4.45f, drawPrecommitPoints, drawPrecommitLabels);
                        DrawPrecommitPolyline(driver.RuntimePrecommitRightSupportInner, innerSupportColor, Mathf.Max(1f, precommitSupportBoundaryWidth - 1f), precommitRadius * 0.6f, "IRS", 4.65f, drawPrecommitPoints, drawPrecommitLabels);
                    }
                }

                if (drawPrecommitFailedInterfaceEdges)
                {
                    DrawPrecommitLinePairs(driver.RuntimePrecommitFailedInterfaceEdgesOuter, driver.RuntimePrecommitFailedInterfaceEdgeLabelsOuter, precommitFailedInterfaceEdgeColor, precommitFailedEdgeWidth, precommitRadius * 0.6f, "FE", 5f, drawPrecommitPoints, drawPrecommitLabels);
                    if (drawInnerPrecommit)
                        DrawPrecommitLinePairs(driver.RuntimePrecommitFailedInterfaceEdgesInner, driver.RuntimePrecommitFailedInterfaceEdgeLabelsInner, innerFailedColor, Mathf.Max(1f, precommitFailedEdgeWidth - 1f), precommitRadius * 0.5f, "IFE", 5.4f, drawPrecommitPoints, drawPrecommitLabels);
                }
            }

            if (drawIncisionBoundaries)
            {
                DrawIncisionPolyline(driver.IncisionOuterLeftBoundaryWorld, incisionOuterLeftBoundaryColor, pointRadius * 0.75f, true);
                DrawIncisionPolyline(driver.IncisionOuterRightBoundaryWorld, incisionOuterRightBoundaryColor, pointRadius * 0.75f, true);

                if (drawIncisionInnerBoundary)
                {
                    DrawIncisionPolyline(driver.IncisionInnerLeftBoundaryWorld, incisionInnerLeftBoundaryColor, pointRadius * 0.68f, true);
                    DrawIncisionPolyline(driver.IncisionInnerRightBoundaryWorld, incisionInnerRightBoundaryColor, pointRadius * 0.68f, true);
                }
            }

            if (driver.drawSutureSideGuide && driver.HasSutureSideGuides)
            {
                DrawIncisionPolyline(driver.SutureSideAOuterBoundaryWorld, driver.sutureSideAColor, pointRadius * 0.9f, true);
                DrawIncisionPolyline(driver.SutureSideBOuterBoundaryWorld, driver.sutureSideBColor, pointRadius * 0.9f, true);
                if (drawIncisionInnerBoundary)
                {
                    DrawIncisionPolyline(driver.SutureSideAInnerBoundaryWorld, WithAlpha(driver.sutureSideAColor, 0.65f), pointRadius * 0.75f, true);
                    DrawIncisionPolyline(driver.SutureSideBInnerBoundaryWorld, WithAlpha(driver.sutureSideBColor, 0.65f), pointRadius * 0.75f, true);
                }
#if UNITY_EDITOR
                UnityEditor.Handles.Label(driver.SutureSideAGuideCenterWorld, driver.GetSutureSideDisplayName(ClothSimulationDriverDoubleLayer.SutureVesselSide.SideA));
                UnityEditor.Handles.Label(driver.SutureSideBGuideCenterWorld, driver.GetSutureSideDisplayName(ClothSimulationDriverDoubleLayer.SutureVesselSide.SideB));
                if (drawIncisionLabels && !string.IsNullOrEmpty(driver.SutureSideGuideSummary))
                    UnityEditor.Handles.Label(driver.SutureSideAGuideCenterWorld + Vector3.up * (pointRadius * 4f), driver.SutureSideGuideSummary);
#endif
            }

            if (driver.drawSuturePassSequenceGuide && driver.HasSuturePassSequence)
                DrawSuturePassSequenceGuides(driver);

            if (drawIncisionStations)
            {
                DrawIncisionStationPoints(driver.IncisionOuterStationCentersWorld, incisionStationOuterColor, pointRadius * 0.7f, "O");
                if (drawIncisionInnerBoundary)
                    DrawIncisionStationPoints(driver.IncisionInnerStationCentersWorld, incisionStationInnerColor, pointRadius * 0.6f, "I");
            }

            var triangles = driver.IncisionDebugTriangles;
            if (triangles != null)
            {
                for (int i = 0; i < triangles.Count; i++)
                {
                    var tri = triangles[i];
                    bool isExcision = tri.kind == (int)ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior;
                    bool isPatch = tri.kind == (int)ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.CutPatch;
                    if (!isPatch && !isExcision)
                        continue;
                    if (isPatch && !drawIncisionPatchRegion)
                        continue;
                    if (isExcision && !drawIncisionExcisionRegion)
                        continue;

                    Color color = isExcision ? incisionRemovedStripInteriorColor : incisionPatchCutColor;
#if UNITY_EDITOR
                    UnityEditor.Handles.color = new Color(color.r, color.g, color.b, isExcision ? 0.18f : 0.08f);
                    UnityEditor.Handles.DrawAAConvexPolygon(tri.a, tri.b, tri.c);
#endif
                    Gizmos.color = color;
                    Gizmos.DrawLine(tri.a, tri.b);
                    Gizmos.DrawLine(tri.b, tri.c);
                    Gizmos.DrawLine(tri.c, tri.a);

                    if (drawIncisionLabels)
                    {
#if UNITY_EDITOR
                        string triText = isExcision ? "ExcisionRegion" : "Patch";
                        UnityEditor.Handles.Label(tri.center, $"{triText} L{tri.layer}");
#endif
                    }
                }
            }

#if UNITY_EDITOR
            if (drawIncisionLayerSummaries)
            {
                var summaries = driver.IncisionDeleteDebugSummaries;
                if (summaries != null)
                {
                    for (int i = 0; i < summaries.Count; i++)
                    {
                        var summary = summaries[i];
                        if (!TryGetIncisionSummaryAnchor(driver, summary.layer, out Vector3 anchor))
                            continue;

                        Color c = summary.layer == 1 ? incisionDeleteSummaryInnerColor : incisionDeleteSummaryOuterColor;
                        Gizmos.color = c;
                        Gizmos.DrawSphere(anchor, pointRadius * 1.05f);
                        UnityEditor.Handles.Label(
                            anchor + Vector3.up * Mathf.Max(pointRadius * 2.2f, 0.0022f),
                            BuildIncisionSummaryLedgerText(summary));
                    }
                }
            }

            if (TryGetPickedIncisionTriangle(driver, out var pickedTriangle))
            {
                if (highlightPickedIncisionTriangle)
                {
                    UnityEditor.Handles.color = pickedIncisionTriangleFillColor;
                    UnityEditor.Handles.DrawAAConvexPolygon(pickedTriangle.a, pickedTriangle.b, pickedTriangle.c);
                }

                Gizmos.color = pickedIncisionTriangleColor;
                Gizmos.DrawLine(pickedTriangle.a, pickedTriangle.b);
                Gizmos.DrawLine(pickedTriangle.b, pickedTriangle.c);
                Gizmos.DrawLine(pickedTriangle.c, pickedTriangle.a);
                Gizmos.DrawSphere(pickedTriangle.center, pointRadius * 1.15f);

                if (showPickedIncisionTriangleLedger)
                {
                    bool hasLayerSummary = TryGetLayerIncisionSummary(driver, pickedTriangle.layer, out var pickedLayerSummary);
                    UnityEditor.Handles.Label(
                        pickedTriangle.center + Vector3.up * Mathf.Max(pickedIncisionTriangleLedgerYOffset, pointRadius * 2.8f),
                        hasLayerSummary
                            ? BuildPickedIncisionTriangleLedgerText(pickedTriangle, false, default, true, pickedLayerSummary)
                            : $"Picked runtime tri#{pickedTriangle.triIndex} L{pickedTriangle.layer}");
                }
            }
#endif
        }

        private void DrawIncisionPolyline(IReadOnlyList<Vector3> points, Color color, float sphereRadius, bool drawPointDots)
        {
            if (points == null || points.Count == 0)
                return;

            Gizmos.color = color;
            for (int i = 0; i < points.Count - 1; i++)
                Gizmos.DrawLine(points[i], points[i + 1]);

            if (!drawPointDots)
                return;

            for (int i = 0; i < points.Count; i++)
                Gizmos.DrawSphere(points[i], sphereRadius);
        }

        private void DrawIncisionStationPoints(IReadOnlyList<Vector3> points, Color color, float radius, string prefix)
        {
            if (points == null)
                return;

            Gizmos.color = color;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                Gizmos.DrawSphere(p, radius);
#if UNITY_EDITOR
                if (drawIncisionLabels)
                    UnityEditor.Handles.Label(p, $"{prefix}S{i}");
#endif
            }
        }

        private void DrawPrecommitPolyline(
            IReadOnlyList<Vector3> points,
            Color color,
            float lineWidth,
            float sphereRadius,
            string labelPrefix,
            float liftMultiplier,
            bool drawPointDots,
            bool drawLabels)
        {
            if (points == null || points.Count == 0)
                return;

            Vector3[] lifted = BuildPrecommitLiftedPoints(points, liftMultiplier);
#if UNITY_EDITOR
            CompareFunction previousZTest = UnityEditor.Handles.zTest;
            Color previousColor = UnityEditor.Handles.color;
            UnityEditor.Handles.zTest = CompareFunction.Always;
            UnityEditor.Handles.color = color;
            if (lineWidth > 0f && lifted.Length >= 2)
                UnityEditor.Handles.DrawAAPolyLine(lineWidth, lifted);
            UnityEditor.Handles.color = previousColor;
            UnityEditor.Handles.zTest = previousZTest;
#else
            if (lineWidth > 0f)
            {
                Gizmos.color = color;
                for (int i = 0; i < lifted.Length - 1; i++)
                    Gizmos.DrawLine(lifted[i], lifted[i + 1]);
            }
#endif

            if (drawPointDots && sphereRadius > 0f)
            {
                Gizmos.color = color;
                for (int i = 0; i < lifted.Length; i++)
                {
                    if (ShouldDrawChainSample(i, lifted.Length, maxPrecommitPointsPerChainDuringPlay))
                        Gizmos.DrawSphere(lifted[i], sphereRadius);
                }
            }

#if UNITY_EDITOR
            if (drawLabels && !string.IsNullOrEmpty(labelPrefix))
            {
                for (int i = 0; i < lifted.Length; i++)
                {
                    if (ShouldDrawChainSample(i, lifted.Length, maxPrecommitLabelsPerChainDuringPlay))
                        DrawPrecommitLabel(lifted[i], $"{labelPrefix}{i}", color);
                }
            }
#endif
        }

        private int GetPlayModeDrawStride(int itemCount, int playModeBudget)
        {
            if (!Application.isPlaying)
                return 1;
            if (playModeBudget <= 0)
                return int.MaxValue;

            return Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)playModeBudget));
        }

        private bool ShouldDrawPlayModeSample(int index, int stride, int playModeBudget)
        {
            if (!Application.isPlaying)
                return true;
            if (playModeBudget <= 0 || stride == int.MaxValue)
                return false;

            return index % Mathf.Max(1, stride) == 0;
        }

        private bool ShouldDrawChainSample(int index, int itemCount, int playModeBudget)
        {
            if (!Application.isPlaying || itemCount <= playModeBudget)
                return true;
            if (index == 0 || index == itemCount - 1)
                return true;

            int interiorBudget = Mathf.Max(0, playModeBudget - 2);
            if (interiorBudget == 0)
                return false;

            int interiorCount = Mathf.Max(1, itemCount - 2);
            int stride = Mathf.Max(1, Mathf.CeilToInt(interiorCount / (float)interiorBudget));
            return (index - 1) % stride == 0;
        }

        private void DrawPrecommitLinePairs(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<string> labels,
            Color color,
            float lineWidth,
            float radius,
            string labelPrefix,
            float liftMultiplier,
            bool drawPointDots,
            bool drawLabels)
        {
            if (points == null || points.Count < 2)
                return;

            Vector3 lift = GetPrecommitLiftDirection() * (precommitLiftOffset * liftMultiplier);
#if UNITY_EDITOR
            CompareFunction previousZTest = UnityEditor.Handles.zTest;
            Color previousColor = UnityEditor.Handles.color;
            UnityEditor.Handles.zTest = CompareFunction.Always;
            UnityEditor.Handles.color = color;
#endif
            Gizmos.color = color;
            for (int i = 0; i + 1 < points.Count; i += 2)
            {
                int labelIndex = i / 2;
                string debugLabel =
                    labels != null &&
                    labelIndex >= 0 &&
                    labelIndex < labels.Count &&
                    !string.IsNullOrEmpty(labels[labelIndex])
                        ? labels[labelIndex]
                        : null;
                if (!ShouldDrawPrecommitFailedInterfaceEdge(debugLabel))
                    continue;

                Vector3 a = points[i] + lift;
                Vector3 b = points[i + 1] + lift;
#if UNITY_EDITOR
                if (lineWidth > 0f)
                    UnityEditor.Handles.DrawAAPolyLine(lineWidth, new Vector3[] { a, b });
#else
                Gizmos.DrawLine(a, b);
#endif
                if (drawPointDots && radius > 0f)
                {
                    Gizmos.DrawSphere(a, radius);
                    Gizmos.DrawSphere(b, radius);
                }
#if UNITY_EDITOR
                if (drawLabels && !string.IsNullOrEmpty(labelPrefix))
                {
                    Vector3 midpoint = (a + b) * 0.5f;
                    string labelText =
                        !string.IsNullOrEmpty(debugLabel)
                            ? debugLabel
                            : $"{labelPrefix}{labelIndex}";
                    DrawPrecommitLabel(midpoint, labelText, color);
                }
#endif
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color = previousColor;
            UnityEditor.Handles.zTest = previousZTest;
#endif
        }

        private bool ShouldDrawPrecommitFailedInterfaceEdge(string label)
        {
            if (precommitFailedInterfaceEdgeFilter == PrecommitFailedInterfaceEdgeFilter.All || string.IsNullOrEmpty(label))
                return true;

            switch (precommitFailedInterfaceEdgeFilter)
            {
                case PrecommitFailedInterfaceEdgeFilter.SxOnly:
                    return label.Contains(" SX>");
                case PrecommitFailedInterfaceEdgeFilter.OsOnly:
                    return label.Contains(" OS>");
                case PrecommitFailedInterfaceEdgeFilter.LbRbOnly:
                    return label.Contains(" LB") || label.Contains(" RB");
                case PrecommitFailedInterfaceEdgeFilter.LsRsOnly:
                    return label.Contains(" LS") || label.Contains(" RS");
                case PrecommitFailedInterfaceEdgeFilter.TipOnly:
                    return label.Contains("Tip");
                default:
                    return true;
            }
        }

        private Vector3[] BuildPrecommitLiftedPoints(IReadOnlyList<Vector3> points, float liftMultiplier)
        {
            Vector3[] lifted = new Vector3[points.Count];
            Vector3 lift = GetPrecommitLiftDirection() * (precommitLiftOffset * liftMultiplier);
            for (int i = 0; i < points.Count; i++)
                lifted[i] = points[i] + lift;
            return lifted;
        }

        private Vector3 GetPrecommitLiftDirection()
        {
            Vector3 direction = Vector3.up;
#if UNITY_EDITOR
            Camera sceneCamera = UnityEditor.SceneView.currentDrawingSceneView != null
                ? UnityEditor.SceneView.currentDrawingSceneView.camera
                : null;
            if (sceneCamera != null)
                direction = -sceneCamera.transform.forward;
            else if (Camera.current != null)
                direction = -Camera.current.transform.forward;
#else
            if (Camera.current != null)
                direction = -Camera.current.transform.forward;
#endif
            if (direction.sqrMagnitude <= 1e-8f)
                direction = Vector3.up;
            return direction.normalized;
        }

#if UNITY_EDITOR
        private static void DrawPrecommitLabel(Vector3 position, string text, Color color)
        {
            GUIStyle style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            style.normal.textColor = color;
            style.fontSize = 11;
            UnityEditor.Handles.Label(position, text, style);
        }
#endif

        private void DrawSuturePassSequenceGuides(ClothSimulationDriverDoubleLayer driver)
        {
            IReadOnlyList<ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord> records = driver.SuturePassSequenceRecords;
            if (records == null || records.Count == 0)
                return;

            float radius = Mathf.Max(0.0035f, portalPointRadius * 1.18f);
            for (int i = 0; i < records.Count; i++)
            {
                ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord record = records[i];
                bool violation = record.status == ClothSimulationDriverDoubleLayer.SutureSequenceStatus.WrongSide ||
                                 record.status == ClothSimulationDriverDoubleLayer.SutureSequenceStatus.WrongDirection ||
                                 record.status == ClothSimulationDriverDoubleLayer.SutureSequenceStatus.WrongSideAndDirection;
                Color color = violation
                    ? driver.sutureSequenceErrorColor
                    : (record.isCurrent ? driver.sutureCurrentPassColor : driver.sutureHistoryPassColor);

                Gizmos.color = color;
                if (record.isComplete)
                {
                    Gizmos.DrawLine(record.outerWorld, record.innerWorld);
                    Gizmos.DrawWireSphere(record.outerWorld, radius * 0.95f);
                    Gizmos.DrawWireSphere(record.innerWorld, radius * 0.95f);
                    Gizmos.DrawSphere(record.centerWorld, radius * 0.55f);
                }
                else
                {
                    Gizmos.DrawWireSphere(record.centerWorld, radius);
                }

#if UNITY_EDITOR
                DrawPrecommitLabel(record.centerWorld + Vector3.up * radius * (2.2f + 0.35f * i), record.label, color);
#endif
            }

#if UNITY_EDITOR
            if (driver.SutureExpectedNextSide != ClothSimulationDriverDoubleLayer.SutureVesselSide.Unknown)
            {
                ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord last = records[records.Count - 1];
                string expectedLabel = $"Next expected: {driver.GetSuturePassDirectionLabel(driver.SutureExpectedNextDirection)} {driver.GetSutureSideDisplayName(driver.SutureExpectedNextSide)}";
                DrawPrecommitLabel(last.centerWorld + Vector3.up * radius * 4.2f, expectedLabel, driver.sutureExpectedPassColor);
            }
#endif
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private bool TryGetIncisionSummaryAnchor(ClothSimulationDriverDoubleLayer driver, int layer, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            if (driver == null)
                return false;

            IReadOnlyList<Vector3> stationCenters = layer == 1 ? driver.IncisionInnerStationCentersWorld : driver.IncisionOuterStationCentersWorld;
            if (stationCenters != null && stationCenters.Count > 0)
            {
                anchor = stationCenters[Mathf.Clamp(stationCenters.Count / 2, 0, stationCenters.Count - 1)];
                return true;
            }

            IReadOnlyList<Vector3> centerCurve = driver.IncisionCenterCurveWorld;
            if (centerCurve != null && centerCurve.Count > 0)
            {
                anchor = centerCurve[Mathf.Clamp(centerCurve.Count / 2, 0, centerCurve.Count - 1)];
                return true;
            }

            IReadOnlyList<Vector3> boundary = layer == 1 ? driver.IncisionInnerLeftBoundaryWorld : driver.IncisionOuterLeftBoundaryWorld;
            if (boundary != null && boundary.Count > 0)
            {
                anchor = boundary[Mathf.Clamp(boundary.Count / 2, 0, boundary.Count - 1)];
                return true;
            }

            var master = driver.IncisionMasterChainWorld;
            if (master != null && master.Count > 0)
            {
                anchor = master[Mathf.Clamp(master.Count / 2, 0, master.Count - 1)];
                return true;
            }

            var target = driver.IncisionTargetStrokeWorld;
            if (target != null && target.Count > 0)
            {
                anchor = target[Mathf.Clamp(target.Count / 2, 0, target.Count - 1)];
                return true;
            }

            return false;
        }

        private bool IsLeftRightBoundaryInteriorTriangleKind(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.StripInteriorCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior;
        }

        private Color GetIncisionTriangleDebugColor(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            if (highlightLeftRightBoundaryInteriorAsYellow && IsLeftRightBoundaryInteriorTriangleKind(kind))
                return incisionLeftRightBoundaryInteriorColor;

            switch (kind)
            {
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.CrossedOriginal:
                    return incisionPatchCrossedColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RefinementPatch:
                    return incisionPatchRefinementColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.CutPatch:
                    return incisionPatchCutColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedDegenerateEdge:
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedDegenerateArea:
                    return incisionRemovedDegenerateColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedBridge:
                    return incisionRemovedBridgeColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.StrictCandidate:
                    return incisionStrictCandidateColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.BridgeCandidate:
                    return incisionBridgeCandidateColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.CoreCandidate:
                    return incisionCoreCandidateColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.CorridorCandidate:
                    return incisionCorridorCandidateColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.MiddleBandCandidate:
                    return incisionMiddleBandCandidateColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.StripInteriorCandidate:
                    return incisionStripInteriorCandidateColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior:
                    return incisionRemovedStripInteriorColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.PathCarrier:
                    return new Color(0.15f, 1f, 1f, 0.95f);
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.PathVertexTouchOnly:
                    return new Color(1f, 0.35f, 1f, 0.95f);
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.DuplicatedSide:
                    return new Color(0.2f, 1f, 0.45f, 0.95f);
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RibbonOverlapCandidate:
                    return new Color(1f, 0.75f, 0.15f, 0.92f);
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.TargetTraceFocus:
                    return incisionTargetTraceFocusColor;
                case ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.TargetTraceNeighbor:
                    return incisionTargetTraceNeighborColor;
                default:
                    return Color.white;
            }
        }

        private Color GetIncisionInsertedVertexColor(ClothSimulationDriverDoubleLayer.IncisionInsertedVertexDebugKind kind)
        {
            switch (kind)
            {
                case ClothSimulationDriverDoubleLayer.IncisionInsertedVertexDebugKind.PreSubdivision:
                    return incisionInsertedSubdivisionColor;
                case ClothSimulationDriverDoubleLayer.IncisionInsertedVertexDebugKind.BandRefine:
                    return incisionInsertedRefineColor;
                case ClothSimulationDriverDoubleLayer.IncisionInsertedVertexDebugKind.SplitStage:
                    return incisionInsertedSplitColor;
                default:
                    return Color.white;
            }
        }

        private Color GetIncisionPairDebugColor(ClothSimulationDriverDoubleLayer.IncisionPairDebugKind kind)
        {
            switch (kind)
            {
                case ClothSimulationDriverDoubleLayer.IncisionPairDebugKind.Raw:
                    return incisionPairRawColor;
                case ClothSimulationDriverDoubleLayer.IncisionPairDebugKind.Compacted:
                    return incisionPairCompactedColor;
                case ClothSimulationDriverDoubleLayer.IncisionPairDebugKind.Rejected:
                    return incisionPairRejectedColor;
                default:
                    return Color.white;
            }
        }

        private Color GetIncisionRenderSampleColor(ClothSimulationDriverDoubleLayer.IncisionRenderSampleDebugKind kind)
        {
            switch (kind)
            {
                case ClothSimulationDriverDoubleLayer.IncisionRenderSampleDebugKind.MarkerInput:
                    return incisionMarkerInputColor;
                case ClothSimulationDriverDoubleLayer.IncisionRenderSampleDebugKind.MarkerPruned:
                    return incisionMarkerPrunedColor;
                case ClothSimulationDriverDoubleLayer.IncisionRenderSampleDebugKind.SidewallOuterInput:
                    return incisionSidewallOuterInputColor;
                case ClothSimulationDriverDoubleLayer.IncisionRenderSampleDebugKind.SidewallOuterPruned:
                    return incisionSidewallOuterPrunedColor;
                case ClothSimulationDriverDoubleLayer.IncisionRenderSampleDebugKind.SidewallInnerInput:
                    return incisionSidewallInnerInputColor;
                case ClothSimulationDriverDoubleLayer.IncisionRenderSampleDebugKind.SidewallInnerPruned:
                    return incisionSidewallInnerPrunedColor;
                default:
                    return incisionCenterlineColor;
            }
        }

        private static float3 ClosestPointOnTriangleForGizmo(float3 p, float3 a, float3 b, float3 c)
        {
            float3 ab = b - a;
            float3 ac = c - a;
            float3 ap = p - a;
            float d1 = math.dot(ab, ap);
            float d2 = math.dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            float3 bp = p - b;
            float d3 = math.dot(ab, bp);
            float d4 = math.dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            float3 cp = p - c;
            float d5 = math.dot(ab, cp);
            float d6 = math.dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float y = vb * denom;
            float z = vc * denom;
            return a + ab * y + ac * z;
        }

        private static void ClosestPointsOnSegmentsForGizmo(float3 p1, float3 q1, float3 p2, float3 q2, out float3 c1, out float3 c2)
        {
            float3 d1 = q1 - p1;
            float3 d2 = q2 - p2;
            float3 r = p1 - p2;
            float a = math.dot(d1, d1);
            float e = math.dot(d2, d2);
            float f = math.dot(d2, r);
            float s;
            float t;

            if (a <= 1e-8f && e <= 1e-8f)
            {
                c1 = p1;
                c2 = p2;
                return;
            }

            if (a <= 1e-8f)
            {
                s = 0f;
                t = math.saturate(f / e);
            }
            else
            {
                float c = math.dot(d1, r);
                if (e <= 1e-8f)
                {
                    t = 0f;
                    s = math.saturate(-c / a);
                }
                else
                {
                    float b = math.dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom != 0f ? math.saturate((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = math.saturate(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = math.saturate((b - c) / a);
                    }
                }
            }

            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        private Vector3 ToVector3(float3 v)
        {
            return new Vector3(v.x, v.y, v.z);
        }
    }
}
