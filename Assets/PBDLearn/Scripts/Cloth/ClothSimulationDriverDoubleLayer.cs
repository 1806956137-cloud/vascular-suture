using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PBDLearn
{
    /// <summary>
    /// ClothSimulationDriverDoubleLayer
    /// - 统一求解：外壁 + 内壁 + 跨层(厚度)距离约束 + 两端 Pin + AABB 碰撞
    /// - 表现层：外/内壁各自挂 ClothRendererSlice，Driver 只提供 slice 信息
    /// </summary>
    [DisallowMultipleComponent]
    public class ClothSimulationDriverDoubleLayer : MonoBehaviour
    {
        public enum VesselLayer
        {
            Outer = 0,
            Inner = 1,
        }

        public enum SutureVesselSide
        {
            Unknown = 0,
            SideA = 1,
            SideB = 2,
        }

        public enum SutureSideGuideMode
        {
            None = 0,
            SlitLeftRight = 1,
            CircumferentialProximalDistal = 2,
        }

        public enum SuturePassDirection
        {
            Unknown = 0,
            OuterToInner = 1,
            InnerToOuter = 2,
        }

        public enum SutureSequencePhase
        {
            Unknown = 0,
            Current = 1,
            History = 2,
            Done = 3,
        }

        public enum SutureSequenceStatus
        {
            Unknown = 0,
            FirstPassNoExpectation = 1,
            WaitingForPair = 2,
            ExpectedMatched = 3,
            WrongSide = 4,
            WrongDirection = 5,
            WrongSideAndDirection = 6,
        }

        [Serializable]
        public struct SuturePortalSequenceRecord
        {
            public int historyIndex;
            public int portalIndex;
            public int passIndex;
            public int colliderEntityId;
            public NeedlePortalWallLayer wallLayer;
            public SuturePassDirection passDirection;
            public SutureVesselSide side;
            public SutureSequencePhase phase;
            public Vector3 worldPoint;
            public float distanceToSide;
            public bool sideValid;
            public bool isExitPortal;
        }

        [Serializable]
        public struct SuturePassSequenceRecord
        {
            public int passIndex;
            public int colliderEntityId;
            public bool isCurrent;
            public bool isComplete;
            public bool hasOuter;
            public bool hasInner;
            public SuturePassDirection direction;
            public SuturePassDirection expectedDirection;
            public SutureVesselSide side;
            public SutureVesselSide expectedSide;
            public SutureSequenceStatus status;
            public Vector3 centerWorld;
            public Vector3 outerWorld;
            public Vector3 innerWorld;
            public float distanceToSide;
            public string label;
        }

        private struct PendingSuturePassBuild
        {
            public int passIndex;
            public int colliderEntityId;
            public bool hasOuter;
            public bool hasInner;
            public bool isCurrent;
            public bool hasExitFlag;
            public bool isExitPass;
            public Vector3 outerWorld;
            public Vector3 innerWorld;
        }

        [Serializable]
        public struct SutureSideClassifyResult
        {
            public bool valid;
            public SutureVesselSide side;
            public float distanceToSide;
            public float distanceToSideA;
            public float distanceToSideB;
            public Vector3 closestPointOnSide;
            public Vector3 closestPointOnSideA;
            public Vector3 closestPointOnSideB;
        }

        [Header("Time Step")]
        [Range(0.005f, 0.02f)]
        public float dt = 0.01f;

        [Header("Setting")]
        public ClothSetting setting;

        [Header("Layer MeshFilters")]
        public MeshFilter outerMeshFilter;
        public MeshFilter innerMeshFilter;

        [Header("Colliders (Proxy)")]
        public UnityColliderProxy[] colliderProxies;

        [Header("Runtime Mesh Options")]
        [Tooltip("是否在运行时克隆两层 Mesh（强烈建议开），避免修改导入资源。")]
        public bool cloneMeshAtRuntime = true;

        [Tooltip("外壁细分次数：0=不细分；1=每个三角拆4个；2=再细分一轮...")]
        [Range(0, 3)]
        public int outerSubdivisionLevel = 0;

        [Tooltip("内壁细分次数：0=不细分；1=每个三角拆4个；2=再细分一轮...")]
        [Range(0, 3)]
        public int innerSubdivisionLevel = 0;

        [Header("Runtime Topology Budget")]
        [Tooltip("限制每层运行时粒子数。细分后的预测顶点数超过预算时，只降低细分级别，不改变原生网格拓扑。")]
        public bool enforceRuntimeVertexBudget = true;

        [Tooltip("每个动态层允许的最大运行时顶点数。1200 可保留旧低模的一次细分，同时阻止高密度新模型再次四倍细分。")]
        [Min(128)]
        public int runtimeVertexBudgetPerLayer = 1200;

        [Header("Mesh Weld (Seam Fix)")]
        public bool weldDuplicateVertices = true;
        public float weldEpsilon = 1e-4f;

        [Header("Pin Ends (Integrated)")]
        [Tooltip("两端固定区域占比（沿纵向材质坐标 UV.v）。例如 0.05 表示近端、远端各固定 5% 长度内的顶点。")]
        [Range(0.0f, 0.45f)]
        public float pinEndRegionRatio = 0.05f;

        [Header("Compliant Clamp Pins")]
        [Tooltip("端部固定是否改为夹具整体让位。开启后 Start/End 夹持区可共享有限 offset。")]
        public bool enableCompliantClampPins = true;
        [Range(0f, 1f)] public float clampPinStiffness = 0.45f;
        [Range(0f, 0.05f)] public float clampPinMaxLetDistance = 0.012f;
        [Range(0f, 0.02f)] public float clampPinMaxCorrectionPerIteration = 0.0008f;
        [Range(0f, 60f)] public float clampPinFollowDamping = 12f;

        [Header("Layer Separation (Hard Inequality)")]
        [Tooltip("硬约束保险：保证外层不会进入内层。")]
        public bool enableLayerSeparation = true;

        [Tooltip("最小分离距离（通常很小正数）。")]
        public float layerSeparationMinSep = 1e-5f;

        [Header("Thickness Links (Cross-layer Distance)")]
        [Tooltip("跨层连接：是否做对称构建（外->内 + 内->外）。密度差较大时更稳。")]
        public bool buildSymmetricThicknessLinks = true;

        [Tooltip("每个顶点的跨层连接采用 4-link（同索引主连接 + 对应层拓扑一环内的邻接点）。")]
        public bool use4Link = true;

        [Tooltip("按 Z 聚类 ring 的 epsilon（局部坐标）。0=自动（length*1e-4）。\n跨层厚度连接使用 XPBD：软硬由 ClothSetting.thicknessCompliance 控制（越大越软）。")]
        public float ringZGroupingEpsilon = 0f;

        [Header("Incision (Experimental)")]
        [Tooltip("开启后，可在 Scene 视图中按住鼠标左键沿外壁表面绘制不规则切口轨迹。")]
        public bool enableSceneIncisionMode = false;

        [Tooltip("仅当当前对象被选中时才响应切口绘制。")]
        public bool incisionOnlyWhenSelected = true;

        [Tooltip("切口引导半径（世界单位）。它不再直接删‘窄带’，而是用于把手绘轨迹吸附到邻近的网格边路径。")]
        [Min(1e-5f)]
        public float incisionBrushRadius = 0.0025f;

        [Tooltip("相邻采样点的最小间距（世界单位）。过小会导致轨迹点太密。")]
        [Min(1e-5f)]
        public float incisionMinPointSpacing = 0.0015f;

        [Tooltip("切口路径在网格上的重采样间距。0 表示按平均边长自动估计。")]
        [Min(0f)]
        public float incisionPathSampleSpacing = 0f;

        [Tooltip("路径贴合惩罚。越大，求得的切口越贴着你画的轨迹走。")]
        [Range(0f, 12f)]
        public float incisionPathSnapPenalty = 4.0f;

        [Tooltip("最多保留多少个路径锚点用于寻路。值太大没必要，值太小会丢细节。")]
        [Range(4, 128)]
        public int incisionMaxAnchorCount = 28;

        [Tooltip("切口两侧的初始张开宽度（世界单位）。它只是提交时给裂缝一个很小的初始开口。")]
        [Min(0f)]
        public float incisionOpenWidth = 0.0008f;

        [Tooltip("开启后不再走紫色 Excision/Support 分区；按纵向材质坐标定位相邻拓扑站，沿 mesh 边整圈断开，再沿切口处真实中心线切向拉开两段。")]
        public bool incisionCircumferentialSeparationCut = false;

        [Tooltip("整圈截断模式下，两段血管沿血管轴向拉开的世界距离。")]
        [Min(0f)]
        public float incisionSeparationDistance = 0.08f;

        [Tooltip("是否将同一条切口同步作用到内壁。建议开启。")]

        public bool incisionAffectsInnerLayer = true;

        [Tooltip("内壁切口引导半径/开口宽度缩放。内壁与外壁有厚度偏移时，可略大一点。")]
        [Range(0.5f, 3.0f)]
        public float incisionInnerRadiusScale = 1.2f;

        [Tooltip("是否在 Scene 中绘制当前切口预览。")]
        public bool drawIncisionPreview = true;

        [Tooltip("切口预览颜色。")]
        public Color incisionPreviewColor = new Color(1.0f, 0.25f, 0.1f, 1.0f);

        [Header("Suture Side Guide")]
        [Tooltip("切开/切断成功后，把两侧血管口缓存成缝合用的 SideA / SideB。第一步只做两侧语义，不处理穿针顺序和线。")]
        public bool enableSutureSideGuide = true;

        [Tooltip("在 Scene 中额外绘制缝合侧 A / B。整圈切断模式下 SideA 固定为拓扑近端岸，SideB 固定为拓扑远端岸。")]
        public bool drawSutureSideGuide = true;

        public Color sutureSideAColor = new Color(0.1f, 0.55f, 1.0f, 1.0f);
        public Color sutureSideBColor = new Color(1.0f, 0.35f, 0.15f, 1.0f);

        [Header("Suture Pass Sequence Guide")]
        [Tooltip("第二步：根据 NeedlePortal history/current 判断穿针顺序。第一针不要求穿哪一侧；从第二针开始，期望方向和侧别都按上一针交替。")]
        public bool enableSuturePassSequenceGuide = true;

        [Tooltip("在 Scene 中绘制当前/历史穿针顺序标签和期望提示。只做诊断，不驱动针或线。 ")]
        public bool drawSuturePassSequenceGuide = true;

        public Color sutureCurrentPassColor = new Color(0.15f, 1.0f, 0.45f, 1.0f);
        public Color sutureHistoryPassColor = new Color(0.85f, 0.85f, 0.85f, 0.85f);
        public Color sutureExpectedPassColor = new Color(1.0f, 0.92f, 0.25f, 1.0f);
        public Color sutureSequenceErrorColor = new Color(1.0f, 0.12f, 0.12f, 1.0f);

        [Header("Procedural Incision Authoring")]
        [Tooltip("是否启用参数化切口轨迹生成功能。手绘轨迹仍可保留为底层复用能力，但这里更适合做规则化横向切口。")]
        public bool enableProceduralIncisionAuthoring = true;

        [Tooltip("是否在编辑态根据下方参数实时重建切口预览轨迹。开启后会用参数化轨迹覆盖当前预览轨迹。")]
        public bool proceduralIncisionLivePreview = false;

        [Tooltip("Play 开始后是否直接按当前参数生成并提交一次切口。开启后不再依赖“先预览、再提交”的多阶段流程。")]
        public bool autoApplyProceduralIncisionOnStart = true;

        [Tooltip("运行时单次切口模式。开启后，进入 Play 只提交一次切口，后续不再接受重复提交。")]
        public bool runtimeSingleIncisionMode = true;

        [Tooltip("切口中心沿血管纵向材质坐标的归一化位置。0=近端，1=远端。字段名保留 Z 仅用于旧场景序列化兼容。")]
        [Range(0f, 1f)] public float proceduralIncisionNormalizedZ = 0.5f;

        [Tooltip("切口中心在血管圆周上的角度（度）。0=+X 方向，90=+Y 方向。")]
        [Range(-180f, 180f)] public float proceduralIncisionCircumferenceAngleDeg = 0f;

        [Tooltip("切口方向角（度）。0=横向/环向切口，90=沿血管轴向切口。通常横向切口把它设在 0 附近。")]
        [Range(-90f, 90f)] public float proceduralIncisionDirectionDeg = 0f;

        [Tooltip("参数化切口长度（世界对应到 outer local 的近似长度）。")]
        [Min(1e-5f)] public float proceduralIncisionLength = 0.012f;

        [Tooltip("参数化切口轨迹采样点数。越多越平滑，但没必要太大。")]
        [Range(4, 128)] public int proceduralIncisionSampleCount = 17;

        [Tooltip("生成轨迹时沿外法线稍微抬起的距离，便于后续投影到外壁表面。")]
        [Min(0f)] public float proceduralIncisionSurfaceLift = 0.0006f;

        [Tooltip("对参数化切口中心做一个 outer local 空间下的细调偏移。")]
        public Vector3 proceduralIncisionCenterOffsetLocal = Vector3.zero;

        [Header("Incision Scene Panel")]
        [Tooltip("是否在 Scene 里显示切口控制面板。即使不开手绘模式，也可以在这里实时调整参数化切口并生成预览。")]
        public bool showIncisionAuthoringPanel = true;

        [Tooltip("是否允许拖动切口控制面板。")]
        public bool allowDragIncisionPanel = true;

        [Tooltip("切口控制面板在 Scene 里的屏幕偏移。")]
        public Vector2 incisionPanelScreenOffset = new Vector2(14f, 72f);

        [Min(240f)] public float incisionPanelWidth = 320f;
        [Min(18f)] public float incisionPanelRowHeight = 20f;

        [SerializeField, HideInInspector]
        private List<Vector3> _incisionStrokeWorld = new List<Vector3>();

        // ---------------- public accessors ----------------
        public ClothSimulator Simulator => _simulator;
        public int OuterStart => 0;
        public int OuterCount => _outerCount;
        public int InnerStart => _outerCount;
        public int InnerCount => _innerCount;

        public bool HasSutureSideGuides => _sutureSideGuideValid;
        public SutureSideGuideMode CurrentSutureSideGuideMode => _sutureSideGuideMode;
        public string SutureSideGuideSummary => _sutureSideGuideSummary;
        public Vector3 SutureSideAGuideCenterWorld => _sutureSideAGuideCenterWorld;
        public Vector3 SutureSideBGuideCenterWorld => _sutureSideBGuideCenterWorld;
        public IReadOnlyList<Vector3> SutureSideAOuterBoundaryWorld => _sutureSideAOuterBoundaryWorld;
        public IReadOnlyList<Vector3> SutureSideBOuterBoundaryWorld => _sutureSideBOuterBoundaryWorld;
        public IReadOnlyList<Vector3> SutureSideAInnerBoundaryWorld => _sutureSideAInnerBoundaryWorld;
        public IReadOnlyList<Vector3> SutureSideBInnerBoundaryWorld => _sutureSideBInnerBoundaryWorld;
        public bool HasSuturePassSequence => _suturePassSequenceRecords.Count > 0;
        public string SuturePassSequenceSummary => _suturePassSequenceSummary;
        public SutureVesselSide SutureExpectedNextSide => _sutureExpectedNextSide;
        public SuturePassDirection SutureExpectedNextDirection => _sutureExpectedNextDirection;
        public IReadOnlyList<SuturePortalSequenceRecord> SuturePortalSequenceRecords => _suturePortalSequenceRecords;
        public IReadOnlyList<SuturePassSequenceRecord> SuturePassSequenceRecords => _suturePassSequenceRecords;

        // ---------------- internal ----------------
        private ClothSimulator _simulator;
        private ClothMeshModifier _meshModifier;
        private Mesh _outerMesh;
        private Mesh _innerMesh;
        private Mesh _outerSourceMesh;
        private Mesh _innerSourceMesh;
        private bool _runtimeMeshesPrepared;
        private int _outerCount;
        private int _innerCount;

        private readonly Dictionary<int, Transform> _attachments = new Dictionary<int, Transform>();
        private const int StartClampGroupId = 1;
        private const int EndClampGroupId = 2;
        private readonly List<int> _startClampPinIndices = new List<int>(128);
        private readonly List<int> _endClampPinIndices = new List<int>(128);
        private Vector3 _startClampInitialCentroid;
        private Vector3 _endClampInitialCentroid;
        private Vector3 _startClampOffset;
        private Vector3 _endClampOffset;
        private int _clampPinUpdateLogCounter;
        private float _elapsedTime;
        private bool _pendingRebuild;

        private bool _incisionRebuildPending;
        private bool _isIncisionDragging;
        private bool _proceduralIncisionAutoApplied;
        private bool _runtimeIncisionCommittedOnce;

        public enum IncisionPatchDebugKind
        {
            CrossedOriginal = 0,
            RefinementPatch = 1,
            CutPatch = 2,
            RemovedDegenerateEdge = 3,
            RemovedDegenerateArea = 4,
            RemovedBridge = 5,
            StrictCandidate = 6,
            BridgeCandidate = 7,
            CoreCandidate = 8,
            CorridorCandidate = 9,
            MiddleBandCandidate = 10,
            PathCarrier = 11,
            PathVertexTouchOnly = 12,
            DuplicatedSide = 13,
            RibbonOverlapCandidate = 14,
            StripInteriorCandidate = 15,
            RemovedStripInterior = 16,
            TargetTraceFocus = 17,
            TargetTraceNeighbor = 18,
        }

        public enum IncisionInsertedVertexDebugKind
        {
            PreSubdivision = 0,
            BandRefine = 1,
            SplitStage = 2,
        }

        public enum IncisionPairDebugKind
        {
            Raw = 0,
            Compacted = 1,
            Rejected = 2,
        }

        public enum IncisionPairRejectReason
        {
            None = 0,
            TooClose = 1,
            JumpTooLarge = 2,
            ReverseDirection = 3,
        }

        public enum IncisionRenderSampleDebugKind
        {
            MarkerInput = 0,
            MarkerPruned = 1,
            SidewallOuterInput = 2,
            SidewallOuterPruned = 3,
            SidewallInnerInput = 4,
            SidewallInnerPruned = 5,
            DebugEffective = 6,
        }

        [System.Serializable]
        public struct IncisionDebugPoint
        {
            public Vector3 center;
            public Vector3 left;
            public Vector3 right;
            public Vector3 normal;
            public float targetOpen;
            public float currentOpen;
            public float weight;
            public int layer; // 0 outer, 1 inner
        }

        [System.Serializable]
        public struct IncisionDebugTriangle
        {
            public Vector3 a;
            public Vector3 b;
            public Vector3 c;
            public Vector3 center;
            public int layer;
            public int kind;
            public int triIndex;
            public int ia;
            public int ib;
            public int ic;
        }

        [System.Serializable]
        public struct IncisionDebugRibbonFrame
        {
            public Vector3 center;
            public Vector3 left;
            public Vector3 right;
            public int layer;
            public float halfWidth;
            public float actualGap;
        }

        [System.Serializable]
        public struct IncisionDeleteDebugSummary
        {
            public int layer;
            public bool success;
            public string failStage;
            public string failReason;
            public float guideRadius;
            public float openWidth;
            public float sampleSpacing;
            public float pathSnapPenalty;
            public int anchorCount;
            public int localSubdivisionPasses;
            public float guideLength;
            public float pathLength;
            public int strokePointCount;
            public int resampledSampleCount;
            public int densifiedSampleCount;
            public float avgTriangleEdgeLength;
            public float insertEpsilon;
            public float surfaceBandRadius;
            public int pathCount;
            public int lipPairCount;
            public int denseStationsCount;
            public int centerlineCount;
            public int patchComponentCount;
            public int excisionComponentCount;
            public int framesCount;
            public int overlapCandidateCount;
            public int corridorCandidateCount;
            public int middleBandCandidateCount;
            public int strictCandidateCount;
            public int bridgeCandidateCount;
            public int coreCandidateCount;
            public int stripInteriorCandidateCount;
            public int cutCandidateCount;
            public int preparedTriangleCount;
            public int selectedTriangleCount;
            public int pendingSegmentCount;
            public int dominantChainCount;
            public int sideFlipCount;
            public int invalidRibbonFrameCount;
            public int overlapRibbon3DCount;
            public int insideBand3DCount;
            public int insideCore3DCount;
            public int twoDThreeDDisagreementCount;
            public float minGap3D;
            public float maxGap3D;
            public float avgGap3D;
            public float minHalfWidth3D;
            public float maxHalfWidth3D;
            public float avgHalfWidth3D;
            public int removedCount;
            public int keptCount;
            public float maxPatchTriangleAspect;
            public float averageRealizedOpenWidth;
            public bool usedFallbackPath;
            public int stripRowCount;
            public int supportQuadCount;
            public int excisionQuadCount;
            public int boundaryContinuityViolationCount;
            public float endTaperLengthWorld;
            public float tinyAreaThreshold;
            public float ribbonPad;
        }

        [System.Serializable]
        public struct IncisionInsertedVertexDebugPoint
        {
            public Vector3 position;
            public int layer;
            public int kind;
        }

        [System.Serializable]
        public struct IncisionPairDebugPoint
        {
            public Vector3 center;
            public Vector3 left;
            public Vector3 right;
            public Vector3 splitDir;
            public float targetOpen;
            public float strokeT;
            public int layer;
            public int kind;
            public int rejectReason;
        }

        [System.Serializable]
        public struct IncisionRenderSampleDebugPoint
        {
            public Vector3 center;
            public Vector3 left;
            public Vector3 right;
            public float t;
            public int layer;
            public int kind;
        }

        [System.Serializable]
        public struct IncisionTargetTraceDebugRecord
        {
            public Vector3 center;
            public int layer;
            public int triIndex;
            public int ia;
            public int ib;
            public int ic;
            public float area2D;
            public float centerlineDistance2D;
            public int corridorHits;
            public int centralHits;
            public bool overlapCandidate;
            public bool strictCandidate;
            public bool coreCandidate;
            public bool corridorCandidate;
            public bool bridgeCandidate;
            public bool middleBandCandidate;
            public bool stripInteriorCandidate;
            public bool removedCandidate;
            public float score;
            public string failHint;
        }

        [System.Serializable]
        public struct IncisionFinalTriangleAuditRecord
        {
            public Vector3 center;
            public int layer;
            public int triIndex;
            public int ia;
            public int ib;
            public int ic;
            public float area2D;
            public float area3D;
            public float centerlineDistance2D;
            public int corridorHits;
            public int centralHits;
            public int nearestRibbonFrameIndex;
            public float signedLateral3D;
            public float normalizedLateral3D;
            public float halfWidth3D;
            public float ribbonGap3D;
            public float ribbonSurfaceOffset3D;
            public bool overlapCandidate;
            public bool overlapRibbon3D;
            public bool insideBoundaryRegion;
            public bool insideRibbonBand3D;
            public bool insideRibbonCore3D;
            public bool removedCandidate;
            public string failHint;
        }

        [SerializeField, HideInInspector]
        private List<IncisionDebugPoint> _incisionDebugPoints = new List<IncisionDebugPoint>();

        [SerializeField, HideInInspector]
        private List<IncisionDebugTriangle> _incisionDebugTriangles = new List<IncisionDebugTriangle>();

        [SerializeField, HideInInspector]
        private List<IncisionInsertedVertexDebugPoint> _incisionInsertedVertices = new List<IncisionInsertedVertexDebugPoint>();

        [SerializeField, HideInInspector]
        private List<IncisionPairDebugPoint> _incisionPairDebugPoints = new List<IncisionPairDebugPoint>();

        [SerializeField, HideInInspector]
        private List<IncisionRenderSampleDebugPoint> _incisionRenderSampleDebugPoints = new List<IncisionRenderSampleDebugPoint>();

        [SerializeField, HideInInspector]
        private List<IncisionTargetTraceDebugRecord> _incisionTargetTraceDebugRecords = new List<IncisionTargetTraceDebugRecord>();

        [SerializeField, HideInInspector]
        private List<IncisionTargetTraceDebugRecord> _incisionAllTargetTraceDebugRecords = new List<IncisionTargetTraceDebugRecord>();

        [SerializeField, HideInInspector]
        private List<IncisionFinalTriangleAuditRecord> _incisionFinalTriangleAuditRecords = new List<IncisionFinalTriangleAuditRecord>();

        [SerializeField, HideInInspector]
        private List<Vector3> _incisionDebugCenterline = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<IncisionDebugRibbonFrame> _incisionDebugRibbonFrames = new List<IncisionDebugRibbonFrame>();
        [SerializeField, HideInInspector]
        private List<IncisionDeleteDebugSummary> _incisionDeleteDebugSummaries = new List<IncisionDeleteDebugSummary>();
        [SerializeField, HideInInspector]
        private List<IncisionDebugRibbonFrame> _runtimeCommittedIncisionDebugRibbonFrames = new List<IncisionDebugRibbonFrame>();
        [SerializeField, HideInInspector]
        private List<IncisionDebugTriangle> _runtimeCommittedIncisionDebugTriangles = new List<IncisionDebugTriangle>();
        [SerializeField, HideInInspector]
        private List<IncisionDeleteDebugSummary> _runtimeCommittedIncisionDeleteDebugSummaries = new List<IncisionDeleteDebugSummary>();
        [SerializeField, HideInInspector]
        private List<IncisionTargetTraceDebugRecord> _runtimeCommittedIncisionTargetTraceDebugRecords = new List<IncisionTargetTraceDebugRecord>();

        [SerializeField, HideInInspector]
        private List<IncisionTargetTraceDebugRecord> _runtimeCommittedIncisionAllTargetTraceDebugRecords = new List<IncisionTargetTraceDebugRecord>();
        [SerializeField, HideInInspector]
        private List<IncisionFinalTriangleAuditRecord> _runtimeCommittedIncisionFinalTriangleAuditRecords = new List<IncisionFinalTriangleAuditRecord>();
        [SerializeField, HideInInspector]
        private List<Vector3> _incisionMasterChainWorld = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _incisionCenterCurveWorld = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _outerIncisionLeftBoundaryWorld = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _outerIncisionRightBoundaryWorld = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _innerIncisionLeftBoundaryWorld = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _innerIncisionRightBoundaryWorld = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitRowCentersOuter = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitLeftBoundaryOuter = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitRightBoundaryOuter = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitLeftSupportOuter = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitRightSupportOuter = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitFailedInterfaceEdgesOuter = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<string> _runtimePrecommitFailedInterfaceEdgeLabelsOuter = new List<string>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitRowCentersInner = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitLeftBoundaryInner = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitRightBoundaryInner = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitLeftSupportInner = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitRightSupportInner = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<Vector3> _runtimePrecommitFailedInterfaceEdgesInner = new List<Vector3>();
        [SerializeField, HideInInspector]
        private List<string> _runtimePrecommitFailedInterfaceEdgeLabelsInner = new List<string>();
        [SerializeField, HideInInspector]
        private string _runtimePrecommitProjectionAlignOuter = string.Empty;
        [SerializeField, HideInInspector]
        private string _runtimePrecommitProjectionAlignInner = string.Empty;

        [SerializeField, HideInInspector]
        private bool _sutureSideGuideValid;
        [SerializeField, HideInInspector]
        private SutureSideGuideMode _sutureSideGuideMode = SutureSideGuideMode.None;
        [SerializeField, HideInInspector]
        private string _sutureSideGuideSummary = string.Empty;
        [SerializeField, HideInInspector]
        private Vector3 _sutureSideAGuideCenterWorld;
        [SerializeField, HideInInspector]
        private Vector3 _sutureSideBGuideCenterWorld;
        private readonly List<Vector3> _sutureSideAOuterBoundaryWorld = new List<Vector3>();
        private readonly List<Vector3> _sutureSideBOuterBoundaryWorld = new List<Vector3>();
        private readonly List<Vector3> _sutureSideAInnerBoundaryWorld = new List<Vector3>();
        private readonly List<Vector3> _sutureSideBInnerBoundaryWorld = new List<Vector3>();
        private readonly List<SutureSidewallBoundaryEdgeInfo> _sutureSidewallBoundaryEdges = new List<SutureSidewallBoundaryEdgeInfo>(128);
        private readonly List<SuturePortalSequenceRecord> _suturePortalSequenceRecords = new List<SuturePortalSequenceRecord>();
        private readonly List<SuturePassSequenceRecord> _suturePassSequenceRecords = new List<SuturePassSequenceRecord>();
        private string _suturePassSequenceSummary = string.Empty;
        private SutureVesselSide _sutureExpectedNextSide = SutureVesselSide.Unknown;
        private SuturePassDirection _sutureExpectedNextDirection = SuturePassDirection.Unknown;

        private readonly List<Vector3> _outerIncisionCenterlineWorld = new List<Vector3>();
        private readonly List<Vector3> _innerIncisionCenterlineWorld = new List<Vector3>();
        private readonly List<PendingLipStation> _outerIncisionStations = new List<PendingLipStation>();
        private readonly List<PendingLipStation> _innerIncisionStations = new List<PendingLipStation>();
        private readonly List<Vector3> _outerIncisionStationCentersWorld = new List<Vector3>();
        private readonly List<Vector3> _innerIncisionStationCentersWorld = new List<Vector3>();
        [SerializeField, HideInInspector]
        private int _incisionOuterPatchComponentCount;
        [SerializeField, HideInInspector]
        private int _incisionInnerPatchComponentCount;
        [SerializeField, HideInInspector]
        private string _incisionOuterQualitySummary = string.Empty;
        [SerializeField, HideInInspector]
        private string _incisionInnerQualitySummary = string.Empty;
        [SerializeField, HideInInspector]
        private int _incisionFramesSinceApply = int.MaxValue;
        [SerializeField, HideInInspector]
        private bool _incisionNaturalRetractionStartedLogged;
        [SerializeField, HideInInspector]
        private int _incisionNaturalRetractionUpdateLogCounter;


        private struct IncisionLipPair
        {
            public int leftLocalIndex;
            public int rightLocalIndex;
            public Vector3 centerLocal;
            public Vector3 splitDirLocal;
            public float targetOpen;
            public float weight;
            public float strokeT;
            public int layer; // 0 outer, 1 inner
        }

        private struct PendingLipSegment
        {
            public int leftAIndex;
            public int rightAIndex;
            public int leftBIndex;
            public int rightBIndex;
            public ulong endpointKeyA;
            public ulong endpointKeyB;
            public Vector3 pointALocal;
            public Vector3 pointBLocal;
            public Vector3 centerLocal;
            public Vector3 splitDirLocal;
            public float targetOpen;
            public float weight;
            public float strokeTA;
            public float strokeTB;
            public int layer;
            public int sourceTriIndex;
        }

        private struct PendingLipStation
        {
            public ulong stationKey;
            public int leftLocalIndex;
            public int rightLocalIndex;
            public Vector3 seedCenterLocal;
            public Vector3 splitDirLocal;
            public float targetOpen;
            public float weight;
            public float strokeT;
            public int layer;
            public bool suppressRuntimeMotion;
        }

        private struct PreparedTriangleSplit
        {
            public int triIndex;
            public int a;
            public int b;
            public int c;
            public int lonelyVertex;
            public int otherA;
            public int otherB;
            public int cutA;
            public int cutB;
            public int dupA;
            public int dupB;
            public bool lonelyPositive;
            public Vector3 refNormal;
        }

        private struct StationGraphNode
        {
            public ulong stationKey;
            public int leftLocalIndex;
            public int rightLocalIndex;
            public Vector3 seedCenterLocal;
            public Vector3 splitDirLocal;
            public float targetOpen;
            public float weight;
            public float strokeT;
            public int layer;
            public int mergeCount;
        }

        private struct StationGraphEdge
        {
            public int nodeA;
            public int nodeB;
            public int sourceTriIndex;
            public float strokeMid;
        }

        private readonly List<IncisionLipPair> _outerIncisionLipPairs = new List<IncisionLipPair>();
        private readonly List<IncisionLipPair> _innerIncisionLipPairs = new List<IncisionLipPair>();

        private struct IncisionPairDebugRecord
        {
            public int leftLocalIndex;
            public int rightLocalIndex;
            public Vector3 centerLocal;
            public Vector3 splitDirLocal;
            public float targetOpen;
            public float strokeT;
            public int layer;
            public IncisionPairDebugKind kind;
            public IncisionPairRejectReason rejectReason;
        }

        private class IncisionDebugCapture
        {
            public readonly int layer;
            public readonly List<IncisionDebugTriangle> triangles = new List<IncisionDebugTriangle>(256);
            public readonly List<IncisionInsertedVertexDebugPoint> insertedVertices = new List<IncisionInsertedVertexDebugPoint>(256);
            public readonly List<IncisionPairDebugRecord> pairRecords = new List<IncisionPairDebugRecord>(256);
            public readonly List<string> targetTraceLogs = new List<string>(32);
            public readonly List<IncisionTargetTraceDebugRecord> targetTraceRecords = new List<IncisionTargetTraceDebugRecord>(32);
            public readonly List<IncisionTargetTraceDebugRecord> allTargetTraceRecords = new List<IncisionTargetTraceDebugRecord>(256);
            public bool targetTraceEnabled;
            public int targetTraceCount = 6;
            public float targetTraceNeighborRadius = 0.0045f;
            public bool targetTraceLogToConsole = true;

            public IncisionDebugCapture(int layer)
            {
                this.layer = layer;
            }
        }

        [Header("Incision Thickness Gap / Sidewall (Experimental)")]
        [Tooltip("切口后是否按血管壁厚度给口缘一个更可见的持续张开趋势。")]
        public bool enableIncisionThicknessGap = true;

        [Tooltip("可见切口缝隙的厚度比例。0.3 表示目标缝隙大约取局部壁厚的 30%。")]
        [Range(0f, 1.5f)] public float incisionThicknessGapScale = 0.32f;

        [Tooltip("切口区域是否强制切断跨层厚度连接，而不是只做降权。")]
        public bool severThicknessLinksAcrossIncision = true;

        [Tooltip("切口区域是否强制切断 layer separation，而不是只做降权。")]
        public bool severLayerSeparationAcrossIncision = true;

        [Tooltip("是否生成一个额外的运行时切口侧壁渲染网格，用来表现真实厚度口缘。")]
        public bool enableIncisionSidewallRender = true;

        [Tooltip("是否把已注册的切口虚拟侧壁边界加入求解器距离/剪切约束。只使用 Outer/Inner 切口站点身份，不从渲染 fallback 倒推。")]
        public bool enableIncisionVirtualSidewallConstraints = true;

        [Tooltip("虚拟侧壁 Outer-Inner 距离约束的 rest length 相对当前壁厚的比例。1=保持当前壁厚，<1=更薄，>1=更厚。")]
        [Range(0.1f, 2.0f)] public float incisionVirtualSidewallDistanceScale = 1.0f;

        [Tooltip("虚拟侧壁 Outer-Inner 距离约束刚度倍率。")]
        [Range(0f, 4f)] public float incisionVirtualSidewallDistanceStiffness = 1.0f;

        [Tooltip("虚拟侧壁四边形对角线剪切约束刚度倍率，用来防止侧壁塌成斜薄片。")]
        [Range(0f, 4f)] public float incisionVirtualSidewallShearStiffness = 0.65f;

        [Tooltip("当中层填充已经开启时，是否自动关闭 IncisionSidewallRuntime。这样可以避免跨层斜薄片把真实切口视觉盖坏。")]
        public bool disableIncisionSidewallWhenMiddleWallVisible = false;

        private GameObject _incisionSidewallObject;
        private MeshFilter _incisionSidewallFilter;
        private MeshRenderer _incisionSidewallRenderer;
        private Mesh _incisionSidewallMesh;
        private Material _incisionSidewallMaterial;
        private readonly List<Vector3> _incisionSidewallVertices = new List<Vector3>(1024);
        private readonly List<int> _incisionSidewallTriangles = new List<int>(2048);
        private readonly List<Vector2> _incisionSidewallUVs = new List<Vector2>(1024);
        private readonly List<Vector3> _incisionSidewallNormals = new List<Vector3>(1024);

        [Header("Incision Marker Render")]
        [Tooltip("是否生成一个独立的切口标识渲染条带。它不依赖内外层侧壁严格配对，主要用于稳定地显示切口位置。")]
        public bool enableIncisionMarkerRender = true;

        [Tooltip("切口标识材质覆盖。留空时会自动创建一个纯色材质。")]
        public Material incisionMarkerMaterialOverride;

        [Tooltip("切口标识颜色。可模拟术中用于定位切口的染色/试剂。")]
        public Color incisionMarkerColor = new Color(0.12f, 0.95f, 0.3f, 0.92f);

        [Tooltip("切口标识宽度占切口实际开口宽度的比例。")]
        [Range(0.1f, 2.0f)] public float incisionMarkerWidthScale = 0.65f;

        [Tooltip("切口标识沿外法线抬起的距离，避免与外壁表面完全重合而闪烁。")]
        [Min(0f)] public float incisionMarkerLift = 0.00035f;

        [Tooltip("切口标识的最小可见宽度。")]
        [Min(0f)] public float incisionMarkerMinWidth = 0.00045f;

        [Tooltip("切口标识优先使用程序定义的目标切口线，而不是实际成功 pair 的中心点。这样标识长度会和你设定的切口线一致；真实生效区间请看 Gizmo 的 effective cut。")]
        public bool incisionMarkerUseTargetStroke = false;

        private GameObject _incisionMarkerObject;
        private MeshFilter _incisionMarkerFilter;
        private MeshRenderer _incisionMarkerRenderer;
        private Mesh _incisionMarkerMesh;
        private Material _incisionMarkerRuntimeMaterial;
        private readonly List<Vector3> _incisionMarkerVertices = new List<Vector3>(1024);
        private readonly List<int> _incisionMarkerTriangles = new List<int>(2048);
        private readonly List<Vector2> _incisionMarkerUVs = new List<Vector2>(1024);
        private readonly List<Vector3> _incisionMarkerNormals = new List<Vector3>(1024);

        [Header("Middle Wall Fill Render")]
        [Tooltip("是否生成一个运行时的中膜/壁厚填充渲染网格。它只负责显示，不参与碰撞、约束和穿刺求解。")]
        public bool enableMiddleWallFillRender = true;

        [Tooltip("中膜填充的材质覆盖。留空时默认复用外壁 Renderer 的首个材质。")]
        public Material middleWallMaterialOverride;

        [Tooltip("是否在编辑态（未运行）也生成中膜预览物体，便于在 Scene 中提前观察整体厚度效果。")]
        public bool previewMiddleWallInEditMode = true;

        [Tooltip("中膜在壁厚中的外侧起始比例。0=贴着外壁，1=贴着内壁。建议略大于 0，避免和外壁完全重合。")]
        [Range(0.02f, 0.48f)] public float middleWallOuterBlend = 0.28f;

        [Tooltip("中膜在壁厚中的内侧终止比例。0=贴着外壁，1=贴着内壁。建议略小于 1，避免和内壁完全重合。")]
        [Range(0.52f, 0.98f)] public float middleWallInnerBlend = 0.72f;

        private struct BoundaryBridgeEdge
        {
            public int a;
            public int b;
        }

        private GameObject _middleWallFillObject;
        private MeshFilter _middleWallFillFilter;
        private MeshRenderer _middleWallFillRenderer;
        private Mesh _middleWallFillMesh;
        private Material _middleWallFillMaterial;
        private bool _middleWallBoundaryValid;
        private string _middleWallBoundaryDiagnostic;
        private readonly List<BoundaryBridgeEdge> _middleWallBoundaryEdges = new List<BoundaryBridgeEdge>(512);
        private readonly List<Vector3> _middleWallFillVertices = new List<Vector3>(4096);
        private readonly List<int> _middleWallFillTriangles = new List<int>(16384);
        private readonly List<Vector2> _middleWallFillUVs = new List<Vector2>(4096);
        private readonly List<Vector3> _middleWallFillNormals = new List<Vector3>(4096);
        private int[] _middleWallOuterToInnerLocal;
        private float[] _middleWallOuterToInnerRest;

        [Header("Incision Relaxation (Experimental)")]
        [Tooltip("切口唇边是否在运行时持续回弹张开。")]
        public bool enableIncisionLipRelaxation = true;

        [Tooltip("沿切口长度做非均匀开口变化。")]
        [Range(0f, 1f)] public float incisionProfileVariation = 0.5f;

        [Tooltip("曲率越大处切口越容易张开。")]
        [Range(0f, 3f)] public float incisionCurvatureWidthScale = 1.25f;

        [Tooltip("每帧唇边回弹强度。")]
        [Range(0f, 1f)] public float incisionLipRelaxation = 0.65f;

        [Tooltip("唇边回弹速度。")]
        [Range(0f, 60f)] public float incisionLipRelaxationSpeed = 12f;
        [Range(0, 60)] public int incisionSupportWarmupFrames = 10;
        [Range(0f, 1f)] public float incisionMinimumSupportKeepScale = 0.35f;


        [Tooltip("每帧最大张口推进量（世界单位）。")]
        [Min(0f)] public float incisionLipMaxPushPerFrame = 0.0008f;

        [Header("Incision Natural Retraction")]
        [Tooltip("切断后是否用自然小幅回缩作为实际可见开口目标，而不是完整人工张开宽度。")]
        public bool enableIncisionNaturalRetraction = true;
        [Tooltip("自然回缩最大可见开口距离。")]
        [Range(0f, 0.05f)] public float incisionNaturalRetractionDistance = 0.01f;
        [Tooltip("自然回缩目标相对原始 targetOpen 的比例。")]
        [Range(0f, 1f)] public float incisionNaturalRetractionScale = 0.45f;
        [Tooltip("自然回缩目标建立速度。")]
        [Range(0f, 60f)] public float incisionNaturalRetractionSpeed = 10f;
        [Tooltip("自然回缩每帧最大 lip 位移。")]
        [Min(0f)] public float incisionNaturalRetractionMaxPushPerFrame = 0.0012f;

        [Header("Incision Debug")]
        public bool drawIncisionDebug = true;
        public bool drawIncisionDebugLabels = false;
        public Color incisionCenterlineColor = new Color(1f, 0.55f, 0.1f, 1f);
        public Color incisionLeftLipColor = new Color(1f, 0.2f, 0.2f, 1f);
        public Color incisionRightLipColor = new Color(0.15f, 0.85f, 1f, 1f);

        [Header("Incision Targeted Trace")]
        [Tooltip("是否在切口删面阶段自动挑出最靠近 strip 中心的小三角，单独高亮并把判定细节打印到 Console。这个调试比满屏标签更适合定位“为什么没删到中间小面”。")]
        public bool enableIncisionTargetTrace = true;

        [Tooltip("每层最多高亮多少个 strip 目标三角。")]
        [Range(1, 12)] public int incisionTargetTraceCount = 6;

        [Tooltip("围绕 focus 三角再额外标出多大范围内的邻居，便于看局部拓扑。")]
        [Min(0f)] public float incisionTargetTraceNeighborRadius = 0.0045f;

        [Tooltip("是否把 target trace 的细节打印到 Console。")]
        public bool incisionTargetTraceLogToConsole = true;

        [Header("Incision Pair / Debug Simplify")]
        [Tooltip("切口唇边 pair 的最小保留间距。小于该距离的近邻 pair 会被合并/抽稀，减少一串挤在一起的调试点。")]
        [Min(0f)] public float incisionPairMinSpacing = 0.0025f;

        [Tooltip("仅影响调试显示：切口调试点之间的最小间距。0 表示直接沿用 pair 间距。")]
        [Min(0f)] public float incisionDebugMinSpacing = 0f;

        [Tooltip("仅影响调试显示：每隔多少个 pair 画一个点。1=全画，2=隔一个画一个。")]
        [Range(1, 8)] public int incisionDebugDrawStride = 1;

        [Header("Incision Local Refinement")]
        [Tooltip("切口提交前，是否先对被切口穿过的原始三角及其 1-ring 邻域做局部预细分。0=关闭；1=细分一轮；2=再细分一轮。")]
        [Range(0, 2)] public int incisionLocalSubdivisionPasses = 1;
#if UNITY_EDITOR
        private bool _sceneGuiSubscribed;
        private bool _editorMiddleWallPreviewDirty = true;
        private static bool s_draggingIncisionPanel;
        private static Vector2 s_incisionPanelDragStartMouse;
        private static Vector2 s_incisionPanelDragStartOffset;
#endif

        /// <summary>
        /// Request a safe rebuild of simulator & constraints on next Update() tick.
        /// Use this when you modify/recreate meshes at runtime (vertexCount changes).
        /// </summary>
        public void RequestRebuild()
        {
            _pendingRebuild = true;
        }

        private float ResolveIncisionProfileVariation()
        {
            return setting != null ? setting.incisionProfileVariation : incisionProfileVariation;
        }

        private float ResolveIncisionCurvatureWidthScale()
        {
            return setting != null ? setting.incisionCurvatureWidthScale : incisionCurvatureWidthScale;
        }

        private bool ResolveEnableIncisionLipRelaxation()
        {
            return setting != null ? (setting.enableIncisionLocalConstraints && setting.enableIncisionLipRelaxation) : enableIncisionLipRelaxation;
        }

        private float ResolveIncisionLipRelaxation()
        {
            return setting != null ? setting.incisionLipRelaxation : incisionLipRelaxation;
        }

        private float ResolveIncisionLipRelaxationSpeed()
        {
            return setting != null ? setting.incisionLipRelaxationSpeed : incisionLipRelaxationSpeed;
        }

        private float ResolveIncisionLipMaxPushPerFrame()
        {
            return setting != null ? setting.incisionLipMaxPushPerFrame : incisionLipMaxPushPerFrame;
        }

        private bool ResolveEnableIncisionNaturalRetraction()
        {
            return setting != null ? setting.enableIncisionNaturalRetraction : enableIncisionNaturalRetraction;
        }

        private float ResolveIncisionNaturalRetractionDistance()
        {
            return setting != null ? setting.incisionNaturalRetractionDistance : incisionNaturalRetractionDistance;
        }

        private float ResolveIncisionNaturalRetractionScale()
        {
            return setting != null ? setting.incisionNaturalRetractionScale : incisionNaturalRetractionScale;
        }

        private float ResolveIncisionNaturalRetractionSpeed()
        {
            return setting != null ? setting.incisionNaturalRetractionSpeed : incisionNaturalRetractionSpeed;
        }

        private float ResolveIncisionNaturalRetractionMaxPushPerFrame()
        {
            return setting != null ? setting.incisionNaturalRetractionMaxPushPerFrame : incisionNaturalRetractionMaxPushPerFrame;
        }

        private bool ResolveEnableCompliantClampPins()
        {
            return setting != null ? setting.enableCompliantClampPins : enableCompliantClampPins;
        }

        private float ResolveClampPinStiffness()
        {
            return setting != null ? setting.clampPinStiffness : clampPinStiffness;
        }

        private float ResolveClampPinMaxLetDistance()
        {
            return setting != null ? setting.clampPinMaxLetDistance : clampPinMaxLetDistance;
        }

        private float ResolveClampPinMaxCorrectionPerIteration()
        {
            return setting != null ? setting.clampPinMaxCorrectionPerIteration : clampPinMaxCorrectionPerIteration;
        }

        private float ResolveClampPinFollowDamping()
        {
            return setting != null ? setting.clampPinFollowDamping : clampPinFollowDamping;
        }

        private float ResolveIncisionThicknessRelaxRadius()
        {
            return setting != null ? setting.incisionThicknessRelaxRadius : 0f;
        }

        private bool HasAnyIncisionPairs()
        {
            return (_outerIncisionStations != null && _outerIncisionStations.Count > 0)
                || (_innerIncisionStations != null && _innerIncisionStations.Count > 0)
                || (_outerIncisionStationCentersWorld != null && _outerIncisionStationCentersWorld.Count > 0)
                || (_innerIncisionStationCentersWorld != null && _innerIncisionStationCentersWorld.Count > 0)
                || (_outerIncisionCenterlineWorld != null && _outerIncisionCenterlineWorld.Count > 0)
                || (_innerIncisionCenterlineWorld != null && _innerIncisionCenterlineWorld.Count > 0)
                || (_outerIncisionLipPairs != null && _outerIncisionLipPairs.Count > 0)
                || (_innerIncisionLipPairs != null && _innerIncisionLipPairs.Count > 0);
        }

        private float EvaluateIncisionConnectivityBlend()
        {
            if (!HasAnyIncisionPairs())
                return 1f;

            int warmup = Mathf.Max(1, incisionSupportWarmupFrames);
            if (_incisionFramesSinceApply < 0)
                return 1f;

            float t = Mathf.Clamp01(_incisionFramesSinceApply / (float)warmup);
            return t * t * (3f - 2f * t);
        }

        private float ResolveIncisionThicknessRelaxScale()
        {
            float target = setting != null ? setting.incisionThicknessRelaxScale : 1f;
            if (!HasAnyIncisionPairs())
                return Mathf.Clamp01(target);

            if (severThicknessLinksAcrossIncision)
                target = Mathf.Max(target, Mathf.Clamp01(incisionMinimumSupportKeepScale));

            return Mathf.Clamp01(Mathf.Lerp(1f, target, EvaluateIncisionConnectivityBlend()));
        }

        private float ResolveIncisionSeparationRelaxScale()
        {
            float target = setting != null ? setting.incisionSeparationRelaxScale : 1f;
            if (!HasAnyIncisionPairs())
                return Mathf.Clamp01(target);

            if (severLayerSeparationAcrossIncision)
                target = Mathf.Max(target, Mathf.Clamp01(incisionMinimumSupportKeepScale));

            return Mathf.Clamp01(Mathf.Lerp(1f, target, EvaluateIncisionConnectivityBlend()));
        }

        private static float DeterministicUnit01(int a, int b)
        {
            unchecked
            {
                uint x = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9e3779b9u;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return (x & 0x00FFFFFFu) / 16777215.0f;
            }
        }

        private bool IsNearIncisionVertex(int globalIndex, Vector3 worldPos, float radiusWorld)
        {
            if (!HasAnyIncisionPairs() || radiusWorld <= 1e-6f)
                return false;

            if (IsOuterLayerGlobalIndex(globalIndex))
                return IsNearIncisionLayerWorld(worldPos, radiusWorld, _outerIncisionStationCentersWorld, _outerIncisionCenterlineWorld, _outerIncisionLipPairs, outerMeshFilter);

            if (IsInnerLayerGlobalIndex(globalIndex))
                return IsNearIncisionLayerWorld(worldPos, radiusWorld, _innerIncisionStationCentersWorld, _innerIncisionCenterlineWorld, _innerIncisionLipPairs, innerMeshFilter);

            return IsNearIncisionLayerWorld(worldPos, radiusWorld, _outerIncisionStationCentersWorld, _outerIncisionCenterlineWorld, _outerIncisionLipPairs, outerMeshFilter)
                || IsNearIncisionLayerWorld(worldPos, radiusWorld, _innerIncisionStationCentersWorld, _innerIncisionCenterlineWorld, _innerIncisionLipPairs, innerMeshFilter);
        }

        private static bool IsNearIncisionVertexForLayer(Vector3 worldPos, List<IncisionLipPair> pairs, Transform tr, float radiusWorld)
        {
            if (pairs == null || pairs.Count == 0 || tr == null || radiusWorld <= 1e-6f)
                return false;

            Matrix4x4 w2l = tr.worldToLocalMatrix;
            Vector3 localPos = w2l.MultiplyPoint3x4(worldPos);
            float r2 = radiusWorld * radiusWorld;
            for (int i = 0; i < pairs.Count; i++)
            {
                Vector3 d = localPos - pairs[i].centerLocal;
                if (d.sqrMagnitude <= r2)
                    return true;
            }
            return false;
        }

        private static bool IsNearIncisionStationForLayer(Vector3 worldPos, List<Vector3> stationCentersWorld, float radiusWorld)
        {
            if (stationCentersWorld == null || stationCentersWorld.Count == 0 || radiusWorld <= 1e-6f)
                return false;

            float r2 = radiusWorld * radiusWorld;
            for (int i = 0; i < stationCentersWorld.Count; i++)
            {
                if ((worldPos - stationCentersWorld[i]).sqrMagnitude <= r2)
                    return true;
            }
            return false;
        }


        private bool IsOuterLayerGlobalIndex(int globalIndex)
        {
            return globalIndex >= 0 && globalIndex < _outerCount;
        }

        private bool IsInnerLayerGlobalIndex(int globalIndex)
        {
            return globalIndex >= _outerCount && globalIndex < (_outerCount + _innerCount);
        }

        private bool IsNearIncisionLayerWorld(Vector3 worldPos, float radiusWorld, List<Vector3> stationCentersWorld, List<Vector3> centerlineWorld, List<IncisionLipPair> lipPairs, MeshFilter filter)
        {
            if (radiusWorld <= 1e-6f)
                return false;

            if (IsNearIncisionStationForLayer(worldPos, stationCentersWorld, radiusWorld))
                return true;

            float r2 = radiusWorld * radiusWorld;
            if (centerlineWorld != null && centerlineWorld.Count > 0 && DistancePointPolylineSq(worldPos, centerlineWorld) <= r2)
                return true;

            return IsNearIncisionVertexForLayer(worldPos, lipPairs, filter != null ? filter.transform : null, radiusWorld);
        }

        private float ResolveIncisionConstraintSuppressRadius()
        {
            float r = Mathf.Max(incisionBrushRadius * 1.25f, Mathf.Max(incisionOpenWidth * 2.0f, 0.0012f));
            if (setting != null)
                r = Mathf.Max(r, Mathf.Max(setting.incisionThicknessRelaxRadius, setting.needlePunctureRelaxRadius * 0.35f));
            return Mathf.Max(5e-4f, r);
        }

        private bool IsNearIncisionConstraintRibbon(int globalIndex, Vector3 worldPos, float radiusWorld)
        {
            if (!HasAnyIncisionPairs() || radiusWorld <= 1e-6f)
                return false;

            if (IsOuterLayerGlobalIndex(globalIndex))
                return IsNearIncisionLayerWorld(worldPos, radiusWorld, _outerIncisionStationCentersWorld, _outerIncisionCenterlineWorld, _outerIncisionLipPairs, outerMeshFilter);

            if (IsInnerLayerGlobalIndex(globalIndex))
                return IsNearIncisionLayerWorld(worldPos, radiusWorld, _innerIncisionStationCentersWorld, _innerIncisionCenterlineWorld, _innerIncisionLipPairs, innerMeshFilter);

            return IsNearIncisionLayerWorld(worldPos, radiusWorld, _outerIncisionStationCentersWorld, _outerIncisionCenterlineWorld, _outerIncisionLipPairs, outerMeshFilter)
                || IsNearIncisionLayerWorld(worldPos, radiusWorld, _innerIncisionStationCentersWorld, _innerIncisionCenterlineWorld, _innerIncisionLipPairs, innerMeshFilter);
        }

        //********************************************************************
        private void Awake()
        {
            if (outerMeshFilter == null || innerMeshFilter == null)
            {
                Debug.LogError("[DoubleLayerDriver] outerMeshFilter / innerMeshFilter is null.");
                enabled = false;
                return;
            }
            if (setting == null)
            {
                Debug.LogError("[DoubleLayerDriver] setting is null.");
                enabled = false;
                return;
            }

            InitSim();
        }

        private void OnEnable()
        {
            CleanupMiddleWallFillObjectsForCurrentMode();
#if UNITY_EDITOR
            SubscribeSceneGui();
            SubscribeEditorPreview();
            if (!Application.isPlaying)
            {
                MarkMiddleWallPreviewDirty();
                ScheduleEditorMiddleWallPreviewRefresh();
            }
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnsubscribeSceneGui();
            UnsubscribeEditorPreview();
            if (!Application.isPlaying)
                DestroyMiddleWallFillRenderer();
#endif
        }

        private void DestroyMiddleWallFillObjectByName(string objectName)
        {
            Transform child = transform.Find(objectName);
            if (child == null)
                return;

            if (_middleWallFillObject == child.gameObject)
            {
                _middleWallFillObject = null;
                _middleWallFillFilter = null;
                _middleWallFillRenderer = null;
            }

            if (!Application.isPlaying)
                DestroyImmediate(child.gameObject);
            else
                Destroy(child.gameObject);
        }

        private void CleanupMiddleWallFillObjectsForCurrentMode()
        {
            if (Application.isPlaying)
            {
                DestroyMiddleWallFillObjectByName("MiddleWallFillPreview");
            }
            else
            {
                DestroyMiddleWallFillObjectByName("MiddleWallFillRuntime");
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            MarkMiddleWallPreviewDirty();

            if (!Application.isPlaying)
            {
                if (proceduralIncisionLivePreview && enableProceduralIncisionAuthoring && !_isIncisionDragging)
                    TryGenerateProceduralIncisionStroke(clearExisting: true, logWarnings: false);

                // 不要在 OnValidate 里直接 AddComponent / 绑定 sharedMesh，
                // 否则 Unity 会报：SendMessage cannot be called during Awake, CheckConsistency, or OnValidate。
                // 这里仅标记脏并延迟到 delayCall / EditorApplication.update 中再创建和刷新预览物体。
                ScheduleEditorMiddleWallPreviewRefresh();
            }
        }
#endif


        private void CacheSourceMeshesIfNeeded()
        {
            if (outerMeshFilter != null && _outerSourceMesh == null)
                _outerSourceMesh = outerMeshFilter.sharedMesh;
            if (innerMeshFilter != null && _innerSourceMesh == null)
                _innerSourceMesh = innerMeshFilter.sharedMesh;
        }

#if UNITY_EDITOR
        private void SubscribeEditorPreview()
        {
            EditorApplication.update -= EditorUpdateMiddleWallPreview;
            if (!Application.isPlaying)
                EditorApplication.update += EditorUpdateMiddleWallPreview;
        }

        private void UnsubscribeEditorPreview()
        {
            EditorApplication.update -= EditorUpdateMiddleWallPreview;
        }

        private void MarkMiddleWallPreviewDirty()
        {
            _editorMiddleWallPreviewDirty = true;
        }

        private void ScheduleEditorMiddleWallPreviewRefresh()
        {
            EditorApplication.delayCall -= DelayedRefreshMiddleWallPreview;
            EditorApplication.delayCall += DelayedRefreshMiddleWallPreview;
        }

        private void DelayedRefreshMiddleWallPreview()
        {
            if (this == null || Application.isPlaying || !isActiveAndEnabled)
                return;

            RefreshMiddleWallPreviewInEditMode(forceRefresh: true);
        }

        private void EditorUpdateMiddleWallPreview()
        {
            if (this == null)
            {
                EditorApplication.update -= EditorUpdateMiddleWallPreview;
                return;
            }

            if (Application.isPlaying || !isActiveAndEnabled)
                return;

            bool dirty = _editorMiddleWallPreviewDirty;
            if (transform.hasChanged)
                dirty = true;
            if (outerMeshFilter != null && outerMeshFilter.transform != null && outerMeshFilter.transform.hasChanged)
                dirty = true;
            if (innerMeshFilter != null && innerMeshFilter.transform != null && innerMeshFilter.transform.hasChanged)
                dirty = true;

            if (!dirty)
                return;

            RefreshMiddleWallPreviewInEditMode(forceRefresh: true);

            transform.hasChanged = false;
            if (outerMeshFilter != null && outerMeshFilter.transform != null)
                outerMeshFilter.transform.hasChanged = false;
            if (innerMeshFilter != null && innerMeshFilter.transform != null)
                innerMeshFilter.transform.hasChanged = false;
        }
#endif

        private void PrepareRuntimeMeshesIfNeeded()
        {
            CacheSourceMeshesIfNeeded();

            if (_runtimeMeshesPrepared)
            {
                _outerMesh = outerMeshFilter != null ? outerMeshFilter.sharedMesh : null;
                _innerMesh = innerMeshFilter != null ? innerMeshFilter.sharedMesh : null;
                return;
            }

            Mesh baseOuter = _outerSourceMesh;
            Mesh baseInner = _innerSourceMesh;
            if (baseOuter == null || baseInner == null)
                return;

            Mesh workingOuter = cloneMeshAtRuntime ? Instantiate(baseOuter) : baseOuter;
            Mesh workingInner = cloneMeshAtRuntime ? Instantiate(baseInner) : baseInner;

            int effectiveOuterSubdivision =
                Mathf.Clamp(outerSubdivisionLevel, 0, 3);
            int effectiveInnerSubdivision =
                Mathf.Clamp(innerSubdivisionLevel, 0, 3);
            if (enforceRuntimeVertexBudget)
            {
                effectiveOuterSubdivision =
                    ResolveSubdivisionLevelWithinBudget(
                        baseOuter,
                        effectiveOuterSubdivision,
                        runtimeVertexBudgetPerLayer,
                        "Outer",
                        out int predictedOuterVertices);
                effectiveInnerSubdivision =
                    ResolveSubdivisionLevelWithinBudget(
                        baseInner,
                        effectiveInnerSubdivision,
                        runtimeVertexBudgetPerLayer,
                        "Inner",
                        out int predictedInnerVertices);

                int synchronizedLevel =
                    Mathf.Min(
                        effectiveOuterSubdivision,
                        effectiveInnerSubdivision);
                if (effectiveOuterSubdivision !=
                        effectiveInnerSubdivision)
                {
                    Debug.LogWarning(
                        $"[DoubleLayerDriver][RuntimeTopologyBudget] " +
                        $"Outer/Inner safe subdivision differs " +
                        $"({effectiveOuterSubdivision}/{effectiveInnerSubdivision}); " +
                        $"using synchronized level={synchronizedLevel}.");
                    effectiveOuterSubdivision = synchronizedLevel;
                    effectiveInnerSubdivision = synchronizedLevel;
                    predictedOuterVertices =
                        EstimateSubdividedVertexCount(
                            baseOuter,
                            synchronizedLevel);
                    predictedInnerVertices =
                        EstimateSubdividedVertexCount(
                            baseInner,
                            synchronizedLevel);
                }

                Debug.Log(
                    $"[DoubleLayerDriver][RuntimeTopologyBudget] " +
                    $"budgetPerLayer={Mathf.Max(128, runtimeVertexBudgetPerLayer)} " +
                    $"requested={outerSubdivisionLevel}/{innerSubdivisionLevel} " +
                    $"effective={effectiveOuterSubdivision}/{effectiveInnerSubdivision} " +
                    $"predictedVerts={predictedOuterVertices}/{predictedInnerVertices}");
            }

            if (effectiveOuterSubdivision > 0)
            {
                for (int i = 0;
                     i < effectiveOuterSubdivision;
                     i++)
                    SubdivideOnce(workingOuter);
            }
            if (effectiveInnerSubdivision > 0)
            {
                for (int i = 0;
                     i < effectiveInnerSubdivision;
                     i++)
                    SubdivideOnce(workingInner);
            }

            if (weldDuplicateVertices)
            {
                ClothMeshModifier.WeldDuplicateVertices(workingOuter, weldEpsilon);
                ClothMeshModifier.WeldDuplicateVertices(workingInner, weldEpsilon);
            }

            workingOuter.RecalculateNormals();
            workingOuter.RecalculateBounds();
            workingInner.RecalculateNormals();
            workingInner.RecalculateBounds();

            if (outerMeshFilter != null) outerMeshFilter.sharedMesh = workingOuter;
            if (innerMeshFilter != null) innerMeshFilter.sharedMesh = workingInner;

            _outerMesh = workingOuter;
            _innerMesh = workingInner;
            _runtimeMeshesPrepared = true;
        }

        private static int ResolveSubdivisionLevelWithinBudget(
            Mesh mesh,
            int requestedLevel,
            int vertexBudget,
            string layerName,
            out int predictedVertexCount)
        {
            int requested = Mathf.Clamp(requestedLevel, 0, 3);
            int budget = Mathf.Max(128, vertexBudget);
            int effective = requested;
            predictedVertexCount =
                EstimateSubdividedVertexCount(
                    mesh,
                    effective);
            while (effective > 0 &&
                   predictedVertexCount > budget)
            {
                effective--;
                predictedVertexCount =
                    EstimateSubdividedVertexCount(
                        mesh,
                        effective);
            }

            if (effective != requested)
            {
                Debug.LogWarning(
                    $"[DoubleLayerDriver][RuntimeTopologyBudget] " +
                    $"{layerName} subdivision reduced " +
                    $"{requested}->{effective}; " +
                    $"predictedVerts={predictedVertexCount} " +
                    $"budget={budget}.");
            }
            if (predictedVertexCount > budget)
            {
                Debug.LogWarning(
                    $"[DoubleLayerDriver][RuntimeTopologyBudget] " +
                    $"{layerName} native mesh already exceeds budget. " +
                    $"nativeVerts={predictedVertexCount} budget={budget}.");
            }
            return effective;
        }

        private static int EstimateSubdividedVertexCount(
            Mesh mesh,
            int subdivisionLevel)
        {
            if (mesh == null)
                return 0;

            int level = Mathf.Clamp(
                subdivisionLevel,
                0,
                3);
            if (level == 0)
                return mesh.vertexCount;

            int[] triangles = mesh.triangles;
            if (triangles == null ||
                triangles.Length < 3)
                return mesh.vertexCount;

            var edgeMultiplicity =
                new Dictionary<ulong, int>(
                    triangles.Length);
            for (int i = 0;
                 i + 2 < triangles.Length;
                 i += 3)
            {
                AddTopologyBudgetEdge(
                    edgeMultiplicity,
                    triangles[i],
                    triangles[i + 1]);
                AddTopologyBudgetEdge(
                    edgeMultiplicity,
                    triangles[i + 1],
                    triangles[i + 2]);
                AddTopologyBudgetEdge(
                    edgeMultiplicity,
                    triangles[i + 2],
                    triangles[i]);
            }

            long vertices = mesh.vertexCount;
            long faces = triangles.Length / 3;
            long edges = edgeMultiplicity.Count;
            long boundaryEdges = 0;
            foreach (KeyValuePair<ulong, int> edge in
                     edgeMultiplicity)
            {
                if (edge.Value == 1)
                    boundaryEdges++;
            }

            for (int iteration = 0;
                 iteration < level;
                 iteration++)
            {
                vertices += edges;
                faces *= 4;
                boundaryEdges *= 2;
                edges =
                    (3 * faces + boundaryEdges) / 2;
                if (vertices >= int.MaxValue)
                    return int.MaxValue;
            }
            return (int)vertices;
        }

        private static void AddTopologyBudgetEdge(
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

        /// <summary>
        /// (Re)initialize simulator and all native containers from current meshes.
        /// Call only on main thread "safe point" (after completing previous jobs).
        /// </summary>
        private void InitSim()
        {
            // Clean existing simulator if any
            if (_simulator != null)
            {
                _simulator.ComputeAsyncJobs();
                _simulator.Dispose();
                _simulator = null;
            }

            _elapsedTime = 0f;
            ResetIncisionRollbackState();

            PrepareRuntimeMeshesIfNeeded();

            _outerMesh = outerMeshFilter != null ? outerMeshFilter.sharedMesh : null;
            _innerMesh = innerMeshFilter != null ? innerMeshFilter.sharedMesh : null;
            if (_outerMesh == null || _innerMesh == null)
            {
                Debug.LogError("[DoubleLayerDriver] sharedMesh is null.");
                enabled = false;
                return;
            }

            _outerCount = _outerMesh.vertexCount;
            _innerCount = _innerMesh.vertexCount;

            // Build merged mesh modifier (world-space vertices)
            _meshModifier = ClothMeshModifier.CreateFromMeshes(_outerMesh, outerMeshFilter.transform, _innerMesh, innerMeshFilter.transform);
            _simulator = new ClothSimulator(_meshModifier, setting);
            ConfigureSutureSidewallConstraintsOnSimulator();

            int outerTriangleCount = (_outerMesh != null && _outerMesh.triangles != null) ? (_outerMesh.triangles.Length / 3) : 0;
            _simulator.SetDoubleLayerSlices(_outerCount, outerTriangleCount);

            // Register colliders
            if (colliderProxies != null)
            {
                foreach (var c in colliderProxies)
                {
                    if (c != null) _simulator.AddCollider(c);
                }
            }

            // Integrated pin ends from validated longitudinal material
            // coordinates. Curved vessels must not be reclassified by local Z.
            ApplyPinEndsLongitudinalMaterial();

            // Cross-layer thickness links from synchronized topology identity.
            BuildThicknessLinksByTopologyIdentity();

            // Layer separation hard inequality (Outer must stay outside Inner)
            if (enableLayerSeparation)
            {
                BuildLayerSeparationConstraintsByTopologyIdentity();
            }

            BuildMiddleWallBoundaryCache();

            Debug.Log($"[DoubleLayerDriver] longitudinal=materialUV, crossLayer=topologyIdentity, 4-link={(use4Link ? "ON" : "OFF")}, thickness=XPBD (compliance in ClothSetting)");
            Debug.Log($"[DoubleLayerDriver] outerVerts={_outerCount}, innerVerts={_innerCount}, total={_simulator.vertexCount}");
        }

        private void Start()
        {
            CleanupMiddleWallFillObjectsForCurrentMode();
            // Keep your previous shader keyword behavior
            TryEnableWorldVertexKeyword(outerMeshFilter);
            TryEnableWorldVertexKeyword(innerMeshFilter);

            if (Application.isPlaying && autoApplyProceduralIncisionOnStart && !_proceduralIncisionAutoApplied)
            {
                bool applied = TryApplyProceduralIncisionDirectRuntimeOnce();
                _proceduralIncisionAutoApplied = applied;
                if (applied)
                    Debug.Log("[DoubleLayerDriver] Play 开始已按当前参数直接生成并提交一次切口（不依赖编辑态预览）。");
            }
        }

        private void OnDestroy()
        {
            if (_simulator != null)
            {
                _simulator.Dispose();
                _simulator = null;
            }

            TryDisableWorldVertexKeyword(outerMeshFilter);
            TryDisableWorldVertexKeyword(innerMeshFilter);
            DestroyIncisionSidewallRenderer();
            DestroyIncisionMarkerRenderer();
            DestroyMiddleWallFillRenderer();
        }



        // ---------------- public API (optional, for interactive pin) ----------------
        public void Attach(int globalVertexIndex, Transform target)
        {
            if (target == null) throw new Exception("target can not be null");
            _attachments[globalVertexIndex] = target;

            if (_simulator != null && globalVertexIndex >= 0 && globalVertexIndex < _simulator.positions.Length)
            {
                target.position = _simulator.positions[globalVertexIndex];
            }
        }

        public void AttachToInitialPosition(int globalVertexIndex)
        {
            if (_simulator == null) return;
            if (globalVertexIndex < 0 || globalVertexIndex >= _simulator.positions.Length) return;
            _simulator.AddPinConstraint(globalVertexIndex, _simulator.positions[globalVertexIndex]);
        }

        public void GetLayerSlice(VesselLayer layer, out int start, out int count)
        {
            if (layer == VesselLayer.Outer)
            {
                start = OuterStart;
                count = OuterCount;
            }
            else
            {
                start = InnerStart;
                count = InnerCount;
            }
        }

        public int PendingIncisionPointCount
        {
            get { return _incisionStrokeWorld != null ? _incisionStrokeWorld.Count : 0; }
        }

#if UNITY_EDITOR
        public IReadOnlyList<Vector3> PendingIncisionStrokeWorld => _incisionStrokeWorld;

        private void ResetIncisionRollbackState(bool destroyRuntimeRenderers = true)
        {
            if (_incisionStrokeWorld == null)
                _incisionStrokeWorld = new List<Vector3>();
            _incisionStrokeWorld.Clear();

            _outerIncisionLipPairs.Clear();
            _innerIncisionLipPairs.Clear();
            _outerIncisionStations.Clear();
            _innerIncisionStations.Clear();
            _outerIncisionCenterlineWorld.Clear();
            _innerIncisionCenterlineWorld.Clear();
            _outerIncisionStationCentersWorld.Clear();
            _innerIncisionStationCentersWorld.Clear();
            _incisionMasterChainWorld.Clear();
            _incisionCenterCurveWorld.Clear();
            _outerIncisionLeftBoundaryWorld.Clear();
            _outerIncisionRightBoundaryWorld.Clear();
            _innerIncisionLeftBoundaryWorld.Clear();
            _innerIncisionRightBoundaryWorld.Clear();
            _incisionOuterPatchComponentCount = 0;
            _incisionInnerPatchComponentCount = 0;
            _incisionOuterQualitySummary = string.Empty;
            _incisionInnerQualitySummary = string.Empty;
            _incisionDebugPoints.Clear();
            _incisionDebugCenterline.Clear();
            _incisionDebugTriangles.Clear();
            _incisionDebugRibbonFrames.Clear();
            _incisionDeleteDebugSummaries.Clear();
            _runtimeCommittedIncisionDebugRibbonFrames.Clear();
            _runtimeCommittedIncisionDebugTriangles.Clear();
            _runtimeCommittedIncisionDeleteDebugSummaries.Clear();
            _runtimeCommittedIncisionTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionAllTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionFinalTriangleAuditRecords.Clear();
            _incisionInsertedVertices.Clear();
            _incisionPairDebugPoints.Clear();
            _incisionRenderSampleDebugPoints.Clear();
            _incisionTargetTraceDebugRecords.Clear();
            _incisionAllTargetTraceDebugRecords.Clear();
            _incisionFinalTriangleAuditRecords.Clear();
            ClearRuntimePrecommitGeometry();

            _incisionFramesSinceApply = int.MaxValue;
            _incisionNaturalRetractionStartedLogged = false;
            _incisionNaturalRetractionUpdateLogCounter = 0;
            _incisionRebuildPending = false;
            _isIncisionDragging = false;
            _proceduralIncisionAutoApplied = false;
            _runtimeIncisionCommittedOnce = false;

            if (destroyRuntimeRenderers)
            {
                ClearIncisionMarkerMesh();
                ClearIncisionSidewallMesh();
                DestroyIncisionMarkerRenderer();
                DestroyIncisionSidewallRenderer();
            }
        }

        public void StopIncisionDraggingFromEditor()
        {
            _isIncisionDragging = false;
        }

        public void AppendIncisionStrokePointWorld(Vector3 worldPoint)
        {
            AppendIncisionPoint(worldPoint);
        }

        public bool TryPickOuterSurfaceFromGUI(Vector2 guiPos, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            return TryPickOuterSurface(guiPos, out hitPoint, out hitNormal);
        }
#endif

        public IReadOnlyList<Vector3> IncisionTargetStrokeWorld => _incisionStrokeWorld;
        public IReadOnlyList<Vector3> IncisionMasterChainWorld => _incisionMasterChainWorld;
        public IReadOnlyList<Vector3> IncisionCenterCurveWorld => _incisionCenterCurveWorld;
        public IReadOnlyList<Vector3> IncisionOuterLeftBoundaryWorld => _outerIncisionLeftBoundaryWorld;
        public IReadOnlyList<Vector3> IncisionOuterRightBoundaryWorld => _outerIncisionRightBoundaryWorld;
        public IReadOnlyList<Vector3> IncisionInnerLeftBoundaryWorld => _innerIncisionLeftBoundaryWorld;
        public IReadOnlyList<Vector3> IncisionInnerRightBoundaryWorld => _innerIncisionRightBoundaryWorld;
        public IReadOnlyList<Vector3> IncisionOuterStationCentersWorld => _outerIncisionStationCentersWorld;
        public IReadOnlyList<Vector3> IncisionInnerStationCentersWorld => _innerIncisionStationCentersWorld;
        public IReadOnlyList<Vector3> RuntimePrecommitRowCentersOuter => _runtimePrecommitRowCentersOuter;
        public IReadOnlyList<Vector3> RuntimePrecommitLeftBoundaryOuter => _runtimePrecommitLeftBoundaryOuter;
        public IReadOnlyList<Vector3> RuntimePrecommitRightBoundaryOuter => _runtimePrecommitRightBoundaryOuter;
        public IReadOnlyList<Vector3> RuntimePrecommitLeftSupportOuter => _runtimePrecommitLeftSupportOuter;
        public IReadOnlyList<Vector3> RuntimePrecommitRightSupportOuter => _runtimePrecommitRightSupportOuter;
        public IReadOnlyList<Vector3> RuntimePrecommitFailedInterfaceEdgesOuter => _runtimePrecommitFailedInterfaceEdgesOuter;
        public IReadOnlyList<string> RuntimePrecommitFailedInterfaceEdgeLabelsOuter => _runtimePrecommitFailedInterfaceEdgeLabelsOuter;
        public IReadOnlyList<Vector3> RuntimePrecommitRowCentersInner => _runtimePrecommitRowCentersInner;
        public IReadOnlyList<Vector3> RuntimePrecommitLeftBoundaryInner => _runtimePrecommitLeftBoundaryInner;
        public IReadOnlyList<Vector3> RuntimePrecommitRightBoundaryInner => _runtimePrecommitRightBoundaryInner;
        public IReadOnlyList<Vector3> RuntimePrecommitLeftSupportInner => _runtimePrecommitLeftSupportInner;
        public IReadOnlyList<Vector3> RuntimePrecommitRightSupportInner => _runtimePrecommitRightSupportInner;
        public IReadOnlyList<Vector3> RuntimePrecommitFailedInterfaceEdgesInner => _runtimePrecommitFailedInterfaceEdgesInner;
        public IReadOnlyList<string> RuntimePrecommitFailedInterfaceEdgeLabelsInner => _runtimePrecommitFailedInterfaceEdgeLabelsInner;
        public string RuntimePrecommitProjectionAlignOuter => _runtimePrecommitProjectionAlignOuter;
        public string RuntimePrecommitProjectionAlignInner => _runtimePrecommitProjectionAlignInner;
        public int IncisionOuterPatchComponentCount => _incisionOuterPatchComponentCount;
        public int IncisionInnerPatchComponentCount => _incisionInnerPatchComponentCount;
        public string IncisionOuterQualitySummary => _incisionOuterQualitySummary;
        public string IncisionInnerQualitySummary => _incisionInnerQualitySummary;
        public IReadOnlyList<Vector3> IncisionDebugCenterline => _incisionDebugCenterline;
        public IReadOnlyList<IncisionDebugPoint> IncisionDebugPoints => _incisionDebugPoints;
        public IReadOnlyList<IncisionDebugTriangle> IncisionDebugTriangles => _incisionDebugTriangles;
        public IReadOnlyList<IncisionDebugRibbonFrame> IncisionDebugRibbonFrames => _incisionDebugRibbonFrames;
        public IReadOnlyList<IncisionDeleteDebugSummary> IncisionDeleteDebugSummaries => _incisionDeleteDebugSummaries;
        public IReadOnlyList<IncisionInsertedVertexDebugPoint> IncisionInsertedVertices => _incisionInsertedVertices;
        public IReadOnlyList<IncisionPairDebugPoint> IncisionPairDebugPoints => _incisionPairDebugPoints;
        public IReadOnlyList<IncisionRenderSampleDebugPoint> IncisionRenderSampleDebugPoints => _incisionRenderSampleDebugPoints;
        public IReadOnlyList<IncisionTargetTraceDebugRecord> IncisionTargetTraceDebugRecords => _incisionTargetTraceDebugRecords;
        public IReadOnlyList<IncisionTargetTraceDebugRecord> IncisionAllTargetTraceDebugRecords => _incisionAllTargetTraceDebugRecords;
        public IReadOnlyList<IncisionFinalTriangleAuditRecord> IncisionFinalTriangleAuditRecords => _incisionFinalTriangleAuditRecords;

        private void ClearRuntimePrecommitGeometry()
        {
            _runtimePrecommitRowCentersOuter.Clear();
            _runtimePrecommitLeftBoundaryOuter.Clear();
            _runtimePrecommitRightBoundaryOuter.Clear();
            _runtimePrecommitLeftSupportOuter.Clear();
            _runtimePrecommitRightSupportOuter.Clear();
            _runtimePrecommitFailedInterfaceEdgesOuter.Clear();
            _runtimePrecommitFailedInterfaceEdgeLabelsOuter.Clear();
            _runtimePrecommitRowCentersInner.Clear();
            _runtimePrecommitLeftBoundaryInner.Clear();
            _runtimePrecommitRightBoundaryInner.Clear();
            _runtimePrecommitLeftSupportInner.Clear();
            _runtimePrecommitRightSupportInner.Clear();
            _runtimePrecommitFailedInterfaceEdgesInner.Clear();
            _runtimePrecommitFailedInterfaceEdgeLabelsInner.Clear();
            _runtimePrecommitProjectionAlignOuter = string.Empty;
            _runtimePrecommitProjectionAlignInner = string.Empty;
        }

        private void ClearSuturePassSequenceData()
        {
            _suturePortalSequenceRecords.Clear();
            _suturePassSequenceRecords.Clear();
            _suturePassSequenceSummary = string.Empty;
            _sutureExpectedNextSide = SutureVesselSide.Unknown;
            _sutureExpectedNextDirection = SuturePassDirection.Unknown;
        }

        private void RebuildSuturePassSequenceState()
        {
            ClearSuturePassSequenceData();
            if (!enableSuturePassSequenceGuide)
                return;
            if (_simulator == null)
                return;

            int historyCount = _simulator.portalHistoryCount;
            if (historyCount <= 0)
            {
                _suturePassSequenceSummary = "SuturePass: waiting for first portal.";
                return;
            }

            List<PendingSuturePassBuild> passes = new List<PendingSuturePassBuild>(Mathf.Max(4, historyCount / 2 + 1));

            for (int i = 0; i < historyCount; i++)
            {
                NeedlePortalHistoryEntry entry = _simulator.GetPortalHistory(i);
                NeedlePortalInfo portal = entry.portal;
                if (portal.enabled == 0)
                    continue;

                NeedlePortalWallLayer layer = (NeedlePortalWallLayer)portal.wallLayer;
                if (layer != NeedlePortalWallLayer.Outer && layer != NeedlePortalWallLayer.Inner)
                    continue;

                Vector3 worldPoint = new Vector3(portal.currentPoint.x, portal.currentPoint.y, portal.currentPoint.z);
                bool sideValid = TryClassifySutureSide(worldPoint, out SutureSideClassifyResult sideResult);
                bool isExit = entry.isExitPortal != 0;
                SuturePassDirection direction = isExit ? SuturePassDirection.InnerToOuter : SuturePassDirection.OuterToInner;

                SuturePortalSequenceRecord portalRecord = new SuturePortalSequenceRecord();
                portalRecord.historyIndex = i;
                portalRecord.portalIndex = entry.portalIndex;
                portalRecord.passIndex = entry.passIndex;
                portalRecord.colliderEntityId = entry.colliderEntityId;
                portalRecord.wallLayer = layer;
                portalRecord.passDirection = direction;
                portalRecord.side = sideValid ? sideResult.side : SutureVesselSide.Unknown;
                if (entry.stitchState == (byte)NeedlePortalStitchState.Done)
                    portalRecord.phase = SutureSequencePhase.Done;
                else if (entry.stitchState == (byte)NeedlePortalStitchState.Hist)
                    portalRecord.phase = SutureSequencePhase.History;
                else
                    portalRecord.phase = entry.isActive != 0 ? SutureSequencePhase.Current : SutureSequencePhase.History;
                portalRecord.worldPoint = worldPoint;
                portalRecord.distanceToSide = sideValid ? sideResult.distanceToSide : float.PositiveInfinity;
                portalRecord.sideValid = sideValid;
                portalRecord.isExitPortal = isExit;
                _suturePortalSequenceRecords.Add(portalRecord);

                int keyPass = entry.passIndex > 0 ? entry.passIndex : portalRecord.portalIndex;
                int passIndex = FindPendingSuturePass(passes, keyPass, entry.colliderEntityId);
                PendingSuturePassBuild pass;
                if (passIndex >= 0)
                {
                    pass = passes[passIndex];
                }
                else
                {
                    pass = default(PendingSuturePassBuild);
                    pass.passIndex = keyPass;
                    pass.colliderEntityId = entry.colliderEntityId;
                    passes.Add(pass);
                    passIndex = passes.Count - 1;
                }

                pass.isCurrent = pass.isCurrent || entry.isActive != 0;
                pass.hasExitFlag = true;
                pass.isExitPass = isExit;
                if (layer == NeedlePortalWallLayer.Outer)
                {
                    pass.hasOuter = true;
                    pass.outerWorld = worldPoint;
                }
                else
                {
                    pass.hasInner = true;
                    pass.innerWorld = worldPoint;
                }
                passes[passIndex] = pass;
            }

            if (passes.Count <= 0)
            {
                _suturePassSequenceSummary = "SuturePass: no valid outer/inner portal in history.";
                return;
            }

            passes.Sort(ComparePendingSuturePass);

            SutureVesselSide previousCompletedSide = SutureVesselSide.Unknown;
            SuturePassDirection previousCompletedDirection = SuturePassDirection.Unknown;
            int completeCount = 0;
            int currentCount = 0;
            int violationCount = 0;

            for (int i = 0; i < passes.Count; i++)
            {
                PendingSuturePassBuild pass = passes[i];
                SuturePassSequenceRecord record = new SuturePassSequenceRecord();
                record.passIndex = pass.passIndex;
                record.colliderEntityId = pass.colliderEntityId;
                record.isCurrent = pass.isCurrent;
                record.hasOuter = pass.hasOuter;
                record.hasInner = pass.hasInner;
                record.isComplete = pass.hasOuter && pass.hasInner;
                record.outerWorld = pass.outerWorld;
                record.innerWorld = pass.innerWorld;
                record.direction = pass.hasExitFlag && pass.isExitPass ? SuturePassDirection.InnerToOuter : SuturePassDirection.OuterToInner;
                record.expectedSide = previousCompletedSide == SutureVesselSide.Unknown ? SutureVesselSide.Unknown : OppositeSutureSide(previousCompletedSide);
                record.expectedDirection = previousCompletedDirection == SuturePassDirection.Unknown ? SuturePassDirection.Unknown : OppositeSutureDirection(previousCompletedDirection);

                if (record.isComplete)
                    record.centerWorld = (pass.outerWorld + pass.innerWorld) * 0.5f;
                else if (pass.hasOuter)
                    record.centerWorld = pass.outerWorld;
                else
                    record.centerWorld = pass.innerWorld;

                if (TryClassifySutureSide(record.centerWorld, out SutureSideClassifyResult sideResult))
                {
                    record.side = sideResult.side;
                    record.distanceToSide = sideResult.distanceToSide;
                }
                else
                {
                    record.side = SutureVesselSide.Unknown;
                    record.distanceToSide = float.PositiveInfinity;
                }

                bool hasExpected = record.expectedSide != SutureVesselSide.Unknown || record.expectedDirection != SuturePassDirection.Unknown;
                bool sideOk = record.expectedSide == SutureVesselSide.Unknown || record.side == record.expectedSide;
                bool directionOk = record.expectedDirection == SuturePassDirection.Unknown || record.direction == record.expectedDirection;

                if (!hasExpected)
                    record.status = record.isComplete ? SutureSequenceStatus.FirstPassNoExpectation : SutureSequenceStatus.WaitingForPair;
                else if (!record.isComplete)
                    record.status = (sideOk && directionOk) ? SutureSequenceStatus.WaitingForPair : BuildWrongSutureStatus(sideOk, directionOk);
                else if (sideOk && directionOk)
                    record.status = SutureSequenceStatus.ExpectedMatched;
                else
                    record.status = BuildWrongSutureStatus(sideOk, directionOk);

                if (IsSutureStatusViolation(record.status))
                    violationCount++;
                if (record.isCurrent)
                    currentCount++;

                record.label = BuildSuturePassSequenceLabel(record);
                _suturePassSequenceRecords.Add(record);

                if (record.isComplete)
                {
                    completeCount++;
                    previousCompletedSide = record.side;
                    previousCompletedDirection = record.direction;
                }
            }

            _sutureExpectedNextSide = previousCompletedSide == SutureVesselSide.Unknown
                ? SutureVesselSide.Unknown
                : OppositeSutureSide(previousCompletedSide);
            _sutureExpectedNextDirection = previousCompletedDirection == SuturePassDirection.Unknown
                ? SuturePassDirection.Unknown
                : OppositeSutureDirection(previousCompletedDirection);

            string expectText = _sutureExpectedNextSide == SutureVesselSide.Unknown
                ? "next=none(first pass not completed)"
                : $"next={GetSuturePassDirectionLabel(_sutureExpectedNextDirection)} {GetSutureSideDisplayName(_sutureExpectedNextSide)}";
            _suturePassSequenceSummary = $"SuturePass: portals={_suturePortalSequenceRecords.Count} passes={_suturePassSequenceRecords.Count} complete={completeCount} current={currentCount} violations={violationCount} {expectText}";
        }

        private static int FindPendingSuturePass(List<PendingSuturePassBuild> passes, int passIndex, int colliderEntityId)
        {
            for (int i = 0; i < passes.Count; i++)
            {
                if (passes[i].passIndex == passIndex && passes[i].colliderEntityId == colliderEntityId)
                    return i;
            }
            return -1;
        }

        private static int ComparePendingSuturePass(PendingSuturePassBuild a, PendingSuturePassBuild b)
        {
            int passCompare = a.passIndex.CompareTo(b.passIndex);
            if (passCompare != 0)
                return passCompare;
            return a.colliderEntityId.CompareTo(b.colliderEntityId);
        }

        private static SutureVesselSide OppositeSutureSide(SutureVesselSide side)
        {
            if (side == SutureVesselSide.SideA) return SutureVesselSide.SideB;
            if (side == SutureVesselSide.SideB) return SutureVesselSide.SideA;
            return SutureVesselSide.Unknown;
        }

        private static SuturePassDirection OppositeSutureDirection(SuturePassDirection direction)
        {
            if (direction == SuturePassDirection.OuterToInner) return SuturePassDirection.InnerToOuter;
            if (direction == SuturePassDirection.InnerToOuter) return SuturePassDirection.OuterToInner;
            return SuturePassDirection.Unknown;
        }

        private static SutureSequenceStatus BuildWrongSutureStatus(bool sideOk, bool directionOk)
        {
            if (!sideOk && !directionOk) return SutureSequenceStatus.WrongSideAndDirection;
            if (!sideOk) return SutureSequenceStatus.WrongSide;
            if (!directionOk) return SutureSequenceStatus.WrongDirection;
            return SutureSequenceStatus.ExpectedMatched;
        }

        private static bool IsSutureStatusViolation(SutureSequenceStatus status)
        {
            return status == SutureSequenceStatus.WrongSide ||
                   status == SutureSequenceStatus.WrongDirection ||
                   status == SutureSequenceStatus.WrongSideAndDirection;
        }

        public string GetSuturePassDirectionLabel(SuturePassDirection direction)
        {
            switch (direction)
            {
                case SuturePassDirection.OuterToInner:
                    return "外壁->内壁";
                case SuturePassDirection.InnerToOuter:
                    return "内壁->外壁";
                default:
                    return "方向未知";
            }
        }

        public string GetSutureSequenceStatusLabel(SutureSequenceStatus status)
        {
            switch (status)
            {
                case SutureSequenceStatus.FirstPassNoExpectation:
                    return "第一针/无期望";
                case SutureSequenceStatus.WaitingForPair:
                    return "当前针未成对";
                case SutureSequenceStatus.ExpectedMatched:
                    return "符合期望";
                case SutureSequenceStatus.WrongSide:
                    return "侧别错误";
                case SutureSequenceStatus.WrongDirection:
                    return "方向错误";
                case SutureSequenceStatus.WrongSideAndDirection:
                    return "侧别+方向错误";
                default:
                    return "未知";
            }
        }

        private string BuildSuturePassSequenceLabel(SuturePassSequenceRecord record)
        {
            string phase = record.isCurrent ? "Current" : "Hist";
            string sideText = GetSutureSideDisplayName(record.side);
            string directionText = GetSuturePassDirectionLabel(record.direction);
            string expectedText = record.expectedSide == SutureVesselSide.Unknown && record.expectedDirection == SuturePassDirection.Unknown
                ? "exp=none"
                : $"exp={GetSuturePassDirectionLabel(record.expectedDirection)} {GetSutureSideDisplayName(record.expectedSide)}";
            string pairText = record.isComplete ? "pair" : (record.hasOuter ? "outer-only" : "inner-only");
            return $"{phase} P{record.passIndex} {directionText} {sideText} {pairText} {GetSutureSequenceStatusLabel(record.status)} {expectedText}";
        }

        private void ClearSutureSideGuides()
        {
            ClearSuturePassSequenceData();
            _sutureSideGuideValid = false;
            _sutureSideGuideMode = SutureSideGuideMode.None;
            _sutureSideGuideSummary = string.Empty;
            _sutureSideAGuideCenterWorld = default(Vector3);
            _sutureSideBGuideCenterWorld = default(Vector3);
            _sutureSideAOuterBoundaryWorld.Clear();
            _sutureSideBOuterBoundaryWorld.Clear();
            _sutureSideAInnerBoundaryWorld.Clear();
            _sutureSideBInnerBoundaryWorld.Clear();
            _sutureSidewallBoundaryEdges.Clear();
            _simulator?.ClearSutureBoundaryGuides();
        }

        private void RebuildSutureSideGuidesFromCurrentIncisionState()
        {
            ClearSutureSideGuides();
            if (!enableSutureSideGuide)
                return;

            bool separationMode = incisionCircumferentialSeparationCut;
            bool swapOuter = false;
            bool swapInner = false;

            if (separationMode)
            {
                if (TryGetSutureBoundaryPairDirection(
                        _outerIncisionLeftBoundaryWorld,
                        _outerIncisionRightBoundaryWorld,
                        out Vector3 outerDirection) &&
                    TryGetSutureBoundaryPairDirection(
                        _innerIncisionLeftBoundaryWorld,
                        _innerIncisionRightBoundaryWorld,
                        out Vector3 innerDirection) &&
                    Vector3.Dot(outerDirection, innerDirection) < 0f)
                {
                    // Outer is the bank-order truth source; only repair an
                    // explicitly reversed Inner ordering.
                    swapInner = true;
                }
            }

            CopySutureSidePair(_outerIncisionLeftBoundaryWorld, _outerIncisionRightBoundaryWorld,
                _sutureSideAOuterBoundaryWorld, _sutureSideBOuterBoundaryWorld, swapOuter);
            CopySutureSidePair(_innerIncisionLeftBoundaryWorld, _innerIncisionRightBoundaryWorld,
                _sutureSideAInnerBoundaryWorld, _sutureSideBInnerBoundaryWorld, swapInner);

            int sideACount = _sutureSideAOuterBoundaryWorld.Count + _sutureSideAInnerBoundaryWorld.Count;
            int sideBCount = _sutureSideBOuterBoundaryWorld.Count + _sutureSideBInnerBoundaryWorld.Count;
            _sutureSideGuideValid = sideACount >= 2 && sideBCount >= 2;
            if (!_sutureSideGuideValid)
            {
                ClearSutureSideGuides();
                return;
            }

            _sutureSideGuideMode = separationMode
                ? SutureSideGuideMode.CircumferentialProximalDistal
                : SutureSideGuideMode.SlitLeftRight;
            _sutureSideAGuideCenterWorld = ComputeAveragePoint(_sutureSideAOuterBoundaryWorld, _sutureSideAInnerBoundaryWorld);
            _sutureSideBGuideCenterWorld = ComputeAveragePoint(_sutureSideBOuterBoundaryWorld, _sutureSideBInnerBoundaryWorld);
            BuildSutureSidewallBoundaryEdges(swapOuter, swapInner, out int sidewallMissing);

            float guideGap = Vector3.Distance(_sutureSideAGuideCenterWorld, _sutureSideBGuideCenterWorld);
            string modeText = separationMode
                ? "CircumCut SideA=Proximal SideB=Distal stationOrder=topology"
                : "Slit SideA=Left SideB=Right";
            _sutureSideGuideSummary = $"{modeText} Apts={sideACount} Bpts={sideBCount} gap={guideGap:F5} sidewallEdges={_sutureSidewallBoundaryEdges.Count} sidewallMissing={sidewallMissing}";
            _simulator?.SetSutureBoundaryGuides(
                _sutureSideAOuterBoundaryWorld,
                _sutureSideBOuterBoundaryWorld,
                _sutureSideAInnerBoundaryWorld,
                _sutureSideBInnerBoundaryWorld);
            ConfigureSutureSidewallConstraintsOnSimulator();
            _simulator?.SetSutureSidewallBoundaryEdges(_sutureSidewallBoundaryEdges);
            if (_simulator != null)
            {
                _sutureSideGuideSummary +=
                    $" sidewallMarked={_simulator.sutureSidewallMarkedParticleCount} sidewallDistance={_simulator.sutureSidewallDistanceConstraintCount} sidewallShear={_simulator.sutureSidewallShearConstraintCount}";
            }
        }

        private void ConfigureSutureSidewallConstraintsOnSimulator()
        {
            _simulator?.ConfigureSutureSidewallConstraints(
                enableIncisionVirtualSidewallConstraints,
                incisionVirtualSidewallDistanceScale,
                incisionVirtualSidewallDistanceStiffness,
                incisionVirtualSidewallShearStiffness);
        }

        private void BuildSutureSidewallBoundaryEdges(bool swapOuter, bool swapInner, out int missingIndexCount)
        {
            _sutureSidewallBoundaryEdges.Clear();
            missingIndexCount = 0;

            var added = new HashSet<ulong>();
            int sourceEdgeId = 0;
            AppendSutureSidewallBoundaryEdges(
                SutureSidewallBoundaryEdgeInfo.SideA,
                !swapOuter,
                !swapInner,
                added,
                ref sourceEdgeId,
                ref missingIndexCount);
            AppendSutureSidewallBoundaryEdges(
                SutureSidewallBoundaryEdgeInfo.SideB,
                swapOuter,
                swapInner,
                added,
                ref sourceEdgeId,
                ref missingIndexCount);

            string source = "incision-station-index";
            if (_sutureSidewallBoundaryEdges.Count == 0)
            {
                AppendSutureSidewallBoundaryEdgesFromBoundaryChains(
                    SutureSidewallBoundaryEdgeInfo.SideA,
                    swapOuter ? _outerIncisionRightBoundaryWorld : _outerIncisionLeftBoundaryWorld,
                    swapInner ? _innerIncisionRightBoundaryWorld : _innerIncisionLeftBoundaryWorld,
                    added,
                    ref sourceEdgeId,
                    ref missingIndexCount);
                AppendSutureSidewallBoundaryEdgesFromBoundaryChains(
                    SutureSidewallBoundaryEdgeInfo.SideB,
                    swapOuter ? _outerIncisionLeftBoundaryWorld : _outerIncisionRightBoundaryWorld,
                    swapInner ? _innerIncisionLeftBoundaryWorld : _innerIncisionRightBoundaryWorld,
                    added,
                    ref sourceEdgeId,
                    ref missingIndexCount);
                source = "boundary-chain-index";
            }

            Debug.Log($"[Suture][VirtualSidewall] edges={_sutureSidewallBoundaryEdges.Count} missing={missingIndexCount} enableMiddleWallFillRender={enableMiddleWallFillRender} source={source}");
        }

        private void AppendSutureSidewallBoundaryEdgesFromBoundaryChains(
            byte side,
            IReadOnlyList<Vector3> outerBoundaryWorld,
            IReadOnlyList<Vector3> innerBoundaryWorld,
            HashSet<ulong> added,
            ref int sourceEdgeId,
            ref int missingIndexCount)
        {
            int count = Mathf.Min(outerBoundaryWorld != null ? outerBoundaryWorld.Count : 0,
                innerBoundaryWorld != null ? innerBoundaryWorld.Count : 0);
            if (count < 2 || outerMeshFilter == null || innerMeshFilter == null ||
                _outerMesh == null || _innerMesh == null)
            {
                missingIndexCount++;
                Debug.LogWarning($"[Suture][VirtualSidewall] reason=boundary-chain-index-missing side={side} outerPts={(outerBoundaryWorld != null ? outerBoundaryWorld.Count : 0)} innerPts={(innerBoundaryWorld != null ? innerBoundaryWorld.Count : 0)}");
                return;
            }

            float maxSqrDistance = ResolveVirtualSidewallBoundaryIndexMaxDistance();
            int appended = 0;
            for (int i = 1; i < count; i++)
            {
                if (!TryFindNearestMeshVertexIndex(_outerMesh, outerMeshFilter.transform, outerBoundaryWorld[i - 1], maxSqrDistance, out int outerA) ||
                    !TryFindNearestMeshVertexIndex(_outerMesh, outerMeshFilter.transform, outerBoundaryWorld[i], maxSqrDistance, out int outerB) ||
                    !TryFindNearestMeshVertexIndex(_innerMesh, innerMeshFilter.transform, innerBoundaryWorld[i - 1], maxSqrDistance, out int innerA) ||
                    !TryFindNearestMeshVertexIndex(_innerMesh, innerMeshFilter.transform, innerBoundaryWorld[i], maxSqrDistance, out int innerB))
                {
                    missingIndexCount++;
                    continue;
                }

                if (!TryAppendSutureSidewallBoundaryEdge(
                        outerA,
                        outerB,
                        innerA,
                        innerB,
                        side,
                        sourceEdgeId,
                        added,
                        ref missingIndexCount))
                {
                    continue;
                }

                sourceEdgeId++;
                appended++;
            }

            if (appended == 0)
                Debug.LogWarning($"[Suture][VirtualSidewall] reason=boundary-chain-index-empty side={side} samples={count} maxDist={Mathf.Sqrt(maxSqrDistance):F6}");
        }

        private float ResolveVirtualSidewallBoundaryIndexMaxDistance()
        {
            float maxDistance = Mathf.Max(incisionBrushRadius * 3f, incisionPairMinSpacing * 3f);
            maxDistance = Mathf.Max(maxDistance, incisionOpenWidth * 6f);
            maxDistance = Mathf.Max(maxDistance, 0.003f);
            return maxDistance * maxDistance;
        }

        private static bool TryFindNearestMeshVertexIndex(
            Mesh mesh,
            Transform meshTransform,
            Vector3 worldPoint,
            float maxSqrDistance,
            out int index)
        {
            index = -1;
            if (mesh == null || meshTransform == null)
                return false;

            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0)
                return false;

            float best = float.PositiveInfinity;
            int bestIndex = -1;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = meshTransform.TransformPoint(vertices[i]);
                float sqr = (world - worldPoint).sqrMagnitude;
                if (sqr >= best)
                    continue;

                best = sqr;
                bestIndex = i;
            }

            if (bestIndex < 0 || best > maxSqrDistance)
                return false;

            index = bestIndex;
            return true;
        }

        private void AppendSutureSidewallBoundaryEdges(
            byte side,
            bool useOuterLeft,
            bool useInnerLeft,
            HashSet<ulong> added,
            ref int sourceEdgeId,
            ref int missingIndexCount)
        {
            if (_outerIncisionStations == null || _innerIncisionStations == null ||
                _outerIncisionStations.Count < 2 || _innerIncisionStations.Count < 2)
            {
                missingIndexCount++;
                Debug.LogWarning($"[Suture][VirtualSidewall] reason=sidewall-boundary-index-missing side={side} outerStations={(_outerIncisionStations != null ? _outerIncisionStations.Count : 0)} innerStations={(_innerIncisionStations != null ? _innerIncisionStations.Count : 0)}");
                return;
            }

            if (_outerIncisionStations.Count != _innerIncisionStations.Count)
            {
                missingIndexCount++;
                Debug.LogWarning($"[Suture][VirtualSidewall] reason=sidewall-boundary-index-missing side={side} outerStations={_outerIncisionStations.Count} innerStations={_innerIncisionStations.Count} mismatch=station-count");
            }

            float maxJoinSpacing = Mathf.Max(incisionPairMinSpacing * 3.5f, incisionBrushRadius * 2.5f);
            maxJoinSpacing = Mathf.Max(maxJoinSpacing, 0.0025f);
            int count = Mathf.Min(_outerIncisionStations.Count, _innerIncisionStations.Count);
            for (int i = 1; i < count; i++)
            {
                PendingLipStation outerPrev = _outerIncisionStations[i - 1];
                PendingLipStation outerCur = _outerIncisionStations[i];
                PendingLipStation innerPrev = _innerIncisionStations[i - 1];
                PendingLipStation innerCur = _innerIncisionStations[i];

                float outerJoin = Vector3.Distance(outerPrev.seedCenterLocal, outerCur.seedCenterLocal);
                float innerJoin = Vector3.Distance(innerPrev.seedCenterLocal, innerCur.seedCenterLocal);
                bool identityOnlyBoundary = outerPrev.suppressRuntimeMotion || outerCur.suppressRuntimeMotion ||
                    innerPrev.suppressRuntimeMotion || innerCur.suppressRuntimeMotion;
                if (!identityOnlyBoundary && (outerJoin > maxJoinSpacing || innerJoin > maxJoinSpacing))
                    continue;

                int outerA = useOuterLeft ? outerPrev.leftLocalIndex : outerPrev.rightLocalIndex;
                int outerB = useOuterLeft ? outerCur.leftLocalIndex : outerCur.rightLocalIndex;
                int innerA = useInnerLeft ? innerPrev.leftLocalIndex : innerPrev.rightLocalIndex;
                int innerB = useInnerLeft ? innerCur.leftLocalIndex : innerCur.rightLocalIndex;
                if (!TryAppendSutureSidewallBoundaryEdge(
                        outerA,
                        outerB,
                        innerA,
                        innerB,
                        side,
                        sourceEdgeId,
                        added,
                        ref missingIndexCount))
                {
                    continue;
                }

                sourceEdgeId++;
            }
        }

        private bool TryAppendSutureSidewallBoundaryEdge(
            int outerA,
            int outerB,
            int innerA,
            int innerB,
            byte side,
            int sourceEdgeId,
            HashSet<ulong> added,
            ref int missingIndexCount)
        {
            if (outerA < 0 || outerB < 0 || innerA < 0 || innerB < 0 ||
                outerA == outerB || innerA == innerB ||
                outerA >= OuterCount || outerB >= OuterCount ||
                innerA >= InnerCount || innerB >= InnerCount)
            {
                missingIndexCount++;
                Debug.LogWarning($"[Suture][VirtualSidewall] reason=sidewall-boundary-index-missing side={side} sourceEdge={sourceEdgeId} outer=({outerA},{outerB}) inner=({innerA},{innerB})");
                return false;
            }

            int outerGlobalA = OuterStart + outerA;
            int outerGlobalB = OuterStart + outerB;
            int innerGlobalA = InnerStart + innerA;
            int innerGlobalB = InnerStart + innerB;
            ulong key = PackSutureSidewallBoundaryEdgeKey(outerGlobalA, outerGlobalB, innerGlobalA, innerGlobalB, side);
            if (added != null && !added.Add(key))
                return false;

            _sutureSidewallBoundaryEdges.Add(new SutureSidewallBoundaryEdgeInfo
            {
                outerA = outerGlobalA,
                outerB = outerGlobalB,
                innerA = innerGlobalA,
                innerB = innerGlobalB,
                sourceEdgeId = sourceEdgeId,
                side = side,
                enabled = 1,
            });
            return true;
        }

        private static ulong PackSutureSidewallBoundaryEdgeKey(int outerA, int outerB, int innerA, int innerB, byte side)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ PackUndirectedEdgeKey(outerA, outerB)) * 1099511628211UL;
                hash = (hash ^ PackUndirectedEdgeKey(innerA, innerB)) * 1099511628211UL;
                hash = (hash ^ side) * 1099511628211UL;
                return hash;
            }
        }

        private static void CopySutureSidePair(IReadOnlyList<Vector3> leftOrLower,
            IReadOnlyList<Vector3> rightOrUpper,
            List<Vector3> sideA,
            List<Vector3> sideB,
            bool swap)
        {
            sideA.Clear();
            sideB.Clear();
            IReadOnlyList<Vector3> a = swap ? rightOrUpper : leftOrLower;
            IReadOnlyList<Vector3> b = swap ? leftOrLower : rightOrUpper;
            if (a != null)
                sideA.AddRange(a);
            if (b != null)
                sideB.AddRange(b);
        }

        private static bool TryGetSutureBoundaryPairDirection(
            IReadOnlyList<Vector3> sideA,
            IReadOnlyList<Vector3> sideB,
            out Vector3 direction)
        {
            direction = Vector3.zero;
            if (sideA == null ||
                sideB == null ||
                sideA.Count == 0 ||
                sideB.Count == 0)
                return false;

            Vector3 centerA = ComputeAveragePoint(sideA, null);
            Vector3 centerB = ComputeAveragePoint(sideB, null);
            direction = centerB - centerA;
            if (direction.sqrMagnitude <= 1e-12f)
                return false;
            direction.Normalize();
            return true;
        }

        private static Vector3 ComputeAveragePoint(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            if (a != null)
            {
                for (int i = 0; i < a.Count; i++)
                {
                    sum += a[i];
                    count++;
                }
            }
            if (b != null)
            {
                for (int i = 0; i < b.Count; i++)
                {
                    sum += b[i];
                    count++;
                }
            }
            return count > 0 ? sum / count : Vector3.zero;
        }

        public string GetSutureSideDisplayName(SutureVesselSide side)
        {
            switch (side)
            {
                case SutureVesselSide.SideA:
                    return _sutureSideGuideMode == SutureSideGuideMode.CircumferentialProximalDistal ? "SideA / 近端" : "SideA / Left侧";
                case SutureVesselSide.SideB:
                    return _sutureSideGuideMode == SutureSideGuideMode.CircumferentialProximalDistal ? "SideB / 远端" : "SideB / Right侧";
                default:
                    return "Unknown";
            }
        }

        public bool TryClassifySutureSide(Vector3 worldPoint, out SutureSideClassifyResult result)
        {
            result = default(SutureSideClassifyResult);
            if (!_sutureSideGuideValid)
                return false;

            bool hasA = TryClosestPointOnSutureSide(worldPoint,
                _sutureSideAOuterBoundaryWorld,
                _sutureSideAInnerBoundaryWorld,
                out Vector3 closestA,
                out float sqrA);
            bool hasB = TryClosestPointOnSutureSide(worldPoint,
                _sutureSideBOuterBoundaryWorld,
                _sutureSideBInnerBoundaryWorld,
                out Vector3 closestB,
                out float sqrB);

            if (!hasA && !hasB)
                return false;

            bool chooseA = hasA && (!hasB || sqrA <= sqrB);
            result.valid = true;
            result.side = chooseA ? SutureVesselSide.SideA : SutureVesselSide.SideB;
            result.distanceToSideA = hasA ? Mathf.Sqrt(Mathf.Max(0f, sqrA)) : float.PositiveInfinity;
            result.distanceToSideB = hasB ? Mathf.Sqrt(Mathf.Max(0f, sqrB)) : float.PositiveInfinity;
            result.distanceToSide = chooseA ? result.distanceToSideA : result.distanceToSideB;
            result.closestPointOnSideA = closestA;
            result.closestPointOnSideB = closestB;
            result.closestPointOnSide = chooseA ? closestA : closestB;
            return true;
        }

        public bool TryClassifySutureSide(Vector3 worldPoint, out SutureVesselSide side, out float distanceToSide)
        {
            if (TryClassifySutureSide(worldPoint, out SutureSideClassifyResult result))
            {
                side = result.side;
                distanceToSide = result.distanceToSide;
                return true;
            }

            side = SutureVesselSide.Unknown;
            distanceToSide = float.PositiveInfinity;
            return false;
        }

        private static bool TryClosestPointOnSutureSide(Vector3 worldPoint,
            IReadOnlyList<Vector3> outerBoundary,
            IReadOnlyList<Vector3> innerBoundary,
            out Vector3 closestPoint,
            out float closestSqrDistance)
        {
            closestPoint = default(Vector3);
            closestSqrDistance = float.PositiveInfinity;
            bool found = false;
            if (TryClosestPointOnPolyline(worldPoint, outerBoundary, out Vector3 outerClosest, out float outerSqr))
            {
                closestPoint = outerClosest;
                closestSqrDistance = outerSqr;
                found = true;
            }
            if (TryClosestPointOnPolyline(worldPoint, innerBoundary, out Vector3 innerClosest, out float innerSqr) && innerSqr < closestSqrDistance)
            {
                closestPoint = innerClosest;
                closestSqrDistance = innerSqr;
                found = true;
            }
            return found;
        }

        private static bool TryClosestPointOnPolyline(Vector3 worldPoint,
            IReadOnlyList<Vector3> points,
            out Vector3 closestPoint,
            out float closestSqrDistance)
        {
            closestPoint = default(Vector3);
            closestSqrDistance = float.PositiveInfinity;
            if (points == null || points.Count == 0)
                return false;

            bool found = false;
            if (points.Count == 1)
            {
                closestPoint = points[0];
                closestSqrDistance = (worldPoint - closestPoint).sqrMagnitude;
                return true;
            }

            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[i + 1];
                Vector3 ab = b - a;
                float denom = Vector3.Dot(ab, ab);
                Vector3 candidate = denom > 1e-12f
                    ? a + ab * Mathf.Clamp01(Vector3.Dot(worldPoint - a, ab) / denom)
                    : a;
                float sqr = (worldPoint - candidate).sqrMagnitude;
                if (sqr < closestSqrDistance)
                {
                    closestSqrDistance = sqr;
                    closestPoint = candidate;
                    found = true;
                }
            }

            if (points.Count > 2 && (points[0] - points[points.Count - 1]).sqrMagnitude > 1e-10f)
            {
                Vector3 a = points[points.Count - 1];
                Vector3 b = points[0];
                Vector3 ab = b - a;
                float denom = Vector3.Dot(ab, ab);
                Vector3 candidate = denom > 1e-12f
                    ? a + ab * Mathf.Clamp01(Vector3.Dot(worldPoint - a, ab) / denom)
                    : a;
                float sqr = (worldPoint - candidate).sqrMagnitude;
                if (sqr < closestSqrDistance)
                {
                    closestSqrDistance = sqr;
                    closestPoint = candidate;
                    found = true;
                }
            }
            return found;
        }


        private void ClearGreenfieldTransientCaches(bool clearTargetStroke)
        {
            if (clearTargetStroke)
            {
                if (_incisionStrokeWorld == null)
                    _incisionStrokeWorld = new List<Vector3>();
                _incisionStrokeWorld.Clear();
            }

            _outerIncisionLipPairs.Clear();
            _innerIncisionLipPairs.Clear();
            _outerIncisionStations.Clear();
            _innerIncisionStations.Clear();
            _outerIncisionCenterlineWorld.Clear();
            _innerIncisionCenterlineWorld.Clear();
            _outerIncisionStationCentersWorld.Clear();
            _innerIncisionStationCentersWorld.Clear();
            _incisionMasterChainWorld.Clear();
            _incisionCenterCurveWorld.Clear();
            _outerIncisionLeftBoundaryWorld.Clear();
            _outerIncisionRightBoundaryWorld.Clear();
            _innerIncisionLeftBoundaryWorld.Clear();
            _innerIncisionRightBoundaryWorld.Clear();
            _incisionOuterPatchComponentCount = 0;
            _incisionInnerPatchComponentCount = 0;
            _incisionOuterQualitySummary = string.Empty;
            _incisionInnerQualitySummary = string.Empty;

            ClearSutureSideGuides();

            _incisionDebugPoints.Clear();
            _incisionDebugCenterline.Clear();
            _incisionDebugTriangles.Clear();
            _incisionDebugRibbonFrames.Clear();
            _incisionDeleteDebugSummaries.Clear();
            _incisionInsertedVertices.Clear();
            _incisionPairDebugPoints.Clear();
            _incisionRenderSampleDebugPoints.Clear();
            _incisionTargetTraceDebugRecords.Clear();
            _incisionAllTargetTraceDebugRecords.Clear();
            _incisionFinalTriangleAuditRecords.Clear();
            _runtimeCommittedIncisionDebugRibbonFrames.Clear();
            _runtimeCommittedIncisionDebugTriangles.Clear();
            _runtimeCommittedIncisionDeleteDebugSummaries.Clear();
            _runtimeCommittedIncisionTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionAllTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionFinalTriangleAuditRecords.Clear();
            ClearRuntimePrecommitGeometry();
        }

        private void CopyGreenfieldPrecommitLayerRuntimeState(DoubleLayerIncisionGenerator.LayerResult layerResult, VesselLayer layer)
        {
            if (layerResult == null)
                return;

            List<Vector3> rowCenters = layer == VesselLayer.Outer ? _runtimePrecommitRowCentersOuter : _runtimePrecommitRowCentersInner;
            List<Vector3> leftBoundary = layer == VesselLayer.Outer ? _runtimePrecommitLeftBoundaryOuter : _runtimePrecommitLeftBoundaryInner;
            List<Vector3> rightBoundary = layer == VesselLayer.Outer ? _runtimePrecommitRightBoundaryOuter : _runtimePrecommitRightBoundaryInner;
            List<Vector3> leftSupport = layer == VesselLayer.Outer ? _runtimePrecommitLeftSupportOuter : _runtimePrecommitLeftSupportInner;
            List<Vector3> rightSupport = layer == VesselLayer.Outer ? _runtimePrecommitRightSupportOuter : _runtimePrecommitRightSupportInner;
            List<Vector3> failedEdges = layer == VesselLayer.Outer ? _runtimePrecommitFailedInterfaceEdgesOuter : _runtimePrecommitFailedInterfaceEdgesInner;
            List<string> failedEdgeLabels = layer == VesselLayer.Outer ? _runtimePrecommitFailedInterfaceEdgeLabelsOuter : _runtimePrecommitFailedInterfaceEdgeLabelsInner;

            rowCenters.Clear();
            leftBoundary.Clear();
            rightBoundary.Clear();
            leftSupport.Clear();
            rightSupport.Clear();
            failedEdges.Clear();
            failedEdgeLabels.Clear();

            if (layerResult.precommitRowCentersWorld != null)
                rowCenters.AddRange(layerResult.precommitRowCentersWorld);
            if (layerResult.precommitLeftBoundaryWorld != null)
                leftBoundary.AddRange(layerResult.precommitLeftBoundaryWorld);
            if (layerResult.precommitRightBoundaryWorld != null)
                rightBoundary.AddRange(layerResult.precommitRightBoundaryWorld);
            if (layerResult.precommitLeftSupportWorld != null)
                leftSupport.AddRange(layerResult.precommitLeftSupportWorld);
            if (layerResult.precommitRightSupportWorld != null)
                rightSupport.AddRange(layerResult.precommitRightSupportWorld);
            if (layerResult.precommitFailedOwnershipEdgesWorld != null)
                failedEdges.AddRange(layerResult.precommitFailedOwnershipEdgesWorld);
            if (layerResult.precommitFailedOwnershipEdgeLabels != null)
                failedEdgeLabels.AddRange(layerResult.precommitFailedOwnershipEdgeLabels);

            if (layer == VesselLayer.Outer)
                _runtimePrecommitProjectionAlignOuter = layerResult.projectionAlignSummary ?? string.Empty;
            else
                _runtimePrecommitProjectionAlignInner = layerResult.projectionAlignSummary ?? string.Empty;
        }

        public void ClearIncisionStroke()
        {
            if (Application.isPlaying && runtimeSingleIncisionMode && _runtimeIncisionCommittedOnce)
            {
                Debug.Log("[DoubleLayerDriver] 运行时单次切口模式已锁定，Play 中不再清空已提交切口的目标线。");
                return;
            }

            if (_incisionStrokeWorld == null)
                _incisionStrokeWorld = new List<Vector3>();
            _incisionStrokeWorld.Clear();
        }

        public void ClearIncisionPreviewAndDebug()
        {
            ClearIncisionPreviewAndDebug(true);
        }

        public void ClearIncisionPreviewAndDebug(bool clearStroke)
        {
            if (Application.isPlaying && runtimeSingleIncisionMode && _runtimeIncisionCommittedOnce)
            {
                Debug.Log("[DoubleLayerDriver] 运行时单次切口模式已锁定，Play 中不再清空已提交切口的运行数据。");
                return;
            }

            if (clearStroke)
            {
                if (_incisionStrokeWorld == null)
                    _incisionStrokeWorld = new List<Vector3>();
                _incisionStrokeWorld.Clear();
            }

            _outerIncisionLipPairs.Clear();
            _innerIncisionLipPairs.Clear();
            _outerIncisionStations.Clear();
            _innerIncisionStations.Clear();
            _outerIncisionCenterlineWorld.Clear();
            _innerIncisionCenterlineWorld.Clear();
            _outerIncisionStationCentersWorld.Clear();
            _innerIncisionStationCentersWorld.Clear();
            _incisionMasterChainWorld.Clear();
            _incisionCenterCurveWorld.Clear();
            _outerIncisionLeftBoundaryWorld.Clear();
            _outerIncisionRightBoundaryWorld.Clear();
            _innerIncisionLeftBoundaryWorld.Clear();
            _innerIncisionRightBoundaryWorld.Clear();
            _incisionOuterPatchComponentCount = 0;
            _incisionInnerPatchComponentCount = 0;
            _incisionOuterQualitySummary = string.Empty;
            _incisionInnerQualitySummary = string.Empty;

            ClearSutureSideGuides();

            _incisionDebugPoints.Clear();
            _incisionDebugCenterline.Clear();
            _incisionDebugTriangles.Clear();
            _incisionDebugRibbonFrames.Clear();
            _incisionDeleteDebugSummaries.Clear();
            _runtimeCommittedIncisionDebugRibbonFrames.Clear();
            _runtimeCommittedIncisionDebugTriangles.Clear();
            _runtimeCommittedIncisionDeleteDebugSummaries.Clear();
            _runtimeCommittedIncisionTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionAllTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionFinalTriangleAuditRecords.Clear();
            _incisionInsertedVertices.Clear();
            _incisionPairDebugPoints.Clear();
            _incisionRenderSampleDebugPoints.Clear();
            _incisionTargetTraceDebugRecords.Clear();
            _incisionAllTargetTraceDebugRecords.Clear();
            _incisionFinalTriangleAuditRecords.Clear();
            ClearRuntimePrecommitGeometry();

            _incisionFramesSinceApply = int.MaxValue;
            _incisionRebuildPending = false;
            _isIncisionDragging = false;
            _proceduralIncisionAutoApplied = false;
            _runtimeIncisionCommittedOnce = false;

            ClearIncisionMarkerMesh();
            ClearIncisionSidewallMesh();
        }

        public bool TryGenerateProceduralIncisionStroke(bool clearExisting = true, bool logWarnings = true)
        {
            if (Application.isPlaying && runtimeSingleIncisionMode)
            {
                if (logWarnings)
                    Debug.LogWarning("[DoubleLayerDriver] 当前工作流已锁定为“Play 开始自动生成一次”。运行时不再单独生成编辑态预览。");
                return false;
            }

            bool ok = TryBuildProceduralIncisionStrokeInternal(clearExisting, logWarnings, buildPreviewDebug: true);
            if (ok && !Application.isPlaying)
                _runtimeIncisionCommittedOnce = false;
            return ok;
        }

        private bool TryBuildProceduralIncisionStrokeInternal(bool clearExisting, bool logWarnings, bool buildPreviewDebug)
        {
            if (_incisionStrokeWorld == null)
                _incisionStrokeWorld = new List<Vector3>();
            if (clearExisting)
                _incisionStrokeWorld.Clear();

            _outerIncisionLipPairs.Clear();
            _innerIncisionLipPairs.Clear();
            _outerIncisionStations.Clear();
            _innerIncisionStations.Clear();
            _outerIncisionCenterlineWorld.Clear();
            _innerIncisionCenterlineWorld.Clear();
            _outerIncisionStationCentersWorld.Clear();
            _innerIncisionStationCentersWorld.Clear();
            _incisionDebugPoints.Clear();
            _incisionDebugCenterline.Clear();
            _incisionDebugTriangles.Clear();
            _incisionDebugRibbonFrames.Clear();
            _incisionDeleteDebugSummaries.Clear();
            _incisionInsertedVertices.Clear();
            _incisionPairDebugPoints.Clear();
            _incisionRenderSampleDebugPoints.Clear();
            _incisionTargetTraceDebugRecords.Clear();
            _incisionAllTargetTraceDebugRecords.Clear();
            ClearIncisionMarkerMesh();
            ClearIncisionSidewallMesh();

            _incisionMasterChainWorld.Clear();
            _incisionCenterCurveWorld.Clear();
            _outerIncisionLeftBoundaryWorld.Clear();
            _outerIncisionRightBoundaryWorld.Clear();
            _innerIncisionLeftBoundaryWorld.Clear();
            _innerIncisionRightBoundaryWorld.Clear();
            _incisionOuterPatchComponentCount = 0;
            _incisionInnerPatchComponentCount = 0;
            _incisionOuterQualitySummary = string.Empty;
            _incisionInnerQualitySummary = string.Empty;

            if (!TryGenerateGreenfieldIncisionResult(out var result, logWarnings))
                return false;

            if (buildPreviewDebug)
                CaptureGreenfieldPreviewResult(result);

            return true;
        }

        private bool UseGreenfieldIncisionPipeline()
        {
            return true;
        }

        private DoubleLayerIncisionGenerator.Preset BuildGreenfieldIncisionPreset()
        {
            return new DoubleLayerIncisionGenerator.Preset
            {
                normalizedZ = proceduralIncisionNormalizedZ,
                circumferenceAngleDeg = proceduralIncisionCircumferenceAngleDeg,
                directionAngleDeg = proceduralIncisionDirectionDeg,
                lengthWorld = proceduralIncisionLength,
                openWidthWorld = incisionOpenWidth,
                circumferentialSeparationCut = incisionCircumferentialSeparationCut,
                separationDistanceWorld = Mathf.Max(0f, incisionSeparationDistance),
                sampleCount = proceduralIncisionSampleCount,
                surfaceLiftWorld = proceduralIncisionSurfaceLift,
                centerOffsetLocal = proceduralIncisionCenterOffsetLocal,
            };
        }

        private DoubleLayerIncisionGenerator.Options BuildGreenfieldIncisionOptions()
        {
            return new DoubleLayerIncisionGenerator.Options
            {
                subdivisionPasses = incisionLocalSubdivisionPasses,
                pathSampleSpacingWorld = incisionPathSampleSpacing,
                pathSnapPenalty = incisionPathSnapPenalty,
                maxAnchorCount = incisionMaxAnchorCount,
                maxStationCount = 64,
            };
        }

        private bool TryGenerateGreenfieldIncisionResult(out DoubleLayerIncisionGenerator.GenerateResult result, bool logWarnings)
        {
            result = null;
            ClearGreenfieldTransientCaches(clearTargetStroke: false);

            Mesh outerMesh = Application.isPlaying ? _outerMesh : (outerMeshFilter != null ? outerMeshFilter.sharedMesh : null);
            Mesh innerMesh = Application.isPlaying ? _innerMesh : (innerMeshFilter != null ? innerMeshFilter.sharedMesh : null);
            if (outerMesh == null || innerMesh == null || outerMeshFilter == null || innerMeshFilter == null)
            {
                if (logWarnings)
                    Debug.LogWarning("[DoubleLayerDriver] 外壁/内壁 mesh 或 transform 不可用，无法生成绿地切口预览。");
                return false;
            }

            DoubleLayerIncisionGenerator.Preset preset = BuildGreenfieldIncisionPreset();
            result = preset.circumferentialSeparationCut
                ? DoubleLayerIncisionGenerator.GenerateCircumferentialSeparationCut(
                    outerMesh,
                    outerMeshFilter.transform,
                    innerMesh,
                    innerMeshFilter.transform,
                    preset)
                : DoubleLayerIncisionGenerator.Generate(
                    outerMesh,
                    outerMeshFilter.transform,
                    innerMesh,
                    innerMeshFilter.transform,
                    preset,
                    BuildGreenfieldIncisionOptions());

            if (_incisionStrokeWorld == null)
                _incisionStrokeWorld = new List<Vector3>();
            _incisionStrokeWorld.Clear();
            if (result != null && result.targetStrokeWorld != null)
                _incisionStrokeWorld.AddRange(result.targetStrokeWorld);

            if (result == null || !result.success)
            {
                CaptureGreenfieldPreviewResult(result);

                if (result != null)
                    AppendGreenfieldFailureSummary(result);

                if (logWarnings)
                    Debug.LogWarning(BuildGreenfieldFailureConsoleReport(result));

                return false;
            }

            if (logWarnings)
                Debug.Log(BuildGreenfieldSuccessConsoleReport(result));

            return true;
        }

        private void AppendGreenfieldFailureSummary(DoubleLayerIncisionGenerator.GenerateResult result)
        {
            _incisionDeleteDebugSummaries.Add(new IncisionDeleteDebugSummary
            {
                layer = -1,
                success = false,
                failStage = result != null ? result.failStage : "Generator",
                failReason = result != null ? result.failReason : "Unknown",
                openWidth = incisionOpenWidth,
                sampleSpacing = incisionPathSampleSpacing,
                pathSnapPenalty = incisionPathSnapPenalty,
                anchorCount = Mathf.Max(4, incisionMaxAnchorCount),
                localSubdivisionPasses = incisionLocalSubdivisionPasses,
                guideLength = _incisionStrokeWorld != null ? ComputePolylineLengthWorld(_incisionStrokeWorld) : 0f,
                strokePointCount = _incisionStrokeWorld != null ? _incisionStrokeWorld.Count : 0,
            });
        }

        private string BuildGreenfieldFailureConsoleReport(DoubleLayerIncisionGenerator.GenerateResult result)
        {
            if (result == null)
                return incisionCircumferentialSeparationCut ? "[DoubleLayerDriver] 整圈截断生成失败。stage=Generator reason=Unknown" : "[DoubleLayerDriver] 绿地切口生成失败。stage=Generator reason=Unknown";

            var sb = new StringBuilder(1024);
            sb.AppendLine(incisionCircumferentialSeparationCut ? "[DoubleLayerDriver] 整圈截断生成失败" : "[DoubleLayerDriver] 绿地切口生成失败");
            sb.AppendLine(
                $"global: stage={result.failStage} reason={result.failReason} targetStrokePts={(result.targetStrokeWorld != null ? result.targetStrokeWorld.Count : 0)} strokePts={(_incisionStrokeWorld != null ? _incisionStrokeWorld.Count : 0)} openWidth={incisionOpenWidth:F6} sampleSpacing={incisionPathSampleSpacing:F6} snapPenalty={incisionPathSnapPenalty:F3} anchors={Mathf.Max(4, incisionMaxAnchorCount)} sub={incisionLocalSubdivisionPasses}");
            AppendGreenfieldPrecommitProjectionLine(sb, result.outer);
            AppendGreenfieldPrecommitProjectionLine(sb, result.inner);
            AppendGreenfieldLayerConsoleReport(sb, result.outer);
            AppendGreenfieldLayerConsoleReport(sb, result.inner);
            return sb.ToString().TrimEnd();
        }

        private string BuildGreenfieldSuccessConsoleReport(DoubleLayerIncisionGenerator.GenerateResult result)
        {
            if (result == null)
                return incisionCircumferentialSeparationCut ? "[DoubleLayerDriver] 整圈截断生成成功。global: result=null" : "[DoubleLayerDriver] 绿地切口生成成功。global: result=null";

            var sb = new StringBuilder(1024);
            sb.AppendLine(incisionCircumferentialSeparationCut ? "[DoubleLayerDriver] 整圈截断生成成功" : "[DoubleLayerDriver] 绿地切口生成成功");
            sb.AppendLine(
                $"global: stage={result.failStage} reason={result.failReason} targetStrokePts={(result.targetStrokeWorld != null ? result.targetStrokeWorld.Count : 0)} strokePts={(_incisionStrokeWorld != null ? _incisionStrokeWorld.Count : 0)} openWidth={incisionOpenWidth:F6} sampleSpacing={incisionPathSampleSpacing:F6} snapPenalty={incisionPathSnapPenalty:F3} anchors={Mathf.Max(4, incisionMaxAnchorCount)} sub={incisionLocalSubdivisionPasses}");
            AppendGreenfieldPrecommitProjectionLine(sb, result.outer);
            AppendGreenfieldPrecommitProjectionLine(sb, result.inner);
            AppendGreenfieldLayerConsoleReport(sb, result.outer);
            AppendGreenfieldLayerConsoleReport(sb, result.inner);
            return sb.ToString().TrimEnd();
        }

        private static void AppendGreenfieldPrecommitProjectionLine(StringBuilder sb, DoubleLayerIncisionGenerator.LayerResult layer)
        {
            if (sb == null || layer == null)
                return;

            string layerText = layer.layer == 0 ? "Outer" : (layer.layer == 1 ? "Inner" : $"Layer{layer.layer}");
            sb.AppendLine(
                $"[DoubleLayerDriver][IncisionPrecommit][Geometry] {layerText} {FormatGreenfieldComponentSizes(layer.projectionAlignSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][IncisionPrecommit][Rails] {layerText} {BuildGreenfieldPrecommitRailsSummary(layer)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][IncisionPrecommit][Draw] {layerText} {BuildGreenfieldPrecommitDrawSummary(layer)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][Summary] {layerText} total={layer.interfaceMismatchEdgeCount} hard={layer.interfaceHardMismatchEdgeCount} audit={layer.interfaceAuditMismatchEdgeCount} {FormatGreenfieldComponentSizes(layer.interfaceFeCategorySummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][Actual] {layerText} total={layer.interfaceMismatchEdgeCount} hard={layer.interfaceHardMismatchEdgeCount} audit={layer.interfaceAuditMismatchEdgeCount} {FormatGreenfieldComponentSizes(layer.interfaceFeActualMaskSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][TipKey] {layerText} {FormatGreenfieldComponentSizes(layer.tipInterfaceKeyCompareSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][TipStage.Helper] {layerText} {FormatGreenfieldComponentSizes(layer.startTipWedgeHelperSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][TipStage.SourceNorm] {layerText} {FormatGreenfieldComponentSizes(layer.startTipWedgeSourceNormSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][TipStage.BoundaryNorm] {layerText} {FormatGreenfieldComponentSizes(layer.startTipWedgeBoundaryNormSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][TipStage.Canonical] {layerText} {FormatGreenfieldComponentSizes(layer.startTipWedgeCanonicalSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][TipStage.PostCanonicalSourceNorm] {layerText} {FormatGreenfieldComponentSizes(layer.startTipWedgePostCanonicalSourceNormSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][TipStage.PostCanonicalBoundaryNorm] {layerText} {FormatGreenfieldComponentSizes(layer.startTipWedgePostCanonicalBoundaryNormSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][EndTip.EarlyHelperProbe] {layerText} {FormatGreenfieldComponentSizes(layer.endTipEarlyHelperProbeSummary)}");
            sb.AppendLine(
                $"[DoubleLayerDriver][InterfaceFE][EndTipOrdinaryAudit] {layerText} {FormatGreenfieldComponentSizes(layer.endTipOrdinaryAuditSummary)}");
        }

        private static void AppendGreenfieldLayerConsoleReport(StringBuilder sb, DoubleLayerIncisionGenerator.LayerResult layer)
        {
            if (sb == null)
                return;

            if (layer == null)
            {
                sb.AppendLine("layer: null");
                return;
            }

            string layerText = layer.layer == 0 ? "Outer" : (layer.layer == 1 ? "Inner" : $"Layer{layer.layer}");
            sb.AppendLine($"{layerText}: success={(layer.success ? 1 : 0)} stage={layer.failStage} reason={layer.failReason}");
            sb.AppendLine(
                $"  precommitAlign: {FormatGreenfieldComponentSizes(layer.projectionAlignSummary)}");
            sb.AppendLine(
                $"  precommitRails: {BuildGreenfieldPrecommitRailsSummary(layer)}");
            sb.AppendLine(
                $"  precommitDraw: {BuildGreenfieldPrecommitDrawSummary(layer)}");
            sb.AppendLine(
                $"  curve: avgEdge={layer.averageEdgeLength:F6} guidePts={layer.guidePointCount} denseGuidePts={layer.denseGuidePointCount} guideSpacing={layer.guideSampleSpacingWorld:F6} masterPathVerts={(layer.masterPathVertexIndices != null ? layer.masterPathVertexIndices.Count : 0)} masterChainPts={(layer.masterChainWorld != null ? layer.masterChainWorld.Count : 0)} masterLen={layer.masterLength:F6} centerCurvePts={layer.centerCurvePointCount} centerCurveLen={layer.centerCurveLength:F6} stations={layer.canonicalStationCount} stationSpacing={layer.stationSpacingWorld:F6} taper={layer.endTaperLengthWorld:F6}");
            sb.AppendLine(
                $"  patch: candidates={layer.patchCandidateTriangleCount} seeds={layer.patchSeedTriangleCount} patchComp={layer.patchComponentCount} bestCompTri={layer.patchBestComponentTriangleCount} bestSeedHits={layer.patchBestComponentSeedHitCount} patchTri={layer.patchTriangleCount} keptTri={layer.keptTriangleCount} removedTri={layer.removedTriangleCount} patchHalfWidth={layer.patchHalfWidthWorld:F6} sPad={layer.patchSPadWorld:F6} normalTol={layer.patchNormalTolWorld:F6}");
            sb.AppendLine(
                $"  remesh: supportOffset={layer.supportOffsetWorld:F6} base={layer.supportOffsetBaseWorld:F6} cap={layer.supportOffsetCapWorld:F6} margin={layer.supportStripMarginWorld:F6} refRows={(layer.usedReferenceStripRows ? layer.referenceStripRowCount : 0)} keptPolys={layer.remeshKeptPolygonCount} removedPolys={layer.remeshRemovedPolygonCount} keptPolyVerts={layer.remeshKeptPolygonVertexCount} removedPolyVerts={layer.remeshRemovedPolygonVertexCount} taggedEdges={layer.taggedBoundaryEdgeCount} leftTagged={layer.leftTaggedBoundaryEdgeCount} rightTagged={layer.rightTaggedBoundaryEdgeCount} openBoundaryEdges={layer.openBoundaryEdgeCount} remeshVerts={layer.remeshedVertexCount} remeshTris={layer.remeshedTriangleCount}");
            string visibleBoundaryMode = layer.visibleBoundaryBuiltFromTaggedEdges
                ? $"LB/RB({(string.IsNullOrEmpty(layer.boundarySourceSummary) ? "taggedEdges" : layer.boundarySourceSummary)})"
                : "LB/RB(invalid)";
            sb.AppendLine(
                $"  semantics: partition={(layer.usedLatticePartition ? "lattice" : "cells")} visibleBoundary={visibleBoundaryMode} domainBoundary=LS/RS(audit-only)");
            sb.AppendLine(
                $"  rails: LS={layer.leftSupportAnchorCount}/{layer.leftSupportPathVertexCount}/{layer.leftSupportSampleCount} LB={layer.leftBoundaryAnchorCount}/{layer.leftBoundaryPathVertexCount}/{layer.leftBoundarySampleCount} RB={layer.rightBoundaryAnchorCount}/{layer.rightBoundaryPathVertexCount}/{layer.rightBoundarySampleCount} RS={layer.rightSupportAnchorCount}/{layer.rightSupportPathVertexCount}/{layer.rightSupportSampleCount} (anchor/path/sample)");
            sb.AppendLine(
                $"  strip: rows={layer.stripRowCount}/{layer.attemptedStripRowCount} tips={layer.tipStripRowCount} startTip={(layer.keptStartTipRow ? 1 : 0)} endTip={(layer.keptEndTipRow ? 1 : 0)} collapsed={layer.collapsedStripRowCount} repeated={layer.repeatedStripRowCount} crossed={layer.crossedStripRowCount} minBoundary={layer.minStructuredBoundaryWidth:F6} minSupport={layer.minStructuredSupportWidth:F6} maxJump={layer.maxStructuredBoundaryJump:F6}");
            sb.AppendLine(
                $"  rowDef: mism={layer.rowDefinitionMismatchCount} {FormatGreenfieldComponentSizes(layer.rowDefinitionSummary)}");
            sb.AppendLine(
                $"  coverage: stripTri={layer.stripCoveredTriangleCount} supportTri={layer.supportCoveredTriangleCount} excisionTri={layer.excisionCoveredTriangleCount} patchOutsideStrip={layer.patchOutsideStripTriangleCount} clippedPatch={layer.clippedPatchTriangleCount} outsideChanged={layer.changedOutsideStripTriangleCount}");
            sb.AppendLine(
                $"  taperClipInput: {FormatGreenfieldComponentSizes(layer.taperClipInputSummary)}");
            sb.AppendLine(
                $"  partition: outsidePoly={layer.preservedOutsideFragmentCount} supportPoly={layer.supportPartitionPolygonCount}->{layer.supportMergedPolygonCount} excisionPoly={layer.excisionPartitionPolygonCount}->{layer.excisionMergedPolygonCount} outsideTri={layer.outsidePartitionTriangleCount} supportTri={layer.supportPartitionTriangleCount} fallback={layer.cellMergeFallbackCount} droppedPoly={layer.droppedDegeneratePartitionPolygonCount}");
            sb.AppendLine(
                $"  interfaceNorm: sourceEdgeKeys={layer.interfaceNormalizationSourceEdgeKeyCount} sourceEdgeInsertedPts={layer.interfaceNormalizationSourceEdgeInsertCount} boundarySegKeys={layer.interfaceNormalizationBoundarySegmentKeyCount} boundaryInsertedPts={layer.interfaceNormalizationBoundaryInsertCount} polygonsTouched={layer.interfaceNormalizationPolygonsTouchedCount}");
            sb.AppendLine(
                $"  interfaceScan: segments={layer.interfaceScanSegmentCount} osMatched={layer.interfaceScanMatchedOutsideSupportCount} sxMatched={layer.interfaceScanMatchedSupportExcisionCount} ox={layer.interfaceScanOutsideExcisionCount} singleSide={layer.interfaceScanSingleSideCount} multiUse={layer.interfaceScanMultiUseCount} triple={layer.interfaceScanTripleUseCount} byFlag={FormatGreenfieldComponentSizes(layer.interfaceScanByFlagSummary)} byRow={FormatGreenfieldComponentSizes(layer.interfaceScanByRowSummary)} ownership={FormatGreenfieldComponentSizes(layer.interfaceScanOwnershipSummary)}");
            sb.AppendLine(
                $"  interfaceFE: total={layer.interfaceMismatchEdgeCount} hard={layer.interfaceHardMismatchEdgeCount} audit={layer.interfaceAuditMismatchEdgeCount} {FormatGreenfieldComponentSizes(layer.interfaceFeCategorySummary)}");
            sb.AppendLine(
                $"  interfaceFEActual: total={layer.interfaceMismatchEdgeCount} hard={layer.interfaceHardMismatchEdgeCount} audit={layer.interfaceAuditMismatchEdgeCount} {FormatGreenfieldComponentSizes(layer.interfaceFeActualMaskSummary)}");
            sb.AppendLine(
                $"  tipKeyCompare: {FormatGreenfieldComponentSizes(layer.tipInterfaceKeyCompareSummary)}");
            sb.AppendLine(
                $"  tipStageHelper: {FormatGreenfieldComponentSizes(layer.startTipWedgeHelperSummary)}");
            sb.AppendLine(
                $"  tipStageSourceNorm: {FormatGreenfieldComponentSizes(layer.startTipWedgeSourceNormSummary)}");
            sb.AppendLine(
                $"  tipStageBoundaryNorm: {FormatGreenfieldComponentSizes(layer.startTipWedgeBoundaryNormSummary)}");
            sb.AppendLine(
                $"  tipStageCanonical: {FormatGreenfieldComponentSizes(layer.startTipWedgeCanonicalSummary)}");
            sb.AppendLine(
                $"  tipStagePostCanonicalSourceNorm: {FormatGreenfieldComponentSizes(layer.startTipWedgePostCanonicalSourceNormSummary)}");
            sb.AppendLine(
                $"  tipStagePostCanonicalBoundaryNorm: {FormatGreenfieldComponentSizes(layer.startTipWedgePostCanonicalBoundaryNormSummary)}");
            sb.AppendLine(
                $"  endTipEarlyHelperProbe: {FormatGreenfieldComponentSizes(layer.endTipEarlyHelperProbeSummary)}");
            sb.AppendLine(
                $"  endTipOrdinaryAudit: {FormatGreenfieldComponentSizes(layer.endTipOrdinaryAuditSummary)}");
            sb.AppendLine(
                $"  ownershipDetail: {FormatGreenfieldComponentSizes(layer.interfaceScanDetailSummary)}");
            sb.AppendLine(
                $"  tipOwnership: {FormatGreenfieldComponentSizes(layer.tipOwnershipSummary)}");
            sb.AppendLine(
                $"  interfaceRepair: segmentsNormalized={layer.interfaceRepairNormalizedSegmentCount} verticesReused={layer.interfaceRepairVertexReuseCount} verticesInserted={layer.interfaceRepairVertexInsertCount} outsideTouched={layer.interfaceRepairOutsideTouchedCount} supportTouched={layer.interfaceRepairSupportTouchedCount} excisionTouched={layer.interfaceRepairExcisionTouchedCount} residualMismatch={layer.interfaceRepairResidualMismatchCount}");
            sb.AppendLine(
                $"  membershipScan: cells={layer.cellMembershipScanCellCount} mism={layer.cellMembershipScanMismatchCount} {FormatGreenfieldComponentSizes(layer.cellMembershipScanSummary)}");
            sb.AppendLine(
                $"  cell: traced={layer.cellTopologyTracedCount}/{layer.cellTopologyCellCount} fail={FormatGreenfieldCellTopology(layer.failedCellRowIndex, layer.failedCellColumnIndex, layer.failedCellBoundaryEdgeCount, layer.failedCellBoundaryVertexCount, layer.failedCellLoopCount, layer.failedCellDegree1VertexCount, layer.failedCellDegree2VertexCount, layer.failedCellBranchVertexCount, layer.failedCellSelfIntersection, layer.failedCellSemanticCoverage, layer.failedCellReason)}");
            sb.AppendLine(
                $"  cellDef: mism={layer.failedCellDefinitionMismatchCount} {FormatGreenfieldComponentSizes(layer.failedCellDefinitionSummary)}");
            sb.AppendLine(
                $"  cellMembership: {FormatGreenfieldComponentSizes(layer.failedCellMembershipSummary)}");
            sb.AppendLine(
                $"  cellGraph: {FormatGreenfieldComponentSizes(layer.failedCellBranchSummary)}");
            sb.AppendLine(
                $"  cellSides: {FormatGreenfieldComponentSizes(layer.failedCellSideTopologySummary)}");
            sb.AppendLine(
                $"  cellCorners: {FormatGreenfieldComponentSizes(layer.failedCellCornerResolutionSummary)}");
            sb.AppendLine(
                $"  cellOpen: {FormatGreenfieldComponentSizes(layer.failedCellOpenEndpointSummary)}");
            sb.AppendLine(
                $"  cellPoly: scan={layer.cellPolygonScanCount} issue={layer.cellPolygonIssueCount} concave={layer.cellPolygonConcaveCount} self={layer.cellPolygonSelfIntersectionCount} repeat={layer.cellPolygonRepeatedVertexCount} tinyEdge={layer.cellPolygonTinyEdgeCount} tinyAngle={layer.cellPolygonTinyAngleCount} summary={FormatGreenfieldComponentSizes(layer.cellPolygonScanSummary)} fail={FormatGreenfieldComponentSizes(layer.failedCellPolygonSummary)}");
            sb.AppendLine(
                $"  interface: total={layer.interfaceMismatchEdgeCount} hard={layer.interfaceHardMismatchEdgeCount} audit={layer.interfaceAuditMismatchEdgeCount} {FormatGreenfieldComponentSizes(layer.failedInterfaceEdgeSummary)}");
            sb.AppendLine(
                $"  order: gaps=({layer.minLeftStructuredGap:F6},{layer.minCenterStructuredGap:F6},{layer.minRightStructuredGap:F6}) viol=({layer.leftOrderViolationCount},{layer.centerOrderViolationCount},{layer.rightOrderViolationCount}) preview={FormatGreenfieldComponentSizes(layer.crossedRowPreview)}");
            sb.AppendLine(
                $"  boundary: source={FormatGreenfieldComponentSizes(layer.boundarySourceSummary)} leftComp={layer.leftBoundaryComponentCount} rightComp={layer.rightBoundaryComponentCount} leftSizes={FormatGreenfieldComponentSizes(layer.leftBoundaryComponentSizes)} rightSizes={FormatGreenfieldComponentSizes(layer.rightBoundaryComponentSizes)} leftCount={layer.leftBoundaryCount} rightCount={layer.rightBoundaryCount} leftTopo={FormatGreenfieldBoundaryTopology(layer.leftBoundaryOrderedVertexCount, layer.leftBoundaryComponentVertexCount, layer.leftBoundaryDegree1VertexCount, layer.leftBoundaryDegree2VertexCount, layer.leftBoundaryBranchVertexCount)} rightTopo={FormatGreenfieldBoundaryTopology(layer.rightBoundaryOrderedVertexCount, layer.rightBoundaryComponentVertexCount, layer.rightBoundaryDegree1VertexCount, layer.rightBoundaryDegree2VertexCount, layer.rightBoundaryBranchVertexCount)} leftLen={layer.leftBoundaryPolylineLength:F6} rightLen={layer.rightBoundaryPolylineLength:F6} leftS=({layer.leftBoundarySMin:F6},{layer.leftBoundarySMax:F6}) rightS=({layer.rightBoundarySMin:F6},{layer.rightBoundarySMax:F6}) boundaryViol={layer.boundaryContinuityViolationCount}");
            sb.AppendLine(
                $"  quality: maxAspect={layer.maxPatchTriangleAspect:F3} minArea={layer.minPatchTriangleArea:F8} inverted={layer.triangulationWindingMismatchCount + layer.degeneratePatchTriangleCount} windingMismatch={layer.triangulationWindingMismatchCount} degenerate={layer.degeneratePatchTriangleCount} overAspect={layer.overAspectPatchTriangleCount}/{layer.auditedPatchTriangleCount} outsideQ={FormatGreenfieldQualityBucket(layer.outsideQualityTriangleCount, layer.outsideQualityWindingMismatchCount, layer.outsideQualityOverAspectCount, layer.outsideQualityMaxAspect)} supportQ={FormatGreenfieldQualityBucket(layer.supportQualityTriangleCount, layer.supportQualityWindingMismatchCount, layer.supportQualityOverAspectCount, layer.supportQualityMaxAspect)} keptByRegion=({layer.keptOutsideTriangleCount},{layer.keptSupportTriangleCount}) avgOpen={layer.averageRealizedOpenWidth:F6} fallback={(layer.usedFallbackPath ? 1 : 0)} boundaryFallback={(layer.usedBoundaryVertexFallback ? 1 : 0)}");
            sb.AppendLine(
                $"  sync: {FormatGreenfieldComponentSizes(layer.syncSummary)}");
        }

        private static string FormatGreenfieldComponentSizes(string sizes)
        {
            return string.IsNullOrEmpty(sizes) ? "none" : sizes;
        }

        private static string FormatGreenfieldBoundaryTopology(
            int orderedVertexCount,
            int componentVertexCount,
            int degree1VertexCount,
            int degree2VertexCount,
            int branchVertexCount)
        {
            return $"ord={orderedVertexCount}/{componentVertexCount} deg=({degree1VertexCount},{degree2VertexCount},{branchVertexCount})";
        }

        private static string FormatGreenfieldCellTopology(
            int rowIndex,
            int columnIndex,
            int edgeCount,
            int vertexCount,
            int loopCount,
            int degree1VertexCount,
            int degree2VertexCount,
            int branchVertexCount,
            bool selfIntersection,
            string semanticCoverage,
            string failReason)
        {
            if (rowIndex < 0 || columnIndex < 0 || string.IsNullOrEmpty(failReason))
                return "none";

            return $"row={rowIndex} col={columnIndex} edges={edgeCount} verts={vertexCount} loops={loopCount} deg=({degree1VertexCount},{degree2VertexCount},{branchVertexCount}) self={(selfIntersection ? 1 : 0)} sides={FormatGreenfieldComponentSizes(semanticCoverage)} reason={failReason}";
        }

        private static string FormatGreenfieldQualityBucket(
            int triangleCount,
            int windingMismatchCount,
            int overAspectCount,
            float maxAspect)
        {
            return $"tri={triangleCount} mm={windingMismatchCount} oa={overAspectCount} max={maxAspect:F3}";
        }

        private struct PrecommitPolylineStats
        {
            public int pointCount;
            public int segmentCount;
            public float totalLength;
            public float maxJump;
            public int componentCount;
        }

        private struct PrecommitLinePairStats
        {
            public int segmentCount;
            public float totalLength;
        }

        private static string BuildGreenfieldPrecommitRailsSummary(DoubleLayerIncisionGenerator.LayerResult layer)
        {
            if (layer == null)
                return "none";

            BuildPrecommitPolylineStats(layer.precommitLeftBoundaryWorld, out PrecommitPolylineStats leftBoundaryStats);
            BuildPrecommitPolylineStats(layer.precommitRightBoundaryWorld, out PrecommitPolylineStats rightBoundaryStats);
            BuildPrecommitPolylineStats(layer.precommitLeftSupportWorld, out PrecommitPolylineStats leftSupportStats);
            BuildPrecommitPolylineStats(layer.precommitRightSupportWorld, out PrecommitPolylineStats rightSupportStats);

            return
                $"masterChainPts={(layer.masterChainWorld != null ? layer.masterChainWorld.Count : 0)} " +
                $"centerCurvePts={(layer.centerCurveWorld != null ? layer.centerCurveWorld.Count : 0)} " +
                $"rows={(layer.precommitRowCentersWorld != null ? layer.precommitRowCentersWorld.Count : 0)} " +
                $"LB: anchors={layer.leftBoundaryAnchorCount} path={layer.leftBoundaryPathVertexCount} sample={layer.leftBoundarySampleCount} len={leftBoundaryStats.totalLength:F6} cont={FormatPrecommitPolylineStats(leftBoundaryStats)} " +
                $"RB: anchors={layer.rightBoundaryAnchorCount} path={layer.rightBoundaryPathVertexCount} sample={layer.rightBoundarySampleCount} len={rightBoundaryStats.totalLength:F6} cont={FormatPrecommitPolylineStats(rightBoundaryStats)} " +
                $"LS: anchors={layer.leftSupportAnchorCount} path={layer.leftSupportPathVertexCount} sample={layer.leftSupportSampleCount} len={leftSupportStats.totalLength:F6} cont={FormatPrecommitPolylineStats(leftSupportStats)} " +
                $"RS: anchors={layer.rightSupportAnchorCount} path={layer.rightSupportPathVertexCount} sample={layer.rightSupportSampleCount} len={rightSupportStats.totalLength:F6} cont={FormatPrecommitPolylineStats(rightSupportStats)}";
        }

        private static string BuildGreenfieldPrecommitDrawSummary(DoubleLayerIncisionGenerator.LayerResult layer)
        {
            if (layer == null)
                return "none";

            BuildPrecommitLinePairStats(layer.precommitFailedOwnershipEdgesWorld, out PrecommitLinePairStats failedEdgeStats);
            return
                $"masterChain polylinePts={(layer.masterChainWorld != null ? layer.masterChainWorld.Count : 0)} " +
                $"centerCurve polylinePts={(layer.centerCurveWorld != null ? layer.centerCurveWorld.Count : 0)} " +
                $"LB polylinePts={(layer.precommitLeftBoundaryWorld != null ? layer.precommitLeftBoundaryWorld.Count : 0)} " +
                $"RB polylinePts={(layer.precommitRightBoundaryWorld != null ? layer.precommitRightBoundaryWorld.Count : 0)} " +
                $"LS polylinePts={(layer.precommitLeftSupportWorld != null ? layer.precommitLeftSupportWorld.Count : 0)} " +
                $"RS polylinePts={(layer.precommitRightSupportWorld != null ? layer.precommitRightSupportWorld.Count : 0)} " +
                $"failedEdges segments={failedEdgeStats.segmentCount} len={failedEdgeStats.totalLength:F6}";
        }

        private static void BuildPrecommitPolylineStats(IReadOnlyList<Vector3> points, out PrecommitPolylineStats stats)
        {
            stats = default;
            if (points == null || points.Count == 0)
                return;

            stats.pointCount = points.Count;
            stats.segmentCount = Mathf.Max(0, points.Count - 1);
            stats.componentCount = 1;
            for (int i = 1; i < points.Count; i++)
            {
                float segmentLength = Vector3.Distance(points[i - 1], points[i]);
                stats.totalLength += segmentLength;
                stats.maxJump = Mathf.Max(stats.maxJump, segmentLength);
            }
        }

        private static string FormatPrecommitPolylineStats(PrecommitPolylineStats stats)
        {
            return $"pts={stats.pointCount} seg={stats.segmentCount} maxJump={stats.maxJump:F6} comp={stats.componentCount}";
        }

        private static void BuildPrecommitLinePairStats(IReadOnlyList<Vector3> pairPoints, out PrecommitLinePairStats stats)
        {
            stats = default;
            if (pairPoints == null || pairPoints.Count < 2)
                return;

            for (int i = 0; i + 1 < pairPoints.Count; i += 2)
            {
                stats.segmentCount++;
                stats.totalLength += Vector3.Distance(pairPoints[i], pairPoints[i + 1]);
            }
        }

        private void CaptureGreenfieldPreviewResult(DoubleLayerIncisionGenerator.GenerateResult result)
        {
            _incisionMasterChainWorld.Clear();
            _incisionCenterCurveWorld.Clear();
            _outerIncisionLeftBoundaryWorld.Clear();
            _outerIncisionRightBoundaryWorld.Clear();
            _innerIncisionLeftBoundaryWorld.Clear();
            _innerIncisionRightBoundaryWorld.Clear();
            _outerIncisionStationCentersWorld.Clear();
            _innerIncisionStationCentersWorld.Clear();
            _incisionDebugCenterline.Clear();
            _incisionDebugTriangles.Clear();
            _incisionDeleteDebugSummaries.Clear();
            _incisionFinalTriangleAuditRecords.Clear();
            _incisionOuterPatchComponentCount = 0;
            _incisionInnerPatchComponentCount = 0;
            _incisionOuterQualitySummary = string.Empty;
            _incisionInnerQualitySummary = string.Empty;
            ClearRuntimePrecommitGeometry();
            ClearSutureSideGuides();

            if (result == null)
                return;

            if (result.outer != null)
            {
                _incisionMasterChainWorld.AddRange(result.outer.masterChainWorld);
                _incisionCenterCurveWorld.AddRange(result.outer.centerCurveWorld);
                _incisionOuterPatchComponentCount = result.outer.patchComponentCount;
                _incisionOuterQualitySummary = BuildGreenfieldQualitySummary(result.outer);
                _outerIncisionLeftBoundaryWorld.AddRange(result.outer.leftBoundaryWorld);
                _outerIncisionRightBoundaryWorld.AddRange(result.outer.rightBoundaryWorld);
                for (int i = 0; i < result.outer.stations.Count; i++)
                    _outerIncisionStationCentersWorld.Add(result.outer.stations[i].centerWorld);
                AppendGreenfieldDebugTriangles(result.outer.debugTriangles);
                _incisionDeleteDebugSummaries.Add(CreateGreenfieldDeleteSummary(result.outer));
                _incisionFinalTriangleAuditRecords.AddRange(BuildGreenfieldFinalTriangleAuditRecords(result.outer, outerMeshFilter != null ? outerMeshFilter.transform : null));
                CopyGreenfieldPrecommitLayerRuntimeState(result.outer, VesselLayer.Outer);
            }

            if (result.inner != null)
            {
                _incisionInnerPatchComponentCount = result.inner.patchComponentCount;
                _incisionInnerQualitySummary = BuildGreenfieldQualitySummary(result.inner);
                _innerIncisionLeftBoundaryWorld.AddRange(result.inner.leftBoundaryWorld);
                _innerIncisionRightBoundaryWorld.AddRange(result.inner.rightBoundaryWorld);
                for (int i = 0; i < result.inner.stations.Count; i++)
                    _innerIncisionStationCentersWorld.Add(result.inner.stations[i].centerWorld);
                AppendGreenfieldDebugTriangles(result.inner.debugTriangles);
                _incisionDeleteDebugSummaries.Add(CreateGreenfieldDeleteSummary(result.inner));
                _incisionFinalTriangleAuditRecords.AddRange(BuildGreenfieldFinalTriangleAuditRecords(result.inner, innerMeshFilter != null ? innerMeshFilter.transform : null));
                CopyGreenfieldPrecommitLayerRuntimeState(result.inner, VesselLayer.Inner);
            }

            RebuildSutureSideGuidesFromCurrentIncisionState();
            RebuildSuturePassSequenceState();
            AppendRawIncisionCenterline(_incisionCenterCurveWorld);
        }

        private void CaptureGreenfieldCommittedState(DoubleLayerIncisionGenerator.GenerateResult result)
        {
            _incisionMasterChainWorld.Clear();
            _outerIncisionLeftBoundaryWorld.Clear();
            _outerIncisionRightBoundaryWorld.Clear();
            _innerIncisionLeftBoundaryWorld.Clear();
            _innerIncisionRightBoundaryWorld.Clear();
            _outerIncisionLipPairs.Clear();
            _innerIncisionLipPairs.Clear();
            _outerIncisionStations.Clear();
            _innerIncisionStations.Clear();
            _outerIncisionCenterlineWorld.Clear();
            _innerIncisionCenterlineWorld.Clear();
            _outerIncisionStationCentersWorld.Clear();
            _innerIncisionStationCentersWorld.Clear();
            _incisionDebugPoints.Clear();
            _incisionDebugCenterline.Clear();
            _incisionDebugTriangles.Clear();
            _incisionDebugRibbonFrames.Clear();
            _incisionDeleteDebugSummaries.Clear();
            _incisionInsertedVertices.Clear();
            _incisionPairDebugPoints.Clear();
            _incisionRenderSampleDebugPoints.Clear();
            _incisionTargetTraceDebugRecords.Clear();
            _incisionAllTargetTraceDebugRecords.Clear();
            _incisionFinalTriangleAuditRecords.Clear();
            _runtimeCommittedIncisionDebugRibbonFrames.Clear();
            _runtimeCommittedIncisionDebugTriangles.Clear();
            _runtimeCommittedIncisionDeleteDebugSummaries.Clear();
            _runtimeCommittedIncisionTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionAllTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionFinalTriangleAuditRecords.Clear();
            _incisionCenterCurveWorld.Clear();
            _incisionOuterPatchComponentCount = 0;
            _incisionInnerPatchComponentCount = 0;
            _incisionOuterQualitySummary = string.Empty;
            _incisionInnerQualitySummary = string.Empty;
            ClearRuntimePrecommitGeometry();
            ClearSutureSideGuides();

            if (result == null)
                return;

            _incisionNaturalRetractionStartedLogged = false;
            _incisionNaturalRetractionUpdateLogCounter = 0;

            if (result.targetStrokeWorld != null)
            {
                if (_incisionStrokeWorld == null)
                    _incisionStrokeWorld = new List<Vector3>();
                _incisionStrokeWorld.Clear();
                _incisionStrokeWorld.AddRange(result.targetStrokeWorld);
            }

            if (result.outer != null)
            {
                _incisionMasterChainWorld.AddRange(result.outer.masterChainWorld);
                _incisionCenterCurveWorld.AddRange(result.outer.centerCurveWorld);
                _incisionOuterPatchComponentCount = result.outer.patchComponentCount;
                _incisionOuterQualitySummary = BuildGreenfieldQualitySummary(result.outer);
                _outerIncisionLeftBoundaryWorld.AddRange(result.outer.leftBoundaryWorld);
                _outerIncisionRightBoundaryWorld.AddRange(result.outer.rightBoundaryWorld);
                CopyGreenfieldLayerRuntimeState(result.outer, outerMeshFilter, VesselLayer.Outer);
                CopyGreenfieldPrecommitLayerRuntimeState(result.outer, VesselLayer.Outer);
                AppendGreenfieldDebugTriangles(result.outer.debugTriangles);
                _incisionDeleteDebugSummaries.Add(CreateGreenfieldDeleteSummary(result.outer));
                _incisionFinalTriangleAuditRecords.AddRange(BuildGreenfieldFinalTriangleAuditRecords(result.outer, outerMeshFilter != null ? outerMeshFilter.transform : null));
            }

            if (result.inner != null)
            {
                _incisionInnerPatchComponentCount = result.inner.patchComponentCount;
                _incisionInnerQualitySummary = BuildGreenfieldQualitySummary(result.inner);
                _innerIncisionLeftBoundaryWorld.AddRange(result.inner.leftBoundaryWorld);
                _innerIncisionRightBoundaryWorld.AddRange(result.inner.rightBoundaryWorld);
                CopyGreenfieldLayerRuntimeState(result.inner, innerMeshFilter, VesselLayer.Inner);
                CopyGreenfieldPrecommitLayerRuntimeState(result.inner, VesselLayer.Inner);
                AppendGreenfieldDebugTriangles(result.inner.debugTriangles);
                _incisionDeleteDebugSummaries.Add(CreateGreenfieldDeleteSummary(result.inner));
                _incisionFinalTriangleAuditRecords.AddRange(BuildGreenfieldFinalTriangleAuditRecords(result.inner, innerMeshFilter != null ? innerMeshFilter.transform : null));
            }

            _runtimeCommittedIncisionDebugTriangles.AddRange(_incisionDebugTriangles);
            _runtimeCommittedIncisionDeleteDebugSummaries.AddRange(_incisionDeleteDebugSummaries);
            _runtimeCommittedIncisionFinalTriangleAuditRecords.AddRange(_incisionFinalTriangleAuditRecords);
            AppendRawIncisionCenterline(_incisionCenterCurveWorld);
            RebuildSutureSideGuidesFromCurrentIncisionState();
            RebuildSuturePassSequenceState();
        }

        private void CopyGreenfieldLayerRuntimeState(DoubleLayerIncisionGenerator.LayerResult layerResult, MeshFilter filter, VesselLayer layer)
        {
            if (layerResult == null || filter == null)
                return;

            List<IncisionLipPair> lipPairs = layer == VesselLayer.Outer ? _outerIncisionLipPairs : _innerIncisionLipPairs;
            List<PendingLipStation> stations = layer == VesselLayer.Outer ? _outerIncisionStations : _innerIncisionStations;
            List<Vector3> centerline = layer == VesselLayer.Outer ? _outerIncisionCenterlineWorld : _innerIncisionCenterlineWorld;
            List<Vector3> stationCenters = layer == VesselLayer.Outer ? _outerIncisionStationCentersWorld : _innerIncisionStationCentersWorld;

            centerline.AddRange(layerResult.centerCurveWorld);
            for (int i = 0; i < layerResult.stations.Count; i++)
            {
                var src = layerResult.stations[i];
                if (src.leftVertexIndex < 0 || src.rightVertexIndex < 0 || src.leftVertexIndex == src.rightVertexIndex)
                    continue;

                Vector3 centerLocal = filter.transform.InverseTransformPoint(src.centerWorld);
                Vector3 splitWorld = src.rightWorld - src.leftWorld;
                Vector3 splitDirLocal = splitWorld.sqrMagnitude > 1e-10f
                    ? filter.transform.InverseTransformDirection(splitWorld.normalized)
                    : filter.transform.InverseTransformDirection(src.binormalWorld);
                if (splitDirLocal.sqrMagnitude <= 1e-10f)
                    splitDirLocal = Vector3.right;
                splitDirLocal.Normalize();

                ulong stationKey = MakeUndirectedEdgeKey(src.leftVertexIndex, src.rightVertexIndex);
                stations.Add(new PendingLipStation
                {
                    stationKey = stationKey,
                    leftLocalIndex = src.leftVertexIndex,
                    rightLocalIndex = src.rightVertexIndex,
                    seedCenterLocal = centerLocal,
                    splitDirLocal = splitDirLocal,
                    targetOpen = src.targetOpen,
                    weight = 1f,
                    strokeT = src.strokeT,
                    layer = (int)layer,
                    suppressRuntimeMotion = layerResult.isCircumferentialSeparationCut,
                });

                lipPairs.Add(new IncisionLipPair
                {
                    leftLocalIndex = src.leftVertexIndex,
                    rightLocalIndex = src.rightVertexIndex,
                    centerLocal = centerLocal,
                    splitDirLocal = splitDirLocal,
                    targetOpen = src.targetOpen,
                    weight = 1f,
                    strokeT = src.strokeT,
                    layer = (int)layer,
                });

                stationCenters.Add(src.centerWorld);
            }
        }

        private void AppendGreenfieldDebugTriangles(List<DoubleLayerIncisionGenerator.DebugTriangle> triangles)
        {
            if (triangles == null)
                return;

            for (int i = 0; i < triangles.Count; i++)
            {
                var tri = triangles[i];
                _incisionDebugTriangles.Add(new IncisionDebugTriangle
                {
                    a = tri.a,
                    b = tri.b,
                    c = tri.c,
                    center = (tri.a + tri.b + tri.c) / 3f,
                    layer = tri.layer,
                    kind = tri.kind == DoubleLayerIncisionGenerator.DebugTriangleKind.Excision
                        ? (int)IncisionPatchDebugKind.RemovedStripInterior
                        : (int)IncisionPatchDebugKind.CutPatch,
                    triIndex = -1,
                    ia = -1,
                    ib = -1,
                    ic = -1,
                });
            }
        }

        private string BuildGreenfieldQualitySummary(DoubleLayerIncisionGenerator.LayerResult layer)
        {
            if (layer == null)
                return string.Empty;

            string layerText = layer.layer == 0 ? "Outer" : (layer.layer == 1 ? "Inner" : "Layer");
            string status = $"{layerText} success={(layer.success ? 1 : 0)} stage={layer.failStage}";
            string structure = $"patchComp={layer.patchComponentCount} excisionComp={layer.excisionComponentCount} patchTri={layer.patchTriangleCount} excisionTri={layer.removedTriangleCount}";
            string strip = $"rows={layer.stripRowCount}/{layer.attemptedStripRowCount} tips={layer.tipStripRowCount} startTip={(layer.keptStartTipRow ? 1 : 0)} endTip={(layer.keptEndTipRow ? 1 : 0)} supportQuads={layer.supportQuadCount} excisionQuads={layer.excisionQuadCount} collapsed={layer.collapsedStripRowCount} repeated={layer.repeatedStripRowCount} crossed={layer.crossedStripRowCount}";
            string quality = $"avgOpen={layer.averageRealizedOpenWidth:F5} maxAspect={layer.maxPatchTriangleAspect:F2} minBoundary={layer.minStructuredBoundaryWidth:F5} stripTri={layer.stripCoveredTriangleCount} clippedPatch={layer.clippedPatchTriangleCount} outsideStrip={layer.patchOutsideStripTriangleCount} outsidePoly={layer.preservedOutsideFragmentCount} supportPoly={layer.supportPartitionPolygonCount}->{layer.supportMergedPolygonCount} excisionPoly={layer.excisionPartitionPolygonCount}->{layer.excisionMergedPolygonCount} mergeFallback={layer.cellMergeFallbackCount} droppedPoly={layer.droppedDegeneratePartitionPolygonCount} outsideChanged={layer.changedOutsideStripTriangleCount} leftLen={layer.leftBoundaryPolylineLength:F5} rightLen={layer.rightBoundaryPolylineLength:F5} gaps=({layer.minLeftStructuredGap:F5},{layer.minCenterStructuredGap:F5},{layer.minRightStructuredGap:F5}) maxJump={layer.maxStructuredBoundaryJump:F5} taper={layer.endTaperLengthWorld:F5} fallback={(layer.usedFallbackPath ? 1 : 0)}";
            return string.IsNullOrEmpty(layer.failReason)
                ? $"{status} | {structure} | {strip} | {quality}"
                : $"{status} | {structure} | {strip} | {quality} | reason={layer.failReason}";
        }

        private IncisionDeleteDebugSummary CreateGreenfieldDeleteSummary(DoubleLayerIncisionGenerator.LayerResult layer)
        {
            return new IncisionDeleteDebugSummary
            {
                layer = layer != null ? layer.layer : -1,
                success = layer != null && layer.success,
                failStage = layer != null ? layer.failStage : "Layer",
                failReason = layer != null ? layer.failReason : "Unknown",
                guideRadius = incisionBrushRadius,
                openWidth = incisionOpenWidth,
                sampleSpacing = incisionPathSampleSpacing,
                pathSnapPenalty = incisionPathSnapPenalty,
                anchorCount = Mathf.Max(4, incisionMaxAnchorCount),
                localSubdivisionPasses = incisionLocalSubdivisionPasses,
                guideLength = _incisionStrokeWorld != null ? ComputePolylineLengthWorld(_incisionStrokeWorld) : 0f,
                pathLength = layer != null ? layer.masterLength : 0f,
                strokePointCount = _incisionStrokeWorld != null ? _incisionStrokeWorld.Count : 0,
                avgTriangleEdgeLength = layer != null ? layer.averageEdgeLength : 0f,
                pathCount = layer != null && layer.masterPathVertexIndices != null ? layer.masterPathVertexIndices.Count : 0,
                lipPairCount = layer != null && layer.stations != null ? layer.stations.Count : 0,
                denseStationsCount = layer != null && layer.stations != null ? layer.stations.Count : 0,
                centerlineCount = layer != null && layer.centerCurveWorld != null ? layer.centerCurveWorld.Count : 0,
                patchComponentCount = layer != null ? layer.patchComponentCount : 0,
                excisionComponentCount = layer != null ? layer.excisionComponentCount : 0,
                preparedTriangleCount = layer != null ? layer.patchTriangleCount : 0,
                selectedTriangleCount = layer != null ? layer.removedTriangleCount : 0,
                keptCount = layer != null ? layer.keptTriangleCount : 0,
                removedCount = layer != null ? layer.removedTriangleCount : 0,
                pendingSegmentCount = layer != null ? layer.openBoundaryEdgeCount : 0,
                maxPatchTriangleAspect = layer != null ? layer.maxPatchTriangleAspect : 0f,
                averageRealizedOpenWidth = layer != null ? layer.averageRealizedOpenWidth : 0f,
                usedFallbackPath = layer != null && layer.usedFallbackPath,
                stripRowCount = layer != null ? layer.stripRowCount : 0,
                supportQuadCount = layer != null ? layer.supportQuadCount : 0,
                excisionQuadCount = layer != null ? layer.excisionQuadCount : 0,
                boundaryContinuityViolationCount = layer != null ? layer.boundaryContinuityViolationCount : 0,
                endTaperLengthWorld = layer != null ? layer.endTaperLengthWorld : 0f,
            };
        }

        private static List<IncisionFinalTriangleAuditRecord> BuildGreenfieldFinalTriangleAuditRecords(
            DoubleLayerIncisionGenerator.LayerResult layer,
            Transform meshTransform)
        {
            var records = new List<IncisionFinalTriangleAuditRecord>();
            if (layer == null || layer.mesh == null || meshTransform == null)
                return records;

            Vector3[] vertices = layer.mesh.vertices;
            int[] triangles = layer.mesh.triangles;
            if (vertices == null || triangles == null || triangles.Length < 3)
                return records;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int ia = triangles[t + 0];
                int ib = triangles[t + 1];
                int ic = triangles[t + 2];
                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                    continue;

                Vector3 aLocal = vertices[ia];
                Vector3 bLocal = vertices[ib];
                Vector3 cLocal = vertices[ic];
                Vector3 aWorld = meshTransform.TransformPoint(aLocal);
                Vector3 bWorld = meshTransform.TransformPoint(bLocal);
                Vector3 cWorld = meshTransform.TransformPoint(cLocal);
                Vector3 centerWorld = (aWorld + bWorld + cWorld) / 3f;

                int nearestStationIndex = FindNearestGreenfieldStationIndex(layer.stations, centerWorld);
                float signedLateral3D = 0f;
                float halfWidth3D = 0f;
                float normalizedLateral3D = -1f;
                float ribbonGap3D = 0f;
                float ribbonSurfaceOffset3D = 0f;
                bool insideSupportStrip = false;
                bool insideExcisionStrip = false;
                if (nearestStationIndex >= 0 && nearestStationIndex < layer.stations.Count)
                {
                    DoubleLayerIncisionGenerator.StationResult station = layer.stations[nearestStationIndex];
                    Vector3 delta = centerWorld - station.centerWorld;
                    signedLateral3D = Vector3.Dot(delta, station.binormalWorld);
                    halfWidth3D = 0.5f * Vector3.Distance(station.leftWorld, station.rightWorld);
                    float supportHalfWidth3D = halfWidth3D + Mathf.Max(1e-5f, layer.supportOffsetWorld);
                    normalizedLateral3D = halfWidth3D > 1e-6f ? Mathf.Abs(signedLateral3D) / halfWidth3D : -1f;
                    ribbonGap3D = Mathf.Max(0f, Mathf.Abs(signedLateral3D) - halfWidth3D);
                    ribbonSurfaceOffset3D = Mathf.Abs(Vector3.Dot(delta, station.normalWorld));
                    insideSupportStrip = Mathf.Abs(signedLateral3D) <= supportHalfWidth3D + 1e-5f;
                    insideExcisionStrip = Mathf.Abs(signedLateral3D) <= halfWidth3D + 1e-5f;
                }

                string bandLabel = insideExcisionStrip
                    ? "greenfield:final-tri-inside-excision-strip"
                    : insideSupportStrip
                        ? "greenfield:final-tri-inside-support-strip"
                        : "greenfield:final-tri-outside-strip";

                records.Add(new IncisionFinalTriangleAuditRecord
                {
                    center = centerWorld,
                    layer = layer.layer,
                    triIndex = t / 3,
                    ia = ia,
                    ib = ib,
                    ic = ic,
                    area2D = 0f,
                    area3D = 0.5f * Vector3.Cross(bWorld - aWorld, cWorld - aWorld).magnitude,
                    centerlineDistance2D = Mathf.Abs(signedLateral3D),
                    corridorHits = insideSupportStrip ? 1 : 0,
                    centralHits = insideExcisionStrip ? 1 : 0,
                    nearestRibbonFrameIndex = nearestStationIndex,
                    signedLateral3D = signedLateral3D,
                    normalizedLateral3D = normalizedLateral3D,
                    halfWidth3D = halfWidth3D,
                    ribbonGap3D = ribbonGap3D,
                    ribbonSurfaceOffset3D = ribbonSurfaceOffset3D,
                    overlapCandidate = insideSupportStrip,
                    overlapRibbon3D = insideSupportStrip,
                    insideBoundaryRegion = insideSupportStrip,
                    insideRibbonBand3D = insideSupportStrip,
                    insideRibbonCore3D = insideExcisionStrip,
                    removedCandidate = insideExcisionStrip,
                    failHint = bandLabel,
                });
            }

            return records;
        }

        private static int FindNearestGreenfieldStationIndex(List<DoubleLayerIncisionGenerator.StationResult> stations, Vector3 worldPoint)
        {
            if (stations == null || stations.Count == 0)
                return -1;

            int bestIndex = -1;
            float bestDistanceSq = float.PositiveInfinity;
            for (int i = 0; i < stations.Count; i++)
            {
                float distanceSq = (stations[i].centerWorld - worldPoint).sqrMagnitude;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        public bool TryApplyProceduralIncision(bool directRuntimeGenerate = false)
        {
            if (Application.isPlaying && runtimeSingleIncisionMode && _runtimeIncisionCommittedOnce)
            {
                Debug.Log("[DoubleLayerDriver] 运行时单次切口模式已提交过一次，本次忽略重复提交。");
                return false;
            }

            bool built = true;
            if (directRuntimeGenerate)
            {
                built = TryBuildProceduralIncisionStrokeInternal(clearExisting: true, logWarnings: true, buildPreviewDebug: false);
            }
            else if (_incisionStrokeWorld == null || _incisionStrokeWorld.Count < 2)
            {
                built = TryGenerateProceduralIncisionStroke(clearExisting: true, logWarnings: true);
            }

            if (!built)
                return false;

            bool applied = TryApplyCurrentIncision();
            if (applied)
                _runtimeIncisionCommittedOnce = true;
            return applied;
        }

        private bool TryApplyProceduralIncisionDirectRuntimeOnce()
        {
            return TryApplyProceduralIncision(directRuntimeGenerate: true);
        }

        [ContextMenu("Generate Procedural Incision Stroke")]
        private void GenerateProceduralIncisionStrokeContextMenu()
        {
            bool ok = TryGenerateProceduralIncisionStroke(clearExisting: true, logWarnings: true);
            if (ok)
                Debug.Log($"[DoubleLayerDriver] 已生成参数化切口预览轨迹，points={_incisionStrokeWorld.Count}。");
        }

        [ContextMenu("Generate And Apply Procedural Incision")]
        private void GenerateAndApplyProceduralIncisionContextMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DoubleLayerDriver] 当前工作流已改为“运行时生成一次”。编辑态只保留目标线/调试预览，真正切口请进入 Play 后自动生成。");
                TryGenerateProceduralIncisionStroke(clearExisting: true, logWarnings: true);
                return;
            }

            TryApplyProceduralIncision();
        }

        private void BuildProceduralIncisionPreviewDebug(bool logWarnings)
        {
            RefreshIncisionDebugData();

            if (_incisionStrokeWorld == null || _incisionStrokeWorld.Count < 2)
            {
                if (logWarnings)
                    Debug.LogWarning("[DoubleLayerDriver] 参数化切口预览轨迹为空，无法生成预览调试数据。");
                return;
            }

            // 先保留目标线本身，供红线显示
            AppendRawIncisionCenterline(_incisionStrokeWorld);

            if (!TryBuildRuntimeStylePreviewDebugFromCurrentMeshes(logWarnings))
            {
                if (logWarnings)
                    Debug.LogWarning("[DoubleLayerDriver] 运行时同链路预览构建失败，当前只显示目标线。");
            }
        }

        private bool TryBuildRuntimeStylePreviewDebugFromCurrentMeshes(bool logWarnings)
        {
            Mesh outerMesh = null;
            Mesh innerMesh = null;

            if (Application.isPlaying)
            {
                outerMesh = _outerMesh;
                innerMesh = _innerMesh;
            }
            else
            {
                outerMesh = outerMeshFilter != null ? outerMeshFilter.sharedMesh : null;
                innerMesh = innerMeshFilter != null ? innerMeshFilter.sharedMesh : null;
            }

            if (outerMesh == null || innerMesh == null || outerMeshFilter == null || innerMeshFilter == null)
            {
                if (logWarnings)
                {
                    Debug.LogWarning(
                        $"[DoubleLayerDriver] 编辑态 dry-run 预览前置条件不满足：outerMeshNull={outerMesh == null}, innerMeshNull={innerMesh == null}, outerFilterNull={outerMeshFilter == null}, innerFilterNull={innerMeshFilter == null}");
                }
                return false;
            }

            var strokeCopy = new List<Vector3>(_incisionStrokeWorld);

            float openWidth = Mathf.Max(1e-5f, incisionOpenWidth);
            float pathSpacing = incisionPathSampleSpacing;
            float snapPenalty = incisionPathSnapPenalty;
            int anchorCount = Mathf.Max(4, incisionMaxAnchorCount);
            float profileVariation = ResolveIncisionProfileVariation();
            float curvatureWidthScale = ResolveIncisionCurvatureWidthScale();

            float outerGuideRadius = Mathf.Max(1e-5f, incisionBrushRadius);
            float innerGuideRadius = Mathf.Max(1e-5f, incisionBrushRadius * Mathf.Max(0.5f, incisionInnerRadiusScale));

            // 注意：这里只做 dry-run，所以直接拿当前 mesh 做 PrepareEdgeSnappedIncisionResult，
            // 不 ApplyToMesh，不 InitSim，不改 sharedMesh。
            if (!PrepareEdgeSnappedIncisionResult(
                    outerMesh,
                    outerMeshFilter.transform,
                    strokeCopy,
                    outerGuideRadius,
                    openWidth,
                    pathSpacing,
                    snapPenalty,
                    anchorCount,
                    profileVariation,
                    curvatureWidthScale,
                    (int)VesselLayer.Outer,
                    enableIncisionTargetTrace,
                    incisionTargetTraceCount,
                    incisionTargetTraceNeighborRadius,
                    incisionTargetTraceLogToConsole,
                    incisionLocalSubdivisionPasses,
                    out var outerResult,
                    out var outerStageSummary))
            {
                if (logWarnings)
                {
                    Debug.LogWarning(
                        $"[DoubleLayerDriver] 编辑态 dry-run 外壁切口预览失败。strokePts={strokeCopy.Count}, guideRadius={outerGuideRadius:F6}, openWidth={openWidth:F6}, pathSpacing={pathSpacing:F6}, snapPenalty={snapPenalty:F3}, anchorCount={anchorCount}, stage={outerStageSummary.failStage}, reason={outerStageSummary.failReason}");
                }
                return false;
            }

            if (!PrepareEdgeSnappedIncisionResult(
                    innerMesh,
                    innerMeshFilter.transform,
                    strokeCopy,
                    innerGuideRadius,
                    openWidth,
                    pathSpacing,
                    snapPenalty,
                    anchorCount,
                    profileVariation,
                    curvatureWidthScale,
                    (int)VesselLayer.Inner,
                    enableIncisionTargetTrace,
                    incisionTargetTraceCount,
                    incisionTargetTraceNeighborRadius,
                    incisionTargetTraceLogToConsole,
                    incisionLocalSubdivisionPasses,
                    out var innerResult,
                    out var innerStageSummary))
            {
                if (logWarnings)
                {
                    Debug.LogWarning(
                        $"[DoubleLayerDriver] 编辑态 dry-run 内壁切口预览失败。strokePts={strokeCopy.Count}, guideRadius={innerGuideRadius:F6}, openWidth={openWidth:F6}, pathSpacing={pathSpacing:F6}, snapPenalty={snapPenalty:F3}, anchorCount={anchorCount}, stage={innerStageSummary.failStage}, reason={innerStageSummary.failReason}");
                }
                return false;
            }

            if (!ValidateSynchronizedEdgeSnappedResults(outerResult, innerResult))
            {
                if (logWarnings)
                {
                    int outerPathCount = outerResult != null && outerResult.path != null ? outerResult.path.Count : 0;
                    int innerPathCount = innerResult != null && innerResult.path != null ? innerResult.path.Count : 0;
                    int outerStationCount = outerResult != null && outerResult.stations != null ? outerResult.stations.Count : 0;
                    int innerStationCount = innerResult != null && innerResult.stations != null ? innerResult.stations.Count : 0;
                    float outerLen = outerResult != null ? outerResult.pathLength : 0f;
                    float innerLen = innerResult != null ? innerResult.pathLength : 0f;
                    Debug.LogWarning(
                        $"[DoubleLayerDriver] 编辑态 dry-run 外/内壁切口不同步。outerPath={outerPathCount}, innerPath={innerPathCount}, outerStations={outerStationCount}, innerStations={innerStationCount}, outerLen={outerLen:F6}, innerLen={innerLen:F6}");
                }
                return false;
            }

            _incisionDebugTriangles.Clear();
            _incisionDebugRibbonFrames.Clear();
            _incisionDeleteDebugSummaries.Clear();
            _incisionDebugCenterline.Clear();
            _incisionInsertedVertices.Clear();
            _incisionPairDebugPoints.Clear();
            _incisionRenderSampleDebugPoints.Clear();
            _incisionTargetTraceDebugRecords.Clear();
            _incisionAllTargetTraceDebugRecords.Clear();
            _incisionDebugPoints.Clear();

            // 编辑态主预览只保留“外壁 dry-run 得到的真实窄缝 ribbon”。
            // inner 结果仅用于同步校验，不再混入 Scene 主预览，避免出现下方黄线/候选三角干扰。
            if (outerResult.debugRibbonFrames != null && outerResult.debugRibbonFrames.Count > 0)
                _incisionDebugRibbonFrames.AddRange(outerResult.debugRibbonFrames);

            _incisionDeleteDebugSummaries.Add(outerResult.deleteDebugSummary);
            _incisionDeleteDebugSummaries.Add(innerResult.deleteDebugSummary);
            if (outerResult.targetTraceRecords != null && outerResult.targetTraceRecords.Count > 0)
                _incisionTargetTraceDebugRecords.AddRange(outerResult.targetTraceRecords);
            if (innerResult.targetTraceRecords != null && innerResult.targetTraceRecords.Count > 0)
                _incisionTargetTraceDebugRecords.AddRange(innerResult.targetTraceRecords);
            if (outerResult.allTargetTraceRecords != null && outerResult.allTargetTraceRecords.Count > 0)
                _incisionAllTargetTraceDebugRecords.AddRange(outerResult.allTargetTraceRecords);
            if (innerResult.allTargetTraceRecords != null && innerResult.allTargetTraceRecords.Count > 0)
                _incisionAllTargetTraceDebugRecords.AddRange(innerResult.allTargetTraceRecords);

            if (_incisionDebugRibbonFrames.Count <= 0 && logWarnings)
            {
                int outerPathCount = outerResult.path != null ? outerResult.path.Count : 0;
                int outerStationCount = outerResult.stations != null ? outerResult.stations.Count : 0;
                int outerCenterlineCount = outerResult.centerlineWorld != null ? outerResult.centerlineWorld.Count : 0;
                Debug.LogWarning(
                    $"[DoubleLayerDriver] 编辑态 dry-run 已跑通，但没有生成可显示的 ribbon frames。outerPath={outerPathCount}, outerStations={outerStationCount}, outerCenterline={outerCenterlineCount}");
            }

            // 红色目标线单独由 _incisionStrokeWorld 绘制；这里不再额外叠加 centerline/patch/pair 等调试层。
            return _incisionDebugRibbonFrames.Count > 0;
        }



        private bool TryComputePreviewSplitDirection(
            List<Vector3> strokeWorld,
            Transform tr,
            Vector3 axisCenterLocal,
            int index,
            out Vector3 splitDirWorld)
        {
            splitDirWorld = tr != null ? tr.right : Vector3.right;

            if (strokeWorld == null || strokeWorld.Count == 0 || tr == null)
                return false;

            Vector3 tangentWorld;
            if (strokeWorld.Count == 1)
            {
                tangentWorld = tr.forward;
            }
            else if (index == 0)
            {
                tangentWorld = strokeWorld[1] - strokeWorld[0];
            }
            else if (index == strokeWorld.Count - 1)
            {
                tangentWorld = strokeWorld[index] - strokeWorld[index - 1];
            }
            else
            {
                tangentWorld = strokeWorld[index + 1] - strokeWorld[index - 1];
            }

            if (tangentWorld.sqrMagnitude <= 1e-12f)
                tangentWorld = tr.forward;
            else
                tangentWorld.Normalize();

            Vector3 centerLocal = tr.InverseTransformPoint(strokeWorld[index]);
            Vector3 radialLocal = new Vector3(
                centerLocal.x - axisCenterLocal.x,
                centerLocal.y - axisCenterLocal.y,
                0f);

            Vector3 normalWorld = radialLocal.sqrMagnitude > 1e-12f
                ? tr.TransformDirection(radialLocal.normalized)
                : tr.up;

            Vector3 split = Vector3.Cross(normalWorld, tangentWorld);

            if (split.sqrMagnitude <= 1e-12f)
                split = Vector3.ProjectOnPlane(tr.right, tangentWorld);

            if (split.sqrMagnitude <= 1e-12f)
                split = Vector3.ProjectOnPlane(tr.up, tangentWorld);

            if (split.sqrMagnitude <= 1e-12f)
                return false;

            splitDirWorld = split.normalized;
            return true;
        }

        [Obsolete(
            "Legacy local-axis stroke builder is not valid for curved vessels. " +
            "Use TryBuildProceduralIncisionStrokeInternal.",
            true)]
        private bool BuildProceduralIncisionStrokeWorld(List<Vector3> outStrokeWorld, bool logWarnings)
        {
            if (outStrokeWorld == null)
                return false;

            outStrokeWorld.Clear();

            Mesh outer = outerMeshFilter != null ? outerMeshFilter.sharedMesh : null;
            Transform outerTf = outerMeshFilter != null ? outerMeshFilter.transform : null;
            if (outer == null || outerTf == null)
            {
                if (logWarnings)
                    Debug.LogWarning("[DoubleLayerDriver] 外壁 Mesh / Transform 不可用，无法生成参数化切口轨迹。");
                return false;
            }

            Vector3[] vertices = outer.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                if (logWarnings)
                    Debug.LogWarning("[DoubleLayerDriver] 外壁顶点为空，无法生成参数化切口轨迹。");
                return false;
            }

            Bounds bounds = outer.bounds;
            Vector3 boundsCenter = bounds.center;
            Vector2 axisCenterXY = new Vector2(boundsCenter.x + proceduralIncisionCenterOffsetLocal.x, boundsCenter.y + proceduralIncisionCenterOffsetLocal.y);
            float zMin = bounds.min.z;
            float zMax = bounds.max.z;
            float zCenter = Mathf.Lerp(zMin, zMax, Mathf.Clamp01(proceduralIncisionNormalizedZ)) + proceduralIncisionCenterOffsetLocal.z;
            float length = Mathf.Max(1e-5f, proceduralIncisionLength);
            int sampleCount = Mathf.Max(4, proceduralIncisionSampleCount);
            float theta0 = proceduralIncisionCircumferenceAngleDeg * Mathf.Deg2Rad;
            float dirRad = proceduralIncisionDirectionDeg * Mathf.Deg2Rad;
            float cosDir = Mathf.Cos(dirRad);
            float sinDir = Mathf.Sin(dirRad);
            float surfaceLift = Mathf.Max(0f, proceduralIncisionSurfaceLift);
            float zQueryWindow = Mathf.Max(bounds.size.z * 0.06f, length * 0.2f, 1e-4f);

            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount > 1 ? (float)i / (sampleCount - 1) : 0.5f;
                float s = (t - 0.5f) * length;
                float z = Mathf.Clamp(zCenter + sinDir * s, zMin, zMax);
                float radius = EstimateProceduralIncisionRadiusLocal(vertices, axisCenterXY, z, zQueryWindow, bounds);
                if (radius <= 1e-6f)
                    continue;

                float theta = theta0 + (cosDir * s / Mathf.Max(radius, 1e-5f));
                Vector3 normalLocal = new Vector3(Mathf.Cos(theta), Mathf.Sin(theta), 0f);
                Vector3 localPoint = new Vector3(axisCenterXY.x, axisCenterXY.y, z) + normalLocal * (radius + surfaceLift);
                outStrokeWorld.Add(outerTf.TransformPoint(localPoint));
            }

            if (outStrokeWorld.Count < 2)
            {
                if (logWarnings)
                    Debug.LogWarning("[DoubleLayerDriver] 参数化切口轨迹生成失败，得到的有效采样点不足 2 个。");
                return false;
            }

            return true;
        }

        private static float EstimateProceduralIncisionRadiusLocal(Vector3[] vertices, Vector2 axisCenterXY, float zCenter, float zWindow, Bounds bounds)
        {
            if (vertices == null || vertices.Length == 0)
                return Mathf.Max(1e-4f, 0.5f * (bounds.extents.x + bounds.extents.y));

            float sum = 0f;
            int count = 0;
            float clampedWindow = Mathf.Max(1e-5f, zWindow);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                if (Mathf.Abs(v.z - zCenter) > clampedWindow)
                    continue;

                Vector2 planar = new Vector2(v.x - axisCenterXY.x, v.y - axisCenterXY.y);
                float r = planar.magnitude;
                if (r <= 1e-6f)
                    continue;

                sum += r;
                count++;
            }

            if (count > 0)
                return Mathf.Max(1e-5f, sum / count);

            sum = 0f;
            count = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                Vector2 planar = new Vector2(v.x - axisCenterXY.x, v.y - axisCenterXY.y);
                float r = planar.magnitude;
                if (r <= 1e-6f)
                    continue;
                sum += r;
                count++;
            }

            if (count > 0)
                return Mathf.Max(1e-5f, sum / count);

            return Mathf.Max(1e-4f, 0.5f * (Mathf.Abs(bounds.extents.x) + Mathf.Abs(bounds.extents.y)));
        }

        public bool TryApplyCurrentIncision()
        {
            if (UseGreenfieldIncisionPipeline())
                return TryApplyCurrentIncisionGreenfield();

            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DoubleLayerDriver] 双层同步切口最小版仅建议在 Play 模式下提交，避免直接改写导入 Mesh 资源。请先进入 Play，再点 Apply。");
                return false;
            }

            if (runtimeSingleIncisionMode && _runtimeIncisionCommittedOnce)
            {
                Debug.Log("[DoubleLayerDriver] 运行时单次切口模式已提交过一次，本次忽略重复提交。");
                return false;
            }

            if (_outerMesh == null || _innerMesh == null)
                PrepareRuntimeMeshesIfNeeded();

            _outerMesh = outerMeshFilter != null ? outerMeshFilter.sharedMesh : null;
            _innerMesh = innerMeshFilter != null ? innerMeshFilter.sharedMesh : null;
            if (_outerMesh == null || _innerMesh == null || outerMeshFilter == null || innerMeshFilter == null)
            {
                Debug.LogWarning("[DoubleLayerDriver] 外壁/内壁 MeshFilter 或 Mesh 不可用，无法提交双层同步切口。");
                return false;
            }

            if (_incisionStrokeWorld == null || _incisionStrokeWorld.Count < 2)
            {
                Debug.LogWarning("[DoubleLayerDriver] 当前没有可提交的切口轨迹。请先生成程序化切口线。");
                return false;
            }

            var strokeCopy = new List<Vector3>(_incisionStrokeWorld);
            _simulator?.ComputeAsyncJobs();

            float outerGuideRadius = Mathf.Max(1e-5f, incisionBrushRadius);
            float innerGuideRadius = Mathf.Max(1e-5f, incisionBrushRadius * Mathf.Max(1f, incisionInnerRadiusScale));
            float openWidth = Mathf.Max(0f, incisionOpenWidth);
            float pathSpacing = incisionPathSampleSpacing;
            float snapPenalty = Mathf.Max(0f, incisionPathSnapPenalty);
            int anchorCount = Mathf.Max(4, incisionMaxAnchorCount);
            float profileVariation = ResolveIncisionProfileVariation();
            float curvatureWidthScale = ResolveIncisionCurvatureWidthScale();

            if (!PrepareEdgeSnappedIncisionResult(_outerMesh, outerMeshFilter.transform, strokeCopy, outerGuideRadius, openWidth, pathSpacing, snapPenalty, anchorCount, profileVariation, curvatureWidthScale, (int)VesselLayer.Outer, enableIncisionTargetTrace, incisionTargetTraceCount, incisionTargetTraceNeighborRadius, incisionTargetTraceLogToConsole, incisionLocalSubdivisionPasses, out var outerResult, out var outerStageSummary))
            {
                RefreshIncisionDebugData();
                _incisionDeleteDebugSummaries.Add(outerStageSummary);
                _runtimeCommittedIncisionDeleteDebugSummaries.Clear();
                _runtimeCommittedIncisionDeleteDebugSummaries.AddRange(_incisionDeleteDebugSummaries);
                Debug.LogWarning($"[DoubleLayerDriver] 外壁边吸附切口生成失败，本次双层同步切口已取消。stage={outerStageSummary.failStage} reason={outerStageSummary.failReason}");
                return false;
            }

            if (!PrepareEdgeSnappedIncisionResult(_innerMesh, innerMeshFilter.transform, strokeCopy, innerGuideRadius, openWidth, pathSpacing, snapPenalty, anchorCount, profileVariation, curvatureWidthScale, (int)VesselLayer.Inner, enableIncisionTargetTrace, incisionTargetTraceCount, incisionTargetTraceNeighborRadius, incisionTargetTraceLogToConsole, incisionLocalSubdivisionPasses, out var innerResult, out var innerStageSummary))
            {
                RefreshIncisionDebugData();
                _incisionDeleteDebugSummaries.Add(outerStageSummary);
                _incisionDeleteDebugSummaries.Add(innerStageSummary);
                _runtimeCommittedIncisionDeleteDebugSummaries.Clear();
                _runtimeCommittedIncisionDeleteDebugSummaries.AddRange(_incisionDeleteDebugSummaries);
                Debug.LogWarning($"[DoubleLayerDriver] 内壁边吸附切口生成失败，本次双层同步切口已取消。stage={innerStageSummary.failStage} reason={innerStageSummary.failReason}");
                return false;
            }

            if (!ValidateSynchronizedEdgeSnappedResults(outerResult, innerResult))
            {
                outerStageSummary.failStage = "Sync";
                outerStageSummary.failReason = "ValidateSynchronizedEdgeSnappedResults 失败。";
                innerStageSummary.failStage = "Sync";
                innerStageSummary.failReason = "ValidateSynchronizedEdgeSnappedResults 失败。";
                RefreshIncisionDebugData();
                _incisionDeleteDebugSummaries.Add(outerStageSummary);
                _incisionDeleteDebugSummaries.Add(innerStageSummary);
                _runtimeCommittedIncisionDeleteDebugSummaries.Clear();
                _runtimeCommittedIncisionDeleteDebugSummaries.AddRange(_incisionDeleteDebugSummaries);
                Debug.LogWarning("[DoubleLayerDriver] 外/内壁边链不同步，本次双层同步切口已取消。");
                return false;
            }

            CollapseGeometricIncisionOpeningForRuntimeResult(outerResult.workMesh, outerResult.stations, "Outer");
            CollapseGeometricIncisionOpeningForRuntimeResult(innerResult.workMesh, innerResult.stations, "Inner");
            outerResult.workMesh.ApplyToMesh(_outerMesh);
            innerResult.workMesh.ApplyToMesh(_innerMesh);
            _outerMesh.RecalculateNormals();
            _outerMesh.RecalculateBounds();
            _innerMesh.RecalculateNormals();
            _innerMesh.RecalculateBounds();
            if (outerMeshFilter != null) outerMeshFilter.sharedMesh = _outerMesh;
            if (innerMeshFilter != null) innerMeshFilter.sharedMesh = _innerMesh;

            InitSim();

            if (_incisionStrokeWorld == null)
                _incisionStrokeWorld = new List<Vector3>();
            _incisionStrokeWorld.Clear();
            _incisionStrokeWorld.AddRange(strokeCopy);

            _outerIncisionLipPairs.Clear();
            _outerIncisionLipPairs.AddRange(outerResult.lipPairs);
            _innerIncisionLipPairs.Clear();
            _innerIncisionLipPairs.AddRange(innerResult.lipPairs);
            _outerIncisionStations.Clear();
            _outerIncisionStations.AddRange(outerResult.stations);
            _innerIncisionStations.Clear();
            _innerIncisionStations.AddRange(innerResult.stations);
            _incisionNaturalRetractionStartedLogged = false;
            _incisionNaturalRetractionUpdateLogCounter = 0;
            _outerIncisionCenterlineWorld.Clear();
            _outerIncisionCenterlineWorld.AddRange(outerResult.centerlineWorld);
            _innerIncisionCenterlineWorld.Clear();
            _innerIncisionCenterlineWorld.AddRange(innerResult.centerlineWorld);
            _outerIncisionStationCentersWorld.Clear();
            _outerIncisionStationCentersWorld.AddRange(outerResult.stationCentersWorld);
            _innerIncisionStationCentersWorld.Clear();
            _innerIncisionStationCentersWorld.AddRange(innerResult.stationCentersWorld);

            _incisionFramesSinceApply = 0;
            BuildMiddleWallBoundaryCache();
            RefreshIncisionDebugData();
            if (outerResult.debugTriangles != null) _incisionDebugTriangles.AddRange(outerResult.debugTriangles);
            if (innerResult.debugTriangles != null) _incisionDebugTriangles.AddRange(innerResult.debugTriangles);
            if (outerResult.debugRibbonFrames != null) _incisionDebugRibbonFrames.AddRange(outerResult.debugRibbonFrames);
            if (innerResult.debugRibbonFrames != null) _incisionDebugRibbonFrames.AddRange(innerResult.debugRibbonFrames);
            _incisionDeleteDebugSummaries.Add(outerResult.deleteDebugSummary);
            _incisionDeleteDebugSummaries.Add(innerResult.deleteDebugSummary);
            if (outerResult.targetTraceRecords != null) _incisionTargetTraceDebugRecords.AddRange(outerResult.targetTraceRecords);
            if (innerResult.targetTraceRecords != null) _incisionTargetTraceDebugRecords.AddRange(innerResult.targetTraceRecords);
            if (outerResult.allTargetTraceRecords != null) _incisionAllTargetTraceDebugRecords.AddRange(outerResult.allTargetTraceRecords);
            if (innerResult.allTargetTraceRecords != null) _incisionAllTargetTraceDebugRecords.AddRange(innerResult.allTargetTraceRecords);
            if (outerResult.finalTriangleAuditRecords != null) _incisionFinalTriangleAuditRecords.AddRange(outerResult.finalTriangleAuditRecords);
            if (innerResult.finalTriangleAuditRecords != null) _incisionFinalTriangleAuditRecords.AddRange(innerResult.finalTriangleAuditRecords);

            _runtimeCommittedIncisionDebugRibbonFrames.Clear();
            _runtimeCommittedIncisionDebugRibbonFrames.AddRange(_incisionDebugRibbonFrames);
            _runtimeCommittedIncisionDebugTriangles.Clear();
            _runtimeCommittedIncisionDebugTriangles.AddRange(_incisionDebugTriangles);
            _runtimeCommittedIncisionDeleteDebugSummaries.Clear();
            _runtimeCommittedIncisionDeleteDebugSummaries.AddRange(_incisionDeleteDebugSummaries);
            _runtimeCommittedIncisionTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionAllTargetTraceDebugRecords.Clear();
            _runtimeCommittedIncisionFinalTriangleAuditRecords.Clear();
            _runtimeCommittedIncisionTargetTraceDebugRecords.AddRange(_incisionTargetTraceDebugRecords);
            _runtimeCommittedIncisionAllTargetTraceDebugRecords.AddRange(_incisionAllTargetTraceDebugRecords);
            _runtimeCommittedIncisionFinalTriangleAuditRecords.AddRange(_incisionFinalTriangleAuditRecords);

            AppendRawIncisionCenterline(_outerIncisionCenterlineWorld);
            AppendRawIncisionCenterline(_innerIncisionCenterlineWorld);
            ClearIncisionMarkerMesh();
            UpdateMiddleWallFillMesh();
            UpdateIncisionSidewallMesh();
            _runtimeIncisionCommittedOnce = true;

            Debug.Log($"[DoubleLayerDriver] 双层同步切口提交成功：outerPath={outerResult.path.Count}, innerPath={innerResult.path.Count}, outerStations={outerResult.stations.Count}, innerStations={innerResult.stations.Count}");
            return true;
        }

        private bool TryApplyCurrentIncisionGreenfield()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DoubleLayerDriver] 绿地双层切口提交只允许在 Play 模式执行，避免直接改写导入 mesh 资源。");
                return false;
            }

            if (runtimeSingleIncisionMode && _runtimeIncisionCommittedOnce)
            {
                Debug.Log("[DoubleLayerDriver] 运行时单次切口模式已提交过一次，本次忽略重复提交。");
                return false;
            }

            if (_outerMesh == null || _innerMesh == null)
                PrepareRuntimeMeshesIfNeeded();

            _outerMesh = outerMeshFilter != null ? outerMeshFilter.sharedMesh : null;
            _innerMesh = innerMeshFilter != null ? innerMeshFilter.sharedMesh : null;
            if (_outerMesh == null || _innerMesh == null || outerMeshFilter == null || innerMeshFilter == null)
            {
                Debug.LogWarning("[DoubleLayerDriver] 外壁/内壁 MeshFilter 或 Mesh 不可用，无法提交绿地双层切口。");
                return false;
            }

            _simulator?.ComputeAsyncJobs();
            if (!TryGenerateGreenfieldIncisionResult(out var result, logWarnings: true))
                return false;

            if (result.outer == null || result.inner == null || result.outer.mesh == null || result.inner.mesh == null)
            {
                Debug.LogWarning("[DoubleLayerDriver] 绿地切口结果缺少 outer/inner mesh，无法提交。");
                return false;
            }

            _outerMesh = result.outer.mesh;
            _innerMesh = result.inner.mesh;
            CollapseGeometricIncisionOpeningForGreenfieldLayer(_outerMesh, outerMeshFilter != null ? outerMeshFilter.transform : null, result.outer, "Outer");
            CollapseGeometricIncisionOpeningForGreenfieldLayer(_innerMesh, innerMeshFilter != null ? innerMeshFilter.transform : null, result.inner, "Inner");
            _outerMesh.RecalculateNormals();
            _outerMesh.RecalculateBounds();
            _innerMesh.RecalculateNormals();
            _innerMesh.RecalculateBounds();
            if (outerMeshFilter != null)
                outerMeshFilter.sharedMesh = _outerMesh;
            if (innerMeshFilter != null)
                innerMeshFilter.sharedMesh = _innerMesh;

            InitSim();
            CaptureGreenfieldCommittedState(result);
            _incisionFramesSinceApply = 0;
            BuildMiddleWallBoundaryCache();
            ClearIncisionMarkerMesh();
            UpdateMiddleWallFillMesh();
            UpdateIncisionSidewallMesh();
            _runtimeIncisionCommittedOnce = true;

            Debug.Log(
                $"[DoubleLayerDriver] 绿地双层切口提交成功，outerStations={_outerIncisionStations.Count}, innerStations={_innerIncisionStations.Count}, outerRemoved={result.outer.removedTriangleCount}, innerRemoved={result.inner.removedTriangleCount}");
            return true;
        }

        private int CollapseGeometricIncisionOpeningForRuntimeResult(PreciseIncisionMesh data, List<PendingLipStation> stations, string layerLabel)
        {
            if (!ResolveEnableIncisionNaturalRetraction() || data == null || data.vertices == null || stations == null || stations.Count == 0)
                return 0;

            var offsetAccum = new Dictionary<int, Vector3>();
            var offsetCount = new Dictionary<int, int>();
            float openBeforeSum = 0f;
            float openAfterSum = 0f;
            int collapsed = 0;

            for (int i = 0; i < stations.Count; i++)
            {
                if (!TryResolveStationBoundaryFrame(data, stations, i, out int leftIndex, out int rightIndex, out _, out Vector3 splitDir, out _))
                    continue;
                if (!TryComputeCollapsedLipOffsets(data.vertices[leftIndex], data.vertices[rightIndex], splitDir, stations[i].targetOpen, out Vector3 leftOffset, out Vector3 rightOffset, out float openBefore, out float openAfter))
                    continue;

                AccumulateVertexOffset(offsetAccum, offsetCount, leftIndex, leftOffset);
                AccumulateVertexOffset(offsetAccum, offsetCount, rightIndex, rightOffset);
                openBeforeSum += openBefore;
                openAfterSum += openAfter;
                collapsed++;
            }

            ApplyAveragedVertexOffsets(data.vertices, offsetAccum, offsetCount);
            LogNaturalRetractionGeometryBaseline(layerLabel, collapsed, openBeforeSum, openAfterSum);
            return collapsed;
        }

        private int CollapseGeometricIncisionOpeningForGreenfieldLayer(Mesh mesh, Transform meshTransform, DoubleLayerIncisionGenerator.LayerResult layerResult, string layerLabel)
        {
            if (!ResolveEnableIncisionNaturalRetraction() || mesh == null || layerResult == null ||
                layerResult.isCircumferentialSeparationCut || layerResult.stations == null || layerResult.stations.Count == 0)
                return 0;

            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0)
                return 0;

            var offsetAccum = new Dictionary<int, Vector3>();
            var offsetCount = new Dictionary<int, int>();
            float openBeforeSum = 0f;
            float openAfterSum = 0f;
            int collapsed = 0;

            for (int i = 0; i < layerResult.stations.Count; i++)
            {
                var station = layerResult.stations[i];
                int leftIndex = station.leftVertexIndex;
                int rightIndex = station.rightVertexIndex;
                if (leftIndex < 0 || rightIndex < 0 || leftIndex >= vertices.Length || rightIndex >= vertices.Length || leftIndex == rightIndex)
                    continue;

                Vector3 splitWorld = station.rightWorld - station.leftWorld;
                if (splitWorld.sqrMagnitude <= 1e-12f)
                    splitWorld = station.binormalWorld;

                Vector3 splitDirLocal = splitWorld.sqrMagnitude > 1e-12f
                    ? (meshTransform != null ? meshTransform.InverseTransformDirection(splitWorld.normalized) : splitWorld.normalized)
                    : vertices[rightIndex] - vertices[leftIndex];
                if (splitDirLocal.sqrMagnitude <= 1e-12f)
                    splitDirLocal = Vector3.right;
                splitDirLocal.Normalize();

                if (Vector3.Dot(vertices[rightIndex] - vertices[leftIndex], splitDirLocal) < 0f)
                    splitDirLocal = -splitDirLocal;

                if (!TryComputeCollapsedLipOffsets(vertices[leftIndex], vertices[rightIndex], splitDirLocal, station.targetOpen, out Vector3 leftOffset, out Vector3 rightOffset, out float openBefore, out float openAfter))
                    continue;

                AccumulateVertexOffset(offsetAccum, offsetCount, leftIndex, leftOffset);
                AccumulateVertexOffset(offsetAccum, offsetCount, rightIndex, rightOffset);
                openBeforeSum += openBefore;
                openAfterSum += openAfter;
                collapsed++;
            }

            ApplyAveragedVertexOffsets(vertices, offsetAccum, offsetCount);
            mesh.vertices = vertices;
            LogNaturalRetractionGeometryBaseline(layerLabel, collapsed, openBeforeSum, openAfterSum);
            return collapsed;
        }

        private bool TryComputeCollapsedLipOffsets(Vector3 left, Vector3 right, Vector3 splitDir, float targetOpen, out Vector3 leftOffset, out Vector3 rightOffset, out float openBefore, out float openAfter)
        {
            leftOffset = Vector3.zero;
            rightOffset = Vector3.zero;
            openBefore = 0f;
            openAfter = 0f;
            if (splitDir.sqrMagnitude <= 1e-12f)
                return false;

            splitDir.Normalize();
            openBefore = Mathf.Abs(Vector3.Dot(right - left, splitDir));
            float baselineOpen = ResolveNaturalRetractionGeometryBaselineOpen(targetOpen);
            Vector3 center = 0.5f * (left + right);
            Vector3 desiredLeft = center - splitDir * (baselineOpen * 0.5f);
            Vector3 desiredRight = center + splitDir * (baselineOpen * 0.5f);
            leftOffset = desiredLeft - left;
            rightOffset = desiredRight - right;
            openAfter = baselineOpen;
            return leftOffset.sqrMagnitude > 1e-14f || rightOffset.sqrMagnitude > 1e-14f;
        }

        private float ResolveNaturalRetractionGeometryBaselineOpen(float sourceTargetOpen)
        {
            if (!ResolveEnableIncisionNaturalRetraction())
                return Mathf.Max(0f, sourceTargetOpen);

            // Keep the generated rest shape nearly closed. NaturalRetraction then creates the
            // small visible opening over time, instead of inheriting an artificially separated rest gap.
            return 0f;
        }

        private static void ApplyAveragedVertexOffsets(List<Vector3> vertices, Dictionary<int, Vector3> offsetAccum, Dictionary<int, int> offsetCount)
        {
            if (vertices == null || offsetAccum == null)
                return;

            foreach (var kv in offsetAccum)
            {
                int index = kv.Key;
                if (index < 0 || index >= vertices.Count)
                    continue;
                int count = offsetCount != null && offsetCount.TryGetValue(index, out int c) ? Mathf.Max(1, c) : 1;
                vertices[index] += kv.Value / count;
            }
        }

        private static void ApplyAveragedVertexOffsets(Vector3[] vertices, Dictionary<int, Vector3> offsetAccum, Dictionary<int, int> offsetCount)
        {
            if (vertices == null || offsetAccum == null)
                return;

            foreach (var kv in offsetAccum)
            {
                int index = kv.Key;
                if (index < 0 || index >= vertices.Length)
                    continue;
                int count = offsetCount != null && offsetCount.TryGetValue(index, out int c) ? Mathf.Max(1, c) : 1;
                vertices[index] += kv.Value / count;
            }
        }

        private void LogNaturalRetractionGeometryBaseline(string layerLabel, int collapsed, float openBeforeSum, float openAfterSum)
        {
            if (!ResolveEnableIncisionNaturalRetraction() || collapsed <= 0)
                return;

            float avgBefore = openBeforeSum / collapsed;
            float avgAfter = openAfterSum / collapsed;
            Debug.Log($"[Incision][NaturalRetractionGeometryBaseline] layer={layerLabel} collapsedPairs={collapsed} avgOpenBefore={avgBefore:F6} avgOpenAfter={avgAfter:F6} baselineOpen=0.000000 distance={ResolveIncisionNaturalRetractionDistance():F6} scale={ResolveIncisionNaturalRetractionScale():F3}");
        }

        private void Update()
        {
            // Time.deltaTime == 0 alone does not stop per-frame relaxation,
            // clamp targets, rebuilds, or puncture history bookkeeping.
            if (Application.isPlaying && Time.timeScale == 0f)
                return;

            if (_incisionFramesSinceApply < int.MaxValue)
                _incisionFramesSinceApply++;

            if (_pendingRebuild)
            {
                _pendingRebuild = false;
                InitSim();
                if (_incisionRebuildPending)
                {
                    _incisionRebuildPending = false;
                    if (!Application.isPlaying && enableProceduralIncisionAuthoring && proceduralIncisionLivePreview)
                        TryGenerateProceduralIncisionStroke(clearExisting: true, logWarnings: false);
                    else if (!Application.isPlaying)
                        ClearIncisionStroke();
                }

                RefreshIncisionDebugData();
                BeginIncisionRenderSampleDebugFrame();
                UpdateIncisionSidewallMesh();
                ClearIncisionMarkerMesh();
                UpdateMiddleWallFillMesh();
                return;
            }

            if (_simulator == null) return;

            // Follow attachments (optional)
            foreach (var pair in _attachments)
            {
                var t = pair.Value;
                if (t != null)
                {
                    _simulator.AddPinConstraint(pair.Key, t.position);
                }
            }

            UpdateCompliantClampPinTargets(Time.deltaTime);
            _simulator.SetFieldForce(float3.zero);

            _elapsedTime += Time.deltaTime;
            int cnt = Mathf.FloorToInt(_elapsedTime / dt);
            for (int i = 0; i < cnt; i++)
            {
                _simulator.Step(dt);
                if (i < cnt - 1)
                {
                    _simulator.ComputeAsyncJobs();
                }
            }

            if (cnt > 0)
            {
                _simulator.ComputeAsyncJobs();
                _elapsedTime -= cnt * dt;
            }

            RebuildSuturePassSequenceState();
            ApplyIncisionLipRelaxation(Time.deltaTime);

            RefreshIncisionDebugData();
            BeginIncisionRenderSampleDebugFrame();
            ClearIncisionMarkerMesh();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || Time.timeScale == 0f || _simulator == null)
                return;
            // Read the same final particle positions as ClothRendererSlice, after
            // all Update-time needle, clamp and lip deformation has completed.
            UpdateMiddleWallFillMesh();
            UpdateIncisionSidewallMesh();
        }

        // ---------------- Pin Ends ----------------

        private void ApplyPinEndsLongitudinalMaterial()
        {
            if (_simulator == null) return;

            _startClampPinIndices.Clear();
            _endClampPinIndices.Clear();
            _startClampOffset = Vector3.zero;
            _endClampOffset = Vector3.zero;
            _clampPinUpdateLogCounter = 0;

            var pos = _simulator.positions;
            float[] outerS;
            float[] innerS;
            string outerSummary = string.Empty;
            string innerSummary = "not-evaluated";
            if (!TryBuildLongitudinalMaterialCoordinates(
                    _outerMesh,
                    out outerS,
                    out outerSummary) ||
                !TryBuildLongitudinalMaterialCoordinates(
                    _innerMesh,
                    out innerS,
                    out innerSummary))
            {
                Debug.LogError(
                    "[DoubleLayerDriver] PinEnds material-coordinate contract failed. " +
                    $"outer={outerSummary} inner={innerSummary}");
                return;
            }

            int pinStartCount = 0;
            int pinEndCount = 0;
            bool compliantClamp = ResolveEnableCompliantClampPins();
            float clampStiffness = Mathf.Clamp01(ResolveClampPinStiffness());
            float clampMaxCorrection = Mathf.Max(0f, ResolveClampPinMaxCorrectionPerIteration());
            const float materialBoundaryEpsilon = 1e-6f;

            for (int i = 0; i < outerS.Length + innerS.Length; i++)
            {
                float s = i < outerS.Length
                    ? outerS[i]
                    : innerS[i - outerS.Length];
                if (s <=
                    pinEndRegionRatio +
                    materialBoundaryEpsilon)
                {
                    if (compliantClamp)
                    {
                        _simulator.AddSoftClampPinConstraint(i, pos[i], StartClampGroupId, clampStiffness, clampMaxCorrection);
                        _startClampPinIndices.Add(i);
                    }
                    else
                    {
                        _simulator.AddPinConstraint(i, pos[i]);
                    }
                    pinStartCount++;
                }
                else if (
                    s >=
                    1f -
                    pinEndRegionRatio -
                    materialBoundaryEpsilon)
                {
                    if (compliantClamp)
                    {
                        _simulator.AddSoftClampPinConstraint(i, pos[i], EndClampGroupId, clampStiffness, clampMaxCorrection);
                        _endClampPinIndices.Add(i);
                    }
                    else
                    {
                        _simulator.AddPinConstraint(i, pos[i]);
                    }
                    pinEndCount++;
                }
            }

            _startClampInitialCentroid = ComputeClampCentroid(_startClampPinIndices);
            _endClampInitialCentroid = ComputeClampCentroid(_endClampPinIndices);
            Debug.Log(
                $"[DoubleLayerDriver] PinEnds(materialUV): ratio={pinEndRegionRatio}, " +
                $"startPins={pinStartCount}, endPins={pinEndCount}, " +
                $"compliantClamp={(compliantClamp ? 1 : 0)}, " +
                $"maxLet={ResolveClampPinMaxLetDistance():F5}, stiffness={clampStiffness:F2} " +
                $"outer={outerSummary} inner={innerSummary}");
        }

        private static bool TryBuildLongitudinalMaterialCoordinates(
            Mesh mesh,
            out float[] station01,
            out string summary)
        {
            station01 = Array.Empty<float>();
            summary = string.Empty;
            if (mesh == null)
            {
                summary = "mesh=null";
                return false;
            }

            Vector2[] uv = mesh.uv;
            if (uv == null || uv.Length != mesh.vertexCount)
            {
                summary =
                    $"uv-count={uv?.Length ?? 0}/{mesh.vertexCount}";
                return false;
            }

            float minV = float.PositiveInfinity;
            float maxV = float.NegativeInfinity;
            for (int i = 0; i < uv.Length; i++)
            {
                minV = Mathf.Min(minV, uv[i].y);
                maxV = Mathf.Max(maxV, uv[i].y);
            }
            float span = maxV - minV;
            if (span <= 1e-6f)
            {
                summary = $"uv-v-span={span:E3}";
                return false;
            }

            int[] triangles = mesh.triangles;
            float maxTriangleVSpan = 0f;
            int invalidTriangle = 0;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i + 0];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 ||
                    a >= uv.Length || b >= uv.Length || c >= uv.Length)
                {
                    invalidTriangle++;
                    continue;
                }
                float triMin = Mathf.Min(
                    uv[a].y,
                    Mathf.Min(uv[b].y, uv[c].y));
                float triMax = Mathf.Max(
                    uv[a].y,
                    Mathf.Max(uv[b].y, uv[c].y));
                maxTriangleVSpan =
                    Mathf.Max(
                        maxTriangleVSpan,
                        (triMax - triMin) / span);
            }
            if (invalidTriangle > 0 || maxTriangleVSpan > 0.125f)
            {
                summary =
                    $"uv-v-topology invalidTriangles={invalidTriangle} " +
                    $"maxTriangleSpan01={maxTriangleVSpan:F5}";
                return false;
            }

            station01 = new float[uv.Length];
            for (int i = 0; i < uv.Length; i++)
                station01[i] = Mathf.Clamp01((uv[i].y - minV) / span);
            summary =
                $"verts={uv.Length} v=({minV:F4},{maxV:F4}) " +
                $"maxTriangleSpan01={maxTriangleVSpan:F5}";
            return true;
        }

        private void UpdateCompliantClampPinTargets(float deltaTime)
        {
            if (_simulator == null || !ResolveEnableCompliantClampPins())
                return;
            if ((_startClampPinIndices.Count == 0 && _endClampPinIndices.Count == 0) || deltaTime <= 0f)
                return;

            float startOffsetLen = UpdateCompliantClampGroup(StartClampGroupId, _startClampPinIndices, _startClampInitialCentroid, ref _startClampOffset, deltaTime);
            float endOffsetLen = UpdateCompliantClampGroup(EndClampGroupId, _endClampPinIndices, _endClampInitialCentroid, ref _endClampOffset, deltaTime);

            _clampPinUpdateLogCounter++;
            if (_clampPinUpdateLogCounter >= 30 && (startOffsetLen > 1e-5f || endOffsetLen > 1e-5f))
            {
                _clampPinUpdateLogCounter = 0;
                Debug.Log($"[ClampPinGroupUpdate] startCount={_startClampPinIndices.Count} endCount={_endClampPinIndices.Count} startOffset={startOffsetLen:F5} endOffset={endOffsetLen:F5} maxLet={ResolveClampPinMaxLetDistance():F5} stiffness={ResolveClampPinStiffness():F2} damping={ResolveClampPinFollowDamping():F2}");
            }
        }

        private float UpdateCompliantClampGroup(int groupId, List<int> indices, Vector3 initialCentroid, ref Vector3 groupOffset, float deltaTime)
        {
            if (indices == null || indices.Count == 0)
                return 0f;

            Vector3 currentCentroid = ComputeClampCentroid(indices);
            Vector3 desiredOffset = currentCentroid - initialCentroid;
            float maxLet = Mathf.Max(0f, ResolveClampPinMaxLetDistance());
            float desiredLen = desiredOffset.magnitude;
            if (maxLet > 0f && desiredLen > maxLet)
                desiredOffset = desiredOffset / Mathf.Max(1e-8f, desiredLen) * maxLet;

            float follow = 1f - Mathf.Exp(-Mathf.Max(0f, ResolveClampPinFollowDamping()) * Mathf.Max(0f, deltaTime));
            groupOffset = Vector3.Lerp(groupOffset, desiredOffset, Mathf.Clamp01(follow));
            _simulator.UpdateSoftClampGroupOffset(groupId, new float3(groupOffset.x, groupOffset.y, groupOffset.z));
            return groupOffset.magnitude;
        }

        private Vector3 ComputeClampCentroid(List<int> indices)
        {
            if (_simulator == null || indices == null || indices.Count == 0)
                return Vector3.zero;

            var positions = _simulator.positions;
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= positions.Length)
                    continue;

                float3 p = positions[index];
                sum += new Vector3(p.x, p.y, p.z);
                count++;
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        // ---------------- Thickness Links (s,theta mapping) ----------------

        private struct VertexPolar
        {
            public int globalIndex;
            public float z;
            public float theta;
        }

        private class Ring
        {
            public float z;              // representative z
            public List<int> indices;    // global indices
            public List<float> thetas;   // sorted, same length as indices
        }

        private struct ThetaIndexPair
        {
            public float theta;
            public int index;
        }

        private void BuildThicknessLinksByTopologyIdentity()
        {
            if (_simulator == null)
                return;

            EnsureMiddleWallMappingCache();
            if (setting == null || !setting.enableXPBDThicknessLinks)
            {
                _simulator.ClearCrossLayerLinks();
                return;
            }
            if (!ValidateSynchronizedLayerTopologyIdentity(
                    out string identitySummary))
            {
                _simulator.ClearCrossLayerLinks();
                Debug.LogError(
                    "[DoubleLayerDriver] ThicknessLinks topology identity contract failed. " +
                    identitySummary);
                return;
            }

            var links = new List<CrossLayerLinkInfo>(OuterCount * 4);
            var added = new HashSet<ulong>();
            int outerToInner =
                BuildTopologyIdentityLinksOneWay(
                    OuterStart,
                    OuterCount,
                    InnerStart,
                    _innerMesh,
                    added,
                    links);
            int innerToOuter = 0;
            if (buildSymmetricThicknessLinks)
            {
                innerToOuter =
                    BuildTopologyIdentityLinksOneWay(
                        InnerStart,
                        InnerCount,
                        OuterStart,
                        _outerMesh,
                        added,
                        links);
            }

            var nativeLinks = new NativeArray<CrossLayerLinkInfo>(
                links.Count,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < links.Count; i++)
                nativeLinks[i] = links[i];
            _simulator.SetCrossLayerLinks(nativeLinks);
            nativeLinks.Dispose();

            Debug.Log(
                $"[DoubleLayerDriver] ThicknessLinks(topologyIdentity): " +
                $"O->I={outerToInner} I->O={innerToOuter} " +
                $"uniqueEdges={added.Count} {identitySummary}");
        }

        private int BuildTopologyIdentityLinksOneWay(
            int fromStart,
            int fromCount,
            int toStart,
            Mesh targetMesh,
            HashSet<ulong> added,
            List<CrossLayerLinkInfo> links)
        {
            if (targetMesh == null ||
                fromCount != targetMesh.vertexCount)
                return 0;

            List<int>[] adjacency =
                BuildMeshVertexAdjacency(targetMesh);
            NativeArray<float3> positions = _simulator.positions;
            int targetLinksPerVertex = use4Link ? 4 : 2;
            int addedCount = 0;
            var candidates = new List<int>(16);
            for (int local = 0; local < fromCount; local++)
            {
                int fromGlobal = fromStart + local;
                candidates.Clear();
                candidates.Add(local);
                if (adjacency != null &&
                    local < adjacency.Length &&
                    adjacency[local] != null)
                {
                    candidates.AddRange(adjacency[local]);
                }
                candidates.Sort(
                    (a, b) =>
                    {
                        float da = math.distancesq(
                            positions[fromGlobal],
                            positions[toStart + a]);
                        float db = math.distancesq(
                            positions[fromGlobal],
                            positions[toStart + b]);
                        return da.CompareTo(db);
                    });

                int accepted = 0;
                for (int candidate = 0;
                     candidate < candidates.Count &&
                     accepted < targetLinksPerVertex;
                     candidate++)
                {
                    int toLocal = candidates[candidate];
                    int before = addedCount;
                    addedCount += TryAddThicknessEdge(
                        fromGlobal,
                        toStart + toLocal,
                        positions,
                        added,
                        links);
                    if (addedCount > before)
                        accepted++;
                }
            }
            return addedCount;
        }

        private static List<int>[] BuildMeshVertexAdjacency(Mesh mesh)
        {
            if (mesh == null)
                return null;

            var sets = new HashSet<int>[mesh.vertexCount];
            for (int i = 0; i < sets.Length; i++)
                sets[i] = new HashSet<int>();
            int[] triangles = mesh.triangles;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i + 0];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                AddMeshAdjacency(sets, a, b);
                AddMeshAdjacency(sets, b, c);
                AddMeshAdjacency(sets, c, a);
            }

            var result = new List<int>[sets.Length];
            for (int i = 0; i < sets.Length; i++)
                result[i] = new List<int>(sets[i]);
            return result;
        }

        private static void AddMeshAdjacency(
            HashSet<int>[] adjacency,
            int a,
            int b)
        {
            if (adjacency == null ||
                a < 0 ||
                b < 0 ||
                a >= adjacency.Length ||
                b >= adjacency.Length ||
                a == b)
                return;
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        private bool ValidateSynchronizedLayerTopologyIdentity(
            out string summary)
        {
            summary = string.Empty;
            if (_outerMesh == null || _innerMesh == null)
            {
                summary = "mesh=null";
                return false;
            }
            if (_outerMesh.vertexCount != _innerMesh.vertexCount)
            {
                summary =
                    $"vertexCount={_outerMesh.vertexCount}/{_innerMesh.vertexCount}";
                return false;
            }

            Vector2[] outerUv = _outerMesh.uv;
            Vector2[] innerUv = _innerMesh.uv;
            if (outerUv == null ||
                innerUv == null ||
                outerUv.Length != _outerMesh.vertexCount ||
                innerUv.Length != _innerMesh.vertexCount)
            {
                summary =
                    $"uvCount={outerUv?.Length ?? 0}/{innerUv?.Length ?? 0}";
                return false;
            }

            float maxUvDelta = 0f;
            for (int i = 0; i < outerUv.Length; i++)
                maxUvDelta = Mathf.Max(
                    maxUvDelta,
                    Vector2.Distance(outerUv[i], innerUv[i]));
            int[] outerTriangles = _outerMesh.triangles;
            int[] innerTriangles = _innerMesh.triangles;
            int triangleIndexMismatch = 0;
            if (outerTriangles.Length != innerTriangles.Length)
            {
                triangleIndexMismatch =
                    Mathf.Abs(
                        outerTriangles.Length -
                        innerTriangles.Length) + 1;
            }
            else
            {
                for (int i = 0; i < outerTriangles.Length; i++)
                {
                    if (outerTriangles[i] != innerTriangles[i])
                        triangleIndexMismatch++;
                }
            }

            bool valid =
                maxUvDelta <= 1e-4f &&
                triangleIndexMismatch == 0;
            summary =
                $"vertices={outerUv.Length} maxUvDelta={maxUvDelta:E3} " +
                $"triangleIndexMismatch={triangleIndexMismatch}";
            return valid;
        }

        private void BuildLayerSeparationConstraintsByTopologyIdentity()
        {
            if (_simulator == null)
                return;

            _simulator.ClearLayerSeparation();
            if (!ValidateSynchronizedLayerTopologyIdentity(
                    out string identitySummary))
            {
                Debug.LogError(
                    "[DoubleLayerDriver] LayerSeparation topology identity contract failed. " +
                    identitySummary);
                return;
            }

            NativeArray<float3> positions = _simulator.positions;
            int added = 0;
            float minDistance = float.PositiveInfinity;
            float maxDistance = 0f;
            for (int local = 0; local < OuterCount; local++)
            {
                int outerGlobal = OuterStart + local;
                int innerGlobal = InnerStart + local;
                float3 direction =
                    positions[outerGlobal] -
                    positions[innerGlobal];
                float distance = math.length(direction);
                if (distance <= 1e-8f)
                {
                    Debug.LogError(
                        $"[DoubleLayerDriver] LayerSeparation coincident topology identity at local={local}.");
                    continue;
                }
                direction /= distance;

                float minSeparation = layerSeparationMinSep;
                float relaxRadius =
                    ResolveIncisionThicknessRelaxRadius();
                float separationScale =
                    Mathf.Clamp01(
                        ResolveIncisionSeparationRelaxScale());
                if (relaxRadius > 1e-6f &&
                    separationScale < 0.999f)
                {
                    float suppressRadius = Mathf.Max(
                        relaxRadius,
                        ResolveIncisionConstraintSuppressRadius());
                    bool nearOuter =
                        IsNearIncisionConstraintRibbon(
                            outerGlobal,
                            new Vector3(
                                positions[outerGlobal].x,
                                positions[outerGlobal].y,
                                positions[outerGlobal].z),
                            suppressRadius);
                    bool nearInner =
                        IsNearIncisionConstraintRibbon(
                            innerGlobal,
                            new Vector3(
                                positions[innerGlobal].x,
                                positions[innerGlobal].y,
                                positions[innerGlobal].z),
                            suppressRadius);
                    if (nearOuter || nearInner)
                        minSeparation *= separationScale;
                }

                _simulator.SetLayerSeparation(
                    outerGlobal,
                    innerGlobal,
                    direction,
                    minSeparation);
                OverrideMiddleWallMappingFromLayerSeparation(
                    outerGlobal,
                    innerGlobal,
                    distance);
                minDistance = Mathf.Min(minDistance, distance);
                maxDistance = Mathf.Max(maxDistance, distance);
                added++;
            }

            Debug.Log(
                $"[DoubleLayerDriver] LayerSeparation(topologyIdentity): " +
                $"mappedOuterVerts={added} minSep={layerSeparationMinSep} " +
                $"wallDistance=({minDistance:F6},{maxDistance:F6}) " +
                identitySummary);
        }

        [Obsolete(
            "Legacy local-Z/theta layer mapping is not valid for curved vessels. " +
            "Use BuildThicknessLinksByTopologyIdentity.",
            true)]
        private void BuildThicknessLinksBySZTheta()
        {
            if (_simulator == null) return;

            EnsureMiddleWallMappingCache();

            // XPBD thickness links switch
            if (setting == null || !setting.enableXPBDThicknessLinks)
            {
                _simulator.ClearCrossLayerLinks();
                return;
            }

            var links = new List<CrossLayerLinkInfo>(1024);


            // Build rings for each layer
            var innerRings = BuildRingsForLayer(VesselLayer.Inner);
            var outerRings = BuildRingsForLayer(VesselLayer.Outer);

            if (innerRings.Count == 0 || outerRings.Count == 0)
            {
                Debug.LogWarning("[DoubleLayerDriver] BuildThicknessLinks: rings empty.");
                return;
            }

            var added = new HashSet<ulong>();

            // Outer -> Inner
            int addCountOI = 0;
            addCountOI += BuildThicknessLinksOneWay(outerRings, innerRings, added, links);

            // Inner -> Outer (optional)
            int addCountIO = 0;
            if (buildSymmetricThicknessLinks)
            {
                addCountIO += BuildThicknessLinksOneWay(innerRings, outerRings, added, links);
            }

            var nativeLinks = new NativeArray<CrossLayerLinkInfo>(links.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < links.Count; i++) nativeLinks[i] = links[i];
            _simulator.SetCrossLayerLinks(nativeLinks);
            nativeLinks.Dispose();

            Debug.Log($"[DoubleLayerDriver] ThicknessLinks(XPBD): O->I {addCountOI}, I->O {addCountIO}, uniqueEdges={added.Count}");
        }


        // ---------------- Layer Separation (Hard Inequality) ----------------

        [Obsolete(
            "Legacy local-Z/theta layer mapping is not valid for curved vessels. " +
            "Use BuildLayerSeparationConstraintsByTopologyIdentity.",
            true)]
        private void BuildLayerSeparationConstraintsBySZTheta()
        {
            if (_simulator == null) return;


            _simulator.ClearLayerSeparation();
            var innerRings = BuildRingsForLayer(VesselLayer.Inner);
            var outerRings = BuildRingsForLayer(VesselLayer.Outer);
            if (innerRings.Count == 0 || outerRings.Count == 0)
            {
                Debug.LogWarning("[DoubleLayerDriver] LayerSeparation: rings empty.");
                return;
            }

            // Precompute inner ring z array for binary search
            float[] innerZ = new float[innerRings.Count];
            for (int i = 0; i < innerRings.Count; i++) innerZ[i] = innerRings[i].z;

            var pos = _simulator.positions;
            int added = 0;

            foreach (var oRing in outerRings)
            {
                int innerRingIdx = FindClosestRingIndex(innerZ, oRing.z);
                var iRing = innerRings[innerRingIdx];
                int n = iRing.indices.Count;
                if (n == 0) continue;

                for (int k = 0; k < oRing.indices.Count; k++)
                {
                    int oIdx = oRing.indices[k];
                    float theta = oRing.thetas[k];

                    // 在 inner ring 上找 theta 最近的点
                    int insert = iRing.thetas.BinarySearch(theta);
                    if (insert < 0) insert = ~insert;

                    int right = insert % n;
                    int left = (insert - 1 + n) % n;

                    int iA = iRing.indices[left];
                    int iB = iRing.indices[right];

                    // 选择角度更近的那个
                    float dA = AngularDistance(theta, iRing.thetas[left]);
                    float dB = AngularDistance(theta, iRing.thetas[right]);
                    int iIdx = (dB < dA) ? iB : iA;

                    float3 dir = pos[oIdx] - pos[iIdx];
                    float len = math.length(dir);
                    if (len < 1e-8f)
                    {
                        // 退化时用径向方向兜底（基于 outer 的 local XY）
                        var w2l = outerMeshFilter.transform.worldToLocalMatrix;
                        var lp = w2l.MultiplyPoint3x4(pos[oIdx]);
                        float3 radialLocal = new float3(lp.x, lp.y, 0);
                        float rl = math.length(radialLocal);
                        if (rl < 1e-8f) dir = new float3(1, 0, 0);
                        else
                        {
                            // 转回世界
                            var l2w = outerMeshFilter.transform.localToWorldMatrix;
                            float3 radialWorld = (float3)l2w.MultiplyVector((Vector3)(radialLocal / rl));
                            dir = radialWorld;
                        }
                    }
                    else
                    {
                        dir /= len;
                    }

                    float minSep = layerSeparationMinSep;
                    float relaxRadius = ResolveIncisionThicknessRelaxRadius();
                    float sepScale = Mathf.Clamp01(ResolveIncisionSeparationRelaxScale());
                    if (relaxRadius > 1e-6f && sepScale < 0.999f)
                    {
                        float suppressRadius = Mathf.Max(relaxRadius, ResolveIncisionConstraintSuppressRadius());
                        bool nearOuter = IsNearIncisionConstraintRibbon(oIdx, new Vector3(pos[oIdx].x, pos[oIdx].y, pos[oIdx].z), suppressRadius);
                        bool nearInner = IsNearIncisionConstraintRibbon(iIdx, new Vector3(pos[iIdx].x, pos[iIdx].y, pos[iIdx].z), suppressRadius);
                        if (nearOuter || nearInner)
                            minSep *= sepScale;
                    }

                    _simulator.SetLayerSeparation(oIdx, iIdx, dir, minSep);
                    OverrideMiddleWallMappingFromLayerSeparation(oIdx, iIdx, len);

                    added++;
                }
            }

            Debug.Log($"[DoubleLayerDriver] LayerSeparation(Hard): mappedOuterVerts={added}, minSep={layerSeparationMinSep}");
        }

        private static float AngularDistance(float a, float b)
        {
            float d = math.abs(a - b);
            if (d > math.PI) d = (float)(2.0 * math.PI) - d;
            return d;
        }

        private int BuildThicknessLinksOneWay(List<Ring> fromRings, List<Ring> toRings, HashSet<ulong> added, List<CrossLayerLinkInfo> links)
        {
            int addedCount = 0;
            var pos = _simulator.positions;

            // Precompute ring z array for binary search
            float[] toZ = new float[toRings.Count];
            for (int i = 0; i < toRings.Count; i++) toZ[i] = toRings[i].z;

            foreach (var ring in fromRings)
            {
                int fromRingCount = ring.indices.Count;
                for (int ii = 0; ii < fromRingCount; ii++)
                {
                    int fromIdx = ring.indices[ii];
                    float theta = ring.thetas[ii];
                    float z = ring.z;

                    // choose 1 or 2 target rings
                    int r0 = FindClosestRingIndex(toZ, z);
                    int r1 = r0;
                    if (use4Link && toRings.Count > 1)
                    {
                        int left = Mathf.Max(0, r0 - 1);
                        int right = Mathf.Min(toRings.Count - 1, r0 + 1);
                        if (left == r0) r1 = right;
                        else if (right == r0) r1 = left;
                        else
                        {
                            float dl = Mathf.Abs(toZ[left] - z);
                            float dr = Mathf.Abs(toZ[right] - z);
                            r1 = (dr < dl) ? right : left;
                        }
                    }

                    // 2 theta neighbors on ring r0
                    addedCount += AddThetaNeighbors(fromIdx, theta, toRings[r0], pos, added, links);

                    // 2 theta neighbors on ring r1 (for 4-link)
                    if (use4Link && r1 != r0)
                    {
                        addedCount += AddThetaNeighbors(fromIdx, theta, toRings[r1], pos, added, links);
                    }
                }
            }

            return addedCount;
        }

        private int AddThetaNeighbors(int fromIdx, float theta, Ring toRing, NativeArray<float3> pos, HashSet<ulong> added, List<CrossLayerLinkInfo> links)
        {
            int n = toRing.indices.Count;
            if (n == 0) return 0;

            // Binary search insertion point
            int insert = toRing.thetas.BinarySearch(theta);
            if (insert < 0) insert = ~insert;

            int right = insert % n;
            int left = (insert - 1 + n) % n;

            int toIdxA = toRing.indices[left];
            int toIdxB = toRing.indices[right];

            int addedCount = 0;
            addedCount += TryAddThicknessEdge(fromIdx, toIdxA, pos, added, links);
            if (toIdxB != toIdxA)
                addedCount += TryAddThicknessEdge(fromIdx, toIdxB, pos, added, links);

            return addedCount;
        }

        private int TryAddThicknessEdge(int a, int b, NativeArray<float3> pos, HashSet<ulong> added, List<CrossLayerLinkInfo> links)
        {
            if (a == b) return 0;
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            ulong key = ((ulong)(uint)min << 32) | (uint)max;
            if (added.Contains(key)) return 0;

            float relaxRadius = ResolveIncisionThicknessRelaxRadius();
            float keepScale = Mathf.Clamp01(ResolveIncisionThicknessRelaxScale());
            if (relaxRadius > 1e-6f && keepScale < 0.999f)
            {
                float suppressRadius = Mathf.Max(relaxRadius, ResolveIncisionConstraintSuppressRadius());
                bool nearA = IsNearIncisionConstraintRibbon(a, new Vector3(pos[a].x, pos[a].y, pos[a].z), suppressRadius);
                bool nearB = IsNearIncisionConstraintRibbon(b, new Vector3(pos[b].x, pos[b].y, pos[b].z), suppressRadius);
                if (nearA || nearB)
                {
                    if (keepScale <= 1e-4f)
                        return 0;

                    if (DeterministicUnit01(a, b) > keepScale)
                        return 0;
                }
            }

            float rest = math.distance(pos[a], pos[b]); // InitialDistance
            links.Add(new CrossLayerLinkInfo { a = a, b = b, restLength = rest });
            if (a >= OuterStart && a < OuterStart + OuterCount && b >= InnerStart && b < InnerStart + InnerCount)
                RegisterMiddleWallFallbackMapping(a, b, rest);
            else if (b >= OuterStart && b < OuterStart + OuterCount && a >= InnerStart && a < InnerStart + InnerCount)
                RegisterMiddleWallFallbackMapping(b, a, rest);
            added.Add(key);
            return 1;
        }

        private List<Ring> BuildRingsForLayer(VesselLayer layer)
        {
            int start, count;
            GetLayerSlice(layer, out start, out count);

            var pos = _simulator.positions;
            var w2l = outerMeshFilter.transform.worldToLocalMatrix;

            // Build list
            var list = new List<VertexPolar>(count);
            for (int i = 0; i < count; i++)
            {
                int gi = start + i;
                var lp = w2l.MultiplyPoint3x4(pos[gi]);
                float theta = Mathf.Atan2(lp.y, lp.x);
                if (theta < 0) theta += Mathf.PI * 2f;

                list.Add(new VertexPolar
                {
                    globalIndex = gi,
                    z = lp.z,
                    theta = theta,
                });
            }

            // Sort by z
            list.Sort((a, b) => a.z.CompareTo(b.z));

            // Cluster by z into rings
            float minZ = list[0].z;
            float maxZ = list[list.Count - 1].z;
            float length = Mathf.Max(1e-6f, maxZ - minZ);
            float eps = ringZGroupingEpsilon > 0 ? ringZGroupingEpsilon : (length * 1e-4f);

            var rings = new List<Ring>(64);
            Ring cur = null;
            float curZ = 0f;
            int curCount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (cur == null || Mathf.Abs(v.z - curZ) > eps)
                {
                    // finish previous ring (compute mean z)
                    if (cur != null && curCount > 0)
                    {
                        cur.z = curZ;
                        rings.Add(cur);
                    }

                    cur = new Ring
                    {
                        z = v.z,
                        indices = new List<int>(64),
                        thetas = new List<float>(64)
                    };
                    curZ = v.z;
                    curCount = 0;
                }

                // Update ring representative z as running mean (more stable)
                curCount++;
                curZ = curZ + (v.z - curZ) / curCount;
                cur.indices.Add(v.globalIndex);
                cur.thetas.Add(v.theta);
            }

            if (cur != null && cur.indices.Count > 0)
            {
                cur.z = curZ;
                rings.Add(cur);
            }

            // For each ring: sort by theta (and keep indices aligned)
            for (int r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                int n = ring.indices.Count;
                var pair = new List<ThetaIndexPair>(n);
                for (int i = 0; i < n; i++)
                {
                    pair.Add(new ThetaIndexPair { theta = ring.thetas[i], index = ring.indices[i] });
                }
                pair.Sort((a, b) => a.theta.CompareTo(b.theta));
                ring.thetas.Clear();
                ring.indices.Clear();
                for (int i = 0; i < n; i++)
                {
                    ring.thetas.Add(pair[i].theta);
                    ring.indices.Add(pair[i].index);
                }
            }

            // Sort rings by z
            rings.Sort((a, b) => a.z.CompareTo(b.z));
            return rings;
        }

        private static int FindClosestRingIndex(float[] sortedZ, float z)
        {
            int n = sortedZ.Length;
            if (n == 1) return 0;

            int lo = 0;
            int hi = n - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                float mz = sortedZ[mid];
                if (mz < z) lo = mid + 1;
                else if (mz > z) hi = mid - 1;
                else return mid;
            }

            // lo is insertion point
            int right = Mathf.Clamp(lo, 0, n - 1);
            int left = Mathf.Clamp(lo - 1, 0, n - 1);
            float dr = Mathf.Abs(sortedZ[right] - z);
            float dl = Mathf.Abs(sortedZ[left] - z);
            return (dr < dl) ? right : left;
        }


        private void ApplyIncisionLipRelaxation(float deltaTime)
        {
            if (_simulator == null || !Application.isPlaying)
                return;
            if (!ResolveEnableIncisionLipRelaxation())
                return;
            if ((_outerIncisionStations == null || _outerIncisionStations.Count == 0)
                && (_innerIncisionStations == null || _innerIncisionStations.Count == 0))
                return;

            bool naturalRetraction = ResolveEnableIncisionNaturalRetraction();
            float speed = naturalRetraction ? ResolveIncisionNaturalRetractionSpeed() : ResolveIncisionLipRelaxationSpeed();
            float gain = Mathf.Clamp01(ResolveIncisionLipRelaxation()) * (1f - Mathf.Exp(-Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime)));
            if (gain <= 1e-6f)
                return;

            float maxPush = naturalRetraction ? ResolveIncisionNaturalRetractionMaxPushPerFrame() : ResolveIncisionLipMaxPushPerFrame();
            int movedOuter = ApplyIncisionLipRelaxationForLayer(_outerIncisionStations, outerMeshFilter, OuterStart, _innerIncisionStations, innerMeshFilter, gain, maxPush, naturalRetraction, out float outerMaxPush, out float outerTargetSum);
            int movedInner = ApplyIncisionLipRelaxationForLayer(_innerIncisionStations, innerMeshFilter, InnerStart, _outerIncisionStations, outerMeshFilter, gain, maxPush, naturalRetraction, out float innerMaxPush, out float innerTargetSum);
            int moved = movedOuter + movedInner;

            if (naturalRetraction && !_incisionNaturalRetractionStartedLogged)
            {
                _incisionNaturalRetractionStartedLogged = true;
                Debug.Log($"[Incision][NaturalRetractionStart] outerStations={(_outerIncisionStations != null ? _outerIncisionStations.Count : 0)} innerStations={(_innerIncisionStations != null ? _innerIncisionStations.Count : 0)} distance={ResolveIncisionNaturalRetractionDistance():F5} scale={ResolveIncisionNaturalRetractionScale():F3} speed={speed:F2} maxPush={maxPush:F5}");
            }

            if (naturalRetraction)
            {
                _incisionNaturalRetractionUpdateLogCounter++;
                if (moved > 0 && (_incisionNaturalRetractionUpdateLogCounter >= 30 || _incisionNaturalRetractionUpdateLogCounter == 1))
                {
                    _incisionNaturalRetractionUpdateLogCounter = 0;
                    float avgTarget = moved > 0 ? (outerTargetSum + innerTargetSum) / moved : 0f;
                    Debug.Log($"[Incision][NaturalRetractionUpdate] moved={moved} outerMoved={movedOuter} innerMoved={movedInner} avgTargetOpen={avgTarget:F5} maxAppliedPush={Mathf.Max(outerMaxPush, innerMaxPush):F5} gain={gain:F3}");
                }
            }
        }

        private int ApplyIncisionLipRelaxationForLayer(
            List<PendingLipStation> stations,
            MeshFilter filter,
            int globalOffset,
            List<PendingLipStation> oppositeStations,
            MeshFilter oppositeFilter,
            float gain,
            float maxPush,
            bool naturalRetraction,
            out float maxAppliedPush,
            out float targetOpenSum)
        {
            maxAppliedPush = 0f;
            targetOpenSum = 0f;
            if (stations == null || stations.Count == 0 || filter == null || _simulator == null)
                return 0;

            Transform tr = filter.transform;
            int moved = 0;
            for (int i = 0; i < stations.Count; i++)
            {
                var station = stations[i];
                if (station.suppressRuntimeMotion)
                    continue;
                int leftIndex = globalOffset + station.leftLocalIndex;
                int rightIndex = globalOffset + station.rightLocalIndex;
                if (leftIndex < 0 || rightIndex < 0)
                    continue;

                Vector3 left = _simulator.GetWorldVertexPosition(leftIndex);
                Vector3 right = _simulator.GetWorldVertexPosition(rightIndex);
                Vector3 splitWorld = tr.TransformDirection(station.splitDirLocal);
                if (splitWorld.sqrMagnitude < 1e-12f)
                    splitWorld = (right - left).sqrMagnitude > 1e-12f ? (right - left).normalized : tr.right;
                else
                    splitWorld.Normalize();

                float currentOpen = Mathf.Abs(Vector3.Dot(right - left, splitWorld));
                float targetOpen = ResolveVisibleIncisionOpenTargetFromStation(station, filter, globalOffset, oppositeStations, oppositeFilter, naturalRetraction);
                float deltaOpen = targetOpen - currentOpen;
                if (Mathf.Abs(deltaOpen) <= 1e-6f)
                    continue;

                float push = deltaOpen * gain * 0.5f;
                if (maxPush > 0f)
                    push = Mathf.Clamp(push, -maxPush * 0.5f, maxPush * 0.5f);

                if (Mathf.Abs(push) <= 1e-8f)
                    continue;

                _simulator.ApplyWorldPositionDelta(leftIndex, -splitWorld * push);
                _simulator.ApplyWorldPositionDelta(rightIndex, splitWorld * push);
                moved++;
                maxAppliedPush = Mathf.Max(maxAppliedPush, Mathf.Abs(push));
                targetOpenSum += targetOpen;
            }

            return moved;
        }

        private float ResolveVisibleIncisionOpenTargetFromStation(PendingLipStation station, MeshFilter sameFilter, int sameGlobalOffset, List<PendingLipStation> oppositeStations, MeshFilter oppositeFilter, bool naturalRetraction)
        {
            float targetOpen = Mathf.Max(0f, station.targetOpen);
            if (naturalRetraction)
            {
                float distanceTarget = Mathf.Max(0f, ResolveIncisionNaturalRetractionDistance());
                float scaledTarget = targetOpen * Mathf.Clamp01(ResolveIncisionNaturalRetractionScale());
                return Mathf.Min(distanceTarget, scaledTarget);
            }

            if (!enableIncisionThicknessGap || oppositeStations == null || oppositeStations.Count == 0 || sameFilter == null || oppositeFilter == null || _simulator == null)
                return targetOpen;

            float localThickness = EstimateOppositeLayerThicknessFromStations(station, sameGlobalOffset, oppositeStations, oppositeFilter);
            if (localThickness <= 1e-6f)
                return targetOpen;

            float thicknessDrivenGap = localThickness * Mathf.Max(0f, incisionThicknessGapScale);
            thicknessDrivenGap *= Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(station.weight));
            return Mathf.Max(targetOpen, thicknessDrivenGap);
        }

        private float EstimateOppositeLayerThicknessFromStations(PendingLipStation station, int sameGlobalOffset, List<PendingLipStation> oppositeStations, MeshFilter oppositeFilter)
        {
            if (_simulator == null || oppositeStations == null || oppositeStations.Count == 0 || oppositeFilter == null)
                return 0f;

            int sameLeft = sameGlobalOffset + station.leftLocalIndex;
            int sameRight = sameGlobalOffset + station.rightLocalIndex;
            Vector3 sameCenter = 0.5f * (_simulator.GetWorldVertexPosition(sameLeft) + _simulator.GetWorldVertexPosition(sameRight));

            int oppositeGlobalOffset = sameGlobalOffset == OuterStart ? InnerStart : OuterStart;
            float bestScore = float.PositiveInfinity;
            float bestDistance = 0f;
            for (int i = 0; i < oppositeStations.Count; i++)
            {
                var other = oppositeStations[i];
                int otherLeft = oppositeGlobalOffset + other.leftLocalIndex;
                int otherRight = oppositeGlobalOffset + other.rightLocalIndex;
                Vector3 otherCenter = 0.5f * (_simulator.GetWorldVertexPosition(otherLeft) + _simulator.GetWorldVertexPosition(otherRight));
                float tScore = Mathf.Abs(other.strokeT - station.strokeT);
                float spatialScore = Vector3.SqrMagnitude(otherCenter - sameCenter);
                float score = tScore * 0.25f + spatialScore;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestDistance = Vector3.Distance(sameCenter, otherCenter);
                }
            }

            return bestDistance;
        }

        private float ResolveVisibleIncisionOpenTarget(IncisionLipPair pair, List<IncisionLipPair> samePairs, List<IncisionLipPair> oppositePairs, MeshFilter sameFilter, MeshFilter oppositeFilter, int sameIndex)
        {
            float targetOpen = Mathf.Max(0f, pair.targetOpen);
            if (!enableIncisionThicknessGap || oppositePairs == null || oppositePairs.Count == 0 || sameFilter == null || oppositeFilter == null)
                return targetOpen;

            float localThickness = EstimateOppositeLayerThickness(samePairs, oppositePairs, sameFilter, oppositeFilter, sameIndex);
            if (localThickness <= 1e-6f)
                return targetOpen;

            float thicknessDrivenGap = localThickness * Mathf.Max(0f, incisionThicknessGapScale);
            thicknessDrivenGap *= Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(pair.weight));
            return Mathf.Max(targetOpen, thicknessDrivenGap);
        }

        private float EstimateOppositeLayerThickness(List<IncisionLipPair> samePairs, List<IncisionLipPair> oppositePairs, MeshFilter sameFilter, MeshFilter oppositeFilter, int sameIndex)
        {
            if (samePairs == null || oppositePairs == null || samePairs.Count == 0 || oppositePairs.Count == 0 || sameFilter == null || oppositeFilter == null)
                return 0f;

            int safeIndex = Mathf.Clamp(sameIndex, 0, samePairs.Count - 1);
            float t = samePairs.Count <= 1 ? 0f : (float)safeIndex / Mathf.Max(1, samePairs.Count - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt(t * Mathf.Max(0, oppositePairs.Count - 1)), 0, Mathf.Max(0, oppositePairs.Count - 1));

            Vector3 sameCenter = sameFilter.transform.TransformPoint(samePairs[safeIndex].centerLocal);
            Vector3 oppCenter = oppositeFilter.transform.TransformPoint(oppositePairs[j].centerLocal);
            return Vector3.Distance(sameCenter, oppCenter);
        }

        private void ClearIncisionStaticDebugData()
        {
            _incisionDebugTriangles.Clear();
            _incisionInsertedVertices.Clear();
            _incisionPairDebugPoints.Clear();
        }

        private void BeginIncisionRenderSampleDebugFrame()
        {
            _incisionRenderSampleDebugPoints.Clear();
            _incisionTargetTraceDebugRecords.Clear();
            _incisionAllTargetTraceDebugRecords.Clear();
        }

        private void AppendIncisionRenderSampleDebug(List<IncisionRenderSample> samples, int layer, IncisionRenderSampleDebugKind kind)
        {
            if (samples == null)
                return;

            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                _incisionRenderSampleDebugPoints.Add(new IncisionRenderSampleDebugPoint
                {
                    center = s.center,
                    left = s.left,
                    right = s.right,
                    t = s.t,
                    layer = layer,
                    kind = (int)kind,
                });
            }
        }

        private void AppendIncisionDebugCapture(IncisionDebugCapture capture, Transform meshTransform)
        {
            if (capture == null || meshTransform == null)
                return;

            if (capture.triangles != null && capture.triangles.Count > 0)
                _incisionDebugTriangles.AddRange(capture.triangles);
            if (capture.insertedVertices != null && capture.insertedVertices.Count > 0)
                _incisionInsertedVertices.AddRange(capture.insertedVertices);
            if (capture.pairRecords != null && capture.pairRecords.Count > 0)
            {
                for (int i = 0; i < capture.pairRecords.Count; i++)
                {
                    var r = capture.pairRecords[i];
                    Vector3 left = r.centerLocal - r.splitDirLocal * (r.targetOpen * 0.5f);
                    Vector3 right = r.centerLocal + r.splitDirLocal * (r.targetOpen * 0.5f);
                    _incisionPairDebugPoints.Add(new IncisionPairDebugPoint
                    {
                        center = meshTransform.TransformPoint(r.centerLocal),
                        left = meshTransform.TransformPoint(left),
                        right = meshTransform.TransformPoint(right),
                        splitDir = meshTransform.TransformDirection(r.splitDirLocal).normalized,
                        targetOpen = r.targetOpen,
                        strokeT = r.strokeT,
                        layer = r.layer,
                        kind = (int)r.kind,
                        rejectReason = (int)r.rejectReason,
                    });
                }
            }
        }

        private static void CapturePatchTriangles(IncisionDebugCapture capture, PreciseIncisionMesh data, Transform meshTransform, HashSet<int> patchTriangles, IncisionPatchDebugKind kind, int layer)
        {
            if (capture == null || data == null || meshTransform == null || patchTriangles == null || patchTriangles.Count == 0)
                return;

            foreach (int tri in patchTriangles)
            {
                if (tri < 0 || tri >= data.TriangleCount)
                    continue;
                data.GetTriangle(tri, out int ia, out int ib, out int ic);
                if (ia < 0 || ib < 0 || ic < 0 || ia >= data.vertices.Count || ib >= data.vertices.Count || ic >= data.vertices.Count)
                    continue;

                Vector3 a = meshTransform.TransformPoint(data.vertices[ia]);
                Vector3 b = meshTransform.TransformPoint(data.vertices[ib]);
                Vector3 c = meshTransform.TransformPoint(data.vertices[ic]);
                capture.triangles.Add(new IncisionDebugTriangle
                {
                    a = a,
                    b = b,
                    c = c,
                    center = (a + b + c) / 3f,
                    layer = layer,
                    kind = (int)kind,
                    triIndex = tri,
                    ia = ia,
                    ib = ib,
                    ic = ic,
                });
            }
        }

        private static void CaptureNewVertices(IncisionDebugCapture capture, PreciseIncisionMesh data, Transform meshTransform, int oldVertexCount, IncisionInsertedVertexDebugKind kind, int layer)
        {
            if (capture == null || data == null || meshTransform == null)
                return;

            for (int i = Mathf.Max(0, oldVertexCount); i < data.vertices.Count; i++)
            {
                capture.insertedVertices.Add(new IncisionInsertedVertexDebugPoint
                {
                    position = meshTransform.TransformPoint(data.vertices[i]),
                    layer = layer,
                    kind = (int)kind,
                });
            }
        }

        private static void CaptureRemovedTriangle(IncisionDebugCapture capture, Transform meshTransform, Vector3 va, Vector3 vb, Vector3 vc, IncisionPatchDebugKind kind, int layer, int triIndex = -1, int ia = -1, int ib = -1, int ic = -1)
        {
            if (capture == null || meshTransform == null)
                return;

            Vector3 a = meshTransform.TransformPoint(va);
            Vector3 b = meshTransform.TransformPoint(vb);
            Vector3 c = meshTransform.TransformPoint(vc);
            capture.triangles.Add(new IncisionDebugTriangle
            {
                a = a,
                b = b,
                c = c,
                center = (a + b + c) / 3f,
                layer = layer,
                kind = (int)kind,
                triIndex = triIndex,
                ia = ia,
                ib = ib,
                ic = ic,
            });
        }

        private static void CapturePairDebugRecord(IncisionDebugCapture capture, IncisionLipPair pair, IncisionPairDebugKind kind, IncisionPairRejectReason reason)
        {
            if (capture == null)
                return;

            capture.pairRecords.Add(new IncisionPairDebugRecord
            {
                leftLocalIndex = pair.leftLocalIndex,
                rightLocalIndex = pair.rightLocalIndex,
                centerLocal = pair.centerLocal,
                splitDirLocal = pair.splitDirLocal,
                targetOpen = pair.targetOpen,
                strokeT = pair.strokeT,
                layer = pair.layer,
                kind = kind,
                rejectReason = reason,
            });
        }

        private void RefreshIncisionDebugData()
        {
            _incisionDebugPoints.Clear();
            _incisionDebugCenterline.Clear();
            _incisionDebugTriangles.Clear();
            _incisionDebugRibbonFrames.Clear();
            _incisionDeleteDebugSummaries.Clear();
            _incisionInsertedVertices.Clear();
            _incisionPairDebugPoints.Clear();
            _incisionRenderSampleDebugPoints.Clear();
            _incisionTargetTraceDebugRecords.Clear();
            _incisionAllTargetTraceDebugRecords.Clear();
            _incisionFinalTriangleAuditRecords.Clear();

            if (Application.isPlaying && _runtimeIncisionCommittedOnce)
            {
                if (_runtimeCommittedIncisionDebugRibbonFrames != null && _runtimeCommittedIncisionDebugRibbonFrames.Count > 0)
                    _incisionDebugRibbonFrames.AddRange(_runtimeCommittedIncisionDebugRibbonFrames);

                if (_runtimeCommittedIncisionDebugTriangles != null && _runtimeCommittedIncisionDebugTriangles.Count > 0)
                    _incisionDebugTriangles.AddRange(_runtimeCommittedIncisionDebugTriangles);

                if (_runtimeCommittedIncisionDeleteDebugSummaries != null && _runtimeCommittedIncisionDeleteDebugSummaries.Count > 0)
                    _incisionDeleteDebugSummaries.AddRange(_runtimeCommittedIncisionDeleteDebugSummaries);

                if (_runtimeCommittedIncisionTargetTraceDebugRecords != null && _runtimeCommittedIncisionTargetTraceDebugRecords.Count > 0)
                    _incisionTargetTraceDebugRecords.AddRange(_runtimeCommittedIncisionTargetTraceDebugRecords);

                if (_runtimeCommittedIncisionAllTargetTraceDebugRecords != null && _runtimeCommittedIncisionAllTargetTraceDebugRecords.Count > 0)
                    _incisionAllTargetTraceDebugRecords.AddRange(_runtimeCommittedIncisionAllTargetTraceDebugRecords);

                if (_runtimeCommittedIncisionFinalTriangleAuditRecords != null && _runtimeCommittedIncisionFinalTriangleAuditRecords.Count > 0)
                    _incisionFinalTriangleAuditRecords.AddRange(_runtimeCommittedIncisionFinalTriangleAuditRecords);
            }
        }

        private void AppendRawIncisionCenterline(List<Vector3> centerlineWorld)
        {
            if (centerlineWorld == null || centerlineWorld.Count == 0)
                return;

            Vector3 lastAccepted = Vector3.zero;
            bool hasLastAccepted = false;
            for (int i = 0; i < centerlineWorld.Count; i++)
            {
                Vector3 p = centerlineWorld[i];
                if (hasLastAccepted && (p - lastAccepted).sqrMagnitude < 1e-12f)
                    continue;

                _incisionDebugCenterline.Add(p);
                lastAccepted = p;
                hasLastAccepted = true;
            }
        }

        private void AppendIncisionDebugForLayer(List<IncisionLipPair> pairs, MeshFilter filter, int globalOffset)
        {
            if (pairs == null || pairs.Count == 0 || filter == null)
                return;

            var samples = BuildIncisionRenderSamples(pairs, filter, globalOffset);
            PruneIncisionRenderSamplesForSidewall(samples);
            StabilizeIncisionRenderSampleSides(samples);
            if (samples == null || samples.Count == 0)
                return;

            int stride = Mathf.Max(1, incisionDebugDrawStride);
            float debugSpacing = Mathf.Max(0f, incisionDebugMinSpacing > 1e-6f ? incisionDebugMinSpacing : incisionPairMinSpacing);
            float debugSpacingSq = debugSpacing * debugSpacing;
            Vector3 lastAcceptedCenter = Vector3.zero;
            bool hasLastAccepted = false;
            int layer = pairs[0].layer;

            for (int i = 0; i < samples.Count; i += stride)
            {
                var sample = samples[i];
                Vector3 splitWorld = sample.right - sample.left;
                if (splitWorld.sqrMagnitude < 1e-12f)
                    splitWorld = filter.transform.right;
                else
                    splitWorld.Normalize();

                Vector3 centerWorld = sample.center;
                if (hasLastAccepted && (centerWorld - lastAcceptedCenter).sqrMagnitude < debugSpacingSq)
                    continue;

                hasLastAccepted = true;
                lastAcceptedCenter = centerWorld;

                float currentOpen = Vector3.Distance(sample.left, sample.right);
                _incisionDebugPoints.Add(new IncisionDebugPoint
                {
                    center = centerWorld,
                    left = sample.left,
                    right = sample.right,
                    normal = splitWorld,
                    targetOpen = currentOpen,
                    currentOpen = currentOpen,
                    weight = 1f,
                    layer = layer
                });
            }
        }

        // ---------------- Incision helpers ----------------

        private enum SegmentEdgeIntersectionKind
        {
            None = 0,
            Proper = 1,
            VertexHit = 2,
            EdgeCoincident = 3,
        }

        private struct IncisionGraphEdge
        {
            public int to;
            public float baseCost;
        }


        private sealed class EdgeSnappedIncisionResult
        {
            public int layer;
            public PreciseIncisionMesh workMesh;
            public List<int> path;
            public List<IncisionLipPair> lipPairs;
            public List<PendingLipStation> stations;
            public List<Vector3> centerlineWorld;
            public List<Vector3> stationCentersWorld;
            public float pathLength;
            public float guideLength;
            public List<IncisionDebugTriangle> debugTriangles;
            public List<IncisionDebugRibbonFrame> debugRibbonFrames;
            public List<IncisionTargetTraceDebugRecord> targetTraceRecords;
            public List<IncisionTargetTraceDebugRecord> allTargetTraceRecords;
            public List<IncisionFinalTriangleAuditRecord> finalTriangleAuditRecords;
            public IncisionDeleteDebugSummary deleteDebugSummary;
        }

        private static bool PrepareEdgeSnappedIncisionResult(
            Mesh mesh,
            Transform meshTransform,
            List<Vector3> strokeWorld,
            float guideRadiusWorld,
            float openWidthWorld,
            float pathSampleSpacingWorld,
            float pathSnapPenalty,
            int maxAnchorCount,
            float profileVariation,
            float curvatureWidthScale,
            int layer,
            bool targetTraceEnabled,
            int targetTraceCount,
            float targetTraceNeighborRadius,
            bool targetTraceLogToConsole,
            int localSubdivisionPasses,
            out EdgeSnappedIncisionResult result,
            out IncisionDeleteDebugSummary stageSummary)
        {
            result = null;
            stageSummary = new IncisionDeleteDebugSummary
            {
                layer = layer,
                success = false,
                guideRadius = guideRadiusWorld,
                openWidth = openWidthWorld,
                sampleSpacing = pathSampleSpacingWorld,
                pathSnapPenalty = pathSnapPenalty,
                anchorCount = Mathf.Max(0, maxAnchorCount),
                localSubdivisionPasses = Mathf.Clamp(localSubdivisionPasses, 0, 2),
                guideLength = ComputePolylineLengthWorld(strokeWorld),
                strokePointCount = strokeWorld != null ? strokeWorld.Count : 0,
            };
            if (mesh == null || meshTransform == null || strokeWorld == null || strokeWorld.Count < 2)
            {
                stageSummary.failStage = "Input";
                stageSummary.failReason = "mesh / transform / stroke 无效或点数不足。";
                return false;
            }

            var slitDebugCapture = new IncisionDebugCapture(layer)
            {
                targetTraceEnabled = targetTraceEnabled,
                targetTraceCount = Mathf.Max(1, targetTraceCount),
                targetTraceNeighborRadius = Mathf.Max(0f, targetTraceNeighborRadius),
                targetTraceLogToConsole = targetTraceLogToConsole,
            };

            if (!PrepareIncisionGraphResult(
                    mesh,
                    meshTransform,
                    strokeWorld,
                    guideRadiusWorld,
                    openWidthWorld,
                    pathSampleSpacingWorld,
                    profileVariation,
                    curvatureWidthScale,
                    Mathf.Clamp(localSubdivisionPasses, 0, 2),
                    layer,
                    slitDebugCapture,
                    out var graph))
            {
                var diag = graph != null ? graph.diagnostics : null;
                if (diag != null)
                {
                    stageSummary.strokePointCount = diag.strokePointCount;
                    stageSummary.guideLength = diag.strokeTotalLength > 0f ? diag.strokeTotalLength : stageSummary.guideLength;
                    stageSummary.avgTriangleEdgeLength = diag.avgTriangleEdgeLength;
                    stageSummary.sampleSpacing = diag.sampleSpacing > 0f ? diag.sampleSpacing : stageSummary.sampleSpacing;
                    stageSummary.resampledSampleCount = diag.resampledSampleCount;
                    stageSummary.densifiedSampleCount = diag.densifiedSampleCount;
                    stageSummary.insertEpsilon = diag.insertEpsilon;
                    stageSummary.surfaceBandRadius = diag.surfaceBandRadius;
                    stageSummary.pathCount = diag.builtPathVertexCount;
                    stageSummary.failStage = diag.failStage;
                    stageSummary.failReason = diag.failReason;
                }
                return false;
            }

            var graphDiag = graph != null ? graph.diagnostics : null;
            if (graphDiag != null)
            {
                stageSummary.strokePointCount = graphDiag.strokePointCount;
                stageSummary.guideLength = graphDiag.strokeTotalLength > 0f ? graphDiag.strokeTotalLength : stageSummary.guideLength;
                stageSummary.avgTriangleEdgeLength = graphDiag.avgTriangleEdgeLength;
                stageSummary.sampleSpacing = graphDiag.sampleSpacing > 0f ? graphDiag.sampleSpacing : stageSummary.sampleSpacing;
                stageSummary.resampledSampleCount = graphDiag.resampledSampleCount;
                stageSummary.densifiedSampleCount = graphDiag.densifiedSampleCount;
                stageSummary.insertEpsilon = graphDiag.insertEpsilon;
                stageSummary.surfaceBandRadius = graphDiag.surfaceBandRadius;
                stageSummary.pathCount = graphDiag.builtPathVertexCount;
            }
            stageSummary.cutCandidateCount = graph != null && graph.cutCandidates != null ? graph.cutCandidates.Count : 0;
            stageSummary.preparedTriangleCount = graph != null && graph.preparedByTri != null ? graph.preparedByTri.Count : 0;
            stageSummary.selectedTriangleCount = graph != null && graph.selectedTriangles != null ? graph.selectedTriangles.Count : 0;
            stageSummary.pendingSegmentCount = graph != null && graph.pendingSegments != null ? graph.pendingSegments.Count : 0;
            stageSummary.dominantChainCount = graph != null && graph.dominantChain != null ? graph.dominantChain.Count : 0;

            if (!CommitPreparedIncisionGraph(
                    mesh,
                    meshTransform,
                    graph,
                    out var lipPairs,
                    out var stations,
                    out var centerlineWorld,
                    out var stationCentersWorld,
                    applyToMesh: false))
            {
                List<PendingLipStation> fallbackChain = null;
                HashSet<int> fallbackTriangles = null;
                bool retriedWithFallback = graph != null &&
                                           graph.pendingSegments != null &&
                                           BuildStrokeOrderedFallbackIncisionPath(
                                               graph.pendingSegments,
                                               Mathf.Max(graph.exciseRadius * 0.5f, 1e-5f),
                                               out fallbackChain,
                                               out fallbackTriangles);
                if (retriedWithFallback)
                {
                    graph.dominantChain = fallbackChain;
                    graph.selectedTriangles = fallbackTriangles;
                    if (CommitPreparedIncisionGraph(
                            mesh,
                            meshTransform,
                            graph,
                            out lipPairs,
                            out stations,
                            out centerlineWorld,
                            out stationCentersWorld,
                            applyToMesh: false))
                    {
                        stageSummary.selectedTriangleCount = graph.selectedTriangles != null ? graph.selectedTriangles.Count : stageSummary.selectedTriangleCount;
                        stageSummary.dominantChainCount = graph.dominantChain != null ? graph.dominantChain.Count : stageSummary.dominantChainCount;
                    }
                    else
                    {
                        stageSummary.failStage = string.IsNullOrEmpty(stageSummary.failStage) ? "CommitGraph" : stageSummary.failStage;
                        stageSummary.failReason = string.IsNullOrEmpty(stageSummary.failReason) ? "CommitPreparedIncisionGraph 未产出稳定结果，3D fallback 重试后仍失败。" : stageSummary.failReason;
                        return false;
                    }
                }
                else
                {
                    stageSummary.failStage = string.IsNullOrEmpty(stageSummary.failStage) ? "CommitGraph" : stageSummary.failStage;
                    stageSummary.failReason = string.IsNullOrEmpty(stageSummary.failReason) ? "CommitPreparedIncisionGraph 未产出稳定结果。" : stageSummary.failReason;
                    return false;
                }
            }

            var data = graph.workMesh;
            if (data == null || stations == null || stations.Count < 2)
            {
                stageSummary.failStage = string.IsNullOrEmpty(stageSummary.failStage) ? "Stations" : stageSummary.failStage;
                stageSummary.failReason = string.IsNullOrEmpty(stageSummary.failReason) ? "graph.workMesh 或 stations 无效。" : stageSummary.failReason;
                return false;
            }

            var path = BuildStationPathIndices(stations);
            if (path == null || path.Count < 2)
            {
                stageSummary.failStage = string.IsNullOrEmpty(stageSummary.failStage) ? "ChainPost" : stageSummary.failStage;
                stageSummary.failReason = string.IsNullOrEmpty(stageSummary.failReason) ? "station path 不足 2 个点。" : stageSummary.failReason;
                return false;
            }

            float stationStep = ComputeAverageStationSpacing(stations);
            float boundaryRegionPad = Mathf.Max(1e-5f, Mathf.Max(openWidthWorld * 0.10f, stationStep * 0.08f));
            var slitDebugFrames = new List<IncisionDebugRibbonFrame>(stations.Count);

            var deleteSummary = ExciseTrianglesInsideLeftRightBoundaryRegion(
                data,
                stations,
                boundaryRegionPad,
                slitDebugCapture,
                meshTransform,
                layer,
                slitDebugFrames,
                path.Count,
                stations.Count);

            RemoveDegenerateTriangles(
                data,
                Mathf.Max(1e-8f, stationStep * stationStep * 0.00025f),
                Mathf.Max(1e-5f, boundaryRegionPad * 0.35f),
                slitDebugCapture,
                meshTransform,
                layer);

            var finalTriangleAuditRecords = BuildFinalTriangleAuditRecords(data, stations, meshTransform, layer, boundaryRegionPad);

            stageSummary.pathCount = path != null ? path.Count : stageSummary.pathCount;
            stageSummary.lipPairCount = lipPairs != null ? lipPairs.Count : 0;
            stageSummary.denseStationsCount = stations != null ? stations.Count : 0;
            stageSummary.centerlineCount = centerlineWorld != null ? centerlineWorld.Count : 0;
            stageSummary.pathLength = centerlineWorld != null && centerlineWorld.Count >= 2
                ? ComputePolylineLengthWorld(centerlineWorld)
                : ComputeAverageStationSpacing(stations) * Mathf.Max(0, stations != null ? stations.Count - 1 : 0);
            stageSummary.framesCount = slitDebugFrames != null ? slitDebugFrames.Count : 0;
            stageSummary.success = true;
            FillRibbonStageMetrics(slitDebugFrames, finalTriangleAuditRecords, ref stageSummary);
            if (string.IsNullOrEmpty(stageSummary.failStage))
                stageSummary.failStage = "OK";
            if (string.IsNullOrEmpty(stageSummary.failReason))
                stageSummary.failReason = "success";

            result = new EdgeSnappedIncisionResult
            {
                layer = layer,
                workMesh = data,
                path = path,
                lipPairs = lipPairs != null ? new List<IncisionLipPair>(lipPairs) : new List<IncisionLipPair>(),
                stations = stations != null ? CloneStationChain(stations) : new List<PendingLipStation>(),
                centerlineWorld = centerlineWorld != null ? new List<Vector3>(centerlineWorld) : new List<Vector3>(),
                stationCentersWorld = stationCentersWorld != null ? new List<Vector3>(stationCentersWorld) : new List<Vector3>(),
                pathLength = centerlineWorld != null && centerlineWorld.Count >= 2
                    ? ComputePolylineLengthWorld(centerlineWorld)
                    : ComputeAverageStationSpacing(stations) * Mathf.Max(0, stations != null ? stations.Count - 1 : 0),
                guideLength = ComputePolylineLengthWorld(strokeWorld),
                debugTriangles = slitDebugCapture != null ? new List<IncisionDebugTriangle>(slitDebugCapture.triangles) : new List<IncisionDebugTriangle>(),
                debugRibbonFrames = slitDebugFrames != null ? new List<IncisionDebugRibbonFrame>(slitDebugFrames) : new List<IncisionDebugRibbonFrame>(),
                targetTraceRecords = slitDebugCapture != null ? new List<IncisionTargetTraceDebugRecord>(slitDebugCapture.targetTraceRecords) : new List<IncisionTargetTraceDebugRecord>(),
                allTargetTraceRecords = slitDebugCapture != null ? new List<IncisionTargetTraceDebugRecord>(slitDebugCapture.allTargetTraceRecords) : new List<IncisionTargetTraceDebugRecord>(),
                finalTriangleAuditRecords = finalTriangleAuditRecords != null ? new List<IncisionFinalTriangleAuditRecord>(finalTriangleAuditRecords) : new List<IncisionFinalTriangleAuditRecord>(),
                deleteDebugSummary = MergeDeleteSummaryWithStage(deleteSummary, stageSummary),
            };
            return true;
        }

        private static IncisionDeleteDebugSummary MergeDeleteSummaryWithStage(IncisionDeleteDebugSummary deleteSummary, IncisionDeleteDebugSummary stageSummary)
        {
            stageSummary.pathCount = Mathf.Max(stageSummary.pathCount, deleteSummary.pathCount);
            stageSummary.denseStationsCount = Mathf.Max(stageSummary.denseStationsCount, deleteSummary.denseStationsCount);
            stageSummary.framesCount = Mathf.Max(stageSummary.framesCount, deleteSummary.framesCount);
            stageSummary.overlapCandidateCount = deleteSummary.overlapCandidateCount;
            stageSummary.corridorCandidateCount = deleteSummary.corridorCandidateCount;
            stageSummary.middleBandCandidateCount = deleteSummary.middleBandCandidateCount;
            stageSummary.strictCandidateCount = deleteSummary.strictCandidateCount;
            stageSummary.bridgeCandidateCount = deleteSummary.bridgeCandidateCount;
            stageSummary.coreCandidateCount = deleteSummary.coreCandidateCount;
            stageSummary.stripInteriorCandidateCount = deleteSummary.stripInteriorCandidateCount;
            stageSummary.removedCount = deleteSummary.removedCount;
            stageSummary.keptCount = deleteSummary.keptCount;
            stageSummary.tinyAreaThreshold = deleteSummary.tinyAreaThreshold;
            stageSummary.ribbonPad = deleteSummary.ribbonPad;
            return stageSummary;
        }

        private static void FillRibbonStageMetrics(List<IncisionDebugRibbonFrame> frames, List<IncisionFinalTriangleAuditRecord> records, ref IncisionDeleteDebugSummary summary)
        {
            summary.sideFlipCount = 0;
            summary.invalidRibbonFrameCount = 0;
            summary.minGap3D = 0f;
            summary.maxGap3D = 0f;
            summary.avgGap3D = 0f;
            summary.minHalfWidth3D = 0f;
            summary.maxHalfWidth3D = 0f;
            summary.avgHalfWidth3D = 0f;
            if (frames != null && frames.Count > 0)
            {
                float gapSum = 0f;
                float hwSum = 0f;
                Vector3 prevDir = Vector3.zero;
                bool hasPrevDir = false;
                bool hasGap = false;
                for (int i = 0; i < frames.Count; i++)
                {
                    var f = frames[i];
                    Vector3 pair = f.right - f.left;
                    float gap = pair.magnitude;
                    float hw = Mathf.Max(0f, f.halfWidth);
                    if (gap <= 1e-8f || hw <= 1e-8f)
                        summary.invalidRibbonFrameCount++;

                    if (!hasGap)
                    {
                        summary.minGap3D = summary.maxGap3D = gap;
                        summary.minHalfWidth3D = summary.maxHalfWidth3D = hw;
                        hasGap = true;
                    }
                    else
                    {
                        summary.minGap3D = Mathf.Min(summary.minGap3D, gap);
                        summary.maxGap3D = Mathf.Max(summary.maxGap3D, gap);
                        summary.minHalfWidth3D = Mathf.Min(summary.minHalfWidth3D, hw);
                        summary.maxHalfWidth3D = Mathf.Max(summary.maxHalfWidth3D, hw);
                    }
                    gapSum += gap;
                    hwSum += hw;
                    Vector3 dir = pair.sqrMagnitude > 1e-12f ? pair.normalized : Vector3.zero;
                    if (hasPrevDir && dir.sqrMagnitude > 1e-12f && Vector3.Dot(prevDir, dir) < 0f)
                        summary.sideFlipCount++;
                    if (dir.sqrMagnitude > 1e-12f)
                    {
                        prevDir = dir;
                        hasPrevDir = true;
                    }
                }
                summary.avgGap3D = gapSum / Mathf.Max(1, frames.Count);
                summary.avgHalfWidth3D = hwSum / Mathf.Max(1, frames.Count);
            }

            summary.overlapRibbon3DCount = 0;
            summary.insideBand3DCount = 0;
            summary.insideCore3DCount = 0;
            summary.twoDThreeDDisagreementCount = 0;
            if (records != null)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    var rec = records[i];
                    if (rec.overlapRibbon3D) summary.overlapRibbon3DCount++;
                    if (rec.insideRibbonBand3D) summary.insideBand3DCount++;
                    if (rec.insideRibbonCore3D) summary.insideCore3DCount++;
                    if (rec.overlapCandidate != rec.overlapRibbon3D || rec.insideBoundaryRegion != rec.insideRibbonBand3D)
                        summary.twoDThreeDDisagreementCount++;
                }
            }
        }

        private static List<IncisionFinalTriangleAuditRecord> BuildFinalTriangleAuditRecords(
            PreciseIncisionMesh data,
            List<PendingLipStation> stations,
            Transform meshTransform,
            int layer,
            float boundaryPad)
        {
            var records = new List<IncisionFinalTriangleAuditRecord>();
            if (data == null || stations == null || stations.Count < 2 || data.triangles == null || data.triangles.Count < 3)
                return records;

            var frames = BuildSlitRibbonFramesFromStations(data, stations, Mathf.Max(boundaryPad, 1e-5f));
            if (frames == null || frames.Count < 2)
                return records;

            var corridorQuads = BuildStationBoundaryQuads2D(data, stations);
            var centralBandQuads = BuildStationCentralBandQuads2D(corridorQuads, 0.16f, 0.84f);
            var regionPolygon = BuildStationBoundaryRegionPolygon2D(data, stations);
            bool hasRegionPolygon = regionPolygon != null && regionPolygon.Count >= 3;
            float ribbonQueryPad = Mathf.Max(1e-5f, boundaryPad * 0.35f);

            for (int t = 0; t < data.triangles.Count; t += 3)
            {
                int a = data.triangles[t];
                int b = data.triangles[t + 1];
                int c = data.triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 ||
                    a >= data.vertices.Count || b >= data.vertices.Count || c >= data.vertices.Count ||
                    a >= data.projected2D.Count || b >= data.projected2D.Count || c >= data.projected2D.Count)
                    continue;

                Vector3 va = data.vertices[a];
                Vector3 vb = data.vertices[b];
                Vector3 vc = data.vertices[c];
                Vector2 pa = data.projected2D[a];
                Vector2 pb = data.projected2D[b];
                Vector2 pc = data.projected2D[c];
                Vector3 centerLocal = (va + vb + vc) / 3f;
                Vector3 centerWorld = meshTransform != null ? meshTransform.TransformPoint(centerLocal) : centerLocal;
                Vector2 center2D = (pa + pb + pc) / 3f;

                bool overlapRibbon3D = TriangleOverlapsSlitRibbon(va, vb, vc, frames, ribbonQueryPad);
                bool insideRibbonBand3D = overlapRibbon3D && TriangleInsideSlitRibbonBand(va, vb, vc, frames, ribbonQueryPad);
                bool insideRibbonCore3D = overlapRibbon3D && TriangleInsideSlitRibbonCore(va, vb, vc, frames, ribbonQueryPad);
                bool removedCandidate = insideRibbonBand3D;

                bool overlapCandidate2D = hasRegionPolygon && TriangleOverlapsBoundaryRegion2D(pa, pb, pc, regionPolygon);
                bool insideBoundaryRegion2D = overlapCandidate2D && TriangleInsideBoundaryRegionTopological2D(data, a, b, c, regionPolygon);

                EstimateTriangleStripCoverageHits2D(data, a, b, c, corridorQuads, centralBandQuads, out int corridorHits, out int centralHits);

                float signedLateral3D = 0f;
                float halfWidth3D = 0f;
                int nearestRibbonFrameIndex = -1;
                float ribbonGap3D = 0f;
                float ribbonSurfaceOffset3D = 0f;
                if (!TrySampleSlitRibbonDetailed(centerLocal, frames, ribbonQueryPad, out signedLateral3D, out halfWidth3D, out nearestRibbonFrameIndex, out _, out _, out _, out ribbonGap3D, out ribbonSurfaceOffset3D))
                {
                    signedLateral3D = 0f;
                    halfWidth3D = 0f;
                    nearestRibbonFrameIndex = -1;
                    ribbonGap3D = 0f;
                    ribbonSurfaceOffset3D = 0f;
                }
                float normalizedLateral3D = halfWidth3D > 1e-6f ? Mathf.Abs(signedLateral3D) / halfWidth3D : -1f;

                string failHint;
                if (!overlapRibbon3D)
                    failHint = "fail:no-3d-ribbon-overlap";
                else if (!insideRibbonBand3D)
                    failHint = "fail:outside-3d-ribbon-band";
                else if (!insideRibbonCore3D)
                    failHint = removedCandidate ? "pass:inside-3d-ribbon-band" : "warn:inside-3d-band-but-kept";
                else
                    failHint = removedCandidate ? "pass:inside-3d-ribbon-core" : "warn:inside-3d-core-but-kept";

                if (overlapCandidate2D != overlapRibbon3D || insideBoundaryRegion2D != insideRibbonBand3D)
                {
                    failHint += $" 2dCheck overlap={(overlapCandidate2D ? 1 : 0)} inside={(insideBoundaryRegion2D ? 1 : 0)}";
                }

                records.Add(new IncisionFinalTriangleAuditRecord
                {
                    center = centerWorld,
                    layer = layer,
                    triIndex = t / 3,
                    ia = a,
                    ib = b,
                    ic = c,
                    area2D = Mathf.Abs(Cross2D(pb - pa, pc - pa)) * 0.5f,
                    area3D = Vector3.Cross(vb - va, vc - va).magnitude * 0.5f,
                    centerlineDistance2D = Mathf.Sqrt(DistancePointStripCenterlineSq2D(center2D, corridorQuads)),
                    corridorHits = corridorHits,
                    centralHits = centralHits,
                    nearestRibbonFrameIndex = nearestRibbonFrameIndex,
                    signedLateral3D = signedLateral3D,
                    normalizedLateral3D = normalizedLateral3D,
                    halfWidth3D = halfWidth3D,
                    ribbonGap3D = ribbonGap3D,
                    ribbonSurfaceOffset3D = ribbonSurfaceOffset3D,
                    overlapCandidate = overlapRibbon3D,
                    overlapRibbon3D = overlapRibbon3D,
                    insideBoundaryRegion = insideRibbonBand3D,
                    insideRibbonBand3D = insideRibbonBand3D,
                    insideRibbonCore3D = insideRibbonCore3D,
                    removedCandidate = removedCandidate,
                    failHint = failHint,
                });
            }

            return records;
        }

        private static List<int> BuildStationPathIndices(List<PendingLipStation> stations)
        {
            var path = new List<int>(stations != null ? stations.Count : 0);
            if (stations == null)
                return path;

            for (int i = 0; i < stations.Count; i++)
            {
                int idx = stations[i].leftLocalIndex;
                if (idx < 0)
                    continue;
                if (path.Count > 0 && path[path.Count - 1] == idx)
                    continue;
                path.Add(idx);
            }
            return path;
        }

        private static float ComputeAverageStationSpacing(List<PendingLipStation> stations)
        {
            if (stations == null || stations.Count < 2)
                return 1e-4f;

            float total = 0f;
            int count = 0;
            for (int i = 1; i < stations.Count; i++)
            {
                total += Vector3.Distance(stations[i - 1].seedCenterLocal, stations[i].seedCenterLocal);
                count++;
            }
            return count > 0 ? Mathf.Max(1e-5f, total / count) : 1e-4f;
        }

        private static bool ValidateSynchronizedEdgeSnappedResults(EdgeSnappedIncisionResult outer, EdgeSnappedIncisionResult inner)
        {
            if (outer == null || inner == null || outer.path == null || inner.path == null)
                return false;
            if (outer.path.Count < 2 || inner.path.Count < 2)
                return false;
            if (outer.stations == null || inner.stations == null || outer.stations.Count < 2 || inner.stations.Count < 2)
                return false;

            float outerLen = Mathf.Max(1e-6f, outer.pathLength);
            float innerLen = Mathf.Max(1e-6f, inner.pathLength);
            float lenRatio = outerLen > innerLen ? outerLen / innerLen : innerLen / outerLen;
            if (lenRatio > 1.85f)
                return false;

            int outerCount = outer.stations.Count;
            int innerCount = inner.stations.Count;
            int maxCount = Mathf.Max(outerCount, innerCount);
            int minCount = Mathf.Max(1, Mathf.Min(outerCount, innerCount));
            if (maxCount > minCount * 3)
                return false;

            return true;
        }

        private static List<PendingLipStation> BuildStationChainFromLipPairs(List<IncisionLipPair> lipPairs)
        {
            var result = new List<PendingLipStation>(lipPairs != null ? lipPairs.Count : 0);
            if (lipPairs == null)
                return result;

            int count = lipPairs.Count;
            for (int i = 0; i < count; i++)
            {
                var pair = lipPairs[i];
                float strokeT = count > 1 ? (float)i / (count - 1) : 0f;
                AppendPendingLipStation(result,
                    MakeUndirectedEdgeKey(pair.leftLocalIndex, pair.rightLocalIndex),
                    pair.leftLocalIndex,
                    pair.rightLocalIndex,
                    pair.centerLocal,
                    pair.splitDirLocal,
                    pair.targetOpen,
                    pair.weight,
                    strokeT,
                    pair.layer);
            }

            return result;
        }

        private static List<int> BuildEdgeSnappedIncisionVertexPath(
            Vector2[] projectedVertices2D,
            List<Vector2> stroke2D,
            List<int>[] adjacency,
            float guideRadius,
            float pathSnapPenalty,
            float sampleSpacing,
            int maxAnchorCount)
        {
            if (projectedVertices2D == null || projectedVertices2D.Length == 0 || stroke2D == null || stroke2D.Count < 2 || adjacency == null)
                return null;

            List<Vector2> resampled = ResampleStroke2D(stroke2D, sampleSpacing);
            if (resampled == null || resampled.Count < 2)
                return null;

            float[] vertexStrokeDistances = new float[projectedVertices2D.Length];
            for (int i = 0; i < projectedVertices2D.Length; i++)
                vertexStrokeDistances[i] = DistancePointToStroke(projectedVertices2D[i], stroke2D);

            float maxSnapDistance = Mathf.Max(guideRadius * 2.0f, sampleSpacing * 2.0f);
            var anchors = new List<int>(resampled.Count);
            int prevAnchor = -1;
            for (int i = 0; i < resampled.Count; i++)
            {
                int anchor = FindNearestVertex(projectedVertices2D, resampled[i], maxSnapDistance);
                if (anchor < 0)
                    continue;

                if (anchors.Count == 0 || anchor != prevAnchor)
                {
                    anchors.Add(anchor);
                    prevAnchor = anchor;
                }
            }

            if (anchors.Count < 2)
                return null;

            anchors = DecimateAnchors(anchors, Mathf.Max(4, maxAnchorCount));
            if (anchors.Count < 2)
                return null;

            var path = new List<int>(anchors.Count * 8);
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                int start = anchors[i];
                int end = anchors[i + 1];
                if (start == end)
                    continue;

                List<int> segmentPath;
                if (!TryFindShortestPathDirectionAware(start, end, adjacency, projectedVertices2D, vertexStrokeDistances, stroke2D, pathSnapPenalty, out segmentPath))
                    return null;
                if (segmentPath == null || segmentPath.Count == 0)
                    return null;

                if (path.Count > 0 && path[path.Count - 1] == segmentPath[0])
                {
                    for (int k = 1; k < segmentPath.Count; k++)
                        path.Add(segmentPath[k]);
                }
                else
                {
                    path.AddRange(segmentPath);
                }
            }

            if (path.Count < 2)
                return null;

            return path;
        }

        private static bool TryFindShortestPathDirectionAware(
            int start,
            int end,
            List<int>[] adjacency,
            Vector2[] projectedVertices2D,
            float[] vertexStrokeDistances,
            List<Vector2> stroke2D,
            float snapPenalty,
            out List<int> path)
        {
            path = null;
            int n = adjacency.Length;
            if (start < 0 || start >= n || end < 0 || end >= n)
                return false;

            var dist = new float[n];
            var prev = new int[n];
            var visited = new bool[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = float.PositiveInfinity;
                prev[i] = -1;
            }

            dist[start] = 0f;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (visited[i])
                        continue;
                    if (dist[i] < best)
                    {
                        best = dist[i];
                        u = i;
                    }
                }

                if (u < 0)
                    break;
                if (u == end)
                    break;

                visited[u] = true;
                var neighbors = adjacency[u];
                if (neighbors == null)
                    continue;

                for (int i = 0; i < neighbors.Count; i++)
                {
                    int v = neighbors[i];
                    if (visited[v])
                        continue;

                    Vector2 uv = projectedVertices2D[v] - projectedVertices2D[u];
                    float edgeLen = uv.magnitude;
                    if (edgeLen <= 1e-8f)
                        continue;

                    Vector2 edgeDir = uv / edgeLen;
                    Vector2 mid = 0.5f * (projectedVertices2D[u] + projectedVertices2D[v]);
                    Vector2 guideDir = FindNearestStrokeTangent(mid, stroke2D);
                    float align = guideDir.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(edgeDir, guideDir.normalized)) : 1f;
                    float directionPenalty = edgeLen * (1f - align) * 2.75f;
                    float step = edgeLen;
                    step += snapPenalty * 0.5f * (vertexStrokeDistances[u] + vertexStrokeDistances[v]);
                    step += directionPenalty;

                    float candidate = dist[u] + step;
                    if (candidate < dist[v])
                    {
                        dist[v] = candidate;
                        prev[v] = u;
                    }
                }
            }

            if (start != end && prev[end] < 0)
                return false;

            path = new List<int>(32);
            int cur = end;
            path.Add(cur);
            while (cur != start)
            {
                cur = prev[cur];
                if (cur < 0)
                    return false;
                path.Add(cur);
            }

            path.Reverse();
            return true;
        }

        private static Vector2 FindNearestStrokeTangent(Vector2 p, List<Vector2> stroke2D)
        {
            if (stroke2D == null || stroke2D.Count < 2)
                return Vector2.zero;

            float bestSq = float.PositiveInfinity;
            Vector2 bestDir = Vector2.zero;
            for (int i = 0; i < stroke2D.Count - 1; i++)
            {
                Vector2 a = stroke2D[i];
                Vector2 b = stroke2D[i + 1];
                Vector2 ab = b - a;
                float denom = Vector2.Dot(ab, ab);
                if (denom <= 1e-12f)
                    continue;

                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
                Vector2 q = a + ab * t;
                float dsq = (p - q).sqrMagnitude;
                if (dsq < bestSq)
                {
                    bestSq = dsq;
                    bestDir = ab.normalized;
                }
            }

            if (bestDir.sqrMagnitude <= 1e-10f)
                bestDir = (stroke2D[stroke2D.Count - 1] - stroke2D[0]).normalized;
            return bestDir;
        }

        private static float ComputePathLength(List<int> path, List<Vector3> vertices)
        {
            if (path == null || vertices == null || path.Count < 2)
                return 0f;

            float length = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                int a = path[i - 1];
                int b = path[i];
                if (a < 0 || b < 0 || a >= vertices.Count || b >= vertices.Count)
                    continue;
                length += Vector3.Distance(vertices[a], vertices[b]);
            }
            return length;
        }

        private static float ComputePolylineLengthWorld(IReadOnlyList<Vector3> polyline)
        {
            if (polyline == null || polyline.Count < 2)
                return 0f;

            float length = 0f;
            for (int i = 1; i < polyline.Count; i++)
                length += Vector3.Distance(polyline[i - 1], polyline[i]);
            return length;
        }

        private static bool ApplyIncisionCrackToMesh(
            Mesh mesh,
            Transform meshTransform,
            List<Vector3> strokeWorld,
            float guideRadiusWorld,
            float openWidthWorld,
            float pathSampleSpacingWorld,
            float pathSnapPenalty,
            int maxAnchorCount,
            float profileVariation,
            float curvatureWidthScale,
            int localSubdivisionPasses,
            int layer,
            out List<IncisionLipPair> lipPairs,
            out List<Vector3> centerlineWorld,
            IncisionDebugCapture debugCapture = null)
        {
            lipPairs = new List<IncisionLipPair>();
            centerlineWorld = new List<Vector3>();
            if (!PrepareIncisionGraphResult(mesh, meshTransform, strokeWorld, guideRadiusWorld, openWidthWorld, pathSampleSpacingWorld, profileVariation, curvatureWidthScale, localSubdivisionPasses, layer, debugCapture, out var graph))
                return false;
            return CommitPreparedIncisionGraph(mesh, meshTransform, graph, out lipPairs, out _, out centerlineWorld, out _);
        }

        private struct TriangleSplitCandidate
        {
            public int triIndex;
            public int i0;
            public int i1;
            public int i2;
            public int segmentIndex;
            public float globalT;
            public int lonelyVertex;
            public int otherA;
            public int otherB;
            public EdgeCrossing crossingA;
            public EdgeCrossing crossingB;
            public bool lonelyPositive;
            public Vector3 segmentStartLocal;
            public Vector3 segmentEndLocal;
            public Vector3 centerLocal;
        }

        private static HashSet<int> BuildIncisionLocalPatch(
            PreciseIncisionMesh data,
            List<StrokeSample> samples,
            float epsilon,
            float surfaceBandRadius,
            int neighborRings)
        {
            var crossed = new HashSet<int>();
            if (data == null || samples == null || samples.Count < 2)
                return crossed;

            float bandSq = surfaceBandRadius > 1e-6f ? surfaceBandRadius * surfaceBandRadius : float.PositiveInfinity;
            int triCount = data.TriangleCount;
            for (int seg = 0; seg < samples.Count - 1; seg++)
            {
                StrokeSample start = samples[seg];
                StrokeSample end = samples[seg + 1];
                Vector2 segDir2 = end.p2d - start.p2d;
                if (segDir2.sqrMagnitude <= 1e-12f)
                    continue;

                for (int tri = 0; tri < triCount; tri++)
                {
                    data.GetTriangle(tri, out int a, out int b, out int c);
                    if (TryGetTriangleGuideChord(data, start, end, a, b, c, epsilon, bandSq, out _, out _))
                        crossed.Add(tri);
                }
            }

            if (crossed.Count == 0 || neighborRings <= 0)
                return crossed;

            var vertexToTriangles = new Dictionary<int, List<int>>();
            for (int tri = 0; tri < triCount; tri++)
            {
                data.GetTriangle(tri, out int a, out int b, out int c);
                if (!vertexToTriangles.TryGetValue(a, out var la)) vertexToTriangles[a] = la = new List<int>(4);
                if (!vertexToTriangles.TryGetValue(b, out var lb)) vertexToTriangles[b] = lb = new List<int>(4);
                if (!vertexToTriangles.TryGetValue(c, out var lc)) vertexToTriangles[c] = lc = new List<int>(4);
                la.Add(tri);
                lb.Add(tri);
                lc.Add(tri);
            }

            var patch = new HashSet<int>(crossed);
            var frontier = new HashSet<int>(crossed);
            for (int ring = 0; ring < neighborRings; ring++)
            {
                var next = new HashSet<int>();
                foreach (int tri in frontier)
                {
                    data.GetTriangle(tri, out int a, out int b, out int c);
                    ExpandPatchViaVertex(vertexToTriangles, patch, next, a);
                    ExpandPatchViaVertex(vertexToTriangles, patch, next, b);
                    ExpandPatchViaVertex(vertexToTriangles, patch, next, c);
                }
                frontier = next;
                if (frontier.Count == 0)
                    break;
            }

            return patch;
        }

        private static void ExpandPatchViaVertex(Dictionary<int, List<int>> vertexToTriangles, HashSet<int> patch, HashSet<int> next, int vertex)
        {
            if (!vertexToTriangles.TryGetValue(vertex, out var tris) || tris == null)
                return;

            for (int i = 0; i < tris.Count; i++)
            {
                int tri = tris[i];
                if (patch.Add(tri))
                    next.Add(tri);
            }
        }

        private static List<TriangleSplitCandidate> CollectTriangleSplitCandidates(
            PreciseIncisionMesh data,
            List<StrokeSample> samples,
            float epsilon,
            float surfaceBandRadius,
            HashSet<int> allowedTriangles = null)
        {
            var byTri = new Dictionary<int, TriangleSplitCandidate>();
            if (data == null || samples == null || samples.Count < 2)
                return new List<TriangleSplitCandidate>();

            float bandSq = surfaceBandRadius > 1e-6f ? surfaceBandRadius * surfaceBandRadius : float.PositiveInfinity;
            int triCount = data.TriangleCount;
            int totalSegments = Mathf.Max(1, samples.Count - 1);

            for (int seg = 0; seg < samples.Count - 1; seg++)
            {
                StrokeSample start = samples[seg];
                StrokeSample end = samples[seg + 1];
                Vector2 segDir2 = end.p2d - start.p2d;
                if (segDir2.sqrMagnitude <= 1e-12f)
                    continue;

                for (int tri = 0; tri < triCount; tri++)
                {
                    if (allowedTriangles != null && !allowedTriangles.Contains(tri))
                        continue;

                    data.GetTriangle(tri, out int a, out int b, out int c);
                    if (!TryGetTriangleGuideChord(data, start, end, a, b, c, epsilon, bandSq, out EdgeCrossing cross0, out EdgeCrossing cross1))
                        continue;

                    int lonely = FindSharedVertex(cross0.a, cross0.b, cross1.a, cross1.b);
                    if (lonely < 0)
                        continue;

                    int other0 = cross0.a == lonely ? cross0.b : cross0.a;
                    int other1 = cross1.a == lonely ? cross1.b : cross1.a;
                    if (other0 == other1)
                        continue;

                    float lonelySign = ComputeTriangleLonelySideSign3D(
                        data,
                        lonely,
                        other0,
                        other1,
                        cross0,
                        cross1,
                        start.local,
                        end.local);
                    if (Mathf.Abs(lonelySign) <= 1e-8f)
                    {
                        lonelySign = Cross2D(segDir2, data.projected2D[lonely] - start.p2d);
                        if (Mathf.Abs(lonelySign) <= 1e-8f)
                        {
                            float s0 = Cross2D(segDir2, data.projected2D[other0] - start.p2d);
                            float s1 = Cross2D(segDir2, data.projected2D[other1] - start.p2d);
                            lonelySign = -(s0 + s1) * 0.5f;
                        }
                    }
                    if (Mathf.Abs(lonelySign) <= 1e-8f)
                        continue;

                    float chordLenSq = (cross0.pointLocal - cross1.pointLocal).sqrMagnitude;
                    if (chordLenSq <= epsilon * epsilon)
                        continue;

                    var candidate = new TriangleSplitCandidate
                    {
                        triIndex = tri,
                        i0 = a,
                        i1 = b,
                        i2 = c,
                        segmentIndex = seg,
                        globalT = (seg + 0.5f * (cross0.tSegment + cross1.tSegment)) / totalSegments,
                        lonelyVertex = lonely,
                        otherA = other0,
                        otherB = other1,
                        crossingA = cross0,
                        crossingB = cross1,
                        lonelyPositive = lonelySign > 0f,
                        segmentStartLocal = start.local,
                        segmentEndLocal = end.local,
                        centerLocal = 0.5f * (cross0.pointLocal + cross1.pointLocal),
                    };

                    if (byTri.TryGetValue(tri, out var prev))
                    {
                        float prevLenSq = (prev.crossingA.pointLocal - prev.crossingB.pointLocal).sqrMagnitude;
                        if (chordLenSq > prevLenSq)
                            byTri[tri] = candidate;
                    }
                    else
                    {
                        byTri.Add(tri, candidate);
                    }
                }
            }

            var result = new List<TriangleSplitCandidate>(byTri.Values);
            result.Sort((x, y) => x.globalT.CompareTo(y.globalT));
            return result;
        }

        private static void CollectTriangleSegmentEdgeCrossing(
            PreciseIncisionMesh data,
            StrokeSample start,
            StrokeSample end,
            int triA,
            int triB,
            int triC,
            int edgeA,
            int edgeB,
            float epsilon,
            float bandSq,
            List<EdgeCrossing> hits)
        {
            if (data == null || hits == null || edgeA == edgeB)
                return;
            if (triA < 0 || triB < 0 || triC < 0 ||
                triA >= data.vertices.Count || triB >= data.vertices.Count || triC >= data.vertices.Count ||
                edgeA < 0 || edgeB < 0 || edgeA >= data.vertices.Count || edgeB >= data.vertices.Count)
                return;
            if (DistanceSegmentSegmentSq(start.local, end.local, data.vertices[edgeA], data.vertices[edgeB]) > bandSq)
                return;
            if (!TryBuildTriangleLocalFrame(data, triA, triB, triC, out Vector3 frameOrigin, out Vector3 frameAxisU, out Vector3 frameAxisV, out Vector2 _, out Vector2 _, out Vector2 _))
                return;

            Vector2 segA2 = ProjectPointToTriangleFrame2D(start.local, frameOrigin, frameAxisU, frameAxisV);
            Vector2 segB2 = ProjectPointToTriangleFrame2D(end.local, frameOrigin, frameAxisU, frameAxisV);
            Vector2 edgeA2 = ProjectPointToTriangleFrame2D(data.vertices[edgeA], frameOrigin, frameAxisU, frameAxisV);
            Vector2 edgeB2 = ProjectPointToTriangleFrame2D(data.vertices[edgeB], frameOrigin, frameAxisU, frameAxisV);
            AddRobustSegmentEdgeCrossingsOnEdge(data, segA2, segB2, edgeA2, edgeB2, edgeA, edgeB, epsilon, hits);
            CollapseCrossingsByPoint(hits, epsilon);
        }

        private static bool TryGetTriangleGuideChord(
            PreciseIncisionMesh data,
            StrokeSample start,
            StrokeSample end,
            int a,
            int b,
            int c,
            float epsilon,
            float bandSq,
            out EdgeCrossing cross0,
            out EdgeCrossing cross1)
        {
            cross0 = default;
            cross1 = default;
            if (data == null)
                return false;
            if (a < 0 || b < 0 || c < 0 || a >= data.vertices.Count || b >= data.vertices.Count || c >= data.vertices.Count)
                return false;
            if (data.projected2D == null || a >= data.projected2D.Count || b >= data.projected2D.Count || c >= data.projected2D.Count)
                return false;
            if (!TryBuildTriangleLocalFrame(data, a, b, c, out Vector3 frameOrigin, out Vector3 frameAxisU, out Vector3 frameAxisV, out Vector2 pa2, out Vector2 pb2, out Vector2 pc2))
                return false;

            Vector2 start2 = ProjectPointToTriangleFrame2D(start.local, frameOrigin, frameAxisU, frameAxisV);
            Vector2 end2 = ProjectPointToTriangleFrame2D(end.local, frameOrigin, frameAxisU, frameAxisV);
            bool startInside = PointInTriangle2D(start2, pa2, pb2, pc2);
            bool endInside = PointInTriangle2D(end2, pa2, pb2, pc2);

            var directHits = new List<EdgeCrossing>(6);
            CollectTriangleSegmentEdgeCrossing(data, start, end, a, b, c, a, b, epsilon, bandSq, directHits);
            CollectTriangleSegmentEdgeCrossing(data, start, end, a, b, c, b, c, epsilon, bandSq, directHits);
            CollectTriangleSegmentEdgeCrossing(data, start, end, a, b, c, c, a, epsilon, bandSq, directHits);
            CollapseCrossingsByPoint(directHits, epsilon);

            if (directHits.Count >= 2)
            {
                SelectBestCrossingPair(directHits, out cross0, out cross1);
                if ((cross0.pointLocal - cross1.pointLocal).sqrMagnitude > epsilon * epsilon)
                    return true;
            }

            if (!startInside && !endInside)
                return false;

            Vector2 segDir2 = end2 - start2;
            if (segDir2.sqrMagnitude <= 1e-12f)
                return false;

            Vector2 min2 = Vector2.Min(pa2, Vector2.Min(pb2, pc2));
            Vector2 max2 = Vector2.Max(pa2, Vector2.Max(pb2, pc2));
            float triExtent = (max2 - min2).magnitude;
            float extend = Mathf.Max(Mathf.Max(triExtent * 2.5f, Mathf.Sqrt(Mathf.Max(bandSq, 0f)) * 2.0f), segDir2.magnitude * 1.5f);
            extend = Mathf.Max(extend, epsilon * 16f);

            Vector2 mid2 = 0.5f * (start2 + end2);
            Vector2 dir2 = segDir2.normalized;
            Vector2 lineA2 = mid2 - dir2 * extend;
            Vector2 lineB2 = mid2 + dir2 * extend;

            var lineHits = new List<EdgeCrossing>(6);
            AddRobustSegmentEdgeCrossingsOnEdge(data, lineA2, lineB2, pa2, pb2, a, b, epsilon, lineHits);
            AddRobustSegmentEdgeCrossingsOnEdge(data, lineA2, lineB2, pb2, pc2, b, c, epsilon, lineHits);
            AddRobustSegmentEdgeCrossingsOnEdge(data, lineA2, lineB2, pc2, pa2, c, a, epsilon, lineHits);
            CollapseCrossingsByPoint(lineHits, epsilon);
            if (lineHits.Count < 2)
                return false;

            SelectBestCrossingPair(lineHits, out cross0, out cross1);
            return (cross0.pointLocal - cross1.pointLocal).sqrMagnitude > epsilon * epsilon;
        }

        private static bool TryBuildTriangleLocalFrame(
            PreciseIncisionMesh data,
            int a,
            int b,
            int c,
            out Vector3 origin,
            out Vector3 axisU,
            out Vector3 axisV,
            out Vector2 pa2,
            out Vector2 pb2,
            out Vector2 pc2)
        {
            origin = Vector3.zero;
            axisU = Vector3.right;
            axisV = Vector3.up;
            pa2 = Vector2.zero;
            pb2 = Vector2.zero;
            pc2 = Vector2.zero;
            if (data == null)
                return false;
            if (a < 0 || b < 0 || c < 0 || a >= data.vertices.Count || b >= data.vertices.Count || c >= data.vertices.Count)
                return false;

            Vector3 va = data.vertices[a];
            Vector3 vb = data.vertices[b];
            Vector3 vc = data.vertices[c];
            Vector3 ab = vb - va;
            Vector3 ac = vc - va;
            if (ab.sqrMagnitude <= 1e-12f || ac.sqrMagnitude <= 1e-12f)
                return false;

            axisU = ab.normalized;
            Vector3 normal = Vector3.Cross(ab, ac);
            if (normal.sqrMagnitude <= 1e-12f)
                return false;
            normal.Normalize();
            axisV = Vector3.Cross(normal, axisU);
            if (axisV.sqrMagnitude <= 1e-12f)
                return false;
            axisV.Normalize();
            origin = va;
            pa2 = Vector2.zero;
            pb2 = ProjectPointToTriangleFrame2D(vb, origin, axisU, axisV);
            pc2 = ProjectPointToTriangleFrame2D(vc, origin, axisU, axisV);
            return true;
        }

        private static Vector2 ProjectPointToTriangleFrame2D(Vector3 point, Vector3 origin, Vector3 axisU, Vector3 axisV)
        {
            Vector3 local = point - origin;
            return new Vector2(Vector3.Dot(local, axisU), Vector3.Dot(local, axisV));
        }

        private static void AddRobustSegmentEdgeCrossingsOnEdge(
            PreciseIncisionMesh data,
            Vector2 segA2,
            Vector2 segB2,
            Vector2 edgeA2,
            Vector2 edgeB2,
            int a,
            int b,
            float epsilon,
            List<EdgeCrossing> result)
        {
            if (data == null || result == null)
                return;
            if (a < 0 || b < 0 || a >= data.vertices.Count || b >= data.vertices.Count)
                return;
            if (data.projected2D == null || a >= data.projected2D.Count || b >= data.projected2D.Count)
                return;

            if (TryGetCollinearOverlap(segA2, segB2, edgeA2, edgeB2, epsilon, out float overlapStart, out float overlapEnd))
            {
                float tA = GetSegmentParam2D(edgeA2, segA2, segB2);
                float tB = GetSegmentParam2D(edgeB2, segA2, segB2);
                if (tA > overlapStart - 1e-4f && tA < overlapEnd + 1e-4f)
                {
                    result.Add(new EdgeCrossing
                    {
                        a = a,
                        b = b,
                        tSegment = Mathf.Clamp01(tA),
                        tEdge = 0f,
                        point2D = data.projected2D[a],
                        pointLocal = data.vertices[a],
                    });
                }
                if (tB > overlapStart - 1e-4f && tB < overlapEnd + 1e-4f)
                {
                    result.Add(new EdgeCrossing
                    {
                        a = a,
                        b = b,
                        tSegment = Mathf.Clamp01(tB),
                        tEdge = 1f,
                        point2D = data.projected2D[b],
                        pointLocal = data.vertices[b],
                    });
                }
                return;
            }

            if (!TryIntersectSegmentsRobust(segA2, segB2, edgeA2, edgeB2, epsilon, out float tSeg, out float tEdge, out _))
                return;

            result.Add(new EdgeCrossing
            {
                a = a,
                b = b,
                tSegment = tSeg,
                tEdge = tEdge,
                point2D = Vector2.Lerp(data.projected2D[a], data.projected2D[b], tEdge),
                pointLocal = Vector3.Lerp(data.vertices[a], data.vertices[b], tEdge),
            });
        }

        private static int FindSharedVertex(int a0, int b0, int a1, int b1)
        {
            if (a0 == a1 || a0 == b1) return a0;
            if (b0 == a1 || b0 == b1) return b0;
            return -1;
        }

        private static float ComputeTriangleLonelySideSign3D(
            PreciseIncisionMesh data,
            int lonely,
            int other0,
            int other1,
            EdgeCrossing cross0,
            EdgeCrossing cross1,
            Vector3 segmentStartLocal,
            Vector3 segmentEndLocal)
        {
            if (data == null || lonely < 0 || other0 < 0 || other1 < 0 ||
                lonely >= data.vertices.Count || other0 >= data.vertices.Count || other1 >= data.vertices.Count)
                return 0f;

            Vector3 lonelyPos = data.vertices[lonely];
            Vector3 triA = data.vertices[other0];
            Vector3 triB = data.vertices[other1];
            Vector3 triNormal = Vector3.Cross(triA - lonelyPos, triB - lonelyPos);
            if (triNormal.sqrMagnitude <= 1e-12f)
                triNormal = Vector3.Cross(cross1.pointLocal - cross0.pointLocal, lonelyPos - 0.5f * (cross0.pointLocal + cross1.pointLocal));
            if (triNormal.sqrMagnitude <= 1e-12f)
                return 0f;
            triNormal.Normalize();

            Vector3 tangent = segmentEndLocal - segmentStartLocal;
            if (tangent.sqrMagnitude <= 1e-12f)
                tangent = cross1.pointLocal - cross0.pointLocal;
            if (tangent.sqrMagnitude <= 1e-12f)
                tangent = triB - triA;
            if (tangent.sqrMagnitude <= 1e-12f)
                return 0f;
            tangent.Normalize();

            Vector3 splitDir = Vector3.Cross(triNormal, tangent);
            if (splitDir.sqrMagnitude <= 1e-12f)
                splitDir = cross1.pointLocal - cross0.pointLocal;
            if (splitDir.sqrMagnitude <= 1e-12f)
                return 0f;
            splitDir.Normalize();

            Vector3 chordMid = 0.5f * (cross0.pointLocal + cross1.pointLocal);
            return Vector3.Dot(lonelyPos - chordMid, splitDir);
        }

        private static bool ApplyTriangleSplitCandidates(
            PreciseIncisionMesh data,
            List<TriangleSplitCandidate> candidates,
            List<StrokeSample> spineSamples,
            float openWidthWorld,
            float exciseRadius,
            float profileVariation,
            float curvatureWidthScale,
            int layer,
            out List<IncisionLipPair> lipPairs,
            out List<Vector3> centerlineLocal,
            IncisionDebugCapture debugCapture = null,
            Transform meshTransform = null)
        {
            lipPairs = new List<IncisionLipPair>();
            centerlineLocal = BuildIncisionRibbonSpine(candidates, spineSamples);
            if (data == null || candidates == null || candidates.Count == 0)
                return false;

            var originalTriangles = new List<int>(data.triangles);
            int originalTriCount = originalTriangles.Count / 3;
            var candidateByTri = new Dictionary<int, TriangleSplitCandidate>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidateByTri.ContainsKey(candidates[i].triIndex))
                    candidateByTri.Add(candidates[i].triIndex, candidates[i]);
            }

            var edgeVertexCache = new Dictionary<ulong, int>();
            var duplicateCache = new Dictionary<int, int>();
            var pendingLipSegments = new List<PendingLipSegment>(candidates.Count);
            var preparedByTri = new Dictionary<int, PreparedTriangleSplit>(candidateByTri.Count);

            bool anyChanged = false;
            for (int tri = 0; tri < originalTriCount; tri++)
            {
                if (!candidateByTri.TryGetValue(tri, out var cand))
                    continue;

                int baseIndex = tri * 3;
                int a = originalTriangles[baseIndex];
                int b = originalTriangles[baseIndex + 1];
                int c = originalTriangles[baseIndex + 2];

                int cutA = GetOrCreateIntersectionVertex(data, cand.crossingA, edgeVertexCache);
                int cutB = GetOrCreateIntersectionVertex(data, cand.crossingB, edgeVertexCache);
                if (cutA < 0 || cutB < 0 || cutA == cutB)
                    continue;

                int dupA = GetOrCreateDuplicateVertex(data, cutA, duplicateCache);
                int dupB = GetOrCreateDuplicateVertex(data, cutB, duplicateCache);
                if (dupA < 0 || dupB < 0)
                    continue;

                Vector3 refNormal = Vector3.Cross(data.vertices[b] - data.vertices[a], data.vertices[c] - data.vertices[a]);
                if (refNormal.sqrMagnitude <= 1e-12f)
                    refNormal = Vector3.forward;

                Vector3 tangent = cand.segmentEndLocal - cand.segmentStartLocal;
                if (tangent.sqrMagnitude <= 1e-12f)
                    tangent = data.vertices[cand.otherB] - data.vertices[cand.otherA];
                if (tangent.sqrMagnitude <= 1e-12f)
                    tangent = Vector3.forward;
                tangent.Normalize();

                Vector3 splitDir = Vector3.Cross(refNormal.normalized, tangent);
                if (splitDir.sqrMagnitude <= 1e-12f)
                    splitDir = (cand.crossingB.pointLocal - cand.crossingA.pointLocal);
                if (splitDir.sqrMagnitude <= 1e-12f)
                    splitDir = Vector3.right;
                splitDir.Normalize();

                float targetOpen = ComputePolylineTargetOpenWidth(cand.globalT, openWidthWorld, profileVariation, curvatureWidthScale);
                float strokeDenom = Mathf.Max(1f, spineSamples != null ? (float)Mathf.Max(1, spineSamples.Count - 1) : (float)Mathf.Max(1, candidates.Count));
                float strokeTA = (cand.segmentIndex + Mathf.Clamp01(cand.crossingA.tSegment)) / strokeDenom;
                float strokeTB = (cand.segmentIndex + Mathf.Clamp01(cand.crossingB.tSegment)) / strokeDenom;

                preparedByTri[tri] = new PreparedTriangleSplit
                {
                    triIndex = tri,
                    a = a,
                    b = b,
                    c = c,
                    lonelyVertex = cand.lonelyVertex,
                    otherA = cand.otherA,
                    otherB = cand.otherB,
                    cutA = cutA,
                    cutB = cutB,
                    dupA = dupA,
                    dupB = dupB,
                    lonelyPositive = cand.lonelyPositive,
                    refNormal = refNormal,
                };

                CollectPendingLipSegment(pendingLipSegments, cutA, dupA, cutB, dupB, cand.crossingA, cand.crossingB, splitDir, targetOpen, openWidthWorld, layer, strokeTA, strokeTB, tri);
                anyChanged = true;
            }

            if (!anyChanged)
                return false;

            if (!BuildDominantIncisionPath(pendingLipSegments, Mathf.Max(exciseRadius * 0.5f, 1e-5f), out var dominantChain, out var selectedTriangles) ||
                dominantChain == null || dominantChain.Count < 2 || selectedTriangles == null || selectedTriangles.Count == 0)
                return false;

            if (!FilterStationBoundaryOutliers(data, dominantChain, Mathf.Max(exciseRadius * 0.5f, 1e-5f)) || dominantChain.Count < 2)
                return false;

            var newTriangles = new List<int>(originalTriangles.Count + selectedTriangles.Count * 6);
            for (int tri = 0; tri < originalTriCount; tri++)
            {
                int baseIndex = tri * 3;
                int a = originalTriangles[baseIndex];
                int b = originalTriangles[baseIndex + 1];
                int c = originalTriangles[baseIndex + 2];

                if (!selectedTriangles.Contains(tri) || !preparedByTri.TryGetValue(tri, out var prep))
                {
                    newTriangles.Add(a);
                    newTriangles.Add(b);
                    newTriangles.Add(c);
                    continue;
                }

                if (prep.lonelyPositive)
                {
                    AddOrientedTriangle(newTriangles, data.vertices, prep.lonelyVertex, prep.dupA, prep.dupB, prep.refNormal);
                    AddOrientedTriangle(newTriangles, data.vertices, prep.cutA, prep.otherA, prep.otherB, prep.refNormal);
                    AddOrientedTriangle(newTriangles, data.vertices, prep.cutA, prep.otherB, prep.cutB, prep.refNormal);
                }
                else
                {
                    AddOrientedTriangle(newTriangles, data.vertices, prep.lonelyVertex, prep.cutA, prep.cutB, prep.refNormal);
                    AddOrientedTriangle(newTriangles, data.vertices, prep.dupA, prep.otherA, prep.otherB, prep.refNormal);
                    AddOrientedTriangle(newTriangles, data.vertices, prep.dupA, prep.otherB, prep.dupB, prep.refNormal);
                }
            }

            data.triangles.Clear();
            data.triangles.AddRange(newTriangles);

            ApplyStationBasedLipOffsets(data, dominantChain);
            lipPairs = BuildLipPairsFromStationChain(dominantChain, data);
            SortAndCompactIncisionLipPairs(lipPairs, Mathf.Max(exciseRadius * 0.35f, 1e-5f), debugCapture);
            if (lipPairs.Count >= 2)
                centerlineLocal = BuildCenterlineFromStationChain(dominantChain, Mathf.Max(exciseRadius * 2.0f, 1e-4f));

            RemoveDegenerateTriangles(data, Mathf.Max(exciseRadius * 0.06f, 1e-7f), Mathf.Max(exciseRadius * 0.18f, 1e-5f), debugCapture, meshTransform, layer);
            ExciseResidualBridgeTriangles(data, dominantChain, exciseRadius, debugCapture, meshTransform, layer);
            RemoveDegenerateTriangles(data, Mathf.Max(exciseRadius * 0.06f, 1e-7f), Mathf.Max(exciseRadius * 0.18f, 1e-5f), debugCapture, meshTransform, layer);

            if (lipPairs.Count >= 2)
                centerlineLocal = BuildCenterlineFromStationChain(dominantChain, Mathf.Max(exciseRadius * 2.0f, 1e-4f));
            return lipPairs.Count >= 2;
        }


        private static List<Vector3> BuildIncisionRibbonSpine(List<TriangleSplitCandidate> candidates, List<StrokeSample> spineSamples)
        {
            var spine = new List<Vector3>();
            if (spineSamples != null)
            {
                for (int i = 0; i < spineSamples.Count; i++)
                {
                    if (spine.Count == 0 || (spine[spine.Count - 1] - spineSamples[i].local).sqrMagnitude > 1e-10f)
                        spine.Add(spineSamples[i].local);
                }
            }

            if (spine.Count >= 2)
                return spine;

            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (spine.Count == 0 || (spine[spine.Count - 1] - candidates[i].centerLocal).sqrMagnitude > 1e-10f)
                        spine.Add(candidates[i].centerLocal);
                }
            }

            return spine;
        }

        private static int GetOrCreateIntersectionVertex(PreciseIncisionMesh data, EdgeCrossing crossing, Dictionary<ulong, int> cache)
        {
            ulong key = MakeUndirectedEdgeKey(crossing.a, crossing.b);
            if (cache.TryGetValue(key, out int existing))
                return existing;
            int idx = data.AddVertexOnEdge(crossing.pointLocal, crossing.point2D, crossing.a, crossing.b, Mathf.Clamp01(crossing.tEdge));
            cache[key] = idx;
            return idx;
        }

        private static int GetOrCreateDuplicateVertex(PreciseIncisionMesh data, int source, Dictionary<int, int> cache)
        {
            if (cache.TryGetValue(source, out int existing))
                return existing;
            int dup = data.vertices.Count;
            cache[source] = dup;
            data.vertices.Add(data.vertices[source]);
            data.projected2D.Add(data.projected2D[source]);
            if (data.hasUV) data.uv.Add(data.uv[source]);
            if (data.hasTangents) data.tangents.Add(data.tangents[source]);
            if (data.hasColors) data.colors.Add(data.colors[source]);
            return dup;
        }

        private static void AccumulateVertexOffset(Dictionary<int, Vector3> accum, Dictionary<int, int> counts, int index, Vector3 delta)
        {
            if (accum.TryGetValue(index, out var prev)) accum[index] = prev + delta;
            else accum[index] = delta;
            counts[index] = counts.TryGetValue(index, out int c) ? c + 1 : 1;
        }

        private static void AddOrientedTriangle(List<int> triangles, List<Vector3> vertices, int a, int b, int c, Vector3 refNormal)
        {
            if (a == b || b == c || c == a)
                return;
            Vector3 pa = vertices[a];
            Vector3 pb = vertices[b];
            Vector3 pc = vertices[c];
            Vector3 n = Vector3.Cross(pb - pa, pc - pa);
            if (Vector3.Dot(n, refNormal) < 0f)
            {
                int t = b; b = c; c = t;
            }
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private static void CollectPendingLipSegment(List<PendingLipSegment> pendingLipSegments, int leftAIndex, int rightAIndex, int leftBIndex, int rightBIndex, in EdgeCrossing crossingA, in EdgeCrossing crossingB, Vector3 splitDir, float targetOpen, float baseOpen, int layer, float strokeTA, float strokeTB, int sourceTriIndex)
        {
            if (pendingLipSegments == null)
                return;

            if (leftAIndex < 0 || rightAIndex < 0 || leftBIndex < 0 || rightBIndex < 0)
                return;

            if (splitDir.sqrMagnitude <= 1e-12f)
                splitDir = Vector3.right;
            else
                splitDir.Normalize();

            pendingLipSegments.Add(new PendingLipSegment
            {
                leftAIndex = leftAIndex,
                rightAIndex = rightAIndex,
                leftBIndex = leftBIndex,
                rightBIndex = rightBIndex,
                endpointKeyA = MakeCrossingEndpointKey(crossingA.a, crossingA.b, crossingA.tEdge),
                endpointKeyB = MakeCrossingEndpointKey(crossingB.a, crossingB.b, crossingB.tEdge),
                pointALocal = crossingA.pointLocal,
                pointBLocal = crossingB.pointLocal,
                centerLocal = 0.5f * (crossingA.pointLocal + crossingB.pointLocal),
                splitDirLocal = splitDir,
                targetOpen = targetOpen,
                weight = baseOpen > 1e-6f ? Mathf.Clamp01(targetOpen / Mathf.Max(1e-6f, baseOpen * 2f)) : 1f,
                strokeTA = Mathf.Clamp01(strokeTA),
                strokeTB = Mathf.Clamp01(strokeTB),
                layer = layer,
                sourceTriIndex = sourceTriIndex,
            });
        }

        private static void AppendPendingLipStation(List<PendingLipStation> stations, ulong stationKey, int leftIndex, int rightIndex, Vector3 center, Vector3 splitDir, float targetOpen, float weight, float strokeT, int layer)
        {
            if (leftIndex < 0 || rightIndex < 0 || leftIndex == rightIndex)
                return;

            var cur = new PendingLipStation
            {
                stationKey = stationKey != 0 ? stationKey : MakeUndirectedEdgeKey(leftIndex, rightIndex),
                leftLocalIndex = leftIndex,
                rightLocalIndex = rightIndex,
                seedCenterLocal = center,
                splitDirLocal = splitDir.sqrMagnitude > 1e-12f ? splitDir.normalized : Vector3.right,
                targetOpen = targetOpen,
                weight = weight,
                strokeT = Mathf.Clamp01(strokeT),
                layer = layer,
            };

            if (stations.Count == 0)
            {
                stations.Add(cur);
                return;
            }

            var prev = stations[stations.Count - 1];
            if (prev.stationKey == cur.stationKey)
            {
                prev.seedCenterLocal = 0.5f * (prev.seedCenterLocal + cur.seedCenterLocal);
                Vector3 mergedDir = prev.splitDirLocal + cur.splitDirLocal;
                if (mergedDir.sqrMagnitude > 1e-12f)
                    prev.splitDirLocal = mergedDir.normalized;
                prev.targetOpen = Mathf.Max(prev.targetOpen, cur.targetOpen);
                prev.weight = Mathf.Max(prev.weight, cur.weight);
                prev.strokeT = 0.5f * (prev.strokeT + cur.strokeT);
                if (cur.targetOpen >= prev.targetOpen)
                {
                    prev.leftLocalIndex = cur.leftLocalIndex;
                    prev.rightLocalIndex = cur.rightLocalIndex;
                }
                stations[stations.Count - 1] = prev;
                return;
            }

            stations.Add(cur);
        }

        private static List<IncisionLipPair> BuildLipPairsFromSegments(List<PendingLipSegment> pendingLipSegments, PreciseIncisionMesh data, float minSpacing)
        {
            if (!BuildDominantIncisionPath(pendingLipSegments, minSpacing, out var dominantChain, out _))
                return new List<IncisionLipPair>();
            if (!FilterStationBoundaryOutliers(data, dominantChain, minSpacing) || dominantChain.Count < 2)
                return new List<IncisionLipPair>();
            return BuildLipPairsFromStationChain(dominantChain, data);
        }

        private static bool BuildDominantIncisionPath(
            List<PendingLipSegment> pendingLipSegments,
            float minSpacing,
            out List<PendingLipStation> dominantChain,
            out HashSet<int> selectedTriangles)
        {
            dominantChain = new List<PendingLipStation>();
            selectedTriangles = new HashSet<int>();
            if (pendingLipSegments == null || pendingLipSegments.Count == 0)
                return false;

            var segments = new List<PendingLipSegment>(pendingLipSegments.Count);
            for (int i = 0; i < pendingLipSegments.Count; i++)
            {
                var seg = pendingLipSegments[i];
                if (seg.leftAIndex < 0 || seg.rightAIndex < 0 || seg.leftBIndex < 0 || seg.rightBIndex < 0)
                    continue;

                if (seg.endpointKeyA == 0UL)
                    seg.endpointKeyA = MakeUndirectedEdgeKey(seg.leftAIndex, seg.rightAIndex);
                if (seg.endpointKeyB == 0UL)
                    seg.endpointKeyB = MakeUndirectedEdgeKey(seg.leftBIndex, seg.rightBIndex);

                if (seg.strokeTA > seg.strokeTB)
                {
                    int leftTmp = seg.leftAIndex;
                    int rightTmp = seg.rightAIndex;
                    ulong keyTmp = seg.endpointKeyA;
                    Vector3 pointTmp = seg.pointALocal;
                    float strokeTmp = seg.strokeTA;
                    seg.leftAIndex = seg.leftBIndex;
                    seg.rightAIndex = seg.rightBIndex;
                    seg.endpointKeyA = seg.endpointKeyB;
                    seg.pointALocal = seg.pointBLocal;
                    seg.strokeTA = seg.strokeTB;
                    seg.leftBIndex = leftTmp;
                    seg.rightBIndex = rightTmp;
                    seg.endpointKeyB = keyTmp;
                    seg.pointBLocal = pointTmp;
                    seg.strokeTB = strokeTmp;
                }

                if (seg.endpointKeyA == seg.endpointKeyB)
                    continue;

                segments.Add(seg);
            }

            if (segments.Count == 0)
                return false;

            int RegisterNode(Dictionary<ulong, int> nodeMap, List<StationGraphNode> nodes, ulong stationKey, int leftIndex, int rightIndex, Vector3 center, Vector3 splitDir, float targetOpen, float weight, float strokeT, int layer)
            {
                if (nodeMap.TryGetValue(stationKey, out int existing))
                {
                    var node = nodes[existing];
                    int count = Mathf.Max(1, node.mergeCount);
                    node.seedCenterLocal = (node.seedCenterLocal * count + center) / (count + 1);
                    Vector3 mergedDir = node.splitDirLocal * count + splitDir;
                    if (mergedDir.sqrMagnitude > 1e-12f)
                        node.splitDirLocal = mergedDir.normalized;
                    node.targetOpen = Mathf.Max(node.targetOpen, targetOpen);
                    node.weight = Mathf.Max(node.weight, weight);
                    node.strokeT = (node.strokeT * count + Mathf.Clamp01(strokeT)) / (count + 1);
                    node.mergeCount = count + 1;
                    nodes[existing] = node;
                    return existing;
                }

                int id = nodes.Count;
                nodeMap.Add(stationKey, id);
                nodes.Add(new StationGraphNode
                {
                    stationKey = stationKey,
                    leftLocalIndex = leftIndex,
                    rightLocalIndex = rightIndex,
                    seedCenterLocal = center,
                    splitDirLocal = splitDir.sqrMagnitude > 1e-12f ? splitDir.normalized : Vector3.right,
                    targetOpen = targetOpen,
                    weight = weight,
                    strokeT = Mathf.Clamp01(strokeT),
                    layer = layer,
                    mergeCount = 1,
                });
                return id;
            }

            var nodeMap2 = new Dictionary<ulong, int>(segments.Count * 2);
            var nodes2 = new List<StationGraphNode>(segments.Count * 2);
            var edges2 = new List<StationGraphEdge>(segments.Count);
            var undirectedAdj = new Dictionary<int, List<int>>();
            var outgoingAdj = new Dictionary<int, List<int>>();

            void AddAdj(Dictionary<int, List<int>> map, int key, int value)
            {
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<int>(4);
                    map.Add(key, list);
                }
                list.Add(value);
            }

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                int nodeA = RegisterNode(nodeMap2, nodes2, seg.endpointKeyA, seg.leftAIndex, seg.rightAIndex, seg.pointALocal, seg.splitDirLocal, seg.targetOpen, seg.weight, seg.strokeTA, seg.layer);
                int nodeB = RegisterNode(nodeMap2, nodes2, seg.endpointKeyB, seg.leftBIndex, seg.rightBIndex, seg.pointBLocal, seg.splitDirLocal, seg.targetOpen, seg.weight, seg.strokeTB, seg.layer);
                if (nodeA == nodeB)
                    continue;

                float strokeA = nodes2[nodeA].strokeT;
                float strokeB = nodes2[nodeB].strokeT;
                if (strokeA > strokeB + 1e-5f || (Mathf.Abs(strokeA - strokeB) <= 1e-5f && nodeA > nodeB))
                {
                    int t = nodeA; nodeA = nodeB; nodeB = t;
                }

                int edgeIndex = edges2.Count;
                edges2.Add(new StationGraphEdge
                {
                    nodeA = nodeA,
                    nodeB = nodeB,
                    sourceTriIndex = seg.sourceTriIndex,
                    strokeMid = 0.5f * (seg.strokeTA + seg.strokeTB),
                });

                AddAdj(undirectedAdj, nodeA, edgeIndex);
                AddAdj(undirectedAdj, nodeB, edgeIndex);
                AddAdj(outgoingAdj, nodeA, edgeIndex);
            }

            if (nodes2.Count < 2 || edges2.Count == 0)
                return false;

            var visitedNodes = new bool[nodes2.Count];
            List<int> bestPathNodes = null;
            List<int> bestPathEdges = null;
            float bestPathScore = float.NegativeInfinity;

            for (int startNode = 0; startNode < nodes2.Count; startNode++)
            {
                if (visitedNodes[startNode])
                    continue;

                var componentNodes = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(startNode);
                visitedNodes[startNode] = true;
                while (queue.Count > 0)
                {
                    int curNode = queue.Dequeue();
                    componentNodes.Add(curNode);
                    if (!undirectedAdj.TryGetValue(curNode, out var edgeList))
                        continue;
                    for (int i = 0; i < edgeList.Count; i++)
                    {
                        var edge = edges2[edgeList[i]];
                        int nextNode = edge.nodeA == curNode ? edge.nodeB : edge.nodeA;
                        if (visitedNodes[nextNode])
                            continue;
                        visitedNodes[nextNode] = true;
                        queue.Enqueue(nextNode);
                    }
                }

                if (componentNodes.Count < 2)
                    continue;

                componentNodes.Sort((x, y) =>
                {
                    int cmp = nodes2[x].strokeT.CompareTo(nodes2[y].strokeT);
                    return cmp != 0 ? cmp : x.CompareTo(y);
                });
                var inComponent = new HashSet<int>(componentNodes);
                var bestScore = new Dictionary<int, float>(componentNodes.Count);
                var bestEdgeCount = new Dictionary<int, int>(componentNodes.Count);
                var parentNode = new Dictionary<int, int>(componentNodes.Count);
                var parentEdge = new Dictionary<int, int>(componentNodes.Count);
                for (int i = 0; i < componentNodes.Count; i++)
                {
                    int node = componentNodes[i];
                    bestScore[node] = 0f;
                    bestEdgeCount[node] = 0;
                    parentNode[node] = -1;
                    parentEdge[node] = -1;
                }

                float maxAllowedJump = Mathf.Max(minSpacing * 6.0f, 1e-4f);
                for (int i = 0; i < componentNodes.Count; i++)
                {
                    int node = componentNodes[i];
                    if (!outgoingAdj.TryGetValue(node, out var outEdges))
                        continue;

                    for (int e = 0; e < outEdges.Count; e++)
                    {
                        int edgeIndex = outEdges[e];
                        var edge = edges2[edgeIndex];
                        int nextNode = edge.nodeB;
                        if (!inComponent.Contains(nextNode))
                            continue;

                        float strokeDelta = nodes2[nextNode].strokeT - nodes2[node].strokeT;
                        if (strokeDelta < -1e-5f)
                            continue;

                        float jump = Vector3.Distance(nodes2[node].seedCenterLocal, nodes2[nextNode].seedCenterLocal);
                        if (!float.IsFinite(jump) || jump > maxAllowedJump)
                            continue;

                        if (Vector3.Dot(nodes2[node].splitDirLocal, nodes2[nextNode].splitDirLocal) < -0.35f)
                            continue;

                        float edgeWeight = jump * 10f + Mathf.Max(strokeDelta, 1e-4f) * 80f;
                        float candidateScore = bestScore[node] + edgeWeight;
                        int candidateEdgeCount = bestEdgeCount[node] + 1;
                        if (candidateScore > bestScore[nextNode] + 1e-6f ||
                            (Mathf.Abs(candidateScore - bestScore[nextNode]) <= 1e-6f && candidateEdgeCount > bestEdgeCount[nextNode]))
                        {
                            bestScore[nextNode] = candidateScore;
                            bestEdgeCount[nextNode] = candidateEdgeCount;
                            parentNode[nextNode] = node;
                            parentEdge[nextNode] = edgeIndex;
                        }
                    }
                }

                int bestEndNode = -1;
                float componentScore = float.NegativeInfinity;
                int componentEdgeCount = 0;
                for (int i = 0; i < componentNodes.Count; i++)
                {
                    int node = componentNodes[i];
                    float score = bestScore[node];
                    int edgeCount = bestEdgeCount[node];
                    if (edgeCount <= 0)
                        continue;
                    float totalScore = edgeCount * 1000f + score;
                    if (totalScore > componentScore)
                    {
                        componentScore = totalScore;
                        componentEdgeCount = edgeCount;
                        bestEndNode = node;
                    }
                }
                if (bestEndNode < 0 || componentEdgeCount <= 0)
                    continue;

                var compPathNodes = new List<int>(componentEdgeCount + 1);
                var compPathEdges = new List<int>(componentEdgeCount);
                int walk = bestEndNode;
                while (walk >= 0)
                {
                    compPathNodes.Add(walk);
                    int edgeIndex = parentEdge[walk];
                    if (edgeIndex >= 0)
                        compPathEdges.Add(edgeIndex);
                    walk = parentNode[walk];
                }
                compPathNodes.Reverse();
                compPathEdges.Reverse();

                if (compPathNodes.Count < 2 || compPathEdges.Count < 1)
                    continue;

                if (componentScore > bestPathScore)
                {
                    bestPathScore = componentScore;
                    bestPathNodes = compPathNodes;
                    bestPathEdges = compPathEdges;
                }
            }

            if (bestPathNodes == null || bestPathEdges == null || bestPathNodes.Count < 2 || bestPathEdges.Count < 1)
                return false;

            for (int i = 0; i < bestPathEdges.Count; i++)
                selectedTriangles.Add(edges2[bestPathEdges[i]].sourceTriIndex);

            for (int i = 0; i < bestPathNodes.Count; i++)
            {
                var node = nodes2[bestPathNodes[i]];
                AppendPendingLipStation(dominantChain, node.stationKey, node.leftLocalIndex, node.rightLocalIndex, node.seedCenterLocal, node.splitDirLocal, node.targetOpen, node.weight, node.strokeT, node.layer);
            }

            if (dominantChain.Count < 2)
                return false;

            if (dominantChain[0].strokeT > dominantChain[dominantChain.Count - 1].strokeT)
                dominantChain.Reverse();

            return selectedTriangles.Count > 0;
        }


        private static bool BuildStrokeOrderedFallbackIncisionPath(
            List<PendingLipSegment> pendingLipSegments,
            float minSpacing,
            out List<PendingLipStation> dominantChain,
            out HashSet<int> selectedTriangles)
        {
            dominantChain = new List<PendingLipStation>();
            selectedTriangles = new HashSet<int>();
            if (pendingLipSegments == null || pendingLipSegments.Count == 0)
                return false;

            var ordered = new List<PendingLipSegment>(pendingLipSegments.Count);
            for (int i = 0; i < pendingLipSegments.Count; i++)
            {
                var seg = pendingLipSegments[i];
                if (seg.leftAIndex < 0 || seg.rightAIndex < 0 || seg.leftBIndex < 0 || seg.rightBIndex < 0)
                    continue;

                if (seg.endpointKeyA == 0UL)
                    seg.endpointKeyA = MakeUndirectedEdgeKey(seg.leftAIndex, seg.rightAIndex);
                if (seg.endpointKeyB == 0UL)
                    seg.endpointKeyB = MakeUndirectedEdgeKey(seg.leftBIndex, seg.rightBIndex);
                if (seg.endpointKeyA == seg.endpointKeyB)
                    continue;

                if (seg.strokeTA > seg.strokeTB)
                {
                    int leftTmp = seg.leftAIndex;
                    int rightTmp = seg.rightAIndex;
                    ulong keyTmp = seg.endpointKeyA;
                    Vector3 pointTmp = seg.pointALocal;
                    float strokeTmp = seg.strokeTA;
                    seg.leftAIndex = seg.leftBIndex;
                    seg.rightAIndex = seg.rightBIndex;
                    seg.endpointKeyA = seg.endpointKeyB;
                    seg.pointALocal = seg.pointBLocal;
                    seg.strokeTA = seg.strokeTB;
                    seg.leftBIndex = leftTmp;
                    seg.rightBIndex = rightTmp;
                    seg.endpointKeyB = keyTmp;
                    seg.pointBLocal = pointTmp;
                    seg.strokeTB = strokeTmp;
                }

                ordered.Add(seg);
            }

            if (ordered.Count == 0)
                return false;

            ordered.Sort((x, y) =>
            {
                int cmp = x.strokeTA.CompareTo(y.strokeTA);
                if (cmp != 0) return cmp;
                cmp = x.strokeTB.CompareTo(y.strokeTB);
                if (cmp != 0) return cmp;
                return x.sourceTriIndex.CompareTo(y.sourceTriIndex);
            });

            float minSpacingSq = Mathf.Max(minSpacing, 1e-5f);
            minSpacingSq *= minSpacingSq;
            int acceptedSegments = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                var seg = ordered[i];
                AppendPendingLipStation(dominantChain, seg.endpointKeyA, seg.leftAIndex, seg.rightAIndex, seg.pointALocal, seg.splitDirLocal, seg.targetOpen, seg.weight, seg.strokeTA, seg.layer);

                bool allowB = dominantChain.Count == 0 || (dominantChain[dominantChain.Count - 1].seedCenterLocal - seg.pointBLocal).sqrMagnitude > minSpacingSq * 0.25f;
                if (allowB)
                    AppendPendingLipStation(dominantChain, seg.endpointKeyB, seg.leftBIndex, seg.rightBIndex, seg.pointBLocal, seg.splitDirLocal, seg.targetOpen, seg.weight, seg.strokeTB, seg.layer);

                selectedTriangles.Add(seg.sourceTriIndex);
                acceptedSegments++;
            }

            if (dominantChain.Count < 2 || acceptedSegments <= 0 || selectedTriangles.Count <= 0)
                return false;

            dominantChain.Sort((x, y) =>
            {
                int cmp = x.strokeT.CompareTo(y.strokeT);
                if (cmp != 0) return cmp;
                return x.stationKey.CompareTo(y.stationKey);
            });

            var compact = new List<PendingLipStation>(dominantChain.Count);
            for (int i = 0; i < dominantChain.Count; i++)
            {
                var cur = dominantChain[i];
                if (compact.Count > 0)
                {
                    var prev = compact[compact.Count - 1];
                    if (prev.stationKey == cur.stationKey ||
                        (prev.seedCenterLocal - cur.seedCenterLocal).sqrMagnitude <= minSpacingSq * 0.04f)
                    {
                        AppendPendingLipStation(compact, cur.stationKey, cur.leftLocalIndex, cur.rightLocalIndex, cur.seedCenterLocal, cur.splitDirLocal, cur.targetOpen, cur.weight, cur.strokeT, cur.layer);
                        continue;
                    }
                }

                compact.Add(cur);
            }

            dominantChain = compact;
            return dominantChain.Count >= 2 && selectedTriangles.Count > 0;
        }

        private static Vector3 ComputeStationChainTangent(List<PendingLipStation> stations, int index)
        {
            if (stations == null || index < 0 || index >= stations.Count)
                return Vector3.zero;

            Vector3 tangent = Vector3.zero;
            Vector3 center = stations[index].seedCenterLocal;

            if (index > 0)
                tangent += center - stations[index - 1].seedCenterLocal;
            if (index + 1 < stations.Count)
                tangent += stations[index + 1].seedCenterLocal - center;

            if (tangent.sqrMagnitude <= 1e-12f && index > 0)
                tangent = center - stations[index - 1].seedCenterLocal;
            if (tangent.sqrMagnitude <= 1e-12f && index + 1 < stations.Count)
                tangent = stations[index + 1].seedCenterLocal - center;

            return tangent;
        }

        private static bool FilterStationBoundaryOutliers(PreciseIncisionMesh data, List<PendingLipStation> stations, float minSpacing)
        {
            if (stations == null)
                return false;

            minSpacing = Mathf.Max(1e-5f, minSpacing);
            var normalized = NormalizeStationBoundaryChain3D(data, stations, minSpacing);
            normalized = SelectBestSurfaceContinuousStationSubchain(data, normalized, minSpacing);
            stations.Clear();
            stations.AddRange(normalized);
            if (stations.Count < 2)
                return false;

            float avgSpacing = 0f;
            int spacingCount = 0;
            float avgGap = 0f;
            int gapCount = 0;
            for (int i = 0; i < stations.Count; i++)
            {
                if (i > 0)
                {
                    avgSpacing += ComputeStationSpacing(stations[i - 1], stations[i]);
                    spacingCount++;
                }

                if (TryGetStationEndpoints(data, stations[i], out Vector3 left, out Vector3 right))
                {
                    avgGap += Vector3.Distance(left, right);
                    gapCount++;
                }
            }
            avgSpacing = spacingCount > 0 ? avgSpacing / spacingCount : minSpacing;
            avgGap = gapCount > 0 ? avgGap / gapCount : minSpacing * 2f;

            float maxCenterStep = Mathf.Max(minSpacing * 4.5f, Mathf.Max(avgSpacing * 2.75f, avgGap * 2.0f));
            float minGap = Mathf.Max(minSpacing * 0.2f, avgGap * 0.15f);
            float maxGap = Mathf.Max(minSpacing * 8f, avgGap * 3.5f);
            float mismatchLimit = Mathf.Max(minSpacing * 6f, avgGap * 2.25f);

            var filtered = new List<PendingLipStation>(stations.Count);
            filtered.Add(stations[0]);
            for (int i = 1; i < stations.Count - 1; i++)
            {
                PendingLipStation prev = filtered[filtered.Count - 1];
                PendingLipStation cur = stations[i];
                PendingLipStation next = stations[i + 1];

                float stepPrev = ComputeStationSpacing(prev, cur);
                float stepNext = ComputeStationSpacing(cur, next);
                bool severeJump = stepPrev > maxCenterStep && stepNext > maxCenterStep;

                bool hasGap = TryGetStationEndpoints(data, cur, out Vector3 curLeft, out Vector3 curRight);
                float gap = hasGap ? Vector3.Distance(curLeft, curRight) : 0f;
                bool badGap = hasGap && (gap < minGap || gap > maxGap);

                float mismatch = 0f;
                bool hasPrevEnds = TryGetStationEndpoints(data, prev, out Vector3 prevLeft, out Vector3 prevRight);
                bool hasNextEnds = TryGetStationEndpoints(data, next, out Vector3 nextLeft, out Vector3 nextRight);
                if (hasGap && hasPrevEnds)
                    mismatch += Vector3.Distance(prevLeft, curLeft) + Vector3.Distance(prevRight, curRight);
                if (hasGap && hasNextEnds)
                    mismatch += Vector3.Distance(curLeft, nextLeft) + Vector3.Distance(curRight, nextRight);

                bool severeMismatch = mismatch > mismatchLimit;
                if (severeJump && badGap && severeMismatch)
                    continue;

                filtered.Add(cur);
            }
            filtered.Add(stations[stations.Count - 1]);

            var compact = NormalizeStationBoundaryChain3D(data, filtered, minSpacing);
            compact = SelectBestSurfaceContinuousStationSubchain(data, compact, minSpacing);
            if (compact.Count >= 2 && compact.Count >= Mathf.Max(2, Mathf.CeilToInt(normalized.Count * 0.5f)))
            {
                stations.Clear();
                stations.AddRange(compact);
                return true;
            }

            stations.Clear();
            stations.AddRange(normalized);
            return stations.Count >= 2;
        }

        private static List<PendingLipStation> NormalizeStationBoundaryChain3D(PreciseIncisionMesh data, List<PendingLipStation> stations, float minSpacing)
        {
            var ordered = new List<PendingLipStation>();
            if (stations == null)
                return ordered;

            minSpacing = Mathf.Max(1e-5f, minSpacing);
            float mergeDistSq = minSpacing * minSpacing * 0.2f;

            for (int i = 0; i < stations.Count; i++)
            {
                var raw = stations[i];
                if (raw.leftLocalIndex < 0 || raw.rightLocalIndex < 0 || raw.leftLocalIndex == raw.rightLocalIndex)
                    continue;

                PendingLipStation cur = ordered.Count > 0
                    ? OrientStationUsingReference(data, raw, ordered[ordered.Count - 1], true)
                    : OrientStationUsingReference(data, raw, default, false);

                if (ordered.Count > 0)
                {
                    PendingLipStation prev = ordered[ordered.Count - 1];
                    bool sameStation = prev.stationKey == cur.stationKey;
                    bool nearDuplicate = (cur.seedCenterLocal - prev.seedCenterLocal).sqrMagnitude <= mergeDistSq;
                    if (sameStation || nearDuplicate)
                    {
                        cur.seedCenterLocal = 0.5f * (prev.seedCenterLocal + cur.seedCenterLocal);

                        Vector3 mergedDir = prev.splitDirLocal + cur.splitDirLocal;
                        if (mergedDir.sqrMagnitude > 1e-12f)
                            cur.splitDirLocal = mergedDir.normalized;
                        else if (cur.splitDirLocal.sqrMagnitude > 1e-12f)
                            cur.splitDirLocal.Normalize();
                        else if (prev.splitDirLocal.sqrMagnitude > 1e-12f)
                            cur.splitDirLocal = prev.splitDirLocal.normalized;

                        cur.targetOpen = Mathf.Max(prev.targetOpen, cur.targetOpen);
                        cur.weight = Mathf.Max(prev.weight, cur.weight);
                        cur.strokeT = 0.5f * (prev.strokeT + cur.strokeT);
                        cur.stationKey = prev.stationKey != 0UL ? prev.stationKey : cur.stationKey;

                        ordered[ordered.Count - 1] = cur;
                        continue;
                    }
                }

                ordered.Add(cur);
            }

            return ordered;
        }

        private static PendingLipStation OrientStationUsingReference(PreciseIncisionMesh data, PendingLipStation station, PendingLipStation reference, bool hasReference)
        {
            if (!TryGetStationEndpoints(data, station, out Vector3 stationLeft, out Vector3 stationRight))
                return station;

            if (hasReference && TryGetStationEndpoints(data, reference, out Vector3 refLeft, out Vector3 refRight))
            {
                float direct = (stationLeft - refLeft).sqrMagnitude + (stationRight - refRight).sqrMagnitude;
                float swapped = (stationRight - refLeft).sqrMagnitude + (stationLeft - refRight).sqrMagnitude;
                if (swapped + 1e-10f < direct)
                {
                    SwapStationSides(ref station);
                    Vector3 tmp = stationLeft;
                    stationLeft = stationRight;
                    stationRight = tmp;
                }
            }

            station.seedCenterLocal = 0.5f * (stationLeft + stationRight);
            return station;
        }

        private static bool TryGetStationEndpoints(PreciseIncisionMesh data, PendingLipStation station, out Vector3 left, out Vector3 right)
        {
            left = Vector3.zero;
            right = Vector3.zero;
            if (data == null)
                return false;
            if (station.leftLocalIndex < 0 || station.rightLocalIndex < 0)
                return false;
            if (station.leftLocalIndex >= data.vertices.Count || station.rightLocalIndex >= data.vertices.Count)
                return false;
            left = data.vertices[station.leftLocalIndex];
            right = data.vertices[station.rightLocalIndex];
            return true;
        }

        private static void SwapStationSides(ref PendingLipStation station)
        {
            int tmp = station.leftLocalIndex;
            station.leftLocalIndex = station.rightLocalIndex;
            station.rightLocalIndex = tmp;
            station.splitDirLocal = -station.splitDirLocal;
        }


        private static float ComputeStationEndpointContinuity(PreciseIncisionMesh data, PendingLipStation a, PendingLipStation b)
        {
            if (!TryGetStationEndpoints(data, a, out Vector3 aLeft, out Vector3 aRight) ||
                !TryGetStationEndpoints(data, b, out Vector3 bLeft, out Vector3 bRight))
                return float.PositiveInfinity;

            float leftStep = Vector3.Distance(aLeft, bLeft);
            float rightStep = Vector3.Distance(aRight, bRight);
            return 0.5f * (leftStep + rightStep);
        }

        private static List<PendingLipStation> SelectBestSurfaceContinuousStationSubchain(PreciseIncisionMesh data, List<PendingLipStation> stations, float minSpacing)
        {
            var best = new List<PendingLipStation>();
            if (stations == null || stations.Count == 0)
                return best;

            minSpacing = Mathf.Max(1e-5f, minSpacing);

            float avgCenterStep = 0f;
            int centerCount = 0;
            float avgEndpointStep = 0f;
            int endpointCount = 0;
            for (int i = 1; i < stations.Count; i++)
            {
                avgCenterStep += Vector3.Distance(stations[i - 1].seedCenterLocal, stations[i].seedCenterLocal);
                centerCount++;

                float endpointStep = ComputeStationEndpointContinuity(data, stations[i - 1], stations[i]);
                if (!(float.IsNaN(endpointStep) || float.IsInfinity(endpointStep)))
                {
                    avgEndpointStep += endpointStep;
                    endpointCount++;
                }
            }

            avgCenterStep = centerCount > 0 ? avgCenterStep / centerCount : minSpacing;
            avgEndpointStep = endpointCount > 0 ? avgEndpointStep / endpointCount : Mathf.Max(minSpacing * 2f, avgCenterStep * 1.5f);

            float maxCenterJump = Mathf.Max(minSpacing * 4.5f, avgCenterStep * 2.5f);
            float maxEndpointJump = Mathf.Max(minSpacing * 6f, avgEndpointStep * 2.5f);
            float maxStrokeJump = 0.35f;

            var current = new List<PendingLipStation>();
            float currentScore = 0f;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < stations.Count; i++)
            {
                PendingLipStation cur = current.Count > 0
                    ? OrientStationUsingReference(data, stations[i], current[current.Count - 1], true)
                    : OrientStationUsingReference(data, stations[i], default, false);

                if (current.Count == 0)
                {
                    current.Add(cur);
                    continue;
                }

                PendingLipStation prev = current[current.Count - 1];
                float centerJump = Vector3.Distance(prev.seedCenterLocal, cur.seedCenterLocal);
                float endpointJump = ComputeStationEndpointContinuity(data, prev, cur);
                float strokeJump = Mathf.Abs(cur.strokeT - prev.strokeT);

                Vector3 prevDir = prev.splitDirLocal.sqrMagnitude > 1e-12f ? prev.splitDirLocal.normalized : Vector3.zero;
                Vector3 curDir = cur.splitDirLocal.sqrMagnitude > 1e-12f ? cur.splitDirLocal.normalized : Vector3.zero;
                float dirDot = (prevDir.sqrMagnitude > 1e-12f && curDir.sqrMagnitude > 1e-12f) ? Vector3.Dot(prevDir, curDir) : 1f;

                bool discontinuity =
                    centerJump > maxCenterJump ||
                    (!(float.IsNaN(endpointJump) || float.IsInfinity(endpointJump)) && endpointJump > maxEndpointJump) ||
                    strokeJump > maxStrokeJump ||
                    dirDot < -0.35f;

                if (discontinuity)
                {
                    float score = current.Count * 100f - currentScore;
                    if (best.Count == 0 || current.Count > best.Count || (current.Count == best.Count && score > bestScore))
                    {
                        best = new List<PendingLipStation>(current);
                        bestScore = score;
                    }

                    current.Clear();
                    currentScore = 0f;
                    current.Add(cur);
                    continue;
                }

                current.Add(cur);
                currentScore += centerJump;
                if (!(float.IsNaN(endpointJump) || float.IsInfinity(endpointJump)))
                    currentScore += endpointJump * 0.35f;
                currentScore += Mathf.Max(0f, strokeJump - 0.12f) * 10f;
            }

            if (current.Count > 0)
            {
                float score = current.Count * 100f - currentScore;
                if (best.Count == 0 || current.Count > best.Count || (current.Count == best.Count && score > bestScore))
                {
                    best = new List<PendingLipStation>(current);
                    bestScore = score;
                }
            }

            return best.Count > 0 ? best : new List<PendingLipStation>(stations);
        }

        private static float ComputeStationSpacing(PendingLipStation a, PendingLipStation b)
        {
            return Vector3.Distance(a.seedCenterLocal, b.seedCenterLocal);
        }


        private static bool TryResolveStationBoundaryFrame(
            PreciseIncisionMesh data,
            List<PendingLipStation> stations,
            int index,
            out int leftIndex,
            out int rightIndex,
            out Vector3 center,
            out Vector3 splitDir,
            out float gap)
        {
            leftIndex = -1;
            rightIndex = -1;
            center = Vector3.zero;
            splitDir = Vector3.right;
            gap = 0f;

            if (data == null || stations == null || index < 0 || index >= stations.Count)
                return false;

            var station = stations[index];
            if (station.leftLocalIndex < 0 || station.rightLocalIndex < 0 ||
                station.leftLocalIndex >= data.vertices.Count || station.rightLocalIndex >= data.vertices.Count ||
                station.leftLocalIndex == station.rightLocalIndex)
                return false;

            leftIndex = station.leftLocalIndex;
            rightIndex = station.rightLocalIndex;

            Vector3 left = data.vertices[leftIndex];
            Vector3 right = data.vertices[rightIndex];
            Vector3 pair = right - left;
            gap = pair.magnitude;
            center = 0.5f * (left + right);

            Vector3 rawDir = gap > 1e-8f
                ? (pair / gap)
                : (station.splitDirLocal.sqrMagnitude > 1e-12f ? station.splitDirLocal.normalized : Vector3.right);

            Vector3 tangent = ComputeStationChainTangent(stations, index);
            if (tangent.sqrMagnitude > 1e-12f)
            {
                tangent.Normalize();

                // 关键：把左右唇边方向从“沿边/沿三角局部的任意方向”
                // 重新约束成“与目标切口链大致垂直”的方向。
                // 若原始 pair 几乎平行于目标线，说明这条 station 很可能是误入的横穿边界，直接丢掉。
                float tangentDot = Mathf.Abs(Vector3.Dot(rawDir, tangent));
                Vector3 projected = Vector3.ProjectOnPlane(rawDir, tangent);
                if (projected.sqrMagnitude <= 1e-10f || tangentDot >= 0.72f)
                    return false;

                splitDir = projected.normalized;
            }
            else
            {
                splitDir = rawDir;
            }

            if (station.splitDirLocal.sqrMagnitude > 1e-12f && Vector3.Dot(splitDir, station.splitDirLocal) < 0f)
                splitDir = -splitDir;

            if (gap > 1e-8f)
            {
                Vector3 pairDir = pair / gap;
                if (Vector3.Dot(pairDir, splitDir) < 0f)
                {
                    int t = leftIndex; leftIndex = rightIndex; rightIndex = t;
                }
            }

            return true;
        }

        private static void ApplyStationBasedLipOffsets(PreciseIncisionMesh data, List<PendingLipStation> stations)
        {
            if (data == null || stations == null || stations.Count == 0)
                return;

            var offsetAccum = new Dictionary<int, Vector3>();
            var offsetCount = new Dictionary<int, int>();
            for (int i = 0; i < stations.Count; i++)
            {
                if (!TryResolveStationBoundaryFrame(data, stations, i, out int leftIndex, out int rightIndex, out _, out Vector3 splitDir, out _))
                    continue;

                float halfGap = Mathf.Max(0f, stations[i].targetOpen) * 0.5f;
                if (halfGap <= 1e-9f)
                    continue;

                AccumulateVertexOffset(offsetAccum, offsetCount, leftIndex, -splitDir * halfGap);
                AccumulateVertexOffset(offsetAccum, offsetCount, rightIndex, splitDir * halfGap);
            }

            foreach (var kv in offsetAccum)
            {
                int idx = kv.Key;
                if (idx < 0 || idx >= data.vertices.Count)
                    continue;
                int count = offsetCount.TryGetValue(idx, out int c) ? Mathf.Max(1, c) : 1;
                data.vertices[idx] += kv.Value / count;
            }
        }

        private static List<IncisionLipPair> BuildLipPairsFromStationChain(List<PendingLipStation> stations, PreciseIncisionMesh data)
        {
            var result = new List<IncisionLipPair>(stations != null ? stations.Count : 0);
            if (stations == null || data == null || data.vertices == null)
                return result;

            for (int i = 0; i < stations.Count; i++)
            {
                if (!TryResolveStationBoundaryFrame(data, stations, i, out int leftIndex, out int rightIndex, out Vector3 center, out Vector3 splitDir, out _))
                    continue;

                result.Add(new IncisionLipPair
                {
                    leftLocalIndex = leftIndex,
                    rightLocalIndex = rightIndex,
                    centerLocal = center,
                    splitDirLocal = splitDir,
                    targetOpen = stations[i].targetOpen,
                    weight = stations[i].weight,
                    strokeT = stations[i].strokeT,
                    layer = stations[i].layer,
                });
            }

            return result;
        }

        private static void SortAndCompactIncisionLipPairs(List<IncisionLipPair> lipPairs, float minSpacing, IncisionDebugCapture debugCapture = null)
        {
            if (lipPairs == null || lipPairs.Count <= 1)
            {
                if (lipPairs != null && debugCapture != null)
                {
                    for (int i = 0; i < lipPairs.Count; i++)
                        CapturePairDebugRecord(debugCapture, lipPairs[i], IncisionPairDebugKind.Compacted, IncisionPairRejectReason.None);
                }
                return;
            }

            for (int i = 0; i < lipPairs.Count; i++)
                CapturePairDebugRecord(debugCapture, lipPairs[i], IncisionPairDebugKind.Raw, IncisionPairRejectReason.None);

            lipPairs.Sort((x, y) =>
            {
                int cmp = x.strokeT.CompareTo(y.strokeT);
                if (cmp != 0) return cmp;
                return x.centerLocal.z.CompareTo(y.centerLocal.z);
            });

            float minSq = Mathf.Max(1e-10f, minSpacing * minSpacing);
            var compact = new List<IncisionLipPair>(lipPairs.Count);
            for (int i = 0; i < lipPairs.Count; i++)
            {
                var cur = lipPairs[i];
                if (compact.Count == 0)
                {
                    compact.Add(cur);
                    continue;
                }

                int last = compact.Count - 1;
                var prev = compact[last];
                bool closeInStroke = Mathf.Abs(cur.strokeT - prev.strokeT) <= 0.025f;
                bool closeInSpace = (cur.centerLocal - prev.centerLocal).sqrMagnitude <= minSq;
                if (closeInStroke && closeInSpace)
                {
                    CapturePairDebugRecord(debugCapture, cur, IncisionPairDebugKind.Rejected, IncisionPairRejectReason.TooClose);
                    bool takeCur = cur.targetOpen >= prev.targetOpen;
                    var keep = takeCur ? cur : prev;
                    keep.centerLocal = 0.5f * (prev.centerLocal + cur.centerLocal);
                    Vector3 mergedDir = prev.splitDirLocal + cur.splitDirLocal;
                    if (mergedDir.sqrMagnitude > 1e-12f)
                        keep.splitDirLocal = mergedDir.normalized;
                    keep.targetOpen = Mathf.Max(prev.targetOpen, cur.targetOpen);
                    keep.weight = Mathf.Max(prev.weight, cur.weight);
                    keep.strokeT = 0.5f * (prev.strokeT + cur.strokeT);
                    compact[last] = keep;
                    continue;
                }

                compact.Add(cur);
            }

            if (compact.Count >= 3)
            {
                var stepValues = new List<float>(compact.Count - 1);
                for (int i = 1; i < compact.Count; i++)
                {
                    float step = Vector3.Distance(compact[i - 1].centerLocal, compact[i].centerLocal);
                    if (step > 1e-6f && float.IsFinite(step))
                        stepValues.Add(step);
                }

                float medianStep = 0f;
                if (stepValues.Count > 0)
                {
                    stepValues.Sort();
                    medianStep = stepValues[stepValues.Count / 2];
                }

                var filtered = new List<IncisionLipPair>(compact.Count);
                filtered.Add(compact[0]);

                float maxStep = Mathf.Max(Mathf.Max(medianStep * 3.0f, minSpacing * 3.5f), 1e-4f);
                for (int i = 1; i < compact.Count; i++)
                {
                    var prev = filtered[filtered.Count - 1];
                    var cur = compact[i];
                    float step = Vector3.Distance(prev.centerLocal, cur.centerLocal);
                    if (step > maxStep)
                    {
                        CapturePairDebugRecord(debugCapture, cur, IncisionPairDebugKind.Rejected, IncisionPairRejectReason.JumpTooLarge);
                        continue;
                    }

                    if (Vector3.Dot(prev.splitDirLocal, cur.splitDirLocal) < 0f)
                        cur.splitDirLocal = -cur.splitDirLocal;

                    if (Vector3.Dot(prev.splitDirLocal, cur.splitDirLocal) < -0.35f)
                    {
                        CapturePairDebugRecord(debugCapture, cur, IncisionPairDebugKind.Rejected, IncisionPairRejectReason.ReverseDirection);
                        continue;
                    }

                    filtered.Add(cur);
                }

                compact = filtered;
            }

            lipPairs.Clear();
            lipPairs.AddRange(compact);
            for (int i = 0; i < lipPairs.Count; i++)
                CapturePairDebugRecord(debugCapture, lipPairs[i], IncisionPairDebugKind.Compacted, IncisionPairRejectReason.None);
        }

        private static List<PendingLipStation> CloneStationChain(List<PendingLipStation> stations)
        {
            var result = new List<PendingLipStation>(stations != null ? stations.Count : 0);
            if (stations == null)
                return result;

            for (int i = 0; i < stations.Count; i++)
                result.Add(stations[i]);
            return result;
        }

        private static List<Vector3> BuildCenterlineFromStationChain(List<PendingLipStation> stations, float maxJump = 0f)
        {
            var result = new List<Vector3>(stations != null ? stations.Count : 0);
            if (stations == null || stations.Count == 0)
                return result;

            float medianStep = 0f;
            if (stations.Count >= 2)
            {
                var steps = new List<float>(stations.Count - 1);
                for (int i = 1; i < stations.Count; i++)
                {
                    float step = Vector3.Distance(stations[i - 1].seedCenterLocal, stations[i].seedCenterLocal);
                    if (step > 1e-6f && float.IsFinite(step))
                        steps.Add(step);
                }

                if (steps.Count > 0)
                {
                    steps.Sort();
                    medianStep = steps[steps.Count / 2];
                }
            }

            float allowJump = maxJump > 0f ? maxJump : Mathf.Max(medianStep * 2.5f, 1e-4f);
            Vector3 prevPoint = Vector3.zero;
            Vector3 prevDir = Vector3.zero;
            bool hasPrev = false;
            bool hasPrevDir = false;

            for (int i = 0; i < stations.Count; i++)
            {
                Vector3 p = stations[i].seedCenterLocal;
                if (!hasPrev)
                {
                    result.Add(p);
                    prevPoint = p;
                    hasPrev = true;
                    continue;
                }

                Vector3 delta = p - prevPoint;
                float len = delta.magnitude;
                if (len <= 1e-10f)
                    continue;
                if (len > allowJump)
                    continue;

                Vector3 dir = delta / len;
                if (hasPrevDir && Vector3.Dot(prevDir, dir) < -0.2f)
                    continue;

                result.Add(p);
                prevPoint = p;
                prevDir = hasPrevDir ? Vector3.Slerp(prevDir, dir, 0.5f).normalized : dir;
                hasPrevDir = true;
            }

            return result;
        }

        private static List<Vector3> BuildCenterlineFromLipPairs(List<IncisionLipPair> lipPairs, float maxJump = 0f)
        {
            var result = new List<Vector3>(lipPairs != null ? lipPairs.Count : 0);
            if (lipPairs == null || lipPairs.Count == 0)
                return result;

            float medianStep = 0f;
            if (lipPairs.Count >= 2)
            {
                var steps = new List<float>(lipPairs.Count - 1);
                for (int i = 1; i < lipPairs.Count; i++)
                {
                    float step = Vector3.Distance(lipPairs[i - 1].centerLocal, lipPairs[i].centerLocal);
                    if (step > 1e-6f && float.IsFinite(step))
                        steps.Add(step);
                }

                if (steps.Count > 0)
                {
                    steps.Sort();
                    medianStep = steps[steps.Count / 2];
                }
            }

            float allowJump = maxJump > 0f ? maxJump : Mathf.Max(medianStep * 2.5f, 1e-4f);
            Vector3 prevPoint = Vector3.zero;
            Vector3 prevDir = Vector3.zero;
            bool hasPrev = false;
            bool hasPrevDir = false;

            for (int i = 0; i < lipPairs.Count; i++)
            {
                Vector3 p = lipPairs[i].centerLocal;
                if (!hasPrev)
                {
                    result.Add(p);
                    prevPoint = p;
                    hasPrev = true;
                    continue;
                }

                Vector3 delta = p - prevPoint;
                float len = delta.magnitude;
                if (len <= 1e-10f)
                    continue;
                if (len > allowJump)
                    continue;

                Vector3 dir = delta / len;
                if (hasPrevDir && Vector3.Dot(prevDir, dir) < -0.2f)
                    continue;

                result.Add(p);
                prevPoint = p;
                prevDir = hasPrevDir ? Vector3.Slerp(prevDir, dir, 0.5f).normalized : dir;
                hasPrevDir = true;
            }
            return result;
        }

        private static void RemoveDegenerateTriangles(PreciseIncisionMesh data, float minArea, float minEdgeLength, IncisionDebugCapture debugCapture = null, Transform meshTransform = null, int layer = 0)
        {
            if (data == null || data.triangles == null || data.triangles.Count < 3)
                return;

            float minAreaSq = Mathf.Max(1e-16f, minArea * minArea);
            float minEdgeSq = Mathf.Max(1e-16f, minEdgeLength * minEdgeLength);
            var kept = new List<int>(data.triangles.Count);
            for (int i = 0; i < data.triangles.Count; i += 3)
            {
                int a = data.triangles[i];
                int b = data.triangles[i + 1];
                int c = data.triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= data.vertices.Count || b >= data.vertices.Count || c >= data.vertices.Count)
                    continue;
                if (a == b || b == c || c == a)
                    continue;

                Vector3 va = data.vertices[a];
                Vector3 vb = data.vertices[b];
                Vector3 vc = data.vertices[c];
                if ((vb - va).sqrMagnitude <= minEdgeSq || (vc - vb).sqrMagnitude <= minEdgeSq || (va - vc).sqrMagnitude <= minEdgeSq)
                {
                    CaptureRemovedTriangle(debugCapture, meshTransform, va, vb, vc, IncisionPatchDebugKind.RemovedDegenerateEdge, layer, i / 3, a, b, c);
                    continue;
                }

                Vector3 cross = Vector3.Cross(vb - va, vc - va);
                if (cross.sqrMagnitude <= minAreaSq)
                {
                    CaptureRemovedTriangle(debugCapture, meshTransform, va, vb, vc, IncisionPatchDebugKind.RemovedDegenerateArea, layer, i / 3, a, b, c);
                    continue;
                }

                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }

            data.triangles.Clear();
            data.triangles.AddRange(kept);
        }
        private struct SlitRibbonFrame
        {
            public Vector3 center;
            public Vector3 left;
            public Vector3 right;
            public Vector3 splitDir;
            public float halfWidth;
            public float actualGap;
        }

        private static List<SlitRibbonFrame> BuildSlitRibbonFramesFromStations(PreciseIncisionMesh data, List<PendingLipStation> stations, float radius)
        {
            var frames = new List<SlitRibbonFrame>(stations != null ? stations.Count : 0);
            if (data == null || stations == null)
                return frames;

            float minHalfWidth = Mathf.Max(1e-5f, radius * 0.18f);

            bool hasPrevBoundary = false;
            Vector3 prevLeft = Vector3.zero;
            Vector3 prevRight = Vector3.zero;

            for (int i = 0; i < stations.Count; i++)
            {
                if (!TryResolveStationBoundaryFrame(data, stations, i, out int leftIndex, out int rightIndex, out Vector3 center, out Vector3 splitDir, out float gap))
                    continue;

                Vector3 left = data.vertices[leftIndex];
                Vector3 right = data.vertices[rightIndex];
                if (hasPrevBoundary)
                {
                    float sameCost = (left - prevLeft).sqrMagnitude + (right - prevRight).sqrMagnitude;
                    float swapCost = (left - prevRight).sqrMagnitude + (right - prevLeft).sqrMagnitude;
                    if (swapCost + 1e-10f < sameCost)
                    {
                        Vector3 tmp = left;
                        left = right;
                        right = tmp;
                        splitDir = -splitDir;
                    }
                }

                if (frames.Count > 0)
                {
                    var prevDir = frames[frames.Count - 1].splitDir;
                    if (prevDir.sqrMagnitude > 1e-12f && Vector3.Dot(prevDir, splitDir) < 0f)
                        splitDir = -splitDir;
                }

                float actualGap = gap;
                float halfWidth = Mathf.Max(minHalfWidth, actualGap * 0.5f);
                if (frames.Count > 0)
                {
                    var prev = frames[frames.Count - 1];
                    if ((prev.center - center).sqrMagnitude <= minHalfWidth * minHalfWidth * 0.04f)
                    {
                        prev.center = 0.5f * (prev.center + center);
                        prev.left = 0.5f * (prev.left + left);
                        prev.right = 0.5f * (prev.right + right);
                        Vector3 mergedDir = prev.splitDir + splitDir;
                        if (mergedDir.sqrMagnitude > 1e-12f)
                            prev.splitDir = mergedDir.normalized;
                        Vector3 mergedPair = prev.right - prev.left;
                        if (mergedPair.sqrMagnitude > 1e-12f)
                        {
                            prev.actualGap = mergedPair.magnitude;
                            prev.halfWidth = Mathf.Max(minHalfWidth, prev.actualGap * 0.5f);
                            prev.splitDir = mergedPair.normalized;
                        }
                        else
                        {
                            prev.actualGap = Mathf.Max(prev.actualGap, actualGap);
                            prev.halfWidth = Mathf.Max(prev.halfWidth, halfWidth);
                        }
                        frames[frames.Count - 1] = prev;

                        prevLeft = left;
                        prevRight = right;
                        hasPrevBoundary = true;
                        continue;
                    }
                }

                frames.Add(new SlitRibbonFrame
                {
                    center = center,
                    left = left,
                    right = right,
                    splitDir = splitDir,
                    halfWidth = halfWidth,
                    actualGap = actualGap,
                });

                prevLeft = left;
                prevRight = right;
                hasPrevBoundary = true;
            }

            return frames;
        }

        private static void CaptureSlitRibbonFrames(List<SlitRibbonFrame> frames, Transform meshTransform, int layer, List<IncisionDebugRibbonFrame> outFrames)
        {
            if (frames == null || outFrames == null || meshTransform == null)
                return;

            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                outFrames.Add(new IncisionDebugRibbonFrame
                {
                    center = meshTransform.TransformPoint(f.center),
                    left = meshTransform.TransformPoint(f.left),
                    right = meshTransform.TransformPoint(f.right),
                    layer = layer,
                    halfWidth = f.halfWidth,
                    actualGap = f.actualGap,
                });
            }
        }

        private static IncisionDeleteDebugSummary ExciseTrianglesInsideLeftRightBoundaryRegion(PreciseIncisionMesh data, List<PendingLipStation> stations, float boundaryPad, IncisionDebugCapture debugCapture = null, Transform meshTransform = null, int layer = 0, List<IncisionDebugRibbonFrame> ribbonFramesDebug = null, int pathCount = 0, int denseStationsCount = 0)
        {
            var summary = new IncisionDeleteDebugSummary
            {
                layer = layer,
                pathCount = pathCount,
                denseStationsCount = denseStationsCount,
                tinyAreaThreshold = 0f,
                ribbonPad = boundaryPad,
            };

            if (data == null || stations == null || stations.Count < 2)
                return summary;

            var frames = BuildSlitRibbonFramesFromStations(data, stations, Mathf.Max(boundaryPad, 1e-5f));
            summary.framesCount = frames != null ? frames.Count : 0;
            if (frames == null || frames.Count < 2)
                return summary;

            if (frames.Count > 0)
                CaptureSlitRibbonFrames(frames, meshTransform, layer, ribbonFramesDebug);

            var corridorQuads = BuildStationBoundaryQuads2D(data, stations);
            var centralBandQuads = BuildStationCentralBandQuads2D(corridorQuads, 0.16f, 0.84f);
            float ribbonQueryPad = Mathf.Max(1e-5f, boundaryPad * 0.35f);

            var kept = new List<int>(data.triangles.Count);
            var targetTraceRecords = (debugCapture != null && debugCapture.targetTraceEnabled)
                ? new List<StripTargetTraceRecord>(Mathf.Min(data.TriangleCount, 128))
                : null;

            for (int t = 0; t < data.triangles.Count; t += 3)
            {
                int a = data.triangles[t];
                int b = data.triangles[t + 1];
                int c = data.triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 ||
                    a >= data.vertices.Count || b >= data.vertices.Count || c >= data.vertices.Count)
                    continue;

                Vector3 va = data.vertices[a];
                Vector3 vb = data.vertices[b];
                Vector3 vc = data.vertices[c];
                Vector2 pa = data.projected2D[a];
                Vector2 pb = data.projected2D[b];
                Vector2 pc = data.projected2D[c];

                bool overlapCandidate = TriangleOverlapsSlitRibbon(va, vb, vc, frames, ribbonQueryPad);
                bool coreCandidate = overlapCandidate && TriangleInsideSlitRibbonCore(va, vb, vc, frames, ribbonQueryPad);
                bool stripInteriorCandidate = overlapCandidate && TriangleInsideSlitRibbonBand(va, vb, vc, frames, ribbonQueryPad);
                bool bridgeCandidate = overlapCandidate && TriangleBridgeCandidateStillInsideRibbon(va, vb, vc, frames, ribbonQueryPad);
                bool removeCandidate = stripInteriorCandidate || bridgeCandidate;

                if (overlapCandidate)
                {
                    summary.overlapCandidateCount++;
                    summary.corridorCandidateCount++;
                    CaptureRemovedTriangle(debugCapture, meshTransform, va, vb, vc, IncisionPatchDebugKind.CorridorCandidate, layer, t / 3, a, b, c);
                }

                if (coreCandidate)
                {
                    summary.coreCandidateCount++;
                    summary.middleBandCandidateCount++;
                    CaptureRemovedTriangle(debugCapture, meshTransform, va, vb, vc, IncisionPatchDebugKind.MiddleBandCandidate, layer, t / 3, a, b, c);
                }

                if (bridgeCandidate)
                {
                    summary.bridgeCandidateCount++;
                    CaptureRemovedTriangle(debugCapture, meshTransform, va, vb, vc, IncisionPatchDebugKind.BridgeCandidate, layer, t / 3, a, b, c);
                }

                if (stripInteriorCandidate)
                {
                    summary.stripInteriorCandidateCount++;
                    CaptureRemovedTriangle(debugCapture, meshTransform, va, vb, vc, IncisionPatchDebugKind.StripInteriorCandidate, layer, t / 3, a, b, c);
                }

                if (targetTraceRecords != null && overlapCandidate)
                {
                    EstimateTriangleStripCoverageHits2D(data, a, b, c, corridorQuads, centralBandQuads, out int corridorHits, out int centralHits);
                    Vector2 center2D = (pa + pb + pc) / 3f;
                    targetTraceRecords.Add(new StripTargetTraceRecord
                    {
                        triIndex = t / 3,
                        ia = a,
                        ib = b,
                        ic = c,
                        va = va,
                        vb = vb,
                        vc = vc,
                        centerWorld = meshTransform != null ? meshTransform.TransformPoint((va + vb + vc) / 3f) : (va + vb + vc) / 3f,
                        center2D = center2D,
                        area2D = Mathf.Abs(Cross2D(pb - pa, pc - pa)) * 0.5f,
                        centerlineDistance2D = Mathf.Sqrt(DistancePointStripCenterlineSq2D(center2D, corridorQuads)),
                        corridorHits = corridorHits,
                        centralHits = centralHits,
                        overlapCandidate = overlapCandidate,
                        strictCandidate = false,
                        coreCandidate = coreCandidate,
                        corridorCandidate = overlapCandidate,
                        bridgeCandidate = bridgeCandidate,
                        middleBandCandidate = coreCandidate,
                        stripInteriorCandidate = stripInteriorCandidate,
                        removedCandidate = removeCandidate,
                    });
                }

                if (removeCandidate)
                {
                    summary.removedCount++;
                    CaptureRemovedTriangle(debugCapture, meshTransform, va, vb, vc, IncisionPatchDebugKind.RemovedStripInterior, layer, t / 3, a, b, c);
                    continue;
                }

                summary.keptCount++;
                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }

            data.triangles.Clear();
            data.triangles.AddRange(kept);

            if (targetTraceRecords != null && targetTraceRecords.Count > 0)
                FinalizeStripTargetTrace(data, corridorQuads, targetTraceRecords, debugCapture, meshTransform, layer);

            return summary;
        }

        private static bool TriangleInsideSlitRibbonBand(Vector3 a, Vector3 b, Vector3 c, List<SlitRibbonFrame> frames, float extraHalfWidth)
        {
            if (frames == null || frames.Count < 2)
                return false;

            float bandPad = Mathf.Max(1e-6f, extraHalfWidth * 0.10f);
            Vector3 centroid = (a + b + c) / 3f;
            if (!PointInsideSlitRibbon(centroid, frames, bandPad))
                return false;

            int insideCount = 1; // centroid
            if (PointInsideSlitRibbon(a, frames, bandPad)) insideCount++;
            if (PointInsideSlitRibbon(b, frames, bandPad)) insideCount++;
            if (PointInsideSlitRibbon(c, frames, bandPad)) insideCount++;
            if (PointInsideSlitRibbon(0.5f * (a + b), frames, bandPad)) insideCount++;
            if (PointInsideSlitRibbon(0.5f * (b + c), frames, bandPad)) insideCount++;
            if (PointInsideSlitRibbon(0.5f * (c + a), frames, bandPad)) insideCount++;

            return insideCount >= 4;
        }

        private static bool TriangleInsideSlitRibbonCore(Vector3 a, Vector3 b, Vector3 c, List<SlitRibbonFrame> frames, float extraHalfWidth)
        {
            if (frames == null || frames.Count < 2)
                return false;

            float queryPad = Mathf.Max(1e-6f, extraHalfWidth * 0.22f);
            Vector3 centroid = (a + b + c) / 3f;
            Vector3 abMid = 0.5f * (a + b);
            Vector3 bcMid = 0.5f * (b + c);
            Vector3 caMid = 0.5f * (c + a);

            if (!TrySampleSlitRibbon(centroid, frames, queryPad, out float centroidSigned, out float centroidHalfWidth))
                return false;

            if (Mathf.Abs(centroidSigned) > centroidHalfWidth * 0.42f)
                return false;

            int insideCount = 0;
            int coreCount = 0;
            float normalizedSum = 0f;

            AccumulateSlitRibbonCoreSample(a, frames, queryPad, ref insideCount, ref coreCount, ref normalizedSum);
            AccumulateSlitRibbonCoreSample(b, frames, queryPad, ref insideCount, ref coreCount, ref normalizedSum);
            AccumulateSlitRibbonCoreSample(c, frames, queryPad, ref insideCount, ref coreCount, ref normalizedSum);
            AccumulateSlitRibbonCoreSample(abMid, frames, queryPad, ref insideCount, ref coreCount, ref normalizedSum);
            AccumulateSlitRibbonCoreSample(bcMid, frames, queryPad, ref insideCount, ref coreCount, ref normalizedSum);
            AccumulateSlitRibbonCoreSample(caMid, frames, queryPad, ref insideCount, ref coreCount, ref normalizedSum);

            if (insideCount < 4 || coreCount < 3)
                return false;

            float avgNormalized = normalizedSum / Mathf.Max(1, insideCount);
            if (avgNormalized > 0.52f)
                return false;

            return true;
        }

        private static void AccumulateSlitRibbonCoreSample(Vector3 point, List<SlitRibbonFrame> frames, float extraHalfWidth, ref int insideCount, ref int coreCount, ref float normalizedSum)
        {
            if (!TrySampleSlitRibbon(point, frames, extraHalfWidth, out float signedLateral, out float sampleHalfWidth))
                return;

            insideCount++;
            float normalized = Mathf.Abs(signedLateral) / Mathf.Max(1e-6f, sampleHalfWidth);
            normalizedSum += normalized;
            if (normalized <= 0.38f)
                coreCount++;
        }

        private static bool TriangleBridgeCandidateStillInsideRibbon(Vector3 a, Vector3 b, Vector3 c, List<SlitRibbonFrame> frames, float extraHalfWidth)
        {
            if (frames == null || frames.Count < 2)
                return false;

            float queryPad = Mathf.Max(1e-6f, extraHalfWidth * 0.12f);
            Vector3 centroid = (a + b + c) / 3f;
            if (!TrySampleSlitRibbon(centroid, frames, queryPad, out float centroidSigned, out float centroidHalfWidth))
                return false;

            float centroidNormalized = Mathf.Abs(centroidSigned) / Mathf.Max(1e-6f, centroidHalfWidth);
            if (centroidNormalized > 0.78f)
                return false;

            int insideCount = 0;
            int interiorCount = 0;
            float normalizedSum = 0f;

            AccumulateSlitRibbonBridgeInteriorSample(centroid, frames, queryPad, ref insideCount, ref interiorCount, ref normalizedSum);
            AccumulateSlitRibbonBridgeInteriorSample(a, frames, queryPad, ref insideCount, ref interiorCount, ref normalizedSum);
            AccumulateSlitRibbonBridgeInteriorSample(b, frames, queryPad, ref insideCount, ref interiorCount, ref normalizedSum);
            AccumulateSlitRibbonBridgeInteriorSample(c, frames, queryPad, ref insideCount, ref interiorCount, ref normalizedSum);
            AccumulateSlitRibbonBridgeInteriorSample(0.5f * (a + b), frames, queryPad, ref insideCount, ref interiorCount, ref normalizedSum);
            AccumulateSlitRibbonBridgeInteriorSample(0.5f * (b + c), frames, queryPad, ref insideCount, ref interiorCount, ref normalizedSum);
            AccumulateSlitRibbonBridgeInteriorSample(0.5f * (c + a), frames, queryPad, ref insideCount, ref interiorCount, ref normalizedSum);

            if (insideCount < 4 || interiorCount < 2)
                return false;

            float avgNormalized = normalizedSum / Mathf.Max(1, insideCount);
            return avgNormalized <= 0.72f;
        }

        private static void AccumulateSlitRibbonBridgeInteriorSample(Vector3 point, List<SlitRibbonFrame> frames, float extraHalfWidth, ref int insideCount, ref int interiorCount, ref float normalizedSum)
        {
            if (!TrySampleSlitRibbon(point, frames, extraHalfWidth, out float signedLateral, out float sampleHalfWidth))
                return;

            insideCount++;
            float normalized = Mathf.Abs(signedLateral) / Mathf.Max(1e-6f, sampleHalfWidth);
            normalizedSum += normalized;
            if (normalized <= 0.62f)
                interiorCount++;
        }

        private static void ExciseResidualBridgeTriangles(PreciseIncisionMesh data, List<PendingLipStation> stations, float radius, IncisionDebugCapture debugCapture = null, Transform meshTransform = null, int layer = 0)
        {
            if (data == null || stations == null || stations.Count < 2)
                return;

            var regionPolygon = BuildStationBoundaryRegionPolygon2D(data, stations);
            if (regionPolygon == null || regionPolygon.Count < 3)
                return;

            var kept = new List<int>(data.triangles.Count);
            for (int t = 0; t < data.triangles.Count; t += 3)
            {
                int a = data.triangles[t];
                int b = data.triangles[t + 1];
                int c = data.triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= data.vertices.Count || b >= data.vertices.Count || c >= data.vertices.Count)
                    continue;

                Vector3 va = data.vertices[a];
                Vector3 vb = data.vertices[b];
                Vector3 vc = data.vertices[c];
                bool insideBoundaryRegion = TriangleInsideBoundaryRegionTopological2D(data, a, b, c, regionPolygon);
                if (insideBoundaryRegion)
                {
                    CaptureRemovedTriangle(debugCapture, meshTransform, va, vb, vc, IncisionPatchDebugKind.RemovedBridge, layer, t / 3, a, b, c);
                    continue;
                }

                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }

            data.triangles.Clear();
            data.triangles.AddRange(kept);
        }


        private static bool TriangleMatchesSlitBoundaryBridgeRuleB(int ia, int ib, int ic, Vector3 va, Vector3 vb, Vector3 vc, List<Vector3> vertices, List<PendingLipStation> stations, List<SlitRibbonFrame> frames, float extraHalfWidth)
        {
            if (stations == null || stations.Count == 0 || vertices == null)
                return false;

            if (!TryGetTriangleOtherVertexForAnyStationCrossbar(ia, ib, ic, va, vb, vc, stations, out int otherIndex, out Vector3 otherVertex))
                return false;

            if (IsSlitBoundaryVertexIndex(otherIndex, stations))
                return true;

            float insidePad = Mathf.Max(1e-6f, extraHalfWidth * 0.45f);
            if (PointInsideSlitRibbon(otherVertex, frames, insidePad))
                return true;

            float nearBoundaryTol = Mathf.Max(1e-5f, extraHalfWidth * 1.6f);
            if (IsPointNearAnySlitBoundaryVertex(otherVertex, vertices, stations, nearBoundaryTol))
                return true;

            if (TrySampleSlitRibbon(otherVertex, frames, insidePad, out float signedLateral, out float halfWidth))
            {
                float boundaryBand = Mathf.Max(nearBoundaryTol, halfWidth * 0.38f);
                if (Mathf.Abs(Mathf.Abs(signedLateral) - halfWidth) <= boundaryBand)
                    return true;
            }

            return false;
        }

        private static bool TryGetTriangleOtherVertexForAnyStationCrossbar(int ia, int ib, int ic, Vector3 va, Vector3 vb, Vector3 vc, List<PendingLipStation> stations, out int otherIndex, out Vector3 otherVertex)
        {
            otherIndex = -1;
            otherVertex = default;
            if (stations == null)
                return false;

            for (int i = 0; i < stations.Count; i++)
            {
                int leftIndex = stations[i].leftLocalIndex;
                int rightIndex = stations[i].rightLocalIndex;
                if (leftIndex < 0 || rightIndex < 0)
                    continue;

                if ((ia == leftIndex && ib == rightIndex) || (ia == rightIndex && ib == leftIndex))
                {
                    otherIndex = ic;
                    otherVertex = vc;
                    return true;
                }

                if ((ib == leftIndex && ic == rightIndex) || (ib == rightIndex && ic == leftIndex))
                {
                    otherIndex = ia;
                    otherVertex = va;
                    return true;
                }

                if ((ic == leftIndex && ia == rightIndex) || (ic == rightIndex && ia == leftIndex))
                {
                    otherIndex = ib;
                    otherVertex = vb;
                    return true;
                }
            }

            return false;
        }

        private static bool IsSlitBoundaryVertexIndex(int index, List<PendingLipStation> stations)
        {
            if (index < 0 || stations == null)
                return false;

            for (int i = 0; i < stations.Count; i++)
            {
                if (stations[i].leftLocalIndex == index || stations[i].rightLocalIndex == index)
                    return true;
            }

            return false;
        }

        private static bool IsPointNearAnySlitBoundaryVertex(Vector3 point, List<Vector3> vertices, List<PendingLipStation> stations, float tolerance)
        {
            if (vertices == null || stations == null)
                return false;

            float tolSq = tolerance * tolerance;
            for (int i = 0; i < stations.Count; i++)
            {
                int leftIndex = stations[i].leftLocalIndex;
                if (leftIndex >= 0 && leftIndex < vertices.Count)
                {
                    if ((vertices[leftIndex] - point).sqrMagnitude <= tolSq)
                        return true;
                }

                int rightIndex = stations[i].rightLocalIndex;
                if (rightIndex >= 0 && rightIndex < vertices.Count)
                {
                    if ((vertices[rightIndex] - point).sqrMagnitude <= tolSq)
                        return true;
                }
            }

            return false;
        }

        private static bool TriangleBridgesSlitRibbonConservative(
            PreciseIncisionMesh data,
            int ia,
            int ib,
            int ic,
            List<PendingLipStation> stations,
            List<StationBoundaryQuad2D> corridorQuads,
            List<SlitRibbonFrame> frames,
            float extraHalfWidth)
        {
            if (data == null || stations == null || stations.Count == 0 || frames == null || frames.Count < 2)
                return false;

            if (ia < 0 || ib < 0 || ic < 0 || ia >= data.vertices.Count || ib >= data.vertices.Count || ic >= data.vertices.Count)
                return false;

            if (!TryGetTriangleStationCrossbarInfo(
                    data, ia, ib, ic, stations,
                    out int stationIndex,
                    out int crossbarAIndex,
                    out int crossbarBIndex,
                    out int otherIndex,
                    out Vector3 crossbarA,
                    out Vector3 crossbarB,
                    out Vector3 otherVertex,
                    out Vector3 splitDir,
                    out Vector3 tangent))
                return false;

            Vector3 crossbar = crossbarB - crossbarA;
            float crossbarLen = crossbar.magnitude;
            if (crossbarLen <= 1e-8f)
                return false;

            Vector3 crossbarDir = crossbar / crossbarLen;

            if (splitDir.sqrMagnitude > 1e-12f)
            {
                splitDir.Normalize();
                if (Mathf.Abs(Vector3.Dot(crossbarDir, splitDir)) < 0.72f)
                    return false;
            }

            if (tangent.sqrMagnitude > 1e-12f)
            {
                tangent.Normalize();
                if (Mathf.Abs(Vector3.Dot(crossbarDir, tangent)) > 0.45f)
                    return false;
            }

            Vector2 crossbarMid2D = 0.5f * (data.projected2D[crossbarAIndex] + data.projected2D[crossbarBIndex]);
            if (corridorQuads != null && corridorQuads.Count > 0 && !PointInAnyBoundaryQuad2D(crossbarMid2D, corridorQuads))
                return false;

            float midPad = Mathf.Max(1e-6f, extraHalfWidth * 0.18f);
            if (!TrySampleSlitRibbon(0.5f * (crossbarA + crossbarB), frames, midPad, out float midSigned, out float midHalfWidth))
                return false;

            float midNormalized = Mathf.Abs(midSigned) / Mathf.Max(1e-6f, midHalfWidth);
            if (midNormalized > 0.35f)
                return false;

            if (!TriangleMatchesSlitBoundaryBridgeRuleB(ia, ib, ic, data.vertices[ia], data.vertices[ib], data.vertices[ic], data.vertices, stations, frames, extraHalfWidth))
                return false;

            if (otherIndex >= 0 && IsSlitBoundaryVertexIndex(otherIndex, stations))
                return true;

            float otherPad = Mathf.Max(1e-6f, extraHalfWidth * 0.45f);
            if (TrySampleSlitRibbon(otherVertex, frames, otherPad, out float otherSigned, out float otherHalfWidth))
            {
                float otherNormalized = Mathf.Abs(otherSigned) / Mathf.Max(1e-6f, otherHalfWidth);
                if (otherNormalized <= 1.05f)
                    return true;
            }

            float nearBoundaryTol = Mathf.Max(1e-5f, extraHalfWidth * 1.4f);
            return IsPointNearAnySlitBoundaryVertex(otherVertex, data.vertices, stations, nearBoundaryTol);
        }

        private static bool TryGetTriangleStationCrossbarInfo(
            PreciseIncisionMesh data,
            int ia,
            int ib,
            int ic,
            List<PendingLipStation> stations,
            out int stationIndex,
            out int crossbarAIndex,
            out int crossbarBIndex,
            out int otherIndex,
            out Vector3 crossbarA,
            out Vector3 crossbarB,
            out Vector3 otherVertex,
            out Vector3 splitDir,
            out Vector3 tangent)
        {
            stationIndex = -1;
            crossbarAIndex = -1;
            crossbarBIndex = -1;
            otherIndex = -1;
            crossbarA = Vector3.zero;
            crossbarB = Vector3.zero;
            otherVertex = Vector3.zero;
            splitDir = Vector3.right;
            tangent = Vector3.zero;

            if (data == null || stations == null)
                return false;

            for (int i = 0; i < stations.Count; i++)
            {
                if (!TryResolveStationBoundaryFrame(data, stations, i, out int leftIndex, out int rightIndex, out _, out Vector3 resolvedSplitDir, out _))
                    continue;

                if ((ia == leftIndex && ib == rightIndex) || (ia == rightIndex && ib == leftIndex))
                {
                    stationIndex = i;
                    crossbarAIndex = ia;
                    crossbarBIndex = ib;
                    otherIndex = ic;
                    crossbarA = data.vertices[ia];
                    crossbarB = data.vertices[ib];
                    otherVertex = data.vertices[ic];
                    splitDir = resolvedSplitDir;
                    tangent = ComputeStationChainTangent(stations, i);
                    return true;
                }

                if ((ib == leftIndex && ic == rightIndex) || (ib == rightIndex && ic == leftIndex))
                {
                    stationIndex = i;
                    crossbarAIndex = ib;
                    crossbarBIndex = ic;
                    otherIndex = ia;
                    crossbarA = data.vertices[ib];
                    crossbarB = data.vertices[ic];
                    otherVertex = data.vertices[ia];
                    splitDir = resolvedSplitDir;
                    tangent = ComputeStationChainTangent(stations, i);
                    return true;
                }

                if ((ic == leftIndex && ia == rightIndex) || (ic == rightIndex && ia == leftIndex))
                {
                    stationIndex = i;
                    crossbarAIndex = ic;
                    crossbarBIndex = ia;
                    otherIndex = ib;
                    crossbarA = data.vertices[ic];
                    crossbarB = data.vertices[ia];
                    otherVertex = data.vertices[ib];
                    splitDir = resolvedSplitDir;
                    tangent = ComputeStationChainTangent(stations, i);
                    return true;
                }
            }

            return false;
        }

        private static bool TriangleInsideSlitRibbonStrict(Vector3 a, Vector3 b, Vector3 c, List<SlitRibbonFrame> frames, float extraHalfWidth)
        {
            if (frames == null || frames.Count < 2)
                return false;

            float queryPad = Mathf.Max(1e-6f, extraHalfWidth * 0.08f);
            Vector3 centroid = (a + b + c) / 3f;
            Vector3 abMid = 0.5f * (a + b);
            Vector3 bcMid = 0.5f * (b + c);
            Vector3 caMid = 0.5f * (c + a);

            if (!TrySampleSlitRibbon(centroid, frames, queryPad, out float centroidSigned, out float centroidHalfWidth))
                return false;

            int insideCount = 0;
            float minSigned = centroidSigned;
            float maxSigned = centroidSigned;

            AccumulateSlitRibbonSample(a, frames, queryPad, ref insideCount, ref minSigned, ref maxSigned);
            AccumulateSlitRibbonSample(b, frames, queryPad, ref insideCount, ref minSigned, ref maxSigned);
            AccumulateSlitRibbonSample(c, frames, queryPad, ref insideCount, ref minSigned, ref maxSigned);
            AccumulateSlitRibbonSample(abMid, frames, queryPad, ref insideCount, ref minSigned, ref maxSigned);
            AccumulateSlitRibbonSample(bcMid, frames, queryPad, ref insideCount, ref minSigned, ref maxSigned);
            AccumulateSlitRibbonSample(caMid, frames, queryPad, ref insideCount, ref minSigned, ref maxSigned);

            float spanThreshold = Mathf.Max(1e-5f, centroidHalfWidth * 0.22f);
            bool spansAcrossSlit = minSigned <= -spanThreshold && maxSigned >= spanThreshold;
            if (!spansAcrossSlit)
                return false;

            return insideCount >= 3;
        }

        private static void AccumulateSlitRibbonSample(Vector3 point, List<SlitRibbonFrame> frames, float extraHalfWidth, ref int insideCount, ref float minSigned, ref float maxSigned)
        {
            float sampleHalfWidth;
            if (TrySampleSlitRibbon(point, frames, extraHalfWidth, out float signedLateral, out sampleHalfWidth))
            {
                insideCount++;
                if (signedLateral < minSigned)
                    minSigned = signedLateral;
                if (signedLateral > maxSigned)
                    maxSigned = signedLateral;
            }
        }

        private static bool TrySampleSlitRibbonDetailed(Vector3 point, List<SlitRibbonFrame> frames, float extraHalfWidth, out float signedLateral, out float halfWidth, out int nearestSegmentIndex, out Vector3 nearestCenter, out Vector3 nearestTangent, out Vector3 nearestNormal, out float nearestGap, out float nearestSurfaceOffset)
        {
            signedLateral = 0f;
            halfWidth = 0f;
            nearestSegmentIndex = -1;
            nearestCenter = Vector3.zero;
            nearestTangent = Vector3.zero;
            nearestNormal = Vector3.zero;
            nearestGap = 0f;
            nearestSurfaceOffset = 0f;
            if (frames == null || frames.Count < 2)
                return false;

            bool found = false;
            float bestScore = float.PositiveInfinity;
            float extra = Mathf.Max(0f, extraHalfWidth);

            for (int i = 0; i < frames.Count - 1; i++)
            {
                Vector3 c0 = frames[i].center;
                Vector3 c1 = frames[i + 1].center;
                Vector3 seg = c1 - c0;
                float segLenSq = seg.sqrMagnitude;
                if (segLenSq <= 1e-12f)
                    continue;

                float u = Mathf.Clamp01(Vector3.Dot(point - c0, seg) / segLenSq);
                Vector3 center = Vector3.Lerp(frames[i].center, frames[i + 1].center, u);
                Vector3 left = Vector3.Lerp(frames[i].left, frames[i + 1].left, u);
                Vector3 right = Vector3.Lerp(frames[i].right, frames[i + 1].right, u);

                Vector3 tangent = seg.normalized;
                Vector3 pair = right - left;
                float localGap = pair.magnitude;
                Vector3 split = localGap > 1e-12f ? (pair / localGap) : Vector3.Slerp(frames[i].splitDir, frames[i + 1].splitDir, u);
                if (split.sqrMagnitude <= 1e-12f)
                    split = frames[i].splitDir.sqrMagnitude > 1e-12f ? frames[i].splitDir : Vector3.right;
                split.Normalize();

                Vector3 normal = Vector3.Cross(tangent, split);
                if (normal.sqrMagnitude <= 1e-12f)
                    normal = Vector3.Cross(split, Vector3.up);
                if (normal.sqrMagnitude <= 1e-12f)
                    normal = Vector3.forward;
                normal.Normalize();

                float localHalfWidth = Mathf.Max(1e-5f, localGap * 0.5f + extra);
                float lateral = Vector3.Dot(point - center, split);
                float lateralAbs = Mathf.Abs(lateral);
                if (lateralAbs > localHalfWidth)
                    continue;

                float outOfSurface = Mathf.Abs(Vector3.Dot(point - center, normal));
                float surfaceTol = Mathf.Max(extra * 1.1f, Mathf.Max(localGap * 0.35f, localHalfWidth * 0.55f));
                if (outOfSurface > surfaceTol)
                    continue;

                float score = lateralAbs + outOfSurface * 0.35f;
                if (!found || score < bestScore)
                {
                    found = true;
                    bestScore = score;
                    signedLateral = lateral;
                    halfWidth = localHalfWidth;
                    nearestSegmentIndex = i;
                    nearestCenter = center;
                    nearestTangent = tangent;
                    nearestNormal = normal;
                    nearestGap = localGap;
                    nearestSurfaceOffset = outOfSurface;
                }
            }

            return found;
        }

        private static bool TrySampleSlitRibbon(Vector3 point, List<SlitRibbonFrame> frames, float extraHalfWidth, out float signedLateral, out float halfWidth)
        {
            return TrySampleSlitRibbonDetailed(point, frames, extraHalfWidth, out signedLateral, out halfWidth, out _, out _, out _, out _, out _, out _);
        }

        private static bool TriangleCentroidInsideSlitRibbon(Vector3 a, Vector3 b, Vector3 c, List<SlitRibbonFrame> frames, float extraHalfWidth)
        {
            if (frames == null || frames.Count < 2)
                return false;

            Vector3 centroid = (a + b + c) / 3f;
            return PointInsideSlitRibbon(centroid, frames, extraHalfWidth);
        }

        private static bool TriangleOverlapsSlitRibbon(Vector3 a, Vector3 b, Vector3 c, List<SlitRibbonFrame> frames, float extraHalfWidth)
        {
            Vector3 centroid = (a + b + c) / 3f;
            if (PointInsideSlitRibbon(centroid, frames, extraHalfWidth))
                return true;

            if (PointInsideSlitRibbon(a, frames, extraHalfWidth) ||
                PointInsideSlitRibbon(b, frames, extraHalfWidth) ||
                PointInsideSlitRibbon(c, frames, extraHalfWidth))
                return true;

            Vector3 abMid = 0.5f * (a + b);
            Vector3 bcMid = 0.5f * (b + c);
            Vector3 caMid = 0.5f * (c + a);
            if (PointInsideSlitRibbon(abMid, frames, extraHalfWidth) ||
                PointInsideSlitRibbon(bcMid, frames, extraHalfWidth) ||
                PointInsideSlitRibbon(caMid, frames, extraHalfWidth))
                return true;

            float loose = Mathf.Max(extraHalfWidth, 1e-5f);
            for (int i = 0; i < frames.Count - 1; i++)
            {
                Vector3 c0 = frames[i].center;
                Vector3 c1 = frames[i + 1].center;
                if (DistanceSegmentSegmentSq(c0, c1, a, b) <= loose * loose ||
                    DistanceSegmentSegmentSq(c0, c1, b, c) <= loose * loose ||
                    DistanceSegmentSegmentSq(c0, c1, c, a) <= loose * loose)
                    return true;
            }

            return false;
        }

        private static bool PointInsideSlitRibbon(Vector3 point, List<SlitRibbonFrame> frames, float extraHalfWidth)
        {
            if (frames == null || frames.Count < 2)
                return false;

            float extra = Mathf.Max(0f, extraHalfWidth);
            for (int i = 0; i < frames.Count - 1; i++)
            {
                Vector3 c0 = frames[i].center;
                Vector3 c1 = frames[i + 1].center;
                Vector3 seg = c1 - c0;
                float segLenSq = seg.sqrMagnitude;
                if (segLenSq <= 1e-12f)
                    continue;

                float u = Mathf.Clamp01(Vector3.Dot(point - c0, seg) / segLenSq);
                Vector3 center = Vector3.Lerp(frames[i].center, frames[i + 1].center, u);
                Vector3 left = Vector3.Lerp(frames[i].left, frames[i + 1].left, u);
                Vector3 right = Vector3.Lerp(frames[i].right, frames[i + 1].right, u);
                Vector3 tangent = seg.normalized;

                Vector3 pair = right - left;
                float localGap = pair.magnitude;
                Vector3 split = localGap > 1e-12f ? (pair / localGap) : Vector3.Slerp(frames[i].splitDir, frames[i + 1].splitDir, u);
                if (split.sqrMagnitude <= 1e-12f)
                    split = frames[i].splitDir.sqrMagnitude > 1e-12f ? frames[i].splitDir : Vector3.right;
                split.Normalize();

                Vector3 normal = Vector3.Cross(tangent, split);
                if (normal.sqrMagnitude <= 1e-12f)
                    normal = Vector3.Cross(split, Vector3.up);
                if (normal.sqrMagnitude <= 1e-12f)
                    normal = Vector3.forward;
                normal.Normalize();

                float localHalfWidth = Mathf.Max(1e-5f, localGap * 0.5f + extra);
                float lateral = Mathf.Abs(Vector3.Dot(point - center, split));
                if (lateral > localHalfWidth)
                    continue;

                float outOfSurface = Mathf.Abs(Vector3.Dot(point - center, normal));
                float surfaceTol = Mathf.Max(extra * 1.5f, Mathf.Max(localGap * 0.4f, localHalfWidth * 0.65f));
                if (outOfSurface <= surfaceTol)
                    return true;
            }

            return false;
        }

        private static void ExciseTrianglesNearIncisionCenterline(PreciseIncisionMesh data, List<Vector3> centerlineLocal, float radius)
        {
            if (data == null || centerlineLocal == null || centerlineLocal.Count < 2 || radius <= 1e-6f)
                return;

            float radiusSq = radius * radius;
            var kept = new List<int>(data.triangles.Count);
            for (int t = 0; t < data.triangles.Count; t += 3)
            {
                int a = data.triangles[t];
                int b = data.triangles[t + 1];
                int c = data.triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= data.vertices.Count || b >= data.vertices.Count || c >= data.vertices.Count)
                    continue;

                Vector3 va = data.vertices[a];
                Vector3 vb = data.vertices[b];
                Vector3 vc = data.vertices[c];
                if (TriangleOverlapsPolylineBand(va, vb, vc, centerlineLocal, radiusSq))
                    continue;

                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }

            data.triangles.Clear();
            data.triangles.AddRange(kept);
        }

        private static bool TriangleOverlapsPolylineBand(Vector3 a, Vector3 b, Vector3 c, List<Vector3> polyline, float radiusSq)
        {
            Vector3 centroid = (a + b + c) / 3f;
            if (DistancePointPolylineSq(centroid, polyline) <= radiusSq)
                return true;

            if (DistancePointPolylineSq(a, polyline) <= radiusSq ||
                DistancePointPolylineSq(b, polyline) <= radiusSq ||
                DistancePointPolylineSq(c, polyline) <= radiusSq)
                return true;

            Vector3 abMid = 0.5f * (a + b);
            Vector3 bcMid = 0.5f * (b + c);
            Vector3 caMid = 0.5f * (c + a);
            if (DistancePointPolylineSq(abMid, polyline) <= radiusSq ||
                DistancePointPolylineSq(bcMid, polyline) <= radiusSq ||
                DistancePointPolylineSq(caMid, polyline) <= radiusSq)
                return true;

            float triEdgeMaxSq = Mathf.Max((b - a).sqrMagnitude, Mathf.Max((c - b).sqrMagnitude, (a - c).sqrMagnitude));
            float looseSq = radiusSq + triEdgeMaxSq * 0.08f;
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 p0 = polyline[i];
                Vector3 p1 = polyline[i + 1];
                if (DistanceSegmentSegmentSq(p0, p1, a, b) <= looseSq ||
                    DistanceSegmentSegmentSq(p0, p1, b, c) <= looseSq ||
                    DistanceSegmentSegmentSq(p0, p1, c, a) <= looseSq)
                    return true;
            }

            return false;
        }

        private static float DistancePointPolylineSq(Vector3 point, List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count == 0)
                return float.PositiveInfinity;
            if (polyline.Count == 1)
                return (point - polyline[0]).sqrMagnitude;

            float best = float.PositiveInfinity;
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                best = Mathf.Min(best, DistancePointSegmentSq(point, polyline[i], polyline[i + 1]));
            }
            return best;
        }

        private static float DistancePointSegmentSq(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float denom = ab.sqrMagnitude;
            if (denom <= 1e-12f)
                return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / denom);
            Vector3 q = a + ab * t;
            return (p - q).sqrMagnitude;
        }


        private static float ComputePolylineTargetOpenWidth(float t01, float openWidthWorld, float profileVariation, float curvatureWidthScale)
        {
            float t = Mathf.Clamp01(t01);
            float envelope = Mathf.Sin(t * Mathf.PI);
            float profile = 1f + profileVariation * (envelope - 0.5f);
            float curvature = 1f + 0.15f * Mathf.Clamp(curvatureWidthScale, -1f, 2f) * envelope;
            return Mathf.Max(0f, openWidthWorld * profile * curvature);
        }

        private sealed class PreciseIncisionMesh
        {
            public readonly List<Vector3> vertices;
            public readonly List<int> triangles;
            public readonly List<Vector2> projected2D;
            public readonly bool hasUV;
            public readonly bool hasTangents;
            public readonly bool hasColors;
            public readonly List<Vector2> uv;
            public readonly List<Vector4> tangents;
            public readonly List<Color> colors;

            private PreciseIncisionMesh(
                List<Vector3> vertices,
                List<int> triangles,
                List<Vector2> projected2D,
                bool hasUV,
                List<Vector2> uv,
                bool hasTangents,
                List<Vector4> tangents,
                bool hasColors,
                List<Color> colors)
            {
                this.vertices = vertices;
                this.triangles = triangles;
                this.projected2D = projected2D;
                this.hasUV = hasUV;
                this.uv = uv;
                this.hasTangents = hasTangents;
                this.tangents = tangents;
                this.hasColors = hasColors;
                this.colors = colors;
            }

            public int VertexCount => vertices.Count;
            public int TriangleCount => triangles.Count / 3;

            public static PreciseIncisionMesh Create(Mesh mesh, List<Vector2> stroke2D, Transform meshTransform, float meshRadius)
            {
                Vector3[] srcVertices = mesh.vertices;
                int[] srcTriangles = mesh.triangles;
                if (srcVertices == null || srcTriangles == null)
                    return null;

                Vector2[] srcUV = mesh.uv;
                Vector4[] srcTangents = mesh.tangents;
                Color[] srcColors = mesh.colors;

                bool hasUV = srcUV != null && srcUV.Length == srcVertices.Length;
                bool hasTangents = srcTangents != null && srcTangents.Length == srcVertices.Length;
                bool hasColors = srcColors != null && srcColors.Length == srcVertices.Length;

                var vertices = new List<Vector3>(srcVertices.Length);
                vertices.AddRange(srcVertices);

                var triangles = new List<int>(srcTriangles.Length);
                triangles.AddRange(srcTriangles);

                var projected = new List<Vector2>(srcVertices.Length);
                for (int i = 0; i < srcVertices.Length; i++)
                    projected.Add(ProjectLocalPointToStrokeSpace(srcVertices[i], stroke2D, meshRadius));

                List<Vector2> uv = null;
                List<Vector4> tangents = null;
                List<Color> colors = null;

                if (hasUV)
                {
                    uv = new List<Vector2>(srcVertices.Length);
                    uv.AddRange(srcUV);
                }

                if (hasTangents)
                {
                    tangents = new List<Vector4>(srcVertices.Length);
                    tangents.AddRange(srcTangents);
                }

                if (hasColors)
                {
                    colors = new List<Color>(srcVertices.Length);
                    colors.AddRange(srcColors);
                }

                return new PreciseIncisionMesh(vertices, triangles, projected, hasUV, uv, hasTangents, tangents, hasColors, colors);
            }

            public void GetTriangle(int triangleIndex, out int a, out int b, out int c)
            {
                int baseIndex = triangleIndex * 3;
                a = triangles[baseIndex];
                b = triangles[baseIndex + 1];
                c = triangles[baseIndex + 2];
            }

            public void SetTriangle(int triangleIndex, int a, int b, int c)
            {
                int baseIndex = triangleIndex * 3;
                triangles[baseIndex] = a;
                triangles[baseIndex + 1] = b;
                triangles[baseIndex + 2] = c;
            }

            public void AddTriangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            public int AddVertex(Vector3 localPoint, Vector2 point2D, Vector3 bary, int triA, int triB, int triC)
            {
                int index = vertices.Count;
                vertices.Add(localPoint);
                projected2D.Add(point2D);

                if (hasUV)
                {
                    Vector2 uvValue = uv[triA] * bary.x + uv[triB] * bary.y + uv[triC] * bary.z;
                    uv.Add(uvValue);
                }

                if (hasTangents)
                {
                    Vector4 t = tangents[triA] * bary.x + tangents[triB] * bary.y + tangents[triC] * bary.z;
                    tangents.Add(t);
                }

                if (hasColors)
                {
                    Color c = colors[triA] * bary.x + colors[triB] * bary.y + colors[triC] * bary.z;
                    colors.Add(c);
                }

                return index;
            }

            public int AddVertexOnEdge(Vector3 localPoint, Vector2 point2D, int a, int b, float t)
            {
                int index = vertices.Count;
                vertices.Add(localPoint);
                projected2D.Add(point2D);

                if (hasUV)
                    uv.Add(Vector2.Lerp(uv[a], uv[b], t));

                if (hasTangents)
                    tangents.Add(Vector4.Lerp(tangents[a], tangents[b], t));

                if (hasColors)
                    colors.Add(Color.Lerp(colors[a], colors[b], t));

                return index;
            }

            public void ApplyToMesh(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(vertices);
                if (hasUV && uv != null) mesh.SetUVs(0, uv);
                if (hasTangents && tangents != null) mesh.SetTangents(tangents);
                if (hasColors && colors != null) mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
            }
        }

        private struct StrokeSample
        {
            public Vector3 local;
            public Vector2 p2d;
        }

        private struct EdgeCrossing
        {
            public int a;
            public int b;
            public float tSegment;
            public float tEdge;
            public Vector2 point2D;
            public Vector3 pointLocal;
        }

        private sealed class IncisionPathDiagnostics
        {
            public string meshLabel;
            public int layer;
            public int strokePointCount;
            public float strokeTotalLength;
            public float strokeMaxSegmentLength;
            public float avgTriangleEdgeLength;
            public float sampleSpacing;
            public int resampledSampleCount;
            public int densifiedSampleCount;
            public float insertEpsilon;
            public float surfaceBandRadius;
            public int startEndInsertionFailures;
            public int zeroCandidateSegments;
            public int zeroCrossingSegments;
            public int weakCrossingSegments;
            public int disconnectedSegments;
            public int totalSegments;
            public int builtPathVertexCount;
            public string failStage;
            public string failReason;
            public readonly List<string> segmentLogs = new List<string>();

            public bool Success => string.IsNullOrEmpty(failStage);

            public void SetFailure(string stage, string reason)
            {
                if (!string.IsNullOrEmpty(failStage))
                    return;
                failStage = stage;
                failReason = reason;
            }

            public void LogToConsole()
            {
                string layerName = layer == (int)VesselLayer.Inner ? "Inner" : "Outer";
                Debug.Log($"[Incision][Stroke][{layerName}] mesh={meshLabel} points={strokePointCount} totalLen={strokeTotalLength:F6} maxSeg={strokeMaxSegmentLength:F6} avgTriEdge={avgTriangleEdgeLength:F6} sampleSpacing={sampleSpacing:F6} resampled={resampledSampleCount} densified={densifiedSampleCount} insertEps={insertEpsilon:F6} bandRadius={surfaceBandRadius:F6}");
                for (int i = 0; i < segmentLogs.Count; i++)
                    Debug.Log(segmentLogs[i]);
                if (Success)
                    Debug.Log($"[Incision][Chain][{layerName}] success pathVerts={builtPathVertexCount} segments={totalSegments} zeroCandidate={zeroCandidateSegments} zeroCrossing={zeroCrossingSegments} weakCrossing={weakCrossingSegments} disconnected={disconnectedSegments}");
                else
                    Debug.LogWarning($"[Incision][Chain][{layerName}] FAIL stage={failStage} reason={failReason} pathVerts={builtPathVertexCount} segments={totalSegments} zeroCandidate={zeroCandidateSegments} zeroCrossing={zeroCrossingSegments} weakCrossing={weakCrossingSegments} disconnected={disconnectedSegments} insertionFail={startEndInsertionFailures}");
            }
        }

        private sealed class IncisionGraphResult
        {
            public int layer;
            public PreciseIncisionMesh workMesh;
            public List<StrokeSample> spineSamples;
            public Dictionary<int, PreparedTriangleSplit> preparedByTri;
            public List<PendingLipSegment> pendingSegments;
            public List<PendingLipStation> dominantChain;
            public HashSet<int> selectedTriangles;
            public List<TriangleSplitCandidate> cutCandidates;
            public float exciseRadius;
            public IncisionPathDiagnostics diagnostics;
        }

        private struct SegmentIntersectionDebugInfo
        {
            public int candidateTriangleCount;
            public int crossedTriangleCount;
            public int maxTriangleHitCount;
            public string crossedTriangleSummary;
        }

        private struct StripTargetTraceRecord
        {
            public int triIndex;
            public int ia;
            public int ib;
            public int ic;
            public Vector3 va;
            public Vector3 vb;
            public Vector3 vc;
            public Vector3 centerWorld;
            public Vector2 center2D;
            public float area2D;
            public float centerlineDistance2D;
            public int corridorHits;
            public int centralHits;
            public bool overlapCandidate;
            public bool strictCandidate;
            public bool coreCandidate;
            public bool corridorCandidate;
            public bool bridgeCandidate;
            public bool middleBandCandidate;
            public bool stripInteriorCandidate;
            public bool removedCandidate;
        }


        private struct StationBoundaryQuad2D
        {
            public Vector2 leftA;
            public Vector2 leftB;
            public Vector2 rightB;
            public Vector2 rightA;
        }

        private static List<StationBoundaryQuad2D> BuildStationBoundaryQuads2D(PreciseIncisionMesh data, List<PendingLipStation> stations)
        {
            var quads = new List<StationBoundaryQuad2D>(stations != null ? Mathf.Max(0, stations.Count - 1) : 0);
            if (data == null || data.projected2D == null || stations == null || stations.Count < 2)
                return quads;

            bool hasPrev = false;
            Vector2 prevLeft = Vector2.zero;
            Vector2 prevRight = Vector2.zero;

            for (int i = 0; i < stations.Count; i++)
            {
                if (!TryResolveStationBoundaryFrame(data, stations, i, out int leftIndex, out int rightIndex, out _, out _, out _))
                    continue;

                if (leftIndex < 0 || rightIndex < 0 || leftIndex >= data.projected2D.Count || rightIndex >= data.projected2D.Count)
                    continue;

                Vector2 curLeft = data.projected2D[leftIndex];
                Vector2 curRight = data.projected2D[rightIndex];

                if (hasPrev)
                {
                    quads.Add(new StationBoundaryQuad2D
                    {
                        leftA = prevLeft,
                        leftB = curLeft,
                        rightB = curRight,
                        rightA = prevRight,
                    });
                }

                prevLeft = curLeft;
                prevRight = curRight;
                hasPrev = true;
            }

            return quads;
        }

        private static bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            if (!TryComputeBarycentric2D(p, a, b, c, out Vector3 bary))
                return false;

            const float tol = 1e-5f;
            return bary.x >= -tol && bary.y >= -tol && bary.z >= -tol;
        }

        private static bool PointInConvexQuad2D(Vector2 p, in StationBoundaryQuad2D quad)
        {
            return PointInTriangle2D(p, quad.leftA, quad.leftB, quad.rightB)
                || PointInTriangle2D(p, quad.leftA, quad.rightB, quad.rightA);
        }

        private static bool SegmentsIntersect2D(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            float o1 = Cross2D(a1 - a0, b0 - a0);
            float o2 = Cross2D(a1 - a0, b1 - a0);
            float o3 = Cross2D(b1 - b0, a0 - b0);
            float o4 = Cross2D(b1 - b0, a1 - b0);

            const float tol = 1e-6f;
            if (((o1 > tol && o2 < -tol) || (o1 < -tol && o2 > tol)) &&
                ((o3 > tol && o4 < -tol) || (o3 < -tol && o4 > tol)))
                return true;

            bool OnSegment(Vector2 p, Vector2 q, Vector2 r)
            {
                return q.x <= Mathf.Max(p.x, r.x) + tol && q.x >= Mathf.Min(p.x, r.x) - tol &&
                       q.y <= Mathf.Max(p.y, r.y) + tol && q.y >= Mathf.Min(p.y, r.y) - tol;
            }

            if (Mathf.Abs(o1) <= tol && OnSegment(a0, b0, a1)) return true;
            if (Mathf.Abs(o2) <= tol && OnSegment(a0, b1, a1)) return true;
            if (Mathf.Abs(o3) <= tol && OnSegment(b0, a0, b1)) return true;
            if (Mathf.Abs(o4) <= tol && OnSegment(b0, a1, b1)) return true;
            return false;
        }

        private static bool TriangleOverlapsStationBoundaryQuad2D(Vector2 a, Vector2 b, Vector2 c, in StationBoundaryQuad2D quad)
        {
            if (PointInConvexQuad2D(a, quad) || PointInConvexQuad2D(b, quad) || PointInConvexQuad2D(c, quad))
                return true;

            if (PointInTriangle2D(quad.leftA, a, b, c) ||
                PointInTriangle2D(quad.leftB, a, b, c) ||
                PointInTriangle2D(quad.rightB, a, b, c) ||
                PointInTriangle2D(quad.rightA, a, b, c))
                return true;

            Vector2[] tri = { a, b, c };
            Vector2[] quadPts = { quad.leftA, quad.leftB, quad.rightB, quad.rightA };
            for (int i = 0; i < 3; i++)
            {
                Vector2 ta = tri[i];
                Vector2 tb = tri[(i + 1) % 3];
                for (int j = 0; j < 4; j++)
                {
                    Vector2 qa = quadPts[j];
                    Vector2 qb = quadPts[(j + 1) % 4];
                    if (SegmentsIntersect2D(ta, tb, qa, qb))
                        return true;
                }
            }

            return false;
        }


        private static StationBoundaryQuad2D BuildInnerBandQuad(in StationBoundaryQuad2D quad, float t0, float t1)
        {
            t0 = Mathf.Clamp01(t0);
            t1 = Mathf.Clamp01(t1);
            if (t1 < t0)
            {
                float tmp = t0;
                t0 = t1;
                t1 = tmp;
            }

            return new StationBoundaryQuad2D
            {
                leftA = Vector2.Lerp(quad.leftA, quad.rightA, t0),
                leftB = Vector2.Lerp(quad.leftB, quad.rightB, t0),
                rightB = Vector2.Lerp(quad.leftB, quad.rightB, t1),
                rightA = Vector2.Lerp(quad.leftA, quad.rightA, t1),
            };
        }

        private static List<StationBoundaryQuad2D> BuildStationCentralBandQuads2D(List<StationBoundaryQuad2D> corridorQuads, float t0, float t1)
        {
            var result = new List<StationBoundaryQuad2D>(corridorQuads != null ? corridorQuads.Count : 0);
            if (corridorQuads == null || corridorQuads.Count == 0)
                return result;

            for (int i = 0; i < corridorQuads.Count; i++)
                result.Add(BuildInnerBandQuad(corridorQuads[i], t0, t1));

            return result;
        }

        private static bool PointInAnyBoundaryQuad2D(Vector2 p, List<StationBoundaryQuad2D> quads)
        {
            if (quads == null || quads.Count == 0)
                return false;

            for (int i = 0; i < quads.Count; i++)
            {
                if (PointInConvexQuad2D(p, quads[i]))
                    return true;
            }

            return false;
        }

        private static void AccumulateCentralBandSample2D(
            Vector2 p,
            List<StationBoundaryQuad2D> corridorQuads,
            List<StationBoundaryQuad2D> centralBandQuads,
            ref int corridorCount,
            ref int centralCount)
        {
            if (!PointInAnyBoundaryQuad2D(p, corridorQuads))
                return;

            corridorCount++;

            if (PointInAnyBoundaryQuad2D(p, centralBandQuads))
                centralCount++;
        }

        private static bool TriangleMostlyInsideCentralBoundaryBand2D(
            PreciseIncisionMesh data,
            int ia, int ib, int ic,
            List<StationBoundaryQuad2D> corridorQuads,
            List<StationBoundaryQuad2D> centralBandQuads)
        {
            if (data == null || data.projected2D == null)
                return false;
            if (corridorQuads == null || corridorQuads.Count == 0)
                return false;
            if (centralBandQuads == null || centralBandQuads.Count == 0)
                return false;

            if (ia < 0 || ib < 0 || ic < 0 ||
                ia >= data.projected2D.Count || ib >= data.projected2D.Count || ic >= data.projected2D.Count)
                return false;

            Vector2 a = data.projected2D[ia];
            Vector2 b = data.projected2D[ib];
            Vector2 c = data.projected2D[ic];
            Vector2 centroid = (a + b + c) / 3f;
            Vector2 abMid = 0.5f * (a + b);
            Vector2 bcMid = 0.5f * (b + c);
            Vector2 caMid = 0.5f * (c + a);

            int corridorCount = 0;
            int centralCount = 0;

            AccumulateCentralBandSample2D(centroid, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(a, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(b, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(c, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(abMid, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(bcMid, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(caMid, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);

            if (corridorCount < 4)
                return false;

            if (centralCount < 4)
                return false;

            if (centralCount < corridorCount - 1)
                return false;

            return true;
        }

        private static bool TriangleOverlapsAnyBoundaryQuad2D(
            PreciseIncisionMesh data,
            int ia, int ib, int ic,
            List<StationBoundaryQuad2D> quads)
        {
            if (data == null || data.projected2D == null || quads == null || quads.Count == 0)
                return false;
            if (ia < 0 || ib < 0 || ic < 0 ||
                ia >= data.projected2D.Count || ib >= data.projected2D.Count || ic >= data.projected2D.Count)
                return false;

            Vector2 a = data.projected2D[ia];
            Vector2 b = data.projected2D[ib];
            Vector2 c = data.projected2D[ic];
            for (int q = 0; q < quads.Count; q++)
            {
                if (TriangleOverlapsStationBoundaryQuad2D(a, b, c, quads[q]))
                    return true;
            }

            return false;
        }

        private static bool TriangleInsideCentralBoundaryBandLoose2D(
            PreciseIncisionMesh data,
            int ia, int ib, int ic,
            List<StationBoundaryQuad2D> corridorQuads,
            List<StationBoundaryQuad2D> centralBandQuads)
        {
            if (data == null || data.projected2D == null)
                return false;
            if (corridorQuads == null || corridorQuads.Count == 0)
                return false;
            if (centralBandQuads == null || centralBandQuads.Count == 0)
                return false;
            if (ia < 0 || ib < 0 || ic < 0 ||
                ia >= data.projected2D.Count || ib >= data.projected2D.Count || ic >= data.projected2D.Count)
                return false;

            Vector2 a = data.projected2D[ia];
            Vector2 b = data.projected2D[ib];
            Vector2 c = data.projected2D[ic];
            Vector2 centroid = (a + b + c) / 3f;
            Vector2 abMid = 0.5f * (a + b);
            Vector2 bcMid = 0.5f * (b + c);
            Vector2 caMid = 0.5f * (c + a);

            int corridorCount = 0;
            int centralCount = 0;
            AccumulateCentralBandSample2D(centroid, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(a, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(b, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(c, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(abMid, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(bcMid, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);
            AccumulateCentralBandSample2D(caMid, corridorQuads, centralBandQuads, ref corridorCount, ref centralCount);

            if (corridorCount < 3)
                return false;
            if (centralCount < 2)
                return false;
            if (centralCount * 2 < corridorCount)
                return false;

            return true;
        }


        private static int FindContainingBoundaryQuad2D(Vector2 p, List<StationBoundaryQuad2D> quads)
        {
            if (quads == null || quads.Count == 0)
                return -1;

            for (int i = 0; i < quads.Count; i++)
            {
                if (PointInConvexQuad2D(p, quads[i]))
                    return i;
            }

            return -1;
        }

        private static void AccumulateStripInteriorSample2D(
            Vector2 p,
            List<StationBoundaryQuad2D> corridorQuads,
            List<StationBoundaryQuad2D> centralBandQuads,
            int[] quadHits,
            int[] quadCentralHits,
            ref int corridorCount,
            ref int centralCount)
        {
            int corridorIndex = FindContainingBoundaryQuad2D(p, corridorQuads);
            if (corridorIndex < 0)
                return;

            corridorCount++;
            if (quadHits != null && corridorIndex < quadHits.Length)
                quadHits[corridorIndex]++;

            int centralIndex = FindContainingBoundaryQuad2D(p, centralBandQuads);
            if (centralIndex < 0)
                return;

            centralCount++;
            if (quadCentralHits != null && corridorIndex < quadCentralHits.Length)
                quadCentralHits[corridorIndex]++;
        }

        private static bool TriangleMostlyInsideStripInteriorCell2D(
            PreciseIncisionMesh data,
            int ia, int ib, int ic,
            List<Vector2> regionPolygon)
        {
            return TriangleInsideBoundaryRegionTopological2D(data, ia, ib, ic, regionPolygon);
        }


        private static void EstimateTriangleStripCoverageHits2D(
            PreciseIncisionMesh data,
            int ia, int ib, int ic,
            List<StationBoundaryQuad2D> corridorQuads,
            List<StationBoundaryQuad2D> centralBandQuads,
            out int corridorHits,
            out int centralHits)
        {
            corridorHits = 0;
            centralHits = 0;
            if (data == null || data.projected2D == null)
                return;
            if (ia < 0 || ib < 0 || ic < 0 ||
                ia >= data.projected2D.Count || ib >= data.projected2D.Count || ic >= data.projected2D.Count)
                return;

            Vector2 a = data.projected2D[ia];
            Vector2 b = data.projected2D[ib];
            Vector2 c = data.projected2D[ic];
            Vector2 centroid = (a + b + c) / 3f;
            Vector2 abMid = 0.5f * (a + b);
            Vector2 bcMid = 0.5f * (b + c);
            Vector2 caMid = 0.5f * (c + a);
            Vector2 i0 = 0.5f * a + 0.25f * b + 0.25f * c;
            Vector2 i1 = 0.25f * a + 0.5f * b + 0.25f * c;
            Vector2 i2 = 0.25f * a + 0.25f * b + 0.5f * c;

            Vector2[] samples =
            {
                centroid,
                a, b, c,
                abMid, bcMid, caMid,
                i0, i1, i2
            };

            for (int i = 0; i < samples.Length; i++)
                AccumulateCentralBandSample2D(samples[i], corridorQuads, centralBandQuads, ref corridorHits, ref centralHits);
        }

        private static Vector2 GetBoundaryQuadCenter2D(in StationBoundaryQuad2D quad)
        {
            return 0.25f * (quad.leftA + quad.leftB + quad.rightA + quad.rightB);
        }

        private static float DistancePointStripCenterlineSq2D(Vector2 p, List<StationBoundaryQuad2D> corridorQuads)
        {
            if (corridorQuads == null || corridorQuads.Count == 0)
                return float.PositiveInfinity;

            float best = float.PositiveInfinity;
            Vector2 prev = GetBoundaryQuadCenter2D(corridorQuads[0]);
            for (int i = 1; i < corridorQuads.Count; i++)
            {
                Vector2 cur = GetBoundaryQuadCenter2D(corridorQuads[i]);
                best = Mathf.Min(best, DistancePointSegmentSq2D(p, prev, cur));
                prev = cur;
            }

            if (!float.IsFinite(best))
                best = (p - prev).sqrMagnitude;
            return best;
        }

        private static float DistancePointSegmentSq2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = ab.sqrMagnitude;
            if (denom <= 1e-12f)
                return (p - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            Vector2 q = a + ab * t;
            return (p - q).sqrMagnitude;
        }

        private static float ComputeStripTargetTraceScore(in StripTargetTraceRecord record)
        {
            float score = 0f;
            score += record.removedCandidate ? 2000f : 0f;
            score += record.stripInteriorCandidate ? 1200f : 0f;
            score += record.corridorCandidate ? 120f : 0f;
            score += record.corridorHits * 2f;
            score -= record.centerlineDistance2D * 3500f;
            score -= record.area2D * 1800f;
            return score;
        }

        private static string GetStripTargetTraceFailHint(in StripTargetTraceRecord record)
        {
            if (!record.corridorCandidate)
                return "fail:no-left-right-overlap";
            if (!record.stripInteriorCandidate)
                return "fail:outside-left-right-region";
            return record.removedCandidate ? "pass:removed" : "pass:not-removed";
        }

        private static string BuildStripTargetTraceLogLine(in StripTargetTraceRecord record)
        {
            string failHint = GetStripTargetTraceFailHint(record);
            return $"tri#{record.triIndex} [{record.ia},{record.ib},{record.ic}] area2D={record.area2D:F6} dist={record.centerlineDistance2D:F5} hits(c={record.corridorHits},m={record.centralHits}) flags overlap={(record.overlapCandidate ? 1 : 0)} strict={(record.strictCandidate ? 1 : 0)} core={(record.coreCandidate ? 1 : 0)} corridor={(record.corridorCandidate ? 1 : 0)} bridge={(record.bridgeCandidate ? 1 : 0)} middle={(record.middleBandCandidate ? 1 : 0)} strip={(record.stripInteriorCandidate ? 1 : 0)} removed={(record.removedCandidate ? 1 : 0)} {failHint}";
        }

        private static void FinalizeStripTargetTrace(
            PreciseIncisionMesh data,
            List<StationBoundaryQuad2D> corridorQuads,
            List<StripTargetTraceRecord> records,
            IncisionDebugCapture debugCapture,
            Transform meshTransform,
            int layer)
        {
            if (records == null || records.Count == 0 || debugCapture == null)
                return;

            records.Sort((x, y) => ComputeStripTargetTraceScore(y).CompareTo(ComputeStripTargetTraceScore(x)));
            int focusCount = Mathf.Clamp(debugCapture.targetTraceCount, 1, Mathf.Min(12, records.Count));

            for (int i = 0; i < records.Count; i++)
            {
                var rec = records[i];
                debugCapture.allTargetTraceRecords.Add(new IncisionTargetTraceDebugRecord
                {
                    center = rec.centerWorld,
                    layer = layer,
                    triIndex = rec.triIndex,
                    ia = rec.ia,
                    ib = rec.ib,
                    ic = rec.ic,
                    area2D = rec.area2D,
                    centerlineDistance2D = rec.centerlineDistance2D,
                    corridorHits = rec.corridorHits,
                    centralHits = rec.centralHits,
                    overlapCandidate = rec.overlapCandidate,
                    strictCandidate = rec.strictCandidate,
                    coreCandidate = rec.coreCandidate,
                    corridorCandidate = rec.corridorCandidate,
                    bridgeCandidate = rec.bridgeCandidate,
                    middleBandCandidate = rec.middleBandCandidate,
                    stripInteriorCandidate = rec.stripInteriorCandidate,
                    removedCandidate = rec.removedCandidate,
                    score = ComputeStripTargetTraceScore(rec),
                    failHint = GetStripTargetTraceFailHint(rec),
                });
            }

            var focusCenters = new List<Vector3>(focusCount);
            for (int i = 0; i < focusCount; i++)
            {
                var rec = records[i];
                focusCenters.Add(rec.centerWorld);
                CaptureRemovedTriangle(debugCapture, meshTransform, rec.va, rec.vb, rec.vc, IncisionPatchDebugKind.TargetTraceFocus, layer, rec.triIndex, rec.ia, rec.ib, rec.ic);
                string line = BuildStripTargetTraceLogLine(rec);
                debugCapture.targetTraceLogs.Add(line);
                debugCapture.targetTraceRecords.Add(new IncisionTargetTraceDebugRecord
                {
                    center = rec.centerWorld,
                    layer = layer,
                    triIndex = rec.triIndex,
                    ia = rec.ia,
                    ib = rec.ib,
                    ic = rec.ic,
                    area2D = rec.area2D,
                    centerlineDistance2D = rec.centerlineDistance2D,
                    corridorHits = rec.corridorHits,
                    centralHits = rec.centralHits,
                    overlapCandidate = rec.overlapCandidate,
                    strictCandidate = rec.strictCandidate,
                    coreCandidate = rec.coreCandidate,
                    corridorCandidate = rec.corridorCandidate,
                    bridgeCandidate = rec.bridgeCandidate,
                    middleBandCandidate = rec.middleBandCandidate,
                    stripInteriorCandidate = rec.stripInteriorCandidate,
                    removedCandidate = rec.removedCandidate,
                    score = ComputeStripTargetTraceScore(rec),
                    failHint = GetStripTargetTraceFailHint(rec),
                });
            }

            float neighborRadius = Mathf.Max(0f, debugCapture.targetTraceNeighborRadius);
            float neighborRadiusSq = neighborRadius * neighborRadius;
            if (neighborRadiusSq > 1e-12f)
            {
                for (int i = focusCount; i < records.Count; i++)
                {
                    var rec = records[i];
                    bool nearFocus = false;
                    for (int j = 0; j < focusCenters.Count; j++)
                    {
                        if ((rec.centerWorld - focusCenters[j]).sqrMagnitude <= neighborRadiusSq)
                        {
                            nearFocus = true;
                            break;
                        }
                    }

                    if (!nearFocus)
                        continue;

                    CaptureRemovedTriangle(debugCapture, meshTransform, rec.va, rec.vb, rec.vc, IncisionPatchDebugKind.TargetTraceNeighbor, layer, rec.triIndex, rec.ia, rec.ib, rec.ic);
                }
            }

            if (debugCapture.targetTraceLogToConsole)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
                sb.Append("[IncisionTargetTrace] layer=").Append(layer)
                  .Append(" focus=").Append(focusCount)
                  .Append(" corridorRecords=").Append(records.Count);
                for (int i = 0; i < debugCapture.targetTraceLogs.Count; i++)
                    sb.AppendLine().Append("  ").Append(debugCapture.targetTraceLogs[i]);
                Debug.Log(sb.ToString());
            }
        }

        private static HashSet<int> BuildBoundaryGuidedCorridorPatch(PreciseIncisionMesh data, List<PendingLipStation> stations)
        {
            var patch = new HashSet<int>();
            if (data == null || data.projected2D == null || stations == null || stations.Count < 2)
                return patch;

            var regionPolygon = BuildStationBoundaryRegionPolygon2D(data, stations);
            if (regionPolygon != null && regionPolygon.Count >= 3)
            {
                for (int tri = 0; tri < data.TriangleCount; tri++)
                {
                    data.GetTriangle(tri, out int ia, out int ib, out int ic);
                    if (ia < 0 || ib < 0 || ic < 0 ||
                        ia >= data.projected2D.Count || ib >= data.projected2D.Count || ic >= data.projected2D.Count)
                        continue;

                    Vector2 a = data.projected2D[ia];
                    Vector2 b = data.projected2D[ib];
                    Vector2 c = data.projected2D[ic];
                    if (TriangleOverlapsBoundaryRegion2D(a, b, c, regionPolygon))
                        patch.Add(tri);
                }

                return patch;
            }

            var quads = BuildStationBoundaryQuads2D(data, stations);
            if (quads == null || quads.Count == 0)
                return patch;

            for (int tri = 0; tri < data.TriangleCount; tri++)
            {
                data.GetTriangle(tri, out int ia, out int ib, out int ic);
                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= data.projected2D.Count || ib >= data.projected2D.Count || ic >= data.projected2D.Count)
                    continue;

                Vector2 a = data.projected2D[ia];
                Vector2 b = data.projected2D[ib];
                Vector2 c = data.projected2D[ic];

                for (int q = 0; q < quads.Count; q++)
                {
                    if (TriangleOverlapsStationBoundaryQuad2D(a, b, c, quads[q]))
                    {
                        patch.Add(tri);
                        break;
                    }
                }
            }

            return patch;
        }

        private static void AppendPolygonPoint2D(List<Vector2> polygon, Vector2 p)
        {
            if (polygon == null)
                return;

            const float sqTol = 1e-10f;
            if (polygon.Count > 0 && (polygon[polygon.Count - 1] - p).sqrMagnitude <= sqTol)
                return;

            polygon.Add(p);
        }

        private static void AppendStationBoundaryChain2D(
            PreciseIncisionMesh data,
            List<PendingLipStation> stations,
            bool useLeftBoundary,
            bool reverseOrder,
            List<Vector2> polygon)
        {
            if (data == null || data.projected2D == null || stations == null || polygon == null)
                return;

            int count = stations.Count;
            for (int k = 0; k < count; k++)
            {
                int i = reverseOrder ? (count - 1 - k) : k;
                if (!TryResolveStationBoundaryFrame(data, stations, i, out int leftIndex, out int rightIndex, out _, out _, out _))
                    continue;

                int boundaryIndex = useLeftBoundary ? leftIndex : rightIndex;
                if (boundaryIndex < 0 || boundaryIndex >= data.projected2D.Count)
                    continue;

                AppendPolygonPoint2D(polygon, data.projected2D[boundaryIndex]);
            }
        }

        private static List<Vector2> BuildStationBoundaryRegionPolygon2D(PreciseIncisionMesh data, List<PendingLipStation> stations)
        {
            var polygon = new List<Vector2>(stations != null ? stations.Count * 2 + 2 : 0);
            if (data == null || data.projected2D == null || stations == null || stations.Count < 2)
                return polygon;

            AppendStationBoundaryChain2D(data, stations, true, false, polygon);
            AppendStationBoundaryChain2D(data, stations, false, true, polygon);

            while (polygon.Count >= 2 && (polygon[0] - polygon[polygon.Count - 1]).sqrMagnitude <= 1e-10f)
                polygon.RemoveAt(polygon.Count - 1);

            if (polygon.Count < 3)
                polygon.Clear();

            return polygon;
        }

        private static bool PointOnSegment2D(Vector2 p, Vector2 a, Vector2 b, float tol = 1e-6f)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq <= tol * tol)
                return (p - a).sqrMagnitude <= tol * tol;

            float cross = Mathf.Abs(Cross2D(ab, p - a));
            if (cross > tol * Mathf.Sqrt(lenSq))
                return false;

            float dot = Vector2.Dot(p - a, ab);
            if (dot < -tol || dot > lenSq + tol)
                return false;

            return true;
        }

        private static bool PointInPolygonOrOnEdge2D(Vector2 p, List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 pi = polygon[i];
                Vector2 pj = polygon[j];

                if (PointOnSegment2D(p, pj, pi))
                    return true;

                bool intersect = ((pi.y > p.y) != (pj.y > p.y)) &&
                                 (p.x < (pj.x - pi.x) * (p.y - pi.y) / Mathf.Max(1e-12f, (pj.y - pi.y)) + pi.x);
                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        private static bool TriangleOverlapsBoundaryRegion2D(Vector2 a, Vector2 b, Vector2 c, List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            if (PointInPolygonOrOnEdge2D(a, polygon) || PointInPolygonOrOnEdge2D(b, polygon) || PointInPolygonOrOnEdge2D(c, polygon))
                return true;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 p0 = polygon[i];
                Vector2 p1 = polygon[(i + 1) % polygon.Count];
                if (PointInTriangle2D(p0, a, b, c) || PointOnSegment2D(p0, a, b) || PointOnSegment2D(p0, b, c) || PointOnSegment2D(p0, c, a))
                    return true;

                if (SegmentsIntersect2D(a, b, p0, p1) ||
                    SegmentsIntersect2D(b, c, p0, p1) ||
                    SegmentsIntersect2D(c, a, p0, p1))
                    return true;
            }

            return false;
        }

        private static bool TriangleInsideBoundaryRegionTopological2D(
            PreciseIncisionMesh data,
            int ia, int ib, int ic,
            List<Vector2> regionPolygon)
        {
            if (data == null || data.projected2D == null || regionPolygon == null || regionPolygon.Count < 3)
                return false;
            if (ia < 0 || ib < 0 || ic < 0 ||
                ia >= data.projected2D.Count || ib >= data.projected2D.Count || ic >= data.projected2D.Count)
                return false;

            Vector2 a = data.projected2D[ia];
            Vector2 b = data.projected2D[ib];
            Vector2 c = data.projected2D[ic];
            Vector2 centroid = (a + b + c) / 3f;

            if (PointInPolygonOrOnEdge2D(centroid, regionPolygon))
                return true;

            // 注意：这里不再按 corridor 命中阈值删面。
            // 仅在质心落边界极近时，用更靠内的三个重心偏置点做一次“拓扑归属”补偿，
            // 目的是处理数值误差和边界正好压在三角质心附近的情况，而不是重新回到采样阈值判定。
            int insideCount = 0;
            Vector2 i0 = 0.5f * a + 0.25f * b + 0.25f * c;
            Vector2 i1 = 0.25f * a + 0.5f * b + 0.25f * c;
            Vector2 i2 = 0.25f * a + 0.25f * b + 0.5f * c;
            if (PointInPolygonOrOnEdge2D(i0, regionPolygon)) insideCount++;
            if (PointInPolygonOrOnEdge2D(i1, regionPolygon)) insideCount++;
            if (PointInPolygonOrOnEdge2D(i2, regionPolygon)) insideCount++;
            return insideCount >= 2;
        }

        private static List<StrokeSample> BuildStationBoundaryGuideSamples(PreciseIncisionMesh data, List<PendingLipStation> stations, int sideMode)
        {
            var result = new List<StrokeSample>(stations != null ? stations.Count : 0);
            if (data == null || data.projected2D == null || data.vertices == null || stations == null)
                return result;

            for (int i = 0; i < stations.Count; i++)
            {
                if (!TryResolveStationBoundaryFrame(data, stations, i, out int leftIndex, out int rightIndex, out Vector3 center, out _, out _))
                    continue;

                if (leftIndex < 0 || rightIndex < 0 ||
                    leftIndex >= data.projected2D.Count || rightIndex >= data.projected2D.Count ||
                    leftIndex >= data.vertices.Count || rightIndex >= data.vertices.Count)
                    continue;

                Vector3 localPoint;
                Vector2 p2d;
                if (sideMode < 0)
                {
                    localPoint = data.vertices[leftIndex];
                    p2d = data.projected2D[leftIndex];
                }
                else if (sideMode > 0)
                {
                    localPoint = data.vertices[rightIndex];
                    p2d = data.projected2D[rightIndex];
                }
                else
                {
                    localPoint = center;
                    p2d = 0.5f * (data.projected2D[leftIndex] + data.projected2D[rightIndex]);
                }

                if (result.Count > 0)
                {
                    StrokeSample prev = result[result.Count - 1];
                    if ((prev.local - localPoint).sqrMagnitude <= 1e-10f || (prev.p2d - p2d).sqrMagnitude <= 1e-10f)
                        continue;
                }

                result.Add(new StrokeSample
                {
                    local = localPoint,
                    p2d = p2d,
                });
            }

            return result;
        }

        private static List<StrokeSample> BuildStationCenterGuideSamples(PreciseIncisionMesh data, List<PendingLipStation> stations)
        {
            return BuildStationBoundaryGuideSamples(data, stations, 0);
        }

        private static List<StrokeSample> BuildStationLeftGuideSamples(PreciseIncisionMesh data, List<PendingLipStation> stations)
        {
            return BuildStationBoundaryGuideSamples(data, stations, -1);
        }

        private static List<StrokeSample> BuildStationRightGuideSamples(PreciseIncisionMesh data, List<PendingLipStation> stations)
        {
            return BuildStationBoundaryGuideSamples(data, stations, 1);
        }

        private static void UnionTrianglePatch(HashSet<int> dst, HashSet<int> src)
        {
            if (dst == null || src == null || src.Count == 0)
                return;

            foreach (int tri in src)
                dst.Add(tri);
        }

        private static bool RefineBoundaryPatchAgainstGuideSamples(
            PreciseIncisionMesh work,
            HashSet<int> boundaryPatch,
            float insertEps,
            float surfaceBandRadius,
            params List<StrokeSample>[] guides)
        {
            if (work == null || boundaryPatch == null || boundaryPatch.Count == 0 || guides == null || guides.Length == 0)
                return false;

            bool anyGuide = false;
            for (int i = 0; i < guides.Length; i++)
            {
                if (guides[i] != null && guides[i].Count >= 2)
                {
                    anyGuide = true;
                    break;
                }
            }

            if (!anyGuide)
                return false;

            bool changed = false;
            float safeBand = Mathf.Max(surfaceBandRadius, insertEps * 4f);
            const int refinePasses = 2;
            for (int pass = 0; pass < refinePasses; pass++)
            {
                if (SubdivideIncisionPatchOnce(work, boundaryPatch, insertEps))
                    changed = true;

                for (int i = 0; i < guides.Length; i++)
                {
                    var guide = guides[i];
                    if (guide == null || guide.Count < 2)
                        continue;

                    RefineIncisionBandLocally(work, guide, insertEps, safeBand, boundaryPatch);
                }
            }

            return changed;
        }

        private static HashSet<int> RebuildExpandedBoundaryPatch(
            PreciseIncisionMesh work,
            List<PendingLipStation> stations,
            float insertEps,
            float surfaceBandRadius,
            List<StrokeSample> stationCenterGuideSamples,
            List<StrokeSample> stationLeftGuideSamples,
            List<StrokeSample> stationRightGuideSamples)
        {
            var expandedBoundaryPatch = BuildBoundaryGuidedCorridorPatch(work, stations) ?? new HashSet<int>();
            UnionTrianglePatch(expandedBoundaryPatch, BuildIncisionLocalPatch(work, stationCenterGuideSamples, insertEps, surfaceBandRadius, 0));
            UnionTrianglePatch(expandedBoundaryPatch, BuildIncisionLocalPatch(work, stationLeftGuideSamples, insertEps, surfaceBandRadius, 0));
            UnionTrianglePatch(expandedBoundaryPatch, BuildIncisionLocalPatch(work, stationRightGuideSamples, insertEps, surfaceBandRadius, 0));
            return expandedBoundaryPatch;
        }

        private static float ComputeCandidateGuideDistanceSq(TriangleSplitCandidate candidate, List<StrokeSample> guideSamples)
        {
            if (guideSamples == null || guideSamples.Count < 2)
                return float.PositiveInfinity;

            Vector2 p = 0.5f * (candidate.crossingA.point2D + candidate.crossingB.point2D);
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < guideSamples.Count - 1; i++)
            {
                Vector2 a = guideSamples[i].p2d;
                Vector2 b = guideSamples[i + 1].p2d;
                Vector2 ab = b - a;
                float denom = Vector2.Dot(ab, ab);
                float t = denom > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom) : 0f;
                Vector2 q = a + ab * t;
                float dsq = (p - q).sqrMagnitude;
                if (dsq < bestSq)
                    bestSq = dsq;
            }

            return bestSq;
        }

        private static void UpsertTriangleSplitCandidate(List<TriangleSplitCandidate> candidates, TriangleSplitCandidate candidate)
        {
            if (candidates == null)
                return;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].triIndex == candidate.triIndex)
                {
                    candidates[i] = candidate;
                    return;
                }
            }

            candidates.Add(candidate);
        }

        private static void MergeBoundaryDrivenSplitCandidates(
            PreciseIncisionMesh work,
            List<StrokeSample> guideSamples,
            HashSet<int> basePatch,
            float insertEps,
            float surfaceBandRadius,
            float openWidthWorld,
            float profileVariation,
            float curvatureWidthScale,
            int layer,
            Dictionary<ulong, int> edgeVertexCache,
            Dictionary<int, int> duplicateCache,
            Dictionary<int, TriangleSplitCandidate> candidateByTri,
            Dictionary<int, PreparedTriangleSplit> preparedByTri,
            HashSet<int> selectedTriangles,
            List<TriangleSplitCandidate> cutCandidates)
        {
            if (work == null || guideSamples == null || guideSamples.Count < 2 ||
                edgeVertexCache == null || duplicateCache == null ||
                candidateByTri == null || preparedByTri == null || selectedTriangles == null || cutCandidates == null)
                return;

            var allowedTriangles = new HashSet<int>();
            UnionTrianglePatch(allowedTriangles, basePatch);
            UnionTrianglePatch(allowedTriangles, BuildIncisionLocalPatch(work, guideSamples, insertEps, surfaceBandRadius, 0));
            if (allowedTriangles.Count == 0)
                return;

            var guideCandidates = CollectTriangleSplitCandidates(work, guideSamples, insertEps, surfaceBandRadius, allowedTriangles);
            if (guideCandidates == null || guideCandidates.Count == 0)
                return;

            for (int i = 0; i < guideCandidates.Count; i++)
            {
                var candidate = guideCandidates[i];
                bool shouldApply = !candidateByTri.TryGetValue(candidate.triIndex, out var existingCandidate);
                if (!shouldApply)
                {
                    float existingDistSq = ComputeCandidateGuideDistanceSq(existingCandidate, guideSamples);
                    float newDistSq = ComputeCandidateGuideDistanceSq(candidate, guideSamples);
                    shouldApply = float.IsFinite(newDistSq) && (!float.IsFinite(existingDistSq) || newDistSq + 1e-10f < existingDistSq * 0.85f);
                }

                if (!shouldApply)
                    continue;

                if (!TryPrepareTriangleSplitCandidate(
                        work,
                        candidate,
                        edgeVertexCache,
                        duplicateCache,
                        guideSamples.Count,
                        openWidthWorld,
                        profileVariation,
                        curvatureWidthScale,
                        layer,
                        null,
                        out var prep))
                    continue;

                candidateByTri[candidate.triIndex] = candidate;
                preparedByTri[candidate.triIndex] = prep;
                selectedTriangles.Add(candidate.triIndex);
                UpsertTriangleSplitCandidate(cutCandidates, candidate);
            }
        }

        private static bool TryPrepareTriangleSplitCandidate(
            PreciseIncisionMesh work,
            TriangleSplitCandidate cand,
            Dictionary<ulong, int> edgeVertexCache,
            Dictionary<int, int> duplicateCache,
            int sampleCount,
            float openWidthWorld,
            float profileVariation,
            float curvatureWidthScale,
            int layer,
            List<PendingLipSegment> pendingLipSegments,
            out PreparedTriangleSplit prep)
        {
            prep = default;
            if (work == null)
                return false;

            int cutA = GetOrCreateIntersectionVertex(work, cand.crossingA, edgeVertexCache);
            int cutB = GetOrCreateIntersectionVertex(work, cand.crossingB, edgeVertexCache);
            if (cutA < 0 || cutB < 0 || cutA == cutB)
                return false;

            int dupA = GetOrCreateDuplicateVertex(work, cutA, duplicateCache);
            int dupB = GetOrCreateDuplicateVertex(work, cutB, duplicateCache);
            if (dupA < 0 || dupB < 0)
                return false;

            if (cand.i0 < 0 || cand.i1 < 0 || cand.i2 < 0 ||
                cand.i0 >= work.vertices.Count || cand.i1 >= work.vertices.Count || cand.i2 >= work.vertices.Count)
                return false;

            Vector3 refNormal = Vector3.Cross(work.vertices[cand.i1] - work.vertices[cand.i0], work.vertices[cand.i2] - work.vertices[cand.i0]);
            if (refNormal.sqrMagnitude <= 1e-12f)
                refNormal = Vector3.forward;

            Vector3 tangent = cand.segmentEndLocal - cand.segmentStartLocal;
            if (tangent.sqrMagnitude <= 1e-12f)
                tangent = work.vertices[cand.otherB] - work.vertices[cand.otherA];
            if (tangent.sqrMagnitude <= 1e-12f)
                tangent = Vector3.forward;
            tangent.Normalize();

            Vector3 splitDir = Vector3.Cross(refNormal.normalized, tangent);
            if (splitDir.sqrMagnitude <= 1e-12f)
                splitDir = cand.crossingB.pointLocal - cand.crossingA.pointLocal;
            if (splitDir.sqrMagnitude <= 1e-12f)
                splitDir = Vector3.right;
            splitDir.Normalize();

            float targetOpen = ComputePolylineTargetOpenWidth(cand.globalT, openWidthWorld, profileVariation, curvatureWidthScale);
            float strokeDenom = Mathf.Max(1f, Mathf.Max(1, sampleCount - 1));
            float strokeTA = (cand.segmentIndex + Mathf.Clamp01(cand.crossingA.tSegment)) / strokeDenom;
            float strokeTB = (cand.segmentIndex + Mathf.Clamp01(cand.crossingB.tSegment)) / strokeDenom;

            prep = new PreparedTriangleSplit
            {
                triIndex = cand.triIndex,
                a = cand.i0,
                b = cand.i1,
                c = cand.i2,
                lonelyVertex = cand.lonelyVertex,
                otherA = cand.otherA,
                otherB = cand.otherB,
                cutA = cutA,
                cutB = cutB,
                dupA = dupA,
                dupB = dupB,
                lonelyPositive = cand.lonelyPositive,
                refNormal = refNormal
            };

            if (pendingLipSegments != null)
            {
                CollectPendingLipSegment(
                    pendingLipSegments,
                    cutA,
                    dupA,
                    cutB,
                    dupB,
                    cand.crossingA,
                    cand.crossingB,
                    splitDir,
                    targetOpen,
                    openWidthWorld,
                    layer,
                    strokeTA,
                    strokeTB,
                    cand.triIndex);
            }

            return true;
        }

        private static bool PrepareIncisionGraphResult(
            Mesh mesh,
            Transform meshTransform,
            List<Vector3> strokeWorld,
            float guideRadiusWorld,
            float openWidthWorld,
            float pathSampleSpacingWorld,
            float profileVariation,
            float curvatureWidthScale,
            int localSubdivisionPasses,
            int layer,
            IncisionDebugCapture debugCapture,
            out IncisionGraphResult result)
        {
            result = new IncisionGraphResult
            {
                layer = layer,
                diagnostics = new IncisionPathDiagnostics { meshLabel = mesh != null ? mesh.name : "null", layer = layer }
            };

            var diagnostics = result.diagnostics;
            if (mesh == null || meshTransform == null || strokeWorld == null || strokeWorld.Count < 2)
            {
                diagnostics.SetFailure("Input", "mesh / transform / stroke 无效或点数不足。");
                return false;
            }

            diagnostics.strokePointCount = strokeWorld.Count;
            for (int i = 0; i < strokeWorld.Count - 1; i++)
            {
                float segLen = Vector3.Distance(strokeWorld[i], strokeWorld[i + 1]);
                diagnostics.strokeTotalLength += segLen;
                diagnostics.strokeMaxSegmentLength = Mathf.Max(diagnostics.strokeMaxSegmentLength, segLen);
            }

            Vector3[] srcVertices = mesh.vertices;
            int[] srcTriangles = mesh.triangles;
            if (srcVertices == null || srcVertices.Length < 3 || srcTriangles == null || srcTriangles.Length < 3)
            {
                diagnostics.SetFailure("Input", "mesh 顶点或三角形为空。");
                return false;
            }

            float meshRadius = GetAverageMeshRadius(srcVertices);
            List<Vector2> stroke2D = BuildUnwrappedStroke2D(strokeWorld, meshTransform, meshRadius);
            if (stroke2D == null || stroke2D.Count < 2)
            {
                diagnostics.SetFailure("Stroke2D", "程序线未能转换成有效的展开 2D 轨迹。");
                return false;
            }

            Vector2[] projectedVertices2D = BuildProjectedVertices2D(srcVertices, stroke2D, meshRadius);
            float avgEdgeLen = ComputeAverageEdgeLength3D(srcVertices, srcTriangles);
            diagnostics.avgTriangleEdgeLength = avgEdgeLen;
            float sampleSpacing = pathSampleSpacingWorld > 1e-6f ? pathSampleSpacingWorld : Mathf.Max(1e-5f, avgEdgeLen * 0.55f);
            diagnostics.sampleSpacing = sampleSpacing;
            float insertEps = Mathf.Max(1e-5f, Mathf.Min(Mathf.Max(guideRadiusWorld * 0.18f, avgEdgeLen * 0.12f), Mathf.Max(avgEdgeLen * 0.5f, guideRadiusWorld)));
            diagnostics.insertEpsilon = insertEps;

            var work = PreciseIncisionMesh.Create(mesh, stroke2D, meshTransform, meshRadius);
            if (work == null || work.VertexCount < 3 || work.TriangleCount < 1)
            {
                diagnostics.SetFailure("WorkMesh", "局部工作网格构建失败。");
                return false;
            }

            List<StrokeSample> samples = BuildResampledStrokeSamples(strokeWorld, meshTransform, meshRadius, sampleSpacing);
            diagnostics.resampledSampleCount = samples != null ? samples.Count : 0;
            if (samples == null || samples.Count < 2)
            {
                diagnostics.SetFailure("Resample", "重采样后的程序线点数不足 2。");
                return false;
            }

            float densifiedStep = Mathf.Max(1e-5f, Mathf.Min(sampleSpacing, avgEdgeLen * 0.35f));
            samples = DensifyStrokeSamplesForIncisionBand(samples, densifiedStep);
            diagnostics.densifiedSampleCount = samples != null ? samples.Count : 0;
            if (samples == null || samples.Count < 2)
            {
                diagnostics.SetFailure("Densify", "局部加密后的程序线点数不足 2。");
                return false;
            }

            float surfaceBandRadius = Mathf.Max(guideRadiusWorld * 1.75f, insertEps * 3.0f);
            diagnostics.surfaceBandRadius = surfaceBandRadius;

            diagnostics.totalSegments = Mathf.Max(0, samples.Count - 1);
            for (int seg = 0; seg < samples.Count - 1; seg++)
            {
                SegmentIntersectionDebugInfo segInfo = EvaluateSegmentIntersectionDebugInfo(work, samples[seg], samples[seg + 1], insertEps, surfaceBandRadius);
                if (segInfo.candidateTriangleCount <= 0) diagnostics.zeroCandidateSegments++;
                if (segInfo.crossedTriangleCount <= 0) diagnostics.zeroCrossingSegments++;
                else if (segInfo.crossedTriangleCount < 2) diagnostics.weakCrossingSegments++;
                diagnostics.segmentLogs.Add($"[Incision][Segment][{(layer == (int)VesselLayer.Inner ? "Inner" : "Outer")}] seg={seg} candidates={segInfo.candidateTriangleCount} crossedTris={segInfo.crossedTriangleCount} maxTriHit={segInfo.maxTriangleHitCount} crossings={Mathf.Max(0, segInfo.crossedTriangleCount)} summary={segInfo.crossedTriangleSummary}");
            }

            var crossedOriginalPatch = BuildIncisionLocalPatch(work, samples, insertEps, surfaceBandRadius, 0);
            CapturePatchTriangles(debugCapture, work, meshTransform, crossedOriginalPatch, IncisionPatchDebugKind.CrossedOriginal, layer);

            var refinementPatch = BuildIncisionLocalPatch(work, samples, insertEps, surfaceBandRadius, 1);
            CapturePatchTriangles(debugCapture, work, meshTransform, refinementPatch, IncisionPatchDebugKind.RefinementPatch, layer);
            for (int subPass = 0; subPass < Mathf.Clamp(localSubdivisionPasses, 0, 2) && refinementPatch.Count > 0; subPass++)
            {
                int vertexBeforeSubdivide = work.VertexCount;
                if (!SubdivideIncisionPatchOnce(work, refinementPatch, insertEps))
                    break;
                CaptureNewVertices(debugCapture, work, meshTransform, vertexBeforeSubdivide, IncisionInsertedVertexDebugKind.PreSubdivision, layer);
                refinementPatch = BuildIncisionLocalPatch(work, samples, insertEps, surfaceBandRadius, 1);
                CapturePatchTriangles(debugCapture, work, meshTransform, refinementPatch, IncisionPatchDebugKind.RefinementPatch, layer);
            }

            int vertexBeforeRefineBand = work.VertexCount;
            RefineIncisionBandLocally(work, samples, insertEps, surfaceBandRadius, refinementPatch);
            CaptureNewVertices(debugCapture, work, meshTransform, vertexBeforeRefineBand, IncisionInsertedVertexDebugKind.BandRefine, layer);

            var cutPatch = BuildIncisionLocalPatch(work, samples, insertEps, surfaceBandRadius, 0);
            CapturePatchTriangles(debugCapture, work, meshTransform, cutPatch, IncisionPatchDebugKind.CutPatch, layer);
            var cutCandidates = CollectTriangleSplitCandidates(work, samples, insertEps, surfaceBandRadius, cutPatch);
            if (cutCandidates == null || cutCandidates.Count < 1)
            {
                diagnostics.SetFailure("CutCandidates", "没有收集到可提交的 triangle split candidates。");
                return false;
            }

            float exciseRadius = Mathf.Max(Mathf.Max(openWidthWorld * 0.18f, insertEps * 0.08f), 5e-5f);
            var edgeVertexCache = new Dictionary<ulong, int>();
            var duplicateCache = new Dictionary<int, int>();
            var pendingLipSegments = new List<PendingLipSegment>(cutCandidates.Count);
            var preparedByTri = new Dictionary<int, PreparedTriangleSplit>(cutCandidates.Count);
            var originalTriangles = new List<int>(work.triangles);
            int originalTriCount = originalTriangles.Count / 3;
            var candidateByTri = new Dictionary<int, TriangleSplitCandidate>(cutCandidates.Count);
            for (int i = 0; i < cutCandidates.Count; i++)
            {
                if (!candidateByTri.ContainsKey(cutCandidates[i].triIndex))
                    candidateByTri.Add(cutCandidates[i].triIndex, cutCandidates[i]);
            }

            for (int tri = 0; tri < originalTriCount; tri++)
            {
                if (!candidateByTri.TryGetValue(tri, out var cand))
                    continue;

                if (!TryPrepareTriangleSplitCandidate(
                        work,
                        cand,
                        edgeVertexCache,
                        duplicateCache,
                        samples.Count,
                        openWidthWorld,
                        profileVariation,
                        curvatureWidthScale,
                        layer,
                        pendingLipSegments,
                        out var prep))
                    continue;

                preparedByTri[tri] = prep;
            }

            if (pendingLipSegments.Count == 0)
            {
                diagnostics.SetFailure("PendingSegments", "切割候选未能生成任何稳定 segment。");
                return false;
            }

            bool builtDominantPath = BuildDominantIncisionPath(pendingLipSegments, Mathf.Max(exciseRadius * 0.5f, 1e-5f), out var dominantChain, out var selectedTriangles);
            if (!builtDominantPath || dominantChain == null || dominantChain.Count < 2 || selectedTriangles == null || selectedTriangles.Count == 0)
            {
                bool fallbackBuilt = BuildStrokeOrderedFallbackIncisionPath(
                    pendingLipSegments,
                    Mathf.Max(exciseRadius * 0.5f, 1e-5f),
                    out dominantChain,
                    out selectedTriangles);
                if (fallbackBuilt)
                {
                    diagnostics.segmentLogs.Add($"[Incision][GraphFallback][{(layer == (int)VesselLayer.Inner ? "Inner" : "Outer")}] dominant chain 失败，已切换到 stroke-ordered 3D fallback。stations={dominantChain.Count} selected={selectedTriangles.Count}");
                }
                else
                {
                    diagnostics.SetFailure("Graph", "dominant chain / selected triangles 构建失败。诊断成功但提交图链失败，且 3D fallback 也未能产出稳定 chain。");
                    diagnostics.builtPathVertexCount = dominantChain != null ? dominantChain.Count : 0;
                    return false;
                }
            }

            if (!FilterStationBoundaryOutliers(work, dominantChain, Mathf.Max(exciseRadius * 0.5f, 1e-5f)) || dominantChain.Count < 2)
            {
                diagnostics.SetFailure("BoundaryFilter", "dominant chain 经过边界一致性过滤后剩余站点不足。说明 station 里混入了较多非边界点。");
                diagnostics.builtPathVertexCount = dominantChain != null ? dominantChain.Count : 0;
                return false;
            }

            var boundaryCorridorPatch = BuildBoundaryGuidedCorridorPatch(work, dominantChain);
            if (boundaryCorridorPatch != null && boundaryCorridorPatch.Count > 0)
            {
                var stationCenterGuideSamples = BuildStationCenterGuideSamples(work, dominantChain);
                var stationLeftGuideSamples = BuildStationLeftGuideSamples(work, dominantChain);
                var stationRightGuideSamples = BuildStationRightGuideSamples(work, dominantChain);

                var expandedBoundaryPatch = RebuildExpandedBoundaryPatch(
                    work,
                    dominantChain,
                    insertEps,
                    surfaceBandRadius,
                    stationCenterGuideSamples,
                    stationLeftGuideSamples,
                    stationRightGuideSamples);

                // 关键补充：不能只把 left / right 连续边界链拿来“选候选三角”，
                // 还要先在整个 boundary corridor patch 里强制做一轮局部细分 + guide 驱动的边插点。
                // 否则像用户截图里那种“缝内只伸进去一角的大三角”，即使落在 corridor 里，
                // 也仍然可能因为没有形成真实交边而完全进不了 split candidate，最后白边还是出不来。
                if (RefineBoundaryPatchAgainstGuideSamples(
                        work,
                        expandedBoundaryPatch,
                        insertEps,
                        surfaceBandRadius,
                        stationCenterGuideSamples,
                        stationLeftGuideSamples,
                        stationRightGuideSamples))
                {
                    expandedBoundaryPatch = RebuildExpandedBoundaryPatch(
                        work,
                        dominantChain,
                        insertEps,
                        surfaceBandRadius,
                        stationCenterGuideSamples,
                        stationLeftGuideSamples,
                        stationRightGuideSamples);
                }

                CapturePatchTriangles(debugCapture, work, meshTransform, expandedBoundaryPatch, IncisionPatchDebugKind.CutPatch, layer);

                // 关键补充：不再只沿 station 中线补切，而是把 left / right 连续边界链也当成真实切分导线。
                // 这样蓝线本身会更容易变成真实网格边，同时凡是与 left / right 蓝线相交的三角面，
                // 也会被纳入上游 split candidate，而不是等到后面的删面阶段才发现“缝内只是大三角的一部分”。
                MergeBoundaryDrivenSplitCandidates(
                    work,
                    stationCenterGuideSamples,
                    expandedBoundaryPatch,
                    insertEps,
                    surfaceBandRadius,
                    openWidthWorld,
                    profileVariation,
                    curvatureWidthScale,
                    layer,
                    edgeVertexCache,
                    duplicateCache,
                    candidateByTri,
                    preparedByTri,
                    selectedTriangles,
                    cutCandidates);

                MergeBoundaryDrivenSplitCandidates(
                    work,
                    stationLeftGuideSamples,
                    expandedBoundaryPatch,
                    insertEps,
                    surfaceBandRadius,
                    openWidthWorld,
                    profileVariation,
                    curvatureWidthScale,
                    layer,
                    edgeVertexCache,
                    duplicateCache,
                    candidateByTri,
                    preparedByTri,
                    selectedTriangles,
                    cutCandidates);

                MergeBoundaryDrivenSplitCandidates(
                    work,
                    stationRightGuideSamples,
                    expandedBoundaryPatch,
                    insertEps,
                    surfaceBandRadius,
                    openWidthWorld,
                    profileVariation,
                    curvatureWidthScale,
                    layer,
                    edgeVertexCache,
                    duplicateCache,
                    candidateByTri,
                    preparedByTri,
                    selectedTriangles,
                    cutCandidates);
            }

            diagnostics.builtPathVertexCount = dominantChain.Count;
            cutCandidates.Sort((x, y) => x.globalT.CompareTo(y.globalT));

            result.workMesh = work;
            result.spineSamples = samples;
            result.cutCandidates = cutCandidates;
            result.preparedByTri = preparedByTri;
            result.pendingSegments = pendingLipSegments;
            result.dominantChain = dominantChain;
            result.selectedTriangles = selectedTriangles;
            result.exciseRadius = exciseRadius;
            return true;
        }

        private static bool CommitPreparedIncisionGraph(
            Mesh mesh,
            Transform meshTransform,
            IncisionGraphResult graph,
            out List<IncisionLipPair> lipPairs,
            out List<PendingLipStation> stations,
            out List<Vector3> centerlineWorld,
            out List<Vector3> stationCentersWorld,
            bool applyToMesh = true)
        {
            lipPairs = new List<IncisionLipPair>();
            stations = new List<PendingLipStation>();
            centerlineWorld = new List<Vector3>();
            stationCentersWorld = new List<Vector3>();
            if (mesh == null || meshTransform == null || graph == null || graph.workMesh == null || graph.preparedByTri == null || graph.selectedTriangles == null || graph.dominantChain == null)
                return false;

            var data = graph.workMesh;
            var originalTriangles = new List<int>(data.triangles);
            int originalTriCount = originalTriangles.Count / 3;
            var newTriangles = new List<int>(originalTriangles.Count + graph.selectedTriangles.Count * 6);
            for (int tri = 0; tri < originalTriCount; tri++)
            {
                int baseIndex = tri * 3;
                int a = originalTriangles[baseIndex];
                int b = originalTriangles[baseIndex + 1];
                int c = originalTriangles[baseIndex + 2];

                if (!graph.selectedTriangles.Contains(tri) || !graph.preparedByTri.TryGetValue(tri, out var prep))
                {
                    newTriangles.Add(a); newTriangles.Add(b); newTriangles.Add(c);
                    continue;
                }

                if (prep.lonelyPositive)
                {
                    AddOrientedTriangle(newTriangles, data.vertices, prep.lonelyVertex, prep.dupA, prep.dupB, prep.refNormal);
                    AddOrientedTriangle(newTriangles, data.vertices, prep.cutA, prep.otherA, prep.otherB, prep.refNormal);
                    AddOrientedTriangle(newTriangles, data.vertices, prep.cutA, prep.otherB, prep.cutB, prep.refNormal);
                }
                else
                {
                    AddOrientedTriangle(newTriangles, data.vertices, prep.lonelyVertex, prep.cutA, prep.cutB, prep.refNormal);
                    AddOrientedTriangle(newTriangles, data.vertices, prep.dupA, prep.otherA, prep.otherB, prep.refNormal);
                    AddOrientedTriangle(newTriangles, data.vertices, prep.dupA, prep.otherB, prep.dupB, prep.refNormal);
                }
            }

            data.triangles.Clear();
            data.triangles.AddRange(newTriangles);
            ApplyStationBasedLipOffsets(data, graph.dominantChain);
            stations = CloneStationChain(graph.dominantChain);
            lipPairs = BuildLipPairsFromStationChain(graph.dominantChain, data);
            SortAndCompactIncisionLipPairs(lipPairs, Mathf.Max(graph.exciseRadius * 0.35f, 1e-5f), null);
            RemoveDegenerateTriangles(data, Mathf.Max(graph.exciseRadius * 0.06f, 1e-7f), Mathf.Max(graph.exciseRadius * 0.18f, 1e-5f), null, meshTransform, graph.layer);
            ExciseResidualBridgeTriangles(data, graph.dominantChain, graph.exciseRadius, null, meshTransform, graph.layer);
            RemoveDegenerateTriangles(data, Mathf.Max(graph.exciseRadius * 0.06f, 1e-7f), Mathf.Max(graph.exciseRadius * 0.18f, 1e-5f), null, meshTransform, graph.layer);
            if (applyToMesh && mesh != null)
                data.ApplyToMesh(mesh);

            for (int i = 0; i < graph.dominantChain.Count; i++)
            {
                stationCentersWorld.Add(meshTransform.TransformPoint(graph.dominantChain[i].seedCenterLocal));
            }
            var centerlineLocal = BuildCenterlineFromStationChain(graph.dominantChain, Mathf.Max(graph.exciseRadius * 2.0f, 1e-4f));
            if (centerlineLocal != null)
            {
                for (int i = 0; i < centerlineLocal.Count; i++)
                    centerlineWorld.Add(meshTransform.TransformPoint(centerlineLocal[i]));
            }
            return stations != null && stations.Count >= 2;
        }

        private static bool RunIncisionPathDiagnostics(
            Mesh mesh,
            Transform meshTransform,
            List<Vector3> strokeWorld,
            float guideRadiusWorld,
            float pathSampleSpacingWorld,
            int localSubdivisionPasses,
            int layer,
            out IncisionPathDiagnostics diagnostics)
        {
            diagnostics = new IncisionPathDiagnostics();
            diagnostics.meshLabel = mesh != null ? mesh.name : "null";
            diagnostics.layer = layer;

            if (mesh == null || meshTransform == null || strokeWorld == null || strokeWorld.Count < 2)
            {
                diagnostics.SetFailure("Input", "mesh / transform / stroke 无效或点数不足。");
                return false;
            }

            diagnostics.strokePointCount = strokeWorld.Count;
            for (int i = 0; i < strokeWorld.Count - 1; i++)
            {
                float segLen = Vector3.Distance(strokeWorld[i], strokeWorld[i + 1]);
                diagnostics.strokeTotalLength += segLen;
                diagnostics.strokeMaxSegmentLength = Mathf.Max(diagnostics.strokeMaxSegmentLength, segLen);
            }

            Vector3[] srcVertices = mesh.vertices;
            int[] srcTriangles = mesh.triangles;
            if (srcVertices == null || srcVertices.Length < 3 || srcTriangles == null || srcTriangles.Length < 3)
            {
                diagnostics.SetFailure("Input", "mesh 顶点或三角形为空。");
                return false;
            }

            float meshRadius = GetAverageMeshRadius(srcVertices);
            List<Vector2> stroke2D = BuildUnwrappedStroke2D(strokeWorld, meshTransform, meshRadius);
            if (stroke2D == null || stroke2D.Count < 2)
            {
                diagnostics.SetFailure("Stroke2D", "程序线未能转换成有效的展开 2D 轨迹。");
                return false;
            }

            Vector2[] projectedVertices2D = BuildProjectedVertices2D(srcVertices, stroke2D, meshRadius);
            float avgEdgeLen = ComputeAverageEdgeLength3D(srcVertices, srcTriangles);
            diagnostics.avgTriangleEdgeLength = avgEdgeLen;
            float sampleSpacing = pathSampleSpacingWorld > 1e-6f ? pathSampleSpacingWorld : Mathf.Max(1e-5f, avgEdgeLen * 0.55f);
            diagnostics.sampleSpacing = sampleSpacing;
            float insertEps = Mathf.Max(1e-5f, Mathf.Min(Mathf.Max(guideRadiusWorld * 0.18f, avgEdgeLen * 0.12f), Mathf.Max(avgEdgeLen * 0.5f, guideRadiusWorld)));
            diagnostics.insertEpsilon = insertEps;

            var work = PreciseIncisionMesh.Create(mesh, stroke2D, meshTransform, meshRadius);
            if (work == null || work.VertexCount < 3 || work.TriangleCount < 1)
            {
                diagnostics.SetFailure("WorkMesh", "局部工作网格构建失败。");
                return false;
            }

            List<StrokeSample> samples = BuildResampledStrokeSamples(strokeWorld, meshTransform, meshRadius, sampleSpacing);
            diagnostics.resampledSampleCount = samples != null ? samples.Count : 0;
            if (samples == null || samples.Count < 2)
            {
                diagnostics.SetFailure("Resample", "重采样后的程序线点数不足 2。");
                return false;
            }

            float densifiedStep = Mathf.Max(1e-5f, Mathf.Min(sampleSpacing, avgEdgeLen * 0.35f));
            samples = DensifyStrokeSamplesForIncisionBand(samples, densifiedStep);
            diagnostics.densifiedSampleCount = samples != null ? samples.Count : 0;
            if (samples == null || samples.Count < 2)
            {
                diagnostics.SetFailure("Densify", "局部加密后的程序线点数不足 2。");
                return false;
            }

            float surfaceBandRadius = Mathf.Max(guideRadiusWorld * 1.75f, insertEps * 3.0f);
            diagnostics.surfaceBandRadius = surfaceBandRadius;
            var refinementPatch = BuildIncisionLocalPatch(work, samples, insertEps, surfaceBandRadius, 1);
            for (int subPass = 0; subPass < Mathf.Clamp(localSubdivisionPasses, 0, 2) && refinementPatch.Count > 0; subPass++)
            {
                if (!SubdivideIncisionPatchOnce(work, refinementPatch, insertEps))
                    break;

                refinementPatch = BuildIncisionLocalPatch(work, samples, insertEps, surfaceBandRadius, 1);
            }
            RefineIncisionBandLocally(work, samples, insertEps, surfaceBandRadius, refinementPatch);

            List<int> meshPath;
            bool ok = BuildPreciseIncisionPolyline(work, samples, insertEps, surfaceBandRadius, out meshPath, diagnostics);
            if (!ok)
                return false;

            RemoveConsecutiveDuplicates(meshPath);
            RemovePathLoops(meshPath);
            diagnostics.builtPathVertexCount = meshPath != null ? meshPath.Count : 0;
            if (meshPath == null || meshPath.Count < 2)
            {
                diagnostics.SetFailure("ChainPost", "meshPath 去重/去环后不足 2 个点。");
                return false;
            }

            return true;
        }

        private static SegmentIntersectionDebugInfo EvaluateSegmentIntersectionDebugInfo(
            PreciseIncisionMesh data,
            StrokeSample start,
            StrokeSample end,
            float epsilon,
            float surfaceBandRadius)
        {
            var info = new SegmentIntersectionDebugInfo();
            if (data == null)
                return info;

            float bandSq = surfaceBandRadius > 1e-6f ? surfaceBandRadius * surfaceBandRadius : float.PositiveInfinity;
            var crossed = new List<string>(8);
            int triCount = data.TriangleCount;
            for (int tri = 0; tri < triCount; tri++)
            {
                data.GetTriangle(tri, out int a, out int b, out int c);
                bool candidate = false;
                int hitCount = 0;
                if (DistanceSegmentSegmentSq(start.local, end.local, data.vertices[a], data.vertices[b]) <= bandSq)
                {
                    candidate = true;
                    if (TryIntersectSegmentsRobust(start.p2d, end.p2d, data.projected2D[a], data.projected2D[b], epsilon, out _, out _, out _))
                        hitCount++;
                }
                if (DistanceSegmentSegmentSq(start.local, end.local, data.vertices[b], data.vertices[c]) <= bandSq)
                {
                    candidate = true;
                    if (TryIntersectSegmentsRobust(start.p2d, end.p2d, data.projected2D[b], data.projected2D[c], epsilon, out _, out _, out _))
                        hitCount++;
                }
                if (DistanceSegmentSegmentSq(start.local, end.local, data.vertices[c], data.vertices[a]) <= bandSq)
                {
                    candidate = true;
                    if (TryIntersectSegmentsRobust(start.p2d, end.p2d, data.projected2D[c], data.projected2D[a], epsilon, out _, out _, out _))
                        hitCount++;
                }

                if (candidate)
                    info.candidateTriangleCount++;
                if (hitCount > 0)
                {
                    info.crossedTriangleCount++;
                    info.maxTriangleHitCount = Mathf.Max(info.maxTriangleHitCount, hitCount);
                    if (crossed.Count < 8)
                        crossed.Add($"tri={tri}:hit={hitCount}");
                }
            }
            info.crossedTriangleSummary = crossed.Count > 0 ? string.Join(", ", crossed) : "none";
            return info;
        }

        private static List<StrokeSample> BuildResampledStrokeSamples(
            List<Vector3> strokeWorld,
            Transform meshTransform,
            float meshRadius,
            float spacing)
        {
            if (strokeWorld == null || strokeWorld.Count < 2 || meshTransform == null)
                return null;

            var local = new List<Vector3>(strokeWorld.Count);
            for (int i = 0; i < strokeWorld.Count; i++)
                local.Add(meshTransform.InverseTransformPoint(strokeWorld[i]));

            var raw2D = new List<Vector2>(strokeWorld.Count);
            float prevTheta = 0f;
            bool hasPrev = false;
            for (int i = 0; i < local.Count; i++)
            {
                Vector3 lp = local[i];
                float theta = Mathf.Atan2(lp.y, lp.x);
                if (hasPrev) theta = UnwrapAngleNear(theta, prevTheta);
                raw2D.Add(new Vector2(lp.z, theta * Mathf.Max(meshRadius, 1e-6f)));
                prevTheta = theta;
                hasPrev = true;
            }

            float step = Mathf.Max(1e-5f, spacing);
            var result = new List<StrokeSample>(raw2D.Count * 2);
            result.Add(new StrokeSample { local = local[0], p2d = raw2D[0] });

            float accumulated = 0f;
            float nextTarget = step;
            for (int i = 0; i < raw2D.Count - 1; i++)
            {
                Vector2 a2 = raw2D[i];
                Vector2 b2 = raw2D[i + 1];
                Vector3 a3 = local[i];
                Vector3 b3 = local[i + 1];
                float segLen = Vector2.Distance(a2, b2);
                if (segLen < 1e-8f)
                    continue;

                while (accumulated + segLen >= nextTarget)
                {
                    float t = (nextTarget - accumulated) / segLen;
                    t = Mathf.Clamp01(t);
                    result.Add(new StrokeSample
                    {
                        local = Vector3.Lerp(a3, b3, t),
                        p2d = Vector2.Lerp(a2, b2, t),
                    });
                    nextTarget += step;
                }

                accumulated += segLen;
            }

            StrokeSample last = new StrokeSample { local = local[local.Count - 1], p2d = raw2D[raw2D.Count - 1] };
            if (result.Count == 0 || (result[result.Count - 1].p2d - last.p2d).sqrMagnitude > 1e-10f)
                result.Add(last);

            return result;
        }

        private static List<StrokeSample> DensifyStrokeSamplesForIncisionBand(List<StrokeSample> samples, float maxStep)
        {
            if (samples == null || samples.Count < 2)
                return samples;

            float step = Mathf.Max(1e-5f, maxStep);
            var result = new List<StrokeSample>(samples.Count * 2);
            result.Add(samples[0]);

            for (int i = 0; i < samples.Count - 1; i++)
            {
                StrokeSample a = samples[i];
                StrokeSample b = samples[i + 1];
                float segLen = Vector2.Distance(a.p2d, b.p2d);
                if (segLen > step)
                {
                    int segmentCount = Mathf.Clamp(Mathf.CeilToInt(segLen / step), 1, 32);
                    for (int s = 1; s < segmentCount; s++)
                    {
                        float t = (float)s / segmentCount;
                        result.Add(new StrokeSample
                        {
                            local = Vector3.Lerp(a.local, b.local, t),
                            p2d = Vector2.Lerp(a.p2d, b.p2d, t),
                        });
                    }
                }

                if ((result[result.Count - 1].p2d - b.p2d).sqrMagnitude > 1e-12f)
                    result.Add(b);
            }

            return result;
        }

        private static bool SubdivideIncisionPatchOnce(
            PreciseIncisionMesh data,
            HashSet<int> patchTriangles,
            float insertEpsilon)
        {
            if (data == null || patchTriangles == null || patchTriangles.Count == 0)
                return false;

            var originalTriangles = new List<int>(data.triangles);
            int originalTriCount = originalTriangles.Count / 3;
            var midpointCache = new Dictionary<ulong, int>();
            bool changed = false;

            for (int tri = 0; tri < originalTriCount; tri++)
            {
                if (!patchTriangles.Contains(tri))
                    continue;

                int baseIndex = tri * 3;
                int a = originalTriangles[baseIndex];
                int b = originalTriangles[baseIndex + 1];
                int c = originalTriangles[baseIndex + 2];
                if (a == b || b == c || c == a)
                    continue;

                int ab = GetOrCreateSubdivisionEdgeMidpoint(data, a, b, midpointCache, insertEpsilon);
                int bc = GetOrCreateSubdivisionEdgeMidpoint(data, b, c, midpointCache, insertEpsilon);
                int ca = GetOrCreateSubdivisionEdgeMidpoint(data, c, a, midpointCache, insertEpsilon);
                if (ab < 0 || bc < 0 || ca < 0)
                    continue;

                changed = true;
            }

            return changed;
        }

        private static int GetOrCreateSubdivisionEdgeMidpoint(
            PreciseIncisionMesh data,
            int a,
            int b,
            Dictionary<ulong, int> midpointCache,
            float insertEpsilon)
        {
            ulong key = MakeUndirectedEdgeKey(a, b);
            if (midpointCache.TryGetValue(key, out int existing))
                return existing;

            Vector3 localPoint = 0.5f * (data.vertices[a] + data.vertices[b]);
            Vector2 point2D = 0.5f * (data.projected2D[a] + data.projected2D[b]);
            int idx = SplitEdgeAndInsertVertex(data, a, b, localPoint, point2D, 0.5f, Mathf.Max(1e-6f, insertEpsilon * 0.25f));
            midpointCache[key] = idx;
            return idx;
        }

        private static void RefineIncisionBandLocally(
            PreciseIncisionMesh data,
            List<StrokeSample> samples,
            float insertEpsilon,
            float surfaceBandRadius,
            HashSet<int> allowedTriangles = null)
        {
            if (data == null || samples == null || samples.Count < 2)
                return;

            const int maxPasses = 3;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool changed = false;
                for (int i = 0; i < samples.Count - 1; i++)
                {
                    if (RefineIncisionSegmentLocally(data, samples[i], samples[i + 1], insertEpsilon, surfaceBandRadius, allowedTriangles))
                        changed = true;
                }

                if (!changed)
                    break;
            }
        }

        private static bool RefineIncisionSegmentLocally(
            PreciseIncisionMesh data,
            StrokeSample start,
            StrokeSample end,
            float insertEpsilon,
            float surfaceBandRadius,
            HashSet<int> allowedTriangles = null)
        {
            int startIndex = FindOrInsertStrokeSampleVertex(data, start, insertEpsilon, surfaceBandRadius, allowedTriangles);
            int endIndex = FindOrInsertStrokeSampleVertex(data, end, insertEpsilon, surfaceBandRadius, allowedTriangles);
            if (startIndex < 0 || endIndex < 0 || startIndex == endIndex)
                return false;

            var crossings = CollectSegmentEdgeCrossings(
                data,
                start.local,
                end.local,
                start.p2d,
                end.p2d,
                startIndex,
                endIndex,
                insertEpsilon,
                surfaceBandRadius,
                allowedTriangles);

            bool changed = false;
            for (int i = 0; i < crossings.Count; i++)
            {
                int newIndex = SplitEdgeAndInsertVertex(data, crossings[i], insertEpsilon);
                if (newIndex >= 0)
                    changed = true;
            }

            return changed;
        }

        private static bool BuildPreciseIncisionPolyline(
            PreciseIncisionMesh data,
            List<StrokeSample> samples,
            float insertEpsilon,
            float surfaceBandRadius,
            out List<int> meshPath,
            IncisionPathDiagnostics diagnostics = null)
        {
            meshPath = new List<int>();
            if (data == null || samples == null || samples.Count < 2)
                return false;

            var sampleVertexIndices = new int[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                sampleVertexIndices[i] = FindOrInsertStrokeSampleVertex(data, samples[i], insertEpsilon, surfaceBandRadius);
                if (sampleVertexIndices[i] < 0)
                {
                    if (diagnostics != null)
                    {
                        diagnostics.startEndInsertionFailures++;
                        diagnostics.SetFailure("InsertSample", $"sample={i} 未能插入/找到对应网格顶点。");
                    }
                    return false;
                }
            }

            meshPath.Add(sampleVertexIndices[0]);

            for (int i = 0; i < samples.Count - 1; i++)
            {
                if (diagnostics != null)
                    diagnostics.totalSegments++;

                int startIndex = sampleVertexIndices[i];
                int endIndex = sampleVertexIndices[i + 1];
                if (startIndex < 0 || endIndex < 0)
                {
                    diagnostics?.SetFailure("SegmentEndpoint", $"segment={i} 的端点索引无效。");
                    return false;
                }

                SegmentIntersectionDebugInfo segInfo = diagnostics != null
                    ? EvaluateSegmentIntersectionDebugInfo(data, samples[i], samples[i + 1], insertEpsilon, surfaceBandRadius)
                    : default;

                var crossings = CollectSegmentEdgeCrossings(data, samples[i].local, samples[i + 1].local, samples[i].p2d, samples[i + 1].p2d, startIndex, endIndex, insertEpsilon, surfaceBandRadius);
                crossings.Sort((x, y) => x.tSegment.CompareTo(y.tSegment));

                if (diagnostics != null)
                {
                    if (segInfo.candidateTriangleCount <= 0)
                        diagnostics.zeroCandidateSegments++;
                    if (crossings.Count <= 0)
                        diagnostics.zeroCrossingSegments++;
                    else if (crossings.Count < 2)
                        diagnostics.weakCrossingSegments++;

                    diagnostics.segmentLogs.Add($"[Incision][Segment][{(diagnostics.layer == (int)VesselLayer.Inner ? "Inner" : "Outer")}] seg={i} candidates={segInfo.candidateTriangleCount} crossedTris={segInfo.crossedTriangleCount} maxTriHit={segInfo.maxTriangleHitCount} crossings={crossings.Count} summary={segInfo.crossedTriangleSummary}");
                }

                int lastVertex = startIndex;
                int addedThisSegment = 0;
                for (int c = 0; c < crossings.Count; c++)
                {
                    int crossVertex = SplitEdgeAndInsertVertex(data, crossings[c], insertEpsilon);
                    if (crossVertex >= 0 && crossVertex != lastVertex)
                    {
                        meshPath.Add(crossVertex);
                        lastVertex = crossVertex;
                        addedThisSegment++;
                    }
                }

                if (endIndex != lastVertex)
                {
                    meshPath.Add(endIndex);
                    if (addedThisSegment == 0 && diagnostics != null && crossings.Count == 0)
                        diagnostics.disconnectedSegments++;
                }
            }

            if (meshPath.Count < 2)
            {
                diagnostics?.SetFailure("ChainBuild", "meshPath 构建后不足 2 个点。");
                return false;
            }

            return true;
        }

        private static int FindOrInsertStrokeSampleVertex(PreciseIncisionMesh data, StrokeSample sample, float insertEpsilon, float surfaceBandRadius, HashSet<int> allowedTriangles = null)
        {
            int nearVertex = FindExistingVertexNear(data, sample.local, sample.p2d, insertEpsilon);
            if (nearVertex >= 0)
                return nearVertex;

            int triIndex;
            Vector3 bary;
            if (!TryFindContainingTriangle(data, sample.local, sample.p2d, surfaceBandRadius, out triIndex, out bary, allowedTriangles))
                return -1;

            data.GetTriangle(triIndex, out int a, out int b, out int c);

            const float edgeTol = 1e-4f;
            if (bary.z <= edgeTol)
            {
                float t = GetSegmentParam2D(sample.p2d, data.projected2D[a], data.projected2D[b]);
                return SplitEdgeAndInsertVertex(data, a, b, sample.local, sample.p2d, Mathf.Clamp01(t), insertEpsilon);
            }

            if (bary.x <= edgeTol)
            {
                float t = GetSegmentParam2D(sample.p2d, data.projected2D[b], data.projected2D[c]);
                return SplitEdgeAndInsertVertex(data, b, c, sample.local, sample.p2d, Mathf.Clamp01(t), insertEpsilon);
            }

            if (bary.y <= edgeTol)
            {
                float t = GetSegmentParam2D(sample.p2d, data.projected2D[c], data.projected2D[a]);
                return SplitEdgeAndInsertVertex(data, c, a, sample.local, sample.p2d, Mathf.Clamp01(t), insertEpsilon);
            }

            return SplitTriangleAtPoint(data, triIndex, sample.local, sample.p2d, bary);
        }

        private static int FindExistingVertexNear(PreciseIncisionMesh data, Vector3 localPoint, Vector2 point2D, float epsilon)
        {
            float eps2dSq = Mathf.Max(1e-10f, epsilon * epsilon);
            float eps3dSq = Mathf.Max(1e-10f, epsilon * epsilon);
            int best = -1;
            float bestScore = float.PositiveInfinity;

            for (int i = 0; i < data.vertices.Count; i++)
            {
                float d2 = (data.projected2D[i] - point2D).sqrMagnitude;
                if (d2 > eps2dSq)
                    continue;

                float d3 = (data.vertices[i] - localPoint).sqrMagnitude;
                if (d3 > eps3dSq * 4f)
                    continue;

                float score = d2 + d3;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static bool TryFindContainingTriangle(PreciseIncisionMesh data, Vector3 localPoint, Vector2 point2D, float searchRadius, out int triangleIndex, out Vector3 bary, HashSet<int> allowedTriangles = null)
        {
            triangleIndex = -1;
            bary = Vector3.zero;

            float bestScore = float.PositiveInfinity;
            float radiusSq = searchRadius > 1e-6f ? searchRadius * searchRadius : float.PositiveInfinity;
            for (int t = 0; t < data.TriangleCount; t++)
            {
                if (allowedTriangles != null && !allowedTriangles.Contains(t))
                    continue;

                data.GetTriangle(t, out int a, out int b, out int c);
                Vector2 p0 = data.projected2D[a];
                Vector2 p1 = data.projected2D[b];
                Vector2 p2 = data.projected2D[c];

                Vector3 bcc;
                if (!TryComputeBarycentric2D(point2D, p0, p1, p2, out bcc))
                    continue;

                Vector3 cp = ClosestPointOnTriangle(localPoint, data.vertices[a], data.vertices[b], data.vertices[c]);
                float d3sq = (cp - localPoint).sqrMagnitude;
                if (d3sq > radiusSq)
                    continue;

                float minB = Mathf.Min(bcc.x, Mathf.Min(bcc.y, bcc.z));
                float penalty = minB >= -1e-4f ? 0f : -minB;
                float score = penalty * 1000f + d3sq;
                if (score < bestScore)
                {
                    bestScore = score;
                    triangleIndex = t;
                    bary = bcc;
                    if (penalty <= 0f && d3sq <= Mathf.Max(1e-10f, radiusSq * 0.1f))
                        break;
                }
            }

            return triangleIndex >= 0;
        }

        private static bool TryComputeBarycentric2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 bary)
        {
            bary = Vector3.zero;
            float area = Cross2D(b - a, c - a);
            if (Mathf.Abs(area) < 1e-10f)
                return false;

            float inv = 1f / area;
            float w0 = Cross2D(b - p, c - p) * inv;
            float w1 = Cross2D(c - p, a - p) * inv;
            float w2 = 1f - w0 - w1;
            bary = new Vector3(w0, w1, w2);
            return true;
        }

        private static float Cross2D(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static int SplitTriangleAtPoint(PreciseIncisionMesh data, int triIndex, Vector3 localPoint, Vector2 point2D, Vector3 bary)
        {
            data.GetTriangle(triIndex, out int a, out int b, out int c);
            int newIndex = data.AddVertex(localPoint, point2D, bary, a, b, c);

            data.SetTriangle(triIndex, a, b, newIndex);
            data.AddTriangle(b, c, newIndex);
            data.AddTriangle(c, a, newIndex);
            return newIndex;
        }

        private static int SplitEdgeAndInsertVertex(PreciseIncisionMesh data, EdgeCrossing crossing, float insertEpsilon)
        {
            return SplitEdgeAndInsertVertex(data, crossing.a, crossing.b, crossing.pointLocal, crossing.point2D, Mathf.Clamp01(crossing.tEdge), insertEpsilon);
        }

        private static int SplitEdgeAndInsertVertex(PreciseIncisionMesh data, int a, int b, Vector3 localPoint, Vector2 point2D, float edgeT, float insertEpsilon)
        {
            int existing = FindExistingVertexNear(data, localPoint, point2D, insertEpsilon);
            if (existing >= 0)
                return existing;

            edgeT = Mathf.Clamp01(edgeT);
            int newIndex = data.AddVertexOnEdge(localPoint, point2D, a, b, edgeT);

            int originalTriangleCount = data.TriangleCount;
            for (int tri = 0; tri < originalTriangleCount; tri++)
            {
                data.GetTriangle(tri, out int i0, out int i1, out int i2);
                if (!TriangleContainsBoth(i0, i1, i2, a, b))
                    continue;

                if (i0 == a && i1 == b)
                {
                    data.SetTriangle(tri, a, newIndex, i2);
                    data.AddTriangle(newIndex, b, i2);
                }
                else if (i1 == a && i2 == b)
                {
                    data.SetTriangle(tri, a, newIndex, i0);
                    data.AddTriangle(newIndex, b, i0);
                }
                else if (i2 == a && i0 == b)
                {
                    data.SetTriangle(tri, a, newIndex, i1);
                    data.AddTriangle(newIndex, b, i1);
                }
                else if (i0 == b && i1 == a)
                {
                    data.SetTriangle(tri, b, newIndex, i2);
                    data.AddTriangle(newIndex, a, i2);
                }
                else if (i1 == b && i2 == a)
                {
                    data.SetTriangle(tri, b, newIndex, i0);
                    data.AddTriangle(newIndex, a, i0);
                }
                else if (i2 == b && i0 == a)
                {
                    data.SetTriangle(tri, b, newIndex, i1);
                    data.AddTriangle(newIndex, a, i1);
                }
            }

            return newIndex;
        }

        private static bool TriangleContainsBoth(int i0, int i1, int i2, int a, int b)
        {
            bool hasA = (i0 == a || i1 == a || i2 == a);
            bool hasB = (i0 == b || i1 == b || i2 == b);
            return hasA && hasB;
        }

        private static List<EdgeCrossing> CollectSegmentEdgeCrossings(
            PreciseIncisionMesh data,
            Vector3 segALocal,
            Vector3 segBLocal,
            Vector2 segA,
            Vector2 segB,
            int startIndex,
            int endIndex,
            float epsilon,
            float surfaceBandRadius,
            HashSet<int> allowedTriangles = null)
        {
            var result = new List<EdgeCrossing>(16);
            var seen = new HashSet<ulong>();

            int triCount = data.TriangleCount;
            for (int tri = 0; tri < triCount; tri++)
            {
                if (allowedTriangles != null && !allowedTriangles.Contains(tri))
                    continue;

                data.GetTriangle(tri, out int a, out int b, out int c);
                CollectSegmentEdgeCrossingForEdge(data, segALocal, segBLocal, segA, segB, startIndex, endIndex, a, b, epsilon, surfaceBandRadius, seen, result);
                CollectSegmentEdgeCrossingForEdge(data, segALocal, segBLocal, segA, segB, startIndex, endIndex, b, c, epsilon, surfaceBandRadius, seen, result);
                CollectSegmentEdgeCrossingForEdge(data, segALocal, segBLocal, segA, segB, startIndex, endIndex, c, a, epsilon, surfaceBandRadius, seen, result);
            }

            result.Sort((x, y) => x.tSegment.CompareTo(y.tSegment));
            for (int i = result.Count - 1; i > 0; i--)
            {
                if (Mathf.Abs(result[i].tSegment - result[i - 1].tSegment) <= 1e-5f)
                    result.RemoveAt(i);
            }

            return result;
        }

        private static void CollectSegmentEdgeCrossingForEdge(
            PreciseIncisionMesh data,
            Vector3 segALocal,
            Vector3 segBLocal,
            Vector2 segA,
            Vector2 segB,
            int startIndex,
            int endIndex,
            int a,
            int b,
            float epsilon,
            float surfaceBandRadius,
            HashSet<ulong> seen,
            List<EdgeCrossing> result)
        {
            if (a == b)
                return;

            if (a == startIndex || b == startIndex || a == endIndex || b == endIndex)
                return;

            ulong key = MakeUndirectedEdgeKey(a, b);
            if (!seen.Add(key))
                return;

            if (surfaceBandRadius > 1e-6f)
            {
                float bandSq = surfaceBandRadius * surfaceBandRadius;
                float distSq = DistanceSegmentSegmentSq(segALocal, segBLocal, data.vertices[a], data.vertices[b]);
                if (distSq > bandSq)
                    return;
            }

            AddRobustSegmentEdgeCrossings(data, segALocal, segBLocal, segA, segB, a, b, epsilon, result);
        }

        private static ulong MakeUndirectedEdgeKey(int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            return ((ulong)(uint)min << 32) | (uint)max;
        }

        private static ulong MakeCrossingEndpointKey(int a, int b, float tEdge)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            float t = Mathf.Clamp01(tEdge);
            if (a > b)
                t = 1f - t;

            uint q = (uint)Mathf.Clamp(Mathf.RoundToInt(t * 1048575f), 0, 1048575);

            unchecked
            {
                ulong h = 1469598103934665603UL;
                h ^= (uint)min; h *= 1099511628211UL;
                h ^= (uint)max; h *= 1099511628211UL;
                h ^= q; h *= 1099511628211UL;
                return h != 0UL ? h : 1UL;
            }
        }

        private static void AddRobustSegmentEdgeCrossings(
            PreciseIncisionMesh data,
            Vector3 segALocal,
            Vector3 segBLocal,
            Vector2 segA,
            Vector2 segB,
            int a,
            int b,
            float epsilon,
            List<EdgeCrossing> result)
        {
            Vector2 edgeA = data.projected2D[a];
            Vector2 edgeB = data.projected2D[b];

            if (TryGetCollinearOverlap(segA, segB, edgeA, edgeB, epsilon, out float overlapStart, out float overlapEnd))
            {
                float tA = GetSegmentParam2D(edgeA, segA, segB);
                float tB = GetSegmentParam2D(edgeB, segA, segB);
                if (tA > overlapStart - 1e-4f && tA < overlapEnd + 1e-4f)
                {
                    result.Add(new EdgeCrossing
                    {
                        a = a,
                        b = b,
                        tSegment = Mathf.Clamp01(tA),
                        tEdge = 0f,
                        point2D = edgeA,
                        pointLocal = data.vertices[a],
                    });
                }
                if (tB > overlapStart - 1e-4f && tB < overlapEnd + 1e-4f)
                {
                    result.Add(new EdgeCrossing
                    {
                        a = a,
                        b = b,
                        tSegment = Mathf.Clamp01(tB),
                        tEdge = 1f,
                        point2D = edgeB,
                        pointLocal = data.vertices[b],
                    });
                }
                return;
            }

            if (!TryIntersectSegmentsRobust(segA, segB, edgeA, edgeB, epsilon, out float tSeg, out float tEdge, out _))
                return;

            result.Add(new EdgeCrossing
            {
                a = a,
                b = b,
                tSegment = tSeg,
                tEdge = tEdge,
                point2D = Vector2.Lerp(segA, segB, tSeg),
                pointLocal = Vector3.Lerp(data.vertices[a], data.vertices[b], tEdge),
            });
        }

        private static bool TryIntersectSegmentsRobust(
            Vector2 p,
            Vector2 p2,
            Vector2 q,
            Vector2 q2,
            float epsilon,
            out float tP,
            out float tQ,
            out SegmentEdgeIntersectionKind kind)
        {
            tP = 0f;
            tQ = 0f;
            kind = SegmentEdgeIntersectionKind.None;

            Vector2 r = p2 - p;
            Vector2 s = q2 - q;
            float eps = Mathf.Max(1e-5f, epsilon * 0.25f);
            float denom = Cross2D(r, s);

            if (Mathf.Abs(denom) < 1e-10f)
                return false;

            Vector2 qp = q - p;
            tP = Cross2D(qp, s) / denom;
            tQ = Cross2D(qp, r) / denom;

            if (tP < -eps || tP > 1f + eps)
                return false;
            if (tQ < -eps || tQ > 1f + eps)
                return false;

            tP = Mathf.Clamp01(tP);
            tQ = Mathf.Clamp01(tQ);

            bool nearVertex = (tQ <= eps || tQ >= 1f - eps);
            bool nearSegmentEnd = (tP <= eps || tP >= 1f - eps);

            kind = (nearVertex || nearSegmentEnd) ? SegmentEdgeIntersectionKind.VertexHit : SegmentEdgeIntersectionKind.Proper;
            return true;
        }

        private static bool TryGetCollinearOverlap(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, float epsilon, out float overlapStart, out float overlapEnd)
        {
            overlapStart = 0f;
            overlapEnd = 0f;

            Vector2 r = p2 - p;
            float rr = r.sqrMagnitude;
            if (rr <= 1e-12f)
                return false;

            float eps = Mathf.Max(1e-5f, epsilon * 0.5f);
            if (Mathf.Abs(Cross2D(r, q - p)) > eps || Mathf.Abs(Cross2D(r, q2 - p)) > eps)
                return false;

            float t0 = GetSegmentParam2D(q, p, p2);
            float t1 = GetSegmentParam2D(q2, p, p2);
            overlapStart = Mathf.Max(0f, Mathf.Min(t0, t1));
            overlapEnd = Mathf.Min(1f, Mathf.Max(t0, t1));
            return overlapEnd - overlapStart > eps;
        }

        private static void CollapseCrossingsByPoint(List<EdgeCrossing> hits, float epsilon)
        {
            if (hits == null || hits.Count <= 1)
                return;

            float posSq = Mathf.Max(1e-10f, epsilon * epsilon);
            hits.Sort((x, y) => x.tSegment.CompareTo(y.tSegment));
            for (int i = hits.Count - 1; i > 0; i--)
            {
                if ((hits[i].pointLocal - hits[i - 1].pointLocal).sqrMagnitude <= posSq ||
                    (hits[i].point2D - hits[i - 1].point2D).sqrMagnitude <= posSq)
                {
                    var merged = hits[i - 1];
                    merged.tSegment = 0.5f * (hits[i].tSegment + hits[i - 1].tSegment);
                    merged.tEdge = 0.5f * (hits[i].tEdge + hits[i - 1].tEdge);
                    merged.point2D = 0.5f * (hits[i].point2D + hits[i - 1].point2D);
                    merged.pointLocal = 0.5f * (hits[i].pointLocal + hits[i - 1].pointLocal);
                    hits[i - 1] = merged;
                    hits.RemoveAt(i);
                }
            }
        }

        private static void SelectBestCrossingPair(List<EdgeCrossing> hits, out EdgeCrossing a, out EdgeCrossing b)
        {
            a = default;
            b = default;
            if (hits == null || hits.Count == 0)
                return;
            if (hits.Count == 1)
            {
                a = hits[0];
                b = hits[0];
                return;
            }

            float best = float.NegativeInfinity;
            for (int i = 0; i < hits.Count; i++)
            {
                for (int j = i + 1; j < hits.Count; j++)
                {
                    float span = Mathf.Abs(hits[j].tSegment - hits[i].tSegment);
                    if (span > best)
                    {
                        best = span;
                        a = hits[i];
                        b = hits[j];
                    }
                }
            }
        }

        private static float GetSegmentParam2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq <= 1e-12f)
                return 0f;
            return Vector2.Dot(p - a, ab) / lenSq;
        }

        private static bool DuplicatePathAndSeparateSide(
            PreciseIncisionMesh data,
            List<int> path,
            List<Vector2> stroke2D,
            float openWidthWorld,
            float profileVariation,
            float curvatureWidthScale,
            int layer,
            out List<IncisionLipPair> lipPairs,
            out List<Vector3> centerlineLocal,
            IncisionDebugCapture debugCapture = null,
            Transform meshTransform = null)
        {
            lipPairs = new List<IncisionLipPair>();
            centerlineLocal = new List<Vector3>();

            if (data == null || path == null || path.Count < 2)
                return false;

            var pathSet = new HashSet<int>(path);
            var pathEdgeSet = BuildUndirectedPathEdgeSet(path);
            var duplicateMap = new Dictionary<int, int>(pathSet.Count);
            var pathCarrierTriangles = new HashSet<int>();
            var pathVertexTouchOnlyTriangles = new HashSet<int>();
            var duplicatedSideTriangles = new HashSet<int>();

            for (int i = 0; i < path.Count; i++)
            {
                int v = path[i];
                if (duplicateMap.ContainsKey(v))
                    continue;

                int dup = data.vertices.Count;
                duplicateMap.Add(v, dup);
                data.vertices.Add(data.vertices[v]);
                data.projected2D.Add(data.projected2D[v]);
                if (data.hasUV) data.uv.Add(data.uv[v]);
                if (data.hasTangents) data.tangents.Add(data.tangents[v]);
                if (data.hasColors) data.colors.Add(data.colors[v]);
            }

            bool anyReassigned = false;
            int triCount = data.TriangleCount;
            for (int tri = 0; tri < triCount; tri++)
            {
                data.GetTriangle(tri, out int i0, out int i1, out int i2);

                bool hasActualPathCut = TriangleContainsActualPathCut(i0, i1, i2, pathSet, pathEdgeSet);
                int pathVertexCount = 0;
                if (pathSet.Contains(i0)) pathVertexCount++;
                if (pathSet.Contains(i1)) pathVertexCount++;
                if (pathSet.Contains(i2)) pathVertexCount++;

                if (hasActualPathCut)
                    pathCarrierTriangles.Add(tri);
                else if (pathVertexCount >= 2)
                    pathVertexTouchOnlyTriangles.Add(tri);

                if (!hasActualPathCut)
                    continue;

                Vector2 center2D = (data.projected2D[i0] + data.projected2D[i1] + data.projected2D[i2]) / 3f;
                float side = SignedSideToPolyline(center2D, stroke2D);
                if (side <= 1e-7f)
                    continue;

                if (pathSet.Contains(i0)) i0 = duplicateMap[i0];
                if (pathSet.Contains(i1)) i1 = duplicateMap[i1];
                if (pathSet.Contains(i2)) i2 = duplicateMap[i2];
                data.SetTriangle(tri, i0, i1, i2);
                duplicatedSideTriangles.Add(tri);
                anyReassigned = true;
            }

            CapturePatchTriangles(debugCapture, data, meshTransform, pathCarrierTriangles, IncisionPatchDebugKind.PathCarrier, layer);
            CapturePatchTriangles(debugCapture, data, meshTransform, pathVertexTouchOnlyTriangles, IncisionPatchDebugKind.PathVertexTouchOnly, layer);
            CapturePatchTriangles(debugCapture, data, meshTransform, duplicatedSideTriangles, IncisionPatchDebugKind.DuplicatedSide, layer);

            if (!anyReassigned)
                return false;

            Vector2[] projectedArray = data.projected2D.ToArray();
            Vector3[] verticesArray = data.vertices.ToArray();
            var splitDirs = ComputePathSplitDirections(path, verticesArray, projectedArray);

            var emittedPairs = new HashSet<int>();
            for (int pathIndex = 0; pathIndex < path.Count; pathIndex++)
            {
                int leftIndex = path[pathIndex];
                int rightIndex = duplicateMap[leftIndex];
                if (leftIndex == rightIndex)
                    continue;
                if (!emittedPairs.Add(leftIndex))
                    continue;

                Vector3 dir;
                if (!splitDirs.TryGetValue(leftIndex, out dir) || dir.sqrMagnitude < 1e-12f)
                    dir = Vector3.right;
                else
                    dir.Normalize();

                float targetOpen = ComputePathTargetOpenWidth(path, pathIndex, projectedArray, Mathf.Max(0f, openWidthWorld), profileVariation, curvatureWidthScale);
                float halfGap = targetOpen * 0.5f;

                Vector3 center = data.vertices[leftIndex];
                if (halfGap > 0f)
                {
                    data.vertices[leftIndex] = data.vertices[leftIndex] - dir * halfGap;
                    data.vertices[rightIndex] = data.vertices[rightIndex] + dir * halfGap;
                }

                lipPairs.Add(new IncisionLipPair
                {
                    leftLocalIndex = leftIndex,
                    rightLocalIndex = rightIndex,
                    centerLocal = center,
                    splitDirLocal = dir,
                    targetOpen = targetOpen,
                    weight = openWidthWorld > 1e-6f ? Mathf.Clamp01(targetOpen / Mathf.Max(1e-6f, openWidthWorld * 2f)) : 1f,
                    layer = layer
                });

                centerlineLocal.Add(center);
            }

            WidenIncisionBandAroundPath(data, path, duplicateMap, splitDirs, stroke2D, lipPairs, Mathf.Max(0f, openWidthWorld));
            return lipPairs.Count >= 2;
        }

        private static void WidenIncisionBandAroundPath(
            PreciseIncisionMesh data,
            List<int> path,
            Dictionary<int, int> duplicateMap,
            Dictionary<int, Vector3> splitDirs,
            List<Vector2> stroke2D,
            List<IncisionLipPair> lipPairs,
            float openWidthWorld)
        {
            if (data == null || path == null || path.Count < 2 || lipPairs == null || lipPairs.Count < 2)
                return;

            float averagePathEdge = ComputeAveragePathEdgeLength(path, data.vertices);
            float baseOpen = Mathf.Max(openWidthWorld, ComputeAverageLipTargetOpen(lipPairs));
            float influenceRadius = Mathf.Max(baseOpen * 2.4f, averagePathEdge * 1.8f);
            influenceRadius = Mathf.Max(influenceRadius, 1e-4f);
            float maxBandPush = Mathf.Max(baseOpen * 0.42f, averagePathEdge * 0.18f);
            if (maxBandPush <= 1e-6f)
                return;

            var pathSet = new HashSet<int>(path);
            for (int i = 0; i < data.vertices.Count; i++)
            {
                int sourceIndex = i;
                bool isDuplicate = false;
                if (!pathSet.Contains(i))
                {
                    foreach (var kv in duplicateMap)
                    {
                        if (kv.Value == i)
                        {
                            sourceIndex = kv.Key;
                            isDuplicate = true;
                            break;
                        }
                    }
                }

                if (pathSet.Contains(i) || isDuplicate)
                    continue;

                Vector2 p2 = data.projected2D[i];
                float signedSide = SignedSideToPolyline(p2, stroke2D);
                if (Mathf.Abs(signedSide) <= 1e-8f)
                    continue;

                int nearestPathIndex = FindNearestPathVertexIndex(path, data.projected2D, p2, out float nearestDistance);
                if (nearestPathIndex < 0 || nearestDistance >= influenceRadius)
                    continue;

                int pathVertex = path[nearestPathIndex];
                if (!splitDirs.TryGetValue(pathVertex, out Vector3 splitDir) || splitDir.sqrMagnitude <= 1e-12f)
                    continue;

                splitDir.Normalize();
                float sideSign = signedSide > 0f ? 1f : -1f;
                float falloff = 1f - Mathf.Clamp01(nearestDistance / influenceRadius);
                falloff = falloff * falloff;
                float push = maxBandPush * falloff;
                data.vertices[i] += splitDir * (sideSign * push);
            }
        }

        private static int FindNearestPathVertexIndex(List<int> path, List<Vector2> projected2D, Vector2 p, out float bestDistance)
        {
            bestDistance = float.PositiveInfinity;
            if (path == null || projected2D == null)
                return -1;

            int bestIndex = -1;
            for (int i = 0; i < path.Count; i++)
            {
                int v = path[i];
                if (v < 0 || v >= projected2D.Count)
                    continue;

                float d = Vector2.Distance(projected2D[v], p);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static float ComputeAveragePathEdgeLength(List<int> path, List<Vector3> vertices)
        {
            if (path == null || vertices == null || path.Count < 2)
                return 0f;

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                int a = path[i];
                int b = path[i + 1];
                if (a < 0 || b < 0 || a >= vertices.Count || b >= vertices.Count || a == b)
                    continue;
                sum += Vector3.Distance(vertices[a], vertices[b]);
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        private static float ComputeAverageLipTargetOpen(List<IncisionLipPair> lipPairs)
        {
            if (lipPairs == null || lipPairs.Count == 0)
                return 0f;

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < lipPairs.Count; i++)
            {
                if (lipPairs[i].targetOpen <= 1e-6f)
                    continue;
                sum += lipPairs[i].targetOpen;
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        private static HashSet<ulong> BuildUndirectedPathEdgeSet(List<int> path)
        {
            var edgeSet = new HashSet<ulong>();
            if (path == null)
                return edgeSet;

            for (int i = 0; i < path.Count - 1; i++)
            {
                int a = path[i];
                int b = path[i + 1];
                if (a == b)
                    continue;
                edgeSet.Add(MakeUndirectedEdgeKey(a, b));
            }

            return edgeSet;
        }

        private static bool TriangleContainsActualPathCut(int i0, int i1, int i2, HashSet<int> pathSet, HashSet<ulong> pathEdgeSet)
        {
            int pathVertexCount = 0;
            if (pathSet.Contains(i0)) pathVertexCount++;
            if (pathSet.Contains(i1)) pathVertexCount++;
            if (pathSet.Contains(i2)) pathVertexCount++;
            if (pathVertexCount < 2)
                return false;

            return pathEdgeSet.Contains(MakeUndirectedEdgeKey(i0, i1))
                || pathEdgeSet.Contains(MakeUndirectedEdgeKey(i1, i2))
                || pathEdgeSet.Contains(MakeUndirectedEdgeKey(i2, i0));
        }

        private static void SimplifyIncisionLipPairs(List<IncisionLipPair> lipPairs, List<Vector3> centerlineLocal, float minSpacing)
        {
            if (lipPairs == null || lipPairs.Count < 2 || minSpacing <= 1e-6f)
                return;

            float minSq = minSpacing * minSpacing;
            var simplifiedPairs = new List<IncisionLipPair>(lipPairs.Count);
            var simplifiedCenters = new List<Vector3>(centerlineLocal != null ? centerlineLocal.Count : lipPairs.Count);

            for (int i = 0; i < lipPairs.Count; i++)
            {
                var pair = lipPairs[i];
                Vector3 center = pair.centerLocal;
                if (simplifiedPairs.Count == 0)
                {
                    simplifiedPairs.Add(pair);
                    if (centerlineLocal != null) simplifiedCenters.Add(center);
                    continue;
                }

                int lastIndex = simplifiedPairs.Count - 1;
                var prev = simplifiedPairs[lastIndex];
                Vector3 prevCenter = prev.centerLocal;
                bool sameLayer = prev.layer == pair.layer;
                bool sameSideOrder = prev.leftLocalIndex != pair.leftLocalIndex || prev.rightLocalIndex != pair.rightLocalIndex;
                float dirDot = Vector3.Dot(prev.splitDirLocal.normalized, pair.splitDirLocal.normalized);
                if (sameLayer && sameSideOrder && (center - prevCenter).sqrMagnitude < minSq && dirDot > 0.65f)
                {
                    float keep = Mathf.Max(prev.weight, pair.weight);
                    if (pair.targetOpen > prev.targetOpen)
                    {
                        prev.leftLocalIndex = pair.leftLocalIndex;
                        prev.rightLocalIndex = pair.rightLocalIndex;
                        prev.centerLocal = 0.5f * (prev.centerLocal + pair.centerLocal);
                        prev.splitDirLocal = (prev.splitDirLocal + pair.splitDirLocal).sqrMagnitude > 1e-12f ? (prev.splitDirLocal + pair.splitDirLocal).normalized : prev.splitDirLocal;
                        prev.targetOpen = pair.targetOpen;
                        prev.weight = keep;
                        simplifiedPairs[lastIndex] = prev;
                        if (centerlineLocal != null) simplifiedCenters[lastIndex] = prev.centerLocal;
                    }
                    else
                    {
                        prev.centerLocal = 0.5f * (prev.centerLocal + pair.centerLocal);
                        Vector3 mergedDir = prev.splitDirLocal + pair.splitDirLocal;
                        if (mergedDir.sqrMagnitude > 1e-12f)
                            prev.splitDirLocal = mergedDir.normalized;
                        prev.targetOpen = Mathf.Max(prev.targetOpen, pair.targetOpen);
                        prev.weight = keep;
                        simplifiedPairs[lastIndex] = prev;
                        if (centerlineLocal != null) simplifiedCenters[lastIndex] = prev.centerLocal;
                    }
                    continue;
                }

                simplifiedPairs.Add(pair);
                if (centerlineLocal != null) simplifiedCenters.Add(center);
            }

            lipPairs.Clear();
            lipPairs.AddRange(simplifiedPairs);
            if (centerlineLocal != null)
            {
                centerlineLocal.Clear();
                centerlineLocal.AddRange(simplifiedCenters);
            }
        }

        private static List<int> BuildIncisionVertexPath(
            Vector2[] projectedVertices2D,
            List<Vector2> stroke2D,
            List<int>[] adjacency,
            float guideRadius,
            float pathSnapPenalty,
            float sampleSpacing,
            int maxAnchorCount)
        {
            if (projectedVertices2D == null || projectedVertices2D.Length == 0 || stroke2D == null || stroke2D.Count < 2 || adjacency == null)
                return null;

            List<Vector2> resampled = ResampleStroke2D(stroke2D, sampleSpacing);
            if (resampled == null || resampled.Count < 2)
                return null;

            float[] vertexStrokeDistances = new float[projectedVertices2D.Length];
            for (int i = 0; i < projectedVertices2D.Length; i++)
            {
                vertexStrokeDistances[i] = DistancePointToStroke(projectedVertices2D[i], stroke2D);
            }

            float maxSnapDistance = Mathf.Max(guideRadius * 2.0f, sampleSpacing * 2.0f);
            var anchors = new List<int>(resampled.Count);
            int prevAnchor = -1;
            for (int i = 0; i < resampled.Count; i++)
            {
                int anchor = FindNearestVertex(projectedVertices2D, resampled[i], maxSnapDistance);
                if (anchor < 0)
                    continue;

                if (anchors.Count == 0 || anchor != prevAnchor)
                {
                    anchors.Add(anchor);
                    prevAnchor = anchor;
                }
            }

            if (anchors.Count < 2)
                return null;

            anchors = DecimateAnchors(anchors, Mathf.Max(4, maxAnchorCount));
            if (anchors.Count < 2)
                return null;

            var path = new List<int>(anchors.Count * 8);
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                int start = anchors[i];
                int end = anchors[i + 1];
                if (start == end)
                    continue;

                List<int> segmentPath;
                if (!TryFindShortestPath(start, end, adjacency, projectedVertices2D, vertexStrokeDistances, pathSnapPenalty, out segmentPath))
                    return null;

                if (segmentPath == null || segmentPath.Count == 0)
                    return null;

                if (path.Count > 0 && path[path.Count - 1] == segmentPath[0])
                {
                    for (int k = 1; k < segmentPath.Count; k++)
                        path.Add(segmentPath[k]);
                }
                else
                {
                    path.AddRange(segmentPath);
                }
            }

            if (path.Count < 2)
                return null;

            RemoveConsecutiveDuplicates(path);
            return path;
        }

        private static void RemoveConsecutiveDuplicates(List<int> path)
        {
            if (path == null || path.Count < 2)
                return;

            for (int i = path.Count - 1; i > 0; i--)
            {
                if (path[i] == path[i - 1])
                    path.RemoveAt(i);
            }
        }

        private static void RemovePathLoops(List<int> path)
        {
            if (path == null || path.Count < 3)
                return;

            var firstIndex = new Dictionary<int, int>(path.Count);
            int i = 0;
            while (i < path.Count)
            {
                int v = path[i];
                if (firstIndex.TryGetValue(v, out int prev) && i - prev > 1)
                {
                    path.RemoveRange(prev + 1, i - prev - 1);
                    i = prev + 1;
                    firstIndex.Clear();
                    for (int k = 0; k <= prev; k++)
                        firstIndex[path[k]] = k;
                    continue;
                }

                firstIndex[v] = i;
                i++;
            }

            RemoveConsecutiveDuplicates(path);
        }

        private static List<int> DecimateAnchors(List<int> anchors, int maxCount)
        {
            if (anchors == null || anchors.Count <= maxCount || maxCount < 2)
                return anchors;

            var result = new List<int>(maxCount);
            result.Add(anchors[0]);
            for (int i = 1; i < maxCount - 1; i++)
            {
                float t = (float)i / (maxCount - 1);
                int src = Mathf.Clamp(Mathf.RoundToInt(t * (anchors.Count - 1)), 0, anchors.Count - 1);
                int value = anchors[src];
                if (value != result[result.Count - 1])
                    result.Add(value);
            }

            int last = anchors[anchors.Count - 1];
            if (result[result.Count - 1] != last)
                result.Add(last);

            return result;
        }

        private static int FindNearestVertex(Vector2[] projectedVertices2D, Vector2 point, float maxDistance)
        {
            int bestIndex = -1;
            float bestSq = float.PositiveInfinity;
            float fallbackSq = float.PositiveInfinity;
            int fallbackIndex = -1;

            float limitSq = Mathf.Max(1e-8f, maxDistance * maxDistance);
            for (int i = 0; i < projectedVertices2D.Length; i++)
            {
                float d = (projectedVertices2D[i] - point).sqrMagnitude;
                if (d < fallbackSq)
                {
                    fallbackSq = d;
                    fallbackIndex = i;
                }

                if (d <= limitSq && d < bestSq)
                {
                    bestSq = d;
                    bestIndex = i;
                }
            }

            return bestIndex >= 0 ? bestIndex : fallbackIndex;
        }

        private static bool TryFindShortestPath(
            int start,
            int end,
            List<int>[] adjacency,
            Vector2[] projectedVertices2D,
            float[] vertexStrokeDistances,
            float snapPenalty,
            out List<int> path)
        {
            path = null;
            int n = adjacency.Length;
            if (start < 0 || start >= n || end < 0 || end >= n)
                return false;

            var dist = new float[n];
            var prev = new int[n];
            var visited = new bool[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = float.PositiveInfinity;
                prev[i] = -1;
            }

            dist[start] = 0f;

            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (visited[i])
                        continue;
                    if (dist[i] < best)
                    {
                        best = dist[i];
                        u = i;
                    }
                }

                if (u < 0)
                    break;

                if (u == end)
                    break;

                visited[u] = true;
                var neighbors = adjacency[u];
                if (neighbors == null)
                    continue;

                for (int i = 0; i < neighbors.Count; i++)
                {
                    int v = neighbors[i];
                    if (visited[v])
                        continue;

                    float step = (projectedVertices2D[u] - projectedVertices2D[v]).magnitude;
                    step += snapPenalty * 0.5f * (vertexStrokeDistances[u] + vertexStrokeDistances[v]);
                    float candidate = dist[u] + step;
                    if (candidate < dist[v])
                    {
                        dist[v] = candidate;
                        prev[v] = u;
                    }
                }
            }

            if (start != end && prev[end] < 0)
                return false;

            path = new List<int>(32);
            int cur = end;
            path.Add(cur);
            while (cur != start)
            {
                cur = prev[cur];
                if (cur < 0)
                    return false;
                path.Add(cur);
            }
            path.Reverse();
            return true;
        }

        private static List<int>[] BuildVertexAdjacency(int vertexCount, int[] triangles, Vector2[] projectedVertices2D)
        {
            var maps = new Dictionary<int, float>[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                maps[i] = new Dictionary<int, float>(8);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                AddGraphEdge(maps, projectedVertices2D, a, b);
                AddGraphEdge(maps, projectedVertices2D, b, c);
                AddGraphEdge(maps, projectedVertices2D, c, a);
            }

            var adjacency = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                adjacency[i] = new List<int>(maps[i].Count);
                foreach (var kv in maps[i])
                    adjacency[i].Add(kv.Key);
            }

            return adjacency;
        }

        private static void AddGraphEdge(Dictionary<int, float>[] maps, Vector2[] projectedVertices2D, int a, int b)
        {
            if (a == b)
                return;

            float len = (projectedVertices2D[a] - projectedVertices2D[b]).magnitude;
            if (!maps[a].ContainsKey(b)) maps[a].Add(b, len);
            if (!maps[b].ContainsKey(a)) maps[b].Add(a, len);
        }

        private static bool SplitMeshAlongPath(
            Mesh mesh,
            List<int> path,
            Vector2[] projectedVertices2D,
            List<Vector2> stroke2D,
            float meshRadius,
            float openWidthWorld,
            float profileVariation,
            float curvatureWidthScale,
            int layer,
            out List<IncisionLipPair> lipPairs,
            out List<Vector3> centerlineLocal)
        {
            lipPairs = new List<IncisionLipPair>();
            centerlineLocal = new List<Vector3>();

            if (mesh == null || path == null || path.Count < 2)
                return false;

            Vector3[] oldVertices = mesh.vertices;
            int[] oldTriangles = mesh.triangles;
            Vector2[] oldUV = mesh.uv;
            Vector4[] oldTangents = mesh.tangents;
            Color[] oldColors = mesh.colors;

            bool hasUV = oldUV != null && oldUV.Length == oldVertices.Length;
            bool hasTangents = oldTangents != null && oldTangents.Length == oldVertices.Length;
            bool hasColors = oldColors != null && oldColors.Length == oldVertices.Length;

            var pathSet = new HashSet<int>(path);
            var duplicateMap = new Dictionary<int, int>(pathSet.Count);

            var newVertices = new List<Vector3>(oldVertices.Length + pathSet.Count);
            newVertices.AddRange(oldVertices);

            var newUV = hasUV ? new List<Vector2>(oldVertices.Length + pathSet.Count) : null;
            var newTangents = hasTangents ? new List<Vector4>(oldVertices.Length + pathSet.Count) : null;
            var newColors = hasColors ? new List<Color>(oldVertices.Length + pathSet.Count) : null;
            if (hasUV) newUV.AddRange(oldUV);
            if (hasTangents) newTangents.AddRange(oldTangents);
            if (hasColors) newColors.AddRange(oldColors);

            foreach (int v in path)
            {
                if (duplicateMap.ContainsKey(v))
                    continue;

                int dup = newVertices.Count;
                duplicateMap.Add(v, dup);
                newVertices.Add(oldVertices[v]);
                if (hasUV) newUV.Add(oldUV[v]);
                if (hasTangents) newTangents.Add(oldTangents[v]);
                if (hasColors) newColors.Add(oldColors[v]);
            }

            int[] newTriangles = (int[])oldTriangles.Clone();
            bool anyReassigned = false;
            for (int i = 0; i < newTriangles.Length; i += 3)
            {
                int i0 = newTriangles[i];
                int i1 = newTriangles[i + 1];
                int i2 = newTriangles[i + 2];
                bool touchesPath = pathSet.Contains(i0) || pathSet.Contains(i1) || pathSet.Contains(i2);
                if (!touchesPath)
                    continue;

                Vector3 center = (oldVertices[i0] + oldVertices[i1] + oldVertices[i2]) / 3f;
                Vector2 center2D = ProjectLocalPointToStrokeSpace(center, stroke2D, meshRadius);
                float side = SignedSideToPolyline(center2D, stroke2D);
                if (side <= 0f)
                    continue;

                if (pathSet.Contains(i0)) newTriangles[i] = duplicateMap[i0];
                if (pathSet.Contains(i1)) newTriangles[i + 1] = duplicateMap[i1];
                if (pathSet.Contains(i2)) newTriangles[i + 2] = duplicateMap[i2];
                anyReassigned = true;
            }

            if (!anyReassigned)
                return false;

            var splitDirs = ComputePathSplitDirections(path, oldVertices, projectedVertices2D);
            for (int pathIndex = 0; pathIndex < path.Count; pathIndex++)
            {
                int v = path[pathIndex];
                int dup = duplicateMap[v];

                Vector3 dir;
                if (!splitDirs.TryGetValue(v, out dir))
                    dir = Vector3.right;
                if (dir.sqrMagnitude < 1e-12f)
                    dir = Vector3.right;
                else
                    dir.Normalize();

                float targetOpen = ComputePathTargetOpenWidth(path, pathIndex, projectedVertices2D, Mathf.Max(0f, openWidthWorld), profileVariation, curvatureWidthScale);
                float halfGap = targetOpen * 0.5f;

                if (halfGap > 0f)
                {
                    newVertices[v] -= dir * halfGap;
                    newVertices[dup] += dir * halfGap;
                }

                lipPairs.Add(new IncisionLipPair
                {
                    leftLocalIndex = v,
                    rightLocalIndex = dup,
                    centerLocal = oldVertices[v],
                    splitDirLocal = dir,
                    targetOpen = targetOpen,
                    weight = openWidthWorld > 1e-6f ? Mathf.Clamp01(targetOpen / Mathf.Max(1e-6f, openWidthWorld * 2f)) : 1f,
                    layer = layer
                });
                centerlineLocal.Add(oldVertices[v]);
            }

            mesh.Clear();
            mesh.SetVertices(newVertices);
            if (hasUV && newUV != null) mesh.SetUVs(0, newUV);
            if (hasTangents && newTangents != null) mesh.SetTangents(newTangents);
            if (hasColors && newColors != null) mesh.SetColors(newColors);
            mesh.SetTriangles(newTriangles, 0);
            return true;
        }


        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
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
            float v2 = vb * denom;
            float w2 = vc * denom;
            return a + ab * v2 + ac * w2;
        }

        private static float DistanceSegmentSegmentSq(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            float s, t;
            if (a <= 1e-12f && e <= 1e-12f)
                return (p1 - p2).sqrMagnitude;

            if (a <= 1e-12f)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
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
                    float denom = a * e - b * b;
                    s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
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

        private static Dictionary<int, Vector3> ComputePathSplitDirections(List<int> path, Vector3[] vertices, Vector2[] projectedVertices2D)
        {
            var result = new Dictionary<int, Vector3>(path.Count);
            if (path == null || path.Count == 0)
                return result;

            for (int i = 0; i < path.Count; i++)
            {
                int v = path[i];
                int prevIndex = path[Mathf.Max(0, i - 1)];
                int nextIndex = path[Mathf.Min(path.Count - 1, i + 1)];

                Vector2 tangent2D;
                if (prevIndex == v && nextIndex == v)
                {
                    tangent2D = Vector2.right;
                }
                else if (prevIndex == v)
                {
                    tangent2D = projectedVertices2D[nextIndex] - projectedVertices2D[v];
                }
                else if (nextIndex == v)
                {
                    tangent2D = projectedVertices2D[v] - projectedVertices2D[prevIndex];
                }
                else
                {
                    tangent2D = (projectedVertices2D[nextIndex] - projectedVertices2D[v]) + (projectedVertices2D[v] - projectedVertices2D[prevIndex]);
                }

                if (tangent2D.sqrMagnitude < 1e-10f)
                    tangent2D = Vector2.right;
                else
                    tangent2D.Normalize();

                Vector3 lp = vertices[v];
                Vector3 radial = new Vector3(lp.x, lp.y, 0f);
                if (radial.sqrMagnitude < 1e-10f)
                    radial = Vector3.right;
                else
                    radial.Normalize();

                Vector3 circum = new Vector3(-radial.y, radial.x, 0f);
                Vector3 splitDir = (-tangent2D.y) * Vector3.forward + tangent2D.x * circum;
                if (splitDir.sqrMagnitude < 1e-10f)
                    splitDir = circum;
                else
                    splitDir.Normalize();

                Vector3 accum;
                if (result.TryGetValue(v, out accum))
                {
                    splitDir = (accum + splitDir).normalized;
                }
                result[v] = splitDir;
            }

            return result;
        }

        private static float ComputePathTargetOpenWidth(
            List<int> path,
            int pathIndex,
            Vector2[] projectedVertices2D,
            float baseOpenWidth,
            float profileVariation,
            float curvatureWidthScale)
        {
            if (baseOpenWidth <= 0f)
                return 0f;

            float t = (path != null && path.Count > 1) ? (float)pathIndex / (path.Count - 1) : 0f;
            float harmonic = 0.55f * Mathf.Sin(t * Mathf.PI * 2f + 0.45f) + 0.35f * Mathf.Sin(t * Mathf.PI * 5f - 0.85f);
            float profileMul = 1f + Mathf.Clamp01(profileVariation) * harmonic;
            profileMul = Mathf.Clamp(profileMul, 0.35f, 2.0f);

            float curvature = 0f;
            if (path != null && path.Count >= 3)
            {
                int prev = path[Mathf.Max(0, pathIndex - 1)];
                int curr = path[pathIndex];
                int next = path[Mathf.Min(path.Count - 1, pathIndex + 1)];
                Vector2 a = projectedVertices2D[curr] - projectedVertices2D[prev];
                Vector2 b = projectedVertices2D[next] - projectedVertices2D[curr];
                if (a.sqrMagnitude > 1e-10f && b.sqrMagnitude > 1e-10f)
                {
                    a.Normalize();
                    b.Normalize();
                    curvature = 1f - Mathf.Clamp(Vector2.Dot(a, b), -1f, 1f);
                }
            }

            float curvatureMul = 1f + Mathf.Max(0f, curvatureWidthScale) * curvature * 0.5f;
            return Mathf.Max(0f, baseOpenWidth * profileMul * curvatureMul);
        }

        private static Vector2[] BuildProjectedVertices2D(Vector3[] vertices, List<Vector2> stroke2D, float radius)
        {
            var result = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                result[i] = ProjectLocalPointToStrokeSpace(vertices[i], stroke2D, radius);
            }
            return result;
        }

        private static Vector2 ProjectLocalPointToStrokeSpace(Vector3 localPoint, List<Vector2> stroke2D, float radius)
        {
            float theta = Mathf.Atan2(localPoint.y, localPoint.x);
            Vector2 bestPoint = new Vector2(localPoint.z, theta * radius);
            float bestSq = float.PositiveInfinity;

            if (stroke2D == null || stroke2D.Count < 2)
                return bestPoint;

            for (int i = 0; i < stroke2D.Count - 1; i++)
            {
                Vector2 a = stroke2D[i];
                Vector2 b = stroke2D[i + 1];
                float refTheta = ((a.y + b.y) * 0.5f) / Mathf.Max(radius, 1e-6f);
                float unwrappedTheta = UnwrapAngleNear(theta, refTheta);
                Vector2 p = new Vector2(localPoint.z, unwrappedTheta * radius);
                float d = DistancePointSegmentSq2D(p, a, b);
                if (d < bestSq)
                {
                    bestSq = d;
                    bestPoint = p;
                }
            }

            return bestPoint;
        }

        private static float ComputeAverageEdgeLength(Vector2[] projectedVertices2D, int[] triangles)
        {
            float sum = 0f;
            int count = 0;
            var seen = new HashSet<ulong>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                AccumulateEdgeLength(triangles[i], triangles[i + 1], projectedVertices2D, seen, ref sum, ref count);
                AccumulateEdgeLength(triangles[i + 1], triangles[i + 2], projectedVertices2D, seen, ref sum, ref count);
                AccumulateEdgeLength(triangles[i + 2], triangles[i], projectedVertices2D, seen, ref sum, ref count);
            }

            return count > 0 ? Mathf.Max(sum / count, 1e-5f) : 1e-3f;
        }

        private static float ComputeAverageEdgeLength3D(Vector3[] vertices, int[] triangles)
        {
            if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length < 3)
                return 1e-3f;

            float sum = 0f;
            int count = 0;
            var seen = new HashSet<ulong>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                AccumulateEdgeLength3D(triangles[i], triangles[i + 1], vertices, seen, ref sum, ref count);
                AccumulateEdgeLength3D(triangles[i + 1], triangles[i + 2], vertices, seen, ref sum, ref count);
                AccumulateEdgeLength3D(triangles[i + 2], triangles[i], vertices, seen, ref sum, ref count);
            }

            return count > 0 ? Mathf.Max(sum / count, 1e-5f) : 1e-3f;
        }

        private static void AccumulateEdgeLength3D(int a, int b, Vector3[] vertices, HashSet<ulong> seen, ref float sum, ref int count)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            ulong key = ((ulong)(uint)min << 32) | (uint)max;
            if (!seen.Add(key))
                return;

            sum += (vertices[a] - vertices[b]).magnitude;
            count++;
        }

        private static void AccumulateEdgeLength(int a, int b, Vector2[] projectedVertices2D, HashSet<ulong> seen, ref float sum, ref int count)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            ulong key = ((ulong)(uint)min << 32) | (uint)max;
            if (!seen.Add(key))
                return;

            sum += (projectedVertices2D[a] - projectedVertices2D[b]).magnitude;
            count++;
        }

        private static List<Vector2> ResampleStroke2D(List<Vector2> stroke2D, float spacing)
        {
            if (stroke2D == null || stroke2D.Count == 0)
                return null;
            if (stroke2D.Count == 1)
                return new List<Vector2>(stroke2D);

            spacing = Mathf.Max(1e-5f, spacing);
            var result = new List<Vector2>(stroke2D.Count * 2);
            Vector2 current = stroke2D[0];
            result.Add(current);
            float remaining = spacing;

            for (int i = 0; i < stroke2D.Count - 1; i++)
            {
                Vector2 segStart = current;
                Vector2 segEnd = stroke2D[i + 1];
                Vector2 delta = segEnd - segStart;
                float segLen = delta.magnitude;
                if (segLen <= 1e-8f)
                    continue;

                Vector2 dir = delta / segLen;
                float progressed = 0f;
                while (segLen - progressed >= remaining)
                {
                    progressed += remaining;
                    Vector2 p = segStart + dir * progressed;
                    if ((p - result[result.Count - 1]).sqrMagnitude > 1e-10f)
                        result.Add(p);
                    remaining = spacing;
                }

                remaining -= (segLen - progressed);
                current = segEnd;
            }

            if ((stroke2D[stroke2D.Count - 1] - result[result.Count - 1]).sqrMagnitude > 1e-10f)
                result.Add(stroke2D[stroke2D.Count - 1]);

            return result;
        }

        private static float DistancePointToStroke(Vector2 p, List<Vector2> stroke2D)
        {
            if (stroke2D == null || stroke2D.Count == 0)
                return 0f;
            if (stroke2D.Count == 1)
                return (p - stroke2D[0]).magnitude;

            float best = float.PositiveInfinity;
            for (int i = 0; i < stroke2D.Count - 1; i++)
            {
                float d = DistancePointSegmentSq2D(p, stroke2D[i], stroke2D[i + 1]);
                if (d < best)
                    best = d;
            }
            return Mathf.Sqrt(best);
        }

        private static float SignedSideToPolyline(Vector2 p, List<Vector2> polyline)
        {
            if (polyline == null || polyline.Count < 2)
                return 0f;

            float bestSq = float.PositiveInfinity;
            float signed = 0f;
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector2 a = polyline[i];
                Vector2 b = polyline[i + 1];
                Vector2 ab = b - a;
                float denom = Vector2.Dot(ab, ab);
                if (denom <= 1e-12f)
                    continue;

                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
                Vector2 q = a + ab * t;
                Vector2 diff = p - q;
                float dsq = diff.sqrMagnitude;
                if (dsq < bestSq)
                {
                    bestSq = dsq;
                    signed = ab.x * diff.y - ab.y * diff.x;
                }
            }
            return signed;
        }

        private static float DistancePointPolyline(Vector3 p, IReadOnlyList<Vector3> polyline, out int bestSegIndex, out float bestSegT)
        {
            bestSegIndex = 0;
            bestSegT = 0f;
            if (polyline == null || polyline.Count == 0) return 0f;
            if (polyline.Count == 1) return Vector3.Distance(p, polyline[0]);

            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < polyline.Count - 1; ++i)
            {
                Vector3 a = polyline[i];
                Vector3 b = polyline[i + 1];
                Vector3 ab = b - a;
                float denom = Vector3.Dot(ab, ab);
                float t = denom > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / denom) : 0f;
                Vector3 q = a + ab * t;
                float dsq = (p - q).sqrMagnitude;
                if (dsq < bestSq)
                {
                    bestSq = dsq;
                    bestSegIndex = i;
                    bestSegT = t;
                }
            }
            return Mathf.Sqrt(bestSq);
        }

        private static float EstimateAverageRadiusLocal(Vector3[] vertices)
        {
            return GetAverageMeshRadius(vertices);
        }

        private static float GetAverageMeshRadius(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
                return 1f;

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                float r = new Vector2(vertices[i].x, vertices[i].y).magnitude;
                if (r > 1e-6f)
                {
                    sum += r;
                    count++;
                }
            }

            return count > 0 ? Mathf.Max(sum / count, 1e-4f) : 1f;
        }

        private static List<Vector2> BuildUnwrappedStroke2D(List<Vector3> strokeWorld, Transform meshTransform, float radius)
        {
            if (strokeWorld == null || strokeWorld.Count == 0 || meshTransform == null)
                return null;

            Matrix4x4 w2l = meshTransform.worldToLocalMatrix;
            var result = new List<Vector2>(strokeWorld.Count);
            float prevTheta = 0f;
            bool hasPrev = false;

            for (int i = 0; i < strokeWorld.Count; i++)
            {
                Vector3 lp = w2l.MultiplyPoint3x4(strokeWorld[i]);
                float theta = Mathf.Atan2(lp.y, lp.x);
                if (!hasPrev)
                {
                    prevTheta = theta;
                    hasPrev = true;
                }
                else
                {
                    theta = UnwrapAngleNear(theta, prevTheta);
                    prevTheta = theta;
                }

                result.Add(new Vector2(lp.z, theta * radius));
            }

            return result;
        }

        private static float DistanceLocalPointToStrokeSqOnCylinder(Vector3 localPoint, List<Vector2> stroke2D, float radius)
        {
            if (stroke2D == null || stroke2D.Count == 0)
                return float.PositiveInfinity;

            float theta = Mathf.Atan2(localPoint.y, localPoint.x);
            float best = float.PositiveInfinity;

            for (int i = 0; i < stroke2D.Count - 1; i++)
            {
                Vector2 a = stroke2D[i];
                Vector2 b = stroke2D[i + 1];
                float refTheta = ((a.y + b.y) * 0.5f) / Mathf.Max(radius, 1e-6f);
                float unwrappedTheta = UnwrapAngleNear(theta, refTheta);
                Vector2 p = new Vector2(localPoint.z, unwrappedTheta * radius);
                float d = DistancePointSegmentSq2D(p, a, b);
                if (d < best) best = d;
            }

            return best;
        }

        private static float UnwrapAngleNear(float angle, float reference)
        {
            float twoPi = Mathf.PI * 2f;
            return angle + Mathf.Round((reference - angle) / twoPi) * twoPi;
        }


#if UNITY_EDITOR
        [ContextMenu("Incision/Clear Current Stroke")]
        private void ContextClearIncisionStroke()
        {
            Undo.RecordObject(this, "Clear Incision Stroke");
            ClearIncisionStroke();
            EditorUtility.SetDirty(this);
            SceneView.RepaintAll();
        }

        [ContextMenu("Incision/Apply Current Stroke")]
        private void ContextApplyCurrentIncision()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DoubleLayerDriver] 当前工作流已改为“运行时生成一次”。编辑态不要再手动提交切口。");
                return;
            }

            TryApplyCurrentIncision();
        }

        private struct IncisionRenderSample
        {
            public Vector3 center;
            public Vector3 left;
            public Vector3 right;
            public float t;
        }

        private void EnsureIncisionSidewallRenderer()
        {
            if (!enableIncisionSidewallRender || outerMeshFilter == null)
                return;

            if (_incisionSidewallObject == null)
            {
                _incisionSidewallObject = new GameObject("IncisionSidewallRuntime");
                _incisionSidewallObject.hideFlags = HideFlags.DontSave;
                _incisionSidewallObject.transform.SetParent(transform, false);
                _incisionSidewallFilter = _incisionSidewallObject.AddComponent<MeshFilter>();
                _incisionSidewallRenderer = _incisionSidewallObject.AddComponent<MeshRenderer>();
            }

            if (_incisionSidewallMesh == null)
            {
                _incisionSidewallMesh = new Mesh();
                _incisionSidewallMesh.name = "IncisionSidewallRuntimeMesh";
                _incisionSidewallMesh.MarkDynamic();
                if (_incisionSidewallFilter != null)
                    _incisionSidewallFilter.sharedMesh = _incisionSidewallMesh;
            }

            if (_incisionSidewallRenderer != null)
            {
                var src = outerMeshFilter.GetComponent<MeshRenderer>();
                if (src != null)
                {
                    _incisionSidewallRenderer.sharedMaterial = PrepareLocalWallMaterial(src.sharedMaterial, ref _incisionSidewallMaterial);
                    _incisionSidewallRenderer.shadowCastingMode = src.shadowCastingMode;
                    _incisionSidewallRenderer.receiveShadows = src.receiveShadows;
                    _incisionSidewallRenderer.lightProbeUsage = src.lightProbeUsage;
                    _incisionSidewallRenderer.reflectionProbeUsage = src.reflectionProbeUsage;
                    _incisionSidewallRenderer.enabled = true;
                }
            }
        }


        private void EnsureIncisionMarkerRenderer()
        {
            if (!enableIncisionMarkerRender || outerMeshFilter == null)
                return;

            if (_incisionMarkerObject == null)
            {
                _incisionMarkerObject = new GameObject("IncisionMarkerRuntime");
                _incisionMarkerObject.hideFlags = HideFlags.DontSave;
                _incisionMarkerObject.transform.SetParent(transform, false);
                _incisionMarkerFilter = _incisionMarkerObject.AddComponent<MeshFilter>();
                _incisionMarkerRenderer = _incisionMarkerObject.AddComponent<MeshRenderer>();
                _incisionMarkerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _incisionMarkerRenderer.receiveShadows = false;
                _incisionMarkerRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _incisionMarkerRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            if (_incisionMarkerMesh == null)
            {
                _incisionMarkerMesh = new Mesh();
                _incisionMarkerMesh.name = "IncisionMarkerRuntimeMesh";
                _incisionMarkerMesh.MarkDynamic();
                if (_incisionMarkerFilter != null)
                    _incisionMarkerFilter.sharedMesh = _incisionMarkerMesh;
            }

            if (_incisionMarkerRenderer != null)
            {
                Material mat = incisionMarkerMaterialOverride;
                if (mat == null)
                {
                    if (_incisionMarkerRuntimeMaterial == null)
                    {
                        Shader shader = Shader.Find("Unlit/Color");
                        if (shader == null) shader = Shader.Find("Sprites/Default");
                        if (shader == null) shader = Shader.Find("Standard");
                        _incisionMarkerRuntimeMaterial = new Material(shader);
                        _incisionMarkerRuntimeMaterial.name = "IncisionMarkerRuntimeMaterial";
                        _incisionMarkerRuntimeMaterial.hideFlags = HideFlags.DontSave;
                        if (_incisionMarkerRuntimeMaterial.HasProperty("_Glossiness"))
                            _incisionMarkerRuntimeMaterial.SetFloat("_Glossiness", 0f);
                        if (_incisionMarkerRuntimeMaterial.HasProperty("_Metallic"))
                            _incisionMarkerRuntimeMaterial.SetFloat("_Metallic", 0f);
                    }
                    mat = _incisionMarkerRuntimeMaterial;
                }

                if (mat != null)
                {
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", incisionMarkerColor);
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", incisionMarkerColor);
                    _incisionMarkerRenderer.sharedMaterial = mat;
                }

                _incisionMarkerRenderer.enabled = true;
            }
        }

        private void DestroyIncisionMarkerRenderer()
        {
            if (_incisionMarkerMesh != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(_incisionMarkerMesh);
                else Destroy(_incisionMarkerMesh);
#else
                Destroy(_incisionMarkerMesh);
#endif
                _incisionMarkerMesh = null;
            }

            if (_incisionMarkerRuntimeMaterial != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(_incisionMarkerRuntimeMaterial);
                else Destroy(_incisionMarkerRuntimeMaterial);
#else
                Destroy(_incisionMarkerRuntimeMaterial);
#endif
                _incisionMarkerRuntimeMaterial = null;
            }

            if (_incisionMarkerObject != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(_incisionMarkerObject);
                else Destroy(_incisionMarkerObject);
#else
                Destroy(_incisionMarkerObject);
#endif
                _incisionMarkerObject = null;
                _incisionMarkerFilter = null;
                _incisionMarkerRenderer = null;
            }
        }

        private void ClearIncisionMarkerMesh()
        {
            if (_incisionMarkerMesh != null)
                _incisionMarkerMesh.Clear();
            if (_incisionMarkerRenderer != null)
                _incisionMarkerRenderer.enabled = false;
        }

        private void DestroyIncisionSidewallRenderer()
        {
            DestroyWallMaterial(ref _incisionSidewallMaterial);
            if (_incisionSidewallMesh != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(_incisionSidewallMesh);
                else Destroy(_incisionSidewallMesh);
#else
                Destroy(_incisionSidewallMesh);
#endif
                _incisionSidewallMesh = null;
            }

            if (_incisionSidewallObject != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(_incisionSidewallObject);
                else Destroy(_incisionSidewallObject);
#else
                Destroy(_incisionSidewallObject);
#endif
                _incisionSidewallObject = null;
                _incisionSidewallFilter = null;
                _incisionSidewallRenderer = null;
            }
        }

        private void ClearIncisionSidewallMesh()
        {
            if (_incisionSidewallMesh != null)
                _incisionSidewallMesh.Clear();
            if (_incisionSidewallRenderer != null)
                _incisionSidewallRenderer.enabled = false;
        }

        private static ulong PackUndirectedEdgeKey(int a, int b)
        {
            uint ua = (uint)Mathf.Min(a, b);
            uint ub = (uint)Mathf.Max(a, b);
            return ((ulong)ua << 32) | ub;
        }

        private static Dictionary<ulong, BoundaryBridgeEdge> BuildBoundaryEdgeMap(int[] triangles)
        {
            var counts = new Dictionary<ulong, int>(triangles != null ? triangles.Length : 0);
            var oriented = new Dictionary<ulong, BoundaryBridgeEdge>(triangles != null ? triangles.Length : 0);
            if (triangles == null)
                return oriented;

            void AddEdge(int a, int b)
            {
                ulong key = PackUndirectedEdgeKey(a, b);
                if (counts.TryGetValue(key, out int count))
                    counts[key] = count + 1;
                else
                {
                    counts.Add(key, 1);
                    oriented.Add(key, new BoundaryBridgeEdge { a = a, b = b });
                }
            }

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i + 0];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                AddEdge(a, b);
                AddEdge(b, c);
                AddEdge(c, a);
            }

            var result = new Dictionary<ulong, BoundaryBridgeEdge>(oriented.Count);
            foreach (var kv in oriented)
            {
                if (counts.TryGetValue(kv.Key, out int count) && count == 1)
                    result.Add(kv.Key, kv.Value);
            }
            return result;
        }

        private static bool IsNearIncisionLocalPoint(Vector3 localPos, List<IncisionLipPair> pairs, float radiusLocal)
        {
            if (pairs == null || pairs.Count == 0 || radiusLocal <= 1e-6f)
                return false;

            float r2 = radiusLocal * radiusLocal;
            for (int i = 0; i < pairs.Count; i++)
            {
                Vector3 d = localPos - pairs[i].centerLocal;
                if (d.sqrMagnitude <= r2)
                    return true;
            }
            return false;
        }

        private bool IsBoundaryEdgeNearIncision(BoundaryBridgeEdge edge)
        {
            if ((_outerIncisionLipPairs == null || _outerIncisionLipPairs.Count == 0) && (_innerIncisionLipPairs == null || _innerIncisionLipPairs.Count == 0))
                return false;

            if (_outerMesh == null || _innerMesh == null)
                return false;

            Vector3[] outerVerts = _outerMesh.vertices;
            Vector3[] innerVerts = _innerMesh.vertices;
            if (outerVerts == null || innerVerts == null)
                return false;
            if (edge.a < 0 || edge.b < 0 || edge.a >= outerVerts.Length || edge.b >= outerVerts.Length || edge.a >= innerVerts.Length || edge.b >= innerVerts.Length)
                return false;

            Vector3 outerMid = 0.5f * (outerVerts[edge.a] + outerVerts[edge.b]);
            Vector3 innerMid = 0.5f * (innerVerts[edge.a] + innerVerts[edge.b]);
            float radius = Mathf.Max(incisionPairMinSpacing * 1.2f, Mathf.Max(incisionBrushRadius, 1e-4f));
            radius = Mathf.Max(radius, ResolveIncisionThicknessRelaxRadius());

            return IsNearIncisionLocalPoint(outerMid, _outerIncisionLipPairs, radius)
                || IsNearIncisionLocalPoint(innerMid, _innerIncisionLipPairs, radius);
        }

        private struct PreviewVertexPolar
        {
            public int localIndex;
            public float z;
            public float theta;
        }

        [Obsolete(
            "Legacy local-Z preview mapping is not valid for curved vessels. " +
            "Use synchronized topology identity.",
            true)]
        private List<Ring> BuildRingsForPreviewMesh(MeshFilter filter, int globalStart)
        {
            var rings = new List<Ring>(64);
            if (filter == null || filter.sharedMesh == null || outerMeshFilter == null)
                return rings;

            Vector3[] verts = filter.sharedMesh.vertices;
            if (verts == null || verts.Length == 0)
                return rings;

            Matrix4x4 localToOuterLocal = outerMeshFilter.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var list = new List<PreviewVertexPolar>(verts.Length);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 lp = localToOuterLocal.MultiplyPoint3x4(verts[i]);
                float theta = Mathf.Atan2(lp.y, lp.x);
                if (theta < 0f) theta += Mathf.PI * 2f;

                list.Add(new PreviewVertexPolar
                {
                    localIndex = i,
                    z = lp.z,
                    theta = theta,
                });
            }

            list.Sort((a, b) => a.z.CompareTo(b.z));
            float minZ = list[0].z;
            float maxZ = list[list.Count - 1].z;
            float length = Mathf.Max(1e-6f, maxZ - minZ);
            float eps = ringZGroupingEpsilon > 0 ? ringZGroupingEpsilon : (length * 1e-4f);

            Ring cur = null;
            float curZ = 0f;
            int curCount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (cur == null || Mathf.Abs(v.z - curZ) > eps)
                {
                    if (cur != null && curCount > 0)
                    {
                        cur.z = curZ;
                        rings.Add(cur);
                    }

                    cur = new Ring
                    {
                        z = v.z,
                        indices = new List<int>(64),
                        thetas = new List<float>(64)
                    };
                    curZ = v.z;
                    curCount = 0;
                }

                curCount++;
                curZ = curZ + (v.z - curZ) / curCount;
                cur.indices.Add(globalStart + v.localIndex);
                cur.thetas.Add(v.theta);
            }

            if (cur != null && cur.indices.Count > 0)
            {
                cur.z = curZ;
                rings.Add(cur);
            }

            for (int r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                int n = ring.indices.Count;
                var pair = new List<ThetaIndexPair>(n);
                for (int i = 0; i < n; i++)
                    pair.Add(new ThetaIndexPair { theta = ring.thetas[i], index = ring.indices[i] });

                pair.Sort((a, b) => a.theta.CompareTo(b.theta));
                ring.thetas.Clear();
                ring.indices.Clear();
                for (int i = 0; i < n; i++)
                {
                    ring.thetas.Add(pair[i].theta);
                    ring.indices.Add(pair[i].index);
                }
            }

            rings.Sort((a, b) => a.z.CompareTo(b.z));
            return rings;
        }

        private void BuildMiddleWallPreviewMapping()
        {
            if (outerMeshFilter == null || innerMeshFilter == null || _outerMesh == null || _innerMesh == null)
                return;

            _outerCount = _outerMesh.vertexCount;
            _innerCount = _innerMesh.vertexCount;
            EnsureMiddleWallMappingCache();

            if (!ValidateSynchronizedLayerTopologyIdentity(
                    out string identitySummary))
            {
                Debug.LogError(
                    "[DoubleLayerDriver] Middle-wall preview topology identity contract failed. " +
                    identitySummary);
                return;
            }

            Vector3[] outerVerts = _outerMesh.vertices;
            Vector3[] innerVerts = _innerMesh.vertices;
            Matrix4x4 outerL2W = outerMeshFilter.transform.localToWorldMatrix;
            Matrix4x4 innerL2W = innerMeshFilter.transform.localToWorldMatrix;

            for (int local = 0; local < _outerCount; local++)
            {
                Vector3 outerWorld =
                    outerL2W.MultiplyPoint3x4(outerVerts[local]);
                Vector3 innerWorld =
                    innerL2W.MultiplyPoint3x4(innerVerts[local]);
                OverrideMiddleWallMappingFromLayerSeparation(
                    OuterStart + local,
                    InnerStart + local,
                    Vector3.Distance(outerWorld, innerWorld));
            }
        }

        private void UpdateMiddleWallFillMeshInEditMode()
        {
            if (!previewMiddleWallInEditMode)
            {
                DestroyMiddleWallFillRenderer();
                return;
            }

            if (!enableMiddleWallFillRender || outerMeshFilter == null || innerMeshFilter == null)
            {
                DestroyMiddleWallFillRenderer();
                return;
            }

            _outerMesh = outerMeshFilter.sharedMesh;
            _innerMesh = innerMeshFilter.sharedMesh;
            if (_outerMesh == null || _innerMesh == null)
            {
                DestroyMiddleWallFillRenderer();
                return;
            }

            BuildMiddleWallPreviewMapping();
            BuildMiddleWallBoundaryCache();

            if (_middleWallOuterToInnerLocal == null || !_middleWallBoundaryValid)
            {
                DestroyMiddleWallFillRenderer();
                return;
            }

            EnsureMiddleWallFillRenderer();
            if (_middleWallFillMesh == null || _middleWallFillObject == null)
                return;

            float tOuter = Mathf.Clamp01(Mathf.Min(middleWallOuterBlend, middleWallInnerBlend - 0.02f));
            float tInner = Mathf.Clamp01(Mathf.Max(middleWallInnerBlend, middleWallOuterBlend + 0.02f));
            if (tInner <= tOuter + 1e-4f)
                tInner = Mathf.Min(0.98f, tOuter + 0.02f);

            _middleWallFillVertices.Clear();
            _middleWallFillTriangles.Clear();
            _middleWallFillUVs.Clear();
            _middleWallFillNormals.Clear();

            Vector3[] outerVerts = _outerMesh.vertices;
            Vector3[] innerVerts = _innerMesh.vertices;
            int[] outerTriangles = _outerMesh.triangles;
            Vector2[] outerUV = _outerMesh.uv;
            Matrix4x4 outerL2W = outerMeshFilter.transform.localToWorldMatrix;
            Matrix4x4 innerL2W = innerMeshFilter.transform.localToWorldMatrix;
            Matrix4x4 w2l = _middleWallFillObject.transform.worldToLocalMatrix;

            if (outerTriangles != null)
            {
                for (int i = 0; i + 2 < outerTriangles.Length; i += 3)
                {
                    int oa = outerTriangles[i + 0];
                    int ob = outerTriangles[i + 1];
                    int oc = outerTriangles[i + 2];

                    if (!IsValidMiddleWallTriangle(oa, ob, oc, out int ia, out int ib, out int ic))
                        continue;
                    if (oa < 0 || oa >= outerVerts.Length || ob < 0 || ob >= outerVerts.Length || oc < 0 || oc >= outerVerts.Length)
                        continue;
                    if (ia < 0 || ia >= innerVerts.Length || ib < 0 || ib >= innerVerts.Length || ic < 0 || ic >= innerVerts.Length)
                        continue;

                    Vector3 oA = outerL2W.MultiplyPoint3x4(outerVerts[oa]);
                    Vector3 oB = outerL2W.MultiplyPoint3x4(outerVerts[ob]);
                    Vector3 oC = outerL2W.MultiplyPoint3x4(outerVerts[oc]);
                    Vector3 iA = innerL2W.MultiplyPoint3x4(innerVerts[ia]);
                    Vector3 iB = innerL2W.MultiplyPoint3x4(innerVerts[ib]);
                    Vector3 iC = innerL2W.MultiplyPoint3x4(innerVerts[ic]);

                    Vector3 s0A = LerpWallPoint(oA, iA, tOuter);
                    Vector3 s0B = LerpWallPoint(oB, iB, tOuter);
                    Vector3 s0C = LerpWallPoint(oC, iC, tOuter);
                    Vector3 s1A = LerpWallPoint(oA, iA, tInner);
                    Vector3 s1B = LerpWallPoint(oB, iB, tInner);
                    Vector3 s1C = LerpWallPoint(oC, iC, tInner);

                    float area0 = Vector3.Cross(s0B - s0A, s0C - s0A).magnitude;
                    float area1 = Vector3.Cross(s1B - s1A, s1C - s1A).magnitude;
                    if (area0 <= 1e-10f || area1 <= 1e-10f)
                        continue;

                    Vector2 uvA = (outerUV != null && oa >= 0 && oa < outerUV.Length) ? outerUV[oa] : Vector2.zero;
                    Vector2 uvB = (outerUV != null && ob >= 0 && ob < outerUV.Length) ? outerUV[ob] : Vector2.zero;
                    Vector2 uvC = (outerUV != null && oc >= 0 && oc < outerUV.Length) ? outerUV[oc] : Vector2.zero;

                    AppendMiddleWallDoubleSidedTriangle(w2l, s0A, s0B, s0C, uvA, uvB, uvC);
                    AppendMiddleWallDoubleSidedTriangle(w2l, s1A, s1C, s1B, uvA, uvC, uvB);
                }
            }

            for (int i = 0; i < _middleWallBoundaryEdges.Count; i++)
            {
                var edge = _middleWallBoundaryEdges[i];
                if (!TryGetMiddleWallInnerLocalIndex(edge.a, out int innerA))
                    continue;
                if (!TryGetMiddleWallInnerLocalIndex(edge.b, out int innerB))
                    continue;
                if (edge.a < 0 || edge.a >= outerVerts.Length || edge.b < 0 || edge.b >= outerVerts.Length)
                    continue;
                if (innerA < 0 || innerA >= innerVerts.Length || innerB < 0 || innerB >= innerVerts.Length)
                    continue;

                Vector3 oA = outerL2W.MultiplyPoint3x4(outerVerts[edge.a]);
                Vector3 oB = outerL2W.MultiplyPoint3x4(outerVerts[edge.b]);
                Vector3 iA = innerL2W.MultiplyPoint3x4(innerVerts[innerA]);
                Vector3 iB = innerL2W.MultiplyPoint3x4(innerVerts[innerB]);

                // Seal the full current thickness; blend values only position
                // the internal shells, not the contact with either vessel wall.
                float uLen = Vector3.Distance(oA, oB);
                float vLen = 0.5f * (Vector3.Distance(oA, iA) + Vector3.Distance(oB, iB));
                if (uLen <= 1e-10f || vLen <= 1e-10f)
                    continue;

                AppendMiddleWallDoubleSidedQuad(
                    w2l,
                    oA, oB, iB, iA,
                    new Vector2(0f, 0f),
                    new Vector2(uLen, 0f),
                    new Vector2(uLen, vLen),
                    new Vector2(0f, vLen));
            }

            if (_middleWallFillVertices.Count == 0 || _middleWallFillTriangles.Count == 0)
            {
                ClearMiddleWallFillMesh();
                return;
            }

            _middleWallFillMesh.Clear();
            _middleWallFillMesh.indexFormat = _middleWallFillVertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _middleWallFillMesh.SetVertices(_middleWallFillVertices);
            _middleWallFillMesh.SetUVs(0, _middleWallFillUVs);
            _middleWallFillMesh.SetNormals(_middleWallFillNormals);
            _middleWallFillMesh.SetTriangles(_middleWallFillTriangles, 0, true);
            _middleWallFillMesh.RecalculateBounds();
            if (_middleWallFillRenderer != null)
                _middleWallFillRenderer.enabled = true;
        }

#if UNITY_EDITOR
        private void RefreshMiddleWallPreviewInEditMode(bool forceRefresh)
        {
            if (Application.isPlaying)
                return;

            if (!forceRefresh && !_editorMiddleWallPreviewDirty)
                return;

            _editorMiddleWallPreviewDirty = false;
            UpdateMiddleWallFillMeshInEditMode();
        }
#endif

        private void EnsureMiddleWallMappingCache()
        {
            if (_outerCount <= 0)
            {
                _middleWallOuterToInnerLocal = null;
                _middleWallOuterToInnerRest = null;
                return;
            }

            if (_middleWallOuterToInnerLocal == null || _middleWallOuterToInnerLocal.Length != _outerCount)
            {
                _middleWallOuterToInnerLocal = new int[_outerCount];
                _middleWallOuterToInnerRest = new float[_outerCount];
            }

            for (int i = 0; i < _outerCount; i++)
            {
                _middleWallOuterToInnerLocal[i] = -1;
                _middleWallOuterToInnerRest[i] = float.PositiveInfinity;
            }
        }

        private void RegisterMiddleWallFallbackMapping(int outerGlobalIndex, int innerGlobalIndex, float restLength)
        {
            if (_middleWallOuterToInnerLocal == null || _middleWallOuterToInnerRest == null)
                return;
            if (outerGlobalIndex < OuterStart || outerGlobalIndex >= OuterStart + OuterCount)
                return;
            if (innerGlobalIndex < InnerStart || innerGlobalIndex >= InnerStart + InnerCount)
                return;

            int outerLocal = outerGlobalIndex - OuterStart;
            int innerLocal = innerGlobalIndex - InnerStart;
            float best = _middleWallOuterToInnerRest[outerLocal];
            if (_middleWallOuterToInnerLocal[outerLocal] < 0 || restLength < best)
            {
                _middleWallOuterToInnerLocal[outerLocal] = innerLocal;
                _middleWallOuterToInnerRest[outerLocal] = restLength;
            }
        }

        private void OverrideMiddleWallMappingFromLayerSeparation(int outerGlobalIndex, int innerGlobalIndex, float preferredDistance)
        {
            if (_middleWallOuterToInnerLocal == null || _middleWallOuterToInnerRest == null)
                return;
            if (outerGlobalIndex < OuterStart || outerGlobalIndex >= OuterStart + OuterCount)
                return;
            if (innerGlobalIndex < InnerStart || innerGlobalIndex >= InnerStart + InnerCount)
                return;

            int outerLocal = outerGlobalIndex - OuterStart;
            _middleWallOuterToInnerLocal[outerLocal] = innerGlobalIndex - InnerStart;
            _middleWallOuterToInnerRest[outerLocal] = Mathf.Max(0f, preferredDistance);
        }

        private bool TryGetMiddleWallInnerLocalIndex(int outerLocalIndex, out int innerLocalIndex)
        {
            innerLocalIndex = -1;
            if (_middleWallOuterToInnerLocal == null)
                return false;
            if (outerLocalIndex < 0 || outerLocalIndex >= _middleWallOuterToInnerLocal.Length)
                return false;

            innerLocalIndex = _middleWallOuterToInnerLocal[outerLocalIndex];
            return innerLocalIndex >= 0 && innerLocalIndex < InnerCount;
        }

        private void BuildMiddleWallBoundaryCache()
        {
            _middleWallBoundaryEdges.Clear();
            _middleWallBoundaryValid = false;
            if (!enableMiddleWallFillRender || _outerMesh == null || _innerMesh == null ||
                _middleWallOuterToInnerLocal == null)
                return;

            // Cache topology identities only. Never join stations using their
            // initial positions, or interpret inner local IDs as outer IDs.
            var outerBoundary = BuildBoundaryEdgeMap(_outerMesh.triangles);
            var innerBoundary = BuildBoundaryEdgeMap(_innerMesh.triangles);
            var usedInner = new HashSet<int>();
            var matchedInnerEdges = new HashSet<ulong>();
            int missing = 0, duplicate = 0, unmatchedOuter = 0;
            var preview = new List<string>();
            for (int local = 0; local < _outerMesh.vertexCount; local++)
            {
                if (!TryGetMiddleWallInnerLocalIndex(local, out int inner) || inner >= _innerMesh.vertexCount)
                    missing++;
                else if (!usedInner.Add(inner))
                    duplicate++;
            }
            foreach (var pair in outerBoundary)
            {
                var edge = pair.Value;
                if (!TryGetMiddleWallInnerLocalIndex(edge.a, out int ia) ||
                    !TryGetMiddleWallInnerLocalIndex(edge.b, out int ib) || ia == ib ||
                    !innerBoundary.ContainsKey(PackUndirectedEdgeKey(ia, ib)))
                {
                    unmatchedOuter++;
                    if (preview.Count < 6) preview.Add($"{edge.a}-{edge.b}");
                    continue;
                }
                matchedInnerEdges.Add(PackUndirectedEdgeKey(ia, ib));
                _middleWallBoundaryEdges.Add(edge);
            }
            int unmatchedInner = innerBoundary.Count - matchedInnerEdges.Count;
            _middleWallBoundaryValid = missing == 0 && duplicate == 0 && unmatchedOuter == 0 && unmatchedInner == 0;
            string diagnostic = $"[MiddleWall][BoundaryIdentity] valid={_middleWallBoundaryValid} " +
                $"outer={outerBoundary.Count} inner={innerBoundary.Count} missing={missing} duplicate={duplicate} " +
                $"unmatched={unmatchedOuter}/{unmatchedInner} preview=[{string.Join(",", preview)}]";
            if (diagnostic != _middleWallBoundaryDiagnostic)
            {
                _middleWallBoundaryDiagnostic = diagnostic;
                if (_middleWallBoundaryValid) Debug.Log(diagnostic, this);
                else Debug.LogError(diagnostic, this);
            }
            if (!_middleWallBoundaryValid)
                _middleWallBoundaryEdges.Clear();
        }

        private void EnsureMiddleWallFillRenderer()
        {
            bool allowInCurrentMode = enableMiddleWallFillRender && outerMeshFilter != null && (Application.isPlaying || previewMiddleWallInEditMode);
            if (!allowInCurrentMode)
                return;

            bool isPreviewMode = !Application.isPlaying;
            string objectName = Application.isPlaying ? "MiddleWallFillRuntime" : "MiddleWallFillPreview";
            string otherObjectName = Application.isPlaying ? "MiddleWallFillPreview" : "MiddleWallFillRuntime";
            HideFlags flags = Application.isPlaying
                ? HideFlags.DontSave
                : HideFlags.DontSaveInEditor;

            DestroyMiddleWallFillObjectByName(otherObjectName);

            if (_middleWallFillObject == null)
            {
                Transform existing = transform.Find(objectName);
                if (existing != null)
                    _middleWallFillObject = existing.gameObject;
            }

            if (_middleWallFillObject != null)
            {
                _middleWallFillFilter = _middleWallFillObject.GetComponent<MeshFilter>();
                _middleWallFillRenderer = _middleWallFillObject.GetComponent<MeshRenderer>();

#if UNITY_EDITOR
                if (isPreviewMode && (_middleWallFillFilter == null || _middleWallFillRenderer == null))
                {
                    DestroyImmediate(_middleWallFillObject);
                    _middleWallFillObject = null;
                    _middleWallFillFilter = null;
                    _middleWallFillRenderer = null;
                }
#endif
            }

            if (_middleWallFillObject == null)
            {
                _middleWallFillObject = new GameObject(objectName);
                _middleWallFillObject.transform.SetParent(transform, false);
            }

            _middleWallFillObject.name = objectName;
            _middleWallFillObject.hideFlags = flags;
            if (_middleWallFillObject.transform.parent != transform)
                _middleWallFillObject.transform.SetParent(transform, false);

            _middleWallFillObject.transform.localPosition = Vector3.zero;
            _middleWallFillObject.transform.localRotation = Quaternion.identity;
            _middleWallFillObject.transform.localScale = Vector3.one;
            _middleWallFillObject.SetActive(true);

            _middleWallFillFilter = _middleWallFillObject.GetComponent<MeshFilter>();
            if (_middleWallFillFilter == null)
                _middleWallFillFilter = _middleWallFillObject.AddComponent<MeshFilter>();

            _middleWallFillRenderer = _middleWallFillObject.GetComponent<MeshRenderer>();
            if (_middleWallFillRenderer == null)
                _middleWallFillRenderer = _middleWallFillObject.AddComponent<MeshRenderer>();

            if (_middleWallFillMesh == null)
            {
                _middleWallFillMesh = new Mesh();
                _middleWallFillMesh.name = Application.isPlaying ? "MiddleWallFillRuntimeMesh" : "MiddleWallFillPreviewMesh";
                _middleWallFillMesh.hideFlags = flags;
                _middleWallFillMesh.MarkDynamic();
            }
            else
            {
                _middleWallFillMesh.hideFlags = flags;
            }

            if (_middleWallFillFilter != null)
            {
                _middleWallFillFilter.hideFlags = flags;
                if (_middleWallFillFilter.sharedMesh != _middleWallFillMesh)
                    _middleWallFillFilter.sharedMesh = _middleWallFillMesh;
            }

            if (_middleWallFillRenderer != null)
            {
                _middleWallFillRenderer.hideFlags = flags;

                Renderer src = outerMeshFilter != null ? outerMeshFilter.GetComponent<Renderer>() : null;
                if (middleWallMaterialOverride != null)
                {
                    _middleWallFillRenderer.sharedMaterial = PrepareLocalWallMaterial(middleWallMaterialOverride, ref _middleWallFillMaterial);
                }
                else if (src != null)
                {
                    _middleWallFillRenderer.sharedMaterial = PrepareLocalWallMaterial(src.sharedMaterial, ref _middleWallFillMaterial);

                    _middleWallFillRenderer.shadowCastingMode = src.shadowCastingMode;
                    _middleWallFillRenderer.receiveShadows = src.receiveShadows;
                    _middleWallFillRenderer.lightProbeUsage = src.lightProbeUsage;
                    _middleWallFillRenderer.reflectionProbeUsage = src.reflectionProbeUsage;
                }

                _middleWallFillRenderer.enabled = true;
            }

        }

        private void DestroyMiddleWallFillRenderer()
        {
            DestroyWallMaterial(ref _middleWallFillMaterial);
            if (_middleWallFillMesh != null)
            {
                if (!Application.isPlaying) DestroyImmediate(_middleWallFillMesh);
                else Destroy(_middleWallFillMesh);
                _middleWallFillMesh = null;
            }

            if (_middleWallFillObject != null)
            {
                if (!Application.isPlaying) DestroyImmediate(_middleWallFillObject);
                else Destroy(_middleWallFillObject);
                _middleWallFillObject = null;
                _middleWallFillFilter = null;
                _middleWallFillRenderer = null;
            }
        }

        private void ClearMiddleWallFillMesh()
        {
            if (_middleWallFillMesh != null)
                _middleWallFillMesh.Clear();
            if (_middleWallFillRenderer != null)
                _middleWallFillRenderer.enabled = false;
        }

        private static Material PrepareLocalWallMaterial(Material source, ref Material owned)
        {
            if (source == null)
            {
                DestroyWallMaterial(ref owned);
                return null;
            }
            if (owned == null || owned.shader != source.shader)
            {
                DestroyWallMaterial(ref owned);
                owned = new Material(source) { name = source.name + " (Local Wall)", hideFlags = HideFlags.DontSave };
            }
            owned.CopyPropertiesFromMaterial(source);
            // Wall meshes contain local vertices and explicit reverse-wound faces.
            // Each side must draw once, without inheriting the outer shader's world mode.
            owned.DisableKeyword("_INPUT_WORLD_VERTEX");
            if (owned.HasProperty("_Cull"))
                owned.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            return owned;
        }

        private static void DestroyWallMaterial(ref Material owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
            owned = null;
        }

        private void AppendMiddleWallSingleSidedTriangle(Matrix4x4 w2l, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld, Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            Vector3 a = w2l.MultiplyPoint3x4(aWorld);
            Vector3 b = w2l.MultiplyPoint3x4(bWorld);
            Vector3 c = w2l.MultiplyPoint3x4(cWorld);

            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude <= 1e-16f)
                return;
            n.Normalize();

            int baseIndex = _middleWallFillVertices.Count;
            _middleWallFillVertices.Add(a);
            _middleWallFillVertices.Add(b);
            _middleWallFillVertices.Add(c);

            _middleWallFillUVs.Add(uvA);
            _middleWallFillUVs.Add(uvB);
            _middleWallFillUVs.Add(uvC);

            _middleWallFillNormals.Add(n);
            _middleWallFillNormals.Add(n);
            _middleWallFillNormals.Add(n);

            _middleWallFillTriangles.Add(baseIndex + 0);
            _middleWallFillTriangles.Add(baseIndex + 1);
            _middleWallFillTriangles.Add(baseIndex + 2);
        }

        private void AppendMiddleWallDoubleSidedTriangle(Matrix4x4 w2l, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld, Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            AppendMiddleWallSingleSidedTriangle(w2l, aWorld, bWorld, cWorld, uvA, uvB, uvC);
            AppendMiddleWallSingleSidedTriangle(w2l, aWorld, cWorld, bWorld, uvA, uvC, uvB);
        }

        private void AppendMiddleWallDoubleSidedQuad(Matrix4x4 w2l, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld, Vector3 dWorld, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            AppendMiddleWallSingleSidedTriangle(w2l, aWorld, bWorld, cWorld, uvA, uvB, uvC);
            AppendMiddleWallSingleSidedTriangle(w2l, aWorld, cWorld, dWorld, uvA, uvC, uvD);
            AppendMiddleWallSingleSidedTriangle(w2l, aWorld, dWorld, cWorld, uvA, uvD, uvC);
            AppendMiddleWallSingleSidedTriangle(w2l, aWorld, cWorld, bWorld, uvA, uvC, uvB);
        }

        private void AddDoubleSidedQuad(List<int> tris, int a, int b, int c, int d)
        {
            AddQuad(tris, a, b, c, d);
            AddQuad(tris, d, c, b, a);
        }

        private void AddDoubleSidedTriangle(List<int> tris, int a, int b, int c)
        {
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
            tris.Add(c);
            tris.Add(b);
            tris.Add(a);
        }

        private bool IsValidMiddleWallTriangle(int outerA, int outerB, int outerC, out int innerA, out int innerB, out int innerC)
        {
            innerA = innerB = innerC = -1;
            if (!TryGetMiddleWallInnerLocalIndex(outerA, out innerA))
                return false;
            if (!TryGetMiddleWallInnerLocalIndex(outerB, out innerB))
                return false;
            if (!TryGetMiddleWallInnerLocalIndex(outerC, out innerC))
                return false;
            if (innerA == innerB || innerB == innerC || innerC == innerA)
                return false;
            return true;
        }

        private static Vector3 LerpWallPoint(Vector3 outer, Vector3 inner, float t)
        {
            return outer + (inner - outer) * t;
        }

        private void UpdateMiddleWallFillMesh()
        {
            if (!enableMiddleWallFillRender || !_middleWallBoundaryValid)
            {
                ClearMiddleWallFillMesh();
                return;
            }

            if (_simulator == null || outerMeshFilter == null || innerMeshFilter == null || _outerMesh == null || _innerMesh == null || _middleWallOuterToInnerLocal == null)
            {
                ClearMiddleWallFillMesh();
                return;
            }

            EnsureMiddleWallFillRenderer();
            if (_middleWallFillMesh == null || _middleWallFillObject == null)
                return;

            float tOuter = Mathf.Clamp01(Mathf.Min(middleWallOuterBlend, middleWallInnerBlend - 0.02f));
            float tInner = Mathf.Clamp01(Mathf.Max(middleWallInnerBlend, middleWallOuterBlend + 0.02f));
            if (tInner <= tOuter + 1e-4f)
                tInner = Mathf.Min(0.98f, tOuter + 0.02f);

            _middleWallFillVertices.Clear();
            _middleWallFillTriangles.Clear();
            _middleWallFillUVs.Clear();
            _middleWallFillNormals.Clear();

            var positions = _simulator.positions;
            int[] outerTriangles = _outerMesh.triangles;
            Vector2[] outerUV = _outerMesh.uv;
            Matrix4x4 w2l = _middleWallFillObject.transform.worldToLocalMatrix;

            if (outerTriangles != null)
            {
                for (int i = 0; i + 2 < outerTriangles.Length; i += 3)
                {
                    int oa = outerTriangles[i + 0];
                    int ob = outerTriangles[i + 1];
                    int oc = outerTriangles[i + 2];

                    if (!IsValidMiddleWallTriangle(oa, ob, oc, out int ia, out int ib, out int ic))
                        continue;

                    Vector3 oA = (Vector3)positions[OuterStart + oa];
                    Vector3 oB = (Vector3)positions[OuterStart + ob];
                    Vector3 oC = (Vector3)positions[OuterStart + oc];
                    Vector3 iA = (Vector3)positions[InnerStart + ia];
                    Vector3 iB = (Vector3)positions[InnerStart + ib];
                    Vector3 iC = (Vector3)positions[InnerStart + ic];

                    Vector3 s0A = LerpWallPoint(oA, iA, tOuter);
                    Vector3 s0B = LerpWallPoint(oB, iB, tOuter);
                    Vector3 s0C = LerpWallPoint(oC, iC, tOuter);
                    Vector3 s1A = LerpWallPoint(oA, iA, tInner);
                    Vector3 s1B = LerpWallPoint(oB, iB, tInner);
                    Vector3 s1C = LerpWallPoint(oC, iC, tInner);

                    float area0 = Vector3.Cross(s0B - s0A, s0C - s0A).magnitude;
                    float area1 = Vector3.Cross(s1B - s1A, s1C - s1A).magnitude;
                    if (area0 <= 1e-10f || area1 <= 1e-10f)
                        continue;

                    Vector2 uvA = (outerUV != null && oa >= 0 && oa < outerUV.Length) ? outerUV[oa] : Vector2.zero;
                    Vector2 uvB = (outerUV != null && ob >= 0 && ob < outerUV.Length) ? outerUV[ob] : Vector2.zero;
                    Vector2 uvC = (outerUV != null && oc >= 0 && oc < outerUV.Length) ? outerUV[oc] : Vector2.zero;

                    AppendMiddleWallDoubleSidedTriangle(w2l, s0A, s0B, s0C, uvA, uvB, uvC);
                    AppendMiddleWallDoubleSidedTriangle(w2l, s1A, s1C, s1B, uvA, uvC, uvB);
                }
            }

            for (int i = 0; i < _middleWallBoundaryEdges.Count; i++)
            {
                var edge = _middleWallBoundaryEdges[i];
                if (!TryGetMiddleWallInnerLocalIndex(edge.a, out int innerA))
                    continue;
                if (!TryGetMiddleWallInnerLocalIndex(edge.b, out int innerB))
                    continue;
                if (innerA == innerB)
                    continue;

                Vector3 oA = (Vector3)positions[OuterStart + edge.a];
                Vector3 oB = (Vector3)positions[OuterStart + edge.b];
                Vector3 iA = (Vector3)positions[InnerStart + innerA];
                Vector3 iB = (Vector3)positions[InnerStart + innerB];

                // Seal the full current thickness; blend values only position
                // the internal shells, not the contact with either vessel wall.
                float uLen = Vector3.Distance(oA, oB);
                float vLen = 0.5f * (Vector3.Distance(oA, iA) + Vector3.Distance(oB, iB));
                if (uLen <= 1e-10f || vLen <= 1e-10f)
                    continue;

                AppendMiddleWallDoubleSidedQuad(
                    w2l,
                    oA, oB, iB, iA,
                    new Vector2(0f, 0f),
                    new Vector2(uLen, 0f),
                    new Vector2(uLen, vLen),
                    new Vector2(0f, vLen));
            }

            if (_middleWallFillVertices.Count == 0 || _middleWallFillTriangles.Count == 0)
            {
                ClearMiddleWallFillMesh();
                return;
            }

            _middleWallFillMesh.Clear();
            _middleWallFillMesh.indexFormat = _middleWallFillVertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _middleWallFillMesh.SetVertices(_middleWallFillVertices);
            _middleWallFillMesh.SetUVs(0, _middleWallFillUVs);
            _middleWallFillMesh.SetNormals(_middleWallFillNormals);
            _middleWallFillMesh.SetTriangles(_middleWallFillTriangles, 0, true);
            _middleWallFillMesh.RecalculateBounds();
            if (_middleWallFillRenderer != null)
                _middleWallFillRenderer.enabled = true;
        }


        private void UpdateIncisionMarkerMesh()
        {
            ClearIncisionMarkerMesh();
        }

        private void AddIncisionMarkerQuadDoubleSided(Matrix4x4 w2l, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld, Vector3 dWorld,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            AddIncisionMarkerQuad(w2l, aWorld, bWorld, cWorld, dWorld, uvA, uvB, uvC, uvD);
            AddIncisionMarkerQuad(w2l, dWorld, cWorld, bWorld, aWorld, uvD, uvC, uvB, uvA);
        }

        private void AddIncisionMarkerQuad(Matrix4x4 w2l, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld, Vector3 dWorld,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            Vector3 a = w2l.MultiplyPoint3x4(aWorld);
            Vector3 b = w2l.MultiplyPoint3x4(bWorld);
            Vector3 c = w2l.MultiplyPoint3x4(cWorld);
            Vector3 d = w2l.MultiplyPoint3x4(dWorld);

            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude <= 1e-12f)
                return;
            normal.Normalize();

            int baseIndex = _incisionMarkerVertices.Count;
            _incisionMarkerVertices.Add(a);
            _incisionMarkerVertices.Add(b);
            _incisionMarkerVertices.Add(c);
            _incisionMarkerVertices.Add(d);
            _incisionMarkerUVs.Add(uvA);
            _incisionMarkerUVs.Add(uvB);
            _incisionMarkerUVs.Add(uvC);
            _incisionMarkerUVs.Add(uvD);
            _incisionMarkerNormals.Add(normal);
            _incisionMarkerNormals.Add(normal);
            _incisionMarkerNormals.Add(normal);
            _incisionMarkerNormals.Add(normal);
            _incisionMarkerTriangles.Add(baseIndex + 0);
            _incisionMarkerTriangles.Add(baseIndex + 1);
            _incisionMarkerTriangles.Add(baseIndex + 2);
            _incisionMarkerTriangles.Add(baseIndex + 0);
            _incisionMarkerTriangles.Add(baseIndex + 2);
            _incisionMarkerTriangles.Add(baseIndex + 3);
        }

        private void UpdateIncisionSidewallMesh()
        {
            ClearIncisionSidewallMesh();

            // The middle wall already seals every real boundary, including the
            // incision. Keep one owner to prevent overlapping cap surfaces.
            if (!enableIncisionSidewallRender || enableMiddleWallFillRender)
                return;
            if (_simulator == null || !_simulator.positions.IsCreated ||
                _sutureSidewallBoundaryEdges == null || _sutureSidewallBoundaryEdges.Count == 0)
                return;

            EnsureIncisionSidewallRenderer();
            if (_incisionSidewallMesh == null || _incisionSidewallRenderer == null)
                return;

            _incisionSidewallVertices.Clear();
            _incisionSidewallTriangles.Clear();
            _incisionSidewallUVs.Clear();
            _incisionSidewallNormals.Clear();

            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
            int particleCount = _simulator.positions.Length;
            int validQuadCount = 0;
            int edgeCount = _sutureSidewallBoundaryEdges.Count;
            for (int i = 0; i < edgeCount; i++)
            {
                SutureSidewallBoundaryEdgeInfo edge = _sutureSidewallBoundaryEdges[i];
                if (edge.enabled == 0 ||
                    !IsValidSimulatorParticle(edge.outerA, particleCount) ||
                    !IsValidSimulatorParticle(edge.outerB, particleCount) ||
                    !IsValidSimulatorParticle(edge.innerA, particleCount) ||
                    !IsValidSimulatorParticle(edge.innerB, particleCount) ||
                    edge.outerA == edge.outerB ||
                    edge.innerA == edge.innerB)
                {
                    continue;
                }

                Vector3 outerA = _simulator.GetWorldVertexPosition(edge.outerA);
                Vector3 outerB = _simulator.GetWorldVertexPosition(edge.outerB);
                Vector3 innerA = _simulator.GetWorldVertexPosition(edge.innerA);
                Vector3 innerB = _simulator.GetWorldVertexPosition(edge.innerB);
                float averageThickness = 0.5f *
                    (Vector3.Distance(outerA, innerA) + Vector3.Distance(outerB, innerB));
                if (!IsIncisionSidewallQuadLocallyStable(
                        outerA,
                        outerB,
                        innerB,
                        innerA,
                        averageThickness))
                {
                    continue;
                }

                float u0 = (float)i / Mathf.Max(1, edgeCount);
                float u1 = (float)(i + 1) / Mathf.Max(1, edgeCount);
                AddIncisionSidewallQuadDoubleSided(
                    worldToLocal,
                    outerA,
                    outerB,
                    innerB,
                    innerA,
                    new Vector2(u0, 0f),
                    new Vector2(u1, 0f),
                    new Vector2(u1, 1f),
                    new Vector2(u0, 1f));
                validQuadCount++;
            }

            if (validQuadCount == 0 || _incisionSidewallTriangles.Count == 0)
            {
                ClearIncisionSidewallMesh();
                return;
            }

            _incisionSidewallMesh.Clear();
            _incisionSidewallMesh.SetVertices(_incisionSidewallVertices);
            _incisionSidewallMesh.SetUVs(0, _incisionSidewallUVs);
            _incisionSidewallMesh.SetNormals(_incisionSidewallNormals);
            _incisionSidewallMesh.SetTriangles(_incisionSidewallTriangles, 0, true);
            _incisionSidewallMesh.RecalculateBounds();
            _incisionSidewallRenderer.enabled = true;
        }

        private static bool IsValidSimulatorParticle(int index, int particleCount)
        {
            return index >= 0 && index < particleCount;
        }

        private void AddIncisionSidewallVertex(Matrix4x4 w2l, Vector3 worldPos, float u, float v)
        {
            _incisionSidewallVertices.Add(w2l.MultiplyPoint3x4(worldPos));
            _incisionSidewallUVs.Add(new Vector2(u, v));
            _incisionSidewallNormals.Add(Vector3.up);
        }

        private void AddIncisionSidewallQuadDoubleSided(Matrix4x4 w2l, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld, Vector3 dWorld,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            // Both sides use diagonal AC, including after a non-planar deformation.
            AppendIncisionSidewallTriangle(w2l, aWorld, bWorld, cWorld, uvA, uvB, uvC);
            AppendIncisionSidewallTriangle(w2l, aWorld, cWorld, dWorld, uvA, uvC, uvD);
            AppendIncisionSidewallTriangle(w2l, aWorld, cWorld, bWorld, uvA, uvC, uvB);
            AppendIncisionSidewallTriangle(w2l, aWorld, dWorld, cWorld, uvA, uvD, uvC);
        }

        private void AppendIncisionSidewallTriangle(Matrix4x4 w2l, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld,
            Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            Vector3 a = w2l.MultiplyPoint3x4(aWorld);
            Vector3 b = w2l.MultiplyPoint3x4(bWorld);
            Vector3 c = w2l.MultiplyPoint3x4(cWorld);

            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude <= 1e-12f)
                return;
            normal.Normalize();

            int baseIndex = _incisionSidewallVertices.Count;
            _incisionSidewallVertices.Add(a);
            _incisionSidewallVertices.Add(b);
            _incisionSidewallVertices.Add(c);
            _incisionSidewallUVs.Add(uvA);
            _incisionSidewallUVs.Add(uvB);
            _incisionSidewallUVs.Add(uvC);
            _incisionSidewallNormals.Add(normal);
            _incisionSidewallNormals.Add(normal);
            _incisionSidewallNormals.Add(normal);
            _incisionSidewallTriangles.Add(baseIndex + 0);
            _incisionSidewallTriangles.Add(baseIndex + 1);
            _incisionSidewallTriangles.Add(baseIndex + 2);
        }

        private static void StabilizeIncisionRenderSampleSides(List<IncisionRenderSample> samples)
        {
            if (samples == null || samples.Count < 2)
                return;

            for (int i = 1; i < samples.Count; i++)
            {
                var prev = samples[i - 1];
                var cur = samples[i];
                float keepCost = Vector3.Distance(prev.left, cur.left) + Vector3.Distance(prev.right, cur.right);
                float swapCost = Vector3.Distance(prev.left, cur.right) + Vector3.Distance(prev.right, cur.left);
                if (swapCost + 1e-5f < keepCost)
                {
                    var tmp = cur.left;
                    cur.left = cur.right;
                    cur.right = tmp;
                    samples[i] = cur;
                }
            }
        }

        private static void PruneIncisionRenderSamplesForSidewall(List<IncisionRenderSample> samples)
        {
            if (samples == null || samples.Count < 3)
                return;

            float medianWidth = ComputeMedianIncisionSampleWidth(samples);
            float medianStep = ComputeMedianIncisionCenterStep(samples);
            float maxWidth = medianWidth > 1e-6f ? medianWidth * 4.0f : float.PositiveInfinity;
            float maxStep = Mathf.Max(medianStep * 3.5f, medianWidth * 4.5f, 1e-4f);

            var filtered = new List<IncisionRenderSample>(samples.Count);
            filtered.Add(samples[0]);

            for (int i = 1; i < samples.Count; i++)
            {
                var cur = samples[i];
                float width = Vector3.Distance(cur.left, cur.right);
                float step = Vector3.Distance(filtered[filtered.Count - 1].center, cur.center);
                if (width <= 1e-6f)
                    continue;
                if (width > maxWidth)
                    continue;
                if (step > maxStep)
                    continue;
                filtered.Add(cur);
            }

            if (filtered.Count < 2)
                return;

            samples.Clear();
            samples.AddRange(filtered);

            float total = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                if (i > 0)
                    total += Vector3.Distance(samples[i - 1].center, samples[i].center);

                var s = samples[i];
                s.t = total;
                samples[i] = s;
            }

            if (total > 1e-6f)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    var s = samples[i];
                    s.t /= total;
                    samples[i] = s;
                }
            }
        }

        private static float ComputeMedianIncisionSampleWidth(List<IncisionRenderSample> samples)
        {
            if (samples == null || samples.Count == 0)
                return 0f;

            var values = new List<float>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                float width = Vector3.Distance(samples[i].left, samples[i].right);
                if (width > 1e-6f && float.IsFinite(width))
                    values.Add(width);
            }

            if (values.Count == 0)
                return 0f;

            values.Sort();
            return values[values.Count / 2];
        }

        private static float ComputeMedianIncisionCenterStep(List<IncisionRenderSample> samples)
        {
            if (samples == null || samples.Count < 2)
                return 0f;

            var values = new List<float>(samples.Count - 1);
            for (int i = 1; i < samples.Count; i++)
            {
                float step = Vector3.Distance(samples[i - 1].center, samples[i].center);
                if (step > 1e-6f && float.IsFinite(step))
                    values.Add(step);
            }

            if (values.Count == 0)
                return 0f;

            values.Sort();
            return values[values.Count / 2];
        }

        private static float ComputeAverageIncisionLayerThickness(List<IncisionRenderSample> outerSamples, List<IncisionRenderSample> innerSamples)
        {
            int count = Mathf.Min(outerSamples != null ? outerSamples.Count : 0, innerSamples != null ? innerSamples.Count : 0);
            if (count <= 0)
                return 0f;

            float sum = 0f;
            int valid = 0;
            for (int i = 0; i < count; i++)
            {
                float dLL = Vector3.Distance(outerSamples[i].left, innerSamples[i].left);
                float dRR = Vector3.Distance(outerSamples[i].right, innerSamples[i].right);
                if (!float.IsFinite(dLL) || !float.IsFinite(dRR))
                    continue;
                sum += 0.5f * (dLL + dRR);
                valid++;
            }

            return valid > 0 ? sum / valid : 0f;
        }

        private static bool IsIncisionSidewallQuadLocallyStable(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float avgThickness)
        {
            float ab = Vector3.Distance(a, b);
            float bc = Vector3.Distance(b, c);
            float cd = Vector3.Distance(c, d);
            float da = Vector3.Distance(d, a);
            float ac = Vector3.Distance(a, c);
            float bd = Vector3.Distance(b, d);

            if (!float.IsFinite(ab) || !float.IsFinite(bc) || !float.IsFinite(cd) || !float.IsFinite(da) || !float.IsFinite(ac) || !float.IsFinite(bd))
                return false;

            float maxEdge = Mathf.Max(Mathf.Max(ab, bc), Mathf.Max(cd, da));
            float minEdge = Mathf.Min(Mathf.Min(ab, bc), Mathf.Min(cd, da));
            if (minEdge <= 1e-7f)
                return false;

            float guard = Mathf.Max(avgThickness * 4.5f, minEdge * 6.0f, 1e-4f);
            if (maxEdge > guard)
                return false;
            if (ac > guard * 1.35f || bd > guard * 1.35f)
                return false;

            Vector3 n0 = Vector3.Cross(b - a, c - a);
            Vector3 n1 = Vector3.Cross(c - a, d - a);
            if (n0.sqrMagnitude <= 1e-12f || n1.sqrMagnitude <= 1e-12f)
                return false;
            n0.Normalize();
            n1.Normalize();
            if (Vector3.Dot(n0, n1) < 0.15f)
                return false;

            return true;
        }

        private static void StabilizeIncisionLayerAlignment(List<IncisionRenderSample> outerSamples, List<IncisionRenderSample> innerSamples)
        {
            int count = Mathf.Min(outerSamples != null ? outerSamples.Count : 0, innerSamples != null ? innerSamples.Count : 0);
            if (count <= 0)
                return;

            for (int i = 0; i < count; i++)
            {
                var outer = outerSamples[i];
                var inner = innerSamples[i];
                float keepCost = Vector3.Distance(outer.left, inner.left) + Vector3.Distance(outer.right, inner.right);
                float swapCost = Vector3.Distance(outer.left, inner.right) + Vector3.Distance(outer.right, inner.left);
                if (swapCost + 1e-5f < keepCost)
                {
                    var tmp = inner.left;
                    inner.left = inner.right;
                    inner.right = tmp;
                    innerSamples[i] = inner;
                }
            }
        }

        private static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }

        private List<IncisionRenderSample> BuildIncisionRenderSamplesFromTargetStroke()
        {
            var result = new List<IncisionRenderSample>(_incisionStrokeWorld != null ? _incisionStrokeWorld.Count : 0);
            if (_incisionStrokeWorld == null || _incisionStrokeWorld.Count < 2 || outerMeshFilter == null)
                return result;

            Bounds b = outerMeshFilter.sharedMesh != null ? outerMeshFilter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            Vector3 axisCenterLocal = b.center;
            float halfWidth = Mathf.Max(incisionMarkerMinWidth, incisionOpenWidth * Mathf.Max(0.1f, incisionMarkerWidthScale)) * 0.5f;
            var tf = outerMeshFilter.transform;

            float total = 0f;
            for (int i = 0; i < _incisionStrokeWorld.Count; i++)
            {
                Vector3 center = _incisionStrokeWorld[i];
                Vector3 tangent;
                if (i == 0) tangent = _incisionStrokeWorld[i + 1] - center;
                else if (i == _incisionStrokeWorld.Count - 1) tangent = center - _incisionStrokeWorld[i - 1];
                else tangent = _incisionStrokeWorld[i + 1] - _incisionStrokeWorld[i - 1];
                if (tangent.sqrMagnitude <= 1e-12f) tangent = tf.forward;
                tangent.Normalize();

                Vector3 local = tf.worldToLocalMatrix.MultiplyPoint3x4(center);
                Vector3 radialLocal = new Vector3(local.x - axisCenterLocal.x, local.y - axisCenterLocal.y, 0f);
                if (radialLocal.sqrMagnitude <= 1e-12f) radialLocal = Vector3.right;
                Vector3 normal = tf.TransformDirection(radialLocal.normalized);
                Vector3 side = Vector3.Cross(normal, tangent);
                if (side.sqrMagnitude <= 1e-12f) side = tf.right;
                side.Normalize();

                result.Add(new IncisionRenderSample
                {
                    center = center,
                    left = center - side * halfWidth,
                    right = center + side * halfWidth,
                    t = 0f
                });
            }

            for (int i = 1; i < result.Count; i++)
            {
                total += Vector3.Distance(result[i - 1].center, result[i].center);
                var s = result[i];
                s.t = total;
                result[i] = s;
            }
            if (total > 1e-6f)
            {
                for (int i = 0; i < result.Count; i++)
                {
                    var s = result[i];
                    s.t /= total;
                    result[i] = s;
                }
            }
            return result;
        }

        private List<IncisionRenderSample> BuildIncisionRenderSamplesFromStations(List<PendingLipStation> stations, MeshFilter filter, int globalOffset)
        {
            var result = new List<IncisionRenderSample>(stations != null ? stations.Count : 0);
            if (stations == null || filter == null || stations.Count == 0)
                return result;

            for (int i = 0; i < stations.Count; i++)
            {
                var station = stations[i];
                if (station.leftLocalIndex < 0 || station.rightLocalIndex < 0)
                    continue;

                Vector3 leftWorld;
                Vector3 rightWorld;
                if (_simulator != null && Application.isPlaying)
                {
                    leftWorld = _simulator.GetWorldVertexPosition(globalOffset + station.leftLocalIndex);
                    rightWorld = _simulator.GetWorldVertexPosition(globalOffset + station.rightLocalIndex);
                }
                else
                {
                    Vector3 splitDir = station.splitDirLocal.sqrMagnitude > 1e-12f ? station.splitDirLocal.normalized : Vector3.right;
                    leftWorld = filter.transform.TransformPoint(station.seedCenterLocal - splitDir * (station.targetOpen * 0.5f));
                    rightWorld = filter.transform.TransformPoint(station.seedCenterLocal + splitDir * (station.targetOpen * 0.5f));
                }

                result.Add(new IncisionRenderSample
                {
                    center = 0.5f * (leftWorld + rightWorld),
                    left = leftWorld,
                    right = rightWorld,
                    t = station.strokeT
                });
            }

            result.Sort((a, b) => a.t.CompareTo(b.t));
            float total = 0f;
            for (int i = 1; i < result.Count; i++)
            {
                total += Vector3.Distance(result[i - 1].center, result[i].center);
                var s = result[i];
                s.t = total;
                result[i] = s;
            }

            if (total > 1e-6f)
            {
                for (int i = 0; i < result.Count; i++)
                {
                    var s = result[i];
                    s.t /= total;
                    result[i] = s;
                }
            }
            else
            {
                for (int i = 0; i < result.Count; i++)
                {
                    var s = result[i];
                    s.t = result.Count <= 1 ? 0f : (float)i / (result.Count - 1);
                    result[i] = s;
                }
            }

            return result;
        }

        private List<IncisionRenderSample> BuildIncisionRenderSamples(List<IncisionLipPair> pairs, MeshFilter filter, int globalOffset)
        {
            var result = new List<IncisionRenderSample>(pairs != null ? pairs.Count : 0);
            if (pairs == null || filter == null || pairs.Count == 0)
                return result;

            for (int i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                Vector3 leftWorld;
                Vector3 rightWorld;
                if (_simulator != null && Application.isPlaying)
                {
                    leftWorld = _simulator.GetWorldVertexPosition(globalOffset + pair.leftLocalIndex);
                    rightWorld = _simulator.GetWorldVertexPosition(globalOffset + pair.rightLocalIndex);
                }
                else
                {
                    leftWorld = filter.transform.TransformPoint(pair.centerLocal - pair.splitDirLocal * (pair.targetOpen * 0.5f));
                    rightWorld = filter.transform.TransformPoint(pair.centerLocal + pair.splitDirLocal * (pair.targetOpen * 0.5f));
                }

                result.Add(new IncisionRenderSample
                {
                    center = 0.5f * (leftWorld + rightWorld),
                    left = leftWorld,
                    right = rightWorld,
                    t = 0f
                });
            }

            float total = 0f;
            for (int i = 1; i < result.Count; i++)
            {
                total += Vector3.Distance(result[i - 1].center, result[i].center);
                var s = result[i];
                s.t = total;
                result[i] = s;
            }

            if (total > 1e-6f)
            {
                for (int i = 0; i < result.Count; i++)
                {
                    var s = result[i];
                    s.t /= total;
                    result[i] = s;
                }
            }
            else
            {
                for (int i = 0; i < result.Count; i++)
                {
                    var s = result[i];
                    s.t = result.Count <= 1 ? 0f : (float)i / (result.Count - 1);
                    result[i] = s;
                }
            }

            return result;
        }

        private static bool ShouldSwapIncisionSides(List<IncisionRenderSample> outerSamples, List<IncisionRenderSample> innerSamples)
        {
            int count = Mathf.Min(outerSamples.Count, innerSamples.Count);
            if (count <= 0)
                return false;

            float keepCost = 0f;
            float swapCost = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = count <= 1 ? 0f : (float)i / (count - 1);
                int j = Mathf.Clamp(Mathf.RoundToInt(t * Mathf.Max(0, innerSamples.Count - 1)), 0, Mathf.Max(0, innerSamples.Count - 1));
                var o = outerSamples[i];
                var inn = innerSamples[j];
                keepCost += Vector3.Distance(o.left, inn.left) + Vector3.Distance(o.right, inn.right);
                swapCost += Vector3.Distance(o.left, inn.right) + Vector3.Distance(o.right, inn.left);
            }
            return swapCost < keepCost;
        }


        private static bool IsIncisionSidewallMappingStable(List<IncisionRenderSample> outerSamples, List<IncisionRenderSample> innerSamples)
        {
            int count = Mathf.Min(outerSamples != null ? outerSamples.Count : 0, innerSamples != null ? innerSamples.Count : 0);
            if (count < 2)
                return false;

            float thicknessSum = 0f;
            int thicknessCount = 0;
            for (int i = 0; i < count; i++)
            {
                var o = outerSamples[i];
                var inn = innerSamples[i];

                Vector3 oDir = o.right - o.left;
                Vector3 iDir = inn.right - inn.left;
                float oLen = oDir.magnitude;
                float iLen = iDir.magnitude;
                if (oLen <= 1e-6f || iLen <= 1e-6f)
                    return false;

                oDir /= oLen;
                iDir /= iLen;
                if (Vector3.Dot(oDir, iDir) < -0.15f)
                    return false;

                float dLL = Vector3.Distance(o.left, inn.left);
                float dRR = Vector3.Distance(o.right, inn.right);
                float dLR = Vector3.Distance(o.left, inn.right);
                float dRL = Vector3.Distance(o.right, inn.left);
                if (!float.IsFinite(dLL) || !float.IsFinite(dRR) || !float.IsFinite(dLR) || !float.IsFinite(dRL))
                    return false;

                if (dLL > dLR || dRR > dRL)
                    return false;

                float localThickness = 0.5f * (dLL + dRR);
                float localWidth = 0.25f * (oLen + iLen);
                if (localThickness > Mathf.Max(localWidth * 3.0f, 0.05f))
                    return false;

                thicknessSum += localThickness;
                thicknessCount++;
            }

            float avgThickness = thicknessCount > 0 ? thicknessSum / thicknessCount : 0f;
            if (avgThickness <= 1e-6f)
                return false;

            for (int i = 1; i < count; i++)
            {
                var op = outerSamples[i - 1];
                var oc = outerSamples[i];
                var ip = innerSamples[i - 1];
                var ic = innerSamples[i];

                float outerStep = Vector3.Distance(op.center, oc.center);
                float innerStep = Vector3.Distance(ip.center, ic.center);
                if (outerStep > avgThickness * 6.0f || innerStep > avgThickness * 6.0f)
                    return false;
            }

            return true;
        }
        private static List<IncisionRenderSample> ResampleIncisionRenderSamples(List<IncisionRenderSample> src, int sampleCount)
        {
            var result = new List<IncisionRenderSample>(sampleCount);
            if (src == null || src.Count == 0)
                return result;
            if (src.Count == 1)
            {
                for (int i = 0; i < sampleCount; i++) result.Add(src[0]);
                return result;
            }

            int seg = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount <= 1 ? 0f : (float)i / (sampleCount - 1);
                while (seg < src.Count - 2 && src[seg + 1].t < t)
                    seg++;
                var a = src[seg];
                var b = src[Mathf.Min(seg + 1, src.Count - 1)];
                float dt = Mathf.Max(1e-6f, b.t - a.t);
                float u = Mathf.Clamp01((t - a.t) / dt);
                result.Add(new IncisionRenderSample
                {
                    t = t,
                    center = Vector3.Lerp(a.center, b.center, u),
                    left = Vector3.Lerp(a.left, b.left, u),
                    right = Vector3.Lerp(a.right, b.right, u),
                });
            }
            return result;
        }

        private void SubscribeSceneGui()
        {
            _sceneGuiSubscribed = false;
        }

        private void UnsubscribeSceneGui()
        {
            _sceneGuiSubscribed = false;
            _isIncisionDragging = false;
        }

        private void RecordIncisionUndoIfNeeded(string label)
        {
            if (!Application.isPlaying)
            {
                Undo.RecordObject(this, label);
            }
        }

        private void HandleSceneGUI(SceneView sceneView)
        {
            // 场景切口交互与面板已完全迁移到 ClothSimulationDriverDoubleLayerEditor.cs
        }

        private void DrawIncisionAuthoringPanel(SceneView sceneView)
        {
            // 旧版 runtime 面板已停用；保留空壳仅为避免旧调用点报错。
        }

        private float DrawIncisionPanelSlider(Rect rect, string label, float value, float min, float max, string fmt, float labelW, float sliderW, float valueW)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelW, rect.height);
            Rect sliderRect = new Rect(labelRect.xMax + 4f, rect.y + 4f, sliderW, Mathf.Max(12f, rect.height - 8f));
            Rect valueRect = new Rect(sliderRect.xMax + 4f, rect.y, valueW, rect.height);

            EditorGUI.LabelField(labelRect, label);
            float newValue = GUI.HorizontalSlider(sliderRect, value, min, max);
            EditorGUI.LabelField(valueRect, newValue.ToString(fmt));
            return newValue;
        }

        private void HandleIncisionPanelDragging(Rect headerRect)
        {
            if (!allowDragIncisionPanel)
                return;

            Event e = Event.current;
            if (e == null)
                return;

            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && headerRect.Contains(e.mousePosition))
                    {
                        s_draggingIncisionPanel = true;
                        s_incisionPanelDragStartMouse = e.mousePosition;
                        s_incisionPanelDragStartOffset = incisionPanelScreenOffset;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (s_draggingIncisionPanel && GUIUtility.hotControl == id)
                    {
                        incisionPanelScreenOffset = s_incisionPanelDragStartOffset + (e.mousePosition - s_incisionPanelDragStartMouse);
                        EditorUtility.SetDirty(this);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (s_draggingIncisionPanel && GUIUtility.hotControl == id)
                    {
                        s_draggingIncisionPanel = false;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        private void AppendIncisionPoint(Vector3 worldPoint)
        {
            if (_incisionStrokeWorld == null)
                _incisionStrokeWorld = new List<Vector3>();

            if (_incisionStrokeWorld.Count > 0)
            {
                float minSpacing = Mathf.Max(1e-6f, incisionMinPointSpacing);
                if ((worldPoint - _incisionStrokeWorld[_incisionStrokeWorld.Count - 1]).sqrMagnitude < minSpacing * minSpacing)
                    return;
            }

            _incisionStrokeWorld.Add(worldPoint);
        }

        private void DrawIncisionPreview(SceneView sceneView)
        {
            // 预览绘制已迁移到 ClothSimulationDriverDoubleLayerEditor.cs
        }

        private bool TryPickOuterSurface(Vector2 guiPos, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = default;
            hitNormal = Vector3.up;

            if (outerMeshFilter == null || outerMeshFilter.sharedMesh == null)
                return false;

            Ray ray = HandleUtility.GUIPointToWorldRay(guiPos);
            return RaycastMesh(ray, outerMeshFilter.sharedMesh, outerMeshFilter.transform.localToWorldMatrix, out hitPoint, out hitNormal);
        }

        private static bool RaycastMesh(Ray ray, Mesh mesh, Matrix4x4 l2w, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = default;
            hitNormal = Vector3.up;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || triangles == null || triangles.Length < 3)
                return false;

            bool found = false;
            float bestT = float.PositiveInfinity;
            Vector3 bestNormal = Vector3.up;
            Vector3 bestPoint = default;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = l2w.MultiplyPoint3x4(vertices[triangles[i]]);
                Vector3 v1 = l2w.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                Vector3 v2 = l2w.MultiplyPoint3x4(vertices[triangles[i + 2]]);

                if (RayTriangle(ray, v0, v1, v2, out float t, out Vector3 n))
                {
                    if (t < bestT)
                    {
                        bestT = t;
                        bestPoint = ray.origin + ray.direction * t;
                        bestNormal = n;
                        found = true;
                    }
                }
            }

            if (!found)
                return false;

            hitPoint = bestPoint;
            hitNormal = bestNormal;
            return true;
        }

        private static bool RayTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t, out Vector3 normal)
        {
            t = 0f;
            normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);

            if (Mathf.Abs(det) < 1e-8f)
                return false;

            float invDet = 1f / det;
            Vector3 s = ray.origin - v0;
            float u = Vector3.Dot(s, p) * invDet;
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(ray.direction, q) * invDet;
            if (v < 0f || u + v > 1f)
                return false;

            t = Vector3.Dot(e2, q) * invDet;
            return t > 1e-6f;
        }
#endif

        // ---------------- shader keyword helper ----------------

        private static void TryEnableWorldVertexKeyword(MeshFilter mf)
        {
            if (mf == null) return;
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null) return;

            // 使用 materials（会实例化），避免修改 sharedMaterial 造成“内外材质串台”
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null) mats[i].EnableKeyword("_INPUT_WORLD_VERTEX");
            }
        }

        private static void TryDisableWorldVertexKeyword(MeshFilter mf)
        {
            if (mf == null) return;
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null) return;

            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null) mats[i].DisableKeyword("_INPUT_WORLD_VERTEX");
            }
        }

        // ---------------- Mesh Subdivide (same logic as single-layer driver) ----------------

        private static void SubdivideOnce(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var triangles = mesh.triangles;

            bool hasUV = uvs != null && uvs.Length == vertices.Length;

            var vertList = new List<Vector3>(vertices);
            var uvList = hasUV ? new List<Vector2>(uvs) : null;
            var newTriangles = new List<int>(triangles.Length * 4);
            var edgeMidpointIndex = new Dictionary<int, int>(triangles.Length);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                int m01 = GetMidpointIndex(i0, i1, vertList, uvList, hasUV, edgeMidpointIndex);
                int m12 = GetMidpointIndex(i1, i2, vertList, uvList, hasUV, edgeMidpointIndex);
                int m20 = GetMidpointIndex(i2, i0, vertList, uvList, hasUV, edgeMidpointIndex);

                newTriangles.Add(i0); newTriangles.Add(m01); newTriangles.Add(m20);
                newTriangles.Add(i1); newTriangles.Add(m12); newTriangles.Add(m01);
                newTriangles.Add(i2); newTriangles.Add(m20); newTriangles.Add(m12);
                newTriangles.Add(m01); newTriangles.Add(m12); newTriangles.Add(m20);
            }

            mesh.SetVertices(vertList);
            if (hasUV && uvList != null) mesh.SetUVs(0, uvList);
            mesh.SetTriangles(newTriangles, 0);
        }

        private static int GetMidpointIndex(
            int i0,
            int i1,
            List<Vector3> vertList,
            List<Vector2> uvList,
            bool hasUV,
            Dictionary<int, int> edgeMidpointIndex)
        {
            int key = GetEdgeKey(i0, i1);
            if (edgeMidpointIndex.TryGetValue(key, out int index))
                return index;

            Vector3 v = (vertList[i0] + vertList[i1]) * 0.5f;
            index = vertList.Count;
            vertList.Add(v);

            if (hasUV && uvList != null)
            {
                Vector2 uv = (uvList[i0] + uvList[i1]) * 0.5f;
                uvList.Add(uv);
            }

            edgeMidpointIndex.Add(key, index);
            return index;
        }

        private static int GetEdgeKey(int a, int b)
        {
            if (a < b) return (a << 16) + b;
            return (b << 16) + a;
        }
    }
}
