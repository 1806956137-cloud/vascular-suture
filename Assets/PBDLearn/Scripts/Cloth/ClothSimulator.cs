using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// ClothSimulator：PBD 的“心脏”，ClothSimulator 里存着 PBD 的所有状态：
/// positions 当前粒子位置
/// predictPositions 预测位置（约束迭代用）
/// velocities 粒子速度
/// masses 质量
/// 约束数组：
///   _distanceConstraints 距离约束（结构/剪切弹簧）
///   _bendConstraints 弯曲约束（控制相邻三角夹角）
///   _pinConstraints Pin 约束（固定点）
///   _collisionConstraints 碰撞约束（每帧更新）
///   _constraintTypes 每个粒子上有哪些约束的 bit mask
/// 碰撞体组：CollidersGroup（球 / 盒 / 胶囊的描述）
/// </summary>
namespace PBDLearn
{
    [System.Flags]
    public enum ConstraintType
    {
        None = 0,
        Distance = 1 << 0,
        Pin = 1 << 1,
        Collision = 1 << 2,
        VirtualSidewall = 1 << 3,
    }

    public class TriangleIndexAABBNodeAdaptor : IAABBNodeAdaptor<int>
    {
        public AABBTree<int> AABBTree { get; private set; }

        private NativeArray<float3> _positions;
        private NativeArray<int> _indices;
        private float _inflate;

        private readonly Dictionary<int, AABBNode<int>> _leafMap = new Dictionary<int, AABBNode<int>>(1024);

        public TriangleIndexAABBNodeAdaptor(NativeArray<float3> positions, NativeArray<int> indices, float inflate)
        {
            _positions = positions;
            _indices = indices;
            _inflate = Mathf.Max(0f, inflate);
        }

        public void SetAABB(AABBTree<int> aabbTree)
        {
            AABBTree = aabbTree;
        }

        public void SetPositions(NativeArray<float3> positions)
        {
            _positions = positions;
        }

        public void SetInflate(float inflate)
        {
            _inflate = Mathf.Max(0f, inflate);
        }

        private void GetTriangle(int obj, out float3 p0, out float3 p1, out float3 p2)
        {
            int baseIndex = obj * 3;
            int i0 = _indices[baseIndex];
            int i1 = _indices[baseIndex + 1];
            int i2 = _indices[baseIndex + 2];
            p0 = _positions[i0];
            p1 = _positions[i1];
            p2 = _positions[i2];
        }

        public Vector3 GetCenter(int obj)
        {
            GetTriangle(obj, out var p0, out var p1, out var p2);
            float3 c = (p0 + p1 + p2) / 3f;
            return new Vector3(c.x, c.y, c.z);
        }

        public Vector3 GetMin(int obj)
        {
            GetTriangle(obj, out var p0, out var p1, out var p2);
            float3 min = math.min(p0, math.min(p1, p2)) - new float3(_inflate);
            return new Vector3(min.x, min.y, min.z);
        }

        public Vector3 GetMax(int obj)
        {
            GetTriangle(obj, out var p0, out var p1, out var p2);
            float3 max = math.max(p0, math.max(p1, p2)) + new float3(_inflate);
            return new Vector3(max.x, max.y, max.z);
        }

        public void MapObjectToAABBLeaf(int obj, AABBNode<int> leaf)
        {
            _leafMap[obj] = leaf;
        }

        public void UnmapObject(int obj)
        {
            _leafMap.Remove(obj);
        }

        public AABBNode<int> GetLeaf(int obj)
        {
            _leafMap.TryGetValue(obj, out var leaf);
            return leaf;
        }
    }


    public struct NeedlePortalDisplayInfo
    {
        public int portalIndex;
        public int passIndex;
        public int pairedPortalIndex;
        public int colliderEntityId;
        public int stitchIndex;
        public byte isExitPortal;
        public byte isConstraintPortal;
        public byte isHistorical;
        public byte stitchState;
        public byte reserved0;
        public byte isProvisional;
        public byte suppressPunctureFx;
        public byte isBreakthroughEvent;
        public byte wallEventOwner;
        public byte entryLayer;
        public byte breakthroughLayer;
        public byte passComplete;
    }

    public struct NeedlePortalHistoryEntry
    {
        public NeedlePortalInfo portal;
        public int portalIndex;
        public int passIndex;
        public int pairedPortalIndex;
        public int colliderEntityId;
        public int stitchIndex;
        public byte isExitPortal;
        public byte isActive;
        public byte retainAsRelaxPortal;
        public byte stitchState;
        public byte reserved0;
    }

    public struct NeedlePassLifecycleInfo
    {
        public int colliderEntityId;
        public int passIndex;
        public int historyEntries;
        public byte hasOuter;
        public byte hasInner;
        public byte hasCurrent;
        public byte hasHist;
        public byte hasDone;
        public byte hasExitPortal;
        public float3 exitPortalPoint;
        public float3 exitDirection;
        // Read-only provenance for diagnosing Current -> Hist handoff.
        public NeedlePortalInfo exitPortal;
        public int outerTriangleCount;
    }

    public struct SutureLineDisplayInfo
    {
        public int lineIndex;
        public int passIndexA;
        public int passIndexB;
        public int colliderEntityId;
        public float3 pointA;
        public float3 pointB;
        public float actualDistance;
        public float currentRestDistance;
        public float targetRestDistance;
        public float lastContactPush;
        public float selfContactThickness;
        public float selfContactNormalPush;
        public float selfContactTangentPush;
        public float minSelfContactVTDistance;
        public float minSelfContactEEDistance;
        public int roiVertA;
        public int roiVertB;
        public int roiTriA;
        public int roiTriB;
        public int virtualSidewallTriA;
        public int virtualSidewallTriB;
        public int triPairs;
        public int foldColumnCount;
        public int foldReason;
        public int patchStartA;
        public int patchCountA;
        public int patchStartB;
        public int patchCountB;
        public int contactTriStartA;
        public int contactTriCountA;
        public int contactTriStartB;
        public int contactTriCountB;
        public int vtPairs;
        public int eePairs;
        public int persistentPairs;
        public float lastCompressionFoldPush;
        public float3 foldDir;
        public byte wallLayer;
        public byte enabled;
        public byte localWallContactActive;
        public byte compressionFoldActive;
        public byte contactState;
        public byte settled;
        public byte workflowReady;
        public byte workflowReadyFrames;
        public byte workflowAdvanceReady;
        public byte workflowAdvanceReadyFrames;
        public byte workflowReleased;
        public byte completedWithoutRealSidewallContact;
    }

    internal struct SutureHistoryPassGroup
    {
        public int passIndex;
        public int colliderEntityId;
        public int outerHistoryIndex;
        public int innerHistoryIndex;
        public int latestHistoryIndex;
        public int stitchIndex;
        public bool hasActive;
        public bool hasDone;
        public bool hasHist;
        public bool hasLine;

        public bool IsComplete
        {
            get { return outerHistoryIndex >= 0 && innerHistoryIndex >= 0; }
        }
    }

    internal struct SutureBoundarySample
    {
        public bool valid;
        public byte side;
        public float distanceToSide;
        public float distanceToSideA;
        public float distanceToSideB;
        public float openGap;
        public float3 closestPointOnSide;
        public float3 closestPointOnSideA;
        public float3 closestPointOnSideB;
    }

    internal struct SutureLineSelfContactEstimate
    {
        public int triPairs;
        public int vtPairs;
        public int eePairs;
        public int sweptCandidates;
        public int ccdHit;
        public int maxPushClamped;
        public float minVTDistance;
        public float minEEDistance;
        public float normalPush;
        public float tangentPush;
    }

    public class ClothSimulator
    {
        public static readonly float3 G = new float3(0, 0, 0);
        public const int MAX_DIST_CONSTRAINT_COUNT_PER_VERTEX = 8;
        private const byte SutureLineContactStateApproach = 0;
        private const byte SutureLineContactStateLatched = 1;
        private const byte SutureLineContactStatePreLatchWallContact = 2;
        private const int SutureFoldReasonNone = 0;
        private const int SutureFoldReasonPreLatchWallContact = 1;
        private const int SutureFoldReasonContactLatched = 2;
        private const int SutureFoldReasonFallbackPatch = 3;
        private const int SutureFoldReasonUnstableContact = 4;
        private const int SutureFoldReasonReleasing = 5;
        private const int SutureFoldReasonPreLatchNoColumnSuppressed = 6;
        private const int SutureFoldReasonPreContactApproach = 7;
        private const int SutureCompressionFoldPreLatchHoldFrames = 3;
        private const int SutureCompressionFoldLatchedHoldFrames = 5;
        private const float SutureCompressionFoldEnterRatio = 0.75f;
        private const float SutureCompressionFoldExitRatio = 1.05f;
        private const float SutureCompressionFoldPreLatchDirBlend = 0.18f;
        private const float SutureCompressionFoldLatchedDirBlend = 0.32f;
        private const float SutureCompressionFoldPreLatchRiseStep = 0.16f;
        private const float SutureCompressionFoldLatchedRiseStep = 0.28f;
        private const float SutureCompressionFoldPreLatchFallStep = 0.12f;
        private const float SutureCompressionFoldLatchedFallStep = 0.22f;
        private const float SutureCompressionFoldUpBias = 0f;
        private const float SutureCompressionFoldApproachWeightScale = 0.65f;
        private const float SutureCompressionFoldApproachMinWeight = 0.2f;
        private const float SutureCompressionFoldApproachPhaseScale = 0.72f;
        private const float SutureCompressionFoldApproachMaxPushScale = 2.0f;
        private const byte SutureLineSettledHoldFrames = 18;
        private const byte SutureLineWorkflowReadyHoldFrames = 6;
        private const float SutureClosureBandPortalAnchorT = 0.22f;
        private const float SutureClosureBandSeamAnchorT = 0.78f;
        private const float AutoSuturePositionFinishDuplicateWindow = 0.75f;
        private const float AutoSuturePositionFinishDuplicateDistance = 0.00075f;
        private const float AutoSuturePositionFinishDuplicateDistanceSq =
            AutoSuturePositionFinishDuplicateDistance * AutoSuturePositionFinishDuplicateDistance;
        private const int SutureContactMissNone = 0;
        private const int SutureContactMissBoundaryGuideInvalid = 1;
        private const int SutureContactMissPatchVertexInsufficient = 2;
        private const int SutureContactMissBiteBandEmpty = 3;
        private const int SutureContactMissTriangleRoiEmpty = 4;
        private const int SutureContactMissLipTrianglesNotPhysical = 5;

        // 顶点数在网格初始化后通常保持不变。
        // **重要**：不要在 Job 链运行期间访问 NativeList.Length（哪怕只是读 Length 也会触发安全检查）。
        // 因此这里缓存一份顶点数，所有 Schedule 都使用该值。
        private int _vertexCount;
        private NativeList<float3> _velocities;
        private NativeList<float3> _predictPositions;

        private NativeList<float> _masses;

        /// <summary>
        /// 激活的约束类型
        /// </summary>
        private NativeList<ConstraintType> _constraintTypes;

        /// <summary>
        /// 距离约束
        /// </summary>
        private NativeList<DistanceConstraintInfo> _distanceConstraints;

        /// <summary>
        /// 弯曲约束
        /// </summary>
        private NativeList<BendConstraintInfo> _bendConstraints;

        /// <summary>
        /// 四面体体积约束（双层防翻层：non-inversion）
        /// </summary>        /// <summary>
        /// 体积约束全局刚度（0~1）。
        /// </summary>        /// <summary>
        /// 碰撞约束
        /// </summary>
        private NativeList<CollisionConstraintInfo> _collisionConstraints;

        /// <summary>
        /// 针尖影响区的软凹陷信息（Phase 1）。
        /// 每帧在碰撞检测阶段刷新，在最终约束阶段转成局部压入修正。
        /// </summary>
        private NativeList<NeedleTipIndentationInfo> _needleTipIndentationInfos;

        /// <summary>
        /// 碰撞滞回状态（跨帧）：0=未接触，1=接触中。
        /// 用于 enter/exit 防抖，减少擦边抖动。
        /// </summary>
        private NativeArray<byte> _collisionStates;

        /// <summary>
        /// 固定约束
        /// </summary>
        private NativeList<PinConstraintInfo> _pinConstraints;


        /// <summary>
        /// 单边不等式：外层点不能进入内层（按顶点存储，对非外层点 innerIndex=-1）
        /// </summary>
        private NativeArray<LayerSeparationInfo> _layerSeparationInfos;

        // ---------------- Cross-layer thickness links (XPBD) ----------------
        private NativeList<CrossLayerLinkInfo> _crossLayerLinks;
        private NativeArray<float> _crossLayerLambdas;
        private NativeArray<float> _distanceLambdas;
        private NativeArray<float> _bendLambdas;


        private NativeArray<float3> _positionCorrects;

        private CollidersGroup _colliderDescGroups;

        private Dictionary<int, ICollider> _colliderProxies = new Dictionary<int, ICollider>();

        private NativeList<RigidBodyForceApply> _rigidBodyForceApplys;
        private readonly List<NeedleTipPiercerFeedbackApply> _needleTipPiercerFeedbacks = new List<NeedleTipPiercerFeedbackApply>(8);
        // ---------------- Broadphase (Particle BVH) ----------------
        // 质点 BVH：用 AABBTree 对质点做粗筛，避免每帧全量遍历所有质点做精确碰撞。
        private const float DEFAULT_LEAF_INFLATE = 0.001f;
        private float _leafInflate = DEFAULT_LEAF_INFLATE;

        private AABBTree<int> _particleBVH;
        private ParticleAABBNodeAdaptor _particleBVHAdaptor;
        private List<int> _particleObjects;
        private NativeList<int> _collisionCandidates;

        private AABBTree<int> _triangleBVH;
        private TriangleIndexAABBNodeAdaptor _triangleBVHAdaptor;
        private List<int> _triangleObjects;
        private NativeList<int> _triangleSurfaceContactCandidates;
        private readonly HashSet<int> _triangleSurfaceContactCandidateSet = new HashSet<int>();

        private struct NeedlePunctureRuntimeState
        {
            public int colliderEntityId;
            public NeedlePuncturePhase phase;
            public NeedlePortalInfo outerPortal;
            public NeedlePortalInfo innerPortal;
            public float cooldown;
            public int passCounter;
            public int currentPassIndex;
            public int outerHistoryIndex;
            public int innerHistoryIndex;
            public float3 prevTipCenter;
            public float3 prevTipProbeCenter;
            public float3 prevTipForward;
            public float3 prevTailCenter;
            public byte punctureFinished;
            public byte needleInsideLumen;
            public byte currentPassIsExit;
            public byte tipHistoryValid;
            public byte reserved0;
            public byte entryLayer;
            public byte breakthroughLayer;
            public byte wallEventOwner;
            public byte hasWholeWallBreakthrough;
            public byte hookActive;
            public byte hookLayer;
            public byte hookIsEdge;
            public int hookTriangleIndex;
            public float3 hookBary;
            public float3 hookPoint;
            public float3 hookNormal;
            public float3 hookStartTipProbeCenter;
            public float hookAttachT;
            public float hookAdvance;
            public float hookDepth;
            public float hookLogCooldown;
            public byte lastAutoSuturePositionFinishValid;
            public byte lastAutoSuturePositionEntryLayer;
            public int lastAutoSuturePositionPassIndex;
            public float lastAutoSuturePositionDuplicateCooldown;
            public float3 lastAutoSuturePositionEntryWorld;
            public float3 lastAutoSuturePositionExitWorld;
        }

        private struct NeedleTipHookCandidate
        {
            public int triangleIndex;
            public NeedlePortalWallLayer layer;
            public float3 bary;
            public float3 point;
            public float3 attachPoint;
            public float3 normal;
            public float distance;
            public float attachT;
            public float depth;
            public float advance;
            public float coneLength;
            public float coneBaseRadius;
            public float coneAxial;
            public float coneLateral;
            public float coneRadiusAtPoint;
            public float normalAlign;
            public byte isEdge;
        }

        private struct NeedleTipPiercerFeedbackApply
        {
            public int colliderEntityId;
            public float3 velocityChange;
            public float3 rigDelta;
            public float desiredPush;
            public float fieldWeight;
        }

        private readonly Dictionary<int, NeedlePunctureRuntimeState> _needlePunctureStates = new Dictionary<int, NeedlePunctureRuntimeState>();
        private readonly List<NeedlePortalHistoryEntry> _needlePortalHistory = new List<NeedlePortalHistoryEntry>(32);
        private readonly List<NeedlePortalDisplayInfo> _activeNeedlePortalDisplayInfos = new List<NeedlePortalDisplayInfo>(8);
        private readonly List<SutureLineDisplayInfo> _sutureLineDisplayInfos = new List<SutureLineDisplayInfo>(8);
        private readonly List<float3> _sutureSideAOuterBoundary = new List<float3>(128);
        private readonly List<float3> _sutureSideBOuterBoundary = new List<float3>(128);
        private readonly List<float3> _sutureSideAInnerBoundary = new List<float3>(128);
        private readonly List<float3> _sutureSideBInnerBoundary = new List<float3>(128);
        private readonly List<SutureSidewallBoundaryEdgeInfo> _sutureSidewallBoundaryEdges = new List<SutureSidewallBoundaryEdgeInfo>(128);
        private bool _enableSutureSidewallDistanceConstraints = true;
        private float _sutureSidewallDistanceRestScale = 1f;
        private float _sutureSidewallDistanceStiffnessMul = 1f;
        private float _sutureSidewallShearStiffnessMul = 0.65f;
        private int _sutureSidewallDistanceConstraintCount;
        private int _sutureSidewallShearConstraintCount;
        private int _sutureSidewallMarkedParticleCount;
        private string _sutureSidewallConstraintSummary = string.Empty;
        private float3 _sutureSideACenter;
        private float3 _sutureSideBCenter;
        private bool _hasSutureBoundaryGuides;
        private NativeList<NeedlePortalInfo> _activeNeedlePortals;
        private NativeList<NeedlePortalInfo> _activeNeedleConstraintPortals;
        private NativeList<NeedleTipHookSoftConstraintInfo> _activeNeedleTipHookSoftConstraints;
        private NativeList<NeedlePortalInfo> _retainedHistoryHoldPortals;
        private NativeList<SutureLineConstraintInfo> _sutureLineConstraints;
        private NativeList<SutureLinePatchVertexInfo> _sutureLinePatchVertices;
        private NativeList<SutureLineContactTriangleInfo> _sutureLineContactTriangles;
        private bool _hasDoubleLayerSlices;
        private int _outerVertexCount;
        private int _outerTriangleCount;

        private float3 _fieldForce;

        private ClothMeshModifier _meshModifier;

        private ClothSetting _setting;

        public ClothSimulator(ClothMeshModifier meshModifier, ClothSetting setting)
        {
            _meshModifier = meshModifier;
            _setting = setting;

            // Presentation may read a paused scene before the first physics step.
            // Populate actual geometric normals instead of exposing uninitialized native memory.
            CalculateNormalsJob(default).Complete();

            var vertexCount = meshModifier.vertexCount;
            _vertexCount = vertexCount;
            _predictPositions = new NativeList<float3>(vertexCount, Allocator.Persistent);
            _predictPositions.AddRange(meshModifier.vertices);

            _velocities = new NativeList<float3>(vertexCount, Allocator.Persistent);
            _velocities.Resize(vertexCount, NativeArrayOptions.ClearMemory);

            _crossLayerLinks = new NativeList<CrossLayerLinkInfo>(128, Allocator.Persistent);
            _crossLayerLambdas = new NativeArray<float>(0, Allocator.Persistent);
            _distanceLambdas = new NativeArray<float>(0, Allocator.Persistent);
            _bendLambdas = new NativeArray<float>(0, Allocator.Persistent);

            _pinConstraints = new NativeList<PinConstraintInfo>(vertexCount, Allocator.Persistent);
            _pinConstraints.Resize(vertexCount, NativeArrayOptions.ClearMemory);

            _collisionConstraints = new NativeList<CollisionConstraintInfo>(vertexCount, Allocator.Persistent);
            _collisionConstraints.Resize(vertexCount, NativeArrayOptions.ClearMemory);

            _needleTipIndentationInfos = new NativeList<NeedleTipIndentationInfo>(vertexCount, Allocator.Persistent);
            _needleTipIndentationInfos.Resize(vertexCount, NativeArrayOptions.ClearMemory);

            _collisionStates = new NativeArray<byte>(vertexCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _activeNeedlePortals = new NativeList<NeedlePortalInfo>(8, Allocator.Persistent);
            _activeNeedleConstraintPortals = new NativeList<NeedlePortalInfo>(8, Allocator.Persistent);
            _activeNeedleTipHookSoftConstraints = new NativeList<NeedleTipHookSoftConstraintInfo>(4, Allocator.Persistent);
            _retainedHistoryHoldPortals = new NativeList<NeedlePortalInfo>(8, Allocator.Persistent);
            _sutureLineConstraints = new NativeList<SutureLineConstraintInfo>(8, Allocator.Persistent);
            _sutureLinePatchVertices = new NativeList<SutureLinePatchVertexInfo>(64, Allocator.Persistent);
            _sutureLineContactTriangles = new NativeList<SutureLineContactTriangleInfo>(64, Allocator.Persistent);
            // Layer separation info：默认 innerIndex=-1 表示无效
            _layerSeparationInfos = new NativeArray<LayerSeparationInfo>(vertexCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            for (int i = 0; i < vertexCount; i++)
            {
                var info = _layerSeparationInfos[i];
                info.innerIndex = -1;
                info.dirOut = new float3(0, 0, 1);
                info.minSep = 0;
                _layerSeparationInfos[i] = info;
            }

            _constraintTypes = new NativeList<ConstraintType>(vertexCount, Allocator.Persistent);
            _constraintTypes.Resize(vertexCount, NativeArrayOptions.ClearMemory);

            _positionCorrects = new NativeArray<float3>(vertexCount, Allocator.Persistent);
            _colliderDescGroups = new CollidersGroup(Allocator.Persistent);

            _rigidBodyForceApplys = new NativeList<RigidBodyForceApply>(vertexCount * 8, Allocator.Persistent);

            this.BuildMasses();// 根据面积/密度给每个顶点分配质量
            this.BuildDistConstraints();// 遍历所有边，为每条边记录
            this.BuildBendConstraints();// 对共边的两三角，构建一个 BendConstraintInfo，记录四个顶点 + 初始二面角 rest

            // ★ 额外一步：缝合 0° / 360° 这种“位置重合但索引分裂”的缝
            // epsilon 可以根据你血管的世界尺寸调节，比如 1e-4f ~ 1e-3f
            this.BuildSeamConstraintsByProximity(1e-4f, 4);

            if (_distanceLambdas.IsCreated) _distanceLambdas.Dispose();
            _distanceLambdas = new NativeArray<float>(_distanceConstraints.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (_bendLambdas.IsCreated) _bendLambdas.Dispose();
            _bendLambdas = new NativeArray<float>(_bendConstraints.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            // Broadphase BVH：拓扑不变时只需 Build 一次，随后每帧 Refit。
            BuildParticleBVH();
            BuildTriangleBVH();
            _collisionCandidates = new NativeList<int>(vertexCount, Allocator.Persistent);
            _triangleSurfaceContactCandidates = new NativeList<int>(math.max(16, _meshModifier.indices.Length / 3), Allocator.Persistent);

        }

        public void SetSutureBoundaryGuides(
            IReadOnlyList<Vector3> sideAOuter,
            IReadOnlyList<Vector3> sideBOuter,
            IReadOnlyList<Vector3> sideAInner,
            IReadOnlyList<Vector3> sideBInner)
        {
            CopySutureBoundaryPoints(sideAOuter, _sutureSideAOuterBoundary);
            CopySutureBoundaryPoints(sideBOuter, _sutureSideBOuterBoundary);
            CopySutureBoundaryPoints(sideAInner, _sutureSideAInnerBoundary);
            CopySutureBoundaryPoints(sideBInner, _sutureSideBInnerBoundary);
            _hasSutureBoundaryGuides =
                (_sutureSideAOuterBoundary.Count + _sutureSideAInnerBoundary.Count) >= 2 &&
                (_sutureSideBOuterBoundary.Count + _sutureSideBInnerBoundary.Count) >= 2;
            _sutureSideACenter = ComputeBoundaryCenter(_sutureSideAOuterBoundary, _sutureSideAInnerBoundary);
            _sutureSideBCenter = ComputeBoundaryCenter(_sutureSideBOuterBoundary, _sutureSideBInnerBoundary);
        }

        public void ConfigureSutureSidewallConstraints(
            bool enabled,
            float distanceRestScale,
            float distanceStiffnessMul,
            float shearStiffnessMul)
        {
            EnsureJobsCompletedForRead();
            _enableSutureSidewallDistanceConstraints = enabled;
            _sutureSidewallDistanceRestScale = math.max(0f, distanceRestScale);
            _sutureSidewallDistanceStiffnessMul = math.max(0f, distanceStiffnessMul);
            _sutureSidewallShearStiffnessMul = math.max(0f, shearStiffnessMul);

            if (_sutureSidewallBoundaryEdges.Count > 0)
            {
                RebuildSutureSidewallDistanceConstraints();
            }
        }

        public void ClearSutureBoundaryGuides()
        {
            EnsureJobsCompletedForRead();
            _sutureSideAOuterBoundary.Clear();
            _sutureSideBOuterBoundary.Clear();
            _sutureSideAInnerBoundary.Clear();
            _sutureSideBInnerBoundary.Clear();
            _sutureSidewallBoundaryEdges.Clear();
            ClearSutureSidewallConstraintState();
            RebuildDistanceLambdas();
            _sutureSideACenter = float3.zero;
            _sutureSideBCenter = float3.zero;
            _hasSutureBoundaryGuides = false;
        }

        public void SetSutureSidewallBoundaryEdges(IReadOnlyList<SutureSidewallBoundaryEdgeInfo> edges)
        {
            EnsureJobsCompletedForRead();
            ClearSutureSidewallConstraintState();
            _sutureSidewallBoundaryEdges.Clear();
            if (edges == null)
            {
                RebuildDistanceLambdas();
                return;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                SutureSidewallBoundaryEdgeInfo edge = edges[i];
                if (edge.enabled == 0 ||
                    edge.outerA < 0 || edge.outerB < 0 ||
                    edge.innerA < 0 || edge.innerB < 0 ||
                    edge.outerA == edge.outerB ||
                    edge.innerA == edge.innerB)
                    continue;

                _sutureSidewallBoundaryEdges.Add(edge);
            }

            RebuildSutureSidewallDistanceConstraints();
        }

        private void RebuildSutureSidewallDistanceConstraints()
        {
            ClearSutureSidewallConstraintState();
            if (!_enableSutureSidewallDistanceConstraints || _sutureSidewallBoundaryEdges.Count <= 0)
            {
                _sutureSidewallConstraintSummary =
                    $"disabled edges={_sutureSidewallBoundaryEdges.Count}";
                RebuildDistanceLambdas();
                return;
            }

            var added = new HashSet<ulong>();
            for (int i = 0; i < _sutureSidewallBoundaryEdges.Count; i++)
            {
                SutureSidewallBoundaryEdgeInfo edge = _sutureSidewallBoundaryEdges[i];
                if (edge.enabled == 0)
                    continue;

                MarkSutureSidewallParticle(edge.outerA);
                MarkSutureSidewallParticle(edge.outerB);
                MarkSutureSidewallParticle(edge.innerA);
                MarkSutureSidewallParticle(edge.innerB);

                float crossA = CurrentDistance(edge.outerA, edge.innerA);
                float crossB = CurrentDistance(edge.outerB, edge.innerB);
                AddSutureSidewallDistanceConstraint(
                    edge.outerA,
                    edge.innerA,
                    crossA * _sutureSidewallDistanceRestScale,
                    _sutureSidewallDistanceStiffnessMul,
                    DistanceConstraintInfo.FlagVirtualSidewallDistance,
                    edge.side,
                    edge.sourceEdgeId,
                    added);
                AddSutureSidewallDistanceConstraint(
                    edge.outerB,
                    edge.innerB,
                    crossB * _sutureSidewallDistanceRestScale,
                    _sutureSidewallDistanceStiffnessMul,
                    DistanceConstraintInfo.FlagVirtualSidewallDistance,
                    edge.side,
                    edge.sourceEdgeId,
                    added);

                float shearRestAB = ComputeScaledSutureSidewallDiagonalRest(edge.outerA, edge.innerB, crossB);
                float shearRestBA = ComputeScaledSutureSidewallDiagonalRest(edge.outerB, edge.innerA, crossA);
                AddSutureSidewallDistanceConstraint(
                    edge.outerA,
                    edge.innerB,
                    shearRestAB,
                    _sutureSidewallShearStiffnessMul,
                    DistanceConstraintInfo.FlagVirtualSidewallShear,
                    edge.side,
                    edge.sourceEdgeId,
                    added);
                AddSutureSidewallDistanceConstraint(
                    edge.outerB,
                    edge.innerA,
                    shearRestBA,
                    _sutureSidewallShearStiffnessMul,
                    DistanceConstraintInfo.FlagVirtualSidewallShear,
                    edge.side,
                    edge.sourceEdgeId,
                    added);
            }

            _sutureSidewallConstraintSummary =
                $"edges={_sutureSidewallBoundaryEdges.Count} marked={_sutureSidewallMarkedParticleCount} distance={_sutureSidewallDistanceConstraintCount} shear={_sutureSidewallShearConstraintCount} restScale={_sutureSidewallDistanceRestScale:F3} stiffness=({_sutureSidewallDistanceStiffnessMul:F2},{_sutureSidewallShearStiffnessMul:F2})";
            RebuildDistanceLambdas();
            Debug.Log($"[Suture][VirtualSidewallConstraints] {_sutureSidewallConstraintSummary}");
        }

        private void ClearSutureSidewallConstraintState()
        {
            if (_distanceConstraints.IsCreated)
            {
                for (int i = _distanceConstraints.Length - 1; i >= 0; i--)
                {
                    DistanceConstraintInfo c = _distanceConstraints[i];
                    if ((c.flags & DistanceConstraintInfo.FlagVirtualSidewall) == 0)
                        continue;
                    _distanceConstraints.RemoveAtSwapBack(i);
                }
            }

            if (_constraintTypes.IsCreated)
            {
                for (int i = 0; i < _constraintTypes.Length; i++)
                    _constraintTypes[i] &= ~ConstraintType.VirtualSidewall;
            }

            _sutureSidewallDistanceConstraintCount = 0;
            _sutureSidewallShearConstraintCount = 0;
            _sutureSidewallMarkedParticleCount = 0;
            _sutureSidewallConstraintSummary = string.Empty;
        }

        private void RebuildDistanceLambdas()
        {
            if (!_distanceConstraints.IsCreated)
                return;

            if (_distanceLambdas.IsCreated)
                _distanceLambdas.Dispose();
            _distanceLambdas = new NativeArray<float>(
                _distanceConstraints.Length,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
        }

        private void MarkSutureSidewallParticle(int vertex)
        {
            if (!_constraintTypes.IsCreated || vertex < 0 || vertex >= _constraintTypes.Length)
                return;

            if ((_constraintTypes[vertex] & ConstraintType.VirtualSidewall) == 0)
                _sutureSidewallMarkedParticleCount++;
            _constraintTypes[vertex] |= ConstraintType.VirtualSidewall;
        }

        private bool AddSutureSidewallDistanceConstraint(
            int index0,
            int index1,
            float restLength,
            float stiffnessMul,
            byte flags,
            byte sourceSide,
            int sourceEdgeId,
            HashSet<ulong> added)
        {
            if (!IsValidSutureSidewallVertex(index0) ||
                !IsValidSutureSidewallVertex(index1) ||
                index0 == index1 ||
                restLength <= 1e-7f ||
                !math.isfinite(restLength) ||
                stiffnessMul <= 0f)
            {
                return false;
            }

            ulong key = PackSutureSidewallConstraintKey(index0, index1, flags);
            if (added != null && !added.Add(key))
                return false;

            AddDistanceConstraint(index0, index1, restLength, stiffnessMul, flags, sourceSide, sourceEdgeId);
            if ((flags & DistanceConstraintInfo.FlagVirtualSidewallShear) != 0)
                _sutureSidewallShearConstraintCount++;
            else
                _sutureSidewallDistanceConstraintCount++;
            return true;
        }

        private bool IsValidSutureSidewallVertex(int vertex)
        {
            return vertex >= 0 && vertex < _predictPositions.Length && vertex < _masses.Length;
        }

        private float CurrentDistance(int a, int b)
        {
            if (!IsValidSutureSidewallVertex(a) || !IsValidSutureSidewallVertex(b))
                return 0f;
            return math.distance(_predictPositions[a], _predictPositions[b]);
        }

        private float ComputeScaledSutureSidewallDiagonalRest(int a, int b, float crossDistance)
        {
            float diagonal = CurrentDistance(a, b);
            if (diagonal <= 1e-7f)
                return 0f;

            float cross = math.max(0f, crossDistance);
            float alongSq = math.max(0f, diagonal * diagonal - cross * cross);
            float scaledCross = cross * _sutureSidewallDistanceRestScale;
            return math.sqrt(alongSq + scaledCross * scaledCross);
        }

        private static ulong PackSutureSidewallConstraintKey(int a, int b, byte flags)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)math.min(a, b)) * 1099511628211UL;
                hash = (hash ^ (uint)math.max(a, b)) * 1099511628211UL;
                hash = (hash ^ flags) * 1099511628211UL;
                return hash;
            }
        }

        private static void CopySutureBoundaryPoints(IReadOnlyList<Vector3> source, List<float3> target)
        {
            target.Clear();
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                Vector3 p = source[i];
                target.Add(new float3(p.x, p.y, p.z));
            }
        }

        private static float3 ComputeBoundaryCenter(List<float3> outer, List<float3> inner)
        {
            float3 sum = float3.zero;
            int count = 0;
            if (outer != null)
            {
                for (int i = 0; i < outer.Count; i++)
                {
                    sum += outer[i];
                    count++;
                }
            }
            if (inner != null)
            {
                for (int i = 0; i < inner.Count; i++)
                {
                    sum += inner[i];
                    count++;
                }
            }
            return count > 0 ? sum / count : float3.zero;
        }

        private void IncreaseVertex(float3 vertex)
        {
            _predictPositions.Add(vertex);
            _velocities.Add(float3.zero);
            _vertexCount++;
            for (var i = 0; i < MAX_DIST_CONSTRAINT_COUNT_PER_VERTEX; i++)
            {
                _distanceConstraints.Add(new DistanceConstraintInfo());
            }
            _pinConstraints.Add(new PinConstraintInfo());
            _collisionConstraints.Add(new CollisionConstraintInfo());
            _needleTipIndentationInfos.Add(new NeedleTipIndentationInfo());
            _constraintTypes.Add(ConstraintType.None);
            _masses.Add(0);

            _vertexCount++;
        }

        /// <summary>
        /// 计算每个质点的质量。 算法为三角面面积乘以密度，再平分到每个顶点
        /// </summary>
        private void BuildMasses()
        {
            var indices = _meshModifier.indices;
            var vertices = _meshModifier.vertices;
            _masses = new NativeList<float>(this.vertexCount, Allocator.Persistent);
            _masses.Resize(this.vertexCount, NativeArrayOptions.ClearMemory);
            for (var i = 0; i < indices.Length / 3; i++)
            {
                var offset = i * 3;
                var i0 = indices[offset];
                var i1 = indices[offset + 1];
                var i2 = indices[offset + 2];
                var v0 = vertices[i0];
                var v1 = vertices[i1];
                var v2 = vertices[i2];
                var area = IntersectUtil.GetArea(v0, v1, v2);
                var m = area * _setting.density;
                var m3 = m / 3;
                _masses[i0] += m3;
                _masses[i1] += m3;
                _masses[i2] += m3;
            }
        }

        private void BuildDistConstraints()
        {
            var edges = _meshModifier.edges;
            _distanceConstraints = new NativeList<DistanceConstraintInfo>(edges.Count, Allocator.Persistent);
            foreach (var e in edges)
            {
                this.AddDistanceConstraint(e.vIndex0, e.vIndex1);
            }
        }

        private void AddDistanceConstraint(
            int index0,
            int index1,
            float restLength,
            float stiffnessMul = 1f,
            byte flags = 0,
            byte sourceSide = 0,
            int sourceEdgeId = -1)
        {
            _constraintTypes[index0] |= ConstraintType.Distance;
            _constraintTypes[index1] |= ConstraintType.Distance;
            _distanceConstraints.Add(new DistanceConstraintInfo()
            {
                restLength = restLength,
                vIndex0 = index0,
                vIndex1 = index1,
                stiffnessMul = stiffnessMul,
                flags = flags,
                sourceSide = sourceSide,
                sourceEdgeId = sourceEdgeId,
            });
        }

        private void AddDistanceConstraint(int index0, int index1)
        {
            var restLength = math.distance(this.positions[index0], this.positions[index1]);
            AddDistanceConstraint(index0, index1, restLength, 1f);
        }

        /// <summary>
        /// 外部可添加“自定义刚度倍率”的距离约束。
        /// 用法：双层血管的跨层厚度连接（4-link）等。
        /// </summary>
        public void AddCustomDistanceConstraint(int index0, int index1, float restLength, float stiffnessMul)
        {
            AddDistanceConstraint(index0, index1, restLength, stiffnessMul);
        }

        /// <summary>
        /// 添加四面体体积约束（non-inversion）：SignedVolume(a,b,c,d) >= minVolume
        /// </summary>


        /// <summary>
        /// 额外为“空间位置几乎重合，但本来没有边连接”的顶点对
        /// 补一条距离约束，用来缝合 0°/360° 这种 UV 缝。
        /// </summary>
        /// <param name="epsilon">判定“几乎重合”的距离阈值（世界坐标）</param>
        /// <param name="maxExtraPerVertex">每个顶点最多额外补几条缝合边，避免 O(N^2) 爆炸</param>
        private void BuildSeamConstraintsByProximity(float epsilon = 1e-4f, int maxExtraPerVertex = 4)
        {
            var vertices = _meshModifier.vertices;
            var uvs = _meshModifier.uvs;
            int n = vertices.Length;
            bool hasUv = uvs.Length == n;

            // 朴素做法 O(N^2) 会爆，这里采用：
            // - 按 theta（或者 x/y/z）分桶也行，但为了最小改动
            // - 先用一个简单空间哈希 grid：key = floor(pos / cell)
            float cell = epsilon * 2f;
            var dict = new Dictionary<long, List<int>>(n);

            long Hash(float3 p)
            {
                int xi = (int)math.floor(p.x / cell);
                int yi = (int)math.floor(p.y / cell);
                int zi = (int)math.floor(p.z / cell);
                // pack 3 ints into one long (粗糙但够用)
                return ((long)xi & 0x1FFFFF) | (((long)yi & 0x1FFFFF) << 21) | (((long)zi & 0x1FFFFF) << 42);
            }

            bool LooksLikeUvWrapSeam(int i0, int i1)
            {
                if (!hasUv)
                    return true;

                float2 uv0 = uvs[i0];
                float2 uv1 = uvs[i1];
                float du = math.abs(uv0.x - uv1.x);
                float dv = math.abs(uv0.y - uv1.y);
                float uvDistSq = math.lengthsq(uv0 - uv1);

                // 只把真正像 0/1 UV seam 这种“位置重合但 UV 明显分离”的点对补缝合约束。
                // 切口复制出来的唇边顶点通常会继承同一个 UV，此时不应该再被 proximity seam 重新拉回去。
                const float minWrapGap = 0.25f;
                return du >= minWrapGap || dv >= minWrapGap || uvDistSq >= minWrapGap * minWrapGap;
            }

            for (int i = 0; i < n; i++)
            {
                long h = Hash(vertices[i]);
                if (!dict.TryGetValue(h, out var list))
                {
                    list = new List<int>(4);
                    dict[h] = list;
                }
                list.Add(i);
            }

            int added = 0;
            int skippedUvFiltered = 0;
            int[] extraCount = new int[n];

            foreach (var pair in dict)
            {
                var list = pair.Value;
                int m = list.Count;
                if (m <= 1) continue;

                for (int a = 0; a < m; a++)
                {
                    int i0 = list[a];
                    if (extraCount[i0] >= maxExtraPerVertex) continue;

                    for (int b = a + 1; b < m; b++)
                    {
                        int i1 = list[b];
                        if (extraCount[i1] >= maxExtraPerVertex) continue;

                        if (math.distance(vertices[i0], vertices[i1]) > epsilon)
                            continue;

                        if (!LooksLikeUvWrapSeam(i0, i1))
                        {
                            skippedUvFiltered++;
                            continue;
                        }

                        float rest = math.distance(this.positions[i0], this.positions[i1]);
                        AddDistanceConstraint(i0, i1, rest, 1f);
                        extraCount[i0]++;
                        extraCount[i1]++;
                        added++;

                        if (extraCount[i0] >= maxExtraPerVertex) break;
                    }
                }
            }

            if (added > 0 || skippedUvFiltered > 0)
            {
                Debug.Log($"[ClothSimulator] SeamConstraintsByProximity: added={added}, skippedUvFiltered={skippedUvFiltered}, eps={epsilon}, maxExtraPerVertex={maxExtraPerVertex}, hasUv={(hasUv ? 1 : 0)}");
            }
        }

        private void BuildBendConstraints()
        {
            var edges = _meshModifier.edges;
            _bendConstraints = new NativeList<BendConstraintInfo>(edges.Count, Allocator.Persistent);
            foreach (var e in edges)
            {
                if (e.vIndex2 >= 0 && e.vIndex3 >= 0)
                {
                    this.AddBendConstraint(e);
                }
            }
        }

        private void AddBendConstraint(ClothMeshModifier.Edge e)
        {
            var p1 = positions[e.vIndex0];
            var p2 = positions[e.vIndex1] - p1;
            var p3 = positions[e.vIndex2] - p1;
            var p4 = positions[e.vIndex3] - p1;
            p1 = 0;
            var n1 = math.normalize(math.cross(p2, p3));
            var n2 = math.normalize(math.cross(p2, p4));
            var d = math.dot(n1, n2);
            var rest = math.acos(d);
            _bendConstraints.Add(new BendConstraintInfo()
            {
                rest = rest,
                vIndex0 = e.vIndex0,
                vIndex1 = e.vIndex1,
                vIndex2 = e.vIndex2,
                vIndex3 = e.vIndex3,
            });
        }

        public int vertexCount
        {
            get
            {
                return _vertexCount;
            }
        }

        public NativeArray<float3> positions
        {
            get
            {
                EnsureJobsCompletedForRead();
                return _meshModifier.vertices;
            }
        }

        public NativeArray<float3> normals
        {
            get
            {
                EnsureJobsCompletedForRead();
                return _meshModifier.normals;
            }
        }

        public NativeArray<float3> predictPositions
        {
            get
            {
                EnsureJobsCompletedForRead();
                return _predictPositions.AsArray();
            }
        }

        public Vector3 GetWorldVertexPosition(int index)
        {
            EnsureJobsCompletedForRead();
            if (index < 0 || index >= _vertexCount) return Vector3.zero;
            float3 p = _predictPositions[index];
            return new Vector3(p.x, p.y, p.z);
        }

        // Read the current material boundary, never reassign lip identities by
        // nearest-point searches after a stitch has deformed the vessel.
        public bool TryCopyCurrentSutureOuterBoundary(List<Vector3> sideA, List<Vector3> sideB)
        {
            return TryCopyCurrentSutureBoundary(NeedlePortalWallLayer.Outer, sideA, sideB);
        }

        public bool TryCopyCurrentSutureInnerBoundary(List<Vector3> sideA, List<Vector3> sideB)
        {
            return TryCopyCurrentSutureBoundary(NeedlePortalWallLayer.Inner, sideA, sideB);
        }

        private bool TryCopyCurrentSutureBoundary(NeedlePortalWallLayer layer, List<Vector3> sideA, List<Vector3> sideB)
        {
            EnsureJobsCompletedForRead();
            sideA.Clear();
            sideB.Clear();
            if (!_predictPositions.IsCreated)
                return false;
            var seenA = new HashSet<int>();
            var seenB = new HashSet<int>();
            foreach (SutureSidewallBoundaryEdgeInfo edge in _sutureSidewallBoundaryEdges)
            {
                if (edge.side != SutureSidewallBoundaryEdgeInfo.SideA &&
                    edge.side != SutureSidewallBoundaryEdgeInfo.SideB)
                    continue;
                if (!IsValidVirtualSidewallBoundaryEdge(edge))
                    return false;
                List<Vector3> points = edge.side == SutureSidewallBoundaryEdgeInfo.SideA ? sideA : sideB;
                HashSet<int> seen = edge.side == SutureSidewallBoundaryEdgeInfo.SideA ? seenA : seenB;
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    int index = layer == NeedlePortalWallLayer.Outer
                        ? (endpoint == 0 ? edge.outerA : edge.outerB)
                        : (endpoint == 0 ? edge.innerA : edge.innerB);
                    if (!seen.Add(index))
                        continue;
                    float3 p = _predictPositions[index];
                    if (!math.all(math.isfinite(p)))
                        return false;
                    points.Add(new Vector3(p.x, p.y, p.z));
                }
            }
            return sideA.Count >= 3 && sideB.Count >= 3;
        }

        public bool TryCaptureSutureMaterialAnchor(
            Vector3 worldPoint,
            byte side,
            NeedlePortalWallLayer layer,
            out SutureMaterialAnchor anchor)
        {
            EnsureJobsCompletedForRead();
            anchor = default;
            if ((side != SutureSidewallBoundaryEdgeInfo.SideA &&
                 side != SutureSidewallBoundaryEdgeInfo.SideB) ||
                (layer != NeedlePortalWallLayer.Outer &&
                 layer != NeedlePortalWallLayer.Inner) ||
                !_predictPositions.IsCreated)
            {
                return false;
            }

            float3 target = new float3(worldPoint.x, worldPoint.y, worldPoint.z);
            float bestDistanceSq = float.PositiveInfinity;
            int bestVertexA = -1;
            int bestVertexB = -1;
            int bestSourceEdgeId = -1;
            float bestT = 0f;

            for (int i = 0; i < _sutureSidewallBoundaryEdges.Count; i++)
            {
                SutureSidewallBoundaryEdgeInfo edge = _sutureSidewallBoundaryEdges[i];
                if (edge.side != side || !IsValidVirtualSidewallBoundaryEdge(edge))
                    continue;

                int vertexA = layer == NeedlePortalWallLayer.Outer ? edge.outerA : edge.innerA;
                int vertexB = layer == NeedlePortalWallLayer.Outer ? edge.outerB : edge.innerB;
                float3 pointA = _predictPositions[vertexA];
                float3 pointB = _predictPositions[vertexB];
                float3 edgeVector = pointB - pointA;
                float lengthSq = math.lengthsq(edgeVector);
                float edgeT = lengthSq > 1e-12f
                    ? math.saturate(math.dot(target - pointA, edgeVector) / lengthSq)
                    : 0f;
                float distanceSq = math.lengthsq(target - math.lerp(pointA, pointB, edgeT));
                if (distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                bestVertexA = vertexA;
                bestVertexB = vertexB;
                bestSourceEdgeId = edge.sourceEdgeId;
                bestT = edgeT;
            }

            if (bestVertexA < 0 || bestVertexB < 0 || !math.isfinite(bestDistanceSq))
                return false;

            anchor = new SutureMaterialAnchor
            {
                vertexA = bestVertexA,
                vertexB = bestVertexB,
                sourceEdgeId = bestSourceEdgeId,
                edgeT = bestT,
                captureDistance = math.sqrt(bestDistanceSq),
                side = side,
                layer = layer,
                valid = 1,
            };
            return true;
        }

        public bool TryResolveSutureMaterialAnchor(
            SutureMaterialAnchor anchor,
            out Vector3 worldPoint)
        {
            EnsureJobsCompletedForRead();
            worldPoint = Vector3.zero;
            if (anchor.valid == 0 ||
                (anchor.side != SutureSidewallBoundaryEdgeInfo.SideA &&
                 anchor.side != SutureSidewallBoundaryEdgeInfo.SideB) ||
                (anchor.layer != NeedlePortalWallLayer.Outer &&
                 anchor.layer != NeedlePortalWallLayer.Inner) ||
                !_predictPositions.IsCreated ||
                anchor.vertexA < 0 || anchor.vertexA >= _predictPositions.Length ||
                anchor.vertexB < 0 || anchor.vertexB >= _predictPositions.Length ||
                !math.isfinite(anchor.edgeT))
            {
                return false;
            }

            float3 point = math.lerp(
                _predictPositions[anchor.vertexA],
                _predictPositions[anchor.vertexB],
                math.saturate(anchor.edgeT));
            if (!math.all(math.isfinite(point)))
                return false;

            worldPoint = new Vector3(point.x, point.y, point.z);
            return true;
        }

        public void ApplyWorldPositionDelta(int index, Vector3 delta)
        {
            _jobHandle.Complete();
            if (index < 0 || index >= _vertexCount) return;

            float3 d = new float3(delta.x, delta.y, delta.z);
            _predictPositions[index] = _predictPositions[index] + d;

            var vertices = _meshModifier.vertices;
            vertices[index] = vertices[index] + d;
        }

        public void ApplyWorldPositionDeltaPair(int indexA, Vector3 deltaA, int indexB, Vector3 deltaB)
        {
            _jobHandle.Complete();
            var vertices = _meshModifier.vertices;

            if (indexA >= 0 && indexA < _vertexCount)
            {
                float3 dA = new float3(deltaA.x, deltaA.y, deltaA.z);
                _predictPositions[indexA] = _predictPositions[indexA] + dA;
                vertices[indexA] = vertices[indexA] + dA;
            }

            if (indexB >= 0 && indexB < _vertexCount)
            {
                float3 dB = new float3(deltaB.x, deltaB.y, deltaB.z);
                _predictPositions[indexB] = _predictPositions[indexB] + dB;
                vertices[indexB] = vertices[indexB] + dB;
            }
        }


        // ---------------- Debug helpers (read-only) ----------------
        // 供 ClothDebugGizmos 等调试脚本读取内部状态（只读）。
        private void EnsureJobsCompletedForRead()
        {
            // Debug/渲染脚本可能在仿真 Job 仍在运行时访问这些只读数据。
            // 为避免 NativeContainer 安全检查报错，这里强制完成。
            _jobHandle.Complete();
        }

        public NativeArray<ConstraintType> ConstraintTypes
        {
            get { EnsureJobsCompletedForRead(); return _constraintTypes.AsArray(); }
        }

        public int distanceConstraintCount
        {
            get { EnsureJobsCompletedForRead(); return _distanceConstraints.IsCreated ? _distanceConstraints.Length : 0; }
        }

        public int sutureSidewallBoundaryEdgeCount
        {
            get { EnsureJobsCompletedForRead(); return _sutureSidewallBoundaryEdges.Count; }
        }

        public int sutureSidewallDistanceConstraintCount
        {
            get { EnsureJobsCompletedForRead(); return _sutureSidewallDistanceConstraintCount; }
        }

        public int sutureSidewallShearConstraintCount
        {
            get { EnsureJobsCompletedForRead(); return _sutureSidewallShearConstraintCount; }
        }

        public int sutureSidewallMarkedParticleCount
        {
            get { EnsureJobsCompletedForRead(); return _sutureSidewallMarkedParticleCount; }
        }

        public string sutureSidewallConstraintSummary
        {
            get { EnsureJobsCompletedForRead(); return _sutureSidewallConstraintSummary; }
        }

        public DistanceConstraintInfo GetDistanceConstraintInfo(int index)
        {
            EnsureJobsCompletedForRead();
            return _distanceConstraints[index];
        }

        // ---------------- Cross-layer thickness links (debug/read-only) ----------------

        public int crossLayerLinkCount
        {
            get { EnsureJobsCompletedForRead(); return _crossLayerLinks.IsCreated ? _crossLayerLinks.Length : 0; }
        }

        public int activePinConstraintCount
        {
            get
            {
                EnsureJobsCompletedForRead();
                if (!_constraintTypes.IsCreated)
                    return 0;
                int count = 0;
                for (int i = 0; i < _constraintTypes.Length; i++)
                {
                    if ((_constraintTypes[i] & ConstraintType.Pin) != 0)
                        count++;
                }
                return count;
            }
        }

        public int activeLayerSeparationCount
        {
            get
            {
                EnsureJobsCompletedForRead();
                if (!_layerSeparationInfos.IsCreated)
                    return 0;
                int count = 0;
                for (int i = 0; i < _layerSeparationInfos.Length; i++)
                {
                    if (_layerSeparationInfos[i].enabled != 0)
                        count++;
                }
                return count;
            }
        }

        public CrossLayerLinkInfo GetCrossLayerLinkInfo(int index)
        {
            EnsureJobsCompletedForRead();
            return _crossLayerLinks[index];
        }

        public AABBTree<int> ParticleBVH
        {
            get { EnsureJobsCompletedForRead(); return _particleBVH; }
        }

        public AABBTree<int> TriangleBVH
        {
            get { EnsureJobsCompletedForRead(); return _triangleBVH; }
        }

        public int collisionCandidateCount
        {
            get { EnsureJobsCompletedForRead(); return _collisionCandidates.IsCreated ? _collisionCandidates.Length : 0; }
        }

        public int GetCollisionCandidate(int index)
        {
            EnsureJobsCompletedForRead();
            return _collisionCandidates[index];
        }

        public int triangleSurfaceContactCandidateCount
        {
            get { EnsureJobsCompletedForRead(); return _triangleSurfaceContactCandidates.IsCreated ? _triangleSurfaceContactCandidates.Length : 0; }
        }

        public int GetTriangleSurfaceContactCandidate(int index)
        {
            EnsureJobsCompletedForRead();
            return _triangleSurfaceContactCandidates[index];
        }

        public NativeArray<int> indices
        {
            get { EnsureJobsCompletedForRead(); return _meshModifier.indices; }
        }

        public int triangleCount
        {
            get { EnsureJobsCompletedForRead(); return _meshModifier.indices.Length / 3; }
        }

        public CollidersGroup ColliderDescGroups
        {
            get { EnsureJobsCompletedForRead(); return _colliderDescGroups; }
        }

        public int activeNeedlePortalCount
        {
            get { EnsureJobsCompletedForRead(); return _activeNeedlePortals.IsCreated ? _activeNeedlePortals.Length : 0; }
        }

        public NeedlePortalInfo GetActiveNeedlePortal(int index)
        {
            EnsureJobsCompletedForRead();
            return _activeNeedlePortals[index];
        }

        public NeedlePortalDisplayInfo GetActiveNeedlePortalDisplayInfo(int index)
        {
            EnsureJobsCompletedForRead();
            return _activeNeedlePortalDisplayInfos[index];
        }

        public int activeNeedleTipHookConstraintCount
        {
            get
            {
                EnsureJobsCompletedForRead();
                return _activeNeedleTipHookSoftConstraints.IsCreated
                    ? _activeNeedleTipHookSoftConstraints.Length
                    : 0;
            }
        }

        public NeedleTipHookSoftConstraintInfo GetActiveNeedleTipHookConstraint(int index)
        {
            EnsureJobsCompletedForRead();
            return _activeNeedleTipHookSoftConstraints[index];
        }

        public int portalHistoryCount
        {
            get { EnsureJobsCompletedForRead(); return _needlePortalHistory.Count; }
        }

        public NeedlePortalHistoryEntry GetPortalHistory(int index)
        {
            EnsureJobsCompletedForRead();
            return _needlePortalHistory[index];
        }

        public int sutureLineDisplayCount
        {
            get { EnsureJobsCompletedForRead(); return _sutureLineDisplayInfos.Count; }
        }

        public SutureLineDisplayInfo GetSutureLineDisplayInfo(int index)
        {
            EnsureJobsCompletedForRead();
            return _sutureLineDisplayInfos[index];
        }

        public SutureLinePatchVertexInfo GetSutureLinePatchVertexInfo(int index)
        {
            EnsureJobsCompletedForRead();
            if (!_sutureLinePatchVertices.IsCreated || index < 0 || index >= _sutureLinePatchVertices.Length)
                return default;
            return _sutureLinePatchVertices[index];
        }

        public SutureLineContactTriangleInfo GetSutureLineContactTriangleInfo(int index)
        {
            EnsureJobsCompletedForRead();
            if (!_sutureLineContactTriangles.IsCreated || index < 0 || index >= _sutureLineContactTriangles.Length)
                return default;
            return _sutureLineContactTriangles[index];
        }

        public int GetNeedlePassCount(int colliderEntityId)
        {
            EnsureJobsCompletedForRead();
            NeedlePunctureRuntimeState state;
            if (_needlePunctureStates.TryGetValue(colliderEntityId, out state))
                return math.max(state.passCounter, state.currentPassIndex);
            return 0;
        }

        public bool TryGetNeedlePassLifecycle(
            int colliderEntityId,
            int passIndex,
            out NeedlePassLifecycleInfo info)
        {
            EnsureJobsCompletedForRead();
            info = new NeedlePassLifecycleInfo
            {
                colliderEntityId = colliderEntityId,
                passIndex = passIndex,
                outerTriangleCount = _hasDoubleLayerSlices ? _outerTriangleCount : triangleCount,
            };
            if (colliderEntityId == 0 || passIndex <= 0)
                return false;

            bool outerHist = false;
            bool innerHist = false;
            NeedlePortalInfo fallbackExitPortal = default;
            bool hasFallbackExitPortal = false;

            for (int i = 0; i < _needlePortalHistory.Count; i++)
            {
                NeedlePortalHistoryEntry entry = _needlePortalHistory[i];
                NeedlePortalInfo portal = entry.portal;
                if (entry.colliderEntityId != colliderEntityId ||
                    entry.passIndex != passIndex ||
                    portal.enabled == 0)
                {
                    continue;
                }

                info.historyEntries++;
                NeedlePortalWallLayer layer = (NeedlePortalWallLayer)portal.wallLayer;
                byte stitchState = GetPortalStitchState(entry);
                bool isCurrent = entry.isActive != 0 ||
                    stitchState == (byte)NeedlePortalStitchState.Current;
                bool isHist = !isCurrent &&
                    stitchState == (byte)NeedlePortalStitchState.Hist;

                info.hasCurrent |= (byte)(isCurrent ? 1 : 0);
                info.hasDone |= (byte)(stitchState == (byte)NeedlePortalStitchState.Done ? 1 : 0);
                if (layer == NeedlePortalWallLayer.Outer)
                {
                    info.hasOuter = 1;
                    outerHist |= isHist;
                }
                else if (layer == NeedlePortalWallLayer.Inner)
                {
                    info.hasInner = 1;
                    innerHist |= isHist;
                }

                NeedlePortalWallLayer breakthroughLayer =
                    (NeedlePortalWallLayer)portal.breakthroughLayer;
                bool isSemanticExit = breakthroughLayer != NeedlePortalWallLayer.Unknown &&
                    layer == breakthroughLayer;
                NeedlePortalWallLayer fallbackExitLayer = entry.isExitPortal != 0
                    ? NeedlePortalWallLayer.Outer
                    : NeedlePortalWallLayer.Inner;
                if (isSemanticExit || (!hasFallbackExitPortal && layer == fallbackExitLayer))
                {
                    fallbackExitPortal = portal;
                    hasFallbackExitPortal = true;
                    if (isSemanticExit)
                        info.hasExitPortal = 1;
                }
            }

            info.hasHist = (byte)(outerHist && innerHist ? 1 : 0);
            if (hasFallbackExitPortal)
            {
                // Portal caches are updated before constraint solving. Resolve
                // this read-only copy against the completed positions used by
                // the caller's collision snapshot, preserving material identity.
                if (!RefreshNeedlePortalCurrentPoint(ref fallbackExitPortal)) return false;
                float3 exitDirection = math.normalizesafe(
                    fallbackExitPortal.insertDirection,
                    new float3(0f, 0f, 1f));
                info.hasExitPortal = 1;
                info.exitPortalPoint = fallbackExitPortal.currentPoint;
                info.exitDirection = exitDirection;
                info.exitPortal = fallbackExitPortal;
            }

            return info.historyEntries > 0;
        }

        public int activeOuterNeedlePortalCount
        {
            get
            {
                EnsureJobsCompletedForRead();
                if (!_activeNeedlePortals.IsCreated) return 0;
                int count = 0;
                for (int i = 0; i < _activeNeedlePortals.Length; i++)
                {
                    if (_activeNeedlePortals[i].enabled != 0 &&
                        _activeNeedlePortals[i].wallLayer == (byte)NeedlePortalWallLayer.Outer)
                        count++;
                }
                return count;
            }
        }

        public int activeInnerNeedlePortalCount
        {
            get
            {
                EnsureJobsCompletedForRead();
                if (!_activeNeedlePortals.IsCreated) return 0;
                int count = 0;
                for (int i = 0; i < _activeNeedlePortals.Length; i++)
                {
                    if (_activeNeedlePortals[i].enabled != 0 &&
                        _activeNeedlePortals[i].wallLayer == (byte)NeedlePortalWallLayer.Inner)
                        count++;
                }
                return count;
            }
        }

        public int CountNeedlePunctureState(NeedlePuncturePhase phase)
        {
            EnsureJobsCompletedForRead();
            int count = 0;
            foreach (var kv in _needlePunctureStates)
            {
                if (kv.Value.phase == phase)
                    count++;
            }
            return count;
        }

        public NeedlePuncturePhase GetNeedlePuncturePhase(int colliderEntityId)
        {
            EnsureJobsCompletedForRead();
            NeedlePunctureRuntimeState state;
            if (_needlePunctureStates.TryGetValue(colliderEntityId, out state))
                return state.phase;
            return NeedlePuncturePhase.None;
        }

        public void SetDoubleLayerSlices(int outerVertexCount, int outerTriangleCount)
        {
            EnsureJobsCompletedForRead();
            _outerVertexCount = math.clamp(outerVertexCount, 0, vertexCount);
            _outerTriangleCount = math.clamp(outerTriangleCount, 0, triangleCount);
            _hasDoubleLayerSlices = (_outerVertexCount > 0 && _outerTriangleCount > 0);
        }

        public float compressStiffness
        {
            get { return _setting.compressStiffness; }
        }

        public float stretchStiffness
        {
            get { return _setting.stretchStiffness; }
        }

        public void SetFieldForce(float3 force)
        {
            _fieldForce = force;
        }

        public void AddCollider(ICollider collider)
        {
            if (collider == null) return;
            _colliderProxies[collider.entityId] = collider;
        }

        public void RemoveCollider(ICollider collider)
        {
            if (collider == null) return;
            _colliderProxies.Remove(collider.entityId);
        }

        public void AddPinConstraint(int index, float3 position)
        {
            this._pinConstraints[index] = new PinConstraintInfo()
            {
                basePosition = position,
                position = position,
                stiffness = 1f,
                maxCorrection = 0f,
                clampGroupId = 0,
                mode = 0,
            };
            _constraintTypes[index] |= ConstraintType.Pin;
        }

        public void AddSoftClampPinConstraint(int index, float3 position, int clampGroupId, float stiffness, float maxCorrection)
        {
            if (index < 0 || index >= vertexCount)
                return;

            this._pinConstraints[index] = new PinConstraintInfo()
            {
                basePosition = position,
                position = position,
                stiffness = math.saturate(stiffness),
                maxCorrection = math.max(0f, maxCorrection),
                clampGroupId = clampGroupId,
                mode = 1,
            };
            _constraintTypes[index] |= ConstraintType.Pin;
        }

        public void UpdateSoftClampGroupOffset(int clampGroupId, float3 offset)
        {
            if (!_pinConstraints.IsCreated)
                return;

            for (int i = 0; i < _pinConstraints.Length; i++)
            {
                PinConstraintInfo pin = _pinConstraints[i];
                if (pin.mode != 1 || pin.clampGroupId != clampGroupId)
                    continue;

                pin.position = pin.basePosition + offset;
                _pinConstraints[i] = pin;
            }
        }

        /// <summary>
        /// 设置单边不等式硬约束（外层点 outerIndex 不能进入 innerIndex 内侧）
        /// dirOut 需要是单位向量，指向“从内到外”的方向。
        /// </summary>
        public void SetLayerSeparation(int outerIndex, int innerIndex, float3 dirOut, float minSep)
        {
            if (!_layerSeparationInfos.IsCreated) return;
            if (outerIndex < 0 || outerIndex >= vertexCount) return;
            if (innerIndex < 0 || innerIndex >= vertexCount) return;

            var info = _layerSeparationInfos[outerIndex];
            info.enabled = 1;
            info.innerIndex = innerIndex;
            info.dirOut = math.normalizesafe(dirOut, new float3(0, 0, 1));
            info.minSep = math.max(0, minSep);
            _layerSeparationInfos[outerIndex] = info;
        }

        /// <summary>
        /// 清空单边分离约束（通常在重建网格/重建约束时调用）
        /// </summary>
        public void ClearLayerSeparation()
        {
            if (!_layerSeparationInfos.IsCreated) return;
            for (int i = 0; i < vertexCount; i++)
            {
                var info = _layerSeparationInfos[i];
                info.enabled = 0;
                info.innerIndex = -1;
                info.minSep = 0;
                _layerSeparationInfos[i] = info;
            }
        }


        // ---------------- Cross-layer thickness links (XPBD) ----------------

        public void ClearCrossLayerLinks()
        {
            _jobHandle.Complete();
            if (_crossLayerLinks.IsCreated) _crossLayerLinks.Clear();
            if (_crossLayerLambdas.IsCreated) _crossLayerLambdas.Dispose();
            _crossLayerLambdas = new NativeArray<float>(0, Allocator.Persistent);
        }

        /// <summary>
        /// 设置跨层厚度连接（供 DoubleLayerDriver 构建）。
        /// 注意：会 Complete 当前 JobHandle，确保安全写入。
        /// </summary>
        public void SetCrossLayerLinks(NativeArray<CrossLayerLinkInfo> links)
        {
            _jobHandle.Complete();

            if (!_crossLayerLinks.IsCreated)
                _crossLayerLinks = new NativeList<CrossLayerLinkInfo>(math.max(links.Length, 16), Allocator.Persistent);
            else
                _crossLayerLinks.Clear();

            if (links.Length > 0)
                _crossLayerLinks.AddRange(links);

            if (_crossLayerLambdas.IsCreated) _crossLayerLambdas.Dispose();
            _crossLayerLambdas = new NativeArray<float>(links.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }


        public int constraintSolverIteratorCount
        {
            get
            {
                return _setting.constraintSolverIteratorCount;
            }
        }

        private JobHandle CalculateNormalsJob(JobHandle depend)
        {
            var job = new NormalCalulateJob();
            job.indices = _meshModifier.indices;
            job.normals = _meshModifier.normals;
            job.positions = _meshModifier.vertices;
            return job.Schedule(depend);
        }

        private JobHandle EstimatePositionsJob(JobHandle depend, float dt)
        {
            var job = new PositionEstimateJob();
            // 仿真内部调度不要走只读 getter（getter 会 Complete，打断 Job 链）。
            job.normals = _meshModifier.normals;
            job.fieldForce = _fieldForce;
            job.positions = _meshModifier.vertices;
            job.velocities = _velocities;
            job.predictPositions = _predictPositions;
            job.masses = _masses;
            job.damper = _setting.damper;
            job.dt = dt;
            return job.Schedule(_vertexCount, 64, depend);
        }

        private float GetStiffnessForIteration(float stiffness, float iterationCount)
        {
            float di = 1.0f / iterationCount;
            return 1 - math.pow(1 - stiffness, di);
        }

        private JobHandle StartDistanceConstraintsJob(JobHandle depend, int iterIndex)
        {
            int iteratorCount = this.constraintSolverIteratorCount;
            float di = 1.0f / (iteratorCount - iterIndex);
            var compressStiffnessIt = GetStiffnessForIteration(this.compressStiffness, iteratorCount);
            var stretchStiffnessIt = GetStiffnessForIteration(this.stretchStiffness, iteratorCount);
            var job = new DistanceConstraintJob();
            job.di = di;
            job.distanceConstriants = _distanceConstraints;
            job.predictPositions = _predictPositions;
            job.masses = _masses;
            job.positionCorrects = _positionCorrects;
            job.stretchStiffness = compressStiffnessIt;
            job.compressStiffness = stretchStiffnessIt;
            return job.Schedule(depend);
        }

        private JobHandle StartBendConstraintsJob(JobHandle depend, int iterIndex)
        {
            float di = 1.0f / (this.constraintSolverIteratorCount - iterIndex);
            var job = new BendConstaintsGenerateJob();
            job.bendConstarints = _bendConstraints;
            job.masses = _masses;
            job.predictPositions = _predictPositions;
            job.verticesCorrectResult = _positionCorrects;
            job.di = di;
            job.bendStiffness = GetStiffnessForIteration(this._setting.bendStiffness, this.constraintSolverIteratorCount);
            return job.Schedule(depend);
        }



        private JobHandle StartXPBDDistanceConstraintsJob(JobHandle depend, float dt)
        {
            if (_distanceConstraints.Length == 0) return depend;
            if (!_distanceLambdas.IsCreated || _distanceLambdas.Length != _distanceConstraints.Length) return depend;

            var job = new XPBDDistanceConstraintJob()
            {
                predictPositions = _predictPositions.AsArray(),
                masses = _masses.AsArray(),
                distanceConstriants = _distanceConstraints.AsArray(),
                lambdas = _distanceLambdas,
                compliance = _setting.distanceCompliance,
                dt = dt,
            };
            return job.Schedule(depend);
        }

        private JobHandle StartXPBDBendConstraintsJob(JobHandle depend, float dt, int iterIndex)
        {
            if (_bendConstraints.Length == 0) return depend;
            if (!_bendLambdas.IsCreated || _bendLambdas.Length != _bendConstraints.Length) return depend;

            float di = 1.0f / (this.constraintSolverIteratorCount - iterIndex);

            var job = new XPBDBendConstraintJob()
            {
                predictPositions = _predictPositions.AsArray(),
                masses = _masses.AsArray(),
                bendConstarints = _bendConstraints.AsArray(),
                lambdas = _bendLambdas,
                compliance = _setting.bendCompliance,
                dt = dt,
                di = di,
                bendStiffness = math.max(this._setting.bendStiffness, 1e-6f),
                minGradientDenominator = 0.01f,
                bendAngleDeadZone = _setting.bendAngleDeadZone,
                bendLambdaDeadZone = _setting.bendLambdaDeadZone,
                bendPositionCorrectionDeadZone = _setting.bendPositionCorrectionDeadZone,
            };
            return job.Schedule(depend);
        }


        private JobHandle StartTriangleSurfaceContactJob(JobHandle depend)
        {
            if (_setting == null || !_setting.enableTriangleSurfaceContact) return depend;
            if (!_triangleSurfaceContactCandidates.IsCreated || _triangleSurfaceContactCandidates.Length == 0) return depend;

            var job = new TriangleSurfaceContactJob()
            {
                candidateTriangles = _triangleSurfaceContactCandidates.AsArray(),
                indices = _meshModifier.indices,
                predictPositions = _predictPositions.AsArray(),
                masses = _masses.AsArray(),
                collidersGroup = _colliderDescGroups,
                activePortals = _activeNeedleConstraintPortals.AsArray(),
                activeTipPiercerHooks = _activeNeedleTipHookSoftConstraints.AsArray(),
                positionCorrects = _positionCorrects,
                contactStiffness = _setting.triangleSurfaceContactStiffness,
                contactMargin = _setting.triangleSurfaceContactMargin,
                maxPushPerIteration = _setting.triangleSurfaceContactMaxPushPerIteration,
                enableNeedleTipPiercer = (byte)(_setting.enableNeedleTipPiercer ? 1 : 0),
            };
            return job.Schedule(depend);
        }


        private JobHandle StartNeedlePuncturePortalConstraintJob(JobHandle depend)
        {
            if (_setting == null || !_setting.enableNeedlePuncture)
                return depend;
            if (!_activeNeedleConstraintPortals.IsCreated || _activeNeedleConstraintPortals.Length == 0)
                return depend;

            var job = new NeedlePuncturePortalConstraintJob()
            {
                portals = _activeNeedleConstraintPortals.AsArray(),
                collidersGroup = _colliderDescGroups,
                masses = _masses.AsArray(),
                predictPositions = _predictPositions.AsArray(),
                positionCorrects = _positionCorrects,
                portalStiffness = _setting.needlePuncturePortalStiffness,
                maxPushPerIteration = _setting.needlePuncturePortalMaxPushPerIteration,
            };
            return job.Schedule(depend);
        }

        private JobHandle StartNeedleTipHookSoftConstraintJob(JobHandle depend)
        {
            if (_setting == null || !_setting.enableNeedleTipPiercer)
                return depend;
            if (!_activeNeedleTipHookSoftConstraints.IsCreated || _activeNeedleTipHookSoftConstraints.Length == 0)
                return depend;

            var job = new NeedleTipHookSoftConstraintJob()
            {
                hooks = _activeNeedleTipHookSoftConstraints.AsArray(),
                masses = _masses.AsArray(),
                predictPositions = _predictPositions.AsArray(),
                positionCorrects = _positionCorrects,
                maxPushPerIteration = _setting.needleTipPiercerMaxPushPerIteration,
            };
            return job.Schedule(depend);
        }

        private JobHandle StartRetainedHistoryPortalHoldConstraintJob(JobHandle depend)
        {
            if (_setting == null || !_setting.enableNeedlePuncture)
                return depend;
            if (!_retainedHistoryHoldPortals.IsCreated || _retainedHistoryHoldPortals.Length == 0)
                return depend;

            var job = new NeedlePortalHoldConstraintJob()
            {
                portals = _retainedHistoryHoldPortals.AsArray(),
                masses = _masses.AsArray(),
                predictPositions = _predictPositions.AsArray(),
                positionCorrects = _positionCorrects,
                holdStiffness = _setting.needlePuncturePortalStiffness,
                maxPushPerIteration = _setting.needlePuncturePortalMaxPushPerIteration,
            };
            return job.Schedule(depend);
        }

        private JobHandle StartSutureLineConstraintJob(
            JobHandle depend,
            float dt,
            NativeArray<SutureLineConstraintInfo> constraintsArray,
            NativeArray<SutureLinePatchVertexInfo> patchVerticesArray,
            NativeArray<SutureLineContactTriangleInfo> contactTrianglesArray,
            int constraintsCount)
        {
            if (_setting == null || !_setting.enableSutureLineConstraints)
                return depend;
            if (!constraintsArray.IsCreated || constraintsCount <= 0)
                return depend;

            var job = new SutureLineConstraintJob()
            {
                // 这里不能在求解迭代中反复读 NativeList.Length / AsArray()。
                // SutureLineConstraintJob 会写回 lambda；上一轮 job 尚未 Complete 时，
                // 主线程再读 NativeList 元数据会触发 AtomicSafetyHandle 冲突。
                // 所以 constraintsArray / patchVerticesArray 必须在进入迭代前缓存好。
                constraints = constraintsArray,
                patchVertices = patchVerticesArray,
                crossLayerLinks = _crossLayerLinks.IsCreated ? _crossLayerLinks.AsArray() : default,
                masses = _masses.AsArray(),
                predictPositions = _predictPositions.AsArray(),
                positionCorrects = _positionCorrects,
                stiffness = _setting.sutureLineWeakStiffness,
                restDistance = _setting.sutureLineMinRestDistance,
                maxCorrection = _setting.sutureLineMaxPullPerIteration,
                dt = dt,
                compliance = _setting.sutureLineXPBDCompliance,
                usePatch = (_setting.enableSutureLinePatchPull && patchVerticesArray.IsCreated && patchVerticesArray.Length > 0) ? (byte)1 : (byte)0,
                useXPBD = _setting.enableSutureLineXPBD ? (byte)1 : (byte)0,
            };
            JobHandle handle = job.Schedule(depend);
            handle = StartApplyPositionCorrectsJob(handle);
            if (!_setting.enableSutureLineContact)
                return handle;

            if (_setting.enableSutureLineCompressionFold && _setting.enableSutureLinePatchPull &&
                patchVerticesArray.IsCreated && patchVerticesArray.Length > 0 &&
                _crossLayerLinks.IsCreated && _crossLayerLinks.Length > 0)
            {
                var slideJob = new SutureLineClosureBandSlideJob()
                {
                    constraints = constraintsArray,
                    patchVertices = patchVerticesArray,
                    crossLayerLinks = _crossLayerLinks.AsArray(),
                    masses = _masses.AsArray(),
                    predictPositions = _predictPositions.AsArray(),
                    positionCorrects = _positionCorrects,
                    maxCorrection = _setting.sutureLineCompressionFoldMaxPushPerIteration * 0.1f,
                    minTargetDelta = _setting.sutureLineCompressionFoldMinTargetDelta,
                };
                handle = slideJob.Schedule(handle);
                handle = StartApplyPositionCorrectsJob(handle);
            }

            var contactJob = new SutureLineContactJob()
            {
                constraints = constraintsArray,
                patchVertices = patchVerticesArray,
                contactTriangles = contactTrianglesArray,
                crossLayerLinks = _crossLayerLinks.IsCreated ? _crossLayerLinks.AsArray() : default,
                masses = _masses.AsArray(),
                previousPositions = _meshModifier.vertices,
                predictPositions = _predictPositions.AsArray(),
                positionCorrects = _positionCorrects,
                contactGap = _setting.sutureLineContactGap,
                contactStiffness = _setting.sutureLineContactStiffness,
                maxCorrection = _setting.sutureLineContactMaxPushPerIteration,
                useSelfContact = (_setting.enableSutureLineSelfContact && patchVerticesArray.IsCreated && patchVerticesArray.Length > 0) ? (byte)1 : (byte)0,
                useLegacyContactFallback = _setting.enableSutureLineSelfContact ? (byte)0 : (byte)1,
                usePatch = (_setting.enableSutureLinePatchPull && patchVerticesArray.IsCreated && patchVerticesArray.Length > 0) ? (byte)1 : (byte)0,
                useLocalPatchContact = (!_setting.enableSutureLineSelfContact && _setting.enableSutureLineLocalPatchContact && _setting.enableSutureLinePatchPull && patchVerticesArray.IsCreated && patchVerticesArray.Length > 0) ? (byte)1 : (byte)0,
                useLocalTriangleContact = (!_setting.enableSutureLineSelfContact && _setting.enableSutureLineLocalTriangleContact && _setting.enableSutureLinePatchPull && patchVerticesArray.IsCreated && patchVerticesArray.Length > 0 && contactTrianglesArray.IsCreated && contactTrianglesArray.Length > 0) ? (byte)1 : (byte)0,
                maxPatchPairChecksPerLine = math.max(16, _setting.sutureLineLocalContactMaxPairChecksPerLine),
                maxTriangleChecksPerLine = math.max(16, _setting.sutureLineLocalContactMaxTriangleChecksPerLine),
                contactLatchedStiffnessScale = _setting.sutureLineContactLatchedStiffnessScale,
                selfContactThicknessScale = _setting.sutureLineSelfContactThicknessScale,
                selfContactEdgeScale = _setting.sutureLineSelfContactEdgeScale,
                selfContactNormalStiffness = _setting.sutureLineSelfContactNormalStiffness,
                selfContactTangentialDamping = _setting.sutureLineSelfContactTangentialDamping,
                selfContactFriction = _setting.sutureLineSelfContactFriction,
                selfContactMaxPush = _setting.sutureLineSelfContactMaxPushPerIteration,
                maxSelfContactVTPairs = math.max(8, _setting.sutureLineSelfContactMaxVTPairs),
                useSelfContactEE = _setting.enableSutureLineSelfContactEE ? (byte)1 : (byte)0,
                maxSelfContactEEPairs = _setting.enableSutureLineSelfContactEE ? math.max(0, _setting.sutureLineSelfContactMaxEEPairs) : 0,
            };
            handle = contactJob.Schedule(handle);

            if (_setting.enableSutureLineCompressionFold && _setting.enableSutureLinePatchPull && patchVerticesArray.IsCreated && patchVerticesArray.Length > 0)
            {
                handle = StartApplyPositionCorrectsJob(handle);
                var foldJob = new SutureLineCompressionFoldJob()
                {
                    constraints = constraintsArray,
                    patchVertices = patchVerticesArray,
                    crossLayerLinks = _crossLayerLinks.IsCreated ? _crossLayerLinks.AsArray() : default,
                    masses = _masses.AsArray(),
                    predictPositions = _predictPositions.AsArray(),
                    positionCorrects = _positionCorrects,
                    stiffness = _setting.sutureLineCompressionFoldStiffness,
                    maxCorrection = _setting.sutureLineCompressionFoldMaxPushPerIteration,
                    minTargetDelta = _setting.sutureLineCompressionFoldMinTargetDelta,
                };
                handle = foldJob.Schedule(handle);
            }

            return handle;
        }

        private JobHandle StartApplyPositionCorrectsJob(JobHandle depend)
        {
            if (!_predictPositions.IsCreated || !_positionCorrects.IsCreated)
                return depend;

            var job = new ApplyPositionCorrectsJob()
            {
                predictPositions = _predictPositions.AsArray(),
                positionCorrects = _positionCorrects,
            };
            return job.Schedule(_vertexCount, 64, depend);
        }

        private JobHandle StartFinalConstraintsJob(JobHandle depend, int iteratorIndex)
        {
            float di = 1.0f / (this.constraintSolverIteratorCount - iteratorIndex);
            // 不要使用 positions/predictPositions getter（会 Complete 并且可能触发 Length 读取冲突）
            var job = new ConstraintsJob(_meshModifier.vertices, _predictPositions.AsArray(), di);
            job.masses = _masses;
            job.pinConstriants = _pinConstraints;
            job.collisionConstraints = _collisionConstraints;
            job.needleTipIndentationInfos = _needleTipIndentationInfos;
            job.portals = _activeNeedleConstraintPortals.AsArray();
            job.tipPiercerHooks = _activeNeedleTipHookSoftConstraints.IsCreated
                ? _activeNeedleTipHookSoftConstraints.AsArray()
                : default;
            job.activeConstraintTypes = _constraintTypes;
            job.iterateIndex = iteratorIndex;
            job.iteratorCount = this.constraintSolverIteratorCount;
            job.positionCorrect = _positionCorrects;
            bool enableTipPiercer = _setting.enableNeedleTipPiercer;
            bool enableLegacyNeedleIndent = _setting.enableNeedleTipIndentation;
            bool enableNeedleIndent = enableLegacyNeedleIndent || enableTipPiercer;
            job.collisionSkin = _setting.collisionSkin;
            job.collisionStiffness = _setting.collisionStiffness;
            job.collisionMaxPushPerIteration = _setting.collisionMaxPushPerIteration;
            job.enableNeedleTipIndentation = (byte)(enableNeedleIndent ? 1 : 0);
            job.needleTipInfluenceRadius = enableTipPiercer
                ? math.max(_setting.needleTipInfluenceRadius, _setting.needleTipPiercerPatchRadius * 0.60f)
                : _setting.needleTipInfluenceRadius;
            job.needleTipIndentationStiffness = enableTipPiercer
                ? math.max(_setting.needleTipIndentationStiffness, _setting.needleTipPiercerAxialStiffness)
                : _setting.needleTipIndentationStiffness;
            job.needleTipMaxPushPerIteration = enableTipPiercer
                ? math.max(_setting.needleTipMaxPushPerIteration, _setting.needleTipPiercerMaxPushPerIteration)
                : _setting.needleTipMaxPushPerIteration;
            job.needleTipFalloffPower = _setting.needleTipFalloffPower;
            job.enableNeedlePuncture = (byte)((_setting != null && _setting.enableNeedlePuncture) ? 1 : 0);
            job.needlePunctureCollisionRelaxScale = (_setting != null) ? _setting.needlePunctureCollisionRelaxScale : 1f;
            job.needleTipPiercerCollisionRelaxScale = (_setting != null) ? _setting.needleTipPiercerCollisionRelaxScale : 1f;
            return job.Schedule(_vertexCount, 64, depend);
        }

        private JobHandle StartLayerSeparationJob(JobHandle depend)
        {
            // 单边不等式硬约束使用串行 Job，允许读写任意索引。
            if (!_layerSeparationInfos.IsCreated || _layerSeparationInfos.Length == 0)
                return depend;

            // 关键：调度时只做 IsCreated 检查。
            // 不要在 Job 链运行期间读取 NativeList.Length（会触发安全检查异常）。
            if (!_predictPositions.IsCreated || !_masses.IsCreated || !_constraintTypes.IsCreated)
                return depend;

            var job = new LayerSeparationJob
            {
                predictPositions = _predictPositions.AsArray(),
                masses = _masses,
                constraintTypes = _constraintTypes,
                pinConstraints = _pinConstraints,
                infos = _layerSeparationInfos,
                portals = _activeNeedleConstraintPortals.AsArray(),
                punctureMinSepScale = (_setting != null) ? _setting.needlePunctureSeparationScale : 1f,
                eps = 1e-8f,
            };
            return job.Schedule(depend);
        }

        private JobHandle StartConstraintsSolveJob(JobHandle depend, float dt)
        {
            JobHandle jobHandle = depend;

            if (_setting != null && _setting.enableXPBDInPlaneDistance && _setting.resetDistanceLambdaEachFrame
                && _distanceLambdas.IsCreated && _distanceLambdas.Length > 0)
            {
                var clear = new ClearFloatArrayJob() { values = _distanceLambdas };
                jobHandle = clear.Schedule(_distanceLambdas.Length, 64, jobHandle);
            }

            if (_setting != null && _setting.enableXPBDBend && _setting.resetBendLambdaEachFrame
                && _bendLambdas.IsCreated && _bendLambdas.Length > 0)
            {
                var clear = new ClearFloatArrayJob() { values = _bendLambdas };
                jobHandle = clear.Schedule(_bendLambdas.Length, 64, jobHandle);
            }

            // XPBD thickness：可选每帧清零 lambda（更便于排查）。
            if (_setting != null && _setting.enableXPBDThicknessLinks && _setting.resetThicknessLambdaEachFrame
                && _crossLayerLambdas.IsCreated && _crossLayerLambdas.Length > 0)
            {
                var clear = new ClearFloatArrayJob() { values = _crossLayerLambdas };
                jobHandle = clear.Schedule(_crossLayerLambdas.Length, 64, jobHandle);
            }

            NativeArray<SutureLineConstraintInfo> sutureLineConstraintsArray = default;
            NativeArray<SutureLinePatchVertexInfo> sutureLinePatchVerticesArray = default;
            NativeArray<SutureLineContactTriangleInfo> sutureLineContactTrianglesArray = default;
            int sutureLineConstraintCount = 0;
            if (_setting != null && _setting.enableSutureLineConstraints && _sutureLineConstraints.IsCreated)
            {
                // 在任何 SutureLineConstraintJob 被 schedule 之前读取一次 Length / AsArray。
                // 后续每个 solver iteration 只复用 NativeArray，避免“上一轮 job 写 NativeList，主线程读 Length”的冲突。
                sutureLineConstraintCount = _sutureLineConstraints.Length;
                if (sutureLineConstraintCount > 0)
                {
                    sutureLineConstraintsArray = _sutureLineConstraints.AsArray();
                    if (_sutureLinePatchVertices.IsCreated)
                        sutureLinePatchVerticesArray = _sutureLinePatchVertices.AsArray();
                    if (_sutureLineContactTriangles.IsCreated)
                        sutureLineContactTrianglesArray = _sutureLineContactTriangles.AsArray();
                }
            }

            for (var i = 0; i < this.constraintSolverIteratorCount; i++)
            {
                if (_setting != null && _setting.enableXPBDInPlaneDistance)
                    jobHandle = StartXPBDDistanceConstraintsJob(jobHandle, dt);
                else
                    jobHandle = StartDistanceConstraintsJob(jobHandle, i);

                if (_setting != null && _setting.enableXPBDBend)
                    jobHandle = StartXPBDBendConstraintsJob(jobHandle, dt, i);
                else
                    jobHandle = StartBendConstraintsJob(jobHandle, i);

                jobHandle = StartTriangleSurfaceContactJob(jobHandle);
                jobHandle = StartNeedleTipHookSoftConstraintJob(jobHandle);
                jobHandle = StartNeedlePuncturePortalConstraintJob(jobHandle);
                jobHandle = StartRetainedHistoryPortalHoldConstraintJob(jobHandle);
                jobHandle = StartSutureLineConstraintJob(jobHandle, dt, sutureLineConstraintsArray, sutureLinePatchVerticesArray, sutureLineContactTrianglesArray, sutureLineConstraintCount);
                jobHandle = StartFinalConstraintsJob(jobHandle, i);

                // 厚度联动：让碰撞/Pin 修正后的外层位移能传递到内层
                jobHandle = StartXPBDCrossLayerThicknessJob(jobHandle, dt);

                // 可选：在每轮内额外补几次厚度求解（排查/增强耦合）
                if (_setting != null && _setting.enableXPBDThicknessLinks)
                {
                    int extra = math.max(0, _setting.thicknessSolveExtraIterations);
                    for (int k = 0; k < extra; k++)
                        jobHandle = StartXPBDCrossLayerThicknessJob(jobHandle, dt);
                }

                jobHandle = StartLayerSeparationJob(jobHandle);

                // 最后再做一次 Pin + Collision 收口
                jobHandle = StartFinalConstraintsJob(jobHandle, i);
            }
            return jobHandle;
        }

        private JobHandle StartUpdateVelocitiesAndPositions(JobHandle depend, float dt)
        {
            var job = new UpdateVelocitiesAndPositionsJob();
            job.collisionConstraintInfos = _collisionConstraints;
            job.constraintTypes = _constraintTypes;
            // 内部调度不走 getter
            job.positions = _meshModifier.vertices;
            job.predictPositions = this._predictPositions;
            job.velocities = this._velocities;
            job.collisionNormalDamping = _setting.collisionNormalDamping;
            job.sleepPositionThreshold = _setting.sleepPositionThreshold;
            job.sleepVelocityThreshold = _setting.sleepVelocityThreshold;
            job.dt = dt;
            return job.Schedule(_vertexCount, 64, depend);
        }

        private JobHandle StartXPBDCrossLayerThicknessJob(JobHandle depend, float dt)
        {
            if (_crossLayerLinks.IsCreated == false || _crossLayerLinks.Length == 0) return depend;
            if (_crossLayerLambdas.IsCreated == false || _crossLayerLambdas.Length != _crossLayerLinks.Length) return depend;
            if (_setting == null || !_setting.enableXPBDThicknessLinks) return depend;

            var job = new XPBDCrossLayerDistanceJob()
            {
                predictPositions = _predictPositions.AsArray(),
                masses = _masses.AsArray(),
                links = _crossLayerLinks.AsArray(),
                lambdas = _crossLayerLambdas,
                compliance = _setting.thicknessCompliance,
                dt = dt,
            };
            return job.Schedule(depend);
        }


        private void UpdateColliderDescs()
        {
            _colliderDescGroups.Clear();
            foreach (var pair in _colliderProxies)
            {
                pair.Value.FillColliderDesc(_colliderDescGroups);
            }
        }

        // ---------------- BVH Broadphase：Build/Refit/Collect candidates ----------------

        private void BuildParticleBVH()
        {
            _particleObjects = new List<int>(vertexCount);
            for (int i = 0; i < vertexCount; i++) _particleObjects.Add(i);

            _particleBVHAdaptor = new ParticleAABBNodeAdaptor(_predictPositions.AsArray(), _leafInflate);
            _particleBVH = new AABBTree<int>(_particleBVHAdaptor, _particleObjects);

            // 初次 build 后 refit 一次，确保根节点 box 正确
            AABBTreeUtil.RefitRecursive(_particleBVH.rootAABB, _particleBVHAdaptor);
        }

        private void RefitParticleBVH()
        {
            if (_particleBVH == null || _particleBVHAdaptor == null || _particleBVH.rootAABB == null) return;

            _particleBVHAdaptor.SetPositions(_predictPositions.AsArray());
            _particleBVHAdaptor.SetInflate(_leafInflate);
            AABBTreeUtil.RefitRecursive(_particleBVH.rootAABB, _particleBVHAdaptor);
        }

        private void BuildTriangleBVH()
        {
            int triangleCount = _meshModifier.indices.Length / 3;
            _triangleObjects = new List<int>(triangleCount);
            for (int i = 0; i < triangleCount; i++) _triangleObjects.Add(i);

            float inflate = (_setting != null ? _setting.triangleSurfaceContactMargin : 0f) + _leafInflate;
            _triangleBVHAdaptor = new TriangleIndexAABBNodeAdaptor(_predictPositions.AsArray(), _meshModifier.indices, inflate);
            _triangleBVH = new AABBTree<int>(_triangleBVHAdaptor, _triangleObjects);

            AABBTreeUtil.RefitRecursive(_triangleBVH.rootAABB, _triangleBVHAdaptor);
        }

        private void RefitTriangleBVH()
        {
            if (_triangleBVH == null || _triangleBVHAdaptor == null || _triangleBVH.rootAABB == null) return;

            float inflate = (_setting != null ? _setting.triangleSurfaceContactMargin : 0f) + _leafInflate;
            _triangleBVHAdaptor.SetPositions(_predictPositions.AsArray());
            _triangleBVHAdaptor.SetInflate(inflate);
            AABBTreeUtil.RefitRecursive(_triangleBVH.rootAABB, _triangleBVHAdaptor);
        }
        private void CollectTriangleSurfaceContactCandidatesByBVH()
        {
            if (!_triangleSurfaceContactCandidates.IsCreated) return;
            _triangleSurfaceContactCandidates.Clear();
            _triangleSurfaceContactCandidateSet.Clear();

            if (_triangleBVH == null || _triangleBVH.rootAABB == null) return;
            if (_setting == null || !_setting.enableTriangleSurfaceContact) return;

            RefitTriangleBVH();

            float extra = math.max(0f, _setting.triangleSurfaceContactMargin) + _leafInflate;
            var capsules = _colliderDescGroups.capsules;
            for (int i = 0; i < capsules.Length; i++)
            {
                var capsule = capsules[i].collider;
                if (capsule.hasNeedleTip == 0)
                    continue;

                if (_setting.enableNeedleTipPiercer)
                {
                    float coneLength = GetNeedleTipPiercerConeLength(capsule);
                    float coneBaseRadius = GetNeedleTipPiercerConeBaseRadius(capsule);
                    float coneExtra = extra + GetNeedleTipPiercerConeSurfaceMargin(capsule);
                    AppendTriangleCandidates(ColliderAABBUtil.FromNeedleTipPiercerCone(capsule, coneLength, coneBaseRadius, coneExtra));
                }
                else
                {
                    var query = ColliderAABBUtil.FromNeedleTipProbe(capsule, extra);
                    AppendTriangleCandidates(query);
                }
            }
        }

        private void AppendTriangleCandidates(AABB query)
        {
            if (_triangleBVH == null || _triangleBVH.rootAABB == null) return;

            var list = _triangleBVH.GetAllObjects(query);
            for (int i = 0; i < list.Count; i++)
            {
                int tri = list[i];
                if (_triangleSurfaceContactCandidateSet.Add(tri))
                    _triangleSurfaceContactCandidates.Add(tri);
            }
        }

        private void CollectCollisionCandidatesByBVH()
        {
            if (!_collisionCandidates.IsCreated) return;
            _collisionCandidates.Clear();

            if (_particleBVH == null || _particleBVH.rootAABB == null) return;

            // Refit 使用最新 predictPositions
            RefitParticleBVH();

            // collider AABB 额外扩一点，避免浮点误差漏检
            float extra = _leafInflate;

            var spheres = _colliderDescGroups.spheres;
            for (int i = 0; i < spheres.Length; i++)
            {
                var query = ColliderAABBUtil.FromSphere(spheres[i].collider, extra);
                AppendCandidates(query);
            }

            var boxes = _colliderDescGroups.boxes;
            for (int i = 0; i < boxes.Length; i++)
            {
                var query = ColliderAABBUtil.FromBox(boxes[i].collider, extra);
                AppendCandidates(query);
            }

            var capsules = _colliderDescGroups.capsules;
            bool enableTipPiercer = _setting != null && _setting.enableNeedleTipPiercer;
            bool enableNeedleIndent = _setting != null && (_setting.enableNeedleTipIndentation || _setting.enableNeedleTipPiercer);
            float needleIndentRadius = 0f;
            if (_setting != null)
            {
                needleIndentRadius = enableTipPiercer
                    ? math.max(_setting.needleTipInfluenceRadius, _setting.needleTipPiercerPatchRadius * 0.60f)
                    : math.max(0f, _setting.needleTipInfluenceRadius);
            }

            for (int i = 0; i < capsules.Length; i++)
            {
                var capsule = capsules[i].collider;

                if (enableTipPiercer && capsule.hasNeedleTip != 0)
                {
                    float coneLength = GetNeedleTipPiercerConeLength(capsule);
                    float coneBaseRadius = GetNeedleTipPiercerConeBaseRadius(capsule);
                    float coneMargin = GetNeedleTipPiercerConeSurfaceMargin(capsule);

                    // TipPiercer 开启时，普通粒子-capsule collision 只查询针身；
                    // 针尖锥体前段单独走 piercer field 候选，避免圆钝 tipProbe 重新进入普通排斥。
                    AppendCandidates(ColliderAABBUtil.FromNeedleShaftCapsule(capsule, coneLength, extra));

                    float piercerExtra = extra + math.max(needleIndentRadius, coneMargin);
                    AppendCandidates(ColliderAABBUtil.FromNeedleTipPiercerCone(capsule, coneLength, coneBaseRadius, piercerExtra));
                    continue;
                }

                // 1) TipPiercer 关闭时，保留整根 capsule 的 legacy 常规粒子候选
                float capsuleExtra = extra;
                if (enableNeedleIndent)
                    capsuleExtra += needleIndentRadius;

                AppendCandidates(ColliderAABBUtil.FromCapsule(capsule, capsuleExtra));

                // 2) 对 needle capsule，再额外加一次“tip probe 驱动”的粒子候选
                if (enableNeedleIndent && capsule.hasNeedleTip != 0)
                {
                    float tipExtra = extra + needleIndentRadius;
                    AppendCandidates(ColliderAABBUtil.FromNeedleTipProbe(capsule, tipExtra));
                }
            }
        }

        private void AppendCandidates(AABB query)
        {
            if (_particleBVH == null || _particleBVH.rootAABB == null) return;

            var list = _particleBVH.GetAllObjects(query);
            for (int i = 0; i < list.Count; i++)
            {
                _collisionCandidates.Add(list[i]);
            }
        }


        public int TriangleSurfaceContactCandidateCount
        {
            get
            {
                return _triangleSurfaceContactCandidates.IsCreated ? _triangleSurfaceContactCandidates.Length : 0;
            }
        }

        public int CollisionCandidateCount
        {
            get
            {
                return _collisionCandidates.IsCreated ? _collisionCandidates.Length : 0;
            }
        }

        public int ActiveCollisionConstraintCount
        {
            get
            {
                if (!_constraintTypes.IsCreated) return 0;

                int count = 0;
                for (int i = 0; i < _constraintTypes.Length; i++)
                {
                    if ((_constraintTypes[i] & ConstraintType.Collision) == ConstraintType.Collision)
                        count++;
                }
                return count;
            }
        }

        public int ActiveNeedleIndentCount
        {
            get
            {
                if (!_needleTipIndentationInfos.IsCreated) return 0;

                int count = 0;
                var arr = _needleTipIndentationInfos.AsArray();
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i].enabled != 0)
                        count++;
                }
                return count;
            }
        }

        public float MaxNeedleIndentWeight
        {
            get
            {
                if (!_needleTipIndentationInfos.IsCreated) return 0f;

                float maxW = 0f;
                var arr = _needleTipIndentationInfos.AsArray();
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i].enabled != 0 && arr[i].weight > maxW)
                        maxW = arr[i].weight;
                }
                return maxW;
            }
        }

        public float MaxPositionCorrectMagnitude
        {
            get
            {
                if (!_positionCorrects.IsCreated) return 0f;

                float maxLen = 0f;
                for (int i = 0; i < _positionCorrects.Length; i++)
                {
                    float len = math.length(_positionCorrects[i]);
                    if (len > maxLen)
                        maxLen = len;
                }
                return maxLen;
            }
        }


        private static float3 ClosestPointOnTriangleMainThread(float3 p, float3 a, float3 b, float3 c, out float3 bary)
        {
            float3 ab = b - a;
            float3 ac = c - a;
            float3 ap = p - a;

            float d1 = math.dot(ab, ap);
            float d2 = math.dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                bary = new float3(1f, 0f, 0f);
                return a;
            }

            float3 bp = p - b;
            float d3 = math.dot(ab, bp);
            float d4 = math.dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                bary = new float3(0f, 1f, 0f);
                return b;
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / math.max(1e-8f, (d1 - d3));
                bary = new float3(1f - v, v, 0f);
                return a + v * ab;
            }

            float3 cp = p - c;
            float d5 = math.dot(ab, cp);
            float d6 = math.dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                bary = new float3(0f, 0f, 1f);
                return c;
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / math.max(1e-8f, (d2 - d6));
                bary = new float3(1f - w, 0f, w);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float denom = math.max(1e-8f, ((d4 - d3) + (d5 - d6)));
                float w = (d4 - d3) / denom;
                bary = new float3(0f, 1f - w, w);
                return b + w * (c - b);
            }

            float denomFace = 1f / math.max(1e-8f, (va + vb + vc));
            float vFace = vb * denomFace;
            float wFace = vc * denomFace;
            float uFace = 1f - vFace - wFace;
            bary = new float3(uFace, vFace, wFace);
            return a * uFace + b * vFace + c * wFace;
        }

        private bool IsTriangleInPortalLayer(int triangleIndex, NeedlePortalWallLayer desiredLayer)
        {
            if (triangleIndex < 0 || triangleIndex >= triangleCount)
                return false;

            if (!_hasDoubleLayerSlices)
                return desiredLayer != NeedlePortalWallLayer.Inner;

            if (desiredLayer == NeedlePortalWallLayer.Outer)
                return triangleIndex < _outerTriangleCount;

            if (desiredLayer == NeedlePortalWallLayer.Inner)
                return triangleIndex >= _outerTriangleCount;

            return true;
        }

        private bool IsVertexInPortalLayer(int vertexIndex, NeedlePortalWallLayer desiredLayer)
        {
            if (vertexIndex < 0 || vertexIndex >= vertexCount)
                return false;

            if (!_hasDoubleLayerSlices)
                return desiredLayer != NeedlePortalWallLayer.Inner;

            if (desiredLayer == NeedlePortalWallLayer.Outer)
                return vertexIndex < _outerVertexCount;

            if (desiredLayer == NeedlePortalWallLayer.Inner)
                return vertexIndex >= _outerVertexCount;

            return true;
        }

        private bool IsTriangleFullyInPortalLayer(int triangleIndex, NeedlePortalWallLayer desiredLayer)
        {
            if (!IsTriangleInPortalLayer(triangleIndex, desiredLayer))
                return false;
            if (!_meshModifier.indices.IsCreated)
                return false;

            int baseIndex = triangleIndex * 3;
            if (baseIndex < 0 || baseIndex + 2 >= _meshModifier.indices.Length)
                return false;

            return IsVertexInPortalLayer(_meshModifier.indices[baseIndex], desiredLayer)
                && IsVertexInPortalLayer(_meshModifier.indices[baseIndex + 1], desiredLayer)
                && IsVertexInPortalLayer(_meshModifier.indices[baseIndex + 2], desiredLayer);
        }

        private NeedlePortalWallLayer GetTrianglePortalLayer(int triangleIndex, NeedlePortalWallLayer fallback)
        {
            if (triangleIndex < 0 || triangleIndex >= triangleCount)
                return fallback;

            if (!_hasDoubleLayerSlices)
                return NeedlePortalWallLayer.Outer;

            return triangleIndex < _outerTriangleCount
                ? NeedlePortalWallLayer.Outer
                : NeedlePortalWallLayer.Inner;
        }

        private static float3 GetCapsuleSegmentEnd(CapsuleDesc capsule)
        {
            return capsule.c0 + capsule.axis.xyz * capsule.axis.w;
        }

        private static float3 ClosestPointOnCapsuleSegment(CapsuleDesc capsule, float3 point, out float t)
        {
            float3 a = capsule.c0;
            float3 b = GetCapsuleSegmentEnd(capsule);
            float3 ab = b - a;
            float abLenSq = math.lengthsq(ab);
            if (abLenSq < 1e-12f)
            {
                t = 0f;
                return a;
            }

            t = math.saturate(math.dot(point - a, ab) / abLenSq);
            return a + ab * t;
        }

        private static float GetPortalCapsuleSignedDistance(CapsuleDesc capsule, NeedlePortalInfo portal, out float segmentT)
        {
            float3 nearest = ClosestPointOnCapsuleSegment(capsule, portal.currentPoint, out segmentT);
            float centerDistance = math.distance(portal.currentPoint, nearest);
            return centerDistance - math.max(0f, capsule.radius);
        }

        private bool IsNeedlePortalOccupiedByCapsule(CapsuleDesc capsule, NeedlePortalInfo portal)
        {
            float segmentT;
            float signedDistance = GetPortalCapsuleSignedDistance(capsule, portal, out segmentT);
            float occupySlack = math.max(0.0012f, math.max(portal.relaxRadius * 0.35f, portal.captureDepth * 0.5f));
            if (signedDistance > occupySlack)
                return false;

            // 要求最近点仍落在“针身有效段”上；segmentT 过于贴近端点时允许一个很小余量，
            // 这样针尖/尾端刚经过孔道时不会因为浮点误差立刻关掉 portal。
            return segmentT >= -0.02f && segmentT <= 1.02f;
        }

        private bool IsNeedlePortalDetachedFromCapsule(CapsuleDesc capsule, NeedlePortalInfo portal)
        {
            if (portal.enabled == 0)
                return true;

            float segmentT;
            float signedDistance = GetPortalCapsuleSignedDistance(capsule, portal, out segmentT);
            float detachSlack = math.max(0.0018f, math.max(portal.relaxRadius * 0.45f, portal.captureDepth * 0.8f));
            if (signedDistance <= detachSlack)
                return false;

            return segmentT >= -0.02f && segmentT <= 1.02f;
        }

        private static bool TryGetCurrentPassExitPortal(NeedlePunctureRuntimeState state, out NeedlePortalInfo exitPortal)
        {
            exitPortal = default;
            if (!CurrentPassHasCommittedPortalPair(state))
                return false;

            exitPortal = state.currentPassIsExit != 0 ? state.outerPortal : state.innerPortal;
            return exitPortal.enabled != 0;
        }

        private static float3 GetEffectiveTailPoint(CapsuleDesc capsule)
        {
            float3 tailDir = math.normalizesafe(capsule.tailForward, -capsule.tipForward);
            return capsule.tailCenter + tailDir * math.max(0f, capsule.radius);
        }

        private static float3 GetEffectiveTailPoint(float3 tailCenter, float3 tailForward, float radius, float3 fallbackForward)
        {
            float3 tailDir = math.normalizesafe(tailForward, fallbackForward);
            return tailCenter + tailDir * math.max(0f, radius);
        }

        private bool ShouldPromoteCurrentPortalPairToHistory(CapsuleDesc capsule, NeedlePunctureRuntimeState state)
        {
            NeedlePortalInfo exitPortal;
            if (!TryGetCurrentPassExitPortal(state, out exitPortal))
                return false;

            float3 tailPoint = GetEffectiveTailPoint(capsule);
            float currentTailAxial = GetPortalAxialFromTipPoint(tailPoint, exitPortal);
            float maxTailAxial = currentTailAxial;

            if (state.tipHistoryValid != 0)
            {
                float3 prevTailForward = -math.normalizesafe(state.prevTipForward, capsule.tipForward);
                float3 prevTailPoint = GetEffectiveTailPoint(state.prevTailCenter, prevTailForward, capsule.radius, -capsule.tipForward);
                maxTailAxial = GetMaxPortalAxialOnSweep(prevTailPoint, tailPoint, exitPortal);
            }

            // Current -> Hist 只看“有效 tail 是否已经到达/越过本次 pass 的出口侧半空间”。
            // 不再要求 sweep 必须刚好夹住 exit portal 平面，也不再用 lateral 贴近度卡住：
            // - 入针 outer->inner：currentPassIsExit==0，exitPortal 是 inner portal，tail 到内壁内侧就转 Hist
            // - 出针 inner->outer：currentPassIsExit!=0，exitPortal 是 outer portal，tail 到外壁外侧就转 Hist
            // 这样 pair 创建稍晚、一帧跨过、或者没有严格按“碰内外壁顺序”采到 crossing 时，也不会漏转。
            float axialSlack = math.max(0.00035f, exitPortal.captureDepth * 0.25f);
            return maxTailAxial >= -axialSlack;
        }


        private static NeedlePortalWallLayer GetCurrentPassFirstPortalLayer(NeedlePunctureRuntimeState state)
        {
            if (state.outerPortal.enabled != 0 && state.innerPortal.enabled == 0)
                return NeedlePortalWallLayer.Outer;
            if (state.innerPortal.enabled != 0 && state.outerPortal.enabled == 0)
                return NeedlePortalWallLayer.Inner;
            return state.currentPassIsExit != 0 ? NeedlePortalWallLayer.Inner : NeedlePortalWallLayer.Outer;
        }

        private static NeedlePortalWallLayer GetNextPassFirstPortalLayer(NeedlePunctureRuntimeState state)
        {
            return state.needleInsideLumen != 0 ? NeedlePortalWallLayer.Inner : NeedlePortalWallLayer.Outer;
        }

        private static NeedlePortalWallLayer GetOppositePortalLayer(NeedlePortalWallLayer layer)
        {
            if (layer == NeedlePortalWallLayer.Outer)
                return NeedlePortalWallLayer.Inner;
            if (layer == NeedlePortalWallLayer.Inner)
                return NeedlePortalWallLayer.Outer;
            return NeedlePortalWallLayer.Unknown;
        }

        private static string FormatNeedlePortalWallLayer(NeedlePortalWallLayer layer)
        {
            if (layer == NeedlePortalWallLayer.Outer) return "Outer";
            if (layer == NeedlePortalWallLayer.Inner) return "Inner";
            return "Unknown";
        }

        private static bool CurrentPassHasCompletePortalPair(NeedlePunctureRuntimeState state)
        {
            return state.outerPortal.enabled != 0 && state.innerPortal.enabled != 0;
        }

        private static bool CurrentPassHasCommittedPortalPair(NeedlePunctureRuntimeState state)
        {
            return CurrentPassHasCompletePortalPair(state) &&
                   state.hasWholeWallBreakthrough != 0 &&
                   state.outerPortal.isProvisional == 0 &&
                   state.innerPortal.isProvisional == 0;
        }

        private static void ResetNeedleCurrentPassSemantics(ref NeedlePunctureRuntimeState state)
        {
            state.currentPassIndex = 0;
            state.currentPassIsExit = 0;
            state.entryLayer = (byte)NeedlePortalWallLayer.Unknown;
            state.breakthroughLayer = (byte)NeedlePortalWallLayer.Unknown;
            state.wallEventOwner = (byte)NeedlePortalWallEventOwner.Unknown;
            state.hasWholeWallBreakthrough = 0;
        }

        private static NeedlePortalWallLayer NormalizeAutoSuturePositionEntryLayer(NeedlePortalWallLayer entryLayer)
        {
            return entryLayer == NeedlePortalWallLayer.Inner
                ? NeedlePortalWallLayer.Inner
                : NeedlePortalWallLayer.Outer;
        }

        private static void ClearAutoSuturePositionFinishLatch(ref NeedlePunctureRuntimeState state)
        {
            state.lastAutoSuturePositionFinishValid = 0;
            state.lastAutoSuturePositionEntryLayer = (byte)NeedlePortalWallLayer.Unknown;
            state.lastAutoSuturePositionPassIndex = 0;
            state.lastAutoSuturePositionDuplicateCooldown = 0f;
            state.lastAutoSuturePositionEntryWorld = default;
            state.lastAutoSuturePositionExitWorld = default;
        }

        private static void RememberAutoSuturePositionFinish(
            ref NeedlePunctureRuntimeState state,
            NeedleSceneControlRig.AutoSuturePositionPunctureRequest request,
            NeedlePortalWallLayer entryLayer)
        {
            state.lastAutoSuturePositionFinishValid = 1;
            state.lastAutoSuturePositionEntryLayer = (byte)NormalizeAutoSuturePositionEntryLayer(entryLayer);
            state.lastAutoSuturePositionPassIndex = math.max(1, state.currentPassIndex);
            state.lastAutoSuturePositionDuplicateCooldown = AutoSuturePositionFinishDuplicateWindow;
            state.lastAutoSuturePositionEntryWorld = request.entryWorld;
            state.lastAutoSuturePositionExitWorld = request.exitWorld;
        }

        private static bool IsDuplicateCompletedAutoSuturePositionFinish(
            NeedlePunctureRuntimeState state,
            NeedleSceneControlRig.AutoSuturePositionPunctureRequest request)
        {
            if (state.lastAutoSuturePositionFinishValid == 0 ||
                state.lastAutoSuturePositionDuplicateCooldown <= 0f ||
                state.punctureFinished == 0 ||
                state.outerPortal.enabled != 0 ||
                state.innerPortal.enabled != 0 ||
                state.passCounter <= 0 ||
                state.lastAutoSuturePositionPassIndex != state.passCounter)
            {
                return false;
            }

            NeedlePortalWallLayer entryLayer = NormalizeAutoSuturePositionEntryLayer(request.entryLayer);
            if (state.lastAutoSuturePositionEntryLayer != (byte)entryLayer)
                return false;

            float3 entryWorld = request.entryWorld;
            float3 exitWorld = request.exitWorld;
            return math.distancesq(entryWorld, state.lastAutoSuturePositionEntryWorld) <= AutoSuturePositionFinishDuplicateDistanceSq &&
                   math.distancesq(exitWorld, state.lastAutoSuturePositionExitWorld) <= AutoSuturePositionFinishDuplicateDistanceSq;
        }

        private static void ResetNeedleTipHook(ref NeedlePunctureRuntimeState state)
        {
            state.hookActive = 0;
            state.hookLayer = (byte)NeedlePortalWallLayer.Unknown;
            state.hookIsEdge = 0;
            state.hookTriangleIndex = -1;
            state.hookBary = default;
            state.hookPoint = default;
            state.hookNormal = default;
            state.hookStartTipProbeCenter = default;
            state.hookAttachT = 1f;
            state.hookAdvance = 0f;
            state.hookDepth = 0f;
        }

        private static void MarkNeedlePortalPreChannel(ref NeedlePortalInfo portal, NeedlePortalWallLayer entryLayer, NeedlePortalWallLayer breakthroughLayer)
        {
            portal.isProvisional = 1;
            portal.suppressPunctureFx = 1;
            portal.isBreakthroughEvent = 0;
            portal.wallEventOwner = (byte)NeedlePortalWallEventOwner.WholeWall;
            portal.entryLayer = (byte)entryLayer;
            portal.breakthroughLayer = (byte)breakthroughLayer;
            portal.passComplete = 0;
        }

        private static void MarkNeedlePortalWholeWallCommitted(ref NeedlePortalInfo portal, NeedlePortalWallLayer entryLayer, NeedlePortalWallLayer breakthroughLayer, bool isBreakthroughPortal)
        {
            portal.isProvisional = 0;
            portal.suppressPunctureFx = (byte)(isBreakthroughPortal ? 0 : 1);
            portal.isBreakthroughEvent = (byte)(isBreakthroughPortal ? 1 : 0);
            portal.wallEventOwner = (byte)NeedlePortalWallEventOwner.WholeWall;
            portal.entryLayer = (byte)entryLayer;
            portal.breakthroughLayer = (byte)breakthroughLayer;
            portal.passComplete = 1;
        }

        private int GetNeedlePortalHistoryPortalIndex(int historyIndex)
        {
            if ((uint)historyIndex >= (uint)_needlePortalHistory.Count)
                return 0;
            return _needlePortalHistory[historyIndex].portalIndex;
        }

        private void RegisterCommittedWholeWallPortalPair(ref NeedlePunctureRuntimeState state)
        {
            if (!CurrentPassHasCommittedPortalPair(state))
                return;

            bool passIsExit = state.currentPassIsExit != 0;
            int passIndex = math.max(1, state.currentPassIndex);
            NeedlePortalWallLayer entryLayer = (NeedlePortalWallLayer)state.entryLayer;

            if (entryLayer == NeedlePortalWallLayer.Inner)
            {
                if (state.innerHistoryIndex < 0)
                    state.innerHistoryIndex = RegisterNeedlePortalHistory(state.innerPortal, passIndex, passIsExit, 0);

                int innerPortalIndex = GetNeedlePortalHistoryPortalIndex(state.innerHistoryIndex);
                if (state.outerHistoryIndex < 0)
                    state.outerHistoryIndex = RegisterNeedlePortalHistory(state.outerPortal, passIndex, passIsExit, innerPortalIndex);

                int outerPortalIndex = GetNeedlePortalHistoryPortalIndex(state.outerHistoryIndex);
                SetNeedlePortalHistoryPair(state.innerHistoryIndex, outerPortalIndex);
            }
            else
            {
                if (state.outerHistoryIndex < 0)
                    state.outerHistoryIndex = RegisterNeedlePortalHistory(state.outerPortal, passIndex, passIsExit, 0);

                int outerPortalIndex = GetNeedlePortalHistoryPortalIndex(state.outerHistoryIndex);
                if (state.innerHistoryIndex < 0)
                    state.innerHistoryIndex = RegisterNeedlePortalHistory(state.innerPortal, passIndex, passIsExit, outerPortalIndex);

                int innerPortalIndex = GetNeedlePortalHistoryPortalIndex(state.innerHistoryIndex);
                SetNeedlePortalHistoryPair(state.outerHistoryIndex, innerPortalIndex);
            }

            SyncNeedlePortalHistory(state.outerHistoryIndex, state.outerPortal, true);
            SyncNeedlePortalHistory(state.innerHistoryIndex, state.innerPortal, true);
        }

        private int RegisterNeedlePortalHistory(NeedlePortalInfo portal, int passIndex, bool isExitPortal, int pairedPortalIndex)
        {
            var entry = new NeedlePortalHistoryEntry()
            {
                portal = portal,
                portalIndex = _needlePortalHistory.Count + 1,
                passIndex = math.max(1, passIndex),
                pairedPortalIndex = math.max(0, pairedPortalIndex),
                colliderEntityId = portal.colliderEntityId,
                isExitPortal = (byte)(isExitPortal ? 1 : 0),
                isActive = 1,
                retainAsRelaxPortal = 0,
                stitchState = portal.stitchState,
                reserved0 = 0,
            };
            _needlePortalHistory.Add(entry);
            return _needlePortalHistory.Count - 1;
        }

        private void SetNeedlePortalHistoryPair(int historyIndex, int pairedPortalIndex)
        {
            if ((uint)historyIndex >= (uint)_needlePortalHistory.Count)
                return;

            var entry = _needlePortalHistory[historyIndex];
            entry.pairedPortalIndex = math.max(0, pairedPortalIndex);
            _needlePortalHistory[historyIndex] = entry;
        }

        private void SyncNeedlePortalHistory(int historyIndex, NeedlePortalInfo portal, bool isActive)
        {
            if ((uint)historyIndex >= (uint)_needlePortalHistory.Count)
                return;

            var entry = _needlePortalHistory[historyIndex];
            if (isActive)
            {
                portal.stitchState = (byte)NeedlePortalStitchState.Current;
                entry.stitchState = (byte)NeedlePortalStitchState.Current;
            }
            else if (entry.stitchState == 0)
            {
                entry.stitchState = (byte)NeedlePortalStitchState.Hist;
            }
            portal.stitchState = entry.stitchState;
            entry.portal = portal;
            entry.isActive = (byte)(isActive ? 1 : 0);
            _needlePortalHistory[historyIndex] = entry;
        }

        private void PromoteNeedlePortalHistoryToRetainedRelax(int historyIndex, NeedlePortalInfo portal)
        {
            if ((uint)historyIndex >= (uint)_needlePortalHistory.Count)
                return;

            var entry = _needlePortalHistory[historyIndex];
            portal.stitchState = entry.stitchState == (byte)NeedlePortalStitchState.Done
                ? (byte)NeedlePortalStitchState.Done
                : (byte)NeedlePortalStitchState.Hist;
            entry.portal = portal;
            entry.isActive = 0;
            entry.retainAsRelaxPortal = 1;
            if (entry.stitchState != (byte)NeedlePortalStitchState.Done)
                entry.stitchState = (byte)NeedlePortalStitchState.Hist;
            _needlePortalHistory[historyIndex] = entry;
        }

        private void DisableNeedlePortalHistoryRetention(int historyIndex)
        {
            if ((uint)historyIndex >= (uint)_needlePortalHistory.Count)
                return;

            var entry = _needlePortalHistory[historyIndex];
            entry.isActive = 0;
            entry.retainAsRelaxPortal = 0;
            if (entry.stitchState == 0)
                entry.stitchState = (byte)NeedlePortalStitchState.Hist;
            var portal = entry.portal;
            portal.stitchState = entry.stitchState;
            entry.portal = portal;
            _needlePortalHistory[historyIndex] = entry;
        }

        private void DisableNeedlePortalHistoryRetentionForCollider(int colliderEntityId)
        {
            if (colliderEntityId == 0)
                return;

            for (int i = 0; i < _needlePortalHistory.Count; i++)
            {
                var entry = _needlePortalHistory[i];
                if (entry.colliderEntityId != colliderEntityId)
                    continue;

                entry.isActive = 0;
                entry.retainAsRelaxPortal = 0;
                if (entry.stitchState == 0)
                    entry.stitchState = (byte)NeedlePortalStitchState.Hist;
                var portal = entry.portal;
                portal.stitchState = entry.stitchState;
                entry.portal = portal;
                _needlePortalHistory[i] = entry;
            }
        }

        private bool TryPullNextHistSutureLineForCollider(int colliderEntityId, bool strongPullLine)
        {
            if (!_sutureLineConstraints.IsCreated)
                return false;

            List<SutureHistoryPassGroup> groups = BuildSutureHistoryPassGroups(colliderEntityId);
            groups.Sort((a, b) => a.passIndex.CompareTo(b.passIndex));
            Debug.Log($"[Suture][PullLineStart] collider={colliderEntityId} strongPull={(strongPullLine ? 1 : 0)} histPasses={groups.Count} pairing=odd-even WholeWall P1<->P2,P3<->P4");

            SutureHistoryPassGroup passA;
            SutureHistoryPassGroup passB;
            bool blockedByExistingLine;
            int existingLineIndex;
            int existingPassA;
            int existingPassB;
            if (!TryFindNextPullLineHistPassPair(
                groups,
                out passA,
                out passB,
                out blockedByExistingLine,
                out existingLineIndex,
                out existingPassA,
                out existingPassB))
            {
                if (blockedByExistingLine)
                {
                    Debug.Log($"[Suture][PullLineAlreadyExists] collider={colliderEntityId} lineIndex={existingLineIndex} Hist{existingPassA}<->Hist{existingPassB} wholeWall=1");
                }
                else
                {
                    Debug.LogWarning($"[Suture][PullLineNoPair] 没有找到可拉线的奇偶 Hist 孔位组。规则是 Hist1<->Hist2、Hist3<->Hist4、Hist5<->Hist6；collider={colliderEntityId} histPasses={groups.Count}");
                }
                return false;
            }

            int lineIndex = GetNextSutureLineIndex();
            if (!TryAddWholeWallSutureLineConstraint(passA, passB, lineIndex, strongPullLine, false, out SutureLineConstraintInfo constraint))
            {
                Debug.LogWarning($"[Suture][PullLineNoPair] 找到了成对 Hist 孔位组，但未能生成 WholeWall 拉线约束。collider={colliderEntityId} passA={passA.passIndex} passB={passB.passIndex}");
                return false;
            }

            MarkSuturePassLineLinked(passA.passIndex, colliderEntityId, lineIndex);
            MarkSuturePassLineLinked(passB.passIndex, colliderEntityId, lineIndex);

            string contactMode = _setting != null && _setting.enableSutureLineSelfContact
                ? "LocalWallSelfContact"
                : "LegacyFallback";
            Debug.Log($"[Suture][PullLineCreated] lineIndex={lineIndex} collider={colliderEntityId} strongPull={constraint.strongPullLine} Hist{passA.passIndex}<->Hist{passB.passIndex} wholeWall=1 contactMode={contactMode} contactState={FormatSutureLineContactState(constraint.contactState)} contactLatched={constraint.contactLatched} actualDistance={constraint.currentRestDistance:F6} currentRest={constraint.currentRestDistance:F6} targetRest={constraint.targetRestDistance:F6} targetDelta={constraint.targetDelta:F6} adaptiveTarget={constraint.adaptiveTargetUsed} biteA={constraint.biteDistanceA:F6} biteB={constraint.biteDistanceB:F6} thickness={constraint.localWallThickness:F6} avgEdge={constraint.localAverageEdgeLength:F6} openGap={constraint.localOpenGap:F6} closureDir=({constraint.closureDirection.x:F3},{constraint.closureDirection.y:F3},{constraint.closureDirection.z:F3}) contactN=({constraint.contactNormalHint.x:F3},{constraint.contactNormalHint.y:F3},{constraint.contactNormalHint.z:F3}) roiVertA={constraint.patchCountA} roiVertB={constraint.patchCountB} patchOuterA={constraint.patchOuterCountA} patchInnerA={constraint.patchInnerCountA} patchOuterB={constraint.patchOuterCountB} patchInnerB={constraint.patchInnerCountB} bandA={constraint.closureBandCountA} bandB={constraint.closureBandCountB} roiTriA={constraint.contactTriCountA} roiTriB={constraint.contactTriCountB} virtualSidewallTriA={constraint.virtualSidewallTriCountA} virtualSidewallTriB={constraint.virtualSidewallTriCountB} sidewallEdges={_sutureSidewallBoundaryEdges.Count} contactThickness={constraint.selfContactThickness:F6} missReason={FormatSutureLineContactMissReason(constraint.contactMissReason)} selfContact={(_setting != null && _setting.enableSutureLineSelfContact)} contactGap={(_setting != null ? _setting.sutureLineContactGap : 0f):F6} localPatchContact={(_setting != null && _setting.enableSutureLineLocalPatchContact)} localTriContact={(_setting != null && _setting.enableSutureLineLocalTriangleContact)} localPairCap={(_setting != null ? _setting.sutureLineLocalContactMaxPairChecksPerLine : 0)} tensionOnly=1 xpbd={(_setting != null && _setting.enableSutureLineXPBD)}");
            return true;
        }

        private bool TryPullExactHistSutureLineForCollider(int colliderEntityId, int passIndexA, int passIndexB, bool strongPullLine)
        {
            if (!_sutureLineConstraints.IsCreated)
                return false;
            if (colliderEntityId == 0 || passIndexA <= 0 || passIndexB <= 0 || passIndexA == passIndexB)
            {
                Debug.LogWarning($"[Suture][AutoPullLineNoPair] invalid request collider={colliderEntityId} passA={passIndexA} passB={passIndexB}");
                return false;
            }

            List<SutureHistoryPassGroup> groups = BuildSutureHistoryPassGroups(colliderEntityId);
            groups.Sort((a, b) => a.passIndex.CompareTo(b.passIndex));
            Debug.Log($"[Suture][AutoPullLineStart] collider={colliderEntityId} strongPull={(strongPullLine ? 1 : 0)} passA={passIndexA} passB={passIndexB} histPasses={groups.Count} pairing=explicit");

            if (!TryFindReadySutureHistoryPassGroup(groups, passIndexA, out SutureHistoryPassGroup passA) ||
                !TryFindReadySutureHistoryPassGroup(groups, passIndexB, out SutureHistoryPassGroup passB))
            {
                Debug.LogWarning($"[Suture][AutoPullLineNoPair] exact Hist pair is not ready. collider={colliderEntityId} passA={passIndexA} passB={passIndexB} histPasses={groups.Count}");
                return false;
            }

            if (TryGetSutureLinePairStatus(passA.passIndex, passB.passIndex, colliderEntityId, out int existingLineIndex))
            {
                Debug.Log($"[Suture][AutoPullLineAlreadyExists] collider={colliderEntityId} lineIndex={existingLineIndex} Hist{passA.passIndex}<->Hist{passB.passIndex} wholeWall=1");
                return true;
            }

            int lineIndex = GetNextSutureLineIndex();
            if (!TryAddWholeWallSutureLineConstraint(passA, passB, lineIndex, strongPullLine, true, out SutureLineConstraintInfo constraint))
            {
                Debug.LogWarning($"[Suture][AutoPullLineNoPair] exact Hist pair found but constraint creation failed. collider={colliderEntityId} passA={passA.passIndex} passB={passB.passIndex}");
                return false;
            }

            MarkSuturePassLineLinked(passA.passIndex, colliderEntityId, lineIndex);
            MarkSuturePassLineLinked(passB.passIndex, colliderEntityId, lineIndex);

            Debug.Log($"[Suture][AutoPullLineCreated] lineIndex={lineIndex} collider={colliderEntityId} strongPull={constraint.strongPullLine} Hist{passA.passIndex}<->Hist{passB.passIndex} wholeWall=1 contactState={FormatSutureLineContactState(constraint.contactState)} contactLatched={constraint.contactLatched} actualDistance={constraint.currentRestDistance:F6} targetRest={constraint.targetRestDistance:F6} targetDelta={constraint.targetDelta:F6} biteA={constraint.biteDistanceA:F6} biteB={constraint.biteDistanceB:F6} thickness={constraint.localWallThickness:F6} avgEdge={constraint.localAverageEdgeLength:F6} openGap={constraint.localOpenGap:F6}");
            return true;
        }

        private bool TryFindNextPullLineHistPassPair(
            List<SutureHistoryPassGroup> groups,
            out SutureHistoryPassGroup passA,
            out SutureHistoryPassGroup passB,
            out bool blockedByExistingLine,
            out int existingLineIndex,
            out int existingPassA,
            out int existingPassB)
        {
            passA = default(SutureHistoryPassGroup);
            passB = default(SutureHistoryPassGroup);
            blockedByExistingLine = false;
            existingLineIndex = 0;
            existingPassA = 0;
            existingPassB = 0;

            int maxPass = 0;
            for (int i = 0; i < groups.Count; i++)
                maxPass = math.max(maxPass, groups[i].passIndex);

            bool sawExistingOnly = false;
            bool sawIncompleteOpenPair = false;
            for (int pairStart = 1; pairStart <= maxPass; pairStart += 2)
            {
                bool hasA = TryFindReadySutureHistoryPassGroup(groups, pairStart, out SutureHistoryPassGroup a);
                bool hasB = TryFindReadySutureHistoryPassGroup(groups, pairStart + 1, out SutureHistoryPassGroup b);

                if (!hasA || !hasB)
                {
                    if (hasA || hasB)
                        sawIncompleteOpenPair = true;
                    continue;
                }

                int pairLineIndex;
                if (TryGetSutureLinePairStatus(a.passIndex, b.passIndex, a.colliderEntityId, out pairLineIndex))
                {
                    sawExistingOnly = true;
                    if (existingLineIndex == 0)
                    {
                        existingLineIndex = pairLineIndex;
                        existingPassA = a.passIndex;
                        existingPassB = b.passIndex;
                    }
                    continue;
                }

                passA = a;
                passB = b;
                return true;
            }

            blockedByExistingLine = sawExistingOnly && !sawIncompleteOpenPair;
            return false;
        }

        private bool TryFindReadySutureHistoryPassGroup(List<SutureHistoryPassGroup> groups, int passIndex, out SutureHistoryPassGroup group)
        {
            group = default(SutureHistoryPassGroup);
            for (int i = 0; i < groups.Count; i++)
            {
                SutureHistoryPassGroup candidate = groups[i];
                if (candidate.passIndex != passIndex)
                    continue;
                if (!IsPullLinePairCandidateBaseReady(candidate))
                    return false;

                group = candidate;
                return true;
            }
            return false;
        }

        private bool IsPullLinePairCandidateBaseReady(SutureHistoryPassGroup group)
        {
            return group.IsComplete && !group.hasActive && !group.hasDone && group.hasHist;
        }

        private List<SutureHistoryPassGroup> BuildSutureHistoryPassGroups(int colliderEntityId)
        {
            var groups = new List<SutureHistoryPassGroup>();
            for (int i = 0; i < _needlePortalHistory.Count; i++)
            {
                NeedlePortalHistoryEntry entry = _needlePortalHistory[i];
                if (entry.colliderEntityId != colliderEntityId)
                    continue;
                if (entry.portal.enabled == 0)
                    continue;

                int groupIndex = -1;
                for (int j = 0; j < groups.Count; j++)
                {
                    if (groups[j].passIndex == entry.passIndex)
                    {
                        groupIndex = j;
                        break;
                    }
                }

                SutureHistoryPassGroup group;
                if (groupIndex >= 0)
                {
                    group = groups[groupIndex];
                }
                else
                {
                    group = new SutureHistoryPassGroup()
                    {
                        passIndex = entry.passIndex,
                        colliderEntityId = colliderEntityId,
                        outerHistoryIndex = -1,
                        innerHistoryIndex = -1,
                        latestHistoryIndex = -1,
                        stitchIndex = entry.stitchIndex,
                        hasActive = false,
                        hasDone = false,
                        hasHist = false,
                        hasLine = false,
                    };
                }

                if (entry.portal.wallLayer == (byte)NeedlePortalWallLayer.Outer)
                    group.outerHistoryIndex = i;
                else if (entry.portal.wallLayer == (byte)NeedlePortalWallLayer.Inner)
                    group.innerHistoryIndex = i;

                group.latestHistoryIndex = math.max(group.latestHistoryIndex, i);
                group.stitchIndex = math.max(group.stitchIndex, entry.stitchIndex);
                byte stitchState = GetPortalStitchState(entry);
                group.hasActive |= entry.isActive != 0 || stitchState == (byte)NeedlePortalStitchState.Current;
                group.hasDone |= stitchState == (byte)NeedlePortalStitchState.Done;
                group.hasHist |= stitchState == (byte)NeedlePortalStitchState.Hist;
                group.hasLine |= entry.stitchIndex > 0;

                if (groupIndex >= 0)
                    groups[groupIndex] = group;
                else
                    groups.Add(group);
            }
            return groups;
        }

        private byte GetPortalStitchState(NeedlePortalHistoryEntry entry)
        {
            if (entry.stitchState != 0)
                return entry.stitchState;
            if (entry.portal.stitchState != 0)
                return entry.portal.stitchState;
            return entry.isActive != 0 ? (byte)NeedlePortalStitchState.Current : (byte)NeedlePortalStitchState.Hist;
        }

        private int GetNextSutureLineIndex()
        {
            int maxIndex = 0;
            for (int i = 0; i < _needlePortalHistory.Count; i++)
                maxIndex = math.max(maxIndex, _needlePortalHistory[i].stitchIndex);
            if (_sutureLineConstraints.IsCreated)
            {
                for (int i = 0; i < _sutureLineConstraints.Length; i++)
                    maxIndex = math.max(maxIndex, _sutureLineConstraints[i].lineIndex);
            }
            return maxIndex + 1;
        }

        private bool TryGetPortalFromPass(SutureHistoryPassGroup passGroup, byte wallLayer, out NeedlePortalInfo portal)
        {
            portal = default;
            int historyIndex = wallLayer == (byte)NeedlePortalWallLayer.Outer ? passGroup.outerHistoryIndex : passGroup.innerHistoryIndex;
            if (historyIndex < 0 || historyIndex >= _needlePortalHistory.Count)
                return false;

            NeedlePortalHistoryEntry entry = _needlePortalHistory[historyIndex];
            if (entry.portal.enabled == 0 || entry.portal.wallLayer != wallLayer)
                return false;

            portal = entry.portal;
            portal.stitchState = entry.stitchState;
            return true;
        }

        private bool TrySampleSutureBoundary(float3 worldPoint, out SutureBoundarySample sample)
        {
            sample = default;
            if (!_hasSutureBoundaryGuides)
                return false;

            bool hasA = TryClosestPointOnSutureBoundary(worldPoint, _sutureSideAOuterBoundary, _sutureSideAInnerBoundary, out float3 closestA, out float sqrA);
            bool hasB = TryClosestPointOnSutureBoundary(worldPoint, _sutureSideBOuterBoundary, _sutureSideBInnerBoundary, out float3 closestB, out float sqrB);
            if (!hasA && !hasB)
                return false;

            bool chooseA = hasA && (!hasB || sqrA <= sqrB);
            sample.valid = true;
            sample.side = (byte)(chooseA ? 1 : 2);
            sample.distanceToSideA = hasA ? math.sqrt(math.max(0f, sqrA)) : float.PositiveInfinity;
            sample.distanceToSideB = hasB ? math.sqrt(math.max(0f, sqrB)) : float.PositiveInfinity;
            sample.distanceToSide = chooseA ? sample.distanceToSideA : sample.distanceToSideB;
            sample.closestPointOnSideA = closestA;
            sample.closestPointOnSideB = closestB;
            sample.closestPointOnSide = chooseA ? closestA : closestB;
            sample.openGap = hasA && hasB
                ? math.distance(closestA, closestB)
                : math.distance(_sutureSideACenter, _sutureSideBCenter);
            return true;
        }

        private static bool TryClosestPointOnSutureBoundary(
            float3 worldPoint,
            List<float3> outerBoundary,
            List<float3> innerBoundary,
            out float3 closestPoint,
            out float closestSqrDistance)
        {
            closestPoint = float3.zero;
            closestSqrDistance = float.PositiveInfinity;
            bool found = false;
            if (TryClosestPointOnPolyline(worldPoint, outerBoundary, out float3 outerClosest, out float outerSqr))
            {
                closestPoint = outerClosest;
                closestSqrDistance = outerSqr;
                found = true;
            }
            if (TryClosestPointOnPolyline(worldPoint, innerBoundary, out float3 innerClosest, out float innerSqr) && innerSqr < closestSqrDistance)
            {
                closestPoint = innerClosest;
                closestSqrDistance = innerSqr;
                found = true;
            }
            return found;
        }

        private static bool TryClosestPointOnPolyline(float3 worldPoint, List<float3> points, out float3 closestPoint, out float closestSqrDistance)
        {
            closestPoint = float3.zero;
            closestSqrDistance = float.PositiveInfinity;
            if (points == null || points.Count == 0)
                return false;

            if (points.Count == 1)
            {
                closestPoint = points[0];
                closestSqrDistance = math.lengthsq(worldPoint - closestPoint);
                return true;
            }

            for (int i = 0; i + 1 < points.Count; i++)
            {
                float3 a = points[i];
                float3 b = points[i + 1];
                float3 ab = b - a;
                float denom = math.lengthsq(ab);
                float3 candidate = denom > 1e-12f
                    ? a + ab * math.saturate(math.dot(worldPoint - a, ab) / denom)
                    : a;
                float sqr = math.lengthsq(worldPoint - candidate);
                if (sqr < closestSqrDistance)
                {
                    closestSqrDistance = sqr;
                    closestPoint = candidate;
                }
            }

            return closestSqrDistance < float.PositiveInfinity;
        }

        private bool TryAddWholeWallSutureLineConstraint(
            SutureHistoryPassGroup passA,
            SutureHistoryPassGroup passB,
            int lineIndex,
            bool strongPullLine,
            bool autoManaged,
            out SutureLineConstraintInfo constraint)
        {
            constraint = default;
            if (!TryGetPortalFromPass(passA, (byte)NeedlePortalWallLayer.Outer, out NeedlePortalInfo outerA)
                || !TryGetPortalFromPass(passA, (byte)NeedlePortalWallLayer.Inner, out NeedlePortalInfo innerA)
                || !TryGetPortalFromPass(passB, (byte)NeedlePortalWallLayer.Outer, out NeedlePortalInfo outerB)
                || !TryGetPortalFromPass(passB, (byte)NeedlePortalWallLayer.Inner, out NeedlePortalInfo innerB))
                return false;
            if (HasSutureLineConstraint(passA.passIndex, passB.passIndex, outerA.colliderEntityId))
                return false;

            outerA.stitchState = (byte)NeedlePortalStitchState.Hist;
            innerA.stitchState = (byte)NeedlePortalStitchState.Hist;
            outerB.stitchState = (byte)NeedlePortalStitchState.Hist;
            innerB.stitchState = (byte)NeedlePortalStitchState.Hist;

            float localThickness = EstimateSutureLineWholeWallThickness(outerA, innerA, outerB, innerB);
            float localAvgEdge = EstimateSutureLineLocalAverageEdgeLength(outerA, innerA, outerB, innerB);
            float3 endpointCenterA = EstimateWholeWallEndpointPoint(outerA, innerA);
            float3 endpointCenterB = EstimateWholeWallEndpointPoint(outerB, innerB);
            TrySampleSutureBoundary(endpointCenterA, out SutureBoundarySample boundaryA);
            TrySampleSutureBoundary(endpointCenterB, out SutureBoundarySample boundaryB);

            int patchStartA = -1;
            int patchCountA = 0;
            int patchStartB = -1;
            int patchCountB = 0;
            int patchOuterCountA = 0;
            int patchInnerCountA = 0;
            int patchOuterCountB = 0;
            int patchInnerCountB = 0;
            int closureBandCountA = 0;
            int closureBandCountB = 0;
            int contactTriStartA = -1;
            int contactTriCountA = 0;
            int virtualSidewallTriCountA = 0;
            int contactTriStartB = -1;
            int contactTriCountB = 0;
            int virtualSidewallTriCountB = 0;
            if (_setting != null && _setting.enableSutureLinePatchPull)
            {
                patchStartA = AppendWholeWallSutureLinePatchVertices(outerA, innerA, boundaryA, localThickness, localAvgEdge, out patchCountA, out patchOuterCountA, out patchInnerCountA, out closureBandCountA);
                patchStartB = AppendWholeWallSutureLinePatchVertices(outerB, innerB, boundaryB, localThickness, localAvgEdge, out patchCountB, out patchOuterCountB, out patchInnerCountB, out closureBandCountB);
                if (_setting.enableSutureLineSelfContact || _setting.enableSutureLineLocalTriangleContact)
                {
                    contactTriStartA = AppendSutureLineContactTriangles(patchStartA, patchCountA, out contactTriCountA, out virtualSidewallTriCountA);
                    contactTriStartB = AppendSutureLineContactTriangles(patchStartB, patchCountB, out contactTriCountB, out virtualSidewallTriCountB);
                }
            }

            float3 pointA = ReadSutureLineConstrainedPoint(outerA, patchStartA, patchCountA);
            float3 pointB = ReadSutureLineConstrainedPoint(outerB, patchStartB, patchCountB);
            float actualDistance = math.distance(pointA, pointB);
            float minRest = _setting != null ? math.max(0f, _setting.sutureLineMinRestDistance) : 0.006f;
            float contactGap = _setting != null ? math.max(0f, _setting.sutureLineContactGap) : 0.006f;
            float closureRest = _setting != null ? math.max(0f, _setting.sutureLineClosureTargetDistance) : minRest;
            float targetRest = math.max(math.max(closureRest, contactGap), minRest);
            byte adaptiveTargetUsed = 0;
            float biteDistanceA = 0f;
            float biteDistanceB = 0f;
            float localOpenGap = 0f;
            if (_setting != null && _setting.enableSutureLineAdaptiveClosureTarget &&
                TryComputeAdaptiveSutureLineTargetRest(endpointCenterA, endpointCenterB, actualDistance, minRest, contactGap, closureRest, localThickness, localAvgEdge,
                    out float adaptiveTargetRest, out biteDistanceA, out biteDistanceB, out localOpenGap))
            {
                targetRest = adaptiveTargetRest;
                adaptiveTargetUsed = 1;
            }
            if (strongPullLine)
            {
                targetRest = math.min(
                    targetRest,
                    ComputeStrongSutureLineTargetRest(actualDistance, minRest, contactGap, localThickness, localAvgEdge));
                adaptiveTargetUsed = 1;
            }
            targetRest = math.min(actualDistance, targetRest);
            float3 closureDirection = math.normalizesafe(pointB - pointA, math.normalizesafe(endpointCenterB - endpointCenterA, new float3(1f, 0f, 0f)));
            float3 contactNormalHint = BuildSutureLineContactNormalHint(outerA, outerB, closureDirection);
            int contactMissReason = DetermineSutureLineContactMissReason(boundaryA, boundaryB, patchCountA, patchCountB, closureBandCountA, closureBandCountB, contactTriCountA, contactTriCountB);

            constraint = new SutureLineConstraintInfo()
            {
                portalA = outerA,
                portalB = outerB,
                colliderEntityId = outerA.colliderEntityId,
                lineIndex = lineIndex,
                passIndexA = passA.passIndex,
                passIndexB = passB.passIndex,
                wallLayer = (byte)NeedlePortalWallLayer.Unknown,
                enabled = 1,
                weakHoldLogged = 0,
                tensionOnlyActive = 0,
                lambdaResetLastFrame = 1,
                adaptiveTargetUsed = adaptiveTargetUsed,
                strongPullLine = (byte)(strongPullLine ? 1 : 0),
                contactState = strongPullLine ? SutureLineContactStateLatched : SutureLineContactStateApproach,
                contactLatched = (byte)(strongPullLine ? 1 : 0),
                restFrozen = 0,
                localWallContactActive = 0,
                compressionFoldActive = 0,
                foldDirValid = 0,
                compressionFoldCandidateActive = 0,
                foldPatchFallbackSuppressed = 0,
                closureBandSlideActive = 0,
                closureBandSlideDirValid = 0,
                reserved0 = SutureLineConstraintInfo.LifecycleActive,
                workflowReady = 0,
                workflowReadyFrames = 0,
                workflowAdvanceReady = 0,
                workflowAdvanceReadyFrames = 0,
                workflowReleased = 0,
                autoManaged = (byte)(autoManaged ? 1 : 0),
                completedWithoutRealSidewallContact = 0,
                progressLogCounter = 0,
                lastLocalContactPairs = 0,
                lastContactTriPairs = 0,
                lastSelfContactTriPairs = 0,
                foldColumnCount = 0,
                foldReason = SutureFoldReasonNone,
                lastSelfContactVTPairs = 0,
                lastSelfContactEEPairs = 0,
                lastPersistentContactPairs = 0,
                lastSweptContactCandidates = 0,
                persistentContactFramesRemaining = 0,
                compressionFoldHoldFramesRemaining = 0,
                closureBandSlideColumnCount = 0,
                lastSelfContactSolveTriPairs = 0,
                lastSelfContactSolveVTPairs = 0,
                lastSelfContactSolveEEPairs = 0,
                lastSelfContactCcdHit = 0,
                lastSelfContactMaxPushClamped = 0,
                lastSelfContactSolveCcdHit = 0,
                lastSelfContactSolveForced = 0,
                contactMissReason = contactMissReason,
                currentRestDistance = actualDistance,
                targetRestDistance = targetRest,
                targetDelta = math.max(0f, actualDistance - targetRest),
                lastActualDistance = actualDistance,
                lastContactPush = 0f,
                lastLocalContactPush = 0f,
                lastTriangleContactPush = 0f,
                lastSelfContactNormalPush = 0f,
                lastSelfContactTangentPush = 0f,
                lastSelfContactSolveNormalPush = 0f,
                lastSelfContactSolveMinDistance = float.PositiveInfinity,
                lastSelfContactCcdAdvancePush = 0f,
                rawCompressionFoldPush = 0f,
                lastCompressionFoldPush = 0f,
                rawClosureBandSlidePush = 0f,
                lastClosureBandSlidePush = 0f,
                selfContactThickness = ComputeSutureLineSelfContactThickness(localThickness, localAvgEdge, contactGap),
                minContactDistance = float.PositiveInfinity,
                minSelfContactVTDistance = float.PositiveInfinity,
                minSelfContactEEDistance = float.PositiveInfinity,
                biteDistanceA = biteDistanceA,
                biteDistanceB = biteDistanceB,
                localWallThickness = localThickness,
                localAverageEdgeLength = localAvgEdge,
                localOpenGap = localOpenGap,
                contactNormalHint = contactNormalHint,
                closureDirection = closureDirection,
                foldDir = float3.zero,
                closureBandSlideDir = float3.zero,
                patchStartA = patchStartA,
                patchCountA = patchCountA,
                patchStartB = patchStartB,
                patchCountB = patchCountB,
                patchOuterCountA = patchOuterCountA,
                patchInnerCountA = patchInnerCountA,
                patchOuterCountB = patchOuterCountB,
                patchInnerCountB = patchInnerCountB,
                closureBandCountA = closureBandCountA,
                closureBandCountB = closureBandCountB,
                contactTriStartA = contactTriStartA,
                contactTriCountA = contactTriCountA,
                virtualSidewallTriCountA = virtualSidewallTriCountA,
                contactTriStartB = contactTriStartB,
                contactTriCountB = contactTriCountB,
                virtualSidewallTriCountB = virtualSidewallTriCountB,
                lambda = 0f,
            };
            _sutureLineConstraints.Add(constraint);
            UpsertSutureLineDisplayInfo(constraint, pointA, pointB);
            return true;
        }

        private static float ComputeStrongSutureLineTargetRest(float actualDistance, float minRest, float contactGap, float localThickness, float localAvgEdge)
        {
            float floor = math.max(0.001f, math.min(0.003f, math.max(minRest * 0.25f, contactGap * 0.25f)));
            float thicknessTarget = localThickness > 1e-6f ? localThickness * 0.35f : float.PositiveInfinity;
            float edgeTarget = localAvgEdge > 1e-6f ? localAvgEdge * 0.75f : float.PositiveInfinity;
            float contactTarget = contactGap > 1e-6f ? contactGap * 0.45f : float.PositiveInfinity;
            float minTarget = minRest > 1e-6f ? minRest * 0.45f : float.PositiveInfinity;
            float target = math.min(math.min(thicknessTarget, edgeTarget), math.min(contactTarget, minTarget));
            if (!math.isfinite(target))
                target = floor;
            target = math.max(floor, target);
            return math.min(actualDistance, target);
        }

        private static float3 BuildSutureLineContactNormalHint(NeedlePortalInfo portalA, NeedlePortalInfo portalB, float3 closureDirection)
        {
            float3 nA = math.normalizesafe(portalA.entryNormal, float3.zero);
            float3 nB = math.normalizesafe(portalB.entryNormal, float3.zero);
            float3 normal = nA + nB;
            if (math.lengthsq(normal) < 1e-8f)
                normal = math.lengthsq(nA) > 1e-8f ? nA : nB;
            normal = ProjectSutureLineVectorAwayFromAxis(normal, closureDirection);
            if (math.lengthsq(normal) < 1e-8f)
                normal = BuildPerpendicularSutureLineVector(closureDirection);
            return math.normalizesafe(normal, new float3(0f, 1f, 0f));
        }

        private static float3 ProjectSutureLineVectorAwayFromAxis(float3 vector, float3 axis)
        {
            float3 n = math.normalizesafe(axis, float3.zero);
            if (math.lengthsq(vector) < 1e-8f || math.lengthsq(n) < 1e-8f)
                return vector;
            return vector - n * math.dot(vector, n);
        }

        private static float3 BuildPerpendicularSutureLineVector(float3 axis)
        {
            float3 n = math.normalizesafe(axis, new float3(1f, 0f, 0f));
            float3 candidate = math.cross(n, new float3(0f, 1f, 0f));
            if (math.lengthsq(candidate) < 1e-8f)
                candidate = math.cross(n, new float3(0f, 0f, 1f));
            return math.normalizesafe(candidate, new float3(0f, 1f, 0f));
        }

        private int DetermineSutureLineContactMissReason(
            SutureBoundarySample boundaryA,
            SutureBoundarySample boundaryB,
            int patchCountA,
            int patchCountB,
            int closureBandCountA,
            int closureBandCountB,
            int contactTriCountA,
            int contactTriCountB)
        {
            if (!boundaryA.valid || !boundaryB.valid)
                return SutureContactMissBoundaryGuideInvalid;
            if (patchCountA < 3 || patchCountB < 3)
                return SutureContactMissPatchVertexInsufficient;
            if (closureBandCountA <= 0 || closureBandCountB <= 0)
                return SutureContactMissBiteBandEmpty;
            if (contactTriCountA <= 0 || contactTriCountB <= 0)
                return SutureContactMissTriangleRoiEmpty;
            return SutureContactMissNone;
        }

        private bool TryComputeAdaptiveSutureLineTargetRest(
            float3 pointA,
            float3 pointB,
            float actualDistance,
            float minRest,
            float contactGap,
            float closureRest,
            float localThickness,
            float localAvgEdge,
            out float targetRest,
            out float biteDistanceA,
            out float biteDistanceB,
            out float openGap)
        {
            targetRest = math.max(math.max(closureRest, contactGap), minRest);
            biteDistanceA = 0f;
            biteDistanceB = 0f;
            openGap = 0f;
            if (!TrySampleSutureBoundary(pointA, out SutureBoundarySample sampleA) ||
                !TrySampleSutureBoundary(pointB, out SutureBoundarySample sampleB))
                return false;

            biteDistanceA = math.max(0f, sampleA.distanceToSide);
            biteDistanceB = math.max(0f, sampleB.distanceToSide);
            openGap = math.max(0f, (sampleA.openGap + sampleB.openGap) * 0.5f);

            float thicknessGap = localThickness * (_setting != null ? math.max(0f, _setting.sutureLineAdaptiveThicknessScale) : 0.75f);
            float edgeGap = localAvgEdge * (_setting != null ? math.max(0f, _setting.sutureLineAdaptiveEdgeScale) : 0.35f);
            float materialGap = math.max(thicknessGap, edgeGap);
            materialGap = math.max(materialGap, minRest * 0.25f);
            if (localThickness > 1e-6f)
            {
                float materialCap = math.max(contactGap, localThickness * 0.60f);
                materialGap = math.min(materialGap, materialCap);
            }

            // Adaptive target is the geometric closure floor, not an intermediate
            // "bite depth" span. Approach continues tightening toward this floor
            // until local ROI contact latches and contact projection takes over.
            targetRest = math.max(math.max(closureRest, contactGap), materialGap);
            targetRest = math.min(actualDistance, targetRest);
            return targetRest > 1e-6f;
        }

        private float ComputeSutureLineSelfContactThickness(float localThickness, float localAvgEdge, float contactGap)
        {
            float thicknessScale = _setting != null ? math.max(0f, _setting.sutureLineSelfContactThicknessScale) : 0.45f;
            float edgeScale = _setting != null ? math.max(0f, _setting.sutureLineSelfContactEdgeScale) : 0.25f;
            float thicknessTerm = math.max(0f, localThickness) * thicknessScale;
            float edgeTerm = math.max(0f, localAvgEdge) * edgeScale;
            float thickness = math.max(math.max(math.max(0f, contactGap), thicknessTerm), edgeTerm);
            if (localThickness > 1e-6f)
            {
                float wallCap = math.max(contactGap, localThickness * 0.55f);
                thickness = math.min(thickness, wallCap);
            }
            return thickness;
        }

        private float EstimateSutureLineWholeWallThickness(
            NeedlePortalInfo outerA,
            NeedlePortalInfo innerA,
            NeedlePortalInfo outerB,
            NeedlePortalInfo innerB)
        {
            float thicknessA = math.distance(ReadPortalPointFromPredictedPositions(outerA), ReadPortalPointFromPredictedPositions(innerA));
            float thicknessB = math.distance(ReadPortalPointFromPredictedPositions(outerB), ReadPortalPointFromPredictedPositions(innerB));
            if (thicknessA > 1e-6f && thicknessB > 1e-6f)
                return (thicknessA + thicknessB) * 0.5f;
            return math.max(thicknessA, thicknessB);
        }

        private float3 EstimateWholeWallEndpointPoint(NeedlePortalInfo outerPortal, NeedlePortalInfo innerPortal)
        {
            return (ReadPortalPointFromPredictedPositions(outerPortal) + ReadPortalPointFromPredictedPositions(innerPortal)) * 0.5f;
        }

        private float EstimateSutureLineLocalAverageEdgeLength(
            NeedlePortalInfo outerA,
            NeedlePortalInfo innerA,
            NeedlePortalInfo outerB,
            NeedlePortalInfo innerB)
        {
            float sum = 0f;
            int count = 0;
            AccumulatePortalTriangleEdges(outerA, ref sum, ref count);
            AccumulatePortalTriangleEdges(innerA, ref sum, ref count);
            AccumulatePortalTriangleEdges(outerB, ref sum, ref count);
            AccumulatePortalTriangleEdges(innerB, ref sum, ref count);
            return count > 0 ? sum / count : 0f;
        }

        private void AccumulatePortalTriangleEdges(NeedlePortalInfo portal, ref float sum, ref int count)
        {
            if (!IsValidSutureLineContactVertex(portal.v0) ||
                !IsValidSutureLineContactVertex(portal.v1) ||
                !IsValidSutureLineContactVertex(portal.v2))
                return;

            sum += math.distance(_predictPositions[portal.v0], _predictPositions[portal.v1]);
            sum += math.distance(_predictPositions[portal.v1], _predictPositions[portal.v2]);
            sum += math.distance(_predictPositions[portal.v2], _predictPositions[portal.v0]);
            count += 3;
        }

        private bool HasSutureLineConstraint(int passA, int passB, int colliderEntityId)
        {
            if (!_sutureLineConstraints.IsCreated)
                return false;

            int minPass = math.min(passA, passB);
            int maxPass = math.max(passA, passB);
            for (int i = 0; i < _sutureLineConstraints.Length; i++)
            {
                SutureLineConstraintInfo c = _sutureLineConstraints[i];
                if (c.enabled == 0 || c.colliderEntityId != colliderEntityId)
                    continue;
                if (math.min(c.passIndexA, c.passIndexB) == minPass && math.max(c.passIndexA, c.passIndexB) == maxPass)
                    return true;
            }
            return false;
        }

        private bool TryGetSutureLinePairStatus(int passA, int passB, int colliderEntityId, out int lineIndex)
        {
            lineIndex = 0;
            if (!_sutureLineConstraints.IsCreated)
                return false;

            int minPass = math.min(passA, passB);
            int maxPass = math.max(passA, passB);
            for (int i = 0; i < _sutureLineConstraints.Length; i++)
            {
                SutureLineConstraintInfo c = _sutureLineConstraints[i];
                if (c.enabled == 0 || c.colliderEntityId != colliderEntityId)
                    continue;
                if (math.min(c.passIndexA, c.passIndexB) != minPass || math.max(c.passIndexA, c.passIndexB) != maxPass)
                    continue;

                lineIndex = math.max(lineIndex, c.lineIndex);
                return true;
            }

            return false;
        }

        public bool TryReleaseAutoSutureWorkflowForPassPair(
            int colliderEntityId,
            int passA,
            int passB,
            out int lineIndex,
            out string reason)
        {
            lineIndex = 0;
            reason = "ready";
            if (!_sutureLineConstraints.IsCreated)
            {
                reason = "no-constraints";
                return false;
            }
            if (colliderEntityId == 0 || passA <= 0 || passB <= 0 || passA == passB)
            {
                reason = "invalid-request";
                return false;
            }

            int minPass = math.min(passA, passB);
            int maxPass = math.max(passA, passB);
            for (int i = 0; i < _sutureLineConstraints.Length; i++)
            {
                SutureLineConstraintInfo c = _sutureLineConstraints[i];
                if (c.enabled == 0 || c.colliderEntityId != colliderEntityId)
                    continue;
                if (math.min(c.passIndexA, c.passIndexB) != minPass ||
                    math.max(c.passIndexA, c.passIndexB) != maxPass)
                    continue;

                lineIndex = c.lineIndex;
                bool physicallySettled = c.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold;
                if (!physicallySettled && c.workflowReleased == 0 && c.workflowAdvanceReady == 0)
                {
                    reason = $"not-advance-ready frames={c.workflowAdvanceReadyFrames}";
                    return false;
                }

                float3 pointA = ReadSutureLineConstrainedPoint(c.portalA, c.patchStartA, c.patchCountA);
                float3 pointB = ReadSutureLineConstrainedPoint(c.portalB, c.patchStartB, c.patchCountB);
                float actualDistance = math.distance(pointA, pointB);
                float targetRest = math.max(0f, c.targetRestDistance);
                ApplyAutoSutureWorkflowRelease(ref c, actualDistance);
                UpsertSutureLineDisplayInfo(c, pointA, pointB);
                _sutureLineConstraints[i] = c;
                MarkSuturePassDone(c.passIndexA, colliderEntityId, c.lineIndex);
                MarkSuturePassDone(c.passIndexB, colliderEntityId, c.lineIndex);
                Debug.Log(
                    $"[Suture][AutoWorkflowReleased] lineIndex={c.lineIndex} collider={colliderEntityId} " +
                    $"passA={c.passIndexA} passB={c.passIndexB} physicallySettled={(physicallySettled ? 1 : 0)} " +
                    $"contactLatched={c.contactLatched} restFrozen={c.restFrozen} " +
                    $"actualDistance={actualDistance:F6} targetRest={targetRest:F6} " +
                    $"advanceFrames={c.workflowAdvanceReadyFrames} completionHold=1 backgroundSolve=0 " +
                    $"noRealSidewallContact={c.completedWithoutRealSidewallContact}");
                return true;
            }

            reason = "line-not-found";
            return false;
        }

        internal static void ApplyAutoSutureWorkflowRelease(
            ref SutureLineConstraintInfo c,
            float actualDistance)
        {
            bool alreadyPhysicallySettled = c.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold;
            if (!alreadyPhysicallySettled)
            {
                c.completedWithoutRealSidewallContact = (byte)(c.workflowReady == 0 ? 1 : 0);
            }
            c.workflowReleased = 1;
            c.workflowAdvanceReady = 1;
            c.workflowAdvanceReadyFrames = (byte)math.max(
                (int)c.workflowAdvanceReadyFrames,
                (int)SutureLineWorkflowReadyHoldFrames);
            c.reserved0 = SutureLineConstraintInfo.LifecycleSettledHold;
            c.lambda = 0f;
            c.lambdaResetLastFrame = 1;
            ApplySutureLineSettledHold(
                ref c,
                actualDistance,
                math.max(0f, c.targetRestDistance));
        }

        private int AppendWholeWallSutureLinePatchVertices(
            NeedlePortalInfo outerPortal,
            NeedlePortalInfo innerPortal,
            SutureBoundarySample boundarySample,
            float localThickness,
            float localAvgEdge,
            out int patchCount,
            out int outerCount,
            out int innerCount,
            out int closureBandCount)
        {
            patchCount = 0;
            outerCount = 0;
            innerCount = 0;
            closureBandCount = 0;
            if (!_sutureLinePatchVertices.IsCreated || !_meshModifier.vertices.IsCreated || !_meshModifier.indices.IsCreated)
                return -1;

            int start = _sutureLinePatchVertices.Length;
            int baseMaxCount = _setting != null ? math.max(6, _setting.sutureLinePatchMaxVertexCount) : 32;
            int closureBandMaxExtra = _setting != null && _setting.enableSutureClosureBand
                ? math.max(0, _setting.sutureClosureBandMaxExtraVertexCount)
                : 0;
            int maxCount = math.max(6, baseMaxCount + closureBandMaxExtra);
            int perLayerMax = math.max(3, maxCount / 2);
            float radius = _setting != null ? math.max(0f, _setting.sutureLinePatchRadius) : 0.024f;
            float ringWeight = _setting != null ? math.saturate(_setting.sutureLinePatchRingWeight) : 0.35f;
            int ringDepth = _setting != null ? math.clamp(_setting.sutureLinePatchRingDepth, 1, 2) : 2;
            float outerWeight = _setting != null ? math.saturate(_setting.sutureLineOuterPatchWeight) : 0.55f;
            float innerWeight = _setting != null ? math.saturate(_setting.sutureLineInnerPatchWeight) : 0.45f;

            var combined = new List<SutureLinePatchVertexInfo>(maxCount);
            var outerPatch = new List<SutureLinePatchVertexInfo>(perLayerMax);
            var innerPatch = new List<SutureLinePatchVertexInfo>(perLayerMax);

            AddSutureLinePortalSubpatchVertices(outerPatch, outerPortal, NeedlePortalWallLayer.Outer, radius, ringWeight, ringDepth, perLayerMax);
            AddSutureLinePortalSubpatchVertices(innerPatch, innerPortal, NeedlePortalWallLayer.Inner, radius, ringWeight, ringDepth, perLayerMax);

            for (int i = 0; i < outerPatch.Count; i++)
                AddOrRaiseSutureLinePatchVertex(combined, outerPatch[i].vertexIndex, outerPatch[i].weight * math.max(0.05f, outerWeight), maxCount);
            outerCount = combined.Count;

            int beforeInner = combined.Count;
            for (int i = 0; i < innerPatch.Count; i++)
                AddOrRaiseSutureLinePatchVertex(combined, innerPatch[i].vertexIndex, innerPatch[i].weight * math.max(0.05f, innerWeight), maxCount);
            innerCount = math.max(0, combined.Count - beforeInner);

            if (combined.Count < 3)
            {
                AddSutureLinePortalPrimaryVertices(combined, outerPortal, NeedlePortalWallLayer.Outer, math.max(0.05f, outerWeight), maxCount);
                AddSutureLinePortalPrimaryVertices(combined, innerPortal, NeedlePortalWallLayer.Inner, math.max(0.05f, innerWeight), maxCount);
                outerCount = math.max(outerCount, math.min(3, combined.Count));
                innerCount = math.max(0, combined.Count - outerCount);
            }

            if (_setting != null && _setting.enableSutureClosureBand && boundarySample.valid && closureBandMaxExtra > 0)
            {
                float bandRadius = math.max(radius, math.max(localThickness, localAvgEdge) * math.max(0.1f, _setting.sutureClosureBandRadiusScale));
                float bandWeight = math.saturate(_setting.sutureClosureBandWeight);
                float3 endpointCenter = EstimateWholeWallEndpointPoint(outerPortal, innerPortal);
                AddSutureClosureBandVertices(combined, outerPortal, NeedlePortalWallLayer.Outer, endpointCenter, boundarySample.closestPointOnSide, bandRadius, bandWeight * math.max(0.05f, outerWeight), maxCount);
                AddSutureClosureBandVertices(combined, innerPortal, NeedlePortalWallLayer.Inner, endpointCenter, boundarySample.closestPointOnSide, bandRadius, bandWeight * math.max(0.05f, innerWeight), maxCount);
                closureBandCount = CountSutureClosureBandVertices(combined);
            }

            for (int i = 0; i < combined.Count; i++)
                _sutureLinePatchVertices.Add(combined[i]);

            patchCount = combined.Count;
            return patchCount > 0 ? start : -1;
        }

        private static int CountSutureClosureBandVertices(List<SutureLinePatchVertexInfo> patch)
        {
            if (patch == null)
                return 0;

            int count = 0;
            for (int i = 0; i < patch.Count; i++)
            {
                if ((patch[i].flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0)
                    count++;
            }
            return count;
        }

        private int AppendSutureLineContactTriangles(int patchStart, int patchCount, out int triangleCount, out int virtualSidewallTriangleCount)
        {
            triangleCount = 0;
            virtualSidewallTriangleCount = 0;
            if (!_sutureLineContactTriangles.IsCreated || !_sutureLinePatchVertices.IsCreated || !_meshModifier.indices.IsCreated)
                return -1;
            if (patchStart < 0 || patchCount <= 0 || patchStart + patchCount > _sutureLinePatchVertices.Length)
                return -1;

            int start = _sutureLineContactTriangles.Length;
            int expectedTriangles = _setting != null ? math.max(8, _setting.sutureLineLocalContactMaxTrianglesPerEndpoint) : 96;
            var patchInfos = new Dictionary<int, SutureLinePatchVertexInfo>(patchCount);
            var patchVertexSet = new HashSet<int>();
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = _sutureLinePatchVertices[patchStart + i];
                if (pv.vertexIndex < 0)
                    continue;
                patchVertexSet.Add(pv.vertexIndex);
                if (patchInfos.TryGetValue(pv.vertexIndex, out SutureLinePatchVertexInfo prev))
                {
                    prev.weight = math.max(prev.weight, pv.weight);
                    prev.flags = (byte)(prev.flags | pv.flags);
                    if ((pv.flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0)
                        prev.bandT = pv.bandT;
                    patchInfos[pv.vertexIndex] = prev;
                }
                else
                {
                    patchInfos.Add(pv.vertexIndex, pv);
                }
            }

            if (patchInfos.Count <= 0)
                return -1;

            var candidates = new List<SutureLineContactTriangleInfo>(math.max(expectedTriangles, patchCount * 6));
            var emittedTriangleKeys = new HashSet<ulong>();
            var contactTriangleVertexSet = new HashSet<int>();
            int triCount = _meshModifier.indices.Length / 3;
            for (int tri = 0; tri < triCount; tri++)
            {
                int baseIndex = tri * 3;
                int v0 = _meshModifier.indices[baseIndex];
                int v1 = _meshModifier.indices[baseIndex + 1];
                int v2 = _meshModifier.indices[baseIndex + 2];

                int hits = 0;
                int closureBandHits = 0;
                int foldColumnHits = 0;
                float weightSum = 0f;
                float bandTSum = 0f;
                float bandWeightSum = 0f;
                byte flags = 0;
                AccumulateSutureLineContactTriangleVertex(patchInfos, v0, ref hits, ref closureBandHits, ref foldColumnHits, ref weightSum, ref bandTSum, ref bandWeightSum, ref flags);
                AccumulateSutureLineContactTriangleVertex(patchInfos, v1, ref hits, ref closureBandHits, ref foldColumnHits, ref weightSum, ref bandTSum, ref bandWeightSum, ref flags);
                AccumulateSutureLineContactTriangleVertex(patchInfos, v2, ref hits, ref closureBandHits, ref foldColumnHits, ref weightSum, ref bandTSum, ref bandWeightSum, ref flags);
                if (hits <= 0)
                    continue;

                var contactTriangle = new SutureLineContactTriangleInfo
                {
                    v0 = v0,
                    v1 = v1,
                    v2 = v2,
                    weight = math.saturate(weightSum / hits),
                    bandT = bandWeightSum > 1e-6f ? math.saturate(bandTSum / bandWeightSum) : 0f,
                    flags = flags,
                    closureBandHits = (byte)math.min(closureBandHits, byte.MaxValue),
                };
                if (AddSutureLineContactTriangleCandidate(candidates, emittedTriangleKeys, contactTriangle))
                {
                    contactTriangleVertexSet.Add(v0);
                    contactTriangleVertexSet.Add(v1);
                    contactTriangleVertexSet.Add(v2);
                }
            }

            AppendVirtualSidewallContactTriangleCandidates(patchVertexSet, contactTriangleVertexSet, candidates, emittedTriangleKeys, ref virtualSidewallTriangleCount);

            candidates.Sort(CompareSutureLineContactTrianglePriority);
            int addCount = candidates.Count;
            for (int i = 0; i < addCount; i++)
            {
                _sutureLineContactTriangles.Add(candidates[i]);
                triangleCount++;
            }

            return triangleCount > 0 ? start : -1;
        }

        private void AppendVirtualSidewallContactTriangleCandidates(
            HashSet<int> patchVertexSet,
            HashSet<int> contactTriangleVertexSet,
            List<SutureLineContactTriangleInfo> candidates,
            HashSet<ulong> emittedTriangleKeys,
            ref int virtualSidewallTriangleCount)
        {
            if (patchVertexSet == null || patchVertexSet.Count == 0 || _sutureSidewallBoundaryEdges.Count == 0)
                return;

            for (int i = 0; i < _sutureSidewallBoundaryEdges.Count; i++)
            {
                SutureSidewallBoundaryEdgeInfo edge = _sutureSidewallBoundaryEdges[i];
                if (!IsValidVirtualSidewallBoundaryEdge(edge) ||
                    !PatchTouchesVirtualSidewallBoundaryEdge(patchVertexSet, contactTriangleVertexSet, edge))
                    continue;

                AddVirtualSidewallContactTriangle(edge.outerA, edge.outerB, edge.innerB, edge, candidates, emittedTriangleKeys, ref virtualSidewallTriangleCount);
                AddVirtualSidewallContactTriangle(edge.outerA, edge.innerB, edge.innerA, edge, candidates, emittedTriangleKeys, ref virtualSidewallTriangleCount);
            }
        }

        private void AddVirtualSidewallContactTriangle(
            int v0,
            int v1,
            int v2,
            SutureSidewallBoundaryEdgeInfo edge,
            List<SutureLineContactTriangleInfo> candidates,
            HashSet<ulong> emittedTriangleKeys,
            ref int virtualSidewallTriangleCount)
        {
            if (v0 < 0 || v1 < 0 || v2 < 0 || v0 == v1 || v1 == v2 || v2 == v0)
                return;

            int vertexCount = _predictPositions.IsCreated ? _predictPositions.Length : _meshModifier.vertexCount;
            if (v0 >= vertexCount || v1 >= vertexCount || v2 >= vertexCount)
                return;

            if (AddSutureLineContactTriangleCandidate(
                candidates,
                emittedTriangleKeys,
                new SutureLineContactTriangleInfo
                {
                    v0 = v0,
                    v1 = v1,
                    v2 = v2,
                    sourceEdgeId = edge.sourceEdgeId,
                    weight = 1f,
                    bandT = 0.5f,
                    flags = SutureLineContactTriangleInfo.FlagVirtualSidewall,
                    closureBandHits = 0,
                    sourceSide = edge.side,
                }))
            {
                virtualSidewallTriangleCount++;
            }
        }

        private bool IsValidVirtualSidewallBoundaryEdge(SutureSidewallBoundaryEdgeInfo edge)
        {
            if (edge.enabled == 0)
                return false;
            if (edge.outerA < 0 || edge.outerB < 0 || edge.innerA < 0 || edge.innerB < 0)
                return false;
            if (edge.outerA == edge.outerB || edge.innerA == edge.innerB)
                return false;

            int vertexCount = _predictPositions.IsCreated ? _predictPositions.Length : _meshModifier.vertexCount;
            return edge.outerA < vertexCount && edge.outerB < vertexCount &&
                   edge.innerA < vertexCount && edge.innerB < vertexCount;
        }

        private static bool PatchTouchesVirtualSidewallBoundaryEdge(HashSet<int> patchVertexSet, HashSet<int> contactTriangleVertexSet, SutureSidewallBoundaryEdgeInfo edge)
        {
            return ContainsVirtualSidewallEndpoint(patchVertexSet, edge) ||
                   ContainsVirtualSidewallEndpoint(contactTriangleVertexSet, edge);
        }

        private static bool ContainsVirtualSidewallEndpoint(HashSet<int> vertexSet, SutureSidewallBoundaryEdgeInfo edge)
        {
            return vertexSet != null &&
                   (vertexSet.Contains(edge.outerA) ||
                    vertexSet.Contains(edge.outerB) ||
                    vertexSet.Contains(edge.innerA) ||
                    vertexSet.Contains(edge.innerB));
        }

        private static bool AddSutureLineContactTriangleCandidate(
            List<SutureLineContactTriangleInfo> candidates,
            HashSet<ulong> emittedTriangleKeys,
            SutureLineContactTriangleInfo triangle)
        {
            if (triangle.v0 < 0 || triangle.v1 < 0 || triangle.v2 < 0 ||
                triangle.v0 == triangle.v1 || triangle.v1 == triangle.v2 || triangle.v2 == triangle.v0)
                return false;

            ulong key = PackSutureLineContactTriangleKey(triangle.v0, triangle.v1, triangle.v2);
            if (emittedTriangleKeys != null && !emittedTriangleKeys.Add(key))
                return false;

            candidates.Add(triangle);
            return true;
        }

        private static ulong PackSutureLineContactTriangleKey(int a, int b, int c)
        {
            if (a > b)
            {
                int t = a;
                a = b;
                b = t;
            }
            if (b > c)
            {
                int t = b;
                b = c;
                c = t;
            }
            if (a > b)
            {
                int t = a;
                a = b;
                b = t;
            }

            unchecked
            {
                uint ua = (uint)a;
                uint ub = (uint)b;
                uint uc = (uint)c;
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ ua) * 1099511628211UL;
                hash = (hash ^ ub) * 1099511628211UL;
                hash = (hash ^ uc) * 1099511628211UL;
                return hash;
            }
        }

        private static int CompareSutureLineContactTrianglePriority(SutureLineContactTriangleInfo a, SutureLineContactTriangleInfo b)
        {
            bool virtualA = IsSutureLineVirtualSidewallContactTriangle(a);
            bool virtualB = IsSutureLineVirtualSidewallContactTriangle(b);
            int virtualCompare = (virtualB ? 1 : 0).CompareTo(virtualA ? 1 : 0);
            if (virtualCompare != 0)
                return virtualCompare;

            bool closureA = a.closureBandHits > 0 && (a.flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0;
            bool closureB = b.closureBandHits > 0 && (b.flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0;
            int closureCompare = (closureB ? 1 : 0).CompareTo(closureA ? 1 : 0);
            if (closureCompare != 0)
                return closureCompare;

            float midA = math.abs(a.bandT - 0.5f);
            float midB = math.abs(b.bandT - 0.5f);
            int midCompare = midA.CompareTo(midB);
            if (midCompare != 0)
                return midCompare;

            int hitsCompare = b.closureBandHits.CompareTo(a.closureBandHits);
            if (hitsCompare != 0)
                return hitsCompare;

            return b.weight.CompareTo(a.weight);
        }

        private static void AccumulateSutureLineContactTriangleVertex(
            Dictionary<int, SutureLinePatchVertexInfo> patchInfos,
            int vertexIndex,
            ref int hits,
            ref int closureBandHits,
            ref int foldColumnHits,
            ref float weightSum,
            ref float bandTSum,
            ref float bandWeightSum,
            ref byte flags)
        {
            if (patchInfos == null || !patchInfos.TryGetValue(vertexIndex, out SutureLinePatchVertexInfo pv))
                return;

            hits++;
            float weight = math.max(0.05f, pv.weight);
            weightSum += weight;
            flags = (byte)(flags | pv.flags);
            if ((pv.flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0)
            {
                closureBandHits++;
                bandTSum += math.saturate(pv.bandT) * math.max(weight, 1e-4f);
                bandWeightSum += math.max(weight, 1e-4f);
            }
            if ((pv.flags & SutureLinePatchVertexInfo.FlagFoldColumn) != 0)
                foldColumnHits++;
        }

        private void AddSutureClosureBandVertices(
            List<SutureLinePatchVertexInfo> patch,
            NeedlePortalInfo portal,
            NeedlePortalWallLayer layer,
            float3 endpointCenter,
            float3 boundaryPoint,
            float radius,
            float baseWeight,
            int maxCount)
        {
            if (patch == null || portal.enabled == 0 || baseWeight <= 0f || radius <= 1e-6f || maxCount <= 0)
                return;

            float3 segment = boundaryPoint - endpointCenter;
            float segmentLengthSq = math.lengthsq(segment);
            if (segmentLengthSq <= 1e-10f)
                return;

            int triCount = _meshModifier.indices.Length / 3;
            for (int tri = 0; tri < triCount; tri++)
            {
                if (!IsTriangleFullyInPortalLayer(tri, layer))
                    continue;

                int baseIndex = tri * 3;
                AddSutureClosureBandVertex(patch, _meshModifier.indices[baseIndex], endpointCenter, segment, segmentLengthSq, radius, baseWeight, layer, maxCount);
                AddSutureClosureBandVertex(patch, _meshModifier.indices[baseIndex + 1], endpointCenter, segment, segmentLengthSq, radius, baseWeight, layer, maxCount);
                AddSutureClosureBandVertex(patch, _meshModifier.indices[baseIndex + 2], endpointCenter, segment, segmentLengthSq, radius, baseWeight, layer, maxCount);

                if (patch.Count >= maxCount)
                    break;
            }
        }

        private void AddSutureClosureBandVertex(
            List<SutureLinePatchVertexInfo> patch,
            int vertexIndex,
            float3 segmentStart,
            float3 segment,
            float segmentLengthSq,
            float radius,
            float baseWeight,
            NeedlePortalWallLayer layer,
            int maxCount)
        {
            if (!IsVertexInPortalLayer(vertexIndex, layer) || patch.Count >= maxCount)
                return;

            float3 p = _meshModifier.vertices[vertexIndex];
            float t = math.saturate(math.dot(p - segmentStart, segment) / segmentLengthSq);
            float3 closest = segmentStart + segment * t;
            float distance = math.distance(p, closest);
            if (distance > radius)
                return;

            float radial = math.max(0.1f, 1f - distance / radius);
            float along = math.lerp(1f, 0.55f, t);
            byte flags = SutureLinePatchVertexInfo.FlagClosureBand;
            if (t <= SutureClosureBandPortalAnchorT)
                flags |= SutureLinePatchVertexInfo.FlagPullAnchor;
            else if (t >= SutureClosureBandSeamAnchorT)
                flags |= SutureLinePatchVertexInfo.FlagSeamAnchor;
            else
                flags |= SutureLinePatchVertexInfo.FlagFoldColumn;

            float middle01 = 1f - math.abs((t - 0.5f) * 2f);
            float roleWeight = (flags & SutureLinePatchVertexInfo.FlagFoldColumn) != 0
                ? math.lerp(0.70f, 1.20f, math.saturate(middle01))
                : 1f;
            AddLayeredSutureLinePatchVertex(patch, vertexIndex, baseWeight * radial * along * roleWeight, layer, maxCount, flags, t);
        }

        private void AddSutureLinePortalSubpatchVertices(
            List<SutureLinePatchVertexInfo> patch,
            NeedlePortalInfo portal,
            NeedlePortalWallLayer layer,
            float radius,
            float ringWeight,
            int ringDepth,
            int maxCount)
        {
            if (portal.enabled == 0 || maxCount <= 0)
                return;

            AddSutureLinePortalPrimaryVertices(patch, portal, layer, 1f, maxCount);

            var frontier = new HashSet<int>();
            AddSutureLineFrontierVertex(frontier, portal.v0, layer);
            AddSutureLineFrontierVertex(frontier, portal.v1, layer);
            AddSutureLineFrontierVertex(frontier, portal.v2, layer);
            if (frontier.Count == 0)
                return;

            float3 center = ReadPortalPointFromPositions(portal);
            int triCount = _meshModifier.indices.Length / 3;
            for (int ring = 0; ring < math.max(1, ringDepth); ring++)
            {
                var nextFrontier = new HashSet<int>();
                float ringScale = ring == 0 ? 1f : 0.55f;

                for (int tri = 0; tri < triCount; tri++)
                {
                    if (!IsTriangleFullyInPortalLayer(tri, layer))
                        continue;

                    int baseIndex = tri * 3;
                    int i0 = _meshModifier.indices[baseIndex];
                    int i1 = _meshModifier.indices[baseIndex + 1];
                    int i2 = _meshModifier.indices[baseIndex + 2];
                    if (!frontier.Contains(i0) && !frontier.Contains(i1) && !frontier.Contains(i2))
                        continue;

                    AddLayeredRingSutureLinePatchVertex(patch, i0, center, radius, ringWeight * ringScale, layer, maxCount);
                    AddLayeredRingSutureLinePatchVertex(patch, i1, center, radius, ringWeight * ringScale, layer, maxCount);
                    AddLayeredRingSutureLinePatchVertex(patch, i2, center, radius, ringWeight * ringScale, layer, maxCount);
                    AddSutureLineFrontierVertex(nextFrontier, i0, layer);
                    AddSutureLineFrontierVertex(nextFrontier, i1, layer);
                    AddSutureLineFrontierVertex(nextFrontier, i2, layer);

                    if (patch.Count >= maxCount)
                        break;
                }

                if (patch.Count >= maxCount || nextFrontier.Count == 0)
                    break;
                frontier = nextFrontier;
            }

            if (patch.Count < 3)
                AddSutureLinePortalPrimaryVertices(patch, portal, layer, 1f, maxCount);
        }

        private void AddSutureLinePortalPrimaryVertices(
            List<SutureLinePatchVertexInfo> patch,
            NeedlePortalInfo portal,
            NeedlePortalWallLayer layer,
            float scale,
            int maxCount)
        {
            if (portal.triangleIndex >= 0 && !IsTriangleFullyInPortalLayer(portal.triangleIndex, layer))
                return;

            AddLayeredSutureLinePatchVertex(patch, portal.v0, math.max(0.20f, portal.bary.x + 0.25f) * scale, layer, maxCount);
            AddLayeredSutureLinePatchVertex(patch, portal.v1, math.max(0.20f, portal.bary.y + 0.25f) * scale, layer, maxCount);
            AddLayeredSutureLinePatchVertex(patch, portal.v2, math.max(0.20f, portal.bary.z + 0.25f) * scale, layer, maxCount);
        }

        private void AddSutureLineFrontierVertex(HashSet<int> frontier, int vertexIndex, NeedlePortalWallLayer layer)
        {
            if (IsVertexInPortalLayer(vertexIndex, layer))
                frontier.Add(vertexIndex);
        }

        private void AddLayeredRingSutureLinePatchVertex(
            List<SutureLinePatchVertexInfo> patch,
            int vertexIndex,
            float3 center,
            float radius,
            float ringWeight,
            NeedlePortalWallLayer layer,
            int maxCount)
        {
            if (!IsVertexInPortalLayer(vertexIndex, layer) || ringWeight <= 0f)
                return;

            float weight = ringWeight;
            if (radius > 1e-6f)
            {
                float distance = math.distance(_meshModifier.vertices[vertexIndex], center);
                if (distance > radius)
                    return;
                weight *= math.max(0.15f, 1f - distance / radius);
            }

            AddLayeredSutureLinePatchVertex(patch, vertexIndex, weight, layer, maxCount);
        }

        private void AddLayeredSutureLinePatchVertex(
            List<SutureLinePatchVertexInfo> patch,
            int vertexIndex,
            float weight,
            NeedlePortalWallLayer layer,
            int maxCount,
            byte flags = SutureLinePatchVertexInfo.FlagPullAnchor,
            float bandT = 0f)
        {
            if (!IsVertexInPortalLayer(vertexIndex, layer))
                return;
            AddOrRaiseSutureLinePatchVertex(patch, vertexIndex, weight, maxCount, flags, bandT);
        }

        private int AppendSutureLinePatchVertices(NeedlePortalInfo portal, out int patchCount)
        {
            patchCount = 0;
            if (!_sutureLinePatchVertices.IsCreated || !_meshModifier.vertices.IsCreated || !_meshModifier.indices.IsCreated)
                return -1;

            int start = _sutureLinePatchVertices.Length;
            int maxCount = _setting != null ? math.max(3, _setting.sutureLinePatchMaxVertexCount) : 18;
            float radius = _setting != null ? math.max(0f, _setting.sutureLinePatchRadius) : 0.018f;
            float ringWeight = _setting != null ? math.saturate(_setting.sutureLinePatchRingWeight) : 0.35f;
            float3 center = ReadPortalPointFromPositions(portal);

            var patch = new List<SutureLinePatchVertexInfo>(maxCount);
            AddOrRaiseSutureLinePatchVertex(patch, portal.v0, math.max(0.20f, portal.bary.x + 0.25f), maxCount);
            AddOrRaiseSutureLinePatchVertex(patch, portal.v1, math.max(0.20f, portal.bary.y + 0.25f), maxCount);
            AddOrRaiseSutureLinePatchVertex(patch, portal.v2, math.max(0.20f, portal.bary.z + 0.25f), maxCount);

            int triCount = _meshModifier.indices.Length / 3;
            for (int tri = 0; tri < triCount; tri++)
            {
                int baseIndex = tri * 3;
                int i0 = _meshModifier.indices[baseIndex];
                int i1 = _meshModifier.indices[baseIndex + 1];
                int i2 = _meshModifier.indices[baseIndex + 2];
                bool touchesPortalTriangle = i0 == portal.v0 || i0 == portal.v1 || i0 == portal.v2
                                          || i1 == portal.v0 || i1 == portal.v1 || i1 == portal.v2
                                          || i2 == portal.v0 || i2 == portal.v1 || i2 == portal.v2;
                if (!touchesPortalTriangle)
                    continue;

                AddRingSutureLinePatchVertex(patch, i0, center, radius, ringWeight, maxCount);
                AddRingSutureLinePatchVertex(patch, i1, center, radius, ringWeight, maxCount);
                AddRingSutureLinePatchVertex(patch, i2, center, radius, ringWeight, maxCount);
                if (patch.Count >= maxCount)
                    break;
            }

            if (patch.Count < 3)
            {
                AddOrRaiseSutureLinePatchVertex(patch, portal.v0, math.max(0.20f, portal.bary.x + 0.25f), maxCount);
                AddOrRaiseSutureLinePatchVertex(patch, portal.v1, math.max(0.20f, portal.bary.y + 0.25f), maxCount);
                AddOrRaiseSutureLinePatchVertex(patch, portal.v2, math.max(0.20f, portal.bary.z + 0.25f), maxCount);
            }

            for (int i = 0; i < patch.Count; i++)
                _sutureLinePatchVertices.Add(patch[i]);

            patchCount = patch.Count;
            return patchCount > 0 ? start : -1;
        }

        private void AddRingSutureLinePatchVertex(List<SutureLinePatchVertexInfo> patch, int vertexIndex, float3 center, float radius, float ringWeight, int maxCount)
        {
            if (vertexIndex < 0 || vertexIndex >= _meshModifier.vertices.Length || ringWeight <= 0f)
                return;

            float weight = ringWeight;
            if (radius > 1e-6f)
            {
                float distance = math.distance(_meshModifier.vertices[vertexIndex], center);
                if (distance > radius)
                    return;
                weight *= math.max(0.15f, 1f - distance / radius);
            }

            AddOrRaiseSutureLinePatchVertex(patch, vertexIndex, weight, maxCount);
        }

        private void AddOrRaiseSutureLinePatchVertex(
            List<SutureLinePatchVertexInfo> patch,
            int vertexIndex,
            float weight,
            int maxCount,
            byte flags = SutureLinePatchVertexInfo.FlagPullAnchor,
            float bandT = 0f)
        {
            if (vertexIndex < 0 || vertexIndex >= _meshModifier.vertices.Length || weight <= 0f)
                return;

            if (flags == 0)
                flags = SutureLinePatchVertexInfo.FlagPullAnchor;

            for (int i = 0; i < patch.Count; i++)
            {
                SutureLinePatchVertexInfo item = patch[i];
                if (item.vertexIndex != vertexIndex)
                    continue;

                bool replaceRoleSample = weight >= item.weight;
                bool hasClosureBandRole = (flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0;
                item.weight = math.max(item.weight, weight);
                item.flags = (byte)(item.flags | flags);
                if (hasClosureBandRole || replaceRoleSample)
                    item.bandT = bandT;
                patch[i] = item;
                return;
            }

            if (patch.Count >= maxCount)
                return;

            patch.Add(new SutureLinePatchVertexInfo()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                bandT = bandT,
                flags = flags,
            });
        }

        private float3 ReadSutureLineConstrainedPoint(NeedlePortalInfo portal, int patchStart, int patchCount)
        {
            if (_setting != null && _setting.enableSutureLinePatchPull &&
                TryReadSutureLineWholeWallHoleColumnPoint(portal, patchStart, patchCount, out float3 columnPoint))
                return columnPoint;
            return ReadPortalPointFromPredictedPositions(portal);
        }

        private bool TryReadSutureLineWholeWallHoleColumnPoint(NeedlePortalInfo portal, int patchStart, int patchCount, out float3 columnPoint)
        {
            columnPoint = float3.zero;
            if (!_crossLayerLinks.IsCreated || _crossLayerLinks.Length == 0 ||
                !TryReadSutureLineLinkedPortalPoint(portal, patchStart, patchCount, out float3 innerPoint))
                return false;

            columnPoint = (ReadPortalPointFromPredictedPositions(portal) + innerPoint) * 0.5f;
            return true;
        }

        private bool TryReadSutureLineLinkedPortalPoint(NeedlePortalInfo portal, int patchStart, int patchCount, out float3 point)
        {
            point = float3.zero;
            if (!_crossLayerLinks.IsCreated || _crossLayerLinks.Length == 0 ||
                !_sutureLinePatchVertices.IsCreated || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > _sutureLinePatchVertices.Length)
                return false;

            float weightSum = 0f;
            for (int slot = 0; slot < 3; slot++)
            {
                int vertex = GetSutureLinePortalVertex(portal, slot);
                float weight = GetSutureLinePortalWeight(portal, slot);
                if (vertex < 0 || weight <= 1e-6f ||
                    !TryFindSutureLineLinkedPatchVertex(vertex, patchStart, patchCount, out int linkedVertex))
                    continue;

                point += _predictPositions[linkedVertex] * weight;
                weightSum += weight;
            }

            if (weightSum <= 1e-6f)
            {
                point = float3.zero;
                return false;
            }

            point /= weightSum;
            return true;
        }

        private bool TryFindSutureLineLinkedPatchVertex(int vertex, int patchStart, int patchCount, out int linkedVertex)
        {
            linkedVertex = -1;
            if (!_crossLayerLinks.IsCreated || !_sutureLinePatchVertices.IsCreated ||
                patchStart < 0 || patchCount <= 0 || patchStart + patchCount > _sutureLinePatchVertices.Length)
                return false;

            for (int i = 0; i < _crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = _crossLayerLinks[i];
                int other = link.a == vertex ? link.b : (link.b == vertex ? link.a : -1);
                if (other < 0 || other >= _predictPositions.Length || !IsSutureLinePatchVertex(patchStart, patchCount, other))
                    continue;

                linkedVertex = other;
                return true;
            }

            return false;
        }

        private bool IsSutureLinePatchVertex(int patchStart, int patchCount, int vertex)
        {
            if (!_sutureLinePatchVertices.IsCreated || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > _sutureLinePatchVertices.Length)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                if (_sutureLinePatchVertices[patchStart + i].vertexIndex == vertex)
                    return true;
            }

            return false;
        }

        private static int GetSutureLinePortalVertex(NeedlePortalInfo portal, int slot)
        {
            switch (slot)
            {
                case 0: return portal.v0;
                case 1: return portal.v1;
                default: return portal.v2;
            }
        }

        private static float GetSutureLinePortalWeight(NeedlePortalInfo portal, int slot)
        {
            switch (slot)
            {
                case 0: return portal.bary.x;
                case 1: return portal.bary.y;
                default: return portal.bary.z;
            }
        }

        private void UpsertSutureLineDisplayInfo(SutureLineConstraintInfo constraint, float3 pointA, float3 pointB)
        {
            SutureLineDisplayInfo display = new SutureLineDisplayInfo()
            {
                lineIndex = constraint.lineIndex,
                passIndexA = constraint.passIndexA,
                passIndexB = constraint.passIndexB,
                colliderEntityId = constraint.colliderEntityId,
                pointA = pointA,
                pointB = pointB,
                actualDistance = constraint.lastActualDistance > 0f ? constraint.lastActualDistance : math.distance(pointA, pointB),
                currentRestDistance = constraint.currentRestDistance,
                targetRestDistance = constraint.targetRestDistance,
                lastContactPush = constraint.lastContactPush,
                selfContactThickness = constraint.selfContactThickness,
                selfContactNormalPush = constraint.lastSelfContactNormalPush,
                selfContactTangentPush = constraint.lastSelfContactTangentPush,
                minSelfContactVTDistance = constraint.minSelfContactVTDistance,
                minSelfContactEEDistance = constraint.minSelfContactEEDistance,
                roiVertA = constraint.patchCountA,
                roiVertB = constraint.patchCountB,
                roiTriA = constraint.contactTriCountA,
                roiTriB = constraint.contactTriCountB,
                virtualSidewallTriA = constraint.virtualSidewallTriCountA,
                virtualSidewallTriB = constraint.virtualSidewallTriCountB,
                triPairs = constraint.lastSelfContactTriPairs,
                foldColumnCount = constraint.foldColumnCount,
                foldReason = constraint.foldReason,
                patchStartA = constraint.patchStartA,
                patchCountA = constraint.patchCountA,
                patchStartB = constraint.patchStartB,
                patchCountB = constraint.patchCountB,
                contactTriStartA = constraint.contactTriStartA,
                contactTriCountA = constraint.contactTriCountA,
                contactTriStartB = constraint.contactTriStartB,
                contactTriCountB = constraint.contactTriCountB,
                vtPairs = constraint.lastSelfContactVTPairs,
                eePairs = constraint.lastSelfContactEEPairs,
                persistentPairs = constraint.lastPersistentContactPairs,
                lastCompressionFoldPush = constraint.lastCompressionFoldPush,
                foldDir = constraint.foldDir,
                wallLayer = constraint.wallLayer,
                enabled = constraint.enabled,
                localWallContactActive = constraint.localWallContactActive,
                compressionFoldActive = constraint.compressionFoldActive,
                contactState = constraint.contactState,
                settled = (byte)(constraint.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold ? 1 : 0),
                workflowReady = (byte)((constraint.workflowReady != 0 || constraint.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold) ? 1 : 0),
                workflowReadyFrames = constraint.workflowReadyFrames,
                workflowAdvanceReady = (byte)((constraint.workflowAdvanceReady != 0 || constraint.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold) ? 1 : 0),
                workflowAdvanceReadyFrames = constraint.workflowAdvanceReadyFrames,
                workflowReleased = constraint.workflowReleased,
                completedWithoutRealSidewallContact = constraint.completedWithoutRealSidewallContact,
            };

            for (int i = 0; i < _sutureLineDisplayInfos.Count; i++)
            {
                SutureLineDisplayInfo existing = _sutureLineDisplayInfos[i];
                if (existing.lineIndex == constraint.lineIndex
                    && existing.colliderEntityId == constraint.colliderEntityId
                    && existing.wallLayer == constraint.wallLayer)
                {
                    _sutureLineDisplayInfos[i] = display;
                    return;
                }
            }

            _sutureLineDisplayInfos.Add(display);
        }

        private static string FormatSutureLineContactState(byte state)
        {
            switch (state)
            {
                case SutureLineContactStateLatched:
                    return "ContactLatched";
                case SutureLineContactStatePreLatchWallContact:
                    return "PreLatchWallContact";
                default:
                    return "Approach";
            }
        }

        private static string FormatSutureFoldReason(int reason)
        {
            switch (reason)
            {
                case SutureFoldReasonPreLatchWallContact:
                    return "pre-latch-wall-contact";
                case SutureFoldReasonContactLatched:
                    return "contact-latched";
                case SutureFoldReasonFallbackPatch:
                    return "fallback-patch";
                case SutureFoldReasonUnstableContact:
                    return "unstable-contact";
                case SutureFoldReasonReleasing:
                    return "releasing";
                case SutureFoldReasonPreLatchNoColumnSuppressed:
                    return "pre-latch-no-column-suppressed";
                case SutureFoldReasonPreContactApproach:
                    return "pre-contact-closure-band-slide";
                default:
                    return "none";
            }
        }

        private static string FormatSutureLineContactMissReason(int reason)
        {
            switch (reason)
            {
                case SutureContactMissBoundaryGuideInvalid:
                    return "boundary-guide-invalid";
                case SutureContactMissPatchVertexInsufficient:
                    return "patch-vertices-insufficient";
                case SutureContactMissBiteBandEmpty:
                    return "bite-band-empty";
                case SutureContactMissTriangleRoiEmpty:
                    return "triangle-roi-empty";
                case SutureContactMissLipTrianglesNotPhysical:
                    return "lip-triangles-not-physical";
                default:
                    return "none";
            }
        }

        private static string FormatFiniteFloat(float value)
        {
            return value < float.PositiveInfinity ? value.ToString("F6") : "inf";
        }

        private float3 ReadPortalPointFromPredictedPositions(NeedlePortalInfo portal)
        {
            float3 p0 = portal.v0 >= 0 && portal.v0 < _predictPositions.Length ? _predictPositions[portal.v0] : portal.currentPoint;
            float3 p1 = portal.v1 >= 0 && portal.v1 < _predictPositions.Length ? _predictPositions[portal.v1] : portal.currentPoint;
            float3 p2 = portal.v2 >= 0 && portal.v2 < _predictPositions.Length ? _predictPositions[portal.v2] : portal.currentPoint;
            return p0 * portal.bary.x + p1 * portal.bary.y + p2 * portal.bary.z;
        }

        private float3 ReadPortalPointFromPositions(NeedlePortalInfo portal)
        {
            float3 p0 = portal.v0 >= 0 && portal.v0 < _meshModifier.vertices.Length ? _meshModifier.vertices[portal.v0] : portal.currentPoint;
            float3 p1 = portal.v1 >= 0 && portal.v1 < _meshModifier.vertices.Length ? _meshModifier.vertices[portal.v1] : portal.currentPoint;
            float3 p2 = portal.v2 >= 0 && portal.v2 < _meshModifier.vertices.Length ? _meshModifier.vertices[portal.v2] : portal.currentPoint;
            return p0 * portal.bary.x + p1 * portal.bary.y + p2 * portal.bary.z;
        }

        private void MarkSuturePassLineLinked(int passIndex, int colliderEntityId, int lineIndex)
        {
            if (passIndex <= 0 || _needlePortalHistory.Count == 0)
                return;

            for (int i = 0; i < _needlePortalHistory.Count; i++)
            {
                NeedlePortalHistoryEntry entry = _needlePortalHistory[i];
                if (entry.portal.enabled == 0 || entry.passIndex != passIndex || entry.colliderEntityId != colliderEntityId)
                    continue;

                entry.stitchState = (byte)NeedlePortalStitchState.Hist;
                if (entry.stitchIndex <= 0)
                    entry.stitchIndex = lineIndex;
                entry.retainAsRelaxPortal = 1;
                entry.portal.stitchState = entry.stitchState;
                _needlePortalHistory[i] = entry;
            }
        }

        private void MarkSuturePassDone(int passIndex, int colliderEntityId, int stitchIndex)
        {
            for (int i = 0; i < _needlePortalHistory.Count; i++)
            {
                NeedlePortalHistoryEntry entry = _needlePortalHistory[i];
                if (entry.colliderEntityId != colliderEntityId || entry.passIndex != passIndex)
                    continue;

                entry.isActive = 0;
                entry.retainAsRelaxPortal = 0;
                entry.stitchState = (byte)NeedlePortalStitchState.Done;
                entry.stitchIndex = stitchIndex;
                NeedlePortalInfo portal = entry.portal;
                portal.stitchState = (byte)NeedlePortalStitchState.Done;
                entry.portal = portal;
                _needlePortalHistory[i] = entry;
            }
        }

        private void PromoteCurrentPortalPairToHistoryRelax(ref NeedlePunctureRuntimeState state, bool finishPuncture)
        {
            bool promoted = false;
            bool clearedProvisional = false;
            bool completedPair = CurrentPassHasCommittedPortalPair(state);
            bool passIsExit = state.currentPassIsExit != 0;

            if (state.outerPortal.enabled != 0)
            {
                if (state.outerPortal.isProvisional != 0)
                {
                    DisableNeedlePortalHistoryRetention(state.outerHistoryIndex);
                    state.outerPortal = default;
                    state.outerHistoryIndex = -1;
                    clearedProvisional = true;
                }
                else
                {
                    if (state.outerHistoryIndex < 0)
                    {
                        state.outerHistoryIndex = RegisterNeedlePortalHistory(
                            state.outerPortal,
                            math.max(1, state.currentPassIndex),
                            passIsExit,
                            0);
                    }
                    PromoteNeedlePortalHistoryToRetainedRelax(state.outerHistoryIndex, state.outerPortal);
                    state.outerPortal = default;
                    state.outerHistoryIndex = -1;
                    promoted = true;
                }
            }

            if (state.innerPortal.enabled != 0)
            {
                if (state.innerPortal.isProvisional != 0)
                {
                    DisableNeedlePortalHistoryRetention(state.innerHistoryIndex);
                    state.innerPortal = default;
                    state.innerHistoryIndex = -1;
                    clearedProvisional = true;
                }
                else
                {
                    if (state.innerHistoryIndex < 0)
                    {
                        int pairedPortalIndex = 0;
                        state.innerHistoryIndex = RegisterNeedlePortalHistory(
                            state.innerPortal,
                            math.max(1, state.currentPassIndex),
                            passIsExit,
                            pairedPortalIndex);
                    }
                    PromoteNeedlePortalHistoryToRetainedRelax(state.innerHistoryIndex, state.innerPortal);
                    state.innerPortal = default;
                    state.innerHistoryIndex = -1;
                    promoted = true;
                }
            }

            if (promoted || clearedProvisional)
            {
                if (completedPair)
                    state.needleInsideLumen = (byte)(passIsExit ? 0 : 1);

                state.phase = NeedlePuncturePhase.None;
                ResetNeedleCurrentPassSemantics(ref state);
                state.cooldown = 0f;
            }

            if (finishPuncture)
                state.punctureFinished = 1;
            else if (promoted)
                state.punctureFinished = 0;
        }

        private void AddRetainedHistoryPortalsForHoldDisplay()
        {
            for (int i = 0; i < _needlePortalHistory.Count; i++)
            {
                var entry = _needlePortalHistory[i];
                if (entry.retainAsRelaxPortal == 0 || entry.portal.enabled == 0)
                    continue;

                var portal = entry.portal;
                if (!RefreshNeedlePortalCurrentPoint(ref portal))
                {
                    entry.portal = default;
                    entry.isActive = 0;
                    entry.retainAsRelaxPortal = 0;
                    _needlePortalHistory[i] = entry;
                    continue;
                }

                if (entry.stitchState == 0)
                    entry.stitchState = (byte)NeedlePortalStitchState.Hist;
                portal.stitchState = entry.stitchState;
                entry.portal = portal;
                entry.isActive = 0;
                _needlePortalHistory[i] = entry;

                // Historical portals keep stitch hold/display only. Collision and layer-separation
                // relax read _activeNeedleConstraintPortals, which contains only the active needle portal.
                _activeNeedlePortals.Add(portal);
                var display = CreateNeedlePortalDisplayInfo(i, portal, false, true);
                display.stitchState = entry.stitchState;
                display.stitchIndex = entry.stitchIndex;
                _activeNeedlePortalDisplayInfos.Add(display);
                if (_retainedHistoryHoldPortals.IsCreated)
                    _retainedHistoryHoldPortals.Add(portal);
            }
        }

        private void ResetNeedlePortalHistoryActivity()
        {
            for (int i = 0; i < _needlePortalHistory.Count; i++)
            {
                var entry = _needlePortalHistory[i];
                entry.isActive = 0;
                _needlePortalHistory[i] = entry;
            }
        }

        private NeedlePortalDisplayInfo CreateNeedlePortalDisplayInfo(
            int historyIndex,
            NeedlePortalInfo portal,
            bool isConstraintPortal,
            bool isHistorical,
            int fallbackPassIndex = 0,
            bool fallbackIsExitPortal = false,
            byte fallbackEntryLayer = 0,
            byte fallbackBreakthroughLayer = 0,
            byte fallbackPassComplete = 0)
        {
            NeedlePortalDisplayInfo info = default;
            info.colliderEntityId = portal.colliderEntityId;
            info.stitchState = portal.stitchState;
            info.isConstraintPortal = (byte)(isConstraintPortal ? 1 : 0);
            info.isHistorical = (byte)(isHistorical ? 1 : 0);
            info.passIndex = math.max(0, fallbackPassIndex);
            info.isExitPortal = (byte)(fallbackIsExitPortal ? 1 : 0);
            info.isProvisional = portal.isProvisional;
            info.suppressPunctureFx = portal.suppressPunctureFx;
            info.isBreakthroughEvent = portal.isBreakthroughEvent;
            info.wallEventOwner = portal.wallEventOwner;
            info.entryLayer = portal.entryLayer != 0 ? portal.entryLayer : fallbackEntryLayer;
            info.breakthroughLayer = portal.breakthroughLayer != 0 ? portal.breakthroughLayer : fallbackBreakthroughLayer;
            info.passComplete = portal.passComplete != 0 ? portal.passComplete : fallbackPassComplete;
            if ((uint)historyIndex < (uint)_needlePortalHistory.Count)
            {
                var entry = _needlePortalHistory[historyIndex];
                info.portalIndex = entry.portalIndex;
                info.passIndex = entry.passIndex;
                info.pairedPortalIndex = entry.pairedPortalIndex;
                info.stitchIndex = entry.stitchIndex;
                info.isExitPortal = entry.isExitPortal;
                info.stitchState = entry.stitchState;
                info.colliderEntityId = entry.colliderEntityId;
            }
            return info;
        }

        private static float GetPortalAxial(CapsuleDesc capsule, NeedlePortalInfo portal)
        {
            float3 dir = math.normalizesafe(portal.insertDirection, -portal.entryNormal);
            float3 toTip = capsule.tipProbeCenter - portal.currentPoint;
            return math.dot(toTip, dir);
        }

        private static float GetPortalLateral(CapsuleDesc capsule, NeedlePortalInfo portal)
        {
            float3 dir = math.normalizesafe(portal.insertDirection, -portal.entryNormal);
            float3 toTip = capsule.tipProbeCenter - portal.currentPoint;
            float axial = math.dot(toTip, dir);
            float3 lateralVec = toTip - dir * axial;
            return math.length(lateralVec);
        }

        private static float GetPortalAxialFromTipPoint(float3 tipPoint, NeedlePortalInfo portal)
        {
            float3 dir = math.normalizesafe(portal.insertDirection, -portal.entryNormal);
            return math.dot(tipPoint - portal.currentPoint, dir);
        }

        private static float GetPortalLateralFromTipPoint(float3 tipPoint, NeedlePortalInfo portal)
        {
            float3 dir = math.normalizesafe(portal.insertDirection, -portal.entryNormal);
            float3 toTip = tipPoint - portal.currentPoint;
            float axial = math.dot(toTip, dir);
            float3 lateralVec = toTip - dir * axial;
            return math.length(lateralVec);
        }

        private static float GetMaxPortalAxialOnSweep(float3 tipStart, float3 tipEnd, NeedlePortalInfo portal)
        {
            float sweepLen = math.distance(tipStart, tipEnd);
            int sampleCount = math.clamp((int)math.ceil(sweepLen / 0.0008f) + 1, 2, 8);
            float maxAxial = float.MinValue;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount > 1 ? (float)i / (sampleCount - 1) : 1f;
                float3 sample = math.lerp(tipStart, tipEnd, t);
                maxAxial = math.max(maxAxial, GetPortalAxialFromTipPoint(sample, portal));
            }
            return maxAxial;
        }

        private static float GetMinPortalAxialOnSweep(float3 tipStart, float3 tipEnd, NeedlePortalInfo portal)
        {
            float sweepLen = math.distance(tipStart, tipEnd);
            int sampleCount = math.clamp((int)math.ceil(sweepLen / 0.0008f) + 1, 2, 8);
            float minAxial = float.MaxValue;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount > 1 ? (float)i / (sampleCount - 1) : 1f;
                float3 sample = math.lerp(tipStart, tipEnd, t);
                minAxial = math.min(minAxial, GetPortalAxialFromTipPoint(sample, portal));
            }
            return minAxial;
        }

        private static float GetMinPortalLateralOnSweep(float3 tipStart, float3 tipEnd, NeedlePortalInfo portal)
        {
            float sweepLen = math.distance(tipStart, tipEnd);
            int sampleCount = math.clamp((int)math.ceil(sweepLen / 0.0008f) + 1, 2, 8);
            float minLateral = float.MaxValue;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount > 1 ? (float)i / (sampleCount - 1) : 1f;
                float3 sample = math.lerp(tipStart, tipEnd, t);
                minLateral = math.min(minLateral, GetPortalLateralFromTipPoint(sample, portal));
            }
            return minLateral;
        }

        private static float3 GetNeedleSweepForward(float3 tipStart, float3 tipEnd, float3 fallbackForward)
        {
            float3 sweep = tipEnd - tipStart;
            if (math.lengthsq(sweep) > 1e-10f)
                return math.normalizesafe(sweep, fallbackForward);
            return math.normalizesafe(fallbackForward, new float3(0f, 1f, 0f));
        }

        private static float3 GetNeedleTipPiercerAttachPoint(CapsuleDesc capsule, float attachT)
        {
            // TipPiercer cone anchors may live slightly behind tipCenter, so attachT is
            // intentionally not saturated to 0..1.
            return math.lerp(capsule.tipCenter, capsule.tipProbeCenter, math.clamp(attachT, -8f, 2f));
        }

        private static float GetNeedleTipHookAdvance(CapsuleDesc capsule, NeedlePunctureRuntimeState state)
        {
            if (state.hookActive == 0)
                return 0f;
            float3 dir = math.normalizesafe(capsule.tipForward, -state.hookNormal);
            float3 attachPoint = GetNeedleTipPiercerAttachPoint(capsule, state.hookAttachT);
            return math.max(0f, math.dot(attachPoint - state.hookStartTipProbeCenter, dir));
        }

        private float GetNeedleTipHookDepth(CapsuleDesc capsule, NeedlePunctureRuntimeState state)
        {
            if (_setting == null || state.hookActive == 0)
                return 0f;
            float3 dir = math.normalizesafe(capsule.tipForward, -state.hookNormal);
            float3 normal = math.normalizesafe(state.hookNormal, -dir);
            if (math.dot(normal, dir) > 0f)
                normal = -normal;
            float3 attachPoint = GetNeedleTipPiercerAttachPoint(capsule, state.hookAttachT);
            float3 delta = attachPoint - state.hookPoint;
            float axialDepth = math.max(0f, math.dot(delta, dir));
            float normalDepth = math.max(0f, -math.dot(delta, normal));
            return math.max(axialDepth, normalDepth);
        }

        private bool IsNeedleTipHookReadyForPreChannel(CapsuleDesc capsule, NeedlePunctureRuntimeState state)
        {
            if (_setting == null || state.hookActive == 0)
                return false;
            if (capsule.needleMotionMode == 2)
                return false;

            // 只要针尖刚碰到表面，depth 往往立刻很大；如果用 depth 直接放行，
            // 就会同帧跳过“粘滞保持/挑起”手感进入 portal。
            // 这里改成必须沿针的推进方向有一段真实 advance 后，才允许 provisional PreChannel。
            float advanceThreshold = math.max(_setting.needleTipPiercerAdvanceThreshold, _setting.needleTipPiercerRadius * 0.65f);
            return state.hookAdvance >= advanceThreshold;
        }

        private bool ShouldBreakNeedleTipPiercer(CapsuleDesc capsule, NeedlePunctureRuntimeState state, out float distance, out float breakDistance)
        {
            distance = 0f;
            breakDistance = 0f;
            if (_setting == null || state.hookActive == 0)
                return false;

            float3 dir = math.normalizesafe(capsule.tipForward, -state.hookNormal);
            float3 apex = GetNeedleTipPiercerAttachPoint(capsule, state.hookAttachT);
            float coneLength = GetNeedleTipPiercerConeLength(capsule);
            float axial = math.dot(apex - state.hookPoint, dir);
            float clampedAxial = math.clamp(axial, 0f, math.max(coneLength, 1e-6f));
            float3 axisPoint = apex - dir * clampedAxial;
            distance = math.distance(state.hookPoint, axisPoint);
            breakDistance = _setting.needleTipPiercerBreakDistance > 0f
                ? _setting.needleTipPiercerBreakDistance
                : math.max(_setting.needleTipPiercerRadius * 2f, capsule.tipProbeRadius * 2f);

            float axialSlack = math.max(breakDistance, GetNeedleTipPiercerConeSurfaceMargin(capsule));
            return distance > breakDistance || axial < -axialSlack || axial > coneLength + axialSlack;
        }

        private bool TryRefreshNeedleTipPiercerAnchor(ref NeedlePunctureRuntimeState state, out float3 currentHookPoint)
        {
            currentHookPoint = default;
            if (state.hookActive == 0 || _meshModifier == null)
                return false;

            NativeArray<int> indices = _meshModifier.indices;
            NativeArray<float3> predict = _predictPositions.AsArray();
            int tri = state.hookTriangleIndex;
            int triOffset = tri * 3;
            if (tri < 0 || triOffset + 2 >= indices.Length)
                return false;

            int v0 = indices[triOffset];
            int v1 = indices[triOffset + 1];
            int v2 = indices[triOffset + 2];
            if ((uint)v0 >= (uint)predict.Length || (uint)v1 >= (uint)predict.Length || (uint)v2 >= (uint)predict.Length)
                return false;

            float3 bary = state.hookBary;
            float b0 = math.saturate(bary.x);
            float b1 = math.saturate(bary.y);
            float b2 = math.saturate(bary.z);
            float bSum = b0 + b1 + b2;
            if (bSum <= 1e-8f)
                return false;

            b0 /= bSum;
            b1 /= bSum;
            b2 /= bSum;
            state.hookBary = new float3(b0, b1, b2);
            currentHookPoint = predict[v0] * b0 + predict[v1] * b1 + predict[v2] * b2;
            state.hookPoint = currentHookPoint;
            return true;
        }

        private float GetNeedleTipPiercerConeLength(CapsuleDesc capsule)
        {
            float configured = _setting != null ? math.max(0f, _setting.needleTipPiercerConeLength) : 0f;
            float3 fallbackAxis = math.normalizesafe(capsule.axis.xyz, new float3(0f, 1f, 0f));
            float3 dir = math.normalizesafe(capsule.tipForward, fallbackAxis);
            float radius = math.max(capsule.radius, capsule.tipProbeRadius * 0.65f);
            float probeForwardOffset = math.max(0f, math.dot(capsule.tipProbeCenter - capsule.tipCenter, dir));
            float minLength = probeForwardOffset + math.max(radius * 1.75f, capsule.tipProbeRadius);
            float autoLength = math.max(math.max(radius * 5.0f, 0.003f), minLength);
            return configured > 1e-6f ? math.max(configured, minLength) : autoLength;
        }

        private float GetNeedleTipPiercerConeBaseRadius(CapsuleDesc capsule)
        {
            float scale = _setting != null ? math.max(0.05f, _setting.needleTipPiercerConeBaseRadiusScale) : 0.85f;
            return math.max(capsule.radius * scale, capsule.tipProbeRadius * 0.35f);
        }

        private float GetNeedleTipPiercerConeSurfaceMargin(CapsuleDesc capsule)
        {
            float configured = _setting != null ? math.max(0f, _setting.needleTipPiercerConeSurfaceMargin) : 0.0012f;
            return math.max(configured, math.max(capsule.radius, capsule.tipProbeRadius) * 0.06f);
        }

        private float EstimateNeedleTipPiercerFieldWeight(NeedleTipHookCandidate candidate)
        {
            if (_setting == null || candidate.coneLength <= 1e-6f)
                return 0f;

            float influenceRadius = math.max(_setting.needleTipInfluenceRadius, _setting.needleTipPiercerPatchRadius * 0.60f);
            float coneMargin = math.max(_setting.needleTipPiercerConeSurfaceMargin, 0f);
            float activeBand = math.max(math.max(influenceRadius, coneMargin * 4f), candidate.coneBaseRadius * 0.55f);
            if (activeBand <= 1e-6f)
                return 0f;

            float signedConeDistance = candidate.coneLateral - candidate.coneRadiusAtPoint;
            float radial01 = 1f - math.saturate(math.max(0f, signedConeDistance) / activeBand);
            float apex01 = 1f - math.saturate(candidate.coneAxial / math.max(candidate.coneLength, 1e-6f));
            float inside01 = 1f - math.saturate(math.max(0f, signedConeDistance - coneMargin) / activeBand);
            float stiffnessBoost = 0.55f + 0.45f * math.saturate(_setting.needleTipPiercerAxialStiffness);

            float weight = radial01 * (0.35f + 0.65f * apex01);
            weight = math.max(weight, inside01 * (0.22f + 0.48f * apex01));
            return math.saturate(weight * stiffnessBoost);
        }

        private float EstimateNeedleTipPiercerFieldPush(NeedleTipHookCandidate candidate)
        {
            if (_setting == null)
                return 0f;

            float influenceRadius = math.max(_setting.needleTipInfluenceRadius, _setting.needleTipPiercerPatchRadius * 0.60f);
            float weight = EstimateNeedleTipPiercerFieldWeight(candidate);
            float push = influenceRadius * weight * math.saturate(_setting.needleTipPiercerAxialStiffness);
            float maxPush = _setting.needleTipPiercerMaxPushPerIteration;
            return maxPush > 0f ? math.min(push, maxPush) : push;
        }

        private float EstimateNeedleTipPiercerStateLoad01(NeedlePunctureRuntimeState state, bool preChannelActive)
        {
            if (_setting == null)
                return preChannelActive ? 1f : 0f;

            float advanceThreshold = math.max(_setting.needleTipPiercerAdvanceThreshold, _setting.needleTipPiercerRadius * 0.65f);
            float depthThreshold = math.max(_setting.needleTipPiercerRadius * 0.45f, _setting.needleTipPiercerAdvanceThreshold);
            float advance01 = advanceThreshold > 1e-6f ? math.saturate(state.hookAdvance / advanceThreshold) : 0f;
            float depth01 = depthThreshold > 1e-6f ? math.saturate(state.hookDepth / depthThreshold) : 0f;
            float load = math.max(advance01, depth01 * 0.85f);
            if (preChannelActive)
                load = math.max(load, 0.90f);
            return math.saturate(load);
        }

        private float EstimateNeedleTipPiercerPunctureLoad01(
            NeedlePunctureRuntimeState state,
            NeedleTipHookCandidate candidate,
            bool preChannelActive)
        {
            if (_setting == null)
                return preChannelActive ? 1f : 0f;

            float fieldWeight = EstimateNeedleTipPiercerFieldWeight(candidate);
            float candidateDepthThreshold = math.max(
                _setting.needleTipPiercerRadius * 0.35f,
                math.max(_setting.needleTipPiercerConeSurfaceMargin * 2.0f, candidate.coneBaseRadius * 0.18f));
            float candidateDepth01 = candidateDepthThreshold > 1e-6f
                ? math.saturate(candidate.depth / candidateDepthThreshold)
                : 0f;

            float stateLoad = state.hookActive != 0
                ? EstimateNeedleTipPiercerStateLoad01(state, preChannelActive)
                : 0f;
            float load = math.max(stateLoad, math.max(fieldWeight * 0.70f, candidateDepth01));
            if (preChannelActive)
                load = math.max(load, 0.90f);
            return math.saturate(load);
        }

        private float GetNeedleTipPiercerNormalBite(float patchRadius, float load01)
        {
            if (_setting == null)
                return 0f;

            float baseBite = math.min(math.max(0.0015f, patchRadius * 0.34f), 0.0060f);
            float bite = baseBite * (0.50f + 0.70f * math.saturate(load01));
            float maxPush = math.max(0f, _setting.needleTipPiercerMaxPushPerIteration);
            if (maxPush > 0f)
                bite = math.min(bite, maxPush);
            return math.max(0f, bite);
        }

        private void QueueNeedleTipPiercerFeedback(
            int colliderEntityId,
            float3 tipForward,
            float desiredPush,
            float fieldWeight,
            float dt)
        {
            if (_setting == null || !_setting.enableNeedleTipPiercerFeedback)
                return;
            if (colliderEntityId == 0 || desiredPush <= 1e-7f)
                return;

            float3 dir = math.normalizesafe(tipForward, new float3(0f, 1f, 0f));
            float safeDt = math.max(dt, 1e-5f);
            float resistanceWeight = math.saturate(0.35f + fieldWeight * 0.65f);

            float velocityMag = (desiredPush / safeDt)
                * resistanceWeight
                * math.max(0f, _setting.needleTipPiercerFeedbackVelocityScale);
            float maxVelocity = math.max(0f, _setting.needleTipPiercerFeedbackMaxVelocityChange);
            if (maxVelocity > 0f)
                velocityMag = math.min(velocityMag, maxVelocity);

            float rigDistance = desiredPush
                * resistanceWeight
                * math.max(0f, _setting.needleTipPiercerFeedbackRigDistanceScale);
            float maxRigDistance = math.max(0f, _setting.needleTipPiercerFeedbackMaxRigRetreatPerFrame);
            if (maxRigDistance > 0f)
                rigDistance = math.min(rigDistance, maxRigDistance);

            if (velocityMag <= 1e-7f && rigDistance <= 1e-7f)
                return;

            _needleTipPiercerFeedbacks.Add(new NeedleTipPiercerFeedbackApply()
            {
                colliderEntityId = colliderEntityId,
                velocityChange = -dir * velocityMag,
                rigDelta = -dir * rigDistance,
                desiredPush = desiredPush,
                fieldWeight = fieldWeight,
            });
        }

        private static float GetNeedleTipPiercerAttachT(CapsuleDesc capsule, float3 attachPoint, float3 dir)
        {
            float3 probeVector = capsule.tipProbeCenter - capsule.tipCenter;
            float denom = math.dot(probeVector, dir);
            if (math.abs(denom) <= 1e-6f)
                return 1f;

            return math.dot(attachPoint - capsule.tipCenter, dir) / denom;
        }

        private static void AccumulateNeedleTipPiercerCone(
            CapsuleDesc capsule,
            float coneLength,
            float coneBaseRadius,
            float coneMargin,
            float3 p0,
            float3 p1,
            float3 p2,
            ref float bestScore,
            ref float bestDistance,
            ref float3 bestBary,
            ref float3 bestPoint,
            ref float3 bestAttachPoint,
            ref float bestAttachT,
            ref float bestDepth,
            ref float bestAxial,
            ref float bestLateral,
            ref float bestConeRadius,
            ref byte bestInside)
        {
            float3 dir = math.normalizesafe(capsule.tipForward, new float3(0f, 1f, 0f));
            float3 apex = capsule.tipProbeCenter;
            float safeLength = math.max(coneLength, 1e-5f);
            float safeBaseRadius = math.max(coneBaseRadius, 1e-6f);
            float safeMargin = math.max(coneMargin, 0f);
            float probeBand = math.max(safeMargin * 4f, safeBaseRadius * 0.75f);
            int sampleCount = math.clamp((int)math.ceil(safeLength / math.max(safeBaseRadius * 0.9f, 0.0012f)) + 1, 4, 7);

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float t = sampleCount > 1 ? (float)sampleIndex / (sampleCount - 1) : 0f;
                float3 axisSample = apex - dir * (safeLength * t);
                float3 bary;
                float3 q = ClosestPointOnTriangleMainThread(axisSample, p0, p1, p2, out bary);

                float axial = math.dot(apex - q, dir);
                float axialOutside = math.max(math.max(-axial, axial - safeLength), 0f);
                if (axialOutside > probeBand)
                    continue;

                float clampedAxial = math.clamp(axial, 0f, safeLength);
                float3 axisPoint = apex - dir * clampedAxial;
                float lateral = math.distance(q, axisPoint);
                float coneRadiusAtAxial = safeBaseRadius * (clampedAxial / safeLength);
                float signedConeDistance = lateral - coneRadiusAtAxial;
                if (signedConeDistance > probeBand)
                    continue;

                bool inside = axialOutside <= safeMargin && signedConeDistance <= safeMargin;
                float depth = math.max(0f, safeMargin - signedConeDistance);
                float axial01 = clampedAxial / safeLength;
                float score = math.max(0f, signedConeDistance) + axialOutside + axial01 * safeMargin * 0.2f - depth * 0.08f;
                if (inside)
                    score -= probeBand;
                if (score >= bestScore)
                    continue;

                // The target is the moving apex, not the cone side. That makes the
                // correction directional along tipForward instead of glueing to a side point.
                bestScore = score;
                bestDistance = math.max(0f, signedConeDistance);
                bestBary = bary;
                bestPoint = q;
                bestAttachPoint = apex;
                bestAttachT = GetNeedleTipPiercerAttachT(capsule, apex, dir);
                bestDepth = depth;
                bestAxial = clampedAxial;
                bestLateral = lateral;
                bestConeRadius = coneRadiusAtAxial;
                bestInside = (byte)(inside ? 1 : 0);
            }
        }

        private static bool MightTriangleOverlapTipPiercerCone(
            CapsuleDesc capsule,
            float coneLength,
            float coneBaseRadius,
            float coneMargin,
            float3 p0,
            float3 p1,
            float3 p2)
        {
            float3 dir = math.normalizesafe(capsule.tipForward, new float3(0f, 1f, 0f));
            float3 apex = capsule.tipProbeCenter;
            float3 sphereCenter = apex - dir * (coneLength * 0.5f);
            float sphereRadius = math.sqrt(coneLength * coneLength * 0.25f + coneBaseRadius * coneBaseRadius) + coneMargin;

            float3 triCenter = (p0 + p1 + p2) / 3f;
            float triRadius = math.sqrt(math.max(
                math.lengthsq(p0 - triCenter),
                math.max(math.lengthsq(p1 - triCenter), math.lengthsq(p2 - triCenter))));

            float maxDistance = sphereRadius + triRadius;
            return math.lengthsq(triCenter - sphereCenter) <= maxDistance * maxDistance;
        }

        private bool TryFindNeedleTipHookCandidate(
            NeedlePortalWallLayer desiredLayer,
            float3 prevTipCenter,
            float3 sweepStart,
            float3 sweepEnd,
            float3 detectForward,
            CapsuleDesc capsule,
            bool candidatesOnly,
            bool ignoreLayerFilter,
            out NeedleTipHookCandidate candidate,
            out string failReason)
        {
            candidate = default;
            candidate.triangleIndex = -1;
            failReason = "not-tested";
            if (_setting == null || !_setting.enableNeedleTipPiercer)
            {
                failReason = "disabled";
                return false;
            }

            if (capsule.needleMotionMode == 2)
            {
                failReason = "retracting";
                return false;
            }

            if (desiredLayer == NeedlePortalWallLayer.Inner && (!_hasDoubleLayerSlices || !_setting.enableNeedleInnerWallPuncture))
            {
                failReason = "inner-disabled";
                return false;
            }

            float coneLength = GetNeedleTipPiercerConeLength(capsule);
            float coneBaseRadius = GetNeedleTipPiercerConeBaseRadius(capsule);
            float coneMargin = GetNeedleTipPiercerConeSurfaceMargin(capsule);
            if (coneLength <= 1e-6f || coneBaseRadius <= 1e-6f)
            {
                failReason = "cone-zero";
                return false;
            }

            NativeArray<float3> predict = _predictPositions.AsArray();
            NativeArray<int> indices = _meshModifier.indices;
            int triCountLocal = triangleCount;
            int candidateCount = candidatesOnly
                ? (_triangleSurfaceContactCandidates.IsCreated ? _triangleSurfaceContactCandidates.Length : 0)
                : triCountLocal;

            if (candidateCount <= 0)
            {
                failReason = "no-triangle-candidates";
                return false;
            }

            float bestScore = float.MaxValue;
            float bestProbeScore = float.MaxValue;
            NeedleTipHookCandidate bestProbe = default;
            bestProbe.triangleIndex = -1;
            float sweepAdvance = math.max(0f, math.dot(sweepEnd - sweepStart, detectForward));
            int layerRejected = 0;
            int distanceRejected = 0;
            int broadRejected = 0;

            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                int tri = candidatesOnly ? _triangleSurfaceContactCandidates[candidateIndex] : candidateIndex;
                if (!ignoreLayerFilter && !IsTriangleInPortalLayer(tri, desiredLayer))
                {
                    layerRejected++;
                    continue;
                }

                int triOffset = tri * 3;
                if (triOffset + 2 >= indices.Length)
                    continue;

                int i0 = indices[triOffset];
                int i1 = indices[triOffset + 1];
                int i2 = indices[triOffset + 2];
                if ((uint)i0 >= (uint)predict.Length || (uint)i1 >= (uint)predict.Length || (uint)i2 >= (uint)predict.Length)
                    continue;

                float3 p0 = predict[i0];
                float3 p1 = predict[i1];
                float3 p2 = predict[i2];
                if (!MightTriangleOverlapTipPiercerCone(capsule, coneLength, coneBaseRadius, coneMargin, p0, p1, p2))
                {
                    broadRejected++;
                    continue;
                }

                float3 triNormal = math.cross(p1 - p0, p2 - p0);
                float nLenSq = math.lengthsq(triNormal);
                if (nLenSq < 1e-12f)
                    continue;
                triNormal *= math.rsqrt(nLenSq);
                if (math.dot(triNormal, detectForward) > 0f)
                    triNormal = -triNormal;

                float bestLocalScore = float.MaxValue;
                float bestLocalDist = float.MaxValue;
                float3 bestLocalBary = default;
                float3 bestLocalPoint = default;
                float3 bestLocalAttachPoint = default;
                float bestLocalAttachT = 1f;
                float bestLocalDepth = 0f;
                float bestLocalAxial = 0f;
                float bestLocalLateral = 0f;
                float bestLocalConeRadius = 0f;
                byte bestLocalInside = 0;

                AccumulateNeedleTipPiercerCone(
                    capsule,
                    coneLength,
                    coneBaseRadius,
                    coneMargin,
                    p0,
                    p1,
                    p2,
                    ref bestLocalScore,
                    ref bestLocalDist,
                    ref bestLocalBary,
                    ref bestLocalPoint,
                    ref bestLocalAttachPoint,
                    ref bestLocalAttachT,
                    ref bestLocalDepth,
                    ref bestLocalAxial,
                    ref bestLocalLateral,
                    ref bestLocalConeRadius,
                    ref bestLocalInside);

                if (bestLocalScore == float.MaxValue)
                {
                    distanceRejected++;
                    continue;
                }

                float normalAlign = math.dot(-triNormal, math.normalizesafe(capsule.tipForward, detectForward));
                if (bestLocalInside == 0)
                {
                    if (bestLocalScore < bestProbeScore)
                    {
                        bestProbeScore = bestLocalScore;
                        bestProbe.triangleIndex = tri;
                        bestProbe.layer = ignoreLayerFilter ? GetTrianglePortalLayer(tri, desiredLayer) : desiredLayer;
                        bestProbe.bary = bestLocalBary;
                        bestProbe.point = bestLocalPoint;
                        bestProbe.attachPoint = bestLocalAttachPoint;
                        bestProbe.normal = triNormal;
                        bestProbe.distance = bestLocalDist;
                        bestProbe.attachT = bestLocalAttachT;
                        bestProbe.depth = bestLocalDepth;
                        bestProbe.advance = sweepAdvance;
                        bestProbe.coneLength = coneLength;
                        bestProbe.coneBaseRadius = coneBaseRadius;
                        bestProbe.coneAxial = bestLocalAxial;
                        bestProbe.coneLateral = bestLocalLateral;
                        bestProbe.coneRadiusAtPoint = bestLocalConeRadius;
                        bestProbe.normalAlign = normalAlign;
                        bestProbe.isEdge = 0;
                    }
                    distanceRejected++;
                    continue;
                }

                float score = bestLocalScore;
                if (score < bestScore)
                {
                    bestScore = score;
                    candidate.triangleIndex = tri;
                    candidate.layer = ignoreLayerFilter ? GetTrianglePortalLayer(tri, desiredLayer) : desiredLayer;
                    candidate.bary = bestLocalBary;
                    candidate.point = bestLocalPoint;
                    candidate.attachPoint = bestLocalAttachPoint;
                    candidate.normal = triNormal;
                    candidate.distance = bestLocalDist;
                    candidate.attachT = bestLocalAttachT;
                    candidate.depth = bestLocalDepth;
                    candidate.advance = sweepAdvance;
                    candidate.coneLength = coneLength;
                    candidate.coneBaseRadius = coneBaseRadius;
                    candidate.coneAxial = bestLocalAxial;
                    candidate.coneLateral = bestLocalLateral;
                    candidate.coneRadiusAtPoint = bestLocalConeRadius;
                    candidate.normalAlign = normalAlign;
                    candidate.isEdge = 0;
                }
            }

            if (bestScore < float.MaxValue)
            {
                failReason = "hit";
                return true;
            }

            if (bestProbe.triangleIndex >= 0)
                candidate = bestProbe;

            failReason = $"miss anyLayer={(ignoreLayerFilter ? 1 : 0)} layerReject={layerRejected} broadReject={broadRejected} distReject={distanceRejected} nearTri={bestProbe.triangleIndex}";
            return false;
        }

        private bool TryFindNeedleTipHookCandidate(
            NeedlePortalWallLayer desiredLayer,
            float3 prevTipCenter,
            float3 sweepStart,
            float3 sweepEnd,
            float3 detectForward,
            CapsuleDesc capsule,
            bool requireDesiredLayer,
            out NeedleTipHookCandidate candidate,
            out string failReason)
        {
            // Tip Piercer 是“尖针接触后先按针轴压入”的预通道语义。
            // 手动穿刺保留跨层自适应；自动计划则必须严格保持指定的首层身份。
            // 常规顺序优先使用三角接触候选，避免未入针时反复全量 cone narrowphase。
            bool hasContactCandidates = _triangleSurfaceContactCandidates.IsCreated && _triangleSurfaceContactCandidates.Length > 0;
            NeedleTipHookCandidate bestProbe = default;
            bestProbe.triangleIndex = -1;
            string bestProbeFail = "not-tested";

            void CaptureProbe(in NeedleTipHookCandidate probe, string reason)
            {
                if (probe.triangleIndex < 0)
                    return;
                if (bestProbe.triangleIndex < 0 || probe.distance < bestProbe.distance)
                {
                    bestProbe = probe;
                    bestProbeFail = reason;
                }
            }

            string strictCandidateFail;
            if (TryFindNeedleTipHookCandidate(desiredLayer, prevTipCenter, sweepStart, sweepEnd, detectForward, capsule, true, false, out candidate, out strictCandidateFail))
            {
                failReason = strictCandidateFail;
                return true;
            }
            CaptureProbe(candidate, strictCandidateFail);

            // 自动缝合计划已经指定了本针的首层。接触候选可能只包含另一层，
            // 所以先在完整网格补查期望层，不能让跨层回退反转针次语义。
            string strictAllFail = "not-tested";
            if (requireDesiredLayer)
            {
                if (TryFindNeedleTipHookCandidate(desiredLayer, prevTipCenter, sweepStart, sweepEnd, detectForward, capsule, false, false, out candidate, out strictAllFail))
                {
                    failReason = "hit-required-layer-all";
                    return true;
                }
                CaptureProbe(candidate, strictAllFail);

                if (bestProbe.triangleIndex >= 0)
                    candidate = bestProbe;
                failReason = $"required-layer-miss desired={FormatNeedlePortalWallLayer(desiredLayer)} candidates=({strictCandidateFail}) all=({strictAllFail})";
                return false;
            }

            string anyCandidateFail;
            if (TryFindNeedleTipHookCandidate(desiredLayer, prevTipCenter, sweepStart, sweepEnd, detectForward, capsule, true, true, out candidate, out anyCandidateFail))
            {
                failReason = "hit-any-layer";
                return true;
            }
            CaptureProbe(candidate, anyCandidateFail);

            string anyAllFail = "not-tested";
            if (!hasContactCandidates &&
                TryFindNeedleTipHookCandidate(desiredLayer, prevTipCenter, sweepStart, sweepEnd, detectForward, capsule, false, true, out candidate, out anyAllFail))
            {
                failReason = "hit-any-layer";
                return true;
            }
            CaptureProbe(candidate, anyAllFail);

            if (bestProbe.triangleIndex >= 0)
            {
                candidate = bestProbe;
                failReason = bestProbeFail;
                return false;
            }

            failReason = hasContactCandidates
                ? (anyCandidateFail != "not-tested" ? anyCandidateFail : strictCandidateFail)
                : (anyAllFail != "not-tested" ? anyAllFail : (anyCandidateFail != "not-tested" ? anyCandidateFail : strictCandidateFail));
            return false;
        }

        private static bool IsNeedleTipHookCandidateLayerAllowed(
            NeedlePortalWallLayer desiredLayer,
            NeedlePortalWallLayer candidateLayer,
            bool requireDesiredLayer)
        {
            return !requireDesiredLayer || candidateLayer == desiredLayer;
        }

        private bool ShouldTriggerWholeWallBreakthrough(CapsuleDesc capsule, NeedlePunctureRuntimeState state, NeedlePortalInfo entryPortal, float firstAxial, float sweepMaxAxial, out float minAdvance)
        {
            minAdvance = 0f;
            if (_setting == null || entryPortal.enabled == 0)
                return false;
            if (state.hasWholeWallBreakthrough != 0)
                return false;
            if (capsule.needleMotionMode == 2)
                return false;

            minAdvance = math.max(_setting.needleInnerPunctureAdvanceThreshold, entryPortal.captureDepth * 0.55f);
            return math.max(firstAxial, sweepMaxAxial) >= minAdvance;
        }

        private bool TryFindNeedlePortalCandidate(
            NeedlePortalWallLayer desiredLayer,
            float3 sweepStart,
            float3 sweepEnd,
            float3 detectForward,
            float effectiveRadius,
            bool candidatesOnly,
            out int bestTri,
            out float3 bestBary,
            out float3 bestPoint,
            out float3 bestNormal)
        {
            bestTri = -1;
            bestBary = default;
            bestPoint = default;
            bestNormal = default;

            var predict = _predictPositions.AsArray();
            var indices = _meshModifier.indices;
            float bestScore = float.MaxValue;
            float sweepLen = math.distance(sweepStart, sweepEnd);
            int sampleCount = math.clamp((int)math.ceil(sweepLen / math.max(effectiveRadius * 0.6f, 0.0006f)) + 1, 2, 8);
            int triCountLocal = triangleCount;

            int candidateCount = candidatesOnly && _triangleSurfaceContactCandidates.IsCreated
                ? _triangleSurfaceContactCandidates.Length
                : triCountLocal;

            if (candidateCount <= 0)
                return false;

            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                int tri = candidatesOnly ? _triangleSurfaceContactCandidates[candidateIndex] : candidateIndex;
                if (!IsTriangleInPortalLayer(tri, desiredLayer))
                    continue;

                int triOffset = tri * 3;
                if (triOffset + 2 >= indices.Length)
                    continue;

                int i0 = indices[triOffset];
                int i1 = indices[triOffset + 1];
                int i2 = indices[triOffset + 2];

                float3 p0 = predict[i0];
                float3 p1 = predict[i1];
                float3 p2 = predict[i2];

                float3 triNormal = math.cross(p1 - p0, p2 - p0);
                float nLenSq = math.lengthsq(triNormal);
                if (nLenSq < 1e-12f)
                    continue;
                triNormal *= math.rsqrt(nLenSq);

                float alignAbs = math.abs(math.dot(triNormal, detectForward));
                if (alignAbs < _setting.needlePunctureNormalBias)
                    continue;

                float bestLocalPenetration = float.MinValue;
                float bestLocalDist = float.MaxValue;
                float3 bestLocalBary = default;
                float3 bestLocalPoint = default;

                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    float t = sampleCount > 1 ? (float)sampleIndex / (sampleCount - 1) : 1f;
                    float3 sampleTip = math.lerp(sweepStart, sweepEnd, t);
                    float3 bary;
                    float3 q = ClosestPointOnTriangleMainThread(sampleTip, p0, p1, p2, out bary);
                    float dist = math.length(sampleTip - q);
                    float penetration = effectiveRadius - dist;
                    if (penetration > bestLocalPenetration ||
                        (penetration >= bestLocalPenetration - 1e-7f && dist < bestLocalDist))
                    {
                        bestLocalPenetration = penetration;
                        bestLocalDist = dist;
                        bestLocalBary = bary;
                        bestLocalPoint = q;
                    }
                }

                if (bestLocalPenetration < _setting.needlePunctureDepthThreshold)
                    continue;

                float score = bestLocalDist - bestLocalPenetration * 0.75f - alignAbs * 0.001f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTri = tri;
                    bestBary = bestLocalBary;
                    bestPoint = bestLocalPoint;
                    bestNormal = triNormal;
                }
            }

            return bestTri >= 0;
        }

        private bool ShouldCloseNeedlePortal(CapsuleDesc capsule, NeedlePortalInfo portal)
        {
            return portal.enabled == 0;
        }

        private bool TryCreateNeedlePortal(ref CapsuleColliderDesc capsuleDesc, in NeedlePunctureRuntimeState state, NeedlePortalWallLayer desiredLayer, out NeedlePortalInfo portal)
        {
            portal = default;
            if (_setting == null || !_setting.enableNeedlePuncture)
                return false;

            var capsule = capsuleDesc.collider;
            if (capsule.hasNeedleTip == 0)
                return false;

            if (desiredLayer == NeedlePortalWallLayer.Inner)
            {
                if (!_hasDoubleLayerSlices) return false;
                if (!_setting.enableNeedleInnerWallPuncture) return false;
            }

            float3 sweepEnd = capsule.tipProbeCenter;
            float3 sweepStart = state.tipHistoryValid != 0 ? state.prevTipProbeCenter : sweepEnd;
            float3 detectForward = GetNeedleSweepForward(sweepStart, sweepEnd, capsule.tipForward);
            float effectiveRadius = math.max(0f, capsule.tipProbeRadius + _setting.triangleSurfaceContactMargin);

            int bestTri;
            float3 bestBary;
            float3 bestPoint;
            float3 bestNormal;

            bool found = TryFindNeedlePortalCandidate(
                desiredLayer,
                sweepStart,
                sweepEnd,
                detectForward,
                effectiveRadius,
                true,
                out bestTri,
                out bestBary,
                out bestPoint,
                out bestNormal);

            if (!found)
            {
                found = TryFindNeedlePortalCandidate(
                    desiredLayer,
                    sweepStart,
                    sweepEnd,
                    detectForward,
                    effectiveRadius,
                    false,
                    out bestTri,
                    out bestBary,
                    out bestPoint,
                    out bestNormal);
            }

            if (!found)
                return false;

            NativeArray<int> indices = _meshModifier.indices;
            int baseIndex = bestTri * 3;
            int v0 = indices[baseIndex];
            int v1 = indices[baseIndex + 1];
            int v2 = indices[baseIndex + 2];

            if (math.dot(bestNormal, detectForward) > 0f)
                bestNormal = -bestNormal;

            portal = new NeedlePortalInfo()
            {
                colliderEntityId = capsuleDesc.entityId,
                triangleIndex = bestTri,
                v0 = v0,
                v1 = v1,
                v2 = v2,
                bary = bestBary,
                entryPoint = bestPoint,
                currentPoint = bestPoint,
                entryNormal = bestNormal,
                insertDirection = detectForward,
                relaxRadius = math.max(_setting.needlePunctureRelaxRadius, capsule.tipProbeRadius * 1.25f),
                captureDepth = math.max(_setting.needlePunctureCaptureDepth, capsule.tipProbeRadius * 0.35f),
                enabled = 1,
                wallLayer = (byte)desiredLayer,
                stitchState = (byte)NeedlePortalStitchState.Current,
                reserved0 = 0,
                reserved1 = 0,
                isProvisional = 0,
                suppressPunctureFx = 0,
                isBreakthroughEvent = 0,
                wallEventOwner = (byte)NeedlePortalWallEventOwner.Unknown,
                entryLayer = (byte)NeedlePortalWallLayer.Unknown,
                breakthroughLayer = (byte)NeedlePortalWallLayer.Unknown,
                passComplete = 0,
            };
            BuildNeedlePortalPatch(ref portal);
            return true;
        }

        private bool TryFindNeedlePortalCandidateRelaxedForWholeWallBreakthrough(
            NeedlePortalWallLayer desiredLayer,
            float3 sweepStart,
            float3 sweepEnd,
            float3 detectForward,
            float searchRadius,
            bool candidatesOnly,
            out int bestTri,
            out float3 bestBary,
            out float3 bestPoint,
            out float3 bestNormal,
            out float bestDistance,
            out float bestNormalAlign,
            out string failReason)
        {
            bestTri = -1;
            bestBary = default;
            bestPoint = default;
            bestNormal = default;
            bestDistance = float.MaxValue;
            bestNormalAlign = 0f;
            failReason = "not-tested";

            NativeArray<float3> predict = _predictPositions.AsArray();
            NativeArray<int> indices = _meshModifier.indices;
            float bestScore = float.MaxValue;
            float nearestRejectedDistance = float.MaxValue;
            float nearestRejectedNormalAlign = 0f;
            float sweepLen = math.distance(sweepStart, sweepEnd);
            int sampleCount = math.clamp((int)math.ceil(sweepLen / math.max(searchRadius * 0.6f, 0.0006f)) + 1, 2, 10);
            int triCountLocal = triangleCount;

            int candidateCount = candidatesOnly && _triangleSurfaceContactCandidates.IsCreated
                ? _triangleSurfaceContactCandidates.Length
                : triCountLocal;

            if (candidateCount <= 0)
            {
                failReason = "no-triangle-candidates";
                return false;
            }

            int layerRejected = 0;
            int distanceRejected = 0;
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                int tri = candidatesOnly ? _triangleSurfaceContactCandidates[candidateIndex] : candidateIndex;
                if (!IsTriangleInPortalLayer(tri, desiredLayer))
                {
                    layerRejected++;
                    continue;
                }

                int triOffset = tri * 3;
                if (triOffset + 2 >= indices.Length)
                    continue;

                int i0 = indices[triOffset];
                int i1 = indices[triOffset + 1];
                int i2 = indices[triOffset + 2];
                if ((uint)i0 >= (uint)predict.Length || (uint)i1 >= (uint)predict.Length || (uint)i2 >= (uint)predict.Length)
                    continue;

                float3 p0 = predict[i0];
                float3 p1 = predict[i1];
                float3 p2 = predict[i2];
                float3 triNormal = math.cross(p1 - p0, p2 - p0);
                float nLenSq = math.lengthsq(triNormal);
                if (nLenSq < 1e-12f)
                    continue;
                triNormal *= math.rsqrt(nLenSq);

                float normalAlign = math.abs(math.dot(triNormal, detectForward));
                float bestLocalDist = float.MaxValue;
                float3 bestLocalBary = default;
                float3 bestLocalPoint = default;

                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    float t = sampleCount > 1 ? (float)sampleIndex / (sampleCount - 1) : 1f;
                    float3 sampleTip = math.lerp(sweepStart, sweepEnd, t);
                    float3 bary;
                    float3 q = ClosestPointOnTriangleMainThread(sampleTip, p0, p1, p2, out bary);
                    float dist = math.length(sampleTip - q);
                    if (dist < bestLocalDist)
                    {
                        bestLocalDist = dist;
                        bestLocalBary = bary;
                        bestLocalPoint = q;
                    }
                }

                if (bestLocalDist < nearestRejectedDistance)
                {
                    nearestRejectedDistance = bestLocalDist;
                    nearestRejectedNormalAlign = normalAlign;
                }

                if (bestLocalDist > searchRadius)
                {
                    distanceRejected++;
                    continue;
                }

                float penetration = searchRadius - bestLocalDist;
                float score = bestLocalDist - penetration * 0.25f - normalAlign * 0.0001f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTri = tri;
                    bestBary = bestLocalBary;
                    bestPoint = bestLocalPoint;
                    bestNormal = triNormal;
                    bestDistance = bestLocalDist;
                    bestNormalAlign = normalAlign;
                }
            }

            if (bestTri >= 0)
            {
                failReason = "hit";
                return true;
            }

            if (nearestRejectedDistance < float.MaxValue)
            {
                bestDistance = nearestRejectedDistance;
                bestNormalAlign = nearestRejectedNormalAlign;
            }
            failReason = $"miss layerReject={layerRejected} distReject={distanceRejected} searchRadius={searchRadius:F5}";
            return false;
        }

        private bool TryCreateNeedlePortalRelaxedForWholeWallBreakthrough(
            ref CapsuleColliderDesc capsuleDesc,
            in NeedlePunctureRuntimeState state,
            NeedlePortalWallLayer desiredLayer,
            NeedlePortalInfo referencePortal,
            out NeedlePortalInfo portal,
            out string failReason,
            out float bestDistance,
            out float bestNormalAlign)
        {
            portal = default;
            failReason = "not-tested";
            bestDistance = float.MaxValue;
            bestNormalAlign = 0f;
            if (_setting == null || !_setting.enableNeedlePuncture)
            {
                failReason = "puncture-disabled";
                return false;
            }

            CapsuleDesc capsule = capsuleDesc.collider;
            if (capsule.hasNeedleTip == 0)
            {
                failReason = "no-needle-tip";
                return false;
            }

            if (desiredLayer == NeedlePortalWallLayer.Inner)
            {
                if (!_hasDoubleLayerSlices)
                {
                    failReason = "inner-no-double-layer";
                    return false;
                }
                if (!_setting.enableNeedleInnerWallPuncture)
                {
                    failReason = "inner-disabled";
                    return false;
                }
            }

            float3 sweepEnd = capsule.tipProbeCenter;
            float3 sweepStart = state.tipHistoryValid != 0 ? state.prevTipProbeCenter : sweepEnd;
            float3 detectForward = GetNeedleSweepForward(sweepStart, sweepEnd, capsule.tipForward);
            float searchRadius = math.max(
                capsule.tipProbeRadius + _setting.triangleSurfaceContactMargin + _setting.needlePunctureDepthThreshold,
                math.max(_setting.needleTipPiercerRadius, math.max(referencePortal.captureDepth, referencePortal.relaxRadius * 0.55f)));

            int bestTri;
            float3 bestBary;
            float3 bestPoint;
            float3 bestNormal;
            string candidateFailReason;

            bool found = TryFindNeedlePortalCandidateRelaxedForWholeWallBreakthrough(
                desiredLayer,
                sweepStart,
                sweepEnd,
                detectForward,
                searchRadius,
                true,
                out bestTri,
                out bestBary,
                out bestPoint,
                out bestNormal,
                out bestDistance,
                out bestNormalAlign,
                out candidateFailReason);

            if (!found)
            {
                found = TryFindNeedlePortalCandidateRelaxedForWholeWallBreakthrough(
                    desiredLayer,
                    sweepStart,
                    sweepEnd,
                    detectForward,
                    searchRadius,
                    false,
                    out bestTri,
                    out bestBary,
                    out bestPoint,
                    out bestNormal,
                    out bestDistance,
                    out bestNormalAlign,
                    out failReason);
            }
            else
            {
                failReason = candidateFailReason;
            }

            if (!found)
            {
                if (failReason == "not-tested")
                    failReason = candidateFailReason;
                return false;
            }

            NativeArray<int> indices = _meshModifier.indices;
            int baseIndex = bestTri * 3;
            int v0 = indices[baseIndex];
            int v1 = indices[baseIndex + 1];
            int v2 = indices[baseIndex + 2];

            if (math.dot(bestNormal, detectForward) > 0f)
                bestNormal = -bestNormal;

            portal = new NeedlePortalInfo()
            {
                colliderEntityId = capsuleDesc.entityId,
                triangleIndex = bestTri,
                v0 = v0,
                v1 = v1,
                v2 = v2,
                bary = bestBary,
                entryPoint = bestPoint,
                currentPoint = bestPoint,
                entryNormal = bestNormal,
                insertDirection = detectForward,
                relaxRadius = math.max(_setting.needlePunctureRelaxRadius, math.max(_setting.needleTipPiercerPatchRadius, capsule.tipProbeRadius * 1.25f)),
                captureDepth = math.max(_setting.needlePunctureCaptureDepth, capsule.tipProbeRadius * 0.35f),
                enabled = 1,
                wallLayer = (byte)desiredLayer,
                stitchState = (byte)NeedlePortalStitchState.Current,
                reserved0 = 0,
                reserved1 = 0,
                isProvisional = 0,
                suppressPunctureFx = 0,
                isBreakthroughEvent = 0,
                wallEventOwner = (byte)NeedlePortalWallEventOwner.Unknown,
                entryLayer = (byte)NeedlePortalWallLayer.Unknown,
                breakthroughLayer = (byte)NeedlePortalWallLayer.Unknown,
                passComplete = 0,
            };
            BuildNeedlePortalPatch(ref portal);
            return true;
        }

        private bool TryCreateNeedlePortalFromHook(ref CapsuleColliderDesc capsuleDesc, in NeedlePunctureRuntimeState state, out NeedlePortalInfo portal)
        {
            portal = default;
            if (_setting == null || !_setting.enableNeedlePuncture || state.hookActive == 0)
                return false;

            NeedlePortalWallLayer desiredLayer = (NeedlePortalWallLayer)state.hookLayer;
            if (desiredLayer != NeedlePortalWallLayer.Outer && desiredLayer != NeedlePortalWallLayer.Inner)
                return false;
            if (desiredLayer == NeedlePortalWallLayer.Inner && (!_hasDoubleLayerSlices || !_setting.enableNeedleInnerWallPuncture))
                return false;

            NativeArray<int> indices = _meshModifier.indices;
            int bestTri = state.hookTriangleIndex;
            int baseIndex = bestTri * 3;
            if (bestTri < 0 || baseIndex + 2 >= indices.Length)
                return false;

            int v0 = indices[baseIndex];
            int v1 = indices[baseIndex + 1];
            int v2 = indices[baseIndex + 2];
            NativeArray<float3> predict = _predictPositions.AsArray();
            if ((uint)v0 >= (uint)predict.Length || (uint)v1 >= (uint)predict.Length || (uint)v2 >= (uint)predict.Length)
                return false;

            CapsuleDesc capsule = capsuleDesc.collider;
            float3 sweepEnd = capsule.tipProbeCenter;
            float3 sweepStart = state.tipHistoryValid != 0 ? state.prevTipProbeCenter : sweepEnd;
            float3 detectForward = GetNeedleSweepForward(sweepStart, sweepEnd, capsule.tipForward);
            float3 hookNormal = math.normalizesafe(state.hookNormal, -detectForward);
            if (math.dot(hookNormal, detectForward) > 0f)
                hookNormal = -hookNormal;

            portal = new NeedlePortalInfo()
            {
                colliderEntityId = capsuleDesc.entityId,
                triangleIndex = bestTri,
                v0 = v0,
                v1 = v1,
                v2 = v2,
                bary = state.hookBary,
                entryPoint = state.hookPoint,
                currentPoint = state.hookPoint,
                entryNormal = hookNormal,
                insertDirection = detectForward,
                relaxRadius = math.max(_setting.needlePunctureRelaxRadius, math.max(_setting.needleTipPiercerPatchRadius, capsule.tipProbeRadius * 1.25f)),
                captureDepth = math.max(_setting.needlePunctureCaptureDepth, capsule.tipProbeRadius * 0.35f),
                enabled = 1,
                wallLayer = (byte)desiredLayer,
                stitchState = (byte)NeedlePortalStitchState.Current,
                reserved0 = 0,
                reserved1 = 0,
                isProvisional = 0,
                suppressPunctureFx = 0,
                isBreakthroughEvent = 0,
                wallEventOwner = (byte)NeedlePortalWallEventOwner.Unknown,
                entryLayer = (byte)NeedlePortalWallLayer.Unknown,
                breakthroughLayer = (byte)NeedlePortalWallLayer.Unknown,
                passComplete = 0,
            };
            BuildNeedlePortalPatch(ref portal);
            return true;
        }

        private bool TryBuildNeedleTipHookSoftConstraint(CapsuleDesc capsule, in NeedlePunctureRuntimeState state, bool preChannelActive, out NeedleTipHookSoftConstraintInfo hook, out float maxWeight, out float normalCorrection, out float tangentCorrection)
        {
            hook = default;
            maxWeight = 0f;
            normalCorrection = 0f;
            tangentCorrection = 0f;
            if (_setting == null || !_setting.enableNeedleTipPiercer || state.hookActive == 0 || _meshModifier == null)
                return false;

            int bestTri = state.hookTriangleIndex;
            NativeArray<int> indices = _meshModifier.indices;
            int baseIndex = bestTri * 3;
            if (bestTri < 0 || baseIndex + 2 >= indices.Length)
                return false;

            NativeArray<float3> predict = _predictPositions.AsArray();
            int seed0 = indices[baseIndex];
            int seed1 = indices[baseIndex + 1];
            int seed2 = indices[baseIndex + 2];
            if ((uint)seed0 >= (uint)predict.Length || (uint)seed1 >= (uint)predict.Length || (uint)seed2 >= (uint)predict.Length)
                return false;

            float coneBaseRadius = GetNeedleTipPiercerConeBaseRadius(capsule);
            float radius = math.max(capsule.radius * 2.5f, math.min(_setting.needleTipPiercerPatchRadius, coneBaseRadius * 4.0f));
            float normalStiffness = math.saturate(_setting.needleTipPiercerNormalStiffness);
            float axialStiffness = math.saturate(_setting.needleTipPiercerAxialStiffness);
            if (radius <= 1e-6f || (normalStiffness <= 0f && axialStiffness <= 0f))
                return false;

            var patchVertices = new List<int>(NeedleTipHookPatchUtil.MaxPatchVertices * 3);
            var seen = new HashSet<int>();
            var firstRing = new HashSet<int>();
            var seedVertices = new HashSet<int>();
            seedVertices.Add(seed0);
            seedVertices.Add(seed1);
            seedVertices.Add(seed2);
            AddNeedleTipHookPatchCandidate(patchVertices, seen, seed0);
            AddNeedleTipHookPatchCandidate(patchVertices, seen, seed1);
            AddNeedleTipHookPatchCandidate(patchVertices, seen, seed2);

            int triCountLocal = triangleCount;
            for (int tri = 0; tri < triCountLocal; tri++)
            {
                int triOffset = tri * 3;
                if (triOffset + 2 >= indices.Length)
                    continue;

                int a = indices[triOffset];
                int b = indices[triOffset + 1];
                int c = indices[triOffset + 2];
                bool sharesSeed = seedVertices.Contains(a) || seedVertices.Contains(b) || seedVertices.Contains(c);
                if (!sharesSeed)
                    continue;

                firstRing.Add(a);
                firstRing.Add(b);
                firstRing.Add(c);
                AddNeedleTipHookPatchCandidate(patchVertices, seen, a);
                AddNeedleTipHookPatchCandidate(patchVertices, seen, b);
                AddNeedleTipHookPatchCandidate(patchVertices, seen, c);
            }

            float3 center = state.hookPoint;
            patchVertices.Sort((a, b) =>
            {
                float da = (uint)a < (uint)predict.Length ? math.lengthsq(predict[a] - center) : float.MaxValue;
                float db = (uint)b < (uint)predict.Length ? math.lengthsq(predict[b] - center) : float.MaxValue;
                return da.CompareTo(db);
            });

            float3 dir = math.normalizesafe(capsule.tipForward, -state.hookNormal);
            float3 normal = math.normalizesafe(state.hookNormal, -dir);
            if (math.dot(normal, dir) > 0f)
                normal = -normal;
            float load01 = EstimateNeedleTipPiercerStateLoad01(state, preChannelActive);
            float normalBite = GetNeedleTipPiercerNormalBite(radius, load01);
            float3 attachPoint = GetNeedleTipPiercerAttachPoint(capsule, state.hookAttachT);
            hook.colliderEntityId = state.colliderEntityId;
            hook.triangleIndex = bestTri;
            hook.hookPoint = state.hookPoint;
            hook.targetPoint = attachPoint + (-normal) * normalBite;
            hook.insertDirection = dir;
            hook.surfaceNormal = normal;
            hook.radius = radius;
            hook.stiffness = math.max(normalStiffness, axialStiffness);
            hook.normalStiffness = normalStiffness;
            hook.tangentialStiffness = axialStiffness;
            hook.enabled = 1;
            hook.wallLayer = state.hookLayer;

            int added = 0;
            for (int i = 0; i < patchVertices.Count && added < NeedleTipHookPatchUtil.MaxPatchVertices; i++)
            {
                int v = patchVertices[i];
                if ((uint)v >= (uint)predict.Length)
                    continue;

                float dist = math.distance(predict[v], center);
                bool seed = v == seed0 || v == seed1 || v == seed2;
                float weight = math.saturate(1f - dist / radius);
                if (seed)
                    weight = math.max(weight, 0.9f);
                else if (firstRing.Contains(v))
                    weight = math.max(weight, 0.22f);

                if (weight <= 0.04f)
                    continue;

                SetNeedleTipHookPatchVertex(ref hook, added, v, weight);
                maxWeight = math.max(maxWeight, weight);
                added++;
            }

            hook.patchCount = added;
            if (added > 0)
            {
                float3 desired = hook.targetPoint - state.hookPoint;
                float axialLen = math.max(0f, math.dot(desired, dir));
                float normalLen = math.max(0f, -math.dot(desired, normal));
                normalCorrection = normalLen * normalStiffness;
                tangentCorrection = axialLen * axialStiffness;
            }
            return added > 0;
        }

        private bool TryBuildNeedleTipConeContactShapingConstraint(
            CapsuleDesc capsule,
            int colliderEntityId,
            NeedleTipHookCandidate candidate,
            bool preChannelActive,
            float punctureLoad01,
            out NeedleTipHookSoftConstraintInfo hook,
            out float maxWeight,
            out float desiredPush,
            out float normalBite)
        {
            hook = default;
            maxWeight = 0f;
            desiredPush = 0f;
            normalBite = 0f;
            if (_setting == null || !_setting.enableNeedleTipPiercer || _meshModifier == null)
                return false;
            if (candidate.triangleIndex < 0)
                return false;

            NativeArray<int> indices = _meshModifier.indices;
            int baseIndex = candidate.triangleIndex * 3;
            if (baseIndex + 2 >= indices.Length)
                return false;

            int seed0 = indices[baseIndex];
            int seed1 = indices[baseIndex + 1];
            int seed2 = indices[baseIndex + 2];
            NativeArray<float3> predict = _predictPositions.AsArray();
            if ((uint)seed0 >= (uint)predict.Length || (uint)seed1 >= (uint)predict.Length || (uint)seed2 >= (uint)predict.Length)
                return false;

            float coneBaseRadius = math.max(candidate.coneBaseRadius, GetNeedleTipPiercerConeBaseRadius(capsule));
            float radius = math.max(capsule.radius * 2.5f, math.min(_setting.needleTipPiercerPatchRadius, coneBaseRadius * 4.0f));
            float normalStiffness = math.saturate(_setting.needleTipPiercerNormalStiffness);
            float axialStiffness = math.saturate(_setting.needleTipPiercerAxialStiffness);
            if (radius <= 1e-6f || (normalStiffness <= 0f && axialStiffness <= 0f))
                return false;

            float fieldWeight = EstimateNeedleTipPiercerFieldWeight(candidate);
            float fieldPush = EstimateNeedleTipPiercerFieldPush(candidate);
            float margin = GetNeedleTipPiercerConeSurfaceMargin(capsule);
            float apex01 = 1f - math.saturate(candidate.coneAxial / math.max(candidate.coneLength, 1e-6f));
            float depthPush = math.max(candidate.depth, margin * (0.55f + 0.45f * apex01));
            float visiblePush = math.min(math.max(0.0008f, radius * 0.18f), 0.004f);

            float axialPush = math.max(fieldPush, depthPush);
            axialPush = math.max(axialPush, visiblePush * math.max(0.25f, fieldWeight));
            if (preChannelActive)
                axialPush = math.max(axialPush, visiblePush * 0.45f);

            float load01 = math.saturate(punctureLoad01);
            normalBite = GetNeedleTipPiercerNormalBite(radius, load01);
            if (preChannelActive)
                normalBite = math.max(normalBite, visiblePush * 0.70f);

            float maxPush = _setting.needleTipPiercerMaxPushPerIteration;
            if (maxPush > 0f)
            {
                axialPush = math.min(axialPush, maxPush);
                normalBite = math.min(normalBite, maxPush);
            }
            if (axialPush <= 1e-6f && normalBite <= 1e-6f)
                return false;

            var patchVertices = new List<int>(NeedleTipHookPatchUtil.MaxPatchVertices * 3);
            var seen = new HashSet<int>();
            var firstRing = new HashSet<int>();
            var seedVertices = new HashSet<int>();
            seedVertices.Add(seed0);
            seedVertices.Add(seed1);
            seedVertices.Add(seed2);
            AddNeedleTipHookPatchCandidate(patchVertices, seen, seed0);
            AddNeedleTipHookPatchCandidate(patchVertices, seen, seed1);
            AddNeedleTipHookPatchCandidate(patchVertices, seen, seed2);

            int triCountLocal = triangleCount;
            for (int tri = 0; tri < triCountLocal; tri++)
            {
                int triOffset = tri * 3;
                if (triOffset + 2 >= indices.Length)
                    continue;

                int a = indices[triOffset];
                int b = indices[triOffset + 1];
                int c = indices[triOffset + 2];
                bool sharesSeed = seedVertices.Contains(a) || seedVertices.Contains(b) || seedVertices.Contains(c);
                if (!sharesSeed)
                    continue;

                firstRing.Add(a);
                firstRing.Add(b);
                firstRing.Add(c);
                AddNeedleTipHookPatchCandidate(patchVertices, seen, a);
                AddNeedleTipHookPatchCandidate(patchVertices, seen, b);
                AddNeedleTipHookPatchCandidate(patchVertices, seen, c);
            }

            float3 center = candidate.point;
            patchVertices.Sort((a, b) =>
            {
                float da = (uint)a < (uint)predict.Length ? math.lengthsq(predict[a] - center) : float.MaxValue;
                float db = (uint)b < (uint)predict.Length ? math.lengthsq(predict[b] - center) : float.MaxValue;
                return da.CompareTo(db);
            });

            float3 dir = math.normalizesafe(capsule.tipForward, -candidate.normal);
            float3 normal = math.normalizesafe(candidate.normal, -dir);
            if (math.dot(normal, dir) > 0f)
                normal = -normal;

            float3 desiredDelta = dir * axialPush + (-normal) * normalBite;
            desiredPush = math.length(desiredDelta);
            if (maxPush > 0f && desiredPush > maxPush)
            {
                float clampScale = maxPush / math.max(desiredPush, 1e-6f);
                desiredDelta *= clampScale;
                normalBite *= clampScale;
                desiredPush = maxPush;
            }

            hook.colliderEntityId = colliderEntityId;
            hook.triangleIndex = candidate.triangleIndex;
            hook.hookPoint = candidate.point;
            hook.targetPoint = candidate.point + desiredDelta;
            hook.insertDirection = dir;
            hook.surfaceNormal = normal;
            hook.radius = radius;
            hook.stiffness = math.max(normalStiffness, axialStiffness);
            hook.normalStiffness = normalStiffness;
            hook.tangentialStiffness = math.max(axialStiffness, 0.85f);
            hook.enabled = 1;
            hook.wallLayer = (byte)candidate.layer;

            float3 bary = candidate.bary;
            float barySum = math.max(1e-6f, math.saturate(bary.x) + math.saturate(bary.y) + math.saturate(bary.z));
            bary = new float3(math.saturate(bary.x) / barySum, math.saturate(bary.y) / barySum, math.saturate(bary.z) / barySum);

            int added = 0;
            for (int i = 0; i < patchVertices.Count && added < NeedleTipHookPatchUtil.MaxPatchVertices; i++)
            {
                int v = patchVertices[i];
                if ((uint)v >= (uint)predict.Length)
                    continue;

                float dist = math.distance(predict[v], center);
                float weight = math.saturate(1f - dist / radius);

                if (v == seed0)
                    weight = math.max(weight, math.max(0.50f, bary.x * 1.85f));
                else if (v == seed1)
                    weight = math.max(weight, math.max(0.50f, bary.y * 1.85f));
                else if (v == seed2)
                    weight = math.max(weight, math.max(0.50f, bary.z * 1.85f));
                else if (firstRing.Contains(v))
                    weight = math.max(weight, 0.18f * math.max(0.35f, fieldWeight));

                if (weight <= 0.04f)
                    continue;

                SetNeedleTipHookPatchVertex(ref hook, added, v, weight);
                maxWeight = math.max(maxWeight, weight);
                added++;
            }

            hook.patchCount = added;
            return added > 0;
        }

        private bool TryAddNeedleTipConeContactShapingConstraint(
            ref NeedlePunctureRuntimeState state,
            CapsuleDesc capsule,
            int colliderEntityId,
            NeedlePortalWallLayer desiredLayer,
            bool preChannelActive,
            float dt)
        {
            if (_setting == null || !_setting.enableNeedleTipPiercer)
                return false;
            if (!_activeNeedleTipHookSoftConstraints.IsCreated)
                return false;
            if (capsule.needleMotionMode == 2)
                return false;

            float3 sweepEnd = capsule.tipProbeCenter;
            float3 sweepStart = state.tipHistoryValid != 0 ? state.prevTipProbeCenter : sweepEnd;
            float3 prevTipCenter = state.tipHistoryValid != 0 ? state.prevTipCenter : capsule.tipCenter;
            float3 detectForward = GetNeedleSweepForward(sweepStart, sweepEnd, capsule.tipForward);

            NeedleTipHookCandidate candidate;
            string failReason;
            bool inside = TryFindNeedleTipHookCandidate(
                desiredLayer,
                prevTipCenter,
                sweepStart,
                sweepEnd,
                detectForward,
                capsule,
                true,
                false,
                out candidate,
                out failReason);

            if (candidate.triangleIndex < 0)
                return false;

            NeedleTipHookSoftConstraintInfo contactConstraint;
            float maxWeight;
            float desiredPush;
            float normalBite;
            float punctureLoad01 = EstimateNeedleTipPiercerPunctureLoad01(state, candidate, preChannelActive);
            if (!TryBuildNeedleTipConeContactShapingConstraint(
                capsule,
                colliderEntityId,
                candidate,
                preChannelActive,
                punctureLoad01,
                out contactConstraint,
                out maxWeight,
                out desiredPush,
                out normalBite))
                return false;

            _activeNeedleTipHookSoftConstraints.Add(contactConstraint);
            float fieldWeight = math.max(EstimateNeedleTipPiercerFieldWeight(candidate), punctureLoad01);
            QueueNeedleTipPiercerFeedback(colliderEntityId, capsule.tipForward, desiredPush, fieldWeight, dt);

            if (state.hookLogCooldown <= 0f)
            {
                state.hookLogCooldown = 0.18f;
                string phase = preChannelActive ? "PreChannel" : "ProbeBeforeEnter";
                Debug.Log($"[Needle][TipConeContactShaping] collider={colliderEntityId} phase={phase} layer={FormatNeedlePortalWallLayer(candidate.layer)} desiredLayer={FormatNeedlePortalWallLayer(desiredLayer)} tri={candidate.triangleIndex} inside={(inside ? 1 : 0)} reason={failReason} coneDist={candidate.distance:F5} axial={candidate.coneAxial:F5} lateral={candidate.coneLateral:F5} coneRadius={candidate.coneRadiusAtPoint:F5} piercerLoad={punctureLoad01:F3} piercerFieldWeight={fieldWeight:F3} shapingPush={desiredPush:F5} normalBite={normalBite:F5} feedbackQueued={(_setting != null && _setting.enableNeedleTipPiercerFeedback ? 1 : 0)} patchCount={contactConstraint.patchCount} maxWeight={maxWeight:F3} hasHook={(state.hookActive != 0 ? 1 : 0)}");
            }

            return true;
        }

        private static void AddNeedleTipHookPatchCandidate(List<int> patchVertices, HashSet<int> seen, int vertex)
        {
            if (vertex < 0 || seen.Contains(vertex))
                return;
            seen.Add(vertex);
            patchVertices.Add(vertex);
        }

        private static void SetNeedleTipHookPatchVertex(ref NeedleTipHookSoftConstraintInfo hook, int slot, int vertex, float weight)
        {
            switch (slot)
            {
                case 0: hook.patchV0 = vertex; hook.patchW0 = weight; break;
                case 1: hook.patchV1 = vertex; hook.patchW1 = weight; break;
                case 2: hook.patchV2 = vertex; hook.patchW2 = weight; break;
                case 3: hook.patchV3 = vertex; hook.patchW3 = weight; break;
                case 4: hook.patchV4 = vertex; hook.patchW4 = weight; break;
                case 5: hook.patchV5 = vertex; hook.patchW5 = weight; break;
                case 6: hook.patchV6 = vertex; hook.patchW6 = weight; break;
                case 7: hook.patchV7 = vertex; hook.patchW7 = weight; break;
                case 8: hook.patchV8 = vertex; hook.patchW8 = weight; break;
                case 9: hook.patchV9 = vertex; hook.patchW9 = weight; break;
                case 10: hook.patchV10 = vertex; hook.patchW10 = weight; break;
                case 11: hook.patchV11 = vertex; hook.patchW11 = weight; break;
            }
        }

        private bool TryUpdateNeedleTipPiercerHold(
            ref NeedlePunctureRuntimeState state,
            CapsuleDesc capsule,
            int colliderEntityId,
            bool preChannelActive,
            out bool readyForPreChannel)
        {
            readyForPreChannel = false;
            if (state.hookActive == 0)
                return false;

            if (capsule.needleMotionMode == 2)
            {
                float retractBreakDistance = _setting.needleTipPiercerBreakDistance > 0f
                    ? _setting.needleTipPiercerBreakDistance
                    : math.max(_setting.needleTipPiercerRadius * 2f, capsule.tipProbeRadius * 2f);
                Debug.Log($"[Needle][TipPiercerBreak] collider={colliderEntityId} layer={FormatNeedlePortalWallLayer((NeedlePortalWallLayer)state.hookLayer)} tri={state.hookTriangleIndex} reason=retracting distance=0 breakDistance={retractBreakDistance:F5}");
                ResetNeedleTipHook(ref state);
                return false;
            }

            float3 currentHookPoint;
            if (!TryRefreshNeedleTipPiercerAnchor(ref state, out currentHookPoint))
            {
                Debug.Log($"[Needle][TipPiercerBreak] collider={colliderEntityId} layer={FormatNeedlePortalWallLayer((NeedlePortalWallLayer)state.hookLayer)} tri={state.hookTriangleIndex} reason=triangle-invalid distance=0 breakDistance=0");
                ResetNeedleTipHook(ref state);
                return false;
            }

            state.hookAdvance = GetNeedleTipHookAdvance(capsule, state);
            state.hookDepth = GetNeedleTipHookDepth(capsule, state);
            float piercerDistance;
            float piercerBreakDistance;
            if (ShouldBreakNeedleTipPiercer(capsule, state, out piercerDistance, out piercerBreakDistance))
            {
                Debug.Log($"[Needle][TipPiercerBreak] collider={colliderEntityId} layer={FormatNeedlePortalWallLayer((NeedlePortalWallLayer)state.hookLayer)} tri={state.hookTriangleIndex} reason=distance distance={piercerDistance:F5} breakDistance={piercerBreakDistance:F5}");
                ResetNeedleTipHook(ref state);
                return false;
            }

            readyForPreChannel = IsNeedleTipHookReadyForPreChannel(capsule, state);

            int patchCount = 0;
            float normalCorrection = 0f;
            float tangentCorrection = 0f;
            float piercerLoad = EstimateNeedleTipPiercerStateLoad01(state, preChannelActive);
            NeedleTipHookSoftConstraintInfo hookConstraint;
            if (TryBuildNeedleTipHookSoftConstraint(capsule, state, preChannelActive, out hookConstraint, out _, out normalCorrection, out tangentCorrection))
            {
                _activeNeedleTipHookSoftConstraints.Add(hookConstraint);
                patchCount = hookConstraint.patchCount;
            }

            if (state.hookLogCooldown <= 0f)
            {
                float3 attachPoint = GetNeedleTipPiercerAttachPoint(capsule, state.hookAttachT);
                float correctionLen = math.sqrt(normalCorrection * normalCorrection + tangentCorrection * tangentCorrection);
                string tag = preChannelActive ? "TipPiercerInsertPreChannel" : "TipPiercerInsert";
                Debug.Log($"[Needle][{tag}] collider={colliderEntityId} layer={FormatNeedlePortalWallLayer((NeedlePortalWallLayer)state.hookLayer)} tri={state.hookTriangleIndex} currentHookPoint=({state.hookPoint.x:F4},{state.hookPoint.y:F4},{state.hookPoint.z:F4}) attachPoint=({attachPoint.x:F4},{attachPoint.y:F4},{attachPoint.z:F4}) correctionLen={correctionLen:F5} normalLen={normalCorrection:F5} axialLen={tangentCorrection:F5} piercerLoad={piercerLoad:F3} patchCount={patchCount} advance={state.hookAdvance:F5} depth={state.hookDepth:F5}");
                state.hookLogCooldown = 0.18f;
            }

            return true;
        }

        private void BuildNeedlePortalPatch(ref NeedlePortalInfo portal)
        {
            portal.patchCount = 0;
            if (_setting == null || portal.enabled == 0 || _meshModifier == null)
                return;

            NativeArray<float3> predict = _predictPositions.AsArray();
            NativeArray<int> indices = _meshModifier.indices;
            if ((uint)portal.v0 >= (uint)predict.Length ||
                (uint)portal.v1 >= (uint)predict.Length ||
                (uint)portal.v2 >= (uint)predict.Length)
                return;

            float radius = math.max(_setting.needleTipPiercerPatchRadius, portal.relaxRadius * 0.85f);
            if (radius <= 1e-6f)
                return;

            var patchVertices = new List<int>(NeedlePortalPatchUtil.MaxPatchVertices * 2);
            var seen = new HashSet<int>();
            AddNeedlePortalPatchCandidate(patchVertices, seen, portal.v0);
            AddNeedlePortalPatchCandidate(patchVertices, seen, portal.v1);
            AddNeedlePortalPatchCandidate(patchVertices, seen, portal.v2);

            int triCountLocal = triangleCount;
            for (int tri = 0; tri < triCountLocal; tri++)
            {
                int triOffset = tri * 3;
                if (triOffset + 2 >= indices.Length)
                    continue;

                int a = indices[triOffset];
                int b = indices[triOffset + 1];
                int c = indices[triOffset + 2];
                bool sharesPortalVertex =
                    a == portal.v0 || a == portal.v1 || a == portal.v2 ||
                    b == portal.v0 || b == portal.v1 || b == portal.v2 ||
                    c == portal.v0 || c == portal.v1 || c == portal.v2;
                if (!sharesPortalVertex)
                    continue;

                AddNeedlePortalPatchCandidate(patchVertices, seen, a);
                AddNeedlePortalPatchCandidate(patchVertices, seen, b);
                AddNeedlePortalPatchCandidate(patchVertices, seen, c);
            }

            float3 center = portal.currentPoint;
            patchVertices.Sort((a, b) =>
            {
                float da = (uint)a < (uint)predict.Length ? math.lengthsq(predict[a] - center) : float.MaxValue;
                float db = (uint)b < (uint)predict.Length ? math.lengthsq(predict[b] - center) : float.MaxValue;
                return da.CompareTo(db);
            });

            int added = 0;
            for (int i = 0; i < patchVertices.Count && added < NeedlePortalPatchUtil.MaxPatchVertices; i++)
            {
                int v = patchVertices[i];
                if ((uint)v >= (uint)predict.Length)
                    continue;

                float dist = math.distance(predict[v], center);
                float weight = math.saturate(1f - dist / radius);
                if (v == portal.v0 || v == portal.v1 || v == portal.v2)
                    weight = math.max(weight, 0.65f);
                if (weight <= 0.04f)
                    continue;

                SetNeedlePortalPatchVertex(ref portal, added, v, weight);
                added++;
            }

            portal.patchCount = added;
            Debug.Log($"[Needle][PortalPatch] collider={portal.colliderEntityId} layer={FormatNeedlePortalWallLayer((NeedlePortalWallLayer)portal.wallLayer)} tri={portal.triangleIndex} patchCount={portal.patchCount} radius={radius:F5} fallbackTriangleOnly={(portal.patchCount <= 3 ? 1 : 0)}");
        }

        private static void AddNeedlePortalPatchCandidate(List<int> patchVertices, HashSet<int> seen, int vertex)
        {
            if (vertex < 0 || seen.Contains(vertex))
                return;
            seen.Add(vertex);
            patchVertices.Add(vertex);
        }

        private static void SetNeedlePortalPatchVertex(ref NeedlePortalInfo portal, int slot, int vertex, float weight)
        {
            switch (slot)
            {
                case 0: portal.patchV0 = vertex; portal.patchW0 = weight; break;
                case 1: portal.patchV1 = vertex; portal.patchW1 = weight; break;
                case 2: portal.patchV2 = vertex; portal.patchW2 = weight; break;
                case 3: portal.patchV3 = vertex; portal.patchW3 = weight; break;
                case 4: portal.patchV4 = vertex; portal.patchW4 = weight; break;
                case 5: portal.patchV5 = vertex; portal.patchW5 = weight; break;
                case 6: portal.patchV6 = vertex; portal.patchW6 = weight; break;
                case 7: portal.patchV7 = vertex; portal.patchW7 = weight; break;
                case 8: portal.patchV8 = vertex; portal.patchW8 = weight; break;
                case 9: portal.patchV9 = vertex; portal.patchW9 = weight; break;
                case 10: portal.patchV10 = vertex; portal.patchW10 = weight; break;
                case 11: portal.patchV11 = vertex; portal.patchW11 = weight; break;
            }
        }

        private bool RefreshNeedlePortalCurrentPoint(ref NeedlePortalInfo portal)
        {
            if (portal.enabled == 0)
                return false;

            var predict = _predictPositions.AsArray();
            if ((uint)portal.v0 >= (uint)predict.Length ||
                (uint)portal.v1 >= (uint)predict.Length ||
                (uint)portal.v2 >= (uint)predict.Length)
                return false;

            float3 p0 = predict[portal.v0];
            float3 p1 = predict[portal.v1];
            float3 p2 = predict[portal.v2];
            portal.currentPoint = p0 * portal.bary.x + p1 * portal.bary.y + p2 * portal.bary.z;
            return true;
        }

        private void RefreshNeedlePuncturePhase(ref NeedlePunctureRuntimeState state, CapsuleDesc capsule)
        {
            bool hasOuter = state.outerPortal.enabled != 0;
            bool hasInner = state.innerPortal.enabled != 0;

            if (!hasOuter && !hasInner)
            {
                state.phase = NeedlePuncturePhase.None;
                return;
            }

            if (hasOuter && !hasInner)
            {
                float outerAxial = GetPortalAxial(capsule, state.outerPortal);
                bool outerOccupied = IsNeedlePortalOccupiedByCapsule(capsule, state.outerPortal);
                if (state.outerPortal.isProvisional != 0)
                {
                    state.phase = (outerOccupied || outerAxial > state.outerPortal.captureDepth)
                        ? NeedlePuncturePhase.PreChannelEntry
                        : NeedlePuncturePhase.WallCompression;
                    return;
                }

                state.phase = (outerOccupied || outerAxial > state.outerPortal.captureDepth)
                    ? NeedlePuncturePhase.InWall
                    : NeedlePuncturePhase.OuterPortal;
                return;
            }

            if (!hasOuter && hasInner)
            {
                float innerOnlyAxial = GetPortalAxial(capsule, state.innerPortal);
                bool innerOnlyOccupied = IsNeedlePortalOccupiedByCapsule(capsule, state.innerPortal);
                if (state.innerPortal.isProvisional != 0)
                {
                    state.phase = (innerOnlyOccupied || innerOnlyAxial > state.innerPortal.captureDepth)
                        ? NeedlePuncturePhase.PreChannelEntry
                        : NeedlePuncturePhase.WallCompression;
                    return;
                }

                state.phase = (innerOnlyOccupied || innerOnlyAxial > state.innerPortal.captureDepth)
                    ? NeedlePuncturePhase.InWall
                    : NeedlePuncturePhase.InnerPortal;
                return;
            }

            if (state.hasWholeWallBreakthrough != 0)
            {
                NeedlePortalInfo exitPortal = state.currentPassIsExit != 0 ? state.outerPortal : state.innerPortal;
                float exitAxial = GetPortalAxial(capsule, exitPortal);
                bool exitOccupied = IsNeedlePortalOccupiedByCapsule(capsule, exitPortal);
                state.phase = (exitOccupied || exitAxial > exitPortal.captureDepth)
                    ? (state.currentPassIsExit != 0 ? NeedlePuncturePhase.WholeWallBreakthrough : NeedlePuncturePhase.InLumen)
                    : NeedlePuncturePhase.WholeWallBreakthrough;
                return;
            }

            float innerAxial = GetPortalAxial(capsule, state.innerPortal);
            bool innerOccupied = IsNeedlePortalOccupiedByCapsule(capsule, state.innerPortal);
            state.phase = (innerOccupied || innerAxial > state.innerPortal.captureDepth)
                ? NeedlePuncturePhase.InLumen
                : NeedlePuncturePhase.InnerPortal;
        }

        private void AddNeedlePortalForRelaxAndConstraint(ref NeedlePunctureRuntimeState state)
        {
            bool hasOuter = state.outerPortal.enabled != 0;
            bool hasInner = state.innerPortal.enabled != 0;

            if (hasOuter)
            {
                state.outerPortal.stitchState = (byte)NeedlePortalStitchState.Current;
                _activeNeedlePortals.Add(state.outerPortal);
                _activeNeedlePortalDisplayInfos.Add(CreateNeedlePortalDisplayInfo(
                    state.outerHistoryIndex,
                    state.outerPortal,
                    true,
                    false,
                    state.currentPassIndex,
                    state.currentPassIsExit != 0,
                    state.entryLayer,
                    state.breakthroughLayer,
                    state.hasWholeWallBreakthrough));
                SyncNeedlePortalHistory(state.outerHistoryIndex, state.outerPortal, true);
                _activeNeedleConstraintPortals.Add(state.outerPortal);
            }

            if (hasInner)
            {
                state.innerPortal.stitchState = (byte)NeedlePortalStitchState.Current;
                _activeNeedlePortals.Add(state.innerPortal);
                _activeNeedlePortalDisplayInfos.Add(CreateNeedlePortalDisplayInfo(
                    state.innerHistoryIndex,
                    state.innerPortal,
                    true,
                    false,
                    state.currentPassIndex,
                    state.currentPassIsExit != 0,
                    state.entryLayer,
                    state.breakthroughLayer,
                    state.hasWholeWallBreakthrough));
                SyncNeedlePortalHistory(state.innerHistoryIndex, state.innerPortal, true);
                _activeNeedleConstraintPortals.Add(state.innerPortal);
            }
        }

        private bool TryCompleteAutoSuturePositionPuncture(
            ref CapsuleColliderDesc capsuleDesc,
            ref NeedlePunctureRuntimeState state,
            NeedleSceneControlRig.AutoSuturePositionPunctureRequest request,
            out string reason)
        {
            reason = "not-ready";
            if (_setting == null || !_setting.enableNeedlePuncture)
            {
                reason = "puncture-disabled";
                return false;
            }

            if (!_hasDoubleLayerSlices || !_setting.enableNeedleInnerWallPuncture)
            {
                reason = "whole-wall-disabled";
                return false;
            }

            if (CurrentPassHasCommittedPortalPair(state))
            {
                reason = "already-committed";
                return true;
            }

            if (IsDuplicateCompletedAutoSuturePositionFinish(state, request))
            {
                reason = "auto-position-duplicate";
                return true;
            }

            bool hasOuter = state.outerPortal.enabled != 0;
            bool hasInner = state.innerPortal.enabled != 0;
            if (!hasOuter && !hasInner)
            {
                NeedlePortalWallLayer requestedEntryLayer = NormalizeAutoSuturePositionEntryLayer(request.entryLayer);
                NeedlePortalInfo entryPortal;
                string entryFailReason;
                if (!TryCreateNeedlePortalFromAutoSuturePath(
                    ref capsuleDesc,
                    request,
                    requestedEntryLayer,
                    out entryPortal,
                    out entryFailReason))
                {
                    reason = $"no-entry-portal {entryFailReason}";
                    return false;
                }

                ClearAutoSuturePositionFinishLatch(ref state);
                state.passCounter += 1;
                state.currentPassIndex = math.max(1, state.passCounter);
                state.currentPassIsExit = (byte)(requestedEntryLayer == NeedlePortalWallLayer.Inner ? 1 : 0);
                state.entryLayer = (byte)requestedEntryLayer;
                state.breakthroughLayer = (byte)GetOppositePortalLayer(requestedEntryLayer);
                state.wallEventOwner = (byte)NeedlePortalWallEventOwner.WholeWall;
                state.hasWholeWallBreakthrough = 0;
                state.outerPortal = default;
                state.innerPortal = default;
                state.outerHistoryIndex = -1;
                state.innerHistoryIndex = -1;
                if (requestedEntryLayer == NeedlePortalWallLayer.Inner)
                    state.innerPortal = entryPortal;
                else
                    state.outerPortal = entryPortal;
                hasOuter = state.outerPortal.enabled != 0;
                hasInner = state.innerPortal.enabled != 0;
            }

            NeedlePortalWallLayer entryLayer = GetCurrentPassFirstPortalLayer(state);
            NeedlePortalWallLayer breakthroughLayer = GetOppositePortalLayer(entryLayer);
            if (entryLayer == NeedlePortalWallLayer.Unknown || breakthroughLayer == NeedlePortalWallLayer.Unknown)
            {
                reason = "unknown-layer";
                return false;
            }

            if (!(hasOuter && hasInner))
            {
                NeedlePortalInfo referencePortal = entryLayer == NeedlePortalWallLayer.Outer
                    ? state.outerPortal
                    : state.innerPortal;
                NeedlePortalInfo breakthroughPortal;
                string failReason;
                float bestDistance;
                float bestNormalAlign;
                if (!TryCreateNeedlePortalRelaxedForWholeWallBreakthrough(
                    ref capsuleDesc,
                    state,
                    breakthroughLayer,
                    referencePortal,
                    out breakthroughPortal,
                    out failReason,
                    out bestDistance,
                    out bestNormalAlign))
                {
                    reason = $"breakthrough-miss {failReason} bestDist={FormatFiniteFloat(bestDistance)} normalAlign={bestNormalAlign:F3}";
                    return false;
                }

                if (breakthroughLayer == NeedlePortalWallLayer.Inner)
                    state.innerPortal = breakthroughPortal;
                else
                    state.outerPortal = breakthroughPortal;

                hasOuter = state.outerPortal.enabled != 0;
                hasInner = state.innerPortal.enabled != 0;
                if (!hasOuter || !hasInner)
                {
                    reason = "created-breakthrough-but-pair-incomplete";
                    return false;
                }
            }

            state.entryLayer = (byte)entryLayer;
            state.breakthroughLayer = (byte)breakthroughLayer;
            state.wallEventOwner = (byte)NeedlePortalWallEventOwner.WholeWall;
            state.hasWholeWallBreakthrough = 1;

            bool outerIsBreakthrough = breakthroughLayer == NeedlePortalWallLayer.Outer;
            bool innerIsBreakthrough = breakthroughLayer == NeedlePortalWallLayer.Inner;
            MarkNeedlePortalWholeWallCommitted(ref state.outerPortal, entryLayer, breakthroughLayer, outerIsBreakthrough);
            MarkNeedlePortalWholeWallCommitted(ref state.innerPortal, entryLayer, breakthroughLayer, innerIsBreakthrough);
            RegisterCommittedWholeWallPortalPair(ref state);
            RememberAutoSuturePositionFinish(ref state, request, entryLayer);
            ResetNeedleTipHook(ref state);
            reason = "auto-position-complete";
            return CurrentPassHasCommittedPortalPair(state);
        }

        private bool TryCreateNeedlePortalFromAutoSuturePath(
            ref CapsuleColliderDesc capsuleDesc,
            NeedleSceneControlRig.AutoSuturePositionPunctureRequest request,
            NeedlePortalWallLayer desiredLayer,
            out NeedlePortalInfo portal,
            out string failReason)
        {
            portal = default;
            failReason = "not-tested";
            if (_setting == null || !_setting.enableNeedlePuncture)
            {
                failReason = "puncture-disabled";
                return false;
            }
            if (!request.hasPath)
            {
                failReason = "request-has-no-path";
                return false;
            }
            if (desiredLayer == NeedlePortalWallLayer.Inner && (!_hasDoubleLayerSlices || !_setting.enableNeedleInnerWallPuncture))
            {
                failReason = "inner-disabled";
                return false;
            }

            CapsuleDesc capsule = capsuleDesc.collider;
            float3 sweepStart = request.entryWorld;
            float3 sweepEnd = request.exitWorld;
            float3 detectForward = math.normalizesafe(sweepEnd - sweepStart, capsule.tipForward);
            if (math.lengthsq(detectForward) < 1e-8f)
            {
                failReason = "path-too-short";
                return false;
            }

            float searchRadius = math.max(
                capsule.tipProbeRadius + _setting.triangleSurfaceContactMargin + _setting.needlePunctureDepthThreshold,
                math.max(_setting.needlePunctureRelaxRadius, _setting.needleTipPiercerRadius));

            int bestTri;
            float3 bestBary;
            float3 bestPoint;
            float3 bestNormal;
            float bestDistance;
            float bestNormalAlign;
            string candidateFailReason;

            bool found = TryFindNeedlePortalCandidateRelaxedForWholeWallBreakthrough(
                desiredLayer,
                sweepStart,
                sweepEnd,
                detectForward,
                searchRadius,
                true,
                out bestTri,
                out bestBary,
                out bestPoint,
                out bestNormal,
                out bestDistance,
                out bestNormalAlign,
                out candidateFailReason);

            if (!found)
            {
                found = TryFindNeedlePortalCandidateRelaxedForWholeWallBreakthrough(
                    desiredLayer,
                    sweepStart,
                    sweepEnd,
                    detectForward,
                    searchRadius,
                    false,
                    out bestTri,
                    out bestBary,
                    out bestPoint,
                    out bestNormal,
                    out bestDistance,
                    out bestNormalAlign,
                    out failReason);
            }
            else
            {
                failReason = candidateFailReason;
            }

            if (!found)
            {
                if (failReason == "not-tested")
                    failReason = candidateFailReason;
                failReason = $"{failReason} bestDist={FormatFiniteFloat(bestDistance)} normalAlign={bestNormalAlign:F3}";
                return false;
            }

            NativeArray<int> indices = _meshModifier.indices;
            int baseIndex = bestTri * 3;
            int v0 = indices[baseIndex];
            int v1 = indices[baseIndex + 1];
            int v2 = indices[baseIndex + 2];

            if (math.dot(bestNormal, detectForward) > 0f)
                bestNormal = -bestNormal;

            portal = new NeedlePortalInfo()
            {
                colliderEntityId = capsuleDesc.entityId,
                triangleIndex = bestTri,
                v0 = v0,
                v1 = v1,
                v2 = v2,
                bary = bestBary,
                entryPoint = bestPoint,
                currentPoint = bestPoint,
                entryNormal = bestNormal,
                insertDirection = detectForward,
                relaxRadius = math.max(_setting.needlePunctureRelaxRadius, capsule.tipProbeRadius * 1.25f),
                captureDepth = math.max(_setting.needlePunctureCaptureDepth, capsule.tipProbeRadius * 0.35f),
                enabled = 1,
                wallLayer = (byte)desiredLayer,
                stitchState = (byte)NeedlePortalStitchState.Current,
                reserved0 = 0,
                reserved1 = 0,
                isProvisional = 0,
                suppressPunctureFx = 0,
                isBreakthroughEvent = 0,
                wallEventOwner = (byte)NeedlePortalWallEventOwner.WholeWall,
                entryLayer = (byte)desiredLayer,
                breakthroughLayer = (byte)GetOppositePortalLayer(desiredLayer),
                passComplete = 0,
            };
            BuildNeedlePortalPatch(ref portal);
            failReason = "hit";
            return true;
        }

        private void UpdateNeedlePunctureStateMainThread(float dt)
        {
            if (!_activeNeedlePortals.IsCreated || !_activeNeedleConstraintPortals.IsCreated)
                return;

            _activeNeedlePortals.Clear();
            _activeNeedleConstraintPortals.Clear();
            if (_activeNeedleTipHookSoftConstraints.IsCreated)
                _activeNeedleTipHookSoftConstraints.Clear();
            if (_retainedHistoryHoldPortals.IsCreated)
                _retainedHistoryHoldPortals.Clear();
            _activeNeedlePortalDisplayInfos.Clear();
            ResetNeedlePortalHistoryActivity();

            if (_setting == null || !_setting.enableNeedlePuncture)
            {
                _needlePunctureStates.Clear();
                _activeNeedlePortalDisplayInfos.Clear();
                return;
            }

            var keys = new List<int>(_needlePunctureStates.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var state = _needlePunctureStates[keys[i]];
                state.cooldown = math.max(0f, state.cooldown - dt);
                state.hookLogCooldown = math.max(0f, state.hookLogCooldown - dt);
                state.lastAutoSuturePositionDuplicateCooldown = math.max(0f, state.lastAutoSuturePositionDuplicateCooldown - dt);
                _needlePunctureStates[keys[i]] = state;
            }

            var capsules = _colliderDescGroups.capsules;
            for (int capsuleIndex = 0; capsuleIndex < capsules.Length; capsuleIndex++)
            {
                var capsuleDesc = capsules[capsuleIndex];
                var capsule = capsuleDesc.collider;
                if (capsule.hasNeedleTip == 0)
                    continue;

                NeedlePunctureRuntimeState state;
                if (!_needlePunctureStates.TryGetValue(capsuleDesc.entityId, out state))
                {
                    state = new NeedlePunctureRuntimeState()
                    {
                        colliderEntityId = capsuleDesc.entityId,
                        phase = NeedlePuncturePhase.None,
                        cooldown = 0f,
                        outerPortal = default,
                        innerPortal = default,
                        passCounter = 0,
                        currentPassIndex = 0,
                        outerHistoryIndex = -1,
                        innerHistoryIndex = -1,
                        prevTipCenter = capsule.tipCenter,
                        prevTipProbeCenter = capsule.tipProbeCenter,
                        prevTipForward = capsule.tipForward,
                        prevTailCenter = capsule.tailCenter,
                        punctureFinished = 0,
                        needleInsideLumen = 0,
                        currentPassIsExit = 0,
                        tipHistoryValid = 0,
                        reserved0 = 0,
                        entryLayer = (byte)NeedlePortalWallLayer.Unknown,
                        breakthroughLayer = (byte)NeedlePortalWallLayer.Unknown,
                        wallEventOwner = (byte)NeedlePortalWallEventOwner.Unknown,
                        lastAutoSuturePositionFinishValid = 0,
                        lastAutoSuturePositionEntryLayer = (byte)NeedlePortalWallLayer.Unknown,
                        lastAutoSuturePositionPassIndex = 0,
                        lastAutoSuturePositionDuplicateCooldown = 0f,
                        lastAutoSuturePositionEntryWorld = default,
                        lastAutoSuturePositionExitWorld = default,
                        hasWholeWallBreakthrough = 0,
                        hookActive = 0,
                        hookLayer = (byte)NeedlePortalWallLayer.Unknown,
                        hookIsEdge = 0,
                        hookTriangleIndex = -1,
                        hookBary = default,
                        hookPoint = default,
                        hookNormal = default,
                        hookStartTipProbeCenter = capsule.tipProbeCenter,
                        hookAttachT = 1f,
                        hookAdvance = 0f,
                        hookDepth = 0f,
                        hookLogCooldown = 0f,
                    };
                }

                bool outerValid = false;
                if (state.outerPortal.enabled != 0)
                {
                    outerValid = RefreshNeedlePortalCurrentPoint(ref state.outerPortal);
                    if (!outerValid)
                    {
                        DisableNeedlePortalHistoryRetention(state.outerHistoryIndex);
                        state.outerPortal = default;
                        state.outerHistoryIndex = -1;
                        if (state.innerPortal.enabled == 0)
                        {
                            ResetNeedleCurrentPassSemantics(ref state);
                        }
                        state.cooldown = 0f;
                    }
                }

                bool innerValid = false;
                if (state.innerPortal.enabled != 0)
                {
                    innerValid = RefreshNeedlePortalCurrentPoint(ref state.innerPortal);
                    if (!innerValid)
                    {
                        DisableNeedlePortalHistoryRetention(state.innerHistoryIndex);
                        state.innerPortal = default;
                        state.innerHistoryIndex = -1;
                        if (state.outerPortal.enabled == 0)
                        {
                            ResetNeedleCurrentPassSemantics(ref state);
                        }
                        state.cooldown = 0f;
                    }
                }

                bool skipCreateThisFrame = false;
                NeedleSceneControlRig.AutoSuturePositionPunctureRequest ignoredAutoPositionRequest;
                if (NeedleSceneControlRig.ConsumeAutoSuturePositionFinishPunctureRequest(
                    capsuleDesc.entityId,
                    out ignoredAutoPositionRequest))
                {
                    Debug.LogWarning(
                        $"[Needle][AutoPositionFinishIgnored] collider={capsuleDesc.entityId} " +
                        "reason=real-contact-required");
                }

                if (!skipCreateThisFrame && NeedleSceneControlRig.ConsumeFinishPunctureRequest(capsuleDesc.entityId))
                {
                    // “收针”只结束当前穿针流程，不自动打结。
                    PromoteCurrentPortalPairToHistoryRelax(ref state, true);
                    skipCreateThisFrame = true;
                }

                NeedleSceneControlRig.AutoSuturePullLineRequest autoSuturePullLineRequest;
                bool hasAutoSuturePullLineRequest = NeedleSceneControlRig.ConsumeAutoSuturePullLineRequest(
                    capsuleDesc.entityId,
                    out autoSuturePullLineRequest);
                if (hasAutoSuturePullLineRequest)
                {
                    if (CurrentPassHasCommittedPortalPair(state))
                    {
                        Debug.LogWarning(
                            $"[Suture][AutoPullLineCurrentBlocked] collider={capsuleDesc.entityId} " +
                            $"pass={state.currentPassIndex} passA={autoSuturePullLineRequest.passA} " +
                            $"passB={autoSuturePullLineRequest.passB} reason=tail-not-hist");
                    }
                    else if (state.outerPortal.enabled != 0 || state.innerPortal.enabled != 0)
                    {
                        Debug.LogWarning($"[Suture][AutoPullLineNoPair] 当前穿刺还没有形成完整外/内壁孔位对，未把 Current 转 Hist。collider={capsuleDesc.entityId} passA={autoSuturePullLineRequest.passA} passB={autoSuturePullLineRequest.passB}");
                    }
                    else
                    {
                        TryPullExactHistSutureLineForCollider(
                            capsuleDesc.entityId,
                            autoSuturePullLineRequest.passA,
                            autoSuturePullLineRequest.passB,
                            autoSuturePullLineRequest.strongPullLine);
                    }
                    skipCreateThisFrame = true;
                }

                bool strongPullLineRequest = !hasAutoSuturePullLineRequest && NeedleSceneControlRig.ConsumeStrongPullLineRequest(capsuleDesc.entityId);
                bool pullLineRequest = !hasAutoSuturePullLineRequest && NeedleSceneControlRig.ConsumePullLineRequest(capsuleDesc.entityId);
                if (strongPullLineRequest || pullLineRequest)
                {
                    // “拉线”由用户手动触发：完整 Current 先转 Hist，再按相邻 Hist pass 连续建立缝线段。
                    if (CurrentPassHasCommittedPortalPair(state))
                    {
                        PromoteCurrentPortalPairToHistoryRelax(ref state, false);
                    }
                    else if (state.outerPortal.enabled != 0 || state.innerPortal.enabled != 0)
                    {
                        Debug.LogWarning($"[Suture][PullLineNoPair] 当前穿刺还没有形成完整外/内壁孔位对，未把 Current 转 Hist。collider={capsuleDesc.entityId}");
                    }

                    TryPullNextHistSutureLineForCollider(capsuleDesc.entityId, strongPullLineRequest);
                    skipCreateThisFrame = true;
                }

                if (NeedleSceneControlRig.ConsumeReleaseRetainedPortalRequest(capsuleDesc.entityId))
                {
                    // 兼容旧入口：只释放保留孔位，不自动选择最近 Hist 组打结。
                    PromoteCurrentPortalPairToHistoryRelax(ref state, true);
                    DisableNeedlePortalHistoryRetentionForCollider(capsuleDesc.entityId);
                    skipCreateThisFrame = true;
                }

                if (!skipCreateThisFrame && state.punctureFinished == 0 && CurrentPassHasCommittedPortalPair(state) && ShouldPromoteCurrentPortalPairToHistory(capsule, state))
                {
                    PromoteCurrentPortalPairToHistoryRelax(ref state, false);
                }

                bool hasOuterPortal = state.outerPortal.enabled != 0;
                bool hasInnerPortal = state.innerPortal.enabled != 0;
                bool hasAnyCurrentPortal = hasOuterPortal || hasInnerPortal;
                bool hasPreChannelPortal =
                    (state.outerPortal.enabled != 0 && state.outerPortal.isProvisional != 0) ||
                    (state.innerPortal.enabled != 0 && state.innerPortal.isProvisional != 0);
                bool tipPiercerHoldUpdatedThisFrame = false;
                bool autoSuturePoseLocked = NeedleSceneControlRig.IsAutoSuturePositionPoseLocked(capsuleDesc.entityId);
                bool autoSuturePunctureContactAllowed =
                    NeedleSceneControlRig.IsAutoSuturePositionPunctureContactAllowed(capsuleDesc.entityId);
                bool suppressImplicitPunctureForAutoSuture =
                    autoSuturePoseLocked && !autoSuturePunctureContactAllowed;

                if (!skipCreateThisFrame &&
                    !suppressImplicitPunctureForAutoSuture &&
                    state.punctureFinished == 0 &&
                    state.hasWholeWallBreakthrough == 0 &&
                    _setting.enableNeedleTipPiercer)
                {
                    NeedlePortalWallLayer shapingLayer = state.hookActive != 0
                        ? (NeedlePortalWallLayer)state.hookLayer
                        : (hasAnyCurrentPortal ? GetCurrentPassFirstPortalLayer(state) : GetNextPassFirstPortalLayer(state));
                    TryAddNeedleTipConeContactShapingConstraint(
                        ref state,
                        capsule,
                        capsuleDesc.entityId,
                        shapingLayer,
                        hasPreChannelPortal,
                        dt);
                }

                if (!skipCreateThisFrame && !suppressImplicitPunctureForAutoSuture && state.punctureFinished == 0 && !hasAnyCurrentPortal)
                {
                    bool useWholeWallSemantic = _hasDoubleLayerSlices && _setting.enableNeedleInnerWallPuncture;
                    NeedlePortalWallLayer firstLayer = GetNextPassFirstPortalLayer(state);
                    bool hadTipPiercerAtFrameStart = state.hookActive != 0;
                    bool hookCandidateHit = false;
                    if (_setting.enableNeedleTipPiercer && state.hookActive == 0)
                    {
                        float3 hookSweepEnd = capsule.tipProbeCenter;
                        float3 hookSweepStart = state.tipHistoryValid != 0 ? state.prevTipProbeCenter : hookSweepEnd;
                        float3 hookPrevTipCenter = state.tipHistoryValid != 0 ? state.prevTipCenter : capsule.tipCenter;
                        float3 hookForward = GetNeedleSweepForward(hookSweepStart, hookSweepEnd, capsule.tipForward);
                        NeedleTipHookCandidate hookCandidate;
                        string hookFailReason;
                        bool requireDesiredHookLayer = autoSuturePoseLocked;
                        bool hookHit = TryFindNeedleTipHookCandidate(
                            firstLayer,
                            hookPrevTipCenter,
                            hookSweepStart,
                            hookSweepEnd,
                            hookForward,
                            capsule,
                            requireDesiredHookLayer,
                            out hookCandidate,
                            out hookFailReason);
                        if (hookHit && !IsNeedleTipHookCandidateLayerAllowed(firstLayer, hookCandidate.layer, requireDesiredHookLayer))
                        {
                            Debug.LogError($"[Needle][AutoSutureHookLayerRejected] collider={capsuleDesc.entityId} desiredLayer={FormatNeedlePortalWallLayer(firstLayer)} candidateLayer={FormatNeedlePortalWallLayer(hookCandidate.layer)} tri={hookCandidate.triangleIndex}");
                            hookHit = false;
                            hookFailReason = "required-layer-rejected";
                        }
                        hookCandidateHit = hookHit;
                        if (!hookHit && hookCandidate.triangleIndex >= 0 && state.hookLogCooldown <= 0f)
                        {
                            state.hookLogCooldown = 0.18f;
                            float piercerWeight = EstimateNeedleTipPiercerFieldWeight(hookCandidate);
                            float piercerPush = EstimateNeedleTipPiercerFieldPush(hookCandidate);
                            Debug.Log($"[Needle][TipPiercerObliqueProbe] collider={capsuleDesc.entityId} phase=ProbeBeforeEnter desiredLayer={FormatNeedlePortalWallLayer(firstLayer)} nearLayer={FormatNeedlePortalWallLayer(hookCandidate.layer)} tri={hookCandidate.triangleIndex} reason={hookFailReason} candidateCount={(_triangleSurfaceContactCandidates.IsCreated ? _triangleSurfaceContactCandidates.Length : 0)} shaftCollision=1 tipRepulsionSkipped=1 coneDist={hookCandidate.distance:F5} axial={hookCandidate.coneAxial:F5} lateral={hookCandidate.coneLateral:F5} coneRadius={hookCandidate.coneRadiusAtPoint:F5} normalAlign={hookCandidate.normalAlign:F3} advance={hookCandidate.advance:F5} piercerFieldWeight={piercerWeight:F3} piercerPush={piercerPush:F5}");
                        }
                        else if (!hookHit && autoSuturePoseLocked && state.hookLogCooldown <= 0f)
                        {
                            state.hookLogCooldown = 0.18f;
                            int candidateCount = _triangleSurfaceContactCandidates.IsCreated
                                ? _triangleSurfaceContactCandidates.Length
                                : 0;
                            Debug.Log(
                                $"[Needle][AutoSutureTipPiercerMiss] collider={capsuleDesc.entityId} " +
                                $"desiredLayer={FormatNeedlePortalWallLayer(firstLayer)} reason={hookFailReason} " +
                                $"candidateCount={candidateCount} poseLocked=1 punctureContactAllowed={(autoSuturePunctureContactAllowed ? 1 : 0)} " +
                                $"tip=({capsule.tipCenter.x:F4},{capsule.tipCenter.y:F4},{capsule.tipCenter.z:F4}) " +
                                $"probe=({capsule.tipProbeCenter.x:F4},{capsule.tipProbeCenter.y:F4},{capsule.tipProbeCenter.z:F4}) " +
                                $"sweepStart=({hookSweepStart.x:F4},{hookSweepStart.y:F4},{hookSweepStart.z:F4}) " +
                                $"sweepEnd=({hookSweepEnd.x:F4},{hookSweepEnd.y:F4},{hookSweepEnd.z:F4})");
                        }
                        if (hookHit)
                        {
                            state.hookActive = 1;
                            state.hookLayer = (byte)hookCandidate.layer;
                            state.hookIsEdge = hookCandidate.isEdge;
                            state.hookTriangleIndex = hookCandidate.triangleIndex;
                            state.hookBary = hookCandidate.bary;
                            state.hookPoint = hookCandidate.point;
                            state.hookNormal = hookCandidate.normal;
                            state.hookStartTipProbeCenter = hookCandidate.attachPoint;
                            state.hookAttachT = hookCandidate.attachT;
                            state.hookAdvance = hookCandidate.advance;
                            state.hookDepth = hookCandidate.depth;
                            state.hookLogCooldown = 0f;
                            float piercerWeight = EstimateNeedleTipPiercerFieldWeight(hookCandidate);
                            float piercerPush = EstimateNeedleTipPiercerFieldPush(hookCandidate);
                            Debug.Log($"[Needle][TipPiercerEnter] collider={capsuleDesc.entityId} layer={FormatNeedlePortalWallLayer(hookCandidate.layer)} tri={hookCandidate.triangleIndex} bary=({hookCandidate.bary.x:F3},{hookCandidate.bary.y:F3},{hookCandidate.bary.z:F3}) shaftCollision=1 tipRepulsionSkipped=1 coneDist={hookCandidate.distance:F5} coneDepth={hookCandidate.depth:F5} coneLength={hookCandidate.coneLength:F5} coneBaseRadius={hookCandidate.coneBaseRadius:F5} axial={hookCandidate.coneAxial:F5} lateral={hookCandidate.coneLateral:F5} coneRadius={hookCandidate.coneRadiusAtPoint:F5} normalAlign={hookCandidate.normalAlign:F3} piercerFieldWeight={piercerWeight:F3} piercerPush={piercerPush:F5}");
                        }
                    }

                    NeedlePortalInfo firstPortal = default;
                    bool hasFirstPortal = false;
                    bool hookReadyForPreChannel = false;
                    if (state.hookActive != 0)
                    {
                        tipPiercerHoldUpdatedThisFrame = TryUpdateNeedleTipPiercerHold(
                            ref state,
                            capsule,
                            capsuleDesc.entityId,
                            false,
                            out hookReadyForPreChannel);
                    }

                    if (hookReadyForPreChannel &&
                        state.hookActive != 0 &&
                        TryCreateNeedlePortalFromHook(ref capsuleDesc, state, out firstPortal))
                    {
                        firstLayer = (NeedlePortalWallLayer)state.hookLayer;
                        hasFirstPortal = true;
                        Debug.Log($"[Needle][TipPiercerPreChannelStart] collider={capsuleDesc.entityId} layer={FormatNeedlePortalWallLayer(firstLayer)} tri={state.hookTriangleIndex} advance={state.hookAdvance:F5} depth={state.hookDepth:F5}");
                    }

                    bool blockOldPortalFallback = _setting.enableNeedleTipPiercer &&
                        !hasFirstPortal &&
                        (hookCandidateHit || state.hookActive != 0 || hadTipPiercerAtFrameStart);

                    if (!hasFirstPortal && !blockOldPortalFallback)
                        hasFirstPortal = TryCreateNeedlePortal(ref capsuleDesc, state, firstLayer, out firstPortal);

                    if (hasFirstPortal)
                    {
                        ClearAutoSuturePositionFinishLatch(ref state);
                        state.passCounter += 1;
                        state.currentPassIndex = math.max(1, state.passCounter);
                        state.currentPassIsExit = (byte)(firstLayer == NeedlePortalWallLayer.Inner ? 1 : 0);
                        state.entryLayer = (byte)firstLayer;
                        state.breakthroughLayer = (byte)(useWholeWallSemantic ? GetOppositePortalLayer(firstLayer) : NeedlePortalWallLayer.Unknown);
                        state.wallEventOwner = (byte)(useWholeWallSemantic ? NeedlePortalWallEventOwner.WholeWall : NeedlePortalWallEventOwner.Unknown);
                        state.hasWholeWallBreakthrough = 0;
                        state.outerPortal = default;
                        state.innerPortal = default;
                        state.outerHistoryIndex = -1;
                        state.innerHistoryIndex = -1;

                        if (useWholeWallSemantic)
                            MarkNeedlePortalPreChannel(ref firstPortal, firstLayer, (NeedlePortalWallLayer)state.breakthroughLayer);

                        if (firstLayer == NeedlePortalWallLayer.Outer)
                        {
                            state.outerPortal = firstPortal;
                            if (!useWholeWallSemantic)
                                state.outerHistoryIndex = RegisterNeedlePortalHistory(firstPortal, state.currentPassIndex, false, 0);
                        }
                        else
                        {
                            state.innerPortal = firstPortal;
                            if (!useWholeWallSemantic)
                                state.innerHistoryIndex = RegisterNeedlePortalHistory(firstPortal, state.currentPassIndex, true, 0);
                        }

                        hasOuterPortal = state.outerPortal.enabled != 0;
                        hasInnerPortal = state.innerPortal.enabled != 0;
                        hasAnyCurrentPortal = hasOuterPortal || hasInnerPortal;
                        hasPreChannelPortal =
                            (state.outerPortal.enabled != 0 && state.outerPortal.isProvisional != 0) ||
                            (state.innerPortal.enabled != 0 && state.innerPortal.isProvisional != 0);
                    }
                }

                if (!skipCreateThisFrame &&
                    !suppressImplicitPunctureForAutoSuture &&
                    state.punctureFinished == 0 &&
                    !tipPiercerHoldUpdatedThisFrame &&
                    state.hookActive != 0 &&
                    state.hasWholeWallBreakthrough == 0)
                {
                    bool ignoredReadyForPreChannel;
                    tipPiercerHoldUpdatedThisFrame = TryUpdateNeedleTipPiercerHold(
                        ref state,
                        capsule,
                        capsuleDesc.entityId,
                        hasPreChannelPortal,
                        out ignoredReadyForPreChannel);
                }

                if (!suppressImplicitPunctureForAutoSuture &&
                    state.punctureFinished == 0 &&
                    hasAnyCurrentPortal &&
                    !(hasOuterPortal && hasInnerPortal) &&
                    _setting.enableNeedleInnerWallPuncture &&
                    _hasDoubleLayerSlices)
                {
                    NeedlePortalWallLayer firstLayer = GetCurrentPassFirstPortalLayer(state);
                    NeedlePortalInfo referencePortal = firstLayer == NeedlePortalWallLayer.Outer ? state.outerPortal : state.innerPortal;
                    NeedlePortalWallLayer secondLayer = firstLayer == NeedlePortalWallLayer.Outer
                        ? NeedlePortalWallLayer.Inner
                        : NeedlePortalWallLayer.Outer;

                    float firstAxial = GetPortalAxial(capsule, referencePortal);
                    float sweepMaxAxial = state.tipHistoryValid != 0
                        ? GetMaxPortalAxialOnSweep(state.prevTipProbeCenter, capsule.tipProbeCenter, referencePortal)
                        : firstAxial;
                    float minAdvance;
                    if (ShouldTriggerWholeWallBreakthrough(capsule, state, referencePortal, firstAxial, sweepMaxAxial, out minAdvance))
                    {
                        NeedlePortalInfo secondPortal;
                        if (TryCreateNeedlePortalRelaxedForWholeWallBreakthrough(
                            ref capsuleDesc,
                            state,
                            secondLayer,
                            referencePortal,
                            out secondPortal,
                            out _,
                            out _,
                            out _))
                        {
                            NeedlePortalWallLayer entryLayer = firstLayer;
                            NeedlePortalWallLayer breakthroughLayer = secondLayer;

                            if (secondLayer == NeedlePortalWallLayer.Inner)
                            {
                                state.innerPortal = secondPortal;
                            }
                            else
                            {
                                state.outerPortal = secondPortal;
                            }

                            state.entryLayer = (byte)entryLayer;
                            state.breakthroughLayer = (byte)breakthroughLayer;
                            state.wallEventOwner = (byte)NeedlePortalWallEventOwner.WholeWall;
                            state.hasWholeWallBreakthrough = 1;

                            bool outerIsBreakthrough = breakthroughLayer == NeedlePortalWallLayer.Outer;
                            bool innerIsBreakthrough = breakthroughLayer == NeedlePortalWallLayer.Inner;
                            MarkNeedlePortalWholeWallCommitted(ref state.outerPortal, entryLayer, breakthroughLayer, outerIsBreakthrough);
                            MarkNeedlePortalWholeWallCommitted(ref state.innerPortal, entryLayer, breakthroughLayer, innerIsBreakthrough);
                            RegisterCommittedWholeWallPortalPair(ref state);

                            Debug.Log($"[Needle][WholeWallBreakthrough] collider={capsuleDesc.entityId} pass={state.currentPassIndex} entry={FormatNeedlePortalWallLayer(entryLayer)} breakthrough={FormatNeedlePortalWallLayer(breakthroughLayer)} eventOwner=WholeWall firstAxial={firstAxial:F5} sweepMaxAxial={sweepMaxAxial:F5} minAdvance={minAdvance:F5} outerCommitted={(state.outerHistoryIndex >= 0 ? 1 : 0)} innerCommitted={(state.innerHistoryIndex >= 0 ? 1 : 0)} suppressEntryFx=1");

                            if (state.hookActive != 0)
                            {
                                float3 attachPoint = GetNeedleTipPiercerAttachPoint(capsule, state.hookAttachT);
                                float releaseDistance = math.distance(attachPoint, state.hookPoint);
                                float releaseBreakDistance = _setting.needleTipPiercerBreakDistance > 0f
                                    ? _setting.needleTipPiercerBreakDistance
                                    : math.max(_setting.needleTipPiercerRadius * 2f, capsule.tipProbeRadius * 2f);
                                Debug.Log($"[Needle][TipPiercerReleaseAfterBreakthrough] collider={capsuleDesc.entityId} layer={FormatNeedlePortalWallLayer((NeedlePortalWallLayer)state.hookLayer)} tri={state.hookTriangleIndex} distance={releaseDistance:F5} breakDistance={releaseBreakDistance:F5}");
                                ResetNeedleTipHook(ref state);
                            }

                            hasOuterPortal = state.outerPortal.enabled != 0;
                            hasInnerPortal = state.innerPortal.enabled != 0;
                            hasAnyCurrentPortal = hasOuterPortal || hasInnerPortal;
                        }
                    }
                }

                RefreshNeedlePuncturePhase(ref state, capsule);
                AddNeedlePortalForRelaxAndConstraint(ref state);

                state.prevTipCenter = capsule.tipCenter;
                state.prevTipProbeCenter = capsule.tipProbeCenter;
                state.prevTipForward = capsule.tipForward;
                state.prevTailCenter = capsule.tailCenter;
                state.tipHistoryValid = 1;
                _needlePunctureStates[capsuleDesc.entityId] = state;
            }

            AddRetainedHistoryPortalsForHoldDisplay();
        }


        private void UpdateSutureLineConstraintProgressMainThread(float dt)
        {
            if (_setting == null || !_sutureLineConstraints.IsCreated || _sutureLineConstraints.Length == 0)
                return;

            float tightenSpeed = math.max(0f, _setting.sutureLineTightenSpeed);
            float approachTightenScale = math.max(0f, _setting.sutureLineApproachTightenSpeedScale);
            float latchedTightenScale = math.max(0f, _setting.sutureLineLatchedTightenSpeedScale);
            float lambdaResetDelta = math.max(0f, _setting.sutureLineLambdaResetRestDelta);
            float lambdaDecay = math.saturate(_setting.sutureLineLambdaDecay);
            float contactGap = math.max(0f, _setting.sutureLineContactGap);
            float contactStiffness = math.saturate(_setting.sutureLineContactStiffness);
            float contactMaxPush = math.max(0f, _setting.sutureLineContactMaxPushPerIteration);
            float latchSlack = math.max(0f, _setting.sutureLineContactLatchRestSlack);
            bool enableContactLatch = _setting.enableSutureLineContactLatch;
            float latchedContactScale = math.max(1f, _setting.sutureLineContactLatchedStiffnessScale);
            bool useSelfContact = _setting.enableSutureLineSelfContact;
            int persistentFrames = math.max(0, _setting.sutureLineSelfContactPersistentFrames);
            float selfContactLatchMargin = math.max(0f, _setting.sutureLineSelfContactLatchMargin);
            bool enableCompressionFold = _setting.enableSutureLineCompressionFold;
            float compressionFoldStiffness = math.saturate(_setting.sutureLineCompressionFoldStiffness);
            float compressionFoldMaxPush = math.max(0f, _setting.sutureLineCompressionFoldMaxPushPerIteration);
            float compressionFoldMinDelta = math.max(0f, _setting.sutureLineCompressionFoldMinTargetDelta);

            for (int i = 0; i < _sutureLineConstraints.Length; i++)
            {
                SutureLineConstraintInfo c = _sutureLineConstraints[i];
                if (c.enabled == 0)
                    continue;

                bool strongPull = c.strongPullLine != 0;
                float targetRest = math.max(0f, c.targetRestDistance);
                if (strongPull)
                {
                    targetRest = math.min(
                        targetRest,
                        ComputeStrongSutureLineTargetRest(c.lastActualDistance > 1e-6f ? c.lastActualDistance : c.currentRestDistance, targetRest, contactGap, c.localWallThickness, c.localAverageEdgeLength));
                    c.targetRestDistance = targetRest;
                    c.contactLatched = 1;
                    c.contactState = SutureLineContactStateLatched;
                    c.restFrozen = 0;
                }
                float lineTightenSpeed = strongPull ? math.max(tightenSpeed * 10f, 0.35f) : tightenSpeed;
                float lineApproachTightenScale = strongPull ? math.max(approachTightenScale, 8f) : approachTightenScale;
                float lineLatchedTightenScale = strongPull ? math.max(latchedTightenScale, 8f) : latchedTightenScale;
                float lineLatchSlack = strongPull
                    ? math.max(latchSlack, math.max(contactGap, c.localWallThickness * 0.5f))
                    : latchSlack;
                float previousRest = c.currentRestDistance;
                float3 pointA = ReadSutureLineConstrainedPoint(c.portalA, c.patchStartA, c.patchCountA);
                float3 pointB = ReadSutureLineConstrainedPoint(c.portalB, c.patchStartB, c.patchCountB);
                float actualDistance = math.distance(pointA, pointB);
                c.lastActualDistance = actualDistance;

                c.selfContactThickness = ComputeSutureLineSelfContactThickness(c.localWallThickness, c.localAverageEdgeLength, contactGap);
                if (c.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold)
                {
                    ApplySutureLineSettledHold(ref c, actualDistance, targetRest);
                    c.lambdaResetLastFrame = 0;
                    UpsertSutureLineDisplayInfo(c, pointA, pointB);
                    _sutureLineConstraints[i] = c;
                    continue;
                }

                float representativeContactPush = (!useSelfContact && _setting.enableSutureLineContact && actualDistance < contactGap)
                    ? math.min((contactGap - actualDistance) * contactStiffness, contactMaxPush)
                    : 0f;
                int localPairs = 0;
                c.lastLocalContactPush = (!useSelfContact && _setting.enableSutureLineContact && _setting.enableSutureLineLocalPatchContact)
                    ? EstimateSutureLineLocalPatchContactPush(c, contactGap, contactStiffness, contactMaxPush, math.max(16, _setting.sutureLineLocalContactMaxPairChecksPerLine), out localPairs)
                    : 0f;
                c.lastLocalContactPairs = localPairs;

                int contactTriPairs = 0;
                float minContactDistance = float.PositiveInfinity;
                float triangleContactStiffness = c.contactLatched != 0 ? math.saturate(contactStiffness * latchedContactScale) : contactStiffness;
                c.lastTriangleContactPush = (!useSelfContact && _setting.enableSutureLineContact && _setting.enableSutureLineLocalTriangleContact)
                    ? EstimateSutureLineTriangleContactState(c, contactGap, triangleContactStiffness, contactMaxPush, math.max(16, _setting.sutureLineLocalContactMaxTriangleChecksPerLine), out contactTriPairs, out minContactDistance)
                    : 0f;
                c.lastContactTriPairs = contactTriPairs;
                c.minContactDistance = minContactDistance;

                if (useSelfContact && _setting.enableSutureLineContact)
                {
                    SutureLineSelfContactEstimate estimate = EstimateSutureLineSelfContactState(c);
                    c.lastSelfContactTriPairs = estimate.triPairs;
                    c.lastSelfContactVTPairs = estimate.vtPairs;
                    c.lastSelfContactEEPairs = estimate.eePairs;
                    c.lastSweptContactCandidates = estimate.sweptCandidates;
                    c.lastSelfContactCcdHit = (byte)(estimate.ccdHit > 0 ? 1 : 0);
                    c.lastSelfContactMaxPushClamped = (byte)(estimate.maxPushClamped > 0 ? 1 : 0);
                    c.minSelfContactVTDistance = estimate.minVTDistance;
                    c.minSelfContactEEDistance = estimate.minEEDistance;
                    c.minContactDistance = math.min(estimate.minVTDistance, estimate.minEEDistance);
                    c.lastSelfContactNormalPush = estimate.normalPush;
                    c.lastSelfContactTangentPush = estimate.tangentPush;
                }

                int activeSelfPairs = c.lastSelfContactVTPairs + c.lastSelfContactEEPairs;
                float minSelfContactDistance = math.min(c.minSelfContactVTDistance, c.minSelfContactEEDistance);
                bool sweptOrCcdContact = c.lastSweptContactCandidates > 0 || c.lastSelfContactCcdHit != 0;
                bool currentNearSelfContact = minSelfContactDistance < float.PositiveInfinity &&
                    minSelfContactDistance <= c.selfContactThickness;
                bool localWallContactActive = useSelfContact && _setting.enableSutureLineContact &&
                    (activeSelfPairs > 0 || currentNearSelfContact);
                c.localWallContactActive = (byte)(localWallContactActive ? 1 : 0);
                bool sidewallEquilibriumCandidate = IsSutureLineSidewallEquilibriumCandidate(
                    c,
                    useSelfContact,
                    _setting.enableSutureLineContact,
                    activeSelfPairs,
                    currentNearSelfContact,
                    sweptOrCcdContact,
                    minSelfContactDistance,
                    contactGap,
                    contactMaxPush);

                bool closureNearEnoughForSelfContact = !useSelfContact || actualDistance <= targetRest + selfContactLatchMargin;
                if (useSelfContact)
                {
                    if ((closureNearEnoughForSelfContact && sweptOrCcdContact) || sidewallEquilibriumCandidate)
                        c.persistentContactFramesRemaining = persistentFrames;
                    else if (closureNearEnoughForSelfContact && c.persistentContactFramesRemaining > 0)
                        c.persistentContactFramesRemaining--;
                    else
                        c.persistentContactFramesRemaining = 0;
                    c.lastPersistentContactPairs = c.persistentContactFramesRemaining > 0 ? math.max(1, activeSelfPairs) : 0;
                }

                bool selfContactLatchEvidence = (closureNearEnoughForSelfContact && (sweptOrCcdContact || c.lastPersistentContactPairs > 0)) ||
                    sidewallEquilibriumCandidate;
                bool fallbackContactLatchEvidence = contactTriPairs > 0 || (minContactDistance < float.PositiveInfinity && minContactDistance <= contactGap);
                bool latchNow = enableContactLatch && c.contactLatched == 0 && (useSelfContact
                    ? selfContactLatchEvidence
                    : fallbackContactLatchEvidence);
                if (latchNow)
                {
                    c.contactLatched = 1;
                    c.contactState = SutureLineContactStateLatched;
                    c.lambda = 0f;
                    c.lambdaResetLastFrame = 1;
                    c.weakHoldLogged = 0;
                }

                if (!enableContactLatch)
                {
                    c.contactLatched = 0;
                }
                else if (!strongPull && useSelfContact && c.contactLatched != 0)
                {
                    float releaseSlack = selfContactLatchMargin + math.max(contactGap, c.selfContactThickness);
                    bool releaseLatchedContact =
                        activeSelfPairs <= 0 &&
                        c.lastPersistentContactPairs <= 0 &&
                        !sidewallEquilibriumCandidate &&
                        actualDistance > targetRest + releaseSlack;
                    if (releaseLatchedContact)
                    {
                        c.contactLatched = 0;
                        c.weakHoldLogged = 0;
                    }
                }

                if (c.contactLatched != 0)
                {
                    c.contactState = SutureLineContactStateLatched;
                    c.restFrozen = 0;
                    float latchedSpeed = lineTightenSpeed * lineLatchedTightenScale;
                    float latchedStartRest = math.max(0f, actualDistance - lineLatchSlack);
                    if (c.currentRestDistance > latchedStartRest)
                        c.currentRestDistance = latchedStartRest;
                    if (latchedSpeed > 0f)
                        c.currentRestDistance = math.max(targetRest, c.currentRestDistance - latchedSpeed * dt);
                    else
                        c.currentRestDistance = targetRest;
                }
                else
                {
                    c.contactState = localWallContactActive
                        ? SutureLineContactStatePreLatchWallContact
                        : SutureLineContactStateApproach;
                    c.restFrozen = 0;
                    float approachSpeed = lineTightenSpeed * lineApproachTightenScale;
                    if (approachSpeed > 0f)
                        c.currentRestDistance = math.max(targetRest, c.currentRestDistance - approachSpeed * dt);
                    else
                        c.currentRestDistance = math.max(targetRest, math.min(c.currentRestDistance, targetRest));
                }
                c.targetDelta = math.max(0f, actualDistance - c.currentRestDistance);

                float restDelta = math.abs(c.currentRestDistance - previousRest);
                if (latchNow || _setting.resetSutureLineLambdaEachFrame || restDelta > lambdaResetDelta)
                {
                    c.lambda = 0f;
                    c.lambdaResetLastFrame = 1;
                }
                else
                {
                    c.lambda *= lambdaDecay;
                    c.lambdaResetLastFrame = 0;
                }

                c.tensionOnlyActive = actualDistance > c.currentRestDistance + 1e-5f ? (byte)1 : (byte)0;
                float closureBandSlideContactWindow = math.max(
                    math.max(c.selfContactThickness * 1.5f, c.localWallThickness * 1.15f),
                    selfContactLatchMargin);
                bool closureBandSlideWindowReady = !useSelfContact ||
                    c.localWallContactActive != 0 ||
                    sweptOrCcdContact ||
                    (minSelfContactDistance < float.PositiveInfinity &&
                     minSelfContactDistance <= closureBandSlideContactWindow);
                bool compressionFoldClosureReady = c.contactLatched != 0 || closureNearEnoughForSelfContact;
                bool stableCompressionContact = compressionFoldClosureReady &&
                    IsSutureLineStableCompressionContact(c, actualDistance, targetRest, selfContactLatchMargin);
                float approachFoldWeight = ComputeSutureLineApproachFoldWeight(c, actualDistance, targetRest, selfContactLatchMargin);
                bool verifiedFoldContact = activeSelfPairs > 0 || c.lastPersistentContactPairs > 0;
                int slideColumnLimit = math.max(1, math.min(c.closureBandCountA, c.closureBandCountB));
                int slideColumnCountA = CountSutureLinePatchFoldColumns(c.patchStartA, c.patchCountA, slideColumnLimit);
                int slideColumnCountB = CountSutureLinePatchFoldColumns(c.patchStartB, c.patchCountB, slideColumnLimit);
                int slideColumnCount = math.min(slideColumnCountA, slideColumnCountB);
                bool closureBandSlideCandidate = enableCompressionFold &&
                    c.contactLatched == 0 &&
                    closureBandSlideWindowReady &&
                    c.tensionOnlyActive != 0 &&
                    c.targetDelta > compressionFoldMinDelta &&
                    c.closureBandCountA > 0 &&
                    c.closureBandCountB > 0 &&
                    slideColumnCount > 0;
                if (closureBandSlideCandidate && c.closureBandSlideDirValid == 0)
                    UpdateSutureLineClosureBandSlideDirection(ref c, pointA, pointB);
                if (closureBandSlideCandidate && c.closureBandSlideDirValid == 0)
                    closureBandSlideCandidate = false;

                float rawSlidePush = 0f;
                if (closureBandSlideCandidate)
                {
                    float slideSourceDelta = math.min(c.targetDelta, math.max(c.selfContactThickness, c.localWallThickness));
                    rawSlidePush = math.min(compressionFoldMaxPush * 0.1f, slideSourceDelta * compressionFoldStiffness * 0.1f);
                }
                c.rawClosureBandSlidePush = rawSlidePush;
                c.lastClosureBandSlidePush = rawSlidePush;
                c.closureBandSlideActive = (byte)(rawSlidePush > 1e-8f ? 1 : 0);
                c.closureBandSlideColumnCount = c.closureBandSlideActive != 0 ? slideColumnCount : 0;

                bool foldCandidateActive = enableCompressionFold &&
                    stableCompressionContact &&
                    c.tensionOnlyActive != 0 &&
                    c.targetDelta > compressionFoldMinDelta;

                bool foldWasActive = c.compressionFoldActive != 0 || c.lastCompressionFoldPush > 1e-7f;
                int foldColumnLimit = c.contactLatched != 0 ? 64 : 24;
                int foldColumnCount = foldCandidateActive || foldWasActive
                    ? CountSutureLineFoldColumns(c, foldColumnLimit)
                    : 0;
                int foldPatchVertexLimit = c.contactLatched != 0 ? 96 : 48;
                int foldPatchVertexCount = foldCandidateActive || foldWasActive
                    ? CountSutureLineFoldPatchVertices(c, foldPatchVertexLimit)
                    : 0;
                bool foldPatchFallbackAvailable = foldPatchVertexCount > 0;
                bool preLatchNoColumnSuppressed = c.contactLatched == 0 &&
                    (foldCandidateActive || foldWasActive) &&
                    foldColumnCount <= 0 &&
                    !foldPatchFallbackAvailable;
                if (preLatchNoColumnSuppressed)
                    foldCandidateActive = false;

                bool foldHoldContact = compressionFoldClosureReady &&
                    enableCompressionFold &&
                    verifiedFoldContact &&
                    c.tensionOnlyActive != 0 &&
                    c.targetDelta > compressionFoldMinDelta * 0.5f &&
                    IsSutureLineCompressionFoldHoldContact(c, actualDistance, targetRest, selfContactLatchMargin);
                if (foldCandidateActive)
                {
                    c.compressionFoldHoldFramesRemaining = c.contactLatched != 0
                        ? SutureCompressionFoldLatchedHoldFrames
                        : SutureCompressionFoldPreLatchHoldFrames;
                }
                else if (foldWasActive && foldHoldContact && c.compressionFoldHoldFramesRemaining > 0)
                {
                    c.compressionFoldHoldFramesRemaining--;
                }
                else
                {
                    c.compressionFoldHoldFramesRemaining = 0;
                }

                bool foldHeldActive = !foldCandidateActive &&
                    foldWasActive &&
                    !preLatchNoColumnSuppressed &&
                    foldHoldContact &&
                    c.compressionFoldHoldFramesRemaining > 0;
                bool compressionFoldActive = foldCandidateActive || foldHeldActive;
                c.compressionFoldCandidateActive = (byte)(foldCandidateActive ? 1 : 0);
                c.foldPatchFallbackSuppressed = (byte)(preLatchNoColumnSuppressed ? 1 : 0);
                c.foldColumnCount = compressionFoldActive || preLatchNoColumnSuppressed ? foldColumnCount : 0;

                float rawFoldPush = 0f;
                if (compressionFoldActive)
                {
                    if (foldCandidateActive || c.foldDirValid == 0)
                        UpdateSutureLineFoldDirection(ref c, pointA, pointB);
                    float phaseScale = c.contactLatched != 0 ? 0.6f : 0.25f;
                    float phaseMaxPush = compressionFoldMaxPush * (c.contactLatched != 0 ? 0.65f : 0.35f);
                    float compressionDepth = minSelfContactDistance < float.PositiveInfinity
                        ? math.max(0f, c.selfContactThickness - minSelfContactDistance)
                        : 0f;
                    float compressionWeight = math.saturate(compressionDepth / math.max(1e-6f, c.selfContactThickness));
                    float foldSourceDelta = math.min(c.targetDelta, math.max(c.selfContactThickness, c.localWallThickness));
                    rawFoldPush = math.min(phaseMaxPush, foldSourceDelta * compressionFoldStiffness * compressionWeight * phaseScale);
                }

                c.rawCompressionFoldPush = rawFoldPush;
                float smoothFoldMaxPush = compressionFoldMaxPush;
                bool allowFoldRelease = foldHoldContact && verifiedFoldContact;
                if (!compressionFoldActive && !allowFoldRelease)
                {
                    c.lastCompressionFoldPush = 0f;
                }
                else
                {
                    c.lastCompressionFoldPush = SmoothSutureLineFoldPush(
                        c.lastCompressionFoldPush,
                        rawFoldPush,
                        smoothFoldMaxPush,
                        c.contactLatched != 0,
                        dt);
                }

                bool outputFoldActive = compressionFoldActive || (allowFoldRelease && c.lastCompressionFoldPush > 1e-7f);
                c.compressionFoldActive = (byte)(outputFoldActive ? 1 : 0);
                if (outputFoldActive)
                {
                    if (preLatchNoColumnSuppressed)
                    {
                        c.foldReason = SutureFoldReasonPreLatchNoColumnSuppressed;
                    }
                    else if (!compressionFoldActive)
                    {
                        c.foldReason = SutureFoldReasonReleasing;
                    }
                    else if (c.foldColumnCount > 0)
                    {
                        c.foldReason = c.contactLatched != 0
                            ? SutureFoldReasonContactLatched
                            : SutureFoldReasonPreLatchWallContact;
                    }
                    else if (foldPatchFallbackAvailable)
                    {
                        c.foldReason = SutureFoldReasonFallbackPatch;
                    }
                    else
                    {
                        c.foldReason = c.contactLatched != 0
                            ? SutureFoldReasonFallbackPatch
                            : SutureFoldReasonPreLatchNoColumnSuppressed;
                    }
                }
                else
                {
                    c.rawCompressionFoldPush = 0f;
                    c.lastCompressionFoldPush = 0f;
                    c.foldReason = preLatchNoColumnSuppressed
                        ? SutureFoldReasonPreLatchNoColumnSuppressed
                        : localWallContactActive && c.tensionOnlyActive != 0
                        ? SutureFoldReasonUnstableContact
                        : SutureFoldReasonNone;
                }

                c.lastContactPush = useSelfContact
                    ? math.max(c.lastSelfContactNormalPush, c.lastSelfContactTangentPush)
                    : math.max(math.max(representativeContactPush, c.lastLocalContactPush), c.lastTriangleContactPush);

                bool workflowContactEvidence =
                    c.contactLatched != 0 ||
                    c.localWallContactActive != 0 ||
                    c.lastPersistentContactPairs > 0 ||
                    activeSelfPairs > 0 ||
                    sweptOrCcdContact ||
                    contactTriPairs > 0 ||
                    localPairs > 0 ||
                    representativeContactPush > 1e-8f ||
                    c.lastLocalContactPush > 1e-8f ||
                    c.lastTriangleContactPush > 1e-8f;
                if (sidewallEquilibriumCandidate)
                    workflowContactEvidence = true;
                if (!workflowContactEvidence && minSelfContactDistance < float.PositiveInfinity)
                    workflowContactEvidence = minSelfContactDistance <= math.max(c.selfContactThickness, contactGap);
                if (!workflowContactEvidence && minContactDistance < float.PositiveInfinity)
                    workflowContactEvidence = minContactDistance <= math.max(c.selfContactThickness, contactGap);

                bool workflowReadyCandidate = IsSutureLineWorkflowReadyCandidate(
                    actualDistance,
                    c.currentRestDistance,
                    targetRest,
                    c.targetDelta,
                    contactGap,
                    selfContactLatchMargin,
                    c.selfContactThickness,
                    workflowContactEvidence,
                    sidewallEquilibriumCandidate);
                UpdateSutureLineWorkflowReady(ref c, workflowReadyCandidate);
                bool workflowAdvanceReadyCandidate = c.autoManaged != 0 &&
                    IsSutureLineWorkflowAdvanceReadyCandidate(
                        actualDistance,
                        c.currentRestDistance,
                        targetRest,
                        c.targetDelta,
                        contactGap,
                        selfContactLatchMargin,
                        c.selfContactThickness);
                bool workflowAdvanceReadyTransitioned = UpdateSutureLineWorkflowAdvanceReady(
                    ref c,
                    workflowAdvanceReadyCandidate);
                if (workflowAdvanceReadyTransitioned)
                {
                    Debug.Log(
                        $"[Suture][WorkflowAdvanceReady] lineIndex={c.lineIndex} collider={c.colliderEntityId} " +
                        $"passA={c.passIndexA} passB={c.passIndexB} actualDistance={actualDistance:F6} " +
                        $"currentRest={c.currentRestDistance:F6} targetRest={targetRest:F6} " +
                        $"targetDelta={c.targetDelta:F6} contactLatched={c.contactLatched} " +
                        $"physicalReady={c.workflowReady} sidewallEquilibrium={(sidewallEquilibriumCandidate ? 1 : 0)} " +
                        $"advanceFrames={c.workflowAdvanceReadyFrames}");
                }

                byte lifecycleBeforeSettle = c.reserved0;
                if (TryAdvanceSutureLineSettledLifecycle(ref c, actualDistance, targetRest, contactGap, selfContactLatchMargin, sidewallEquilibriumCandidate))
                {
                    if (lifecycleBeforeSettle != SutureLineConstraintInfo.LifecycleSettledHold)
                    {
                        c.lambda = 0f;
                        c.lambdaResetLastFrame = 1;
                        Debug.Log($"[Suture][PullLineSettled] lineIndex={c.lineIndex} collider={c.colliderEntityId} passA={c.passIndexA} passB={c.passIndexB} actualDistance={actualDistance:F6} targetRest={targetRest:F6} settleFrames={SutureLineSettledHoldFrames} holdOnly=1 skipContact=1 skipFold=1");
                    }

                    ApplySutureLineSettledHold(ref c, actualDistance, targetRest);
                    UpsertSutureLineDisplayInfo(c, pointA, pointB);
                    _sutureLineConstraints[i] = c;
                    continue;
                }

                UpsertSutureLineDisplayInfo(c, pointA, pointB);

                bool shouldLog = c.weakHoldLogged == 0 || c.progressLogCounter >= 30;
                if (shouldLog)
                {
                    c.weakHoldLogged = 1;
                    c.progressLogCounter = 0;
                    float3 portalPointA = ReadPortalPointFromPredictedPositions(c.portalA);
                    float3 portalPointB = ReadPortalPointFromPredictedPositions(c.portalB);
                    float portalDistance = math.distance(portalPointA, portalPointB);
                    float patchPortalDriftA = math.distance(pointA, portalPointA);
                    float patchPortalDriftB = math.distance(pointB, portalPointB);
                    int vtBudget = _setting != null ? math.max(8, _setting.sutureLineSelfContactMaxVTPairs) : 1024;
                    int eeBudget = _setting != null && _setting.enableSutureLineSelfContactEE ? math.max(0, _setting.sutureLineSelfContactMaxEEPairs) : 0;
                    float selfContactDepth = minSelfContactDistance < float.PositiveInfinity
                        ? math.max(0f, c.selfContactThickness - minSelfContactDistance)
                        : float.PositiveInfinity;
                    float3 foldDir = math.normalizesafe(c.foldDir, float3.zero);
                    float3 foldClosureAxis = math.normalizesafe(c.closureDirection, math.normalizesafe(pointB - pointA, new float3(1f, 0f, 0f)));
                    float3 foldContactNormal = math.normalizesafe(c.contactNormalHint, float3.zero);
                    float foldUpDot = math.dot(foldDir, new float3(0f, 1f, 0f));
                    float foldClosureDot = math.dot(foldDir, foldClosureAxis);
                    float foldContactNDot = math.lengthsq(foldContactNormal) > 1e-8f ? math.dot(foldDir, foldContactNormal) : 0f;
                    Debug.Log($"[Suture][PullLineWeakHold] lineIndex={c.lineIndex} collider={c.colliderEntityId} passA={c.passIndexA} passB={c.passIndexB} wholeWall=1 localWallContact={(useSelfContact ? 1 : 0)} wallContactState={FormatSutureLineContactState(c.contactState)} localWallContactActive={c.localWallContactActive} sidewallEquilibrium={(sidewallEquilibriumCandidate ? 1 : 0)} foldClosureReady={(compressionFoldClosureReady ? 1 : 0)} stableCompressionContact={(stableCompressionContact ? 1 : 0)} approachFoldWeight={approachFoldWeight:F3} compressionFoldCandidate={c.compressionFoldCandidateActive} compressionFoldActive={c.compressionFoldActive} foldFallbackSuppressed={c.foldPatchFallbackSuppressed} foldHold={c.compressionFoldHoldFramesRemaining} minContactDistance={FormatFiniteFloat(c.minContactDistance)} contactLatched={c.contactLatched} workflowReady={c.workflowReady} workflowFrames={c.workflowReadyFrames} latchNearEnough={(closureNearEnoughForSelfContact ? 1 : 0)} latchMargin={selfContactLatchMargin:F6} actualDistance={actualDistance:F6} portalDistance={portalDistance:F6} patchPortalDriftA={patchPortalDriftA:F6} patchPortalDriftB={patchPortalDriftB:F6} currentRest={c.currentRestDistance:F6} targetRest={targetRest:F6} targetDelta={c.targetDelta:F6} restFrozen={c.restFrozen} adaptiveTarget={c.adaptiveTargetUsed} biteA={c.biteDistanceA:F6} biteB={c.biteDistanceB:F6} thickness={c.localWallThickness:F6} avgEdge={c.localAverageEdgeLength:F6} openGap={c.localOpenGap:F6} closureDir=({c.closureDirection.x:F3},{c.closureDirection.y:F3},{c.closureDirection.z:F3}) contactN=({c.contactNormalHint.x:F3},{c.contactNormalHint.y:F3},{c.contactNormalHint.z:F3}) tensionOnlyActive={c.tensionOnlyActive} lambdaReset={c.lambdaResetLastFrame} roiVertA={c.patchCountA} roiVertB={c.patchCountB} roiTriA={c.contactTriCountA} roiTriB={c.contactTriCountB} virtualSidewallTriA={c.virtualSidewallTriCountA} virtualSidewallTriB={c.virtualSidewallTriCountB} sidewallEdges={_sutureSidewallBoundaryEdges.Count} bandA={c.closureBandCountA} bandB={c.closureBandCountB} sweptCandidates={c.lastSweptContactCandidates} triTriPairs={c.lastSelfContactTriPairs} vtPairs={c.lastSelfContactVTPairs} vtBudget={vtBudget} vtBudgetHit={(c.lastSelfContactVTPairs >= vtBudget ? 1 : 0)} eePairs={c.lastSelfContactEEPairs} eeBudget={eeBudget} eeBudgetHit={(eeBudget > 0 && c.lastSelfContactEEPairs >= eeBudget ? 1 : 0)} persistentPairs={c.lastPersistentContactPairs} minVTDistance={FormatFiniteFloat(c.minSelfContactVTDistance)} minEEDistance={FormatFiniteFloat(c.minSelfContactEEDistance)} contactThickness={c.selfContactThickness:F6} selfContactDepth={FormatFiniteFloat(selfContactDepth)} normalPush={c.lastSelfContactNormalPush:F6} tangentPush={c.lastSelfContactTangentPush:F6} solveTriPairs={c.lastSelfContactSolveTriPairs} solveVTPairs={c.lastSelfContactSolveVTPairs} solveEEPairs={c.lastSelfContactSolveEEPairs} solveMinDistance={FormatFiniteFloat(c.lastSelfContactSolveMinDistance)} solveNormalPush={c.lastSelfContactSolveNormalPush:F6} solveCcdHit={c.lastSelfContactSolveCcdHit} solveForced={c.lastSelfContactSolveForced} ccdAdvancePush={c.lastSelfContactCcdAdvancePush:F6} rawFoldPush={c.rawCompressionFoldPush:F6} foldPush={c.lastCompressionFoldPush:F6} foldDir=({c.foldDir.x:F3},{c.foldDir.y:F3},{c.foldDir.z:F3}) foldUpDot={foldUpDot:F3} foldClosureDot={foldClosureDot:F3} foldContactNDot={foldContactNDot:F3} foldColumnCount={c.foldColumnCount} foldPatchVertexCount={foldPatchVertexCount} foldPatchFallback={(foldPatchFallbackAvailable ? 1 : 0)} foldReason={FormatSutureFoldReason(c.foldReason)} slideWindow={(closureBandSlideWindowReady ? 1 : 0)} slideWindowDistance={closureBandSlideContactWindow:F6} closureBandSlide={c.closureBandSlideActive} slideColumns={c.closureBandSlideColumnCount} rawSlidePush={c.rawClosureBandSlidePush:F6} slidePush={c.lastClosureBandSlidePush:F6} slideDirValid={c.closureBandSlideDirValid} slideDir=({c.closureBandSlideDir.x:F3},{c.closureBandSlideDir.y:F3},{c.closureBandSlideDir.z:F3}) ccdHit={c.lastSelfContactCcdHit} maxPushClamped={c.lastSelfContactMaxPushClamped} contactTriPairs={c.lastContactTriPairs} contactPush={c.lastContactPush:F6} localContactPairs={c.lastLocalContactPairs} localContactPush={c.lastLocalContactPush:F6} triangleContactPush={c.lastTriangleContactPush:F6} contactRoiMissReason={FormatSutureLineContactMissReason(c.contactMissReason)}");
                }
                else
                {
                    c.progressLogCounter++;
                }

                _sutureLineConstraints[i] = c;
            }
        }

        internal static float ComputeSutureLineWorkflowReadySlack(float contactGap, float latchMargin, float selfContactThickness)
        {
            return math.max(
                math.max(math.max(0f, latchMargin), math.max(0f, selfContactThickness)),
                math.max(math.max(0f, contactGap), 0.0015f));
        }

        internal static bool IsSutureLineWorkflowAdvanceReadyCandidate(
            float actualDistance,
            float currentRestDistance,
            float targetRest,
            float targetDelta,
            float contactGap,
            float latchMargin,
            float selfContactThickness)
        {
            float safeTargetRest = math.max(0f, targetRest);
            float slack = ComputeSutureLineWorkflowReadySlack(contactGap, latchMargin, selfContactThickness);
            float restEpsilon = math.max(0.0005f, math.max(0f, contactGap) * 0.2f);
            bool restReached = currentRestDistance <= safeTargetRest + restEpsilon;
            bool holeDistanceReady = actualDistance <= safeTargetRest + slack && targetDelta <= slack;
            return restReached && holeDistanceReady;
        }

        internal static bool IsSutureLineWorkflowReadyCandidate(
            float actualDistance,
            float currentRestDistance,
            float targetRest,
            float targetDelta,
            float contactGap,
            float latchMargin,
            float selfContactThickness,
            bool contactEvidence,
            bool sidewallEquilibrium)
        {
            if (!contactEvidence && !sidewallEquilibrium)
                return false;

            float safeTargetRest = math.max(0f, targetRest);
            float restEpsilon = math.max(0.0005f, math.max(0f, contactGap) * 0.2f);
            bool restReached = currentRestDistance <= safeTargetRest + restEpsilon;
            return restReached && (sidewallEquilibrium || IsSutureLineWorkflowAdvanceReadyCandidate(
                actualDistance,
                currentRestDistance,
                targetRest,
                targetDelta,
                contactGap,
                latchMargin,
                selfContactThickness));
        }

        private static void UpdateSutureLineWorkflowReady(ref SutureLineConstraintInfo c, bool readyCandidate)
        {
            if (!readyCandidate)
            {
                c.workflowReady = 0;
                c.workflowReadyFrames = 0;
                return;
            }

            byte nextFrameCount = (byte)math.min(SutureLineWorkflowReadyHoldFrames, c.workflowReadyFrames + 1);
            c.workflowReadyFrames = nextFrameCount;
            c.workflowReady = (byte)(nextFrameCount >= SutureLineWorkflowReadyHoldFrames ? 1 : 0);
        }

        private static bool UpdateSutureLineWorkflowAdvanceReady(ref SutureLineConstraintInfo c, bool readyCandidate)
        {
            bool wasReady = c.workflowAdvanceReady != 0;
            if (c.workflowReleased != 0)
            {
                c.workflowAdvanceReady = 1;
                c.workflowAdvanceReadyFrames = SutureLineWorkflowReadyHoldFrames;
                return !wasReady;
            }
            if (!readyCandidate)
            {
                c.workflowAdvanceReady = 0;
                c.workflowAdvanceReadyFrames = 0;
                return false;
            }

            byte nextFrameCount = (byte)math.min(
                SutureLineWorkflowReadyHoldFrames,
                c.workflowAdvanceReadyFrames + 1);
            c.workflowAdvanceReadyFrames = nextFrameCount;
            c.workflowAdvanceReady = (byte)(nextFrameCount >= SutureLineWorkflowReadyHoldFrames ? 1 : 0);
            return !wasReady && c.workflowAdvanceReady != 0;
        }

        private bool TryAdvanceSutureLineSettledLifecycle(ref SutureLineConstraintInfo c, float actualDistance, float targetRest, float contactGap, float latchMargin, bool sidewallEquilibrium)
        {
            if (c.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold)
                return true;

            float settleSlack = ComputeSutureLineWorkflowReadySlack(contactGap, latchMargin, c.selfContactThickness);
            float restEpsilon = math.max(0.0005f, contactGap * 0.2f);
            bool restAtTarget = c.currentRestDistance <= targetRest + restEpsilon;
            bool closeEnough = actualDistance <= targetRest + settleSlack;
            bool pulledTight = c.targetDelta <= settleSlack;
            bool settleCandidate = c.contactLatched != 0 && restAtTarget &&
                ((closeEnough && pulledTight) || sidewallEquilibrium);
            if (!settleCandidate)
            {
                c.reserved0 = SutureLineConstraintInfo.LifecycleActive;
                return false;
            }

            byte nextFrameCount = (byte)math.min(SutureLineSettledHoldFrames, c.reserved0 + 1);
            c.reserved0 = nextFrameCount >= SutureLineSettledHoldFrames
                ? SutureLineConstraintInfo.LifecycleSettledHold
                : nextFrameCount;
            return c.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold;
        }

        private static bool IsSutureLineSidewallEquilibriumCandidate(
            SutureLineConstraintInfo c,
            bool useSelfContact,
            bool enableContact,
            int activeSelfPairs,
            bool currentNearSelfContact,
            bool sweptOrCcdContact,
            float minSelfContactDistance,
            float contactGap,
            float contactMaxPush)
        {
            if (!useSelfContact || !enableContact ||
                c.virtualSidewallTriCountA <= 0 ||
                c.virtualSidewallTriCountB <= 0)
                return false;

            bool contactEvidence =
                activeSelfPairs > 0 ||
                currentNearSelfContact ||
                sweptOrCcdContact ||
                c.lastPersistentContactPairs > 0 ||
                c.lastSelfContactSolveVTPairs + c.lastSelfContactSolveEEPairs > 0;
            if (!contactEvidence)
                return false;

            float thickness = math.max(1e-6f, c.selfContactThickness);
            float minDistance = minSelfContactDistance;
            if (c.lastSelfContactSolveMinDistance < float.PositiveInfinity)
                minDistance = math.min(minDistance, c.lastSelfContactSolveMinDistance);
            if (minDistance >= float.PositiveInfinity)
                return false;

            float sidewallSkin = ComputeSutureLineVirtualSidewallContactSkin(thickness, contactGap);
            bool insideSidewallSkin = minDistance <= sidewallSkin;
            if (!insideSidewallSkin)
                return false;

            float quietPush = math.max(sidewallSkin * 0.5f, contactMaxPush * 0.35f);
            bool solverQuiet =
                c.lastSelfContactSolveNormalPush <= quietPush ||
                c.lastSelfContactNormalPush <= quietPush ||
                c.lastPersistentContactPairs > 0;
            return solverQuiet || sweptOrCcdContact || activeSelfPairs > 0 || currentNearSelfContact;
        }

        private static float ComputeSutureLineVirtualSidewallContactSkin(float thickness, float contactGap)
        {
            float gapSkin = contactGap > 1e-7f ? contactGap * 0.5f : 0f;
            float thicknessSkin = math.max(0f, thickness) * 0.08f;
            float skin = math.max(math.max(gapSkin, thicknessSkin), 1e-5f);
            return math.min(math.max(1e-5f, thickness), skin);
        }

        private float ComputeSutureLineVirtualSidewallContactSkin(float thickness)
        {
            float contactGap = _setting != null ? math.max(0f, _setting.sutureLineContactGap) : 0f;
            return ComputeSutureLineVirtualSidewallContactSkin(thickness, contactGap);
        }

        private static void ApplySutureLineSettledHold(ref SutureLineConstraintInfo c, float actualDistance, float targetRest)
        {
            c.contactLatched = 1;
            c.contactState = SutureLineContactStateLatched;
            c.workflowReady = 1;
            c.workflowReadyFrames = SutureLineWorkflowReadyHoldFrames;
            c.workflowAdvanceReady = 1;
            c.workflowAdvanceReadyFrames = SutureLineWorkflowReadyHoldFrames;
            c.restFrozen = 1;
            c.currentRestDistance = targetRest;
            c.targetDelta = math.max(0f, actualDistance - targetRest);
            c.tensionOnlyActive = c.targetDelta > 1e-5f ? (byte)1 : (byte)0;
            c.localWallContactActive = 0;
            c.lastLocalContactPairs = 0;
            c.lastContactTriPairs = 0;
            c.lastSelfContactTriPairs = 0;
            c.lastSelfContactVTPairs = 0;
            c.lastSelfContactEEPairs = 0;
            c.lastPersistentContactPairs = 0;
            c.lastSweptContactCandidates = 0;
            c.persistentContactFramesRemaining = 0;
            c.compressionFoldHoldFramesRemaining = 0;
            c.lastSelfContactCcdHit = 0;
            c.lastSelfContactMaxPushClamped = 0;
            c.minContactDistance = float.PositiveInfinity;
            c.minSelfContactVTDistance = float.PositiveInfinity;
            c.minSelfContactEEDistance = float.PositiveInfinity;
            c.lastContactPush = 0f;
            c.lastLocalContactPush = 0f;
            c.lastTriangleContactPush = 0f;
            c.lastSelfContactNormalPush = 0f;
            c.lastSelfContactTangentPush = 0f;
            c.lastSelfContactSolveTriPairs = 0;
            c.lastSelfContactSolveVTPairs = 0;
            c.lastSelfContactSolveEEPairs = 0;
            c.lastSelfContactSolveCcdHit = 0;
            c.lastSelfContactSolveForced = 0;
            c.lastSelfContactSolveNormalPush = 0f;
            c.lastSelfContactSolveMinDistance = float.PositiveInfinity;
            c.lastSelfContactCcdAdvancePush = 0f;
            c.rawCompressionFoldPush = 0f;
            c.lastCompressionFoldPush = 0f;
            c.rawClosureBandSlidePush = 0f;
            c.lastClosureBandSlidePush = 0f;
            c.compressionFoldActive = 0;
            c.compressionFoldCandidateActive = 0;
            c.foldPatchFallbackSuppressed = 0;
            c.closureBandSlideActive = 0;
            c.foldColumnCount = 0;
            c.closureBandSlideColumnCount = 0;
            c.foldReason = SutureFoldReasonNone;
            c.weakHoldLogged = 1;
            c.progressLogCounter = 0;
        }

        private void UpdateSutureLineFoldDirection(ref SutureLineConstraintInfo c, float3 pointA, float3 pointB)
        {
            float3 previous = math.normalizesafe(c.foldDir, float3.zero);
            bool hasPrevious = c.foldDirValid != 0 && math.lengthsq(previous) > 1e-8f;

            float3 lineDir = GetSutureLineFoldClosureDirection(c, pointA, pointB);
            float3 wallNormal = GetSutureLineFoldWallNormal(c, lineDir);
            float3 candidate = math.cross(wallNormal, lineDir);

            if (math.lengthsq(candidate) < 1e-8f && hasPrevious)
                candidate = previous;
            if (math.lengthsq(candidate) < 1e-8f)
                candidate = math.cross(new float3(0f, 1f, 0f), lineDir);
            if (math.lengthsq(candidate) < 1e-8f)
                candidate = math.cross(lineDir, new float3(0f, 0f, 1f));

            candidate = math.normalizesafe(candidate, float3.zero);
            candidate = ProjectFoldDirection(candidate, wallNormal, lineDir);
            bool bandOriented = TryOrientSutureLineFoldDirectionFromClosureBands(c, ref candidate);

            float3 upEscape = ProjectFoldDirection(new float3(0f, 1f, 0f), wallNormal, lineDir);
            if (math.lengthsq(upEscape) > 1e-8f)
            {
                if (hasPrevious)
                {
                    if (math.dot(upEscape, previous) < 0f)
                        upEscape = -upEscape;
                }
                else if (((c.lineIndex + c.passIndexA + c.passIndexB) & 1) != 0)
                {
                    upEscape = -upEscape;
                }

                candidate = math.lengthsq(candidate) > 1e-8f
                    ? math.normalizesafe(math.lerp(candidate, upEscape, SutureCompressionFoldUpBias), upEscape)
                    : upEscape;
            }

            if (math.lengthsq(candidate) < 1e-8f)
            {
                c.foldDirValid = 0;
                c.foldDir = float3.zero;
                return;
            }

            if (hasPrevious && !bandOriented && math.dot(candidate, previous) < 0f)
                candidate = -candidate;

            if (hasPrevious && (!bandOriented || math.dot(candidate, previous) > 0f))
            {
                float blend = c.contactLatched != 0
                    ? SutureCompressionFoldLatchedDirBlend
                    : SutureCompressionFoldPreLatchDirBlend;
                candidate = math.normalizesafe(math.lerp(previous, candidate, math.saturate(blend)), candidate);
            }

            c.foldDir = candidate;
            c.foldDirValid = 1;
        }

        private void UpdateSutureLineClosureBandSlideDirection(ref SutureLineConstraintInfo c, float3 pointA, float3 pointB)
        {
            if (c.closureBandSlideDirValid != 0 && math.lengthsq(c.closureBandSlideDir) > 1e-8f)
                return;

            float3 lineDir = GetSutureLineFoldClosureDirection(c, pointA, pointB);
            float3 wallNormal = GetSutureLineFoldWallNormal(c, lineDir);
            float3 candidate = math.cross(wallNormal, lineDir);
            if (math.lengthsq(candidate) < 1e-8f)
                candidate = math.cross(new float3(0f, 1f, 0f), lineDir);
            if (math.lengthsq(candidate) < 1e-8f)
                candidate = math.cross(lineDir, new float3(0f, 0f, 1f));

            candidate = ProjectFoldDirection(candidate, wallNormal, lineDir);
            if (math.lengthsq(candidate) < 1e-8f)
                return;

            if (!TryOrientSutureLineFoldDirectionFromClosureBands(c, ref candidate))
                return;

            c.closureBandSlideDir = candidate;
            c.closureBandSlideDirValid = 1;
        }

        private bool TryOrientSutureLineFoldDirectionFromClosureBands(SutureLineConstraintInfo c, ref float3 direction)
        {
            direction = math.normalizesafe(direction, float3.zero);
            if (math.lengthsq(direction) < 1e-8f)
                return false;

            if (!TryReadSutureLineClosureBandSlideCenter(c.patchStartA, c.patchCountA, out float3 centerA) ||
                !TryReadSutureLineClosureBandSlideCenter(c.patchStartB, c.patchCountB, out float3 centerB))
                return false;

            float lateralOffset = math.dot(centerB - centerA, direction);
            float signEpsilon = math.max(1e-5f, math.max(c.localAverageEdgeLength, c.selfContactThickness) * 0.05f);
            if (math.abs(lateralOffset) <= signEpsilon)
                return false;

            if (lateralOffset < 0f)
                direction = -direction;
            return true;
        }

        private bool TryReadSutureLineClosureBandSlideCenter(int patchStart, int patchCount, out float3 center)
        {
            center = float3.zero;
            if (!_sutureLinePatchVertices.IsCreated || !_predictPositions.IsCreated ||
                patchStart < 0 || patchCount <= 0 || patchStart + patchCount > _sutureLinePatchVertices.Length)
                return false;

            float weightSum = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = _sutureLinePatchVertices[patchStart + i];
                if (pv.vertexIndex < 0 || pv.vertexIndex >= _predictPositions.Length)
                    continue;

                float weight = GetSutureLinePatchFoldWeight(pv);
                if (weight <= 0f)
                    continue;

                center += _predictPositions[pv.vertexIndex] * weight;
                weightSum += weight;
            }

            if (weightSum <= 1e-8f)
                return false;

            center /= weightSum;
            return true;
        }

        private static float3 GetSutureLineFoldClosureDirection(SutureLineConstraintInfo c, float3 pointA, float3 pointB)
        {
            float3 stored = math.normalizesafe(c.closureDirection, float3.zero);
            if (math.lengthsq(stored) > 1e-8f)
                return stored;

            return math.normalizesafe(pointB - pointA, new float3(1f, 0f, 0f));
        }

        private static float ComputeSutureLineApproachFoldWeight(SutureLineConstraintInfo c, float actualDistance, float targetRest, float latchMargin)
        {
            float thickness = math.max(1e-6f, c.selfContactThickness);
            float openGap = c.localOpenGap > 1e-5f
                ? c.localOpenGap
                : math.max(targetRest + latchMargin, math.max(c.localWallThickness * 2f, thickness * 3f));
            float approachMargin = math.max(
                math.max(c.localWallThickness * 1.5f, thickness * 3f),
                c.localAverageEdgeLength * 1.5f);
            approachMargin = math.max(approachMargin, 1e-6f);
            float enterDistance = math.max(targetRest + latchMargin, openGap) + approachMargin;
            if (actualDistance > enterDistance)
                return 0f;
            return math.saturate((enterDistance - actualDistance) / approachMargin);
        }

        private bool IsSutureLineStableCompressionContact(SutureLineConstraintInfo c, float actualDistance, float targetRest, float latchMargin)
        {
            if (c.localWallContactActive == 0)
                return false;

            int activePairs = c.lastSelfContactVTPairs + c.lastSelfContactEEPairs;
            if (activePairs <= 0)
                return false;

            float minDistance = math.min(c.minSelfContactVTDistance, c.minSelfContactEEDistance);
            if (!(minDistance < float.PositiveInfinity))
                return false;

            float thickness = math.max(1e-6f, c.selfContactThickness);
            bool currentCompression = minDistance <= thickness * SutureCompressionFoldEnterRatio;
            if (!currentCompression)
                return false;

            if (c.contactLatched != 0)
                return true;

            float openGap = c.localOpenGap > 1e-5f
                ? c.localOpenGap
                : math.max(targetRest + latchMargin, math.max(c.localWallThickness * 2f, thickness * 3f));
            float compressionWindow = openGap + math.max(c.localWallThickness * 0.75f, thickness * 1.5f);
            return actualDistance <= compressionWindow;
        }

        private bool IsSutureLineCompressionFoldHoldContact(SutureLineConstraintInfo c, float actualDistance, float targetRest, float latchMargin)
        {
            if (c.localWallContactActive == 0 && c.lastPersistentContactPairs <= 0)
                return false;

            float minDistance = math.min(c.minSelfContactVTDistance, c.minSelfContactEEDistance);
            float thickness = math.max(1e-6f, c.selfContactThickness);
            bool nearCompression = minDistance < float.PositiveInfinity
                ? minDistance <= thickness * SutureCompressionFoldExitRatio
                : c.lastPersistentContactPairs > 0;
            if (!nearCompression)
                return false;

            if (c.contactLatched != 0)
                return true;

            float openGap = c.localOpenGap > 1e-5f
                ? c.localOpenGap
                : math.max(targetRest + latchMargin, math.max(c.localWallThickness * 2f, thickness * 3f));
            float compressionWindow = openGap + math.max(c.localWallThickness * 1.1f, thickness * 2.0f);
            return actualDistance <= compressionWindow;
        }

        private static float SmoothSutureLineFoldPush(float previous, float target, float maxPush, bool contactLatched, float dt)
        {
            previous = math.max(0f, previous);
            target = math.max(0f, target);
            maxPush = math.max(0f, maxPush);
            if (maxPush <= 1e-8f)
                return 0f;

            float frameScale = math.clamp(dt * 60f, 0.25f, 2f);
            float stepScale = target > previous
                ? (contactLatched ? SutureCompressionFoldLatchedRiseStep : SutureCompressionFoldPreLatchRiseStep)
                : (contactLatched ? SutureCompressionFoldLatchedFallStep : SutureCompressionFoldPreLatchFallStep);
            float maxStep = math.max(1e-8f, maxPush * stepScale * frameScale);
            if (target > previous)
                return math.min(target, previous + maxStep);
            return math.max(target, previous - maxStep);
        }

        private float3 GetSutureLineFoldWallNormal(SutureLineConstraintInfo c, float3 lineDir)
        {
            float3 hintedNormal = ProjectSutureLineVectorAwayFromAxis(c.contactNormalHint, lineDir);
            if (math.lengthsq(hintedNormal) > 1e-8f)
                return math.normalizesafe(hintedNormal, new float3(0f, 1f, 0f));

            float3 nA = math.normalizesafe(c.portalA.entryNormal, float3.zero);
            float3 nB = math.normalizesafe(c.portalB.entryNormal, float3.zero);
            float3 wallNormal = nA + nB;
            if (math.lengthsq(wallNormal) < 1e-8f)
                wallNormal = math.lengthsq(nA) > 1e-8f ? nA : nB;
            wallNormal = ProjectSutureLineVectorAwayFromAxis(wallNormal, lineDir);
            wallNormal = math.normalizesafe(wallNormal, new float3(0f, 1f, 0f));
            return wallNormal;
        }

        private static float3 ProjectFoldDirection(float3 candidate, float3 wallNormal, float3 lineDir)
        {
            candidate -= wallNormal * math.dot(candidate, wallNormal);
            candidate -= lineDir * math.dot(candidate, lineDir);
            return math.normalizesafe(candidate, float3.zero);
        }

        private int CountSutureLineFoldColumns(SutureLineConstraintInfo c, int maxColumns)
        {
            if (!_crossLayerLinks.IsCreated || !_sutureLinePatchVertices.IsCreated)
                return 0;

            int remaining = math.max(0, maxColumns);
            int count = CountSutureLinePatchFoldColumns(c.patchStartA, c.patchCountA, remaining);
            remaining -= count;
            if (remaining > 0)
                count += CountSutureLinePatchFoldColumns(c.patchStartB, c.patchCountB, remaining);
            return count;
        }

        private int CountSutureLinePatchFoldColumns(int patchStart, int patchCount, int maxColumns)
        {
            if (maxColumns <= 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > _sutureLinePatchVertices.Length)
                return 0;

            int count = 0;
            for (int i = 0; i < _crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = _crossLayerLinks[i];
                if (TryGetSutureLinePatchFoldWeight(patchStart, patchCount, link.a, out _) &&
                    TryGetSutureLinePatchFoldWeight(patchStart, patchCount, link.b, out _))
                {
                    count++;
                    if (count >= maxColumns)
                        return count;
                }
            }
            return count;
        }

        private int CountSutureLineFoldPatchVertices(SutureLineConstraintInfo c, int maxVertices)
        {
            int remaining = math.max(0, maxVertices);
            int count = CountSutureLinePatchFoldVertices(c.patchStartA, c.patchCountA, remaining);
            remaining -= count;
            if (remaining > 0)
                count += CountSutureLinePatchFoldVertices(c.patchStartB, c.patchCountB, remaining);
            return count;
        }

        private int CountSutureLinePatchFoldVertices(int patchStart, int patchCount, int maxVertices)
        {
            if (maxVertices <= 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > _sutureLinePatchVertices.Length)
                return 0;

            int count = 0;
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = _sutureLinePatchVertices[patchStart + i];
                if (GetSutureLinePatchFoldWeight(pv) <= 0f)
                    continue;

                count++;
                if (count >= maxVertices)
                    return count;
            }
            return count;
        }

        private bool TryGetSutureLinePatchFoldWeight(int patchStart, int patchCount, int vertexIndex, out float weight)
        {
            weight = 0f;
            if (vertexIndex < 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > _sutureLinePatchVertices.Length)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = _sutureLinePatchVertices[patchStart + i];
                if (pv.vertexIndex != vertexIndex)
                    continue;

                weight = GetSutureLinePatchFoldWeight(pv);
                return weight > 0f;
            }
            return false;
        }

        private static float GetSutureLinePatchFoldWeight(SutureLinePatchVertexInfo pv)
        {
            float weight = math.max(0f, pv.weight);
            if ((pv.flags & SutureLinePatchVertexInfo.FlagPullAnchor) != 0)
                return 0f;
            if ((pv.flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0 &&
                (pv.flags & SutureLinePatchVertexInfo.FlagFoldColumn) != 0)
            {
                float middle01 = 1f - math.abs((math.saturate(pv.bandT) - 0.5f) * 2f);
                return weight * math.lerp(0.55f, 1f, math.saturate(middle01));
            }

            return (pv.flags & SutureLinePatchVertexInfo.FlagFoldColumn) != 0 ? weight : 0f;
        }

        private bool SutureLinePatchContainsVertex(int patchStart, int patchCount, int vertexIndex)
        {
            if (vertexIndex < 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > _sutureLinePatchVertices.Length)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                if (_sutureLinePatchVertices[patchStart + i].vertexIndex == vertexIndex)
                    return true;
            }
            return false;
        }

        private SutureLineSelfContactEstimate EstimateSutureLineSelfContactState(SutureLineConstraintInfo c)
        {
            SutureLineSelfContactEstimate result = new SutureLineSelfContactEstimate()
            {
                minVTDistance = float.PositiveInfinity,
                minEEDistance = float.PositiveInfinity,
            };
            if (!_sutureLinePatchVertices.IsCreated || !_sutureLineContactTriangles.IsCreated || !_predictPositions.IsCreated || !_masses.IsCreated)
                return result;
            if (c.selfContactThickness <= 1e-7f)
                return result;

            float stiffness = _setting != null ? math.saturate(_setting.sutureLineSelfContactNormalStiffness) : 0.85f;
            float maxPush = _setting != null ? math.max(0f, _setting.sutureLineSelfContactMaxPushPerIteration) : 0.0025f;
            int bandBudget = math.max(12, (c.closureBandCountA + c.closureBandCountB) * 2);
            int vtBudget = _setting != null ? math.min(math.max(8, _setting.sutureLineSelfContactMaxVTPairs), math.max(12, bandBudget * 2)) : 48;
            bool enableEE = _setting != null && _setting.enableSutureLineSelfContactEE;
            int eeBudget = enableEE && _setting != null ? math.min(math.max(0, _setting.sutureLineSelfContactMaxEEPairs), math.max(0, bandBudget / 3)) : 0;
            int triPairCount = c.contactTriCountA > 0 && c.contactTriCountB > 0 && c.contactTriCountA <= int.MaxValue / c.contactTriCountB
                ? c.contactTriCountA * c.contactTriCountB
                : int.MaxValue;
            int triPairBudget = _setting != null
                ? math.min(math.max(16, _setting.sutureLineLocalContactMaxTriangleChecksPerLine), math.max(16, triPairCount))
                : math.max(16, triPairCount);

            EstimateSelfContactTrianglePairSet(
                c.contactTriStartA,
                c.contactTriCountA,
                c.contactTriStartB,
                c.contactTriCountB,
                c.selfContactThickness,
                stiffness,
                maxPush,
                c.contactNormalHint,
                c.closureDirection,
                ref result,
                triPairBudget,
                vtBudget,
                eeBudget);
            result.tangentPush = 0f;
            return result;
        }

        private void EstimateSelfContactTrianglePairSet(
            int triStartA,
            int triCountA,
            int triStartB,
            int triCountB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref SutureLineSelfContactEstimate result,
            int triPairBudget,
            int vtBudget,
            int eeBudget)
        {
            if (triStartA < 0 || triCountA <= 0 || triStartA + triCountA > _sutureLineContactTriangles.Length ||
                triStartB < 0 || triCountB <= 0 || triStartB + triCountB > _sutureLineContactTriangles.Length)
                return;

            int checkedPairs = 0;
            for (int ai = 0; ai < triCountA; ai++)
            {
                SutureLineContactTriangleInfo triA = _sutureLineContactTriangles[triStartA + ai];
                if (!IsValidSutureLineContactTriangle(triA))
                    continue;

                for (int bi = 0; bi < triCountB; bi++)
                {
                    if (checkedPairs >= triPairBudget || (result.vtPairs >= vtBudget && (eeBudget <= 0 || result.eePairs >= eeBudget)))
                        return;
                    checkedPairs++;

                    SutureLineContactTriangleInfo triB = _sutureLineContactTriangles[triStartB + bi];
                    if (!IsValidSutureLineContactTriangle(triB) || SutureLineTrianglesShareVertex(triA, triB))
                        continue;
                    bool opposingVirtualSidewalls = IsOpposingSutureLineVirtualSidewallTrianglePair(triA, triB);
                    float pairThickness = opposingVirtualSidewalls
                        ? ComputeSutureLineVirtualSidewallContactSkin(thickness)
                        : thickness;
                    if (!ShouldConsiderSutureLineSelfContactTrianglePair(triA, triB, pairThickness, closureAxis))
                        continue;

                    bool swept;
                    if (!IsSutureLineTrianglePairNearOrSwept(triA, triB, pairThickness, out swept))
                        continue;
                    if (opposingVirtualSidewalls &&
                        !IsMutualBestSutureLineVirtualSidewallPair(triA, triB, triStartA, triCountA, triStartB, triCountB, pairThickness, closureAxis))
                        continue;

                    result.triPairs++;
                    if (swept)
                    {
                        result.sweptCandidates++;
                        result.ccdHit = 1;
                    }

                    float pairWeight = math.saturate(math.sqrt(math.max(0f, triA.weight) * math.max(0f, triB.weight)));
                    if (pairWeight <= 1e-5f)
                        continue;
                    float pairStiffness = stiffness * pairWeight;

                    EstimateTriTriContactManifold(triA, triB, pairThickness, pairStiffness, maxPush, fallbackNormal, closureAxis, ref result, vtBudget, eeBudget);
                }
            }
        }

        private void EstimateTriTriContactManifold(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref SutureLineSelfContactEstimate result,
            int vtBudget,
            int eeBudget)
        {
            if (IsOpposingSutureLineVirtualSidewallTrianglePair(triA, triB))
            {
                if (result.vtPairs < vtBudget)
                    EstimateVirtualSidewallTriangleBarrier(triA, triB, thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref result);
                return;
            }

            if (result.vtPairs < vtBudget) EstimateSignedSelfContactVT(triA.v0, triB, thickness, stiffness, maxPush, fallbackNormal, ref result, vtBudget);
            if (result.vtPairs < vtBudget) EstimateSignedSelfContactVT(triA.v1, triB, thickness, stiffness, maxPush, fallbackNormal, ref result, vtBudget);
            if (result.vtPairs < vtBudget) EstimateSignedSelfContactVT(triA.v2, triB, thickness, stiffness, maxPush, fallbackNormal, ref result, vtBudget);
            if (result.vtPairs < vtBudget) EstimateSignedSelfContactVT(triB.v0, triA, thickness, stiffness, maxPush, -fallbackNormal, ref result, vtBudget);
            if (result.vtPairs < vtBudget) EstimateSignedSelfContactVT(triB.v1, triA, thickness, stiffness, maxPush, -fallbackNormal, ref result, vtBudget);
            if (result.vtPairs < vtBudget) EstimateSignedSelfContactVT(triB.v2, triA, thickness, stiffness, maxPush, -fallbackNormal, ref result, vtBudget);

            if (eeBudget <= 0 || result.eePairs >= eeBudget)
                return;

            EstimateTriTriLimitedEE(triA, triB, thickness, stiffness, maxPush, fallbackNormal, ref result, eeBudget);
        }

        private void EstimateVirtualSidewallTriangleBarrier(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref SutureLineSelfContactEstimate result)
        {
            float3 centerA = GetSutureLineTriangleCenter(triA);
            float3 centerB = GetSutureLineTriangleCenter(triB);
            float3 delta = centerA - centerB;
            float3 axis = math.normalizesafe(closureAxis, math.normalizesafe(fallbackNormal, new float3(1f, 0f, 0f)));
            if (math.lengthsq(axis) < 1e-8f)
                return;

            float axisDistance = math.abs(math.dot(delta, axis));
            result.minVTDistance = math.min(result.minVTDistance, axisDistance);
            if (axisDistance >= thickness)
                return;

            float denom =
                (GetSutureLineContactInvMass(triA.v0) + GetSutureLineContactInvMass(triA.v1) + GetSutureLineContactInvMass(triA.v2) +
                 GetSutureLineContactInvMass(triB.v0) + GetSutureLineContactInvMass(triB.v1) + GetSutureLineContactInvMass(triB.v2)) / 9f;
            if (denom <= 1e-8f)
                return;

            float depth = thickness - axisDistance;
            float depth01 = math.saturate(depth / math.max(thickness, 1e-6f));
            float smoothDepth = depth * depth01 * depth01;
            float sidewallStiffness = math.min(stiffness, 0.25f) * 0.18f;
            float push = (smoothDepth * sidewallStiffness) / denom;
            float sidewallMaxPush = maxPush > 0f
                ? math.min(maxPush * 0.08f, thickness * 0.18f)
                : thickness * 0.18f;
            push = math.min(push, sidewallMaxPush);
            if (push <= 1e-9f)
                return;

            result.normalPush = math.max(result.normalPush, push);
            result.vtPairs++;
        }

        private void EstimateSignedSelfContactVT(
            int vertex,
            SutureLineContactTriangleInfo tri,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            ref SutureLineSelfContactEstimate result,
            int pairBudget)
        {
            if (result.vtPairs >= pairBudget ||
                !IsValidSutureLineContactVertex(vertex) ||
                vertex == tri.v0 || vertex == tri.v1 || vertex == tri.v2)
                return;

            float3 p = _predictPositions[vertex];
            float3 a = _predictPositions[tri.v0];
            float3 b = _predictPositions[tri.v1];
            float3 c = _predictPositions[tri.v2];
            float3 triNormal = GetSutureLineTriangleNormal(a, b, c, fallbackNormal);
            float signedDistance = math.dot(p - a, triNormal);
            float3 projected = p - triNormal * signedDistance;
            float3 bary = BarycentricOnTriangle(projected, a, b, c);
            bool projectedInside = bary.x >= -0.02f && bary.y >= -0.02f && bary.z >= -0.02f;
            float distance;
            if (projectedInside)
            {
                distance = math.abs(signedDistance);
            }
            else
            {
                float3 closest = ClosestPointOnTriangle(p, a, b, c, out bary);
                distance = math.distance(p, closest);
            }

            result.minVTDistance = math.min(result.minVTDistance, distance);
            bool ccd = IsSutureLineVTSweptCandidate(vertex, tri, thickness) &&
                       EstimatePreviousSutureLineVTDistance(vertex, tri) < thickness * 1.5f;
            if (ccd)
            {
                result.sweptCandidates++;
                result.ccdHit = 1;
            }
            if (distance >= thickness && !ccd)
                return;

            float invVertex = GetSutureLineContactInvMass(vertex);
            float invTri =
                GetSutureLineContactInvMass(tri.v0) * bary.x * bary.x +
                GetSutureLineContactInvMass(tri.v1) * bary.y * bary.y +
                GetSutureLineContactInvMass(tri.v2) * bary.z * bary.z;
            float denom = invVertex + invTri;
            if (denom <= 1e-8f)
                return;

            float effectiveDistance = ccd && distance >= thickness ? thickness * 0.75f : distance;
            float push = ((thickness - effectiveDistance) * stiffness) / denom;
            if (maxPush > 0f && push > maxPush)
            {
                push = maxPush;
                result.maxPushClamped = 1;
            }
            if (push <= 1e-9f)
                return;

            result.normalPush = math.max(result.normalPush, push);
            result.vtPairs++;
        }

        private void EstimateTriTriLimitedEE(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            ref SutureLineSelfContactEstimate result,
            int eeBudget)
        {
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 0), GetSutureLineTriangleEdge(triB, 0), thickness, stiffness, maxPush, ref result, eeBudget);
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 0), GetSutureLineTriangleEdge(triB, 1), thickness, stiffness, maxPush, ref result, eeBudget);
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 0), GetSutureLineTriangleEdge(triB, 2), thickness, stiffness, maxPush, ref result, eeBudget);
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 1), GetSutureLineTriangleEdge(triB, 0), thickness, stiffness, maxPush, ref result, eeBudget);
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 1), GetSutureLineTriangleEdge(triB, 1), thickness, stiffness, maxPush, ref result, eeBudget);
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 1), GetSutureLineTriangleEdge(triB, 2), thickness, stiffness, maxPush, ref result, eeBudget);
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 2), GetSutureLineTriangleEdge(triB, 0), thickness, stiffness, maxPush, ref result, eeBudget);
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 2), GetSutureLineTriangleEdge(triB, 1), thickness, stiffness, maxPush, ref result, eeBudget);
            EstimateLimitedEE(GetSutureLineTriangleEdge(triA, 2), GetSutureLineTriangleEdge(triB, 2), thickness, stiffness, maxPush, ref result, eeBudget);
        }

        private void EstimateLimitedEE(
            SutureLineContactEdgeInfo ea,
            SutureLineContactEdgeInfo eb,
            float thickness,
            float stiffness,
            float maxPush,
            ref SutureLineSelfContactEstimate result,
            int pairBudget)
        {
            if (result.eePairs >= pairBudget ||
                !IsValidSutureLineContactEdge(ea) ||
                !IsValidSutureLineContactEdge(eb) ||
                ea.v0 == eb.v0 || ea.v0 == eb.v1 || ea.v1 == eb.v0 || ea.v1 == eb.v1)
                return;

            ClosestPointsOnSegments(_predictPositions[ea.v0], _predictPositions[ea.v1], _predictPositions[eb.v0], _predictPositions[eb.v1], out float sa, out float sb, out float3 pa, out float3 pb);
            float distance = math.distance(pa, pb);
            result.minEEDistance = math.min(result.minEEDistance, distance);
            bool ccd = AreSutureLineEdgesSweptCandidates(ea, eb, thickness) &&
                       EstimatePreviousSutureLineEEDistance(ea, eb) < thickness * 1.5f;
            if (ccd)
            {
                result.sweptCandidates++;
                result.ccdHit = 1;
            }
            if (distance >= thickness && !ccd)
                return;

            float wa0 = 1f - sa;
            float wa1 = sa;
            float wb0 = 1f - sb;
            float wb1 = sb;
            float denom =
                GetSutureLineContactInvMass(ea.v0) * wa0 * wa0 +
                GetSutureLineContactInvMass(ea.v1) * wa1 * wa1 +
                GetSutureLineContactInvMass(eb.v0) * wb0 * wb0 +
                GetSutureLineContactInvMass(eb.v1) * wb1 * wb1;
            if (denom <= 1e-8f)
                return;

            float effectiveDistance = ccd && distance >= thickness ? thickness * 0.75f : distance;
            float push = ((thickness - effectiveDistance) * stiffness) / denom;
            if (maxPush > 0f && push > maxPush)
            {
                push = maxPush;
                result.maxPushClamped = 1;
            }
            if (push <= 1e-9f)
                return;

            result.normalPush = math.max(result.normalPush, push);
            result.eePairs++;
        }

        private bool IsSutureLineTrianglePairNearOrSwept(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float inflate,
            out bool swept)
        {
            swept = false;
            GetSutureLineTriangleAabb(triA, inflate, false, out float3 minA, out float3 maxA);
            GetSutureLineTriangleAabb(triB, inflate, false, out float3 minB, out float3 maxB);
            if (SutureLineAabbOverlap(minA, maxA, minB, maxB))
                return true;

            if (!_meshModifier.vertices.IsCreated)
                return false;

            GetSutureLineTriangleAabb(triA, inflate, true, out minA, out maxA);
            GetSutureLineTriangleAabb(triB, inflate, true, out minB, out maxB);
            swept = SutureLineAabbOverlap(minA, maxA, minB, maxB);
            return swept;
        }

        private bool ShouldConsiderSutureLineSelfContactTrianglePair(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float3 closureAxis)
        {
            bool virtualA = IsSutureLineVirtualSidewallContactTriangle(triA);
            bool virtualB = IsSutureLineVirtualSidewallContactTriangle(triB);
            bool opposingVirtualSidewalls = IsOpposingSutureLineVirtualSidewallTrianglePair(triA, triB);
            if (virtualA && virtualB && !opposingVirtualSidewalls)
                return false;

            if (!virtualA && !virtualB)
            {
                bool closureA = IsSutureLineClosureBandContactTriangle(triA);
                bool closureB = IsSutureLineClosureBandContactTriangle(triB);
                if (closureA && closureB)
                {
                    float minBandT = math.min(triA.bandT, triB.bandT);
                    if (minBandT < 0.16f)
                        return false;
                    if (math.abs(triA.bandT - triB.bandT) > 0.46f)
                        return false;
                }
            }

            float3 centerA = GetSutureLineTriangleCenter(triA);
            float3 centerB = GetSutureLineTriangleCenter(triB);
            float3 delta = centerB - centerA;
            float contactGap = _setting != null ? math.max(0f, _setting.sutureLineContactGap) : 0.006f;
            float maxDistance = opposingVirtualSidewalls
                ? math.max(EstimateSutureLineTriangleRadius(triA) + EstimateSutureLineTriangleRadius(triB) + thickness * 2f, 1e-4f)
                : math.max(math.max(thickness * 5f, contactGap * 3f), 1e-4f);
            if (math.lengthsq(delta) > maxDistance * maxDistance)
                return false;

            float3 axis = math.normalizesafe(closureAxis, float3.zero);
            if (!opposingVirtualSidewalls && math.lengthsq(axis) > 1e-8f)
            {
                float along = math.abs(math.dot(delta, axis));
                float maxAlong = math.max(math.max(thickness * 2.5f, contactGap * 1.5f), 1e-4f);
                if (along > maxAlong)
                    return false;
            }

            return true;
        }

        private static bool IsSutureLineClosureBandContactTriangle(SutureLineContactTriangleInfo tri)
        {
            return tri.closureBandHits > 0 &&
                (tri.flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0;
        }

        private static bool IsSutureLineVirtualSidewallContactTriangle(SutureLineContactTriangleInfo tri)
        {
            return (tri.flags & SutureLineContactTriangleInfo.FlagVirtualSidewall) != 0;
        }

        private static bool IsOpposingSutureLineVirtualSidewallTrianglePair(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB)
        {
            return IsSutureLineVirtualSidewallContactTriangle(triA) &&
                IsSutureLineVirtualSidewallContactTriangle(triB) &&
                triA.sourceSide != 0 &&
                triB.sourceSide != 0 &&
                triA.sourceSide != triB.sourceSide;
        }

        private float3 GetSutureLineTriangleCenter(SutureLineContactTriangleInfo tri)
        {
            return (_predictPositions[tri.v0] + _predictPositions[tri.v1] + _predictPositions[tri.v2]) / 3f;
        }

        private bool IsMutualBestSutureLineVirtualSidewallPair(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            int triStartA,
            int triCountA,
            int triStartB,
            int triCountB,
            float thickness,
            float3 closureAxis)
        {
            if (!TryScoreSutureLineVirtualSidewallPair(triA, triB, thickness, closureAxis, out float pairScore))
                return false;

            float epsilon = math.max(1e-8f, pairScore * 1e-4f);
            for (int bi = 0; bi < triCountB; bi++)
            {
                SutureLineContactTriangleInfo candidate = _sutureLineContactTriangles[triStartB + bi];
                if (candidate.v0 == triB.v0 && candidate.v1 == triB.v1 && candidate.v2 == triB.v2)
                    continue;
                if (TryScoreSutureLineVirtualSidewallPair(triA, candidate, thickness, closureAxis, out float score) &&
                    score + epsilon < pairScore)
                    return false;
            }

            for (int ai = 0; ai < triCountA; ai++)
            {
                SutureLineContactTriangleInfo candidate = _sutureLineContactTriangles[triStartA + ai];
                if (candidate.v0 == triA.v0 && candidate.v1 == triA.v1 && candidate.v2 == triA.v2)
                    continue;
                if (TryScoreSutureLineVirtualSidewallPair(candidate, triB, thickness, closureAxis, out float score) &&
                    score + epsilon < pairScore)
                    return false;
            }

            return true;
        }

        private bool TryScoreSutureLineVirtualSidewallPair(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float3 closureAxis,
            out float score)
        {
            score = float.PositiveInfinity;
            bool swept;
            if (!IsValidSutureLineContactTriangle(triA) ||
                !IsValidSutureLineContactTriangle(triB) ||
                SutureLineTrianglesShareVertex(triA, triB) ||
                !IsOpposingSutureLineVirtualSidewallTrianglePair(triA, triB) ||
                !ShouldConsiderSutureLineSelfContactTrianglePair(triA, triB, thickness, closureAxis) ||
                !IsSutureLineTrianglePairNearOrSwept(triA, triB, thickness, out swept))
                return false;

            float3 delta = GetSutureLineTriangleCenter(triB) - GetSutureLineTriangleCenter(triA);
            float3 axis = math.normalizesafe(closureAxis, float3.zero);
            float along = math.lengthsq(axis) > 1e-8f ? math.dot(delta, axis) : 0f;
            float3 tangent = delta - axis * along;
            score = math.lengthsq(tangent) + along * along * 0.25f;
            return true;
        }

        private float EstimateSutureLineTriangleRadius(SutureLineContactTriangleInfo tri)
        {
            float3 center = GetSutureLineTriangleCenter(tri);
            float r0 = math.length(_predictPositions[tri.v0] - center);
            float r1 = math.length(_predictPositions[tri.v1] - center);
            float r2 = math.length(_predictPositions[tri.v2] - center);
            return math.max(r0, math.max(r1, r2));
        }

        private void GetSutureLineTriangleAabb(SutureLineContactTriangleInfo tri, float inflate, bool swept, out float3 minPoint, out float3 maxPoint)
        {
            float3 p0 = _predictPositions[tri.v0];
            float3 p1 = _predictPositions[tri.v1];
            float3 p2 = _predictPositions[tri.v2];
            minPoint = math.min(math.min(p0, p1), p2);
            maxPoint = math.max(math.max(p0, p1), p2);
            if (swept)
            {
                float3 q0 = _meshModifier.vertices[tri.v0];
                float3 q1 = _meshModifier.vertices[tri.v1];
                float3 q2 = _meshModifier.vertices[tri.v2];
                minPoint = math.min(minPoint, math.min(math.min(q0, q1), q2));
                maxPoint = math.max(maxPoint, math.max(math.max(q0, q1), q2));
            }
            minPoint -= new float3(inflate);
            maxPoint += new float3(inflate);
        }

        private bool SutureLineTrianglesShareVertex(SutureLineContactTriangleInfo a, SutureLineContactTriangleInfo b)
        {
            return a.v0 == b.v0 || a.v0 == b.v1 || a.v0 == b.v2 ||
                   a.v1 == b.v0 || a.v1 == b.v1 || a.v1 == b.v2 ||
                   a.v2 == b.v0 || a.v2 == b.v1 || a.v2 == b.v2;
        }

        private SutureLineContactEdgeInfo GetSutureLineTriangleEdge(SutureLineContactTriangleInfo tri, int edgeIndex)
        {
            switch (edgeIndex)
            {
                case 0:
                    return new SutureLineContactEdgeInfo { v0 = tri.v0, v1 = tri.v1, weight = 1f };
                case 1:
                    return new SutureLineContactEdgeInfo { v0 = tri.v1, v1 = tri.v2, weight = 1f };
                default:
                    return new SutureLineContactEdgeInfo { v0 = tri.v2, v1 = tri.v0, weight = 1f };
            }
        }

        private float3 GetSutureLineTriangleNormal(float3 a, float3 b, float3 c, float3 fallbackNormal)
        {
            float3 fallback = math.normalizesafe(fallbackNormal, new float3(1f, 0f, 0f));
            float3 normal = math.normalizesafe(math.cross(b - a, c - a), fallback);
            return math.dot(normal, fallback) < 0f ? -normal : normal;
        }

        private float3 BarycentricOnTriangle(float3 p, float3 a, float3 b, float3 c)
        {
            float3 v0 = b - a;
            float3 v1 = c - a;
            float3 v2 = p - a;
            float d00 = math.dot(v0, v0);
            float d01 = math.dot(v0, v1);
            float d11 = math.dot(v1, v1);
            float d20 = math.dot(v2, v0);
            float d21 = math.dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            if (math.abs(denom) <= 1e-8f)
                return new float3(-1f, -1f, -1f);
            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            return new float3(1f - v - w, v, w);
        }

        private void EstimateSelfContactVTSet(
            int triStart,
            int triCount,
            int patchStart,
            int patchCount,
            float thickness,
            float stiffness,
            float maxPush,
            ref SutureLineSelfContactEstimate result,
            int pairBudget)
        {
            if (triStart < 0 || triCount <= 0 || triStart + triCount > _sutureLineContactTriangles.Length ||
                patchStart < 0 || patchCount <= 0 || patchStart + patchCount > _sutureLinePatchVertices.Length)
                return;

            int checkedPairs = 0;
            for (int ti = 0; ti < triCount; ti++)
            {
                if (result.vtPairs >= pairBudget)
                    return;
                SutureLineContactTriangleInfo tri = _sutureLineContactTriangles[triStart + ti];
                if (!IsValidSutureLineContactTriangle(tri))
                    continue;

                for (int pi = 0; pi < patchCount; pi++)
                {
                    if (result.vtPairs >= pairBudget)
                        return;
                    checkedPairs++;

                    SutureLinePatchVertexInfo pv = _sutureLinePatchVertices[patchStart + pi];
                    if (!IsValidSutureLineContactVertex(pv.vertexIndex) ||
                        pv.vertexIndex == tri.v0 ||
                        pv.vertexIndex == tri.v1 ||
                        pv.vertexIndex == tri.v2)
                        continue;

                    float3 p = _predictPositions[pv.vertexIndex];
                    float3 closest = ClosestPointOnTriangle(p, _predictPositions[tri.v0], _predictPositions[tri.v1], _predictPositions[tri.v2], out float3 bary);
                    float distance = math.distance(p, closest);
                    result.minVTDistance = math.min(result.minVTDistance, distance);
                    bool ccd = IsSutureLineVTSweptCandidate(pv.vertexIndex, tri, thickness) &&
                               EstimatePreviousSutureLineVTDistance(pv.vertexIndex, tri) < thickness * 1.5f;
                    if (ccd)
                    {
                        result.sweptCandidates++;
                        result.ccdHit = 1;
                    }
                    if (distance >= thickness && !ccd)
                        continue;

                    float invVertex = GetSutureLineContactInvMass(pv.vertexIndex);
                    float invTri =
                        GetSutureLineContactInvMass(tri.v0) * bary.x * bary.x +
                        GetSutureLineContactInvMass(tri.v1) * bary.y * bary.y +
                        GetSutureLineContactInvMass(tri.v2) * bary.z * bary.z;
                    float denom = invVertex + invTri;
                    if (denom <= 1e-8f)
                        continue;

                    float effectiveDistance = ccd && distance >= thickness ? thickness * 0.75f : distance;
                    float pairWeight = math.saturate(math.sqrt(math.max(0f, pv.weight) * math.max(0f, tri.weight)));
                    float push = ((thickness - effectiveDistance) * stiffness * pairWeight) / denom;
                    if (maxPush > 0f && push > maxPush)
                    {
                        push = maxPush;
                        result.maxPushClamped = 1;
                    }
                    if (push <= 1e-9f)
                        continue;

                    result.normalPush = math.max(result.normalPush, push);
                    result.vtPairs++;
                }
            }
        }

        private float EstimatePreviousSutureLineVTDistance(int vertex, SutureLineContactTriangleInfo tri)
        {
            if (!_meshModifier.vertices.IsCreated)
                return float.PositiveInfinity;
            float3 p = _meshModifier.vertices[vertex];
            float3 closest = ClosestPointOnTriangle(p, _meshModifier.vertices[tri.v0], _meshModifier.vertices[tri.v1], _meshModifier.vertices[tri.v2], out _);
            return math.distance(p, closest);
        }

        private float EstimatePreviousSutureLineEEDistance(SutureLineContactEdgeInfo ea, SutureLineContactEdgeInfo eb)
        {
            if (!_meshModifier.vertices.IsCreated)
                return float.PositiveInfinity;
            ClosestPointsOnSegments(_meshModifier.vertices[ea.v0], _meshModifier.vertices[ea.v1], _meshModifier.vertices[eb.v0], _meshModifier.vertices[eb.v1], out _, out _, out float3 pa, out float3 pb);
            return math.distance(pa, pb);
        }

        private bool IsSutureLineVTSweptCandidate(int vertex, SutureLineContactTriangleInfo tri, float inflate)
        {
            if (!_meshModifier.vertices.IsCreated)
                return false;
            float3 minV = math.min(_meshModifier.vertices[vertex], _predictPositions[vertex]) - new float3(inflate);
            float3 maxV = math.max(_meshModifier.vertices[vertex], _predictPositions[vertex]) + new float3(inflate);
            float3 minT = math.min(math.min(_meshModifier.vertices[tri.v0], _meshModifier.vertices[tri.v1]), _meshModifier.vertices[tri.v2]);
            minT = math.min(minT, math.min(math.min(_predictPositions[tri.v0], _predictPositions[tri.v1]), _predictPositions[tri.v2])) - new float3(inflate);
            float3 maxT = math.max(math.max(_meshModifier.vertices[tri.v0], _meshModifier.vertices[tri.v1]), _meshModifier.vertices[tri.v2]);
            maxT = math.max(maxT, math.max(math.max(_predictPositions[tri.v0], _predictPositions[tri.v1]), _predictPositions[tri.v2])) + new float3(inflate);
            return SutureLineAabbOverlap(minV, maxV, minT, maxT);
        }

        private bool AreSutureLineEdgesSweptCandidates(SutureLineContactEdgeInfo ea, SutureLineContactEdgeInfo eb, float inflate)
        {
            if (!_meshModifier.vertices.IsCreated)
                return false;
            GetSutureLineSweptEdgeAabb(ea, inflate, out float3 minA, out float3 maxA);
            GetSutureLineSweptEdgeAabb(eb, inflate, out float3 minB, out float3 maxB);
            return SutureLineAabbOverlap(minA, maxA, minB, maxB);
        }

        private void GetSutureLineSweptEdgeAabb(SutureLineContactEdgeInfo edge, float inflate, out float3 minPoint, out float3 maxPoint)
        {
            minPoint = math.min(math.min(_meshModifier.vertices[edge.v0], _meshModifier.vertices[edge.v1]), math.min(_predictPositions[edge.v0], _predictPositions[edge.v1])) - new float3(inflate);
            maxPoint = math.max(math.max(_meshModifier.vertices[edge.v0], _meshModifier.vertices[edge.v1]), math.max(_predictPositions[edge.v0], _predictPositions[edge.v1])) + new float3(inflate);
        }

        private static bool SutureLineAabbOverlap(float3 minA, float3 maxA, float3 minB, float3 maxB)
        {
            return minA.x <= maxB.x && maxA.x >= minB.x &&
                   minA.y <= maxB.y && maxA.y >= minB.y &&
                   minA.z <= maxB.z && maxA.z >= minB.z;
        }

        private bool IsValidSutureLineContactEdge(SutureLineContactEdgeInfo edge)
        {
            return IsValidSutureLineContactVertex(edge.v0) && IsValidSutureLineContactVertex(edge.v1) && edge.v0 != edge.v1;
        }

        private static void ClosestPointsOnSegments(
            float3 p1,
            float3 q1,
            float3 p2,
            float3 q2,
            out float s,
            out float t,
            out float3 c1,
            out float3 c2)
        {
            float3 d1 = q1 - p1;
            float3 d2 = q2 - p2;
            float3 r = p1 - p2;
            float a = math.dot(d1, d1);
            float e = math.dot(d2, d2);
            float f = math.dot(d2, r);

            if (a <= 1e-8f && e <= 1e-8f)
            {
                s = 0f;
                t = 0f;
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

        private float EstimateSutureLineLocalPatchContactPush(
            SutureLineConstraintInfo c,
            float contactGap,
            float contactStiffness,
            float contactMaxPush,
            int maxPairChecks,
            out int contactPairs)
        {
            contactPairs = 0;
            if (!_sutureLinePatchVertices.IsCreated || !_predictPositions.IsCreated || !_masses.IsCreated)
                return 0f;
            if (contactGap <= 0f || contactStiffness <= 0f ||
                c.patchCountA <= 0 || c.patchCountB <= 0 ||
                c.patchStartA < 0 || c.patchStartB < 0 ||
                c.patchStartA + c.patchCountA > _sutureLinePatchVertices.Length ||
                c.patchStartB + c.patchCountB > _sutureLinePatchVertices.Length)
                return 0f;

            int checkLimit = math.max(16, maxPairChecks);
            int checks = 0;
            float maxEstimatedPush = 0f;
            for (int i = 0; i < c.patchCountA; i++)
            {
                SutureLinePatchVertexInfo a = _sutureLinePatchVertices[c.patchStartA + i];
                if (!IsValidSutureLineContactVertex(a.vertexIndex))
                    continue;

                for (int j = 0; j < c.patchCountB; j++)
                {
                    if (checks >= checkLimit)
                        return maxEstimatedPush;
                    checks++;

                    SutureLinePatchVertexInfo b = _sutureLinePatchVertices[c.patchStartB + j];
                    if (!IsValidSutureLineContactVertex(b.vertexIndex) || a.vertexIndex == b.vertexIndex)
                        continue;

                    float distance = math.distance(_predictPositions[a.vertexIndex], _predictPositions[b.vertexIndex]);
                    if (distance >= contactGap)
                        continue;

                    float invMassA = GetSutureLineContactInvMass(a.vertexIndex);
                    float invMassB = GetSutureLineContactInvMass(b.vertexIndex);
                    float denom = invMassA + invMassB;
                    if (denom <= 1e-8f)
                        continue;

                    float pairWeight = math.saturate(math.sqrt(math.max(0f, a.weight) * math.max(0f, b.weight)));
                    if (pairWeight <= 1e-5f)
                        continue;

                    float estimatedPush = ((contactGap - distance) * contactStiffness * pairWeight) / denom;
                    if (contactMaxPush > 0f)
                        estimatedPush = math.min(estimatedPush, contactMaxPush);
                    maxEstimatedPush = math.max(maxEstimatedPush, estimatedPush);
                    contactPairs++;
                }
            }

            return maxEstimatedPush;
        }

        private float EstimateSutureLineTriangleContactState(
            SutureLineConstraintInfo c,
            float contactGap,
            float contactStiffness,
            float contactMaxPush,
            int maxTriangleChecks,
            out int contactPairs,
            out float minDistance)
        {
            contactPairs = 0;
            minDistance = float.PositiveInfinity;
            if (!_sutureLinePatchVertices.IsCreated || !_sutureLineContactTriangles.IsCreated || !_predictPositions.IsCreated || !_masses.IsCreated)
                return 0f;
            if (contactGap <= 0f || contactStiffness <= 0f)
                return 0f;

            int checkBudget = math.max(16, maxTriangleChecks);
            int checks = 0;
            float maxEstimatedPush = 0f;
            maxEstimatedPush = math.max(maxEstimatedPush, EstimateSutureLineTriangleSetContact(
                c.contactTriStartA,
                c.contactTriCountA,
                c.patchStartB,
                c.patchCountB,
                contactGap,
                contactStiffness,
                contactMaxPush,
                ref checks,
                checkBudget,
                ref contactPairs,
                ref minDistance));
            if (checks >= checkBudget)
                return maxEstimatedPush;

            maxEstimatedPush = math.max(maxEstimatedPush, EstimateSutureLineTriangleSetContact(
                c.contactTriStartB,
                c.contactTriCountB,
                c.patchStartA,
                c.patchCountA,
                contactGap,
                contactStiffness,
                contactMaxPush,
                ref checks,
                checkBudget,
                ref contactPairs,
                ref minDistance));

            return maxEstimatedPush;
        }

        private float EstimateSutureLineTriangleSetContact(
            int triStart,
            int triCount,
            int patchStart,
            int patchCount,
            float contactGap,
            float contactStiffness,
            float contactMaxPush,
            ref int checks,
            int checkBudget,
            ref int contactPairs,
            ref float minDistance)
        {
            if (triStart < 0 || triCount <= 0 || triStart + triCount > _sutureLineContactTriangles.Length ||
                patchStart < 0 || patchCount <= 0 || patchStart + patchCount > _sutureLinePatchVertices.Length)
                return 0f;

            float maxEstimatedPush = 0f;
            for (int ti = 0; ti < triCount; ti++)
            {
                SutureLineContactTriangleInfo tri = _sutureLineContactTriangles[triStart + ti];
                if (!IsValidSutureLineContactTriangle(tri))
                    continue;

                for (int pi = 0; pi < patchCount; pi++)
                {
                    if (checks >= checkBudget)
                        return maxEstimatedPush;
                    checks++;

                    SutureLinePatchVertexInfo pv = _sutureLinePatchVertices[patchStart + pi];
                    if (!IsValidSutureLineContactVertex(pv.vertexIndex) ||
                        pv.vertexIndex == tri.v0 ||
                        pv.vertexIndex == tri.v1 ||
                        pv.vertexIndex == tri.v2)
                        continue;

                    float3 p = _predictPositions[pv.vertexIndex];
                    float3 a = _predictPositions[tri.v0];
                    float3 b = _predictPositions[tri.v1];
                    float3 c = _predictPositions[tri.v2];
                    float3 closest = ClosestPointOnTriangle(p, a, b, c, out float3 bary);
                    float distance = math.distance(p, closest);
                    minDistance = math.min(minDistance, distance);
                    if (distance > contactGap)
                        continue;

                    float invVertex = GetSutureLineContactInvMass(pv.vertexIndex);
                    float invTri =
                        GetSutureLineContactInvMass(tri.v0) * bary.x * bary.x +
                        GetSutureLineContactInvMass(tri.v1) * bary.y * bary.y +
                        GetSutureLineContactInvMass(tri.v2) * bary.z * bary.z;
                    float denom = invVertex + invTri;
                    if (denom <= 1e-8f)
                        continue;

                    float pairWeight = math.saturate(math.sqrt(math.max(0f, tri.weight) * math.max(0f, pv.weight)));
                    if (pairWeight <= 1e-5f)
                        continue;

                    float estimatedPush = ((contactGap - distance) * contactStiffness * pairWeight) / denom;
                    if (contactMaxPush > 0f)
                        estimatedPush = math.min(estimatedPush, contactMaxPush);
                    maxEstimatedPush = math.max(maxEstimatedPush, estimatedPush);
                    contactPairs++;
                }
            }

            return maxEstimatedPush;
        }

        private float3 ClosestPointOnTriangle(float3 p, float3 a, float3 b, float3 c, out float3 bary)
        {
            float3 ab = b - a;
            float3 ac = c - a;
            float3 ap = p - a;
            float d1 = math.dot(ab, ap);
            float d2 = math.dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                bary = new float3(1f, 0f, 0f);
                return a;
            }

            float3 bp = p - b;
            float d3 = math.dot(ab, bp);
            float d4 = math.dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                bary = new float3(0f, 1f, 0f);
                return b;
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                bary = new float3(1f - v, v, 0f);
                return a + ab * v;
            }

            float3 cp = p - c;
            float d5 = math.dot(ab, cp);
            float d6 = math.dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                bary = new float3(0f, 0f, 1f);
                return c;
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                bary = new float3(1f - w, 0f, w);
                return a + ac * w;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                bary = new float3(0f, 1f - w, w);
                return b + (c - b) * w;
            }

            float denom = 1f / (va + vb + vc);
            float vFace = vb * denom;
            float wFace = vc * denom;
            bary = new float3(1f - vFace - wFace, vFace, wFace);
            return a + ab * vFace + ac * wFace;
        }

        private bool IsValidSutureLineContactVertex(int vertex)
        {
            return vertex >= 0 && vertex < _predictPositions.Length && vertex < _masses.Length;
        }

        private bool IsValidSutureLineContactTriangle(SutureLineContactTriangleInfo tri)
        {
            return IsValidSutureLineContactVertex(tri.v0) &&
                   IsValidSutureLineContactVertex(tri.v1) &&
                   IsValidSutureLineContactVertex(tri.v2) &&
                   tri.v0 != tri.v1 && tri.v1 != tri.v2 && tri.v2 != tri.v0;
        }

        private float GetSutureLineContactInvMass(int vertex)
        {
            if (!IsValidSutureLineContactVertex(vertex))
                return 0f;
            float mass = _masses[vertex];
            return mass > 1e-8f ? 1f / mass : 0f;
        }

        // ---------------- Collision ----------------

        private JobHandle StartCollisionDetectionJob(float dt)
        {
            UpdateColliderDescs();
            _needleTipPiercerFeedbacks.Clear();
            CollectCollisionCandidatesByBVH();
            CollectTriangleSurfaceContactCandidatesByBVH();
            UpdateNeedlePunctureStateMainThread(dt);

            bool enableTipPiercer = _setting != null && _setting.enableNeedleTipPiercer;
            bool enableLegacyNeedleIndent = _setting != null && _setting.enableNeedleTipIndentation;
            bool enableNeedleIndent = enableLegacyNeedleIndent || enableTipPiercer;
            float needleIndentRadius = 0f;
            if (_setting != null)
            {
                needleIndentRadius = enableTipPiercer
                    ? math.max(_setting.needleTipInfluenceRadius, _setting.needleTipPiercerPatchRadius * 0.60f)
                    : _setting.needleTipInfluenceRadius;
            }

            var handle = default(JobHandle);

            // 先清空碰撞状态（对全体顶点）
            var clear = new ClearCollisionStateJob()
            {
                constraintTypes = _constraintTypes,
                collisionConstraints = _collisionConstraints,
                needleTipIndentationInfos = _needleTipIndentationInfos
            };
            handle = clear.Schedule(vertexCount, 64, handle);

            _rigidBodyForceApplys.Clear();

            // 再对候选点做 Narrowphase
            if (_collisionCandidates.Length == 0) return handle;

            // 预留容量，避免 ParallelWriter AddNoResize 越界
            if (_rigidBodyForceApplys.Capacity < _collisionCandidates.Length)
            {
                _rigidBodyForceApplys.Capacity = _collisionCandidates.Length;
            }

            var job = new CollisionCandidatesJob()
            {
                candidates = _collisionCandidates.AsArray(),
                dt = dt,
                constraintTypes = _constraintTypes,
                collidersGroup = _colliderDescGroups,
                collisionConstraints = _collisionConstraints,
                needleTipIndentationInfos = _needleTipIndentationInfos,
                masses = _masses,
                positions = _meshModifier.vertices,
                predictPositions = _predictPositions,
                rigidBodyForceApplies = _rigidBodyForceApplys,
                collisionStates = _collisionStates,
                skinEnter = _setting.collisionEnter,
                skinExit = _setting.collisionExit,
                enableNeedleTipIndentation = (byte)(enableNeedleIndent ? 1 : 0),
                needleTipInfluenceRadius = needleIndentRadius,
                enableNeedleTipPiercer = (byte)(enableTipPiercer ? 1 : 0),
                needleTipPiercerConeLength = _setting.needleTipPiercerConeLength,
                needleTipPiercerConeBaseRadiusScale = _setting.needleTipPiercerConeBaseRadiusScale,
                needleTipPiercerConeSurfaceMargin = _setting.needleTipPiercerConeSurfaceMargin,
                needleTipPiercerAxialStiffness = _setting.needleTipPiercerAxialStiffness,
            };

            return job.Schedule(handle);
        }

        private void ApplyPhysicalSimulationToRigidbodies()
        {
            for (var i = 0; i < _rigidBodyForceApplys.Length; i++)
            {
                var forceInfo = _rigidBodyForceApplys[i];
                if (!_colliderProxies.TryGetValue(forceInfo.entityId, out ICollider rigidbodyCollider) || rigidbodyCollider == null)
                    continue;

                Rigidbody rb = rigidbodyCollider.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddForce(forceInfo.velocity, ForceMode.VelocityChange);
            }

            for (int i = 0; i < _needleTipPiercerFeedbacks.Count; i++)
            {
                NeedleTipPiercerFeedbackApply feedback = _needleTipPiercerFeedbacks[i];
                bool rigApplied = NeedleSceneControlRig.TryApplyNeedleTipPiercerDamping(
                    feedback.colliderEntityId,
                    new Vector3(feedback.rigDelta.x, feedback.rigDelta.y, feedback.rigDelta.z));

                if (rigApplied)
                    continue;

                if (!_colliderProxies.TryGetValue(feedback.colliderEntityId, out ICollider piercerCollider) || piercerCollider == null)
                    continue;

                Rigidbody rb = piercerCollider.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                {
                    rb.AddForce(
                        new Vector3(feedback.velocityChange.x, feedback.velocityChange.y, feedback.velocityChange.z),
                        ForceMode.VelocityChange);
                }
            }

            _needleTipPiercerFeedbacks.Clear();
        }

        private JobHandle _jobHandle;

        public void Step(float dt)
        {
            // 让本帧所有 Job 都依赖上一帧的句柄，避免读写冲突
            var handle = _jobHandle;
            handle = CalculateNormalsJob(handle);
            handle = EstimatePositionsJob(handle, dt); // 预测位置（无约束）

            // Broadphase(BVH) 在主线程读取 predictPositions，因此先完成前置 Job。
            handle.Complete();

            // 拉线约束的 restDistance 只在主线程推进一次，避免在求解迭代里反复重置 lambda。
            UpdateSutureLineConstraintProgressMainThread(dt);

            // BVH 粗筛 + 候选点 Narrowphase
            handle = StartCollisionDetectionJob(dt);

            // 立刻发布当前句柄，避免本帧其它脚本（Gizmos/Renderer）在 Step 尚未结束时读取容器
            _jobHandle = handle;

            handle = StartConstraintsSolveJob(handle, dt);
            _jobHandle = handle;
            handle = StartUpdateVelocitiesAndPositions(handle, dt);
            _jobHandle = handle;
        }

        public void ComputeAsyncJobs()
        {
            _jobHandle.Complete();
            this.ApplyPhysicalSimulationToRigidbodies();
        }

        public void Dispose()
        {
            try { _jobHandle.Complete(); } catch { }

            _constraintTypes.Dispose();
            _velocities.Dispose();
            _predictPositions.Dispose();
            if (_layerSeparationInfos.IsCreated) _layerSeparationInfos.Dispose();
            if (_crossLayerLinks.IsCreated) _crossLayerLinks.Dispose();
            if (_crossLayerLambdas.IsCreated) _crossLayerLambdas.Dispose();
            if (_distanceLambdas.IsCreated) _distanceLambdas.Dispose();
            if (_bendLambdas.IsCreated) _bendLambdas.Dispose();
            _distanceConstraints.Dispose();
            _bendConstraints.Dispose(); _pinConstraints.Dispose();
            _collisionConstraints.Dispose();
            if (_needleTipIndentationInfos.IsCreated) _needleTipIndentationInfos.Dispose();
            if (_collisionStates.IsCreated) _collisionStates.Dispose();
            _masses.Dispose();
            _meshModifier.Dispose();
            _colliderDescGroups.Dispose();
            _rigidBodyForceApplys.Dispose();
            if (_collisionCandidates.IsCreated) _collisionCandidates.Dispose();
            if (_triangleSurfaceContactCandidates.IsCreated) _triangleSurfaceContactCandidates.Dispose();
            if (_activeNeedlePortals.IsCreated) _activeNeedlePortals.Dispose();
            if (_activeNeedleConstraintPortals.IsCreated) _activeNeedleConstraintPortals.Dispose();
            if (_activeNeedleTipHookSoftConstraints.IsCreated) _activeNeedleTipHookSoftConstraints.Dispose();
            if (_retainedHistoryHoldPortals.IsCreated) _retainedHistoryHoldPortals.Dispose();
            if (_sutureLineConstraints.IsCreated) _sutureLineConstraints.Dispose();
            if (_sutureLinePatchVertices.IsCreated) _sutureLinePatchVertices.Dispose();
            if (_sutureLineContactTriangles.IsCreated) _sutureLineContactTriangles.Dispose();
            _positionCorrects.Dispose();
        }
    }
}
