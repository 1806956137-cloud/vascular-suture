using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PBDLearn
{
    public struct DistanceConstraintInfo
    {
        public const byte FlagVirtualSidewallDistance = 1 << 0;
        public const byte FlagVirtualSidewallShear = 1 << 1;
        public const byte FlagVirtualSidewall =
            FlagVirtualSidewallDistance | FlagVirtualSidewallShear;

        public float restLength;
        public int vIndex0;
        public int vIndex1;
        /// <summary>
        /// 该距离约束的刚度倍率（默认 1）。
        /// 用于在不新增约束类型的前提下，让某些边（例如双层厚度连接）更“硬”。
        /// </summary>
        public float stiffnessMul;
        public int sourceEdgeId;
        public byte flags;
        public byte sourceSide;
    }

    public struct BendConstraintInfo
    {
        public int vIndex0;
        public int vIndex1;
        public int vIndex2;
        public int vIndex3;
        public float rest; // angle
    }

    public struct PinConstraintInfo
    {
        public float3 basePosition;
        public float3 position;
        public float stiffness;
        public float maxCorrection;
        public int clampGroupId;
        public byte mode; // 0=HardPin, 1=SoftClampPin
    }

    /// <summary>
    /// 碰撞约束
    /// </summary>
    public struct CollisionConstraintInfo
    {
        // 最近表面点（不做外推）
        public float3 surfacePoint;
        public float3 normal;
        public float3 velocity;
    }

    /// <summary>
    /// 针尖影响区的软凹陷信息。
    /// 这是“Phase 1: 先凹陷”的局部接触场，不要求粒子已经真正穿到碰撞体表面内。
    /// </summary>
    public struct NeedleTipIndentationInfo
    {
        public float3 surfacePoint;   // 针尖球帽表面点
        public float3 normal;         // 从针尖球帽指向粒子的方向（单位向量）
        public float weight;          // 0~1，越靠近针尖越大
        public byte enabled;          // 0=无效，1=有效
    }

    /// <summary>
    /// 单边不等式：外层点不能进入内层。
    /// 对每个外层点 i 记录一个内层点 innerIndex，以及初始化时的外向方向 dirOut。
    /// 约束：dot(pOuter - pInner, dirOut) >= minSep
    /// </summary>
    public struct LayerSeparationInfo
    {
        public int innerIndex;
        public float3 dirOut;   // from inner -> outer (world), unit preferred
        public float minSep;
        public byte enabled;
    }

    /// <summary>
    /// 跨层厚度连接（outer <-> inner）。
    /// XPBD 版本会为每条 link 维护一个 lambda，实现更稳定的“可压缩连接”。
    /// </summary>
    public struct CrossLayerLinkInfo
    {
        public int a;
        public int b;
        public float restLength;
    }

    /// <summary>
    /// 切口局部张口/调试用的共享数据结构。
    /// 当前这条 modified 分支仍主要由 Driver 在主线程使用它们，
    /// 先放在 Jobs 文件里统一类型定义，后续真要并行化可直接复用。
    /// </summary>
    public struct IncisionLocalConstraintInfo
    {
        public int leftIndex;
        public int rightIndex;
        public float targetOpen;
        public float weight;
        public byte layer;
    }

    public struct IncisionThicknessRelaxInfo
    {
        public float3 center;
        public float radius;
        public float keepScale;
        public byte layer;
    }


    public enum NeedlePuncturePhase : byte
    {
        None = 0,
        OuterPortal = 1,
        InWall = 2,
        InnerPortal = 3,
        InLumen = 4,
        WallCompression = 5,
        PreChannelEntry = 6,
        WholeWallBreakthrough = 7,
    }

    public enum NeedlePortalWallLayer : byte
    {
        Unknown = 0,
        Outer = 1,
        Inner = 2,
    }

    public enum NeedlePortalStitchState : byte
    {
        Unknown = 0,
        Current = 1,
        Hist = 2,
        Done = 3,
    }

    public enum NeedlePortalWallEventOwner : byte
    {
        Unknown = 0,
        WholeWall = 1,
    }

    /// <summary>
    /// 针刺阶段建立的“局部虚拟穿刺通道”。
    /// - Outer：外壁 portal，表示针从外侧进入血管壁。
    /// - Inner：内壁 portal，表示针从壁厚进入 lumen。
    /// 当前版本仍然不改拓扑；通过 portal + 局部放松 + portal 约束来做双层穿刺阶段。
    /// </summary>
    public struct NeedlePortalInfo
    {
        public int colliderEntityId;
        public int triangleIndex;
        public int v0;
        public int v1;
        public int v2;
        public float3 bary;

        public float3 entryPoint;      // 初次建立时的接触点
        public float3 currentPoint;    // 本帧 portal 中心（主线程刷新，供并行 Job 只读）
        public float3 entryNormal;     // 建立时面法线（已朝向“阻挡针尖”的一侧）
        public float3 insertDirection; // 刺入方向（通常取 needle tipForward）
        public float relaxRadius;      // portal 局部放松半径
        public float captureDepth;     // 保持开口的最小深度
        public byte enabled;
        public byte wallLayer;         // NeedlePortalWallLayer
        public byte stitchState;       // NeedlePortalStitchState：Current / Hist / Done
        public byte reserved0;
        public byte reserved1;
        public byte isProvisional;     // 1=pre-channel only, not a real puncture event / history portal.
        public byte suppressPunctureFx;
        public byte isBreakthroughEvent;
        public byte wallEventOwner;    // NeedlePortalWallEventOwner
        public byte entryLayer;        // NeedlePortalWallLayer
        public byte breakthroughLayer; // NeedlePortalWallLayer
        public byte passComplete;
        public int patchCount;
        public int patchV0;
        public int patchV1;
        public int patchV2;
        public int patchV3;
        public int patchV4;
        public int patchV5;
        public int patchV6;
        public int patchV7;
        public int patchV8;
        public int patchV9;
        public int patchV10;
        public int patchV11;
        public float patchW0;
        public float patchW1;
        public float patchW2;
        public float patchW3;
        public float patchW4;
        public float patchW5;
        public float patchW6;
        public float patchW7;
        public float patchW8;
        public float patchW9;
        public float patchW10;
        public float patchW11;
    }

    public static class NeedlePortalPatchUtil
    {
        public const int MaxPatchVertices = 12;

        public static int GetPatchVertex(NeedlePortalInfo portal, int index)
        {
            switch (index)
            {
                case 0: return portal.patchV0;
                case 1: return portal.patchV1;
                case 2: return portal.patchV2;
                case 3: return portal.patchV3;
                case 4: return portal.patchV4;
                case 5: return portal.patchV5;
                case 6: return portal.patchV6;
                case 7: return portal.patchV7;
                case 8: return portal.patchV8;
                case 9: return portal.patchV9;
                case 10: return portal.patchV10;
                case 11: return portal.patchV11;
                default: return -1;
            }
        }

        public static float GetPatchWeight(NeedlePortalInfo portal, int index)
        {
            switch (index)
            {
                case 0: return portal.patchW0;
                case 1: return portal.patchW1;
                case 2: return portal.patchW2;
                case 3: return portal.patchW3;
                case 4: return portal.patchW4;
                case 5: return portal.patchW5;
                case 6: return portal.patchW6;
                case 7: return portal.patchW7;
                case 8: return portal.patchW8;
                case 9: return portal.patchW9;
                case 10: return portal.patchW10;
                case 11: return portal.patchW11;
                default: return 0f;
            }
        }
    }

    public struct SutureLinePatchVertexInfo
    {
        public const byte FlagPullAnchor = 1 << 0;
        public const byte FlagFoldColumn = 1 << 1;
        public const byte FlagSeamAnchor = 1 << 2;
        public const byte FlagClosureBand = 1 << 3;
        public const float FoldColumnPullWeightScale = 0.45f;
        public const float SeamAnchorPullWeightScale = 0.18f;

        public int vertexIndex;
        public float weight;
        public float bandT;
        public byte flags;
    }

    public struct SutureLineContactTriangleInfo
    {
        public const byte FlagVirtualSidewall = 1 << 4;

        public int v0;
        public int v1;
        public int v2;
        public int sourceEdgeId;
        public float weight;
        public float bandT;
        public byte flags;
        public byte closureBandHits;
        public byte sourceSide;
    }

    public struct SutureSidewallBoundaryEdgeInfo
    {
        public const byte SideA = 1;
        public const byte SideB = 2;

        public int outerA;
        public int outerB;
        public int innerA;
        public int innerB;
        public int sourceEdgeId;
        public byte side;
        public byte enabled;
    }

    public struct SutureMaterialAnchor
    {
        public int vertexA;
        public int vertexB;
        public int sourceEdgeId;
        public float edgeT;
        public float captureDistance;
        public byte side;
        public NeedlePortalWallLayer layer;
        public byte valid;
    }

    public struct SutureLineContactEdgeInfo
    {
        public int v0;
        public int v1;
        public float weight;
    }

    public struct NeedleTipHookSoftConstraintInfo
    {
        public int colliderEntityId;
        public int triangleIndex;
        public float3 hookPoint;
        public float3 targetPoint;
        public float3 insertDirection;
        public float3 surfaceNormal;
        public float radius;
        public float stiffness;
        public float normalStiffness;
        public float tangentialStiffness;
        public int patchCount;
        public int patchV0;
        public int patchV1;
        public int patchV2;
        public int patchV3;
        public int patchV4;
        public int patchV5;
        public int patchV6;
        public int patchV7;
        public int patchV8;
        public int patchV9;
        public int patchV10;
        public int patchV11;
        public float patchW0;
        public float patchW1;
        public float patchW2;
        public float patchW3;
        public float patchW4;
        public float patchW5;
        public float patchW6;
        public float patchW7;
        public float patchW8;
        public float patchW9;
        public float patchW10;
        public float patchW11;
        public byte enabled;
        public byte wallLayer;
    }

    public static class NeedleTipHookPatchUtil
    {
        public const int MaxPatchVertices = NeedlePortalPatchUtil.MaxPatchVertices;

        public static int GetPatchVertex(NeedleTipHookSoftConstraintInfo hook, int index)
        {
            switch (index)
            {
                case 0: return hook.patchV0;
                case 1: return hook.patchV1;
                case 2: return hook.patchV2;
                case 3: return hook.patchV3;
                case 4: return hook.patchV4;
                case 5: return hook.patchV5;
                case 6: return hook.patchV6;
                case 7: return hook.patchV7;
                case 8: return hook.patchV8;
                case 9: return hook.patchV9;
                case 10: return hook.patchV10;
                case 11: return hook.patchV11;
                default: return -1;
            }
        }

        public static float GetPatchWeight(NeedleTipHookSoftConstraintInfo hook, int index)
        {
            switch (index)
            {
                case 0: return hook.patchW0;
                case 1: return hook.patchW1;
                case 2: return hook.patchW2;
                case 3: return hook.patchW3;
                case 4: return hook.patchW4;
                case 5: return hook.patchW5;
                case 6: return hook.patchW6;
                case 7: return hook.patchW7;
                case 8: return hook.patchW8;
                case 9: return hook.patchW9;
                case 10: return hook.patchW10;
                case 11: return hook.patchW11;
                default: return 0f;
            }
        }
    }

    public struct SutureLineConstraintInfo
    {
        public const byte LifecycleActive = 0;
        public const byte LifecycleSettledHold = 255;

        public NeedlePortalInfo portalA;
        public NeedlePortalInfo portalB;
        public int colliderEntityId;
        public int lineIndex;
        public int passIndexA;
        public int passIndexB;
        public byte wallLayer;
        public byte enabled;
        public byte weakHoldLogged;
        public byte tensionOnlyActive;
        public byte lambdaResetLastFrame;
        public byte adaptiveTargetUsed;
        public byte strongPullLine;
        public byte contactState;
        public byte contactLatched;
        public byte restFrozen;
        public byte localWallContactActive;
        public byte compressionFoldActive;
        public byte foldDirValid;
        public byte compressionFoldCandidateActive;
        public byte foldPatchFallbackSuppressed;
        public byte closureBandSlideActive;
        public byte closureBandSlideDirValid;
        public byte reserved0;
        public byte workflowReady;
        public byte workflowReadyFrames;
        public byte workflowAdvanceReady;
        public byte workflowAdvanceReadyFrames;
        public byte workflowReleased;
        public byte autoManaged;
        public byte completedWithoutRealSidewallContact;
        public int progressLogCounter;
        public int lastLocalContactPairs;
        public int lastContactTriPairs;
        public int contactMissReason;
        public int foldColumnCount;
        public int foldReason;
        public int lastSelfContactTriPairs;
        public int lastSelfContactVTPairs;
        public int lastSelfContactEEPairs;
        public int lastPersistentContactPairs;
        public int lastSweptContactCandidates;
        public int persistentContactFramesRemaining;
        public int compressionFoldHoldFramesRemaining;
        public int closureBandSlideColumnCount;
        public int lastSelfContactSolveTriPairs;
        public int lastSelfContactSolveVTPairs;
        public int lastSelfContactSolveEEPairs;
        public byte lastSelfContactCcdHit;
        public byte lastSelfContactMaxPushClamped;
        public byte lastSelfContactSolveCcdHit;
        public byte lastSelfContactSolveForced;
        public float currentRestDistance;
        public float targetRestDistance;
        public float targetDelta;
        public float lastActualDistance;
        public float lastContactPush;
        public float lastLocalContactPush;
        public float lastTriangleContactPush;
        public float lastSelfContactNormalPush;
        public float lastSelfContactTangentPush;
        public float lastSelfContactSolveNormalPush;
        public float lastSelfContactSolveMinDistance;
        public float lastSelfContactCcdAdvancePush;
        public float rawCompressionFoldPush;
        public float lastCompressionFoldPush;
        public float rawClosureBandSlidePush;
        public float lastClosureBandSlidePush;
        public float selfContactThickness;
        public float minContactDistance;
        public float minSelfContactVTDistance;
        public float minSelfContactEEDistance;
        public float biteDistanceA;
        public float biteDistanceB;
        public float localWallThickness;
        public float localAverageEdgeLength;
        public float localOpenGap;
        public float3 contactNormalHint;
        public float3 closureDirection;
        public float3 foldDir;
        public float3 closureBandSlideDir;

        // WholeWall patch pulling: each endpoint combines outer + inner subpatches.
        public int patchStartA;
        public int patchCountA;
        public int patchStartB;
        public int patchCountB;
        public int patchOuterCountA;
        public int patchInnerCountA;
        public int patchOuterCountB;
        public int patchInnerCountB;
        public int closureBandCountA;
        public int closureBandCountB;
        public int contactTriStartA;
        public int contactTriCountA;
        public int virtualSidewallTriCountA;
        public int contactTriStartB;
        public int contactTriCountB;
        public int virtualSidewallTriCountB;
        // XPBD accumulated multiplier. ClothSimulator resets it once per frame when requested.
        public float lambda;
    }


    [Unity.Burst.BurstCompile]
    public struct ClearFloatArrayJob : IJobParallelFor
    {
        public NativeArray<float> values;
        public void Execute(int index)
        {
            values[index] = 0f;
        }
    }

    /// <summary>
    /// Applies staged per-vertex corrections and clears the accumulator.
    /// This is used as a solver barrier when a later constraint must observe
    /// corrections produced by earlier serial jobs in the same iteration.
    /// </summary>
    [Unity.Burst.BurstCompile]
    public struct ApplyPositionCorrectsJob : IJobParallelFor
    {
        public NativeArray<float3> predictPositions;
        public NativeArray<float3> positionCorrects;

        public void Execute(int index)
        {
            float3 correction = positionCorrects[index];
            if (math.lengthsq(correction) > 0f)
                predictPositions[index] += correction;
            positionCorrects[index] = float3.zero;
        }
    }

    /// <summary>
    /// XPBD 跨层厚度距离约束（串行 IJob，避免并行写冲突）。
    /// 约束：|p[a] - p[b]| = restLength
    /// 
    /// XPBD 更新：
    ///   alpha = compliance / dt^2
    ///   dLambda = (-(C) - alpha*lambda) / (wSum + alpha)
    ///   pA += wA * dLambda * n
    ///   pB -= wB * dLambda * n
    ///   lambda += dLambda
    /// </summary>
    [Unity.Burst.BurstCompile]
    public struct XPBDCrossLayerDistanceJob : IJob
    {
        public NativeArray<float3> predictPositions;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeArray<CrossLayerLinkInfo> links;
        public NativeArray<float> lambdas;

        [ReadOnly] public float compliance;
        [ReadOnly] public float dt;

        public void Execute()
        {
            if (links.Length == 0) return;
            float invDt2 = dt > 1e-8f ? (1.0f / (dt * dt)) : 0f;
            float alpha = compliance * invDt2;

            for (int i = 0; i < links.Length; i++)
            {
                var link = links[i];
                int ia = link.a;
                int ib = link.b;

                float ma = masses[ia];
                float mb = masses[ib];
                float wa = ma > 0f ? 1f / ma : 0f;
                float wb = mb > 0f ? 1f / mb : 0f;
                float wSum = wa + wb;
                if (wSum <= 0f) continue;

                float3 pa = predictPositions[ia];
                float3 pb = predictPositions[ib];

                float3 d = pa - pb;
                float len = math.length(d);
                if (len < 1e-8f) continue;

                float C = len - link.restLength;
                float3 n = d / len;

                float lambda = lambdas[i];
                float dLambda = (-(C) - alpha * lambda) / (wSum + alpha);

                pa += (wa * dLambda) * n;
                pb -= (wb * dLambda) * n;

                predictPositions[ia] = pa;
                predictPositions[ib] = pb;

                lambdas[i] = lambda + dLambda;
            }
        }
    }


    /// <summary>
    /// 四面体体积约束信息（Path A 用于双层血管防翻层：non-inversion）。
    /// - a,b,c：外壁三角形三个顶点（顺序需与外壁三角形 winding 一致，初始化时会自动修正）
    /// - d：内壁映射点（通常是内壁顶点索引）
    /// 约束：SignedVolume(a,b,c,d) >= minVolume
    /// </summary>


    [Unity.Burst.BurstCompile]
    struct PositionEstimateJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<float3> positions;
        [ReadOnly]
        public NativeArray<float3> normals;

        [ReadOnly]
        public NativeArray<float3> velocities;
        [ReadOnly]
        public NativeArray<float> masses;
        [WriteOnly]
        public NativeArray<float3> predictPositions;
        public float3 fieldForce;
        public float damper;
        public float dt;
        public void Execute(int index)
        {
            var p = positions[index];
            var v = velocities[index];
            var m = masses[index];
            if (m > 0)
            {
                var normal = normals[index];
                var fieldForceAtNormal = math.dot(fieldForce, normal) * normal;
                var v1 = v + ClothSimulator.G * dt + fieldForceAtNormal * dt / m;
                v1 *= math.max(0, (1 - damper * dt / m)); //阻尼
                var p1 = p + v1 * dt;
                predictPositions[index] = p1;
            }
            else
            {
                predictPositions[index] = p;
            }

        }
    }

    [Unity.Burst.BurstCompile]
    public struct CollisionJob : IJobParallelFor
    {
        [ReadOnly]
        public float dt;
        [ReadOnly]
        public NativeArray<float3> positions;
        [ReadOnly]
        public NativeArray<float3> predictPositions;

        [ReadOnly]
        public NativeArray<float> masses;

        [ReadOnly]
        public CollidersGroup collidersGroup;

        [WriteOnly]
        public NativeArray<CollisionConstraintInfo> collisionConstraints;
        public NativeArray<ConstraintType> constraintTypes;
        public NativeList<RigidBodyForceApply>.ParallelWriter rigidBodyForceApplies;



        private void EnableCollisionConstraint(int index, ref ConcatInfo concatInfo, ref RigidbodyDesc rigidbody, int entityId)
        {
            constraintTypes[index] |= ConstraintType.Collision;

            var collisionConstraintInfo = new CollisionConstraintInfo()
            {
                surfacePoint = concatInfo.position,
                normal = concatInfo.normal
            };
            var m1 = rigidbody.mass;
            if (m1 > 0)
            {
                var bounciness = rigidbody.bounciness;
                var m0 = masses[index];
                var v0 = (predictPositions[index] - positions[index]) / dt;
                var v0Normal = math.dot(v0, concatInfo.normal) * concatInfo.normal;
                var v1Normal = math.dot(rigidbody.velocity, concatInfo.normal) * concatInfo.normal;

                var v0NormalNew = (bounciness + 1) * m1 * v1Normal + v0Normal * (m0 - bounciness * m1);
                var v1NormalNew = (bounciness + 1) * m0 * v0Normal + v1Normal * (m1 - bounciness * m0);

                v0NormalNew /= (m0 + m1);
                v1NormalNew /= (m0 + m1);

                rigidBodyForceApplies.AddNoResize(new RigidBodyForceApply()
                {
                    entityId = entityId,
                    velocity = v1NormalNew - v1Normal
                });
                collisionConstraintInfo.velocity = (v0 - v0Normal + v0NormalNew);
            }

            collisionConstraints[index] = collisionConstraintInfo;
        }

        private void DisableCollisionConstraint(int index)
        {
            constraintTypes[index] &= (~ConstraintType.Collision);
        }


        public void Execute(int index)
        {
            var position = this.positions[index];
            var predictPosition = this.predictPositions[index];
            var spheres = collidersGroup.spheres;
            var boxes = collidersGroup.boxes;
            var capsules = collidersGroup.capsules;

            for (var i = 0; i < spheres.Length; i++)
            {
                var s = spheres[i];
                ConcatInfo concatInfo;
                if (IntersectUtil.GetClosestSurfacePoint(predictPosition, s.collider, out concatInfo))
                {
                    EnableCollisionConstraint(index, ref concatInfo, ref s.rigidbody, s.entityId);
                    return;
                }
            }

            for (var i = 0; i < boxes.Length; i++)
            {
                var s = boxes[i];
                ConcatInfo concatInfo;
                if (IntersectUtil.GetClosestSurfacePoint(predictPosition, s.collider, out concatInfo))
                {
                    EnableCollisionConstraint(index, ref concatInfo, ref s.rigidbody, s.entityId);

                    return;
                }
            }

            for (var i = 0; i < capsules.Length; i++)
            {
                var s = capsules[i];
                ConcatInfo concatInfo;
                if (IntersectUtil.GetClosestSurfacePoint(predictPosition, s.collider, out concatInfo))
                {
                    EnableCollisionConstraint(index, ref concatInfo, ref s.rigidbody, s.entityId);
                    return;
                }
            }
            DisableCollisionConstraint(index);
        }
    }

    [Unity.Burst.BurstCompile]
    public struct XPBDDistanceConstraintJob : IJob
    {
        public NativeArray<float3> predictPositions;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeArray<DistanceConstraintInfo> distanceConstriants;
        public NativeArray<float> lambdas;
        [ReadOnly] public float compliance;
        [ReadOnly] public float dt;

        public void Execute()
        {
            if (distanceConstriants.Length == 0) return;
            float invDt2 = dt > 1e-8f ? (1.0f / (dt * dt)) : 0f;

            for (int index = 0; index < distanceConstriants.Length; index++)
            {
                var constraint = distanceConstriants[index];
                int i0 = constraint.vIndex0;
                int i1 = constraint.vIndex1;

                float m0 = masses[i0];
                float m1 = masses[i1];
                float w0 = m0 > 0f ? 1f / m0 : 0f;
                float w1 = m1 > 0f ? 1f / m1 : 0f;
                float wSum = w0 + w1;
                if (wSum <= 0f) continue;

                float3 p0 = predictPositions[i0];
                float3 p1 = predictPositions[i1];
                float3 d = p1 - p0;
                float len = math.length(d);
                if (len < 1e-8f) continue;

                float C = len - constraint.restLength;
                float3 n = d / len;

                float alpha = math.max(0f, compliance / math.max(math.max(constraint.stiffnessMul, 1e-6f), 1e-6f)) * invDt2;
                float lambda = lambdas[index];
                float dLambda = (-(C) - alpha * lambda) / (wSum + alpha);

                p0 -= (w0 * dLambda) * n;
                p1 += (w1 * dLambda) * n;

                predictPositions[i0] = p0;
                predictPositions[i1] = p1;
                lambdas[index] = lambda + dLambda;
            }
        }
    }

    [Unity.Burst.BurstCompile]
    public struct XPBDBendConstraintJob : IJob
    {
        public NativeArray<float3> predictPositions;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeArray<BendConstraintInfo> bendConstarints;
        public NativeArray<float> lambdas;
        [ReadOnly] public float compliance;
        [ReadOnly] public float dt;

        // 恢复“接近旧 PBD bend”的强度标定
        [ReadOnly] public float di;
        [ReadOnly] public float bendStiffness;

        [ReadOnly] public float minGradientDenominator;

        // anti-jitter
        [ReadOnly] public float bendAngleDeadZone;
        [ReadOnly] public float bendLambdaDeadZone;
        [ReadOnly] public float bendPositionCorrectionDeadZone;

        public void Execute()
        {
            if (bendConstarints.Length == 0) return;

            float invDt2 = dt > 1e-8f ? (1.0f / (dt * dt)) : 0f;
            float alpha = math.max(0f, compliance) * invDt2;

            // 关键：恢复旧版的单轮缩放，不然会比 PBD bend 硬很多
            float correctionScale = math.max(0f, di * bendStiffness);
            if (correctionScale <= 0f) return;

            float minDen = math.max(1e-8f, minGradientDenominator);
            float angleDeadZone = math.max(0f, bendAngleDeadZone);
            float lambdaDeadZone = math.max(0f, bendLambdaDeadZone);
            float posDeadZone = math.max(0f, bendPositionCorrectionDeadZone);
            float posDeadZoneSq = posDeadZone * posDeadZone;

            for (int index = 0; index < bendConstarints.Length; index++)
            {
                var cons = bendConstarints[index];

                float3 p1 = predictPositions[cons.vIndex0];
                float3 p2 = predictPositions[cons.vIndex1] - p1;
                float3 p3 = predictPositions[cons.vIndex2] - p1;
                float3 p4 = predictPositions[cons.vIndex3] - p1;
                p1 = 0;

                float3 c23 = math.cross(p2, p3);
                float3 c24 = math.cross(p2, p4);
                float len23 = math.length(c23);
                float len24 = math.length(c24);
                if (len23 < 1e-8f || len24 < 1e-8f)
                {
                    lambdas[index] = 0f;
                    continue;
                }

                float3 n1 = c23 / len23;
                float3 n2 = c24 / len24;
                float d = math.clamp(math.dot(n1, n2), -0.9999f, 0.9999f);

                float oneMinusD2 = math.max(1e-8f, 1f - d * d);
                float sqrtOneMinusD2 = math.sqrt(oneMinusD2);

                float3 q3 = (math.cross(p2, n2) + math.cross(n1, p2) * d) / len23;
                float3 q4 = (math.cross(p2, n1) + math.cross(n2, p2) * d) / len24;
                float3 q2 = -(math.cross(p3, n2) + math.cross(n1, p3) * d) / len23
                           - (math.cross(p4, n1) + math.cross(n2, p4) * d) / len24;
                float3 q1 = -q2 - q3 - q4;

                float3 g1 = q1 / sqrtOneMinusD2;
                float3 g2 = q2 / sqrtOneMinusD2;
                float3 g3 = q3 / sqrtOneMinusD2;
                float3 g4 = q4 / sqrtOneMinusD2;

                float m1 = masses[cons.vIndex0];
                float m2 = masses[cons.vIndex1];
                float m3 = masses[cons.vIndex2];
                float m4 = masses[cons.vIndex3];

                float w1 = m1 > 0f ? 1f / m1 : 0f;
                float w2 = m2 > 0f ? 1f / m2 : 0f;
                float w3 = m3 > 0f ? 1f / m3 : 0f;
                float w4 = m4 > 0f ? 1f / m4 : 0f;

                float sum = w1 * math.lengthsq(g1)
                          + w2 * math.lengthsq(g2)
                          + w3 * math.lengthsq(g3)
                          + w4 * math.lengthsq(g4);

                sum = math.max(minDen, sum);

                float C = math.acos(d) - cons.rest;
                float lambda = lambdas[index];

                if (math.abs(C) <= angleDeadZone)
                {
                    lambdas[index] = 0f;
                    continue;
                }

                float rawDLambda = (-(C) - alpha * lambda) / (sum + alpha);
                if (!math.isfinite(rawDLambda))
                {
                    lambdas[index] = 0f;
                    continue;
                }

                // 关键：这里恢复缩放后的 dLambda
                float dLambda = rawDLambda * correctionScale;

                float3 dp1 = (w1 * dLambda) * g1;
                float3 dp2 = (w2 * dLambda) * g2;
                float3 dp3 = (w3 * dLambda) * g3;
                float3 dp4 = (w4 * dLambda) * g4;

                float maxCorrSq =
                    math.max(
                        math.max(math.lengthsq(dp1), math.lengthsq(dp2)),
                        math.max(math.lengthsq(dp3), math.lengthsq(dp4))
                    );

                if (maxCorrSq <= posDeadZoneSq)
                {
                    lambdas[index] = 0f;
                    continue;
                }

                predictPositions[cons.vIndex0] += dp1;
                predictPositions[cons.vIndex1] += dp2;
                predictPositions[cons.vIndex2] += dp3;
                predictPositions[cons.vIndex3] += dp4;

                float newLambda = lambda + dLambda;
                if (math.abs(newLambda) <= lambdaDeadZone)
                    newLambda = 0f;

                lambdas[index] = newLambda;
            }
        }
    }


    /// <summary>
    /// 距离约束任务
    /// </summary>
    [Unity.Burst.BurstCompile]
    public struct DistanceConstraintJob : IJob
    {
        [ReadOnly]
        public float compressStiffness;

        [ReadOnly]
        public float stretchStiffness;

        [ReadOnly]
        public NativeArray<float3> predictPositions;

        [ReadOnly]
        public NativeArray<float> masses;

        [ReadOnly]
        public NativeArray<DistanceConstraintInfo> distanceConstriants;

        public NativeArray<float3> positionCorrects;

        public float di;

        public void Execute()
        {
            for (int index = 0; index < distanceConstriants.Length; index++)
            {
                var constraint = distanceConstriants[index];

                // 对该约束应用“刚度倍率”。
                // - stiffnessMul=1：保持原行为
                // - stiffnessMul>>1：让该边更硬（常用于双层厚度连接）
                float kMul = math.max(0f, constraint.stiffnessMul);
                float compressK = math.saturate(compressStiffness * kMul);
                float stretchK = math.saturate(stretchStiffness * kMul);

                var p0 = predictPositions[constraint.vIndex0];
                var p1 = predictPositions[constraint.vIndex1];
                var m0 = masses[constraint.vIndex0];
                var m1 = masses[constraint.vIndex1];
                var distV = p1 - p0;
                var normal = math.normalize(distV);
                var length = math.length(distV);
                var err = length - constraint.restLength;
                float3 correct;
                if (err < 0)
                {
                    correct = compressK * normal * err;
                }
                else
                {
                    correct = stretchK * normal * err;
                }
                var totalM = m0 + m1;
                positionCorrects[constraint.vIndex0] += correct * di * m1 / totalM;
                positionCorrects[constraint.vIndex1] -= correct * di * m0 / totalM;
            }
        }
    }


    /// <summary>
    /// 为每一条边(即一对三角面片)生成关于4顶点的约束信息
    /// </summary>

    /// <summary>
    /// 每帧先把“碰撞相关状态”清空：
    /// - constraintTypes: 清掉 Collision bit
    /// - collisionConstraints: 写 default
    ///
    /// 目的：
    /// 使用候选列表做 Narrowphase 时，不会让“上一帧的碰撞结果”残留在非候选点上。
    /// </summary>
    [Unity.Burst.BurstCompile]
    public struct ClearCollisionStateJob : IJobParallelFor
    {
        public NativeArray<ConstraintType> constraintTypes;
        public NativeArray<CollisionConstraintInfo> collisionConstraints;
        public NativeArray<NeedleTipIndentationInfo> needleTipIndentationInfos;

        public void Execute(int index)
        {
            constraintTypes[index] &= (~ConstraintType.Collision);
            collisionConstraints[index] = default;
            if (needleTipIndentationInfos.IsCreated)
            {
                needleTipIndentationInfos[index] = default;
            }
        }
    }

    /// <summary>
    /// 只对候选质点进行碰撞检测（Narrowphase）。
    /// 候选列表由主线程 Broadphase（AABBTree）生成。
    /// </summary>
    [Unity.Burst.BurstCompile]
    public struct CollisionCandidatesJob : IJob
    {
        [ReadOnly] public NativeArray<int> candidates;

        [ReadOnly] public float dt;
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> predictPositions;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public CollidersGroup collidersGroup;

        public NativeArray<CollisionConstraintInfo> collisionConstraints;
        public NativeArray<NeedleTipIndentationInfo> needleTipIndentationInfos;
        public NativeArray<ConstraintType> constraintTypes;

        public NativeList<RigidBodyForceApply> rigidBodyForceApplies;

        // 碰撞滞回状态（跨帧）：0=未接触，1=接触中
        public NativeArray<byte> collisionStates;

        // 进入/退出门槛（建议 exit > enter）
        public float skinEnter;
        public float skinExit;

        // 针尖影响区（Phase 1）
        public byte enableNeedleTipIndentation;
        public float needleTipInfluenceRadius;
        public byte enableNeedleTipPiercer;
        public float needleTipPiercerConeLength;
        public float needleTipPiercerConeBaseRadiusScale;
        public float needleTipPiercerConeSurfaceMargin;
        public float needleTipPiercerAxialStiffness;

        private static float GetTipPiercerConeLength(ref CapsuleDesc capsule, float configuredLength)
        {
            float3 fallbackAxis = math.normalizesafe(capsule.axis.xyz, new float3(0f, 1f, 0f));
            float3 dir = math.normalizesafe(capsule.tipForward, fallbackAxis);
            float radius = math.max(capsule.radius, capsule.tipProbeRadius * 0.65f);
            float probeForwardOffset = math.max(0f, math.dot(capsule.tipProbeCenter - capsule.tipCenter, dir));
            float minLength = probeForwardOffset + math.max(radius * 1.75f, capsule.tipProbeRadius);
            float autoLength = math.max(math.max(radius * 5.0f, 0.003f), minLength);
            return configuredLength > 1e-6f
                ? math.max(configuredLength, minLength)
                : autoLength;
        }

        private static float GetTipPiercerConeBaseRadius(ref CapsuleDesc capsule, float baseRadiusScale)
        {
            float scale = math.max(0.05f, baseRadiusScale);
            return math.max(capsule.radius * scale, capsule.tipProbeRadius * 0.35f);
        }

        private static float GetTipPiercerConeSurfaceMargin(ref CapsuleDesc capsule, float configuredMargin)
        {
            return math.max(math.max(0f, configuredMargin), math.max(capsule.radius, capsule.tipProbeRadius) * 0.06f);
        }

        private static bool TryBuildNeedleShaftCapsule(
            ref CapsuleDesc capsule,
            float coneLength,
            out CapsuleDesc shaftCapsule)
        {
            shaftCapsule = capsule;
            if (capsule.hasNeedleTip == 0)
                return false;

            float3 fallbackAxis = math.normalizesafe(capsule.axis.xyz, new float3(0f, 1f, 0f));
            float3 dir = math.normalizesafe(capsule.tipForward, fallbackAxis);
            float3 tail = capsule.tailCenter;
            float3 tipConeBase = capsule.tipProbeCenter - dir * math.max(0f, coneLength);
            float3 axisVec = tipConeBase - tail;
            float axisLen = math.length(axisVec);
            if (axisLen <= 1e-6f)
                return false;

            shaftCapsule.c0 = tail;
            shaftCapsule.axis = new float4(axisVec / axisLen, axisLen);
            shaftCapsule.radius = capsule.radius;
            shaftCapsule.hasNeedleTip = 0;
            shaftCapsule.needleTipEnd = 0;
            shaftCapsule.tipCenter = tipConeBase;
            shaftCapsule.tipProbeCenter = tipConeBase;
            shaftCapsule.tipForward = dir;
            shaftCapsule.tailCenter = tail;
            shaftCapsule.tailForward = -dir;
            return true;
        }

        private static bool TryBuildTipPiercerFieldIndentation(
            float3 predictPosition,
            ref CapsuleDesc capsule,
            float influenceRadius,
            float configuredConeLength,
            float coneBaseRadiusScale,
            float configuredConeMargin,
            float axialStiffness,
            out NeedleTipIndentationInfo info)
        {
            info = default;
            if (capsule.hasNeedleTip == 0)
                return false;

            float3 fallbackAxis = math.normalizesafe(capsule.axis.xyz, new float3(0f, 1f, 0f));
            float3 dir = math.normalizesafe(capsule.tipForward, fallbackAxis);
            float coneLength = GetTipPiercerConeLength(ref capsule, configuredConeLength);
            float coneBaseRadius = GetTipPiercerConeBaseRadius(ref capsule, coneBaseRadiusScale);
            float coneMargin = GetTipPiercerConeSurfaceMargin(ref capsule, configuredConeMargin);
            if (coneLength <= 1e-6f || coneBaseRadius <= 1e-6f)
                return false;

            float activeBand = math.max(math.max(influenceRadius, coneMargin * 4f), coneBaseRadius * 0.55f);
            if (activeBand <= 1e-6f)
                return false;

            float3 apex = capsule.tipProbeCenter;
            float axial = math.dot(apex - predictPosition, dir);
            float axialFrontSlack = math.max(coneMargin, activeBand * 0.30f);
            float axialBackSlack = math.max(coneMargin, activeBand * 0.60f);
            if (axial < -axialFrontSlack || axial > coneLength + axialBackSlack)
                return false;

            float clampedAxial = math.clamp(axial, 0f, coneLength);
            float3 axisPoint = apex - dir * clampedAxial;
            float lateral = math.distance(predictPosition, axisPoint);
            float coneRadiusAtPoint = coneBaseRadius * (clampedAxial / math.max(coneLength, 1e-6f));
            float signedConeDistance = lateral - coneRadiusAtPoint;
            if (signedConeDistance > activeBand)
                return false;

            float radial01 = 1f - math.saturate(math.max(0f, signedConeDistance) / math.max(activeBand, 1e-6f));
            float apex01 = 1f - math.saturate(clampedAxial / math.max(coneLength, 1e-6f));
            float inside01 = 1f - math.saturate(math.max(0f, signedConeDistance - coneMargin) / math.max(activeBand, 1e-6f));
            float stiffnessBoost = 0.55f + 0.45f * math.saturate(axialStiffness);

            float weight = radial01 * (0.35f + 0.65f * apex01);
            weight = math.max(weight, inside01 * (0.22f + 0.48f * apex01));
            weight = math.saturate(weight * stiffnessBoost);

            if (weight <= 0f)
                return false;

            info.surfacePoint = axisPoint;
            info.normal = dir;
            info.weight = weight;
            info.enabled = 1;
            return true;
        }

        private static bool TryBuildCapsuleTipIndentation(
            float3 predictPosition,
            ref CapsuleDesc capsule,
            float influenceRadius,
            out NeedleTipIndentationInfo info)
        {
            info = default;
            if (influenceRadius <= 0f)
                return false;

            if (capsule.hasNeedleTip == 0)
                return false;

            float3 tipForward = math.normalizesafe(capsule.tipForward, new float3(0f, 1f, 0f));

            // 可见几何尖端
            float visibleTipRadius = math.max(0f, capsule.radius);
            float3 visibleTipPoint = capsule.tipCenter + tipForward * visibleTipRadius;

            // 作用半径：至少不小于真实端帽半径
            float tipRadius = math.max(visibleTipRadius, capsule.tipProbeRadius);
            if (tipRadius <= 0f)
                return false;

            // 首触预激活壳层：刚碰到时就不再从 0 权重起步
            float contactShell = math.max(0.0015f, tipRadius * 0.25f);

            float3 toPoint = predictPosition - visibleTipPoint;

            // 允许尖端后方一点点容差，避免刚碰到就被半空间裁掉
            float backAllowance = tipRadius * 0.45f;
            float axial = math.dot(toPoint, tipForward);
            if (axial < -backAllowance)
                return false;

            float dist = math.length(toPoint);
            if (dist <= 1e-8f)
            {
                info.surfacePoint = visibleTipPoint;
                info.normal = tipForward;
                info.weight = 1f;
                info.enabled = 1;
                return true;
            }

            float signedDistance = dist - tipRadius;
            float activeRange = influenceRadius + contactShell;
            if (signedDistance > activeRange)
                return false;

            float3 n = toPoint / dist;

            // 原始软衰减
            float soft01 = 1f - math.saturate(signedDistance / math.max(activeRange, 1e-6f));
            // 首触增强：只要进了 contactShell，就给一个可见权重
            float onset01 = 1f - math.saturate(math.max(0f, signedDistance) / math.max(contactShell, 1e-6f));

            float weight = math.max(soft01, 0.45f * onset01);

            info.surfacePoint = visibleTipPoint + n * tipRadius;
            info.normal = n;
            info.weight = weight;
            info.enabled = (byte)(weight > 0f ? 1 : 0);
            return info.enabled != 0;
        }

        private void TryEnableNeedleTipIndentation(int index, ref CapsuleDesc capsule)
        {
            if (enableNeedleTipIndentation == 0 || !needleTipIndentationInfos.IsCreated)
                return;

            NeedleTipIndentationInfo info;
            bool built = enableNeedleTipPiercer != 0
                ? TryBuildTipPiercerFieldIndentation(
                    predictPositions[index],
                    ref capsule,
                    needleTipInfluenceRadius,
                    needleTipPiercerConeLength,
                    needleTipPiercerConeBaseRadiusScale,
                    needleTipPiercerConeSurfaceMargin,
                    needleTipPiercerAxialStiffness,
                    out info)
                : TryBuildCapsuleTipIndentation(predictPositions[index], ref capsule, needleTipInfluenceRadius, out info);

            if (!built)
                return;

            var old = needleTipIndentationInfos[index];
            if (old.enabled == 0 || info.weight > old.weight)
            {
                needleTipIndentationInfos[index] = info;
            }
        }

        private static bool IsNeedleTailOuterCapContact(ref CapsuleDesc capsule, ref ConcatInfo concatInfo)
        {
            if (capsule.hasNeedleTip == 0)
                return false;

            float3 axisDir = math.normalizesafe(capsule.axis.xyz, new float3(0f, 1f, 0f));
            float axisLen = math.max(0f, capsule.axis.w);
            float axial = math.dot(concatInfo.position - capsule.c0, axisDir);
            const float eps = 1e-6f;

            bool hitOuterHemisphereC0 = axial < -eps;
            bool hitOuterHemisphereC1 = axial > axisLen + eps;
            if (!hitOuterHemisphereC0 && !hitOuterHemisphereC1)
                return false;

            bool tailIsC0 = capsule.needleTipEnd == 1;
            bool tailIsC1 = capsule.needleTipEnd == 0;

            if (tailIsC0 && hitOuterHemisphereC0)
                return true;

            if (tailIsC1 && hitOuterHemisphereC1)
                return true;

            return false;
        }

        private void EnableCollisionConstraint(int index, ref ConcatInfo concatInfo, ref RigidbodyDesc rigidbody, int entityId)
        {
            constraintTypes[index] |= ConstraintType.Collision;

            var collisionConstraintInfo = new CollisionConstraintInfo()
            {
                surfacePoint = concatInfo.position,
                normal = concatInfo.normal
            };

            var m1 = rigidbody.mass;
            if (m1 > 0)
            {
                var bounciness = rigidbody.bounciness;
                var m0 = masses[index];
                var v0 = (predictPositions[index] - positions[index]) / dt;
                var v0Normal = math.dot(v0, concatInfo.normal) * concatInfo.normal;
                var v1Normal = math.dot(rigidbody.velocity, concatInfo.normal) * concatInfo.normal;

                var v0NormalNew = (bounciness + 1) * m1 * v1Normal + v0Normal * (m0 - bounciness * m1);
                var v1NormalNew = (bounciness + 1) * m0 * v0Normal + v1Normal * (m1 - bounciness * m0);

                v0NormalNew /= (m0 + m1);
                v1NormalNew /= (m0 + m1);

                rigidBodyForceApplies.AddNoResize(new RigidBodyForceApply()
                {
                    entityId = entityId,
                    velocity = v1NormalNew - v1Normal
                });

                collisionConstraintInfo.velocity = (v0 - v0Normal + v0NormalNew);
            }

            collisionConstraints[index] = collisionConstraintInfo;
        }

        public void Execute()
        {
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                int index = candidates[candidateIndex];

                var predictPosition = this.predictPositions[index];
                var spheres = collidersGroup.spheres;
                var boxes = collidersGroup.boxes;
                var capsules = collidersGroup.capsules;

                bool hit = false;

                for (var i = 0; i < spheres.Length; i++)
                {
                    var s = spheres[i];
                    ConcatInfo concatInfo;
                    if (IntersectUtil.GetClosestSurfacePoint(predictPosition, s.collider, out concatInfo))
                    {
                        float dist = math.dot(predictPosition - concatInfo.position, concatInfo.normal);
                        float threshold = (collisionStates.IsCreated && collisionStates[index] != 0) ? skinExit : skinEnter;
                        if (dist < threshold)
                        {
                            EnableCollisionConstraint(index, ref concatInfo, ref s.rigidbody, s.entityId);
                            hit = true;
                            break;
                        }
                    }
                }

                if (!hit)
                {
                    for (var i = 0; i < boxes.Length; i++)
                    {
                        var s = boxes[i];
                        ConcatInfo concatInfo;
                        if (IntersectUtil.GetClosestSurfacePoint(predictPosition, s.collider, out concatInfo))
                        {
                            float dist = math.dot(predictPosition - concatInfo.position, concatInfo.normal);
                            float threshold = (collisionStates.IsCreated && collisionStates[index] != 0) ? skinExit : skinEnter;
                            if (dist < threshold)
                            {
                                EnableCollisionConstraint(index, ref concatInfo, ref s.rigidbody, s.entityId);
                                hit = true;
                                break;
                            }
                        }
                    }
                }

                for (var i = 0; i < capsules.Length; i++)
                {
                    var s = capsules[i];

                    // 针尖穿刺场在普通 capsule collision 之前预写入 indentation。
                    // TipPiercer 开启时，这里是 apex-driven cone field；关闭时保留 legacy 球形 tip indentation。
                    TryEnableNeedleTipIndentation(index, ref s.collider);

                    if (hit) continue;

                    CapsuleDesc collisionCapsule = s.collider;
                    bool shaftOnlyNeedleCollision =
                        enableNeedleTipPiercer != 0 &&
                        s.collider.hasNeedleTip != 0 &&
                        TryBuildNeedleShaftCapsule(
                            ref s.collider,
                            GetTipPiercerConeLength(ref s.collider, needleTipPiercerConeLength),
                            out collisionCapsule);

                    ConcatInfo concatInfo;
                    if (IntersectUtil.GetClosestSurfacePoint(predictPosition, collisionCapsule, out concatInfo))
                    {
                        // 第二批修改：如果最近接触落在“固定尾端球帽的外半球”，
                        // 则不把它当作普通外部压头来生成 CollisionConstraint。
                        // 这样先把最明显的“后端把外层往外顶鼓”压下去。
                        if (!shaftOnlyNeedleCollision && IsNeedleTailOuterCapContact(ref s.collider, ref concatInfo))
                        {
                            continue;
                        }

                        float dist = math.dot(predictPosition - concatInfo.position, concatInfo.normal);
                        float threshold = (collisionStates.IsCreated && collisionStates[index] != 0) ? skinExit : skinEnter;
                        if (dist < threshold)
                        {
                            EnableCollisionConstraint(index, ref concatInfo, ref s.rigidbody, s.entityId);
                            hit = true;
                        }
                    }
                }

                if (collisionStates.IsCreated)
                {
                    collisionStates[index] = (byte)(hit ? 1 : 0);
                }

                // 命中则已写入 collisionConstraints/constraintTypes；继续处理下一个 candidate
            }
        }
    }




    [Unity.Burst.BurstCompile]
    public struct TriangleSurfaceContactJob : IJob
    {
        [ReadOnly] public NativeArray<int> candidateTriangles;
        [ReadOnly] public NativeArray<int> indices;
        [ReadOnly] public NativeArray<float3> predictPositions;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public CollidersGroup collidersGroup;
        [ReadOnly] public NativeArray<NeedlePortalInfo> activePortals;
        [ReadOnly] public NativeArray<NeedleTipHookSoftConstraintInfo> activeTipPiercerHooks;

        public NativeArray<float3> positionCorrects;

        [ReadOnly] public float contactStiffness;
        [ReadOnly] public float contactMargin;
        [ReadOnly] public float maxPushPerIteration;
        [ReadOnly] public byte enableNeedleTipPiercer;

        private static float3 ClosestPointOnTriangle(float3 p, float3 a, float3 b, float3 c, out float3 bary)
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

        private bool IsPortalRelaxedContact(int colliderEntityId, int triangleIndex, float3 contactPoint)
        {
            if (!activePortals.IsCreated || activePortals.Length == 0)
                return false;

            for (int i = 0; i < activePortals.Length; i++)
            {
                var portal = activePortals[i];
                if (portal.enabled == 0) continue;
                if (portal.colliderEntityId != colliderEntityId) continue;

                if (portal.triangleIndex == triangleIndex)
                    return true;

                float rr = math.max(0f, portal.relaxRadius);
                if (rr <= 0f) continue;

                if (math.lengthsq(contactPoint - portal.currentPoint) <= rr * rr)
                    return true;
            }

            return false;
        }

        private bool IsTipPiercerRelaxedContact(int colliderEntityId, int triangleIndex, float3 contactPoint)
        {
            if (!activeTipPiercerHooks.IsCreated || activeTipPiercerHooks.Length == 0)
                return false;

            for (int i = 0; i < activeTipPiercerHooks.Length; i++)
            {
                NeedleTipHookSoftConstraintInfo hook = activeTipPiercerHooks[i];
                if (hook.enabled == 0)
                    continue;
                if (hook.colliderEntityId != colliderEntityId)
                    continue;

                if (hook.triangleIndex == triangleIndex)
                    return true;

                float radius = math.max(0f, hook.radius);
                if (radius <= 0f)
                    continue;

                if (math.lengthsq(contactPoint - hook.hookPoint) <= radius * radius)
                    return true;
            }

            return false;
        }


        public void Execute()
        {
            if (candidateTriangles.Length == 0) return;
            var capsules = collidersGroup.capsules;
            if (capsules.Length == 0) return;

            float stiffness = math.saturate(contactStiffness);
            if (stiffness <= 0f) return;

            float margin = math.max(0f, contactMargin);
            float maxPush = math.max(0f, maxPushPerIteration);

            for (int cIndex = 0; cIndex < candidateTriangles.Length; cIndex++)
            {
                int tri = candidateTriangles[cIndex];
                int triOffset = tri * 3;
                if (triOffset + 2 >= indices.Length) continue;

                int i0 = indices[triOffset];
                int i1 = indices[triOffset + 1];
                int i2 = indices[triOffset + 2];

                float3 p0 = predictPositions[i0];
                float3 p1 = predictPositions[i1];
                float3 p2 = predictPositions[i2];

                float3 triNormal = math.cross(p1 - p0, p2 - p0);
                float triNormalLenSq = math.lengthsq(triNormal);
                if (triNormalLenSq < 1e-12f) continue;
                triNormal *= math.rsqrt(triNormalLenSq);

                float m0 = masses[i0];
                float m1 = masses[i1];
                float m2 = masses[i2];

                float inv0 = m0 > 0f ? 1f / m0 : 0f;
                float inv1 = m1 > 0f ? 1f / m1 : 0f;
                float inv2 = m2 > 0f ? 1f / m2 : 0f;
                if (inv0 + inv1 + inv2 <= 0f) continue;

                for (int capsuleIndex = 0; capsuleIndex < capsules.Length; capsuleIndex++)
                {
                    CapsuleDesc capsule = capsules[capsuleIndex].collider;
                    if (capsule.hasNeedleTip == 0)
                        continue;
                    if (enableNeedleTipPiercer != 0)
                        continue;

                    float3 tipCenter = capsule.tipProbeCenter;
                    float effectiveRadius = capsule.tipProbeRadius + margin;
                    if (effectiveRadius <= 0f)
                        continue;

                    // 首触壳层：刚进入接触区就给一个可见反应
                    float contactShell = math.max(0.0015f, margin);
                    float preContactRadius = effectiveRadius + contactShell;

                    float3 bary;
                    float3 q = ClosestPointOnTriangle(tipCenter, p0, p1, p2, out bary);

                    if (IsPortalRelaxedContact(capsules[capsuleIndex].entityId, tri, q))
                        continue;
                    if (IsTipPiercerRelaxedContact(capsules[capsuleIndex].entityId, tri, q))
                        continue;

                    float3 toTip = tipCenter - q;
                    float distSq = math.lengthsq(toTip);
                    float radiusSq = preContactRadius * preContactRadius;
                    if (distSq >= radiusSq)
                        continue;

                    float dist = math.sqrt(math.max(distSq, 1e-12f));

                    float3 away;
                    if (distSq > 1e-10f)
                    {
                        away = -toTip / dist;
                    }
                    else
                    {
                        float3 triCenter = (p0 + p1 + p2) / 3f;
                        away = triNormal;
                        if (math.dot(away, tipCenter - triCenter) > 0f)
                            away = -away;
                    }

                    float penetration = preContactRadius - dist;

                    // 首触增强：在壳层里也给非零推量
                    float onset01 = 1f - math.saturate(
                        math.max(0f, dist - effectiveRadius) / math.max(contactShell, 1e-6f));

                    float push = math.max(
                        penetration * stiffness,
                        contactShell * 0.55f * stiffness * onset01);

                    if (maxPush > 0f)
                    {
                        push = math.min(push, math.max(maxPush, contactShell * 0.55f * stiffness));
                    }

                    if (push <= 0f)
                        continue;

                    float b0 = math.saturate(bary.x);
                    float b1 = math.saturate(bary.y);
                    float b2 = math.saturate(bary.z);
                    float bSum = b0 + b1 + b2;
                    if (bSum <= 1e-8f)
                        continue;

                    b0 /= bSum;
                    b1 /= bSum;
                    b2 /= bSum;

                    float denom = b0 * b0 * inv0 + b1 * b1 * inv1 + b2 * b2 * inv2;
                    if (denom <= 1e-8f)
                        continue;

                    float3 delta = away * push;
                    if (inv0 > 0f) positionCorrects[i0] += delta * ((b0 * inv0) / denom);
                    if (inv1 > 0f) positionCorrects[i1] += delta * ((b1 * inv1) / denom);
                    if (inv2 > 0f) positionCorrects[i2] += delta * ((b2 * inv2) / denom);
                }
            }
        }
    }



    [Unity.Burst.BurstCompile]
    public struct SutureLineConstraintJob : IJob
    {
        public NativeArray<SutureLineConstraintInfo> constraints;
        [ReadOnly] public NativeArray<SutureLinePatchVertexInfo> patchVertices;
        [ReadOnly] public NativeArray<CrossLayerLinkInfo> crossLayerLinks;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeArray<float3> predictPositions;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positionCorrects;
        public float stiffness;
        public float maxCorrection;
        public float restDistance;
        public float dt;
        public float compliance;
        public byte usePatch;
        public byte useXPBD;

        public void Execute()
        {
            for (int index = 0; index < constraints.Length; index++)
                Solve(index);
        }

        private void Solve(int index)
        {
            SutureLineConstraintInfo c = constraints[index];
            if (c.enabled == 0)
                return;

            bool patchEnabled = usePatch != 0 && patchVertices.IsCreated && patchVertices.Length > 0;
            float3 pointA = ReadConstrainedPoint(c.portalA, c.patchStartA, c.patchCountA, patchEnabled);
            float3 pointB = ReadConstrainedPoint(c.portalB, c.patchStartB, c.patchCountB, patchEnabled);
            float3 delta = pointB - pointA;
            float distance = math.length(delta);
            if (distance < 1e-7f)
                return;

            float targetRest = c.currentRestDistance > 0f ? c.currentRestDistance : (c.targetRestDistance > 0f ? c.targetRestDistance : restDistance);
            targetRest = math.max(0f, targetRest);
            float C = distance - targetRest;
            if (C <= 1e-6f)
                return;

            float3 n = delta / distance;
            float invWeightA = ConstrainedPointInvWeight(c.portalA, c.patchStartA, c.patchCountA, patchEnabled);
            float invWeightB = ConstrainedPointInvWeight(c.portalB, c.patchStartB, c.patchCountB, patchEnabled);
            float denom = invWeightA + invWeightB;
            if (denom <= 1e-8f)
                return;

            float pullMagnitude = C / denom;
            c.lambda = 0f;
            constraints[index] = c;

            if (math.abs(pullMagnitude) < 1e-9f)
                return;

            float3 correctionA = n * pullMagnitude;
            float3 correctionB = -n * pullMagnitude;
            ApplyConstrainedPointCorrection(c.portalA, c.patchStartA, c.patchCountA, patchEnabled, correctionA);
            ApplyConstrainedPointCorrection(c.portalB, c.patchStartB, c.patchCountB, patchEnabled, correctionB);
        }

        private float3 ReadConstrainedPoint(NeedlePortalInfo portal, int patchStart, int patchCount, bool patchEnabled)
        {
            if (patchEnabled && TryReadWholeWallHoleColumnPoint(portal, patchStart, patchCount, out float3 columnPoint))
                return columnPoint;

            return ReadPortalPoint(portal);
        }

        private bool TryReadWholeWallHoleColumnPoint(NeedlePortalInfo portal, int patchStart, int patchCount, out float3 columnPoint)
        {
            columnPoint = float3.zero;
            float3 outerPoint = ReadPortalPoint(portal);
            if (!crossLayerLinks.IsCreated || crossLayerLinks.Length == 0 ||
                !TryReadLinkedPortalPoint(portal, patchStart, patchCount, out float3 innerPoint))
                return false;

            columnPoint = (outerPoint + innerPoint) * 0.5f;
            return true;
        }

        private float3 ReadPortalPoint(NeedlePortalInfo portal)
        {
            float3 p0 = IsValidVertex(portal.v0) ? predictPositions[portal.v0] : portal.currentPoint;
            float3 p1 = IsValidVertex(portal.v1) ? predictPositions[portal.v1] : portal.currentPoint;
            float3 p2 = IsValidVertex(portal.v2) ? predictPositions[portal.v2] : portal.currentPoint;
            return p0 * portal.bary.x + p1 * portal.bary.y + p2 * portal.bary.z;
        }

        private float ConstrainedPointInvWeight(NeedlePortalInfo portal, int patchStart, int patchCount, bool patchEnabled)
        {
            if (patchEnabled && TryGetWholeWallHoleColumnInvWeight(portal, patchStart, patchCount, out float invWeight))
                return invWeight;

            return PortalInvWeight(portal);
        }

        private bool TryGetWholeWallHoleColumnInvWeight(NeedlePortalInfo portal, int patchStart, int patchCount, out float invWeight)
        {
            invWeight = 0f;
            if (!crossLayerLinks.IsCreated || crossLayerLinks.Length == 0 ||
                !TryGetLinkedPortalInvWeight(portal, patchStart, patchCount, out float innerInvWeight))
                return false;

            invWeight = (PortalInvWeight(portal) + innerInvWeight) * 0.25f;
            return invWeight > 1e-8f;
        }

        private bool TryGetPatchInvWeight(int patchStart, int patchCount, out float invWeight)
        {
            invWeight = 0f;
            if (patchCount <= 0 || patchStart < 0 || patchStart + patchCount > patchVertices.Length)
                return false;

            float weightSum = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;
                weightSum += GetPullWeight(pv);
            }

            if (weightSum <= 1e-8f)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;

                float q = GetPullWeight(pv) / weightSum;
                if (q <= 0f)
                    continue;

                invWeight += GetInvMass(pv.vertexIndex) * q * q;
            }

            return invWeight > 1e-8f;
        }

        private float PortalInvWeight(NeedlePortalInfo portal)
        {
            float w0 = GetInvMass(portal.v0);
            float w1 = GetInvMass(portal.v1);
            float w2 = GetInvMass(portal.v2);
            return w0 * portal.bary.x * portal.bary.x
                 + w1 * portal.bary.y * portal.bary.y
                 + w2 * portal.bary.z * portal.bary.z;
        }

        private void ApplyConstrainedPointCorrection(NeedlePortalInfo portal, int patchStart, int patchCount, bool patchEnabled, float3 correction)
        {
            if (patchEnabled && TryApplyPatchCorrection(portal, patchStart, patchCount, correction))
                return;

            ApplyPortalCorrection(portal, correction);
        }

        private bool TryApplyPatchCorrection(NeedlePortalInfo portal, int patchStart, int patchCount, float3 correction)
        {
            if (patchCount <= 0 || patchStart < 0 || patchStart + patchCount > patchVertices.Length)
                return false;

            if (crossLayerLinks.IsCreated && crossLayerLinks.Length > 0 &&
                TryApplyWholeWallHoleColumnCorrection(portal, patchStart, patchCount, correction))
                return true;
            if (crossLayerLinks.IsCreated && crossLayerLinks.Length > 0 &&
                TryApplyPatchColumnCorrection(patchStart, patchCount, correction))
                return true;

            return false;
        }

        private bool TryApplyWholeWallHoleColumnCorrection(NeedlePortalInfo portal, int patchStart, int patchCount, float3 correction)
        {
            if (!TryReadLinkedPortalPoint(portal, patchStart, patchCount, out _))
                return false;

            ApplyPortalCorrectionScaled(portal, correction, 0.5f);
            TryApplyLinkedPortalCorrection(portal, patchStart, patchCount, correction * 0.5f);
            return true;
        }

        private bool TryReadLinkedPortalPoint(NeedlePortalInfo portal, int patchStart, int patchCount, out float3 point)
        {
            point = float3.zero;
            if (!crossLayerLinks.IsCreated || crossLayerLinks.Length == 0)
                return false;

            float weightSum = 0f;
            for (int slot = 0; slot < 3; slot++)
            {
                int vertex = GetPortalVertex(portal, slot);
                float weight = GetPortalWeight(portal, slot);
                if (vertex < 0 || weight <= 1e-6f ||
                    !TryFindLinkedPatchVertex(vertex, patchStart, patchCount, out int linkedVertex))
                    continue;

                point += predictPositions[linkedVertex] * weight;
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

        private bool TryGetLinkedPortalInvWeight(NeedlePortalInfo portal, int patchStart, int patchCount, out float invWeight)
        {
            invWeight = 0f;
            if (!crossLayerLinks.IsCreated || crossLayerLinks.Length == 0)
                return false;

            float weightSum = 0f;
            for (int slot = 0; slot < 3; slot++)
            {
                int vertex = GetPortalVertex(portal, slot);
                float weight = GetPortalWeight(portal, slot);
                if (vertex < 0 || weight <= 1e-6f ||
                    !TryFindLinkedPatchVertex(vertex, patchStart, patchCount, out _))
                    continue;

                weightSum += weight;
            }

            if (weightSum <= 1e-6f)
                return false;

            for (int slot = 0; slot < 3; slot++)
            {
                int vertex = GetPortalVertex(portal, slot);
                float weight = GetPortalWeight(portal, slot);
                if (vertex < 0 || weight <= 1e-6f ||
                    !TryFindLinkedPatchVertex(vertex, patchStart, patchCount, out int linkedVertex))
                    continue;

                float q = weight / weightSum;
                invWeight += GetInvMass(linkedVertex) * q * q;
            }

            return invWeight > 1e-8f;
        }

        private bool TryApplyLinkedPortalCorrection(NeedlePortalInfo portal, int patchStart, int patchCount, float3 correction)
        {
            float weightSum = 0f;
            for (int slot = 0; slot < 3; slot++)
            {
                int vertex = GetPortalVertex(portal, slot);
                float weight = GetPortalWeight(portal, slot);
                if (vertex < 0 || weight <= 1e-6f ||
                    !TryFindLinkedPatchVertex(vertex, patchStart, patchCount, out _))
                    continue;

                weightSum += weight;
            }

            if (weightSum <= 1e-6f)
                return false;

            for (int slot = 0; slot < 3; slot++)
            {
                int vertex = GetPortalVertex(portal, slot);
                float weight = GetPortalWeight(portal, slot);
                if (vertex < 0 || weight <= 1e-6f ||
                    !TryFindLinkedPatchVertex(vertex, patchStart, patchCount, out int linkedVertex))
                    continue;

                ApplyVertexCorrection(linkedVertex, correction * (weight / weightSum));
            }

            return true;
        }

        private bool TryFindLinkedPatchVertex(int vertex, int patchStart, int patchCount, out int linkedVertex)
        {
            linkedVertex = -1;
            if (!crossLayerLinks.IsCreated || crossLayerLinks.Length == 0 ||
                patchStart < 0 || patchCount <= 0 || patchStart + patchCount > patchVertices.Length)
                return false;

            for (int i = 0; i < crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = crossLayerLinks[i];
                int other = link.a == vertex ? link.b : (link.b == vertex ? link.a : -1);
                if (other < 0 || !IsValidVertex(other) || !IsPatchVertex(patchStart, patchCount, other))
                    continue;

                linkedVertex = other;
                return true;
            }

            return false;
        }

        private bool IsPatchVertex(int patchStart, int patchCount, int vertex)
        {
            if (patchStart < 0 || patchCount <= 0 || patchStart + patchCount > patchVertices.Length)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                if (patchVertices[patchStart + i].vertexIndex == vertex)
                    return true;
            }

            return false;
        }

        private bool TryApplyPatchColumnCorrection(int patchStart, int patchCount, float3 correction)
        {
            float weightSum = 0f;
            int columnCount = 0;
            for (int i = 0; i < crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = crossLayerLinks[i];
                if (!TryGetPatchPullWeight(patchStart, patchCount, link.a, out float wa) ||
                    !TryGetPatchPullWeight(patchStart, patchCount, link.b, out float wb))
                    continue;

                weightSum += math.max(0.05f, math.min(wa, wb));
                columnCount++;
            }

            if (columnCount <= 0 || weightSum <= 1e-8f)
                return false;

            for (int i = 0; i < crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = crossLayerLinks[i];
                if (!TryGetPatchPullWeight(patchStart, patchCount, link.a, out float wa) ||
                    !TryGetPatchPullWeight(patchStart, patchCount, link.b, out float wb))
                    continue;

                float q = math.max(0.05f, math.min(wa, wb)) / weightSum;
                ApplyVertexCorrection(link.a, correction * q);
                ApplyVertexCorrection(link.b, correction * q);
            }

            return true;
        }

        private bool TryGetPatchPullWeight(int patchStart, int patchCount, int vertexIndex, out float weight)
        {
            weight = 0f;
            if (vertexIndex < 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > patchVertices.Length)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (pv.vertexIndex != vertexIndex)
                    continue;

                weight = GetHolePullWeight(pv);
                return weight > 0f;
            }

            return false;
        }

        private void ApplyPortalCorrection(NeedlePortalInfo portal, float3 correction)
        {
            ApplyVertexCorrection(portal.v0, correction * portal.bary.x);
            ApplyVertexCorrection(portal.v1, correction * portal.bary.y);
            ApplyVertexCorrection(portal.v2, correction * portal.bary.z);
        }

        private void ApplyPortalCorrectionScaled(NeedlePortalInfo portal, float3 correction, float scale)
        {
            ApplyVertexCorrection(portal.v0, correction * portal.bary.x * scale);
            ApplyVertexCorrection(portal.v1, correction * portal.bary.y * scale);
            ApplyVertexCorrection(portal.v2, correction * portal.bary.z * scale);
        }

        private static int GetPortalVertex(NeedlePortalInfo portal, int slot)
        {
            switch (slot)
            {
                case 0: return portal.v0;
                case 1: return portal.v1;
                default: return portal.v2;
            }
        }

        private static float GetPortalWeight(NeedlePortalInfo portal, int slot)
        {
            switch (slot)
            {
                case 0: return portal.bary.x;
                case 1: return portal.bary.y;
                default: return portal.bary.z;
            }
        }

        private static float GetPullWeight(SutureLinePatchVertexInfo pv)
        {
            byte flags = pv.flags;
            if (flags == 0)
                flags = SutureLinePatchVertexInfo.FlagPullAnchor;
            float weight = math.max(0f, pv.weight);
            if ((flags & SutureLinePatchVertexInfo.FlagPullAnchor) != 0)
                return weight;
            if ((flags & SutureLinePatchVertexInfo.FlagFoldColumn) != 0)
                return weight * SutureLinePatchVertexInfo.FoldColumnPullWeightScale;
            if ((flags & SutureLinePatchVertexInfo.FlagSeamAnchor) != 0)
                return weight * SutureLinePatchVertexInfo.SeamAnchorPullWeightScale;
            return 0f;
        }

        private static float GetHolePullWeight(SutureLinePatchVertexInfo pv)
        {
            byte flags = pv.flags;
            if (flags == 0)
                flags = SutureLinePatchVertexInfo.FlagPullAnchor;
            return (flags & SutureLinePatchVertexInfo.FlagPullAnchor) != 0
                ? math.max(0f, pv.weight)
                : 0f;
        }

        private void ApplyVertexCorrection(int vertex, float3 correction)
        {
            if (!IsValidVertex(vertex))
                return;

            float invMass = GetInvMass(vertex);
            if (invMass <= 0f)
                return;

            positionCorrects[vertex] = positionCorrects[vertex] + correction * invMass;
        }

        private float GetInvMass(int vertex)
        {
            if (!IsValidVertex(vertex))
                return 0f;
            float mass = masses[vertex];
            return mass > 1e-8f ? 1f / mass : 0f;
        }

        private bool IsValidVertex(int vertex)
        {
            return vertex >= 0 && vertex < predictPositions.Length && vertex < masses.Length;
        }
    }

    [Unity.Burst.BurstCompile]
    public struct SutureLineContactJob : IJob
    {
        private const float SutureRoiContactMinWeight = 0.05f;
        private const float SutureRoiThicknessGuardMinScale = 0.82f;
        private const float SutureRoiThicknessGuardStiffness = 1f;

        public NativeArray<SutureLineConstraintInfo> constraints;
        [ReadOnly] public NativeArray<SutureLinePatchVertexInfo> patchVertices;
        [ReadOnly] public NativeArray<SutureLineContactTriangleInfo> contactTriangles;
        [ReadOnly] public NativeArray<CrossLayerLinkInfo> crossLayerLinks;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeArray<float3> previousPositions;
        [ReadOnly] public NativeArray<float3> predictPositions;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positionCorrects;
        public float contactGap;
        public float contactStiffness;
        public float maxCorrection;
        public byte useSelfContact;
        public byte useLegacyContactFallback;
        public byte usePatch;
        public byte useLocalPatchContact;
        public byte useLocalTriangleContact;
        public int maxPatchPairChecksPerLine;
        public int maxTriangleChecksPerLine;
        public float contactLatchedStiffnessScale;
        public float selfContactThicknessScale;
        public float selfContactEdgeScale;
        public float selfContactNormalStiffness;
        public float selfContactTangentialDamping;
        public float selfContactFriction;
        public float selfContactMaxPush;
        public int maxSelfContactVTPairs;
        public byte useSelfContactEE;
        public int maxSelfContactEEPairs;

        public void Execute()
        {
            if (!constraints.IsCreated || constraints.Length == 0)
                return;

            float gap = math.max(0f, contactGap);
            float stiffness = math.saturate(contactStiffness);
            if (gap <= 0f || stiffness <= 0f)
                return;

            bool patchEnabled = usePatch != 0 && patchVertices.IsCreated && patchVertices.Length > 0;
            float maxPush = math.max(0f, maxCorrection);

            for (int index = 0; index < constraints.Length; index++)
            {
                SutureLineConstraintInfo c = constraints[index];
                if (c.enabled == 0 || c.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold)
                    continue;

                float lineStiffness = c.contactLatched != 0
                    ? math.saturate(stiffness * math.max(1f, contactLatchedStiffnessScale))
                    : stiffness;

                if (patchEnabled && crossLayerLinks.IsCreated && crossLayerLinks.Length > 0)
                    SolveSutureRoiThicknessGuards(c, maxPush);

                if (useSelfContact != 0 && patchEnabled && contactTriangles.IsCreated && contactTriangles.Length > 0)
                {
                    SolveLocalSelfContact(index, c, lineStiffness);
                }
                else if (useLegacyContactFallback != 0)
                {
                    SolveRepresentativeContact(c, patchEnabled, gap, lineStiffness, maxPush);

                    if (useLocalPatchContact != 0 && patchEnabled)
                        SolvePatchPairContacts(c, gap, lineStiffness, maxPush);

                    if (useLocalTriangleContact != 0 && patchEnabled && contactTriangles.IsCreated && contactTriangles.Length > 0)
                        SolvePatchTriangleContacts(c, gap, lineStiffness, maxPush);
                }
            }
        }

        private void SolveSutureRoiThicknessGuards(SutureLineConstraintInfo c, float maxPush)
        {
            float guardMaxPush = selfContactMaxPush > 0f ? selfContactMaxPush : maxPush;
            SolveSutureRoiPatchThicknessGuard(c.patchStartA, c.patchCountA, guardMaxPush);
            SolveSutureRoiPatchThicknessGuard(c.patchStartB, c.patchCountB, guardMaxPush);
        }

        private void SolveSutureRoiPatchThicknessGuard(int patchStart, int patchCount, float maxPush)
        {
            if (patchStart < 0 || patchCount <= 0 || patchStart + patchCount > patchVertices.Length)
                return;

            for (int i = 0; i < crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = crossLayerLinks[i];
                if (!IsValidVertex(link.a) || !IsValidVertex(link.b) ||
                    !TryGetPatchContactWeight(patchStart, patchCount, link.a, out float wa) ||
                    !TryGetPatchContactWeight(patchStart, patchCount, link.b, out float wb))
                    continue;

                float minLength = math.max(0f, link.restLength * SutureRoiThicknessGuardMinScale);
                if (minLength <= 1e-7f)
                    continue;

                float3 delta = predictPositions[link.b] - predictPositions[link.a];
                float distance = math.length(delta);
                if (distance >= minLength)
                    continue;

                float3 dir = distance > 1e-7f ? delta / distance : float3.zero;
                if (math.lengthsq(dir) < 1e-8f && previousPositions.IsCreated && previousPositions.Length == predictPositions.Length)
                    dir = math.normalizesafe(previousPositions[link.b] - previousPositions[link.a], float3.zero);
                if (math.lengthsq(dir) < 1e-8f)
                    dir = new float3(0f, 1f, 0f);

                float invA = GetInvMass(link.a);
                float invB = GetInvMass(link.b);
                float denom = invA + invB;
                if (denom <= 1e-8f)
                    continue;

                float pairWeight = math.saturate(math.sqrt(wa * wb));
                float push = ((minLength - distance) * SutureRoiThicknessGuardStiffness * pairWeight) / denom;
                if (maxPush > 0f)
                    push = math.min(push, maxPush);
                if (push <= 1e-9f)
                    continue;

                ApplyVertexCorrection(link.a, -dir * push);
                ApplyVertexCorrection(link.b, dir * push);
            }
        }

        private bool TryGetPatchContactWeight(int patchStart, int patchCount, int vertexIndex, out float weight)
        {
            weight = 0f;
            if (vertexIndex < 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > patchVertices.Length)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (pv.vertexIndex != vertexIndex)
                    continue;

                weight = GetPatchContactWeight(pv);
                return weight > 0f;
            }

            return false;
        }

        private static float GetPatchContactWeight(SutureLinePatchVertexInfo pv)
        {
            return math.max(SutureRoiContactMinWeight, math.max(0f, pv.weight));
        }

        private void SolveLocalSelfContact(int index, SutureLineConstraintInfo c, float lineStiffness)
        {
            c.lastSelfContactSolveTriPairs = 0;
            c.lastSelfContactSolveVTPairs = 0;
            c.lastSelfContactSolveEEPairs = 0;
            c.lastSelfContactSolveCcdHit = 0;
            c.lastSelfContactSolveForced = 0;
            c.lastSelfContactSolveNormalPush = 0f;
            c.lastSelfContactSolveMinDistance = float.PositiveInfinity;
            c.lastSelfContactCcdAdvancePush = 0f;

            if (!contactTriangles.IsCreated)
            {
                constraints[index] = c;
                return;
            }

            float thickness = ComputeSelfContactThickness(c);
            float stiffness = math.saturate(math.max(0f, selfContactNormalStiffness) * math.max(0.05f, lineStiffness));
            float maxPush = selfContactMaxPush > 0f ? selfContactMaxPush : maxCorrection;
            if (thickness <= 1e-7f || stiffness <= 0f)
            {
                constraints[index] = c;
                return;
            }
            if (!ShouldRunLocalWallContact(c, thickness))
            {
                constraints[index] = c;
                return;
            }

            int bandBudget = math.max(12, (c.closureBandCountA + c.closureBandCountB) * 2);
            int vtBudget = maxSelfContactVTPairs > 0 ? math.min(maxSelfContactVTPairs, math.max(12, bandBudget * 2)) : 48;
            int triPairCount = c.contactTriCountA > 0 && c.contactTriCountB > 0 && c.contactTriCountA <= int.MaxValue / c.contactTriCountB
                ? c.contactTriCountA * c.contactTriCountB
                : int.MaxValue;
            int triPairBudget = maxTriangleChecksPerLine > 0
                ? math.min(math.max(16, maxTriangleChecksPerLine), math.max(16, triPairCount))
                : math.max(16, triPairCount);
            int vtPairs = 0;
            int eePairs = 0;
            int triPairs = 0;
            int eeBudget = useSelfContactEE != 0 && maxSelfContactEEPairs > 0 ? math.min(maxSelfContactEEPairs, math.max(0, bandBudget / 3)) : 0;
            float3 closureAxis = GetContactClosureAxis(c, true);
            float3 fallbackNormal = GetContactFallbackNormal(c, closureAxis);
            bool forceSweptSolve = c.lastSweptContactCandidates > 0 || c.lastSelfContactCcdHit != 0;
            float solveNormalPush = 0f;
            float solveMinDistance = float.PositiveInfinity;
            int solveCcdHits = 0;
            float ccdAdvancePush = 0f;

            SolveSelfContactTrianglePairSet(
                c.contactTriStartA,
                c.contactTriCountA,
                c.contactTriStartB,
                c.contactTriCountB,
                thickness,
                stiffness,
                maxPush,
                fallbackNormal,
                closureAxis,
                ref triPairs,
                triPairBudget,
                ref vtPairs,
                vtBudget,
                ref eePairs,
                eeBudget,
                forceSweptSolve,
                ref solveNormalPush,
                ref solveMinDistance,
                ref solveCcdHits,
                ref ccdAdvancePush);

            c.lastSelfContactSolveTriPairs = triPairs;
            c.lastSelfContactSolveVTPairs = vtPairs;
            c.lastSelfContactSolveEEPairs = eePairs;
            c.lastSelfContactSolveCcdHit = (byte)(solveCcdHits > 0 ? 1 : 0);
            c.lastSelfContactSolveForced = (byte)(forceSweptSolve ? 1 : 0);
            c.lastSelfContactSolveNormalPush = solveNormalPush;
            c.lastSelfContactSolveMinDistance = solveMinDistance;
            c.lastSelfContactCcdAdvancePush = ccdAdvancePush;
            constraints[index] = c;
        }

        private bool ShouldRunLocalWallContact(SutureLineConstraintInfo c, float thickness)
        {
            bool sweptOrCcdContact = c.lastSweptContactCandidates > 0 || c.lastSelfContactCcdHit != 0;
            bool sutureClosureContact = c.tensionOnlyActive != 0 &&
                c.contactTriCountA > 0 && c.contactTriCountB > 0 &&
                c.targetDelta > math.max(0.001f, thickness * 0.2f);
            return sutureClosureContact || c.closureBandSlideActive != 0 || sweptOrCcdContact || (c.contactLatched != 0 && c.lastPersistentContactPairs > 0);
        }

        private float ComputeSelfContactThickness(SutureLineConstraintInfo c)
        {
            float thicknessTerm = math.max(0f, c.localWallThickness) * math.max(0f, selfContactThicknessScale);
            float edgeTerm = math.max(0f, c.localAverageEdgeLength) * math.max(0f, selfContactEdgeScale);
            float raw = math.max(math.max(contactGap, thicknessTerm), edgeTerm);
            if (c.localWallThickness > 1e-6f)
            {
                float wallCap = math.max(contactGap, c.localWallThickness * 0.55f);
                raw = math.min(raw, wallCap);
            }
            return raw;
        }

        private float3 GetContactClosureAxis(SutureLineConstraintInfo c, bool patchEnabled)
        {
            float3 axis = math.normalizesafe(c.closureDirection, float3.zero);
            if (math.lengthsq(axis) > 1e-8f)
                return axis;

            float3 pointA = ReadConstrainedPoint(c.portalA, c.patchStartA, c.patchCountA, patchEnabled);
            float3 pointB = ReadConstrainedPoint(c.portalB, c.patchStartB, c.patchCountB, patchEnabled);
            return math.normalizesafe(pointB - pointA, new float3(1f, 0f, 0f));
        }

        private static float3 GetContactFallbackNormal(SutureLineConstraintInfo c, float3 closureAxis)
        {
            float3 normal = ProjectAwayFromClosureAxis(c.contactNormalHint, closureAxis);
            if (math.lengthsq(normal) < 1e-8f)
                normal = BuildPerpendicularToAxis(closureAxis);
            return math.normalizesafe(normal, new float3(0f, 1f, 0f));
        }

        private void SolveSelfContactTrianglePairSet(
            int triStartA,
            int triCountA,
            int triStartB,
            int triCountB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref int triPairs,
            int triPairBudget,
            ref int vtPairs,
            int vtBudget,
            ref int eePairs,
            int eeBudget,
            bool forceSweptSolve,
            ref float solveNormalPush,
            ref float solveMinDistance,
            ref int solveCcdHits,
            ref float ccdAdvancePush)
        {
            if (triStartA < 0 || triCountA <= 0 || triStartA + triCountA > contactTriangles.Length ||
                triStartB < 0 || triCountB <= 0 || triStartB + triCountB > contactTriangles.Length)
                return;

            int checkedPairs = 0;
            for (int ai = 0; ai < triCountA; ai++)
            {
                SutureLineContactTriangleInfo triA = contactTriangles[triStartA + ai];
                if (!IsValidContactTriangle(triA))
                    continue;

                for (int bi = 0; bi < triCountB; bi++)
                {
                    if (checkedPairs >= triPairBudget || (vtPairs >= vtBudget && (eeBudget <= 0 || eePairs >= eeBudget)))
                        return;
                    checkedPairs++;

                    SutureLineContactTriangleInfo triB = contactTriangles[triStartB + bi];
                    if (!IsValidContactTriangle(triB) || TrianglesShareVertex(triA, triB))
                        continue;
                    bool opposingVirtualSidewalls = IsOpposingVirtualSidewallTrianglePair(triA, triB);
                    float pairThickness = opposingVirtualSidewalls
                        ? ComputeVirtualSidewallContactSkin(thickness)
                        : thickness;
                    if (!ShouldConsiderSelfContactTrianglePair(triA, triB, pairThickness, closureAxis))
                        continue;
                    if (!AreTrianglePairNearOrSwept(triA, triB, pairThickness, out bool pairSwept))
                        continue;
                    if (opposingVirtualSidewalls &&
                        !IsMutualBestVirtualSidewallPair(triA, triB, triStartA, triCountA, triStartB, triCountB, pairThickness, closureAxis))
                        continue;

                    triPairs++;
                    float pairWeight = math.saturate(math.sqrt(math.max(0f, triA.weight) * math.max(0f, triB.weight)));
                    if (pairWeight <= 1e-5f)
                        continue;
                    float pairStiffness = stiffness * pairWeight;

                    SolveTriTriContactManifold(
                        triA,
                        triB,
                        pairThickness,
                        pairStiffness,
                        maxPush,
                        fallbackNormal,
                        closureAxis,
                        ref vtPairs,
                        vtBudget,
                        ref eePairs,
                        eeBudget,
                        forceSweptSolve && pairSwept,
                        ref solveNormalPush,
                        ref solveMinDistance,
                        ref solveCcdHits,
                        ref ccdAdvancePush);
                }
            }
        }

        private void SolveTriTriContactManifold(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref int vtPairs,
            int vtBudget,
            ref int eePairs,
            int eeBudget,
            bool forceSweptSolve,
            ref float solveNormalPush,
            ref float solveMinDistance,
            ref int solveCcdHits,
            ref float ccdAdvancePush)
        {
            bool opposingVirtualSidewalls = IsOpposingVirtualSidewallTrianglePair(triA, triB);
            if (opposingVirtualSidewalls)
            {
                if (vtPairs < vtBudget && SolveVirtualSidewallTriangleBarrier(
                    triA,
                    triB,
                    thickness,
                    stiffness,
                    maxPush,
                    fallbackNormal,
                    closureAxis,
                    ref solveNormalPush,
                    ref solveMinDistance))
                {
                    vtPairs++;
                }
                return;
            }

            int vtBefore = vtPairs;
            int eeBefore = eePairs;
            if (vtPairs < vtBudget && SolveSignedSelfContactVT(triA.v0, triB, thickness, stiffness, maxPush, fallbackNormal, closureAxis, forceSweptSolve, opposingVirtualSidewalls, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush))
                vtPairs++;
            if (vtPairs < vtBudget && SolveSignedSelfContactVT(triA.v1, triB, thickness, stiffness, maxPush, fallbackNormal, closureAxis, forceSweptSolve, opposingVirtualSidewalls, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush))
                vtPairs++;
            if (vtPairs < vtBudget && SolveSignedSelfContactVT(triA.v2, triB, thickness, stiffness, maxPush, fallbackNormal, closureAxis, forceSweptSolve, opposingVirtualSidewalls, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush))
                vtPairs++;

            if (vtPairs < vtBudget && SolveSignedSelfContactVT(triB.v0, triA, thickness, stiffness, maxPush, -fallbackNormal, closureAxis, forceSweptSolve, opposingVirtualSidewalls, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush))
                vtPairs++;
            if (vtPairs < vtBudget && SolveSignedSelfContactVT(triB.v1, triA, thickness, stiffness, maxPush, -fallbackNormal, closureAxis, forceSweptSolve, opposingVirtualSidewalls, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush))
                vtPairs++;
            if (vtPairs < vtBudget && SolveSignedSelfContactVT(triB.v2, triA, thickness, stiffness, maxPush, -fallbackNormal, closureAxis, forceSweptSolve, opposingVirtualSidewalls, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush))
                vtPairs++;

            if (eeBudget > 0 && eePairs < eeBudget)
                SolveTriTriLimitedEE(triA, triB, thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, opposingVirtualSidewalls, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);

            if (forceSweptSolve && !opposingVirtualSidewalls && vtPairs == vtBefore && eePairs == eeBefore &&
                ApplySweptTrianglePairAdvancePush(triA, triB, thickness, stiffness, maxPush, fallbackNormal, closureAxis, opposingVirtualSidewalls, ref solveNormalPush, ref solveMinDistance, ref ccdAdvancePush))
            {
                solveCcdHits++;
                if (vtPairs < vtBudget)
                    vtPairs++;
            }
        }

        private bool SolveVirtualSidewallTriangleBarrier(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref float solveNormalPush,
            ref float solveMinDistance)
        {
            float3 centerA = GetTriangleCenter(triA);
            float3 centerB = GetTriangleCenter(triB);
            float3 delta = centerA - centerB;
            float3 axis = math.normalizesafe(closureAxis, math.normalizesafe(fallbackNormal, new float3(1f, 0f, 0f)));
            if (math.lengthsq(axis) < 1e-8f)
                return false;

            float axisDistance = math.abs(math.dot(delta, axis));
            solveMinDistance = math.min(solveMinDistance, axisDistance);
            if (axisDistance >= thickness)
                return false;

            float3 n = axis * (math.dot(delta, axis) >= 0f ? 1f : -1f);
            float denom =
                (GetInvMass(triA.v0) + GetInvMass(triA.v1) + GetInvMass(triA.v2) +
                 GetInvMass(triB.v0) + GetInvMass(triB.v1) + GetInvMass(triB.v2)) / 9f;
            if (denom <= 1e-8f)
                return false;

            float depth = thickness - axisDistance;
            float depth01 = math.saturate(depth / math.max(thickness, 1e-6f));
            float smoothDepth = depth * depth01 * depth01;
            float sidewallStiffness = math.min(stiffness, 0.25f) * 0.18f;
            float normalPush = (smoothDepth * sidewallStiffness) / denom;
            float sidewallMaxPush = maxPush > 0f
                ? math.min(maxPush * 0.08f, thickness * 0.18f)
                : thickness * 0.18f;
            normalPush = math.min(normalPush, sidewallMaxPush);
            if (normalPush <= 1e-9f)
                return false;

            float3 pushA = n * (normalPush / 3f);
            float3 pushB = -n * (normalPush / 3f);
            ApplyVertexCorrection(triA.v0, pushA);
            ApplyVertexCorrection(triA.v1, pushA);
            ApplyVertexCorrection(triA.v2, pushA);
            ApplyVertexCorrection(triB.v0, pushB);
            ApplyVertexCorrection(triB.v1, pushB);
            ApplyVertexCorrection(triB.v2, pushB);

            solveNormalPush = math.max(solveNormalPush, normalPush);
            return true;
        }

        private bool SolveSignedSelfContactVT(
            int vertex,
            SutureLineContactTriangleInfo tri,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            bool forceSweptSolve,
            bool preserveClosureAxisNormal,
            ref float solveNormalPush,
            ref float solveMinDistance,
            ref int solveCcdHits,
            ref float ccdAdvancePush)
        {
            if (!IsValidVertex(vertex) ||
                vertex == tri.v0 || vertex == tri.v1 || vertex == tri.v2)
                return false;

            float3 p = predictPositions[vertex];
            float3 a = predictPositions[tri.v0];
            float3 b = predictPositions[tri.v1];
            float3 c = predictPositions[tri.v2];
            float3 triNormal = GetTriangleNormal(a, b, c, fallbackNormal);
            float signedDistance = math.dot(p - a, triNormal);
            float3 projected = p - triNormal * signedDistance;
            float3 bary = BarycentricOnTriangle(projected, a, b, c);
            bool projectedInside = bary.x >= -0.02f && bary.y >= -0.02f && bary.z >= -0.02f;
            float distance;
            float3 n;
            if (projectedInside)
            {
                distance = math.abs(signedDistance);
                n = signedDistance >= 0f ? triNormal : -triNormal;
            }
            else
            {
                float3 closest = ClosestPointOnTriangle(p, a, b, c, out bary);
                float3 delta = p - closest;
                distance = math.length(delta);
                n = distance > 1e-7f ? delta / distance : triNormal;
            }

            solveMinDistance = math.min(solveMinDistance, distance);
            bool ccdHit = false;
            if (distance >= thickness)
            {
                ccdHit = IsVertexTriangleSweptCandidate(vertex, tri, thickness) &&
                         EstimatePreviousVTDistance(vertex, tri) < thickness * 1.5f;
                if (ccdHit)
                    solveCcdHits++;
                bool allowForcedAdvance = forceSweptSolve && !preserveClosureAxisNormal;
                if (!ccdHit && !allowForcedAdvance)
                    return false;
            }

            float invVertex = GetInvMass(vertex);
            float invTri =
                GetInvMass(tri.v0) * bary.x * bary.x +
                GetInvMass(tri.v1) * bary.y * bary.y +
                GetInvMass(tri.v2) * bary.z * bary.z;
            float denom = invVertex + invTri;
            if (denom <= 1e-8f)
                return false;

            n = BuildSelfContactNormal(n, fallbackNormal, closureAxis, preserveClosureAxisNormal, p - GetTriangleCenter(tri));
            if (math.lengthsq(n) < 1e-8f)
                return false;
            bool forcedAdvance = forceSweptSolve && !preserveClosureAxisNormal;
            float effectiveDistance = (ccdHit || forcedAdvance) && distance >= thickness ? thickness * 0.75f : distance;
            float normalPush = ((thickness - effectiveDistance) * stiffness) / denom;
            if (maxPush > 0f)
                normalPush = math.min(normalPush, maxPush);
            if (normalPush <= 1e-9f)
                return false;

            ApplyVertexCorrection(vertex, n * normalPush);
            float3 triCorrection = -n * normalPush;
            ApplyVertexCorrection(tri.v0, triCorrection * bary.x);
            ApplyVertexCorrection(tri.v1, triCorrection * bary.y);
            ApplyVertexCorrection(tri.v2, triCorrection * bary.z);

            solveNormalPush = math.max(solveNormalPush, normalPush);
            if ((ccdHit || forcedAdvance) && distance >= thickness)
                ccdAdvancePush = math.max(ccdAdvancePush, normalPush);
            return true;
        }

        private bool ApplySweptTrianglePairAdvancePush(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            bool preserveClosureAxisNormal,
            ref float solveNormalPush,
            ref float solveMinDistance,
            ref float ccdAdvancePush)
        {
            float3 centerA = GetTriangleCenter(triA);
            float3 centerB = GetTriangleCenter(triB);
            float3 delta = centerA - centerB;
            float distance = math.length(delta);
            solveMinDistance = math.min(solveMinDistance, distance);

            float3 n = BuildSelfContactNormal(
                distance > 1e-7f ? delta / distance : fallbackNormal,
                fallbackNormal,
                closureAxis,
                preserveClosureAxisNormal,
                delta);
            if (math.lengthsq(n) < 1e-8f)
                return false;

            float denom =
                (GetInvMass(triA.v0) + GetInvMass(triA.v1) + GetInvMass(triA.v2) +
                 GetInvMass(triB.v0) + GetInvMass(triB.v1) + GetInvMass(triB.v2)) / 9f;
            if (denom <= 1e-8f)
                return false;

            float effectiveDistance = distance >= thickness ? thickness * 0.8f : distance;
            float normalPush = ((thickness - effectiveDistance) * stiffness) / denom;
            if (maxPush > 0f)
                normalPush = math.min(normalPush, maxPush);
            normalPush *= 0.5f;
            if (normalPush <= 1e-9f)
                return false;

            float3 pushA = n * normalPush / 3f;
            float3 pushB = -n * normalPush / 3f;
            ApplyVertexCorrection(triA.v0, pushA);
            ApplyVertexCorrection(triA.v1, pushA);
            ApplyVertexCorrection(triA.v2, pushA);
            ApplyVertexCorrection(triB.v0, pushB);
            ApplyVertexCorrection(triB.v1, pushB);
            ApplyVertexCorrection(triB.v2, pushB);

            solveNormalPush = math.max(solveNormalPush, normalPush);
            ccdAdvancePush = math.max(ccdAdvancePush, normalPush);
            return true;
        }

        private void SolveTriTriLimitedEE(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref int eePairs,
            int eeBudget,
            bool forceSweptSolve,
            bool preserveClosureAxisNormal,
            ref float solveNormalPush,
            ref float solveMinDistance,
            ref int solveCcdHits,
            ref float ccdAdvancePush)
        {
            SolveLimitedEE(GetTriangleEdge(triA, 0), GetTriangleEdge(triB, 0), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
            SolveLimitedEE(GetTriangleEdge(triA, 0), GetTriangleEdge(triB, 1), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
            SolveLimitedEE(GetTriangleEdge(triA, 0), GetTriangleEdge(triB, 2), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
            SolveLimitedEE(GetTriangleEdge(triA, 1), GetTriangleEdge(triB, 0), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
            SolveLimitedEE(GetTriangleEdge(triA, 1), GetTriangleEdge(triB, 1), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
            SolveLimitedEE(GetTriangleEdge(triA, 1), GetTriangleEdge(triB, 2), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
            SolveLimitedEE(GetTriangleEdge(triA, 2), GetTriangleEdge(triB, 0), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
            SolveLimitedEE(GetTriangleEdge(triA, 2), GetTriangleEdge(triB, 1), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
            SolveLimitedEE(GetTriangleEdge(triA, 2), GetTriangleEdge(triB, 2), thickness, stiffness, maxPush, fallbackNormal, closureAxis, ref eePairs, eeBudget, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
        }

        private void SolveLimitedEE(
            SutureLineContactEdgeInfo edgeA,
            SutureLineContactEdgeInfo edgeB,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref int eePairs,
            int eeBudget,
            bool forceSweptSolve,
            bool preserveClosureAxisNormal,
            ref float solveNormalPush,
            ref float solveMinDistance,
            ref int solveCcdHits,
            ref float ccdAdvancePush)
        {
            if (eePairs >= eeBudget)
                return;
            if (SolveSelfContactEE(edgeA, edgeB, thickness, stiffness, maxPush, fallbackNormal, closureAxis, forceSweptSolve, preserveClosureAxisNormal, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush))
                eePairs++;
        }

        private void SolveSelfContactTriangleSetAgainstPatch(
            int triStart,
            int triCount,
            int patchStart,
            int patchCount,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            ref int solvedPairs,
            int pairBudget)
        {
            if (triStart < 0 || triCount <= 0 || triStart + triCount > contactTriangles.Length ||
                patchStart < 0 || patchCount <= 0 || patchStart + patchCount > patchVertices.Length)
                return;

            for (int ti = 0; ti < triCount; ti++)
            {
                if (solvedPairs >= pairBudget)
                    return;
                SutureLineContactTriangleInfo tri = contactTriangles[triStart + ti];
                if (!IsValidContactTriangle(tri))
                    continue;

                for (int pi = 0; pi < patchCount; pi++)
                {
                    if (solvedPairs >= pairBudget)
                        return;

                    SutureLinePatchVertexInfo pv = patchVertices[patchStart + pi];
                    if (!IsValidVertex(pv.vertexIndex) ||
                        pv.vertexIndex == tri.v0 ||
                        pv.vertexIndex == tri.v1 ||
                        pv.vertexIndex == tri.v2)
                        continue;

                    float pairWeight = math.saturate(math.sqrt(math.max(0f, tri.weight) * GetPatchContactWeight(pv)));
                    if (pairWeight <= 1e-5f)
                        continue;

                    if (SolveSelfContactVT(pv.vertexIndex, tri, thickness, stiffness * pairWeight, maxPush, fallbackNormal, closureAxis))
                        solvedPairs++;
                }
            }
        }

        private bool SolveSelfContactVT(
            int vertex,
            SutureLineContactTriangleInfo tri,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis)
        {
            float3 p = predictPositions[vertex];
            float3 a = predictPositions[tri.v0];
            float3 b = predictPositions[tri.v1];
            float3 c = predictPositions[tri.v2];
            float3 closest = ClosestPointOnTriangle(p, a, b, c, out float3 bary);
            float3 delta = p - closest;
            float distance = math.length(delta);
            bool ccdHit = false;
            if (distance >= thickness)
            {
                ccdHit = IsVertexTriangleSweptCandidate(vertex, tri, thickness) &&
                         EstimatePreviousVTDistance(vertex, tri) < thickness * 1.5f;
                if (!ccdHit)
                    return false;
            }

            float invVertex = GetInvMass(vertex);
            float invTri =
                GetInvMass(tri.v0) * bary.x * bary.x +
                GetInvMass(tri.v1) * bary.y * bary.y +
                GetInvMass(tri.v2) * bary.z * bary.z;
            float denom = invVertex + invTri;
            if (denom <= 1e-8f)
                return false;

            float3 n = distance > 1e-7f
                ? delta / distance
                : math.normalizesafe(fallbackNormal, new float3(1f, 0f, 0f));
            n = math.normalizesafe(ProjectAwayFromClosureAxis(n, closureAxis), float3.zero);
            if (math.lengthsq(n) < 1e-8f)
                n = math.normalizesafe(fallbackNormal, float3.zero);
            if (math.lengthsq(n) < 1e-8f)
                return false;

            float effectiveDistance = ccdHit && distance >= thickness ? thickness * 0.75f : distance;
            float normalPush = ((thickness - effectiveDistance) * stiffness) / denom;
            if (maxPush > 0f)
                normalPush = math.min(normalPush, maxPush);
            if (normalPush <= 1e-9f)
                return false;

            ApplyVertexCorrection(vertex, n * normalPush);
            float3 triCorrection = -n * normalPush;
            ApplyVertexCorrection(tri.v0, triCorrection * bary.x);
            ApplyVertexCorrection(tri.v1, triCorrection * bary.y);
            ApplyVertexCorrection(tri.v2, triCorrection * bary.z);

            return true;
        }

        private void ApplyTangentialVT(int vertex, SutureLineContactTriangleInfo tri, float3 bary, float3 normal, float normalPush, float denom, float3 closureAxis)
        {
            if (!previousPositions.IsCreated || previousPositions.Length != predictPositions.Length)
                return;
            float damping = math.saturate(selfContactTangentialDamping);
            float friction = math.max(0f, selfContactFriction);
            if (damping <= 0f || friction <= 0f || denom <= 1e-8f)
                return;

            float3 p0 = previousPositions[vertex];
            float3 triPrev = previousPositions[tri.v0] * bary.x + previousPositions[tri.v1] * bary.y + previousPositions[tri.v2] * bary.z;
            float3 p1 = predictPositions[vertex];
            float3 triNow = predictPositions[tri.v0] * bary.x + predictPositions[tri.v1] * bary.y + predictPositions[tri.v2] * bary.z;
            float3 relMove = (p1 - p0) - (triNow - triPrev);
            float3 tangent = relMove - normal * math.dot(relMove, normal);
            tangent = ProjectAwayFromClosureAxis(tangent, closureAxis);
            float tangentLen = math.length(tangent);
            if (tangentLen <= 1e-8f)
                return;

            float tangentPush = math.min(tangentLen * damping / denom, normalPush * friction);
            float3 t = -tangent / tangentLen;
            ApplyVertexCorrection(vertex, t * tangentPush);
            float3 triCorrection = -t * tangentPush;
            ApplyVertexCorrection(tri.v0, triCorrection * bary.x);
            ApplyVertexCorrection(tri.v1, triCorrection * bary.y);
            ApplyVertexCorrection(tri.v2, triCorrection * bary.z);
        }

        private bool SolveSelfContactEE(SutureLineContactEdgeInfo ea, SutureLineContactEdgeInfo eb, float thickness, float stiffness, float maxPush, float3 fallbackNormal, float3 closureAxis, bool forceSweptSolve)
        {
            float solveNormalPush = 0f;
            float solveMinDistance = float.PositiveInfinity;
            int solveCcdHits = 0;
            float ccdAdvancePush = 0f;
            return SolveSelfContactEE(ea, eb, thickness, stiffness, maxPush, fallbackNormal, closureAxis, forceSweptSolve, false, ref solveNormalPush, ref solveMinDistance, ref solveCcdHits, ref ccdAdvancePush);
        }

        private bool SolveSelfContactEE(
            SutureLineContactEdgeInfo ea,
            SutureLineContactEdgeInfo eb,
            float thickness,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis,
            bool forceSweptSolve,
            bool preserveClosureAxisNormal,
            ref float solveNormalPush,
            ref float solveMinDistance,
            ref int solveCcdHits,
            ref float ccdAdvancePush)
        {
            float3 a0 = predictPositions[ea.v0];
            float3 a1 = predictPositions[ea.v1];
            float3 b0 = predictPositions[eb.v0];
            float3 b1 = predictPositions[eb.v1];
            ClosestPointsOnSegments(a0, a1, b0, b1, out float sa, out float sb, out float3 pa, out float3 pb);
            float3 delta = pa - pb;
            float distance = math.length(delta);
            solveMinDistance = math.min(solveMinDistance, distance);
            bool ccdHit = false;
            if (distance >= thickness)
            {
                ccdHit = AreSweptEdgesNear(ea, eb, thickness) && EstimatePreviousEEDistance(ea, eb) < thickness * 1.5f;
                if (ccdHit)
                    solveCcdHits++;
                bool allowForcedAdvance = forceSweptSolve && !preserveClosureAxisNormal;
                if (!ccdHit && !allowForcedAdvance)
                    return false;
            }

            float wa0 = 1f - sa;
            float wa1 = sa;
            float wb0 = 1f - sb;
            float wb1 = sb;
            float denom =
                GetInvMass(ea.v0) * wa0 * wa0 +
                GetInvMass(ea.v1) * wa1 * wa1 +
                GetInvMass(eb.v0) * wb0 * wb0 +
                GetInvMass(eb.v1) * wb1 * wb1;
            if (denom <= 1e-8f)
                return false;

            float3 n = BuildSelfContactNormal(
                distance > 1e-7f ? delta / distance : math.normalizesafe(fallbackNormal, new float3(1f, 0f, 0f)),
                fallbackNormal,
                closureAxis,
                preserveClosureAxisNormal,
                delta);
            if (math.lengthsq(n) < 1e-8f)
                return false;
            bool forcedAdvance = forceSweptSolve && !preserveClosureAxisNormal;
            float effectiveDistance = (ccdHit || forcedAdvance) && distance >= thickness ? thickness * 0.75f : distance;
            float normalPush = ((thickness - effectiveDistance) * stiffness) / denom;
            if (maxPush > 0f)
                normalPush = math.min(normalPush, maxPush);
            if (normalPush <= 1e-9f)
                return false;

            ApplyVertexCorrection(ea.v0, n * normalPush * wa0);
            ApplyVertexCorrection(ea.v1, n * normalPush * wa1);
            ApplyVertexCorrection(eb.v0, -n * normalPush * wb0);
            ApplyVertexCorrection(eb.v1, -n * normalPush * wb1);

            solveNormalPush = math.max(solveNormalPush, normalPush);
            if ((ccdHit || forcedAdvance) && distance >= thickness)
                ccdAdvancePush = math.max(ccdAdvancePush, normalPush);
            return true;
        }

        private void ApplyTangentialEE(SutureLineContactEdgeInfo ea, SutureLineContactEdgeInfo eb, float sa, float sb, float3 normal, float normalPush, float denom, float3 closureAxis)
        {
            if (!previousPositions.IsCreated || previousPositions.Length != predictPositions.Length)
                return;
            float damping = math.saturate(selfContactTangentialDamping);
            float friction = math.max(0f, selfContactFriction);
            if (damping <= 0f || friction <= 0f || denom <= 1e-8f)
                return;

            float wa0 = 1f - sa;
            float wa1 = sa;
            float wb0 = 1f - sb;
            float wb1 = sb;
            float3 prevA = previousPositions[ea.v0] * wa0 + previousPositions[ea.v1] * wa1;
            float3 prevB = previousPositions[eb.v0] * wb0 + previousPositions[eb.v1] * wb1;
            float3 nowA = predictPositions[ea.v0] * wa0 + predictPositions[ea.v1] * wa1;
            float3 nowB = predictPositions[eb.v0] * wb0 + predictPositions[eb.v1] * wb1;
            float3 relMove = (nowA - prevA) - (nowB - prevB);
            float3 tangent = relMove - normal * math.dot(relMove, normal);
            tangent = ProjectAwayFromClosureAxis(tangent, closureAxis);
            float tangentLen = math.length(tangent);
            if (tangentLen <= 1e-8f)
                return;

            float tangentPush = math.min(tangentLen * damping / denom, normalPush * friction);
            float3 t = -tangent / tangentLen;
            ApplyVertexCorrection(ea.v0, t * tangentPush * wa0);
            ApplyVertexCorrection(ea.v1, t * tangentPush * wa1);
            ApplyVertexCorrection(eb.v0, -t * tangentPush * wb0);
            ApplyVertexCorrection(eb.v1, -t * tangentPush * wb1);
        }

        private static float3 ProjectAwayFromClosureAxis(float3 vector, float3 closureAxis)
        {
            float3 axis = math.normalizesafe(closureAxis, float3.zero);
            if (math.lengthsq(vector) < 1e-8f || math.lengthsq(axis) < 1e-8f)
                return vector;

            return vector - axis * math.dot(vector, axis);
        }

        private static float3 BuildSelfContactNormal(
            float3 rawNormal,
            float3 fallbackNormal,
            float3 closureAxis,
            bool preserveClosureAxisNormal,
            float3 separationDelta)
        {
            if (preserveClosureAxisNormal)
            {
                float3 axis = math.normalizesafe(closureAxis, float3.zero);
                if (math.lengthsq(axis) > 1e-8f)
                    return axis * (math.dot(separationDelta, axis) >= 0f ? 1f : -1f);

                float3 sidewallNormal = math.normalizesafe(rawNormal, math.normalizesafe(fallbackNormal, float3.zero));
                if (math.lengthsq(sidewallNormal) > 1e-8f)
                    return sidewallNormal;
            }

            float3 n = math.normalizesafe(ProjectAwayFromClosureAxis(rawNormal, closureAxis), float3.zero);
            if (math.lengthsq(n) < 1e-8f)
                n = math.normalizesafe(fallbackNormal, float3.zero);
            return n;
        }

        private float ComputeVirtualSidewallContactSkin(float thickness)
        {
            float gapSkin = contactGap > 1e-7f ? contactGap * 0.5f : 0f;
            float thicknessSkin = math.max(0f, thickness) * 0.08f;
            float skin = math.max(math.max(gapSkin, thicknessSkin), 1e-5f);
            return math.min(math.max(1e-5f, thickness), skin);
        }

        private bool IsMutualBestVirtualSidewallPair(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            int triStartA,
            int triCountA,
            int triStartB,
            int triCountB,
            float thickness,
            float3 closureAxis)
        {
            if (!TryScoreVirtualSidewallPair(triA, triB, thickness, closureAxis, out float pairScore))
                return false;

            float epsilon = math.max(1e-8f, pairScore * 1e-4f);
            for (int bi = 0; bi < triCountB; bi++)
            {
                SutureLineContactTriangleInfo candidate = contactTriangles[triStartB + bi];
                if (candidate.v0 == triB.v0 && candidate.v1 == triB.v1 && candidate.v2 == triB.v2)
                    continue;
                if (TryScoreVirtualSidewallPair(triA, candidate, thickness, closureAxis, out float score) &&
                    score + epsilon < pairScore)
                    return false;
            }

            for (int ai = 0; ai < triCountA; ai++)
            {
                SutureLineContactTriangleInfo candidate = contactTriangles[triStartA + ai];
                if (candidate.v0 == triA.v0 && candidate.v1 == triA.v1 && candidate.v2 == triA.v2)
                    continue;
                if (TryScoreVirtualSidewallPair(candidate, triB, thickness, closureAxis, out float score) &&
                    score + epsilon < pairScore)
                    return false;
            }

            return true;
        }

        private bool TryScoreVirtualSidewallPair(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float3 closureAxis,
            out float score)
        {
            score = float.PositiveInfinity;
            if (!IsValidContactTriangle(triA) ||
                !IsValidContactTriangle(triB) ||
                TrianglesShareVertex(triA, triB) ||
                !IsOpposingVirtualSidewallTrianglePair(triA, triB) ||
                !ShouldConsiderSelfContactTrianglePair(triA, triB, thickness, closureAxis) ||
                !AreTrianglePairNearOrSwept(triA, triB, thickness))
                return false;

            float3 delta = GetTriangleCenter(triB) - GetTriangleCenter(triA);
            float3 axis = math.normalizesafe(closureAxis, float3.zero);
            float along = math.lengthsq(axis) > 1e-8f ? math.dot(delta, axis) : 0f;
            float3 tangent = delta - axis * along;
            score = math.lengthsq(tangent) + along * along * 0.25f;
            return true;
        }

        private static float3 BuildPerpendicularToAxis(float3 closureAxis)
        {
            float3 axis = math.normalizesafe(closureAxis, new float3(1f, 0f, 0f));
            float3 candidate = math.cross(axis, new float3(0f, 1f, 0f));
            if (math.lengthsq(candidate) < 1e-8f)
                candidate = math.cross(axis, new float3(0f, 0f, 1f));
            return math.normalizesafe(candidate, new float3(0f, 1f, 0f));
        }

        private bool AreTrianglePairNearOrSwept(SutureLineContactTriangleInfo a, SutureLineContactTriangleInfo b, float inflate)
        {
            return AreTrianglePairNearOrSwept(a, b, inflate, out _);
        }

        private bool AreTrianglePairNearOrSwept(
            SutureLineContactTriangleInfo a,
            SutureLineContactTriangleInfo b,
            float inflate,
            out bool swept)
        {
            swept = false;
            GetTriangleAabb(a, inflate, false, out float3 minA, out float3 maxA);
            GetTriangleAabb(b, inflate, false, out float3 minB, out float3 maxB);
            if (AabbOverlap(minA, maxA, minB, maxB))
                return true;
            if (!previousPositions.IsCreated || previousPositions.Length != predictPositions.Length)
                return false;
            GetTriangleAabb(a, inflate, true, out minA, out maxA);
            GetTriangleAabb(b, inflate, true, out minB, out maxB);
            swept = AabbOverlap(minA, maxA, minB, maxB);
            return swept;
        }

        private bool ShouldConsiderSelfContactTrianglePair(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB,
            float thickness,
            float3 closureAxis)
        {
            bool virtualA = IsVirtualSidewallContactTriangle(triA);
            bool virtualB = IsVirtualSidewallContactTriangle(triB);
            bool opposingVirtualSidewalls = IsOpposingVirtualSidewallTrianglePair(triA, triB);
            if (virtualA && virtualB && !opposingVirtualSidewalls)
                return false;

            if (!virtualA && !virtualB)
            {
                bool closureA = IsClosureBandContactTriangle(triA);
                bool closureB = IsClosureBandContactTriangle(triB);
                if (closureA && closureB)
                {
                    float minBandT = math.min(triA.bandT, triB.bandT);
                    if (minBandT < 0.16f)
                        return false;
                    if (math.abs(triA.bandT - triB.bandT) > 0.46f)
                        return false;
                }
            }

            float3 centerA = GetTriangleCenter(triA);
            float3 centerB = GetTriangleCenter(triB);
            float3 delta = centerB - centerA;
            float maxDistance = opposingVirtualSidewalls
                ? math.max(EstimateTriangleRadius(triA) + EstimateTriangleRadius(triB) + thickness * 2f, 1e-4f)
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

        private static bool IsClosureBandContactTriangle(SutureLineContactTriangleInfo tri)
        {
            return tri.closureBandHits > 0 &&
                (tri.flags & SutureLinePatchVertexInfo.FlagClosureBand) != 0;
        }

        private static bool IsVirtualSidewallContactTriangle(SutureLineContactTriangleInfo tri)
        {
            return (tri.flags & SutureLineContactTriangleInfo.FlagVirtualSidewall) != 0;
        }

        private static bool IsOpposingVirtualSidewallTrianglePair(
            SutureLineContactTriangleInfo triA,
            SutureLineContactTriangleInfo triB)
        {
            return IsVirtualSidewallContactTriangle(triA) &&
                IsVirtualSidewallContactTriangle(triB) &&
                triA.sourceSide != 0 &&
                triB.sourceSide != 0 &&
                triA.sourceSide != triB.sourceSide;
        }

        private float3 GetTriangleCenter(SutureLineContactTriangleInfo tri)
        {
            return (predictPositions[tri.v0] + predictPositions[tri.v1] + predictPositions[tri.v2]) / 3f;
        }

        private float EstimateTriangleRadius(SutureLineContactTriangleInfo tri)
        {
            float3 center = GetTriangleCenter(tri);
            float r0 = math.length(predictPositions[tri.v0] - center);
            float r1 = math.length(predictPositions[tri.v1] - center);
            float r2 = math.length(predictPositions[tri.v2] - center);
            return math.max(r0, math.max(r1, r2));
        }

        private void GetTriangleAabb(SutureLineContactTriangleInfo tri, float inflate, bool swept, out float3 minPoint, out float3 maxPoint)
        {
            float3 p0 = predictPositions[tri.v0];
            float3 p1 = predictPositions[tri.v1];
            float3 p2 = predictPositions[tri.v2];
            minPoint = math.min(math.min(p0, p1), p2);
            maxPoint = math.max(math.max(p0, p1), p2);
            if (swept)
            {
                float3 q0 = previousPositions[tri.v0];
                float3 q1 = previousPositions[tri.v1];
                float3 q2 = previousPositions[tri.v2];
                minPoint = math.min(minPoint, math.min(math.min(q0, q1), q2));
                maxPoint = math.max(maxPoint, math.max(math.max(q0, q1), q2));
            }
            minPoint -= new float3(inflate);
            maxPoint += new float3(inflate);
        }

        private bool TrianglesShareVertex(SutureLineContactTriangleInfo a, SutureLineContactTriangleInfo b)
        {
            return a.v0 == b.v0 || a.v0 == b.v1 || a.v0 == b.v2 ||
                   a.v1 == b.v0 || a.v1 == b.v1 || a.v1 == b.v2 ||
                   a.v2 == b.v0 || a.v2 == b.v1 || a.v2 == b.v2;
        }

        private SutureLineContactEdgeInfo GetTriangleEdge(SutureLineContactTriangleInfo tri, int edgeIndex)
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

        private float3 GetTriangleNormal(float3 a, float3 b, float3 c, float3 fallbackNormal)
        {
            float3 n = math.normalizesafe(math.cross(b - a, c - a), math.normalizesafe(fallbackNormal, new float3(1f, 0f, 0f)));
            float3 fallback = math.normalizesafe(fallbackNormal, n);
            if (math.dot(n, fallback) < 0f)
                n = -n;
            return n;
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

        private bool IsVertexTriangleSweptCandidate(int vertex, SutureLineContactTriangleInfo tri, float inflate)
        {
            if (!previousPositions.IsCreated || previousPositions.Length != predictPositions.Length)
                return false;
            float3 minV = math.min(previousPositions[vertex], predictPositions[vertex]) - new float3(inflate);
            float3 maxV = math.max(previousPositions[vertex], predictPositions[vertex]) + new float3(inflate);
            float3 minT = math.min(math.min(previousPositions[tri.v0], previousPositions[tri.v1]), previousPositions[tri.v2]);
            minT = math.min(minT, math.min(math.min(predictPositions[tri.v0], predictPositions[tri.v1]), predictPositions[tri.v2])) - new float3(inflate);
            float3 maxT = math.max(math.max(previousPositions[tri.v0], previousPositions[tri.v1]), previousPositions[tri.v2]);
            maxT = math.max(maxT, math.max(math.max(predictPositions[tri.v0], predictPositions[tri.v1]), predictPositions[tri.v2])) + new float3(inflate);
            return AabbOverlap(minV, maxV, minT, maxT);
        }

        private float EstimatePreviousVTDistance(int vertex, SutureLineContactTriangleInfo tri)
        {
            if (!previousPositions.IsCreated || previousPositions.Length != predictPositions.Length)
                return float.PositiveInfinity;
            float3 p = previousPositions[vertex];
            float3 closest = ClosestPointOnTriangle(p, previousPositions[tri.v0], previousPositions[tri.v1], previousPositions[tri.v2], out _);
            return math.distance(p, closest);
        }

        private bool AreSweptEdgesNear(SutureLineContactEdgeInfo ea, SutureLineContactEdgeInfo eb, float inflate)
        {
            if (!previousPositions.IsCreated || previousPositions.Length != predictPositions.Length)
                return false;
            GetSweptEdgeAabb(ea, inflate, out float3 minA, out float3 maxA);
            GetSweptEdgeAabb(eb, inflate, out float3 minB, out float3 maxB);
            return AabbOverlap(minA, maxA, minB, maxB);
        }

        private float EstimatePreviousEEDistance(SutureLineContactEdgeInfo ea, SutureLineContactEdgeInfo eb)
        {
            if (!previousPositions.IsCreated || previousPositions.Length != predictPositions.Length)
                return float.PositiveInfinity;
            ClosestPointsOnSegments(
                previousPositions[ea.v0],
                previousPositions[ea.v1],
                previousPositions[eb.v0],
                previousPositions[eb.v1],
                out _, out _, out float3 pa, out float3 pb);
            return math.distance(pa, pb);
        }

        private void GetSweptEdgeAabb(SutureLineContactEdgeInfo e, float inflate, out float3 minPoint, out float3 maxPoint)
        {
            minPoint = math.min(math.min(previousPositions[e.v0], previousPositions[e.v1]), math.min(predictPositions[e.v0], predictPositions[e.v1])) - new float3(inflate);
            maxPoint = math.max(math.max(previousPositions[e.v0], previousPositions[e.v1]), math.max(predictPositions[e.v0], predictPositions[e.v1])) + new float3(inflate);
        }

        private static bool AabbOverlap(float3 minA, float3 maxA, float3 minB, float3 maxB)
        {
            return minA.x <= maxB.x && maxA.x >= minB.x &&
                   minA.y <= maxB.y && maxA.y >= minB.y &&
                   minA.z <= maxB.z && maxA.z >= minB.z;
        }

        private bool IsValidContactEdge(SutureLineContactEdgeInfo edge)
        {
            return IsValidVertex(edge.v0) && IsValidVertex(edge.v1) && edge.v0 != edge.v1;
        }

        private void SolveRepresentativeContact(SutureLineConstraintInfo c, bool patchEnabled, float gap, float stiffness, float maxPush)
        {
            float3 pointA = ReadConstrainedPoint(c.portalA, c.patchStartA, c.patchCountA, patchEnabled);
            float3 pointB = ReadConstrainedPoint(c.portalB, c.patchStartB, c.patchCountB, patchEnabled);
            float3 delta = pointB - pointA;
            float distance = math.length(delta);
            if (distance >= gap)
                return;

            float3 closureAxis = GetContactClosureAxis(c, patchEnabled);
            float3 fallbackNormal = GetContactFallbackNormal(c, closureAxis);
            float3 n = distance > 1e-7f
                ? ProjectAwayFromClosureAxis(delta / distance, closureAxis)
                : fallbackNormal;
            if (math.lengthsq(n) < 1e-8f)
                n = fallbackNormal;
            n = math.normalizesafe(n, float3.zero);
            if (math.lengthsq(n) < 1e-8f)
                return;
            float signedNormalDistance = math.dot(delta, n);
            if (signedNormalDistance < 0f)
                n = -n;
            float normalDistance = math.abs(signedNormalDistance);

            float invWeightA = ConstrainedPointInvWeight(c.portalA, c.patchStartA, c.patchCountA, patchEnabled);
            float invWeightB = ConstrainedPointInvWeight(c.portalB, c.patchStartB, c.patchCountB, patchEnabled);
            float denom = invWeightA + invWeightB;
            if (denom <= 1e-8f)
                return;

            float pushMagnitude = ((gap - normalDistance) * stiffness) / denom;
            if (maxPush > 0f)
                pushMagnitude = math.min(pushMagnitude, maxPush);
            if (pushMagnitude <= 1e-9f)
                return;

            ApplyConstrainedPointCorrection(c.portalA, c.patchStartA, c.patchCountA, patchEnabled, -n * pushMagnitude);
            ApplyConstrainedPointCorrection(c.portalB, c.patchStartB, c.patchCountB, patchEnabled, n * pushMagnitude);
        }

        private void SolvePatchPairContacts(SutureLineConstraintInfo c, float gap, float stiffness, float maxPush)
        {
            if (c.patchCountA <= 0 || c.patchCountB <= 0 ||
                c.patchStartA < 0 || c.patchStartB < 0 ||
                c.patchStartA + c.patchCountA > patchVertices.Length ||
                c.patchStartB + c.patchCountB > patchVertices.Length)
                return;

            float3 closureAxis = GetContactClosureAxis(c, true);
            float3 fallbackNormal = GetContactFallbackNormal(c, closureAxis);
            int checkLimit = maxPatchPairChecksPerLine > 0 ? maxPatchPairChecksPerLine : 256;
            int checks = 0;
            for (int i = 0; i < c.patchCountA; i++)
            {
                SutureLinePatchVertexInfo a = patchVertices[c.patchStartA + i];
                if (!IsValidVertex(a.vertexIndex))
                    continue;

                for (int j = 0; j < c.patchCountB; j++)
                {
                    if (checks >= checkLimit)
                        return;
                    checks++;

                    SutureLinePatchVertexInfo b = patchVertices[c.patchStartB + j];
                    if (!IsValidVertex(b.vertexIndex) || a.vertexIndex == b.vertexIndex)
                        continue;

                    float pairWeight = math.saturate(math.sqrt(GetPatchContactWeight(a) * GetPatchContactWeight(b)));
                    if (pairWeight <= 1e-5f)
                        continue;

                    SolveVertexPairContact(a.vertexIndex, b.vertexIndex, gap, stiffness * pairWeight, maxPush, fallbackNormal, closureAxis);
                }
            }
        }

        private void SolveVertexPairContact(int vertexA, int vertexB, float gap, float stiffness, float maxPush, float3 fallbackNormal, float3 closureAxis)
        {
            float3 delta = predictPositions[vertexB] - predictPositions[vertexA];
            float distance = math.length(delta);
            if (distance >= gap)
                return;

            float invMassA = GetInvMass(vertexA);
            float invMassB = GetInvMass(vertexB);
            float denom = invMassA + invMassB;
            if (denom <= 1e-8f)
                return;

            float3 n = distance > 1e-7f
                ? ProjectAwayFromClosureAxis(delta / distance, closureAxis)
                : fallbackNormal;
            if (math.lengthsq(n) < 1e-8f)
                n = fallbackNormal;
            n = math.normalizesafe(n, float3.zero);
            if (math.lengthsq(n) < 1e-8f)
                return;
            float signedNormalDistance = math.dot(delta, n);
            if (signedNormalDistance < 0f)
                n = -n;
            float normalDistance = math.abs(signedNormalDistance);

            float pushMagnitude = ((gap - normalDistance) * stiffness) / denom;
            if (maxPush > 0f)
                pushMagnitude = math.min(pushMagnitude, maxPush);
            if (pushMagnitude <= 1e-9f)
                return;

            ApplyVertexCorrection(vertexA, -n * pushMagnitude);
            ApplyVertexCorrection(vertexB, n * pushMagnitude);
        }

        private void SolvePatchTriangleContacts(SutureLineConstraintInfo c, float gap, float stiffness, float maxPush)
        {
            float3 closureAxis = GetContactClosureAxis(c, true);
            float3 fallbackNormal = GetContactFallbackNormal(c, closureAxis);
            if (c.contactTriCountA > 0 && c.patchCountB > 0)
                SolveTriangleSetAgainstPatchVertices(c.contactTriStartA, c.contactTriCountA, c.patchStartB, c.patchCountB, gap, stiffness, maxPush, fallbackNormal, closureAxis);
            if (c.contactTriCountB > 0 && c.patchCountA > 0)
                SolveTriangleSetAgainstPatchVertices(c.contactTriStartB, c.contactTriCountB, c.patchStartA, c.patchCountA, gap, stiffness, maxPush, -fallbackNormal, closureAxis);
        }

        private void SolveTriangleSetAgainstPatchVertices(
            int triStart,
            int triCount,
            int patchStart,
            int patchCount,
            float gap,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis)
        {
            if (triStart < 0 || triCount <= 0 || triStart + triCount > contactTriangles.Length ||
                patchStart < 0 || patchCount <= 0 || patchStart + patchCount > patchVertices.Length)
                return;

            int checkLimit = maxTriangleChecksPerLine > 0 ? maxTriangleChecksPerLine : 384;
            int checks = 0;
            for (int ti = 0; ti < triCount; ti++)
            {
                SutureLineContactTriangleInfo tri = contactTriangles[triStart + ti];
                if (!IsValidContactTriangle(tri))
                    continue;

                for (int pi = 0; pi < patchCount; pi++)
                {
                    if (checks >= checkLimit)
                        return;
                    checks++;

                    SutureLinePatchVertexInfo pv = patchVertices[patchStart + pi];
                    if (!IsValidVertex(pv.vertexIndex) ||
                        pv.vertexIndex == tri.v0 ||
                        pv.vertexIndex == tri.v1 ||
                        pv.vertexIndex == tri.v2)
                        continue;

                    float pairWeight = math.saturate(math.sqrt(math.max(0f, tri.weight) * GetPatchContactWeight(pv)));
                    if (pairWeight <= 1e-5f)
                        continue;

                    SolveVertexTriangleContact(pv.vertexIndex, tri, gap, stiffness * pairWeight, maxPush, fallbackNormal, closureAxis);
                }
            }
        }

        private void SolveVertexTriangleContact(
            int vertex,
            SutureLineContactTriangleInfo tri,
            float gap,
            float stiffness,
            float maxPush,
            float3 fallbackNormal,
            float3 closureAxis)
        {
            float3 p = predictPositions[vertex];
            float3 a = predictPositions[tri.v0];
            float3 b = predictPositions[tri.v1];
            float3 c = predictPositions[tri.v2];
            float3 closest = ClosestPointOnTriangle(p, a, b, c, out float3 bary);
            float3 delta = p - closest;
            float distance = math.length(delta);
            if (distance >= gap)
                return;

            float invVertex = GetInvMass(vertex);
            float invTri =
                GetInvMass(tri.v0) * bary.x * bary.x +
                GetInvMass(tri.v1) * bary.y * bary.y +
                GetInvMass(tri.v2) * bary.z * bary.z;
            float denom = invVertex + invTri;
            if (denom <= 1e-8f)
                return;

            float3 n = distance > 1e-7f
                ? delta / distance
                : math.normalizesafe(fallbackNormal, new float3(1f, 0f, 0f));
            n = math.normalizesafe(ProjectAwayFromClosureAxis(n, closureAxis), float3.zero);
            if (math.lengthsq(n) < 1e-8f)
                n = math.normalizesafe(fallbackNormal, float3.zero);
            if (math.lengthsq(n) < 1e-8f)
                return;
            float normalDistance = math.abs(math.dot(delta, n));

            float pushMagnitude = ((gap - normalDistance) * stiffness) / denom;
            if (maxPush > 0f)
                pushMagnitude = math.min(pushMagnitude, maxPush);
            if (pushMagnitude <= 1e-9f)
                return;

            float3 vertexCorrection = n * pushMagnitude;
            float3 triCorrection = -n * pushMagnitude;
            ApplyVertexCorrection(vertex, vertexCorrection);
            ApplyVertexCorrection(tri.v0, triCorrection * bary.x);
            ApplyVertexCorrection(tri.v1, triCorrection * bary.y);
            ApplyVertexCorrection(tri.v2, triCorrection * bary.z);
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

        private bool IsValidContactTriangle(SutureLineContactTriangleInfo tri)
        {
            return IsValidVertex(tri.v0) && IsValidVertex(tri.v1) && IsValidVertex(tri.v2) &&
                   tri.v0 != tri.v1 && tri.v1 != tri.v2 && tri.v2 != tri.v0;
        }

        private float3 ReadConstrainedPoint(NeedlePortalInfo portal, int patchStart, int patchCount, bool patchEnabled)
        {
            return ReadPortalPoint(portal);
        }

        private bool TryReadPatchPoint(int patchStart, int patchCount, out float3 patchPoint)
        {
            patchPoint = float3.zero;
            if (patchCount <= 0 || patchStart < 0 || patchStart + patchCount > patchVertices.Length)
                return false;

            float weightSum = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;

                float w = math.max(0f, pv.weight);
                if (w <= 0f)
                    continue;

                patchPoint += predictPositions[pv.vertexIndex] * w;
                weightSum += w;
            }

            if (weightSum <= 1e-8f)
            {
                patchPoint = float3.zero;
                return false;
            }

            patchPoint /= weightSum;
            return true;
        }

        private float3 ReadPortalPoint(NeedlePortalInfo portal)
        {
            float3 p0 = IsValidVertex(portal.v0) ? predictPositions[portal.v0] : portal.currentPoint;
            float3 p1 = IsValidVertex(portal.v1) ? predictPositions[portal.v1] : portal.currentPoint;
            float3 p2 = IsValidVertex(portal.v2) ? predictPositions[portal.v2] : portal.currentPoint;
            return p0 * portal.bary.x + p1 * portal.bary.y + p2 * portal.bary.z;
        }

        private float ConstrainedPointInvWeight(NeedlePortalInfo portal, int patchStart, int patchCount, bool patchEnabled)
        {
            if (patchEnabled && TryGetPatchInvWeight(patchStart, patchCount, out float invWeight))
                return invWeight;

            return PortalInvWeight(portal);
        }

        private bool TryGetPatchInvWeight(int patchStart, int patchCount, out float invWeight)
        {
            invWeight = 0f;
            if (patchCount <= 0 || patchStart < 0 || patchStart + patchCount > patchVertices.Length)
                return false;

            float weightSum = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;
                weightSum += math.max(0f, pv.weight);
            }

            if (weightSum <= 1e-8f)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;

                float q = math.max(0f, pv.weight) / weightSum;
                if (q <= 0f)
                    continue;

                invWeight += GetInvMass(pv.vertexIndex) * q * q;
            }

            return invWeight > 1e-8f;
        }

        private float PortalInvWeight(NeedlePortalInfo portal)
        {
            float w0 = GetInvMass(portal.v0);
            float w1 = GetInvMass(portal.v1);
            float w2 = GetInvMass(portal.v2);
            return w0 * portal.bary.x * portal.bary.x
                 + w1 * portal.bary.y * portal.bary.y
                 + w2 * portal.bary.z * portal.bary.z;
        }

        private void ApplyConstrainedPointCorrection(NeedlePortalInfo portal, int patchStart, int patchCount, bool patchEnabled, float3 correction)
        {
            if (patchEnabled && TryApplyPatchCorrection(patchStart, patchCount, correction))
                return;

            ApplyPortalCorrection(portal, correction);
        }

        private bool TryApplyPatchCorrection(int patchStart, int patchCount, float3 correction)
        {
            if (patchCount <= 0 || patchStart < 0 || patchStart + patchCount > patchVertices.Length)
                return false;

            float weightSum = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;
                weightSum += math.max(0f, pv.weight);
            }

            if (weightSum <= 1e-8f)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;

                float q = math.max(0f, pv.weight) / weightSum;
                if (q <= 0f)
                    continue;

                ApplyVertexCorrection(pv.vertexIndex, correction * q);
            }
            return true;
        }

        private void ApplyPortalCorrection(NeedlePortalInfo portal, float3 correction)
        {
            ApplyVertexCorrection(portal.v0, correction * portal.bary.x);
            ApplyVertexCorrection(portal.v1, correction * portal.bary.y);
            ApplyVertexCorrection(portal.v2, correction * portal.bary.z);
        }

        private void ApplyVertexCorrection(int vertex, float3 correction)
        {
            if (!IsValidVertex(vertex))
                return;

            float invMass = GetInvMass(vertex);
            if (invMass <= 0f)
                return;

            float3 accumulated = positionCorrects[vertex] + correction * invMass;

            // A vertex can participate in many VT/EE/triangle pairs during one serial
            // contact job. Every pair reads the same predictPositions snapshot, so without
            // a per-vertex bound their full penetration corrections stack before the next
            // solver barrier. The setting is documented as a per-iteration correction
            // limit; enforce it on the actual accumulated world-space displacement.
            float maxVertexCorrection = selfContactMaxPush > 0f
                ? selfContactMaxPush
                : maxCorrection;
            if (maxVertexCorrection > 0f)
            {
                float lengthSq = math.lengthsq(accumulated);
                float maxLengthSq = maxVertexCorrection * maxVertexCorrection;
                if (lengthSq > maxLengthSq)
                    accumulated *= maxVertexCorrection * math.rsqrt(lengthSq);
            }

            positionCorrects[vertex] = accumulated;
        }

        private float GetInvMass(int vertex)
        {
            if (!IsValidVertex(vertex))
                return 0f;
            float mass = masses[vertex];
            return mass > 1e-8f ? 1f / mass : 0f;
        }

        private bool IsValidVertex(int vertex)
        {
            return vertex >= 0 && vertex < predictPositions.Length && vertex < masses.Length;
        }
    }

    [Unity.Burst.BurstCompile]
    public struct SutureLineClosureBandSlideJob : IJob
    {
        [ReadOnly] public NativeArray<SutureLineConstraintInfo> constraints;
        [ReadOnly] public NativeArray<SutureLinePatchVertexInfo> patchVertices;
        [ReadOnly] public NativeArray<CrossLayerLinkInfo> crossLayerLinks;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeArray<float3> predictPositions;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positionCorrects;
        public float maxCorrection;
        public float minTargetDelta;

        public void Execute()
        {
            if (!constraints.IsCreated || constraints.Length == 0 ||
                !patchVertices.IsCreated || patchVertices.Length == 0 ||
                !crossLayerLinks.IsCreated || crossLayerLinks.Length == 0)
                return;

            float maxPush = math.max(0f, maxCorrection);
            float minDelta = math.max(0f, minTargetDelta);
            if (maxPush <= 0f)
                return;

            for (int i = 0; i < constraints.Length; i++)
            {
                SutureLineConstraintInfo c = constraints[i];
                if (c.enabled == 0 || c.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold ||
                    c.closureBandSlideActive == 0 || c.targetDelta <= minDelta ||
                    c.closureBandSlideColumnCount <= 0)
                    continue;

                float3 slideDir = c.closureBandSlideDirValid != 0
                    ? math.normalizesafe(c.closureBandSlideDir, float3.zero)
                    : float3.zero;
                if (math.lengthsq(slideDir) < 1e-8f)
                    continue;

                float amount = math.min(maxPush, math.max(0f, c.lastClosureBandSlidePush));
                if (amount <= 1e-8f)
                    continue;

                ApplySlideColumns(c.patchStartA, c.patchCountA, slideDir * amount, c.closureBandSlideColumnCount);
                ApplySlideColumns(c.patchStartB, c.patchCountB, -slideDir * amount, c.closureBandSlideColumnCount);
            }
        }

        private int ApplySlideColumns(int patchStart, int patchCount, float3 correction, int maxColumns)
        {
            if (maxColumns <= 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > patchVertices.Length)
                return 0;

            float weightSum = 0f;
            int columnCount = 0;
            for (int i = 0; i < crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = crossLayerLinks[i];
                if (!TryGetPatchSlideWeight(patchStart, patchCount, link.a, out float wa) ||
                    !TryGetPatchSlideWeight(patchStart, patchCount, link.b, out float wb))
                    continue;

                weightSum += math.max(0.05f, math.min(wa, wb));
                columnCount++;
                if (columnCount >= maxColumns)
                    break;
            }

            if (columnCount <= 0 || weightSum <= 1e-8f)
                return 0;

            int applied = 0;
            for (int i = 0; i < crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = crossLayerLinks[i];
                if (!TryGetPatchSlideWeight(patchStart, patchCount, link.a, out float wa) ||
                    !TryGetPatchSlideWeight(patchStart, patchCount, link.b, out float wb))
                    continue;

                float q = math.max(0.05f, math.min(wa, wb)) / weightSum;
                ApplyVertexShift(link.a, correction * q);
                ApplyVertexShift(link.b, correction * q);
                applied++;
                if (applied >= maxColumns)
                    break;
            }

            return applied;
        }

        private bool TryGetPatchSlideWeight(int patchStart, int patchCount, int vertexIndex, out float weight)
        {
            weight = 0f;
            if (vertexIndex < 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > patchVertices.Length)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (pv.vertexIndex != vertexIndex)
                    continue;

                weight = GetSlideWeight(pv);
                return weight > 0f;
            }

            return false;
        }

        private static float GetSlideWeight(SutureLinePatchVertexInfo pv)
        {
            byte flags = pv.flags;
            if ((flags & SutureLinePatchVertexInfo.FlagPullAnchor) != 0)
                return 0f;
            if ((flags & SutureLinePatchVertexInfo.FlagClosureBand) == 0 ||
                (flags & SutureLinePatchVertexInfo.FlagFoldColumn) == 0)
                return 0f;

            float middle01 = 1f - math.abs((math.saturate(pv.bandT) - 0.5f) * 2f);
            return math.max(0f, pv.weight) * math.lerp(0.35f, 1f, math.saturate(middle01));
        }

        private void ApplyVertexShift(int vertex, float3 correction)
        {
            if (!IsValidVertex(vertex))
                return;
            float invMass = GetInvMass(vertex);
            if (invMass <= 0f)
                return;

            // correction is already a world-space displacement that was clamped by
            // maxCorrection and normalized across the selected closure-band columns.
            // Multiplying it by inverse mass here changes its units and can amplify a
            // sub-millimetre slide into a macroscopic jump on fine, low-mass meshes.
            positionCorrects[vertex] = positionCorrects[vertex] + correction;
        }

        private float GetInvMass(int vertex)
        {
            if (!IsValidVertex(vertex))
                return 0f;
            float mass = masses[vertex];
            return mass > 1e-8f ? 1f / mass : 0f;
        }

        private bool IsValidVertex(int vertex)
        {
            return vertex >= 0 && vertex < predictPositions.Length && vertex < masses.Length;
        }
    }

    [Unity.Burst.BurstCompile]
    public struct SutureLineCompressionFoldJob : IJob
    {
        [ReadOnly] public NativeArray<SutureLineConstraintInfo> constraints;
        [ReadOnly] public NativeArray<SutureLinePatchVertexInfo> patchVertices;
        [ReadOnly] public NativeArray<CrossLayerLinkInfo> crossLayerLinks;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeArray<float3> predictPositions;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positionCorrects;
        public float stiffness;
        public float maxCorrection;
        public float minTargetDelta;

        public void Execute()
        {
            if (!constraints.IsCreated || constraints.Length == 0 ||
                !patchVertices.IsCreated || patchVertices.Length == 0)
                return;

            float foldStiffness = math.saturate(stiffness);
            float maxPush = math.max(0f, maxCorrection);
            float minDelta = math.max(0f, minTargetDelta);
            if (foldStiffness <= 0f || maxPush <= 0f)
                return;

            for (int i = 0; i < constraints.Length; i++)
            {
                SutureLineConstraintInfo c = constraints[i];
                if (c.enabled == 0 || c.reserved0 == SutureLineConstraintInfo.LifecycleSettledHold ||
                    c.compressionFoldActive == 0 || c.targetDelta <= minDelta)
                    continue;
                if (!TryReadPatchPoint(c.patchStartA, c.patchCountA, out float3 pointA) ||
                    !TryReadPatchPoint(c.patchStartB, c.patchCountB, out float3 pointB))
                    continue;

                float3 lineDir = GetFoldClosureDirection(c, pointA, pointB);
                float3 wallNormal = GetFoldWallNormal(c, lineDir);
                float3 foldDir = c.foldDirValid != 0
                    ? math.normalizesafe(c.foldDir, float3.zero)
                    : float3.zero;
                if (math.lengthsq(foldDir) < 1e-8f)
                    foldDir = math.cross(wallNormal, lineDir);
                if (math.lengthsq(foldDir) < 1e-8f)
                    foldDir = math.cross(new float3(0f, 1f, 0f), lineDir);
                if (math.lengthsq(foldDir) < 1e-8f)
                    foldDir = math.cross(lineDir, new float3(0f, 0f, 1f));
                foldDir = ProjectFoldDirection(foldDir, wallNormal, lineDir);
                if (math.lengthsq(foldDir) < 1e-8f)
                    continue;

                float lineMaxPush = maxPush;
                float amount = c.lastCompressionFoldPush > 0f
                    ? c.lastCompressionFoldPush
                    : math.min(lineMaxPush, c.targetDelta * foldStiffness);
                amount = math.min(amount, lineMaxPush);
                if (amount <= 1e-8f)
                    continue;

                int columnsA = 0;
                int columnsB = 0;
                if (crossLayerLinks.IsCreated && crossLayerLinks.Length > 0 && c.foldColumnCount > 0)
                {
                    columnsA = ApplyColumnShift(c.patchStartA, c.patchCountA, foldDir * amount, c.foldColumnCount);
                    columnsB = ApplyColumnShift(c.patchStartB, c.patchCountB, -foldDir * amount, c.foldColumnCount);
                }

                bool allowPatchFallback = c.contactLatched != 0;
                if (columnsA <= 0 && allowPatchFallback)
                    ApplyPatchShift(c.patchStartA, c.patchCountA, foldDir * amount);
                if (columnsB <= 0 && allowPatchFallback)
                    ApplyPatchShift(c.patchStartB, c.patchCountB, -foldDir * amount);
            }
        }

        private float3 GetFoldClosureDirection(SutureLineConstraintInfo c, float3 pointA, float3 pointB)
        {
            float3 stored = math.normalizesafe(c.closureDirection, float3.zero);
            if (math.lengthsq(stored) > 1e-8f)
                return stored;

            return math.normalizesafe(pointB - pointA, new float3(1f, 0f, 0f));
        }

        private float3 GetFoldWallNormal(SutureLineConstraintInfo c, float3 lineDir)
        {
            float3 hintedNormal = c.contactNormalHint - lineDir * math.dot(c.contactNormalHint, lineDir);
            if (math.lengthsq(hintedNormal) > 1e-8f)
                return math.normalizesafe(hintedNormal, new float3(0f, 1f, 0f));

            float3 nA = math.normalizesafe(c.portalA.entryNormal, float3.zero);
            float3 nB = math.normalizesafe(c.portalB.entryNormal, float3.zero);
            float3 wallNormal = nA + nB;
            if (math.lengthsq(wallNormal) < 1e-8f)
                wallNormal = math.lengthsq(nA) > 1e-8f ? nA : nB;
            wallNormal -= lineDir * math.dot(wallNormal, lineDir);
            return math.normalizesafe(wallNormal, new float3(0f, 1f, 0f));
        }

        private float3 ProjectFoldDirection(float3 foldDir, float3 wallNormal, float3 lineDir)
        {
            foldDir -= wallNormal * math.dot(foldDir, wallNormal);
            foldDir -= lineDir * math.dot(foldDir, lineDir);
            return math.normalizesafe(foldDir, float3.zero);
        }

        private int ApplyColumnShift(int patchStart, int patchCount, float3 correction, int maxColumns)
        {
            if (maxColumns <= 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > patchVertices.Length)
                return 0;

            float weightSum = 0f;
            int columnCount = 0;
            for (int i = 0; i < crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = crossLayerLinks[i];
                if (!TryGetPatchFoldWeight(patchStart, patchCount, link.a, out float wa) ||
                    !TryGetPatchFoldWeight(patchStart, patchCount, link.b, out float wb))
                    continue;

                weightSum += math.max(0.05f, math.min(wa, wb));
                columnCount++;
                if (columnCount >= maxColumns)
                    break;
            }

            if (columnCount <= 0 || weightSum <= 1e-8f)
                return 0;

            int applied = 0;
            for (int i = 0; i < crossLayerLinks.Length; i++)
            {
                CrossLayerLinkInfo link = crossLayerLinks[i];
                if (!TryGetPatchFoldWeight(patchStart, patchCount, link.a, out float wa) ||
                    !TryGetPatchFoldWeight(patchStart, patchCount, link.b, out float wb))
                    continue;

                float q = math.max(0.05f, math.min(wa, wb)) / weightSum;
                ApplyVertexShift(link.a, correction * q);
                ApplyVertexShift(link.b, correction * q);
                applied++;
                if (applied >= maxColumns)
                    break;
            }
            return applied;
        }

        private bool TryReadPatchPoint(int patchStart, int patchCount, out float3 patchPoint)
        {
            patchPoint = float3.zero;
            if (patchStart < 0 || patchCount <= 0 || patchStart + patchCount > patchVertices.Length)
                return false;

            float weightSum = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;
                float weight = GetPullWeight(pv);
                if (weight <= 0f)
                    continue;
                patchPoint += predictPositions[pv.vertexIndex] * weight;
                weightSum += weight;
            }

            if (weightSum <= 1e-8f)
                return false;
            patchPoint /= weightSum;
            return true;
        }

        private void ApplyPatchShift(int patchStart, int patchCount, float3 correction)
        {
            if (patchStart < 0 || patchCount <= 0 || patchStart + patchCount > patchVertices.Length)
                return;

            float weightSum = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;
                weightSum += GetFoldWeight(pv);
            }
            if (weightSum <= 1e-8f)
                return;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (!IsValidVertex(pv.vertexIndex))
                    continue;
                float q = GetFoldWeight(pv) / weightSum;
                if (q <= 0f)
                    continue;
                ApplyVertexShift(pv.vertexIndex, correction * q);
            }
        }

        private bool TryGetPatchFoldWeight(int patchStart, int patchCount, int vertexIndex, out float weight)
        {
            weight = 0f;
            if (vertexIndex < 0 || patchStart < 0 || patchCount <= 0 ||
                patchStart + patchCount > patchVertices.Length)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                SutureLinePatchVertexInfo pv = patchVertices[patchStart + i];
                if (pv.vertexIndex != vertexIndex)
                    continue;
                weight = GetFoldWeight(pv);
                return weight > 0f;
            }
            return false;
        }

        private static float GetPullWeight(SutureLinePatchVertexInfo pv)
        {
            byte flags = pv.flags;
            if (flags == 0)
                flags = SutureLinePatchVertexInfo.FlagPullAnchor;
            float weight = math.max(0f, pv.weight);
            if ((flags & SutureLinePatchVertexInfo.FlagPullAnchor) != 0)
                return weight;
            if ((flags & SutureLinePatchVertexInfo.FlagFoldColumn) != 0)
                return weight * SutureLinePatchVertexInfo.FoldColumnPullWeightScale;
            if ((flags & SutureLinePatchVertexInfo.FlagSeamAnchor) != 0)
                return weight * SutureLinePatchVertexInfo.SeamAnchorPullWeightScale;
            return 0f;
        }

        private static float GetFoldWeight(SutureLinePatchVertexInfo pv)
        {
            byte flags = pv.flags;
            return (flags & SutureLinePatchVertexInfo.FlagFoldColumn) != 0
                ? math.max(0f, pv.weight)
                : 0f;
        }

        private void ApplyVertexShift(int vertex, float3 correction)
        {
            if (!IsValidVertex(vertex))
                return;
            float invMass = GetInvMass(vertex);
            if (invMass <= 0f)
                return;

            // ApplyColumnShift/ApplyPatchShift pass a clamped, normalized world-space
            // displacement. Inverse mass belongs in a constraint-lambda solve; applying
            // it again here makes the advertised maxCorrection cease to be a displacement
            // bound and is capable of ejecting low-mass incision vertices.
            positionCorrects[vertex] = positionCorrects[vertex] + correction;
        }

        private float GetInvMass(int vertex)
        {
            if (!IsValidVertex(vertex))
                return 0f;
            float mass = masses[vertex];
            return mass > 1e-8f ? 1f / mass : 0f;
        }

        private bool IsValidVertex(int vertex)
        {
            return vertex >= 0 && vertex < predictPositions.Length && vertex < masses.Length;
        }
    }

    [Unity.Burst.BurstCompile]
    public struct NeedleTipHookSoftConstraintJob : IJob
    {
        [ReadOnly] public NativeArray<NeedleTipHookSoftConstraintInfo> hooks;
        [ReadOnly] public NativeArray<float> masses;

        public NativeArray<float3> predictPositions;
        public NativeArray<float3> positionCorrects;

        [ReadOnly] public float maxPushPerIteration;

        public void Execute()
        {
            if (!hooks.IsCreated || hooks.Length == 0)
                return;

            float maxPush = math.max(0f, maxPushPerIteration);

            for (int hookIndex = 0; hookIndex < hooks.Length; hookIndex++)
            {
                NeedleTipHookSoftConstraintInfo hook = hooks[hookIndex];
                if (hook.enabled == 0)
                    continue;

                float normalStiffness = math.saturate(hook.normalStiffness);
                float axialStiffness = math.saturate(hook.tangentialStiffness);
                if (normalStiffness <= 0f && axialStiffness <= 0f)
                    continue;

                int patchCount = math.min(hook.patchCount, NeedleTipHookPatchUtil.MaxPatchVertices);
                if (patchCount <= 0)
                    continue;

                float fallbackMaxPush = math.max(0f, hook.radius) * 0.35f;
                float localMaxPush = maxPush > 0f ? maxPush : fallbackMaxPush;
                float3 insertDir = math.normalizesafe(hook.insertDirection, -hook.surfaceNormal);
                float3 normal = math.normalizesafe(hook.surfaceNormal, -insertDir);
                if (math.dot(normal, insertDir) > 0f)
                    normal = -normal;
                float weightSum = 0f;

                for (int i = 0; i < patchCount; i++)
                {
                    int v = NeedleTipHookPatchUtil.GetPatchVertex(hook, i);
                    if ((uint)v >= (uint)predictPositions.Length || (uint)v >= (uint)masses.Length)
                        continue;

                    float mass = masses[v];
                    if (mass <= 1e-8f)
                        continue;

                    float weight = math.saturate(NeedleTipHookPatchUtil.GetPatchWeight(hook, i));
                    if (weight <= 0f)
                        continue;

                    weightSum += weight;
                }

                if (weightSum <= 1e-8f)
                    continue;

                float3 toNeedle = hook.targetPoint - hook.hookPoint;
                float axialAdvance = math.max(0f, math.dot(toNeedle, insertDir));
                float normalAdvance = math.max(0f, -math.dot(toNeedle, normal));
                if (axialAdvance <= 1e-8f && normalAdvance <= 1e-8f)
                    continue;

                // Model the sharp tip as a directional piercer: the local material follows
                // the needle axis into the wall, while normal indentation fills only the
                // inward component not already supplied by axial motion.
                float3 axialDelta = insertDir * (axialAdvance * axialStiffness);
                float axialNormalLen = math.max(0f, -math.dot(axialDelta, normal));
                float extraNormalLen = math.max(0f, normalAdvance * normalStiffness - axialNormalLen);
                float3 desiredDelta = axialDelta + (-normal) * extraNormalLen;
                float desiredLen = math.length(desiredDelta);
                if (desiredLen <= 1e-8f)
                    continue;

                if (localMaxPush > 0f && desiredLen > localMaxPush)
                    desiredDelta *= localMaxPush / desiredLen;

                float denom = 0f;
                for (int i = 0; i < patchCount; i++)
                {
                    int v = NeedleTipHookPatchUtil.GetPatchVertex(hook, i);
                    if ((uint)v >= (uint)predictPositions.Length || (uint)v >= (uint)masses.Length)
                        continue;

                    float mass = masses[v];
                    float inv = mass > 1e-8f ? 1f / mass : 0f;
                    if (inv <= 0f)
                        continue;

                    float weight = math.saturate(NeedleTipHookPatchUtil.GetPatchWeight(hook, i));
                    if (weight <= 0f)
                        continue;

                    denom += weight * weight * inv;
                }

                if (denom <= 1e-8f)
                    continue;

                for (int i = 0; i < patchCount; i++)
                {
                    int v = NeedleTipHookPatchUtil.GetPatchVertex(hook, i);
                    if ((uint)v >= (uint)predictPositions.Length || (uint)v >= (uint)masses.Length)
                        continue;

                    float mass = masses[v];
                    float inv = mass > 1e-8f ? 1f / mass : 0f;
                    if (inv <= 0f)
                        continue;

                    float weight = math.saturate(NeedleTipHookPatchUtil.GetPatchWeight(hook, i));
                    if (weight <= 0f)
                        continue;

                    positionCorrects[v] += desiredDelta * ((weight * inv) / denom);
                }
            }
        }
    }

    [Unity.Burst.BurstCompile]
    public struct NeedlePuncturePortalConstraintJob : IJob
    {
        [ReadOnly] public NativeArray<NeedlePortalInfo> portals;
        [ReadOnly] public CollidersGroup collidersGroup;
        [ReadOnly] public NativeArray<float> masses;

        public NativeArray<float3> predictPositions;
        public NativeArray<float3> positionCorrects;

        [ReadOnly] public float portalStiffness;
        [ReadOnly] public float maxPushPerIteration;

        private bool TryApplyPatchCorrection(NeedlePortalInfo portal, float3 delta)
        {
            int patchCount = math.min(portal.patchCount, NeedlePortalPatchUtil.MaxPatchVertices);
            if (patchCount <= 0)
                return false;

            float denom = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                int v = NeedlePortalPatchUtil.GetPatchVertex(portal, i);
                if ((uint)v >= (uint)masses.Length)
                    continue;
                float weight = math.saturate(NeedlePortalPatchUtil.GetPatchWeight(portal, i));
                if (weight <= 0f)
                    continue;
                float m = masses[v];
                float inv = m > 0f ? 1f / m : 0f;
                denom += weight * weight * inv;
            }

            if (denom <= 1e-8f)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                int v = NeedlePortalPatchUtil.GetPatchVertex(portal, i);
                if ((uint)v >= (uint)masses.Length)
                    continue;
                float weight = math.saturate(NeedlePortalPatchUtil.GetPatchWeight(portal, i));
                if (weight <= 0f)
                    continue;
                float m = masses[v];
                float inv = m > 0f ? 1f / m : 0f;
                if (inv > 0f)
                    positionCorrects[v] += delta * ((weight * inv) / denom);
            }

            return true;
        }

        private bool TryGetCapsule(int entityId, out CapsuleDesc capsule)
        {
            var capsules = collidersGroup.capsules;
            for (int i = 0; i < capsules.Length; i++)
            {
                if (capsules[i].entityId == entityId)
                {
                    capsule = capsules[i].collider;
                    return true;
                }
            }

            capsule = default;
            return false;
        }

        public void Execute()
        {
            if (!portals.IsCreated || portals.Length == 0)
                return;

            float stiffness = math.saturate(portalStiffness);
            if (stiffness <= 0f)
                return;

            float maxPush = math.max(0f, maxPushPerIteration);

            for (int portalIndex = 0; portalIndex < portals.Length; portalIndex++)
            {
                var portal = portals[portalIndex];
                if (portal.enabled == 0)
                    continue;

                CapsuleDesc capsule;
                if (!TryGetCapsule(portal.colliderEntityId, out capsule))
                    continue;

                float3 dir = math.normalizesafe(portal.insertDirection, -portal.entryNormal);
                float3 p0 = predictPositions[portal.v0];
                float3 p1 = predictPositions[portal.v1];
                float3 p2 = predictPositions[portal.v2];
                float3 currentPoint = p0 * portal.bary.x + p1 * portal.bary.y + p2 * portal.bary.z;

                float3 toTip = capsule.tipProbeCenter - currentPoint;
                float axial = math.dot(toTip, dir);
                float3 lateralVec = toTip - dir * axial;
                float lateralSq = math.lengthsq(lateralVec);
                float relaxRadius = math.max(0f, portal.relaxRadius);

                if (relaxRadius > 0f && lateralSq > relaxRadius * relaxRadius * 1.44f)
                    continue;

                float desiredPush = math.clamp(axial + math.max(0f, portal.captureDepth), 0f, math.max(relaxRadius, portal.captureDepth));
                float push = desiredPush * stiffness;

                if (maxPush > 0f)
                    push = math.min(push, maxPush);

                if (push <= 0f)
                    continue;

                float m0 = masses[portal.v0];
                float m1 = masses[portal.v1];
                float m2 = masses[portal.v2];

                float inv0 = m0 > 0f ? 1f / m0 : 0f;
                float inv1 = m1 > 0f ? 1f / m1 : 0f;
                float inv2 = m2 > 0f ? 1f / m2 : 0f;
                float b0 = math.saturate(portal.bary.x);
                float b1 = math.saturate(portal.bary.y);
                float b2 = math.saturate(portal.bary.z);

                float denom = b0 * b0 * inv0 + b1 * b1 * inv1 + b2 * b2 * inv2;
                if (denom <= 1e-8f)
                    continue;

                float3 delta = dir * push;
                if (TryApplyPatchCorrection(portal, delta))
                    continue;

                if (inv0 > 0f) positionCorrects[portal.v0] += delta * ((b0 * inv0) / denom);
                if (inv1 > 0f) positionCorrects[portal.v1] += delta * ((b1 * inv1) / denom);
                if (inv2 > 0f) positionCorrects[portal.v2] += delta * ((b2 * inv2) / denom);
            }
        }
    }


    [Unity.Burst.BurstCompile]
    public struct NeedlePortalHoldConstraintJob : IJob
    {
        [ReadOnly] public NativeArray<NeedlePortalInfo> portals;
        [ReadOnly] public NativeArray<float> masses;

        public NativeArray<float3> predictPositions;
        public NativeArray<float3> positionCorrects;

        [ReadOnly] public float holdStiffness;
        [ReadOnly] public float maxPushPerIteration;

        private bool TryApplyPatchCorrection(NeedlePortalInfo portal, float3 delta)
        {
            int patchCount = math.min(portal.patchCount, NeedlePortalPatchUtil.MaxPatchVertices);
            if (patchCount <= 0)
                return false;

            float denom = 0f;
            for (int i = 0; i < patchCount; i++)
            {
                int v = NeedlePortalPatchUtil.GetPatchVertex(portal, i);
                if ((uint)v >= (uint)masses.Length)
                    continue;
                float weight = math.saturate(NeedlePortalPatchUtil.GetPatchWeight(portal, i));
                if (weight <= 0f)
                    continue;
                float m = masses[v];
                float inv = m > 0f ? 1f / m : 0f;
                denom += weight * weight * inv;
            }

            if (denom <= 1e-8f)
                return false;

            for (int i = 0; i < patchCount; i++)
            {
                int v = NeedlePortalPatchUtil.GetPatchVertex(portal, i);
                if ((uint)v >= (uint)masses.Length)
                    continue;
                float weight = math.saturate(NeedlePortalPatchUtil.GetPatchWeight(portal, i));
                if (weight <= 0f)
                    continue;
                float m = masses[v];
                float inv = m > 0f ? 1f / m : 0f;
                if (inv > 0f)
                    positionCorrects[v] += delta * ((weight * inv) / denom);
            }

            return true;
        }

        public void Execute()
        {
            if (!portals.IsCreated || portals.Length == 0)
                return;

            float stiffness = math.saturate(holdStiffness);
            if (stiffness <= 0f)
                return;

            float maxPush = math.max(0f, maxPushPerIteration);

            for (int portalIndex = 0; portalIndex < portals.Length; portalIndex++)
            {
                var portal = portals[portalIndex];
                if (portal.enabled == 0)
                    continue;

                if ((uint)portal.v0 >= (uint)predictPositions.Length ||
                    (uint)portal.v1 >= (uint)predictPositions.Length ||
                    (uint)portal.v2 >= (uint)predictPositions.Length)
                    continue;

                float3 dir = math.normalizesafe(portal.insertDirection, -portal.entryNormal);
                float3 p0 = predictPositions[portal.v0];
                float3 p1 = predictPositions[portal.v1];
                float3 p2 = predictPositions[portal.v2];
                float3 currentPoint = p0 * portal.bary.x + p1 * portal.bary.y + p2 * portal.bary.z;

                float desiredDepth = math.max(0f, portal.captureDepth);
                if (desiredDepth <= 0f)
                    continue;

                float currentDepth = math.dot(currentPoint - portal.entryPoint, dir);
                float missingDepth = desiredDepth - currentDepth;
                if (missingDepth <= 0f)
                    continue;

                float push = missingDepth * stiffness;
                if (maxPush > 0f)
                    push = math.min(push, maxPush);
                if (push <= 0f)
                    continue;

                float m0 = masses[portal.v0];
                float m1 = masses[portal.v1];
                float m2 = masses[portal.v2];

                float inv0 = m0 > 0f ? 1f / m0 : 0f;
                float inv1 = m1 > 0f ? 1f / m1 : 0f;
                float inv2 = m2 > 0f ? 1f / m2 : 0f;
                float b0 = math.saturate(portal.bary.x);
                float b1 = math.saturate(portal.bary.y);
                float b2 = math.saturate(portal.bary.z);

                float denom = b0 * b0 * inv0 + b1 * b1 * inv1 + b2 * b2 * inv2;
                if (denom <= 1e-8f)
                    continue;

                float3 delta = dir * push;
                if (TryApplyPatchCorrection(portal, delta))
                    continue;

                if (inv0 > 0f) positionCorrects[portal.v0] += delta * ((b0 * inv0) / denom);
                if (inv1 > 0f) positionCorrects[portal.v1] += delta * ((b1 * inv1) / denom);
                if (inv2 > 0f) positionCorrects[portal.v2] += delta * ((b2 * inv2) / denom);
            }
        }
    }


    [Unity.Burst.BurstCompile]
    public struct BendConstaintsGenerateJob : IJob
    {

        [ReadOnly]
        public NativeArray<BendConstraintInfo> bendConstarints;

        [ReadOnly]
        public NativeArray<float3> predictPositions;

        [ReadOnly]
        public NativeArray<float> masses;

        public NativeArray<float3> verticesCorrectResult;

        [ReadOnly]
        public float di;

        [ReadOnly]
        public float bendStiffness;

        public void Execute()
        {
            for (int index = 0; index < bendConstarints.Length; index++)
            {
                var cons = bendConstarints[index];

                var p1 = predictPositions[cons.vIndex0];
                var p2 = predictPositions[cons.vIndex1] - p1;
                var p3 = predictPositions[cons.vIndex2] - p1;
                var p4 = predictPositions[cons.vIndex3] - p1;
                p1 = 0;
                var n1 = math.normalize(math.cross(p2, p3));
                var n2 = math.normalize(math.cross(p2, p4));

                var d = math.dot(n1, n2);

                var p23Len = math.length(math.cross(p2, p3));
                var p24Len = math.length(math.cross(p2, p4));

                var q3 = (math.cross(p2, n2) + math.cross(n1, p2) * d) / p23Len;
                var q4 = (math.cross(p2, n1) + math.cross(n2, p2) * d) / p24Len;
                var q2 = -(math.cross(p3, n2) + math.cross(n1, p3) * d) / p23Len
                - (math.cross(p4, n1) + math.cross(n2, p4) * d) / p24Len;
                var q1 = -q2 - q3 - q4;

                var w1 = 1 / masses[cons.vIndex0];
                var w2 = 1 / masses[cons.vIndex1];
                var w3 = 1 / masses[cons.vIndex2];
                var w4 = 1 / masses[cons.vIndex3];

                var sum = w1 * math.lengthsq(q1)
                + w2 * math.lengthsq(q2)
                + w3 * math.lengthsq(q3)
                + w4 * math.lengthsq(q4);

                sum = math.max(0.01f, sum);

                var s = -(math.acos(d) - cons.rest) * math.sqrt(1 - d * d) / sum;

                if (math.isfinite(s))
                {
                    var dp1 = s * w1 * q1 * di * bendStiffness;
                    var dp2 = s * w2 * q2 * di * bendStiffness;
                    var dp3 = s * w3 * q3 * di * bendStiffness;
                    var dp4 = s * w4 * q4 * di * bendStiffness;
                    verticesCorrectResult[cons.vIndex0] += dp1;
                    verticesCorrectResult[cons.vIndex1] += dp2;
                    verticesCorrectResult[cons.vIndex2] += dp3;
                    verticesCorrectResult[cons.vIndex3] += dp4;
                }

            }
        }
    }

    /// <summary>
    /// 四面体体积约束（non-inversion）：当 V < minVolume 时施加位置修正。
    /// 修正累加到 positionCorrects，最终由 ConstraintsJob 写回 predictPositions。
    /// </summary>
    [Unity.Burst.BurstCompile]


    /// <summary>
    /// 约束合计
    /// </summary>

    public struct ConstraintsJob : IJobParallelFor
    {

        /// <summary>
        /// 总共迭代次数
        /// </summary>
        public int iteratorCount;

        /// <summary>
        /// 当前第几次迭代
        /// </summary>
        public int iterateIndex;


        [ReadOnly]
        public NativeArray<float3> positions;

        // 注意：IJobParallelFor 中 ReadWrite 的 NativeArray 只能读写 job 的 index 位置。
        // 为避免并行读写限制（例如读取 innerIndex），单边不等式硬约束（外层不进内层）
        // 不在本 Job 中执行，而是由单独的 IJob（串行）在每次迭代末尾执行。
        public NativeArray<float3> predictPositions;


        [ReadOnly]
        public NativeArray<float> masses;

        [ReadOnly]
        public NativeArray<PinConstraintInfo> pinConstriants;


        [ReadOnly]
        public NativeArray<CollisionConstraintInfo> collisionConstraints;

        [ReadOnly]
        public NativeArray<NeedleTipIndentationInfo> needleTipIndentationInfos;

        [ReadOnly]
        public NativeArray<NeedlePortalInfo> portals;

        [ReadOnly]
        public NativeArray<NeedleTipHookSoftConstraintInfo> tipPiercerHooks;

        [ReadOnly]
        public NativeArray<ConstraintType> activeConstraintTypes;

        public NativeArray<float3> positionCorrect;

        public float di;

        // 碰撞壳层厚度（skin）：约束为 dist >= skin
        public float collisionSkin;

        // 碰撞投影刚度：1=满额硬推；越小越软。
        public float collisionStiffness;

        // 每轮碰撞求解允许的最大推开量。0 表示不限制。
        public float collisionMaxPushPerIteration;

        // 针尖影响区的软凹陷参数（Phase 1）。
        public byte enableNeedleTipIndentation;
        public float needleTipInfluenceRadius;
        public float needleTipIndentationStiffness;
        public float needleTipMaxPushPerIteration;
        public float needleTipFalloffPower;

        public byte enableNeedlePuncture;
        public float needlePunctureCollisionRelaxScale;
        public float needleTipPiercerCollisionRelaxScale;

        public ConstraintsJob(NativeArray<float3> positions,
        NativeArray<float3> predictPositions, float di)
        {
            this.positions = positions;
            this.predictPositions = predictPositions;
            this.di = di;
            this.pinConstriants = default;
            this.collisionConstraints = default;
            this.needleTipIndentationInfos = default;
            this.activeConstraintTypes = default;
            this.iteratorCount = 1;
            this.iterateIndex = 0;
            this.masses = default;
            this.positionCorrect = default;
            this.collisionSkin = 0.001f;
            this.collisionStiffness = 1f;
            this.collisionMaxPushPerIteration = 0f;
            this.enableNeedleTipIndentation = 0;
            this.needleTipInfluenceRadius = 0f;
            this.needleTipIndentationStiffness = 0f;
            this.needleTipMaxPushPerIteration = 0f;
            this.needleTipFalloffPower = 2f;
            this.portals = default;
            this.tipPiercerHooks = default;
            this.enableNeedlePuncture = 0;
            this.needlePunctureCollisionRelaxScale = 1f;
            this.needleTipPiercerCollisionRelaxScale = 1f;
        }

        private float GetPortalCollisionRelaxScale(float3 point)
        {
            if (enableNeedlePuncture == 0 || !portals.IsCreated || portals.Length == 0)
                return 1f;

            float scale = 1f;
            for (int i = 0; i < portals.Length; i++)
            {
                var portal = portals[i];
                if (portal.enabled == 0) continue;

                float rr = math.max(0f, portal.relaxRadius);
                if (rr <= 0f) continue;

                if (math.lengthsq(point - portal.currentPoint) <= rr * rr)
                    scale = math.min(scale, math.saturate(needlePunctureCollisionRelaxScale));
            }

            return scale;
        }

        private float GetTipPiercerCollisionRelaxScale(int vertexIndex, float3 point)
        {
            if (!tipPiercerHooks.IsCreated || tipPiercerHooks.Length == 0)
                return 1f;

            float scale = 1f;
            float piercerScale = math.saturate(needleTipPiercerCollisionRelaxScale);

            for (int hookIndex = 0; hookIndex < tipPiercerHooks.Length; hookIndex++)
            {
                NeedleTipHookSoftConstraintInfo hook = tipPiercerHooks[hookIndex];
                if (hook.enabled == 0)
                    continue;

                int patchCount = math.min(hook.patchCount, NeedleTipHookPatchUtil.MaxPatchVertices);
                for (int i = 0; i < patchCount; i++)
                {
                    if (NeedleTipHookPatchUtil.GetPatchVertex(hook, i) == vertexIndex)
                    {
                        scale = math.min(scale, piercerScale);
                        break;
                    }
                }

                float rr = math.max(0f, hook.radius);
                if (rr > 0f && math.lengthsq(point - hook.hookPoint) <= rr * rr)
                    scale = math.min(scale, piercerScale);
            }

            return scale;
        }

        public void Execute(int index)
        {
            var constraintType = activeConstraintTypes[index];

            // 其它约束（Distance/Bend 等）预先累计到 positionCorrect[index]
            var positionCorrect = this.positionCorrect[index];
            this.positionCorrect[index] = 0;

            // HardPin：直接写死；SoftClampPin 会作为有限回拉参与后续约束。
            bool hasPin = (constraintType & ConstraintType.Pin) == ConstraintType.Pin;
            PinConstraintInfo pin = default;
            bool softClampPin = false;
            if (hasPin && pinConstriants.IsCreated && index < pinConstriants.Length)
            {
                pin = pinConstriants[index];
                softClampPin = pin.mode == 1;
            }
            if ((constraintType & ConstraintType.Pin) == ConstraintType.Pin)
            {
                if (!softClampPin)
                {
                    this.predictPositions[index] = this.pinConstriants[index].position;
                    return;
                }
            }

            // 1) 先叠加内部约束修正（Distance/Bend/Thickness...）
            this.predictPositions[index] += positionCorrect;

            float3 p = this.predictPositions[index];

            if (softClampPin)
            {
                float3 toTarget = pin.position - p;
                float correctionLen = math.length(toTarget) * math.saturate(pin.stiffness);
                float maxPinCorrection = math.max(0f, pin.maxCorrection);
                if (maxPinCorrection > 0f)
                    correctionLen = math.min(correctionLen, maxPinCorrection);
                if (correctionLen > 1e-8f)
                    p += math.normalizesafe(toTarget) * correctionLen;
            }

            // 2) 先做“针尖影响区”的软凹陷：
            //    不是要求粒子已经撞进碰撞体表面，而是让针尖附近一小片区域提前被压入。
            if (enableNeedleTipIndentation != 0 && needleTipIndentationInfos.IsCreated)
            {
                var indent = needleTipIndentationInfos[index];
                if (indent.enabled != 0 && indent.weight > 0f)
                {
                    float falloff = math.pow(math.saturate(indent.weight), math.max(0.5f, needleTipFalloffPower));
                    float iterIndentScale = 0.35f + 0.65f * math.saturate(di);

                    float push = math.max(0f, needleTipInfluenceRadius)
                               * falloff
                               * math.saturate(needleTipIndentationStiffness)
                               * iterIndentScale;

                    // 首触最小推量：避免逻辑上触发了，但视觉上完全看不见
                    float onsetMinPush = math.min(
                        math.max(0.0012f, needleTipInfluenceRadius * 0.16f),
                        0.0030f);

                    push = math.max(push, onsetMinPush * math.saturate(indent.weight));

                    float maxPush = needleTipMaxPushPerIteration;
                    if (maxPush > 0f)
                    {
                        push = math.min(push, math.max(maxPush, onsetMinPush));
                    }

                    p += indent.normal * push;
                }
            }

            // 3) 再做碰撞单边投影（标准 projection）：dist >= skin
            if ((constraintType & ConstraintType.Collision) == ConstraintType.Collision)
            {
                var c = collisionConstraints[index];

                float dist = math.dot(p - c.surfacePoint, c.normal); // signed distance
                float skin = collisionSkin;
                if (dist < skin)
                {
                    float penetration = skin - dist;

                    // 碰撞不要像旧版那样满额硬推，但也不能被 di 压得几乎“没有反应”。
                    // 这里保留每轮缩放的思路，同时提高早期迭代的接触存在感，
                    // 让“先凹陷”主要来自 bend / thickness，而不是靠把碰撞削到看不见。
                    float iterContactScale = 0.5f + 0.5f * math.saturate(di);
                    float portalRelaxScale = GetPortalCollisionRelaxScale(p);
                    float tipPiercerRelaxScale = GetTipPiercerCollisionRelaxScale(index, p);
                    float push = penetration * math.saturate(collisionStiffness * iterContactScale) * math.min(portalRelaxScale, tipPiercerRelaxScale);

                    float maxPush = collisionMaxPushPerIteration;
                    if (maxPush > 0f)
                    {
                        push = math.min(push, maxPush);
                    }

                    if (push > 0f)
                        p += c.normal * push;
                }
            }

            this.predictPositions[index] = p;

            // 单边硬约束（外层不进内层）由 LayerSeparationJob 在每次迭代末尾执行。
        }

    }

    /// <summary>
    public struct LayerSeparationJob : IJob
    {
        public NativeArray<float3> predictPositions;

        [ReadOnly] public NativeArray<float> masses; // 注意：这是“质量 m”，与你的 DistanceConstraintJob 一致
        [ReadOnly] public NativeArray<ConstraintType> constraintTypes;
        [ReadOnly] public NativeArray<PinConstraintInfo> pinConstraints;
        [ReadOnly] public NativeArray<LayerSeparationInfo> infos;
        [ReadOnly] public NativeArray<NeedlePortalInfo> portals;

        public float punctureMinSepScale;
        public float eps; // 建议 1e-8f

        public void Execute()
        {
            int n = infos.Length;
            int total = predictPositions.Length;

            for (int o = 0; o < n; o++)
            {
                var info = infos[o];
                if (info.enabled == 0) continue;

                int i = info.innerIndex;
                if ((uint)o >= (uint)total || (uint)i >= (uint)total) continue;

                float3 dir = info.dirOut;
                float dirLenSq = math.lengthsq(dir);
                if (dirLenSq < eps) continue;
                dir *= math.rsqrt(dirLenSq);

                float3 pO = predictPositions[o];
                float3 pI = predictPositions[i];

                float localMinSep = info.minSep;
                if (portals.IsCreated && portals.Length > 0)
                {
                    float sepScale = math.saturate(punctureMinSepScale);
                    if (sepScale < 1f)
                    {
                        for (int portalIndex = 0; portalIndex < portals.Length; portalIndex++)
                        {
                            var portal = portals[portalIndex];
                            if (portal.enabled == 0) continue;

                            float rr = math.max(0f, portal.relaxRadius);
                            if (rr <= 0f) continue;

                            if (math.lengthsq(pO - portal.currentPoint) <= rr * rr)
                            {
                                localMinSep *= sepScale;
                                break;
                            }
                        }
                    }
                }

                // s = dot(pOuter - pInner, dirOut)
                float s = math.dot(pO - pI, dir);
                float C = localMinSep - s; // C>0 => violation (inner outside / thickness inverted)
                if (C <= 0f) continue;

                bool pinO = IsHardPinned(o);
                bool pinI = IsHardPinned(i);

                // 两边都 Pin：无法修正
                if (pinO && pinI) continue;

                // 一边 Pin：把全部修正给另一边（硬保证最稳）
                if (pinO && !pinI)
                {
                    // inner 往里拉
                    pI -= dir * C;
                    predictPositions[i] = pI;
                    continue;
                }
                if (pinI && !pinO)
                {
                    // outer 往外推
                    pO += dir * C;
                    predictPositions[o] = pO;
                    continue;
                }

                // 两边都可动：按“质量 m”分配（沿用你 DistanceConstraintJob 的风格）
                float mO = masses[o];
                float mI = masses[i];
                float totalM = mO + mI;

                if (totalM < eps)
                {
                    // 极端退化：均分
                    float half = 0.5f * C;
                    pO += dir * half;
                    pI -= dir * half;
                }
                else
                {
                    // outer 修正比例 = mI / (mO + mI)
                    // inner 修正比例 = mO / (mO + mI)
                    float aO = mI / totalM;
                    float aI = mO / totalM;

                    pO += dir * (C * aO);
                    pI -= dir * (C * aI);
                }

                predictPositions[o] = pO;
                predictPositions[i] = pI;
            }
        }

        private bool IsHardPinned(int index)
        {
            if ((constraintTypes[index] & ConstraintType.Pin) != ConstraintType.Pin)
                return false;
            if (!pinConstraints.IsCreated || index >= pinConstraints.Length)
                return true;
            return pinConstraints[index].mode == 0;
        }
    }

    [Unity.Burst.BurstCompile]
    public struct UpdateVelocitiesAndPositionsJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<ConstraintType> constraintTypes;

        [ReadOnly]
        public NativeArray<float3> predictPositions;

        [ReadOnly]
        public NativeArray<CollisionConstraintInfo> collisionConstraintInfos;

        public NativeArray<float3> positions;

        [WriteOnly]
        public NativeArray<float3> velocities;

        public float collisionNormalDamping;
        public float dt;

        // -------- sleep / anti-jitter --------
        public float sleepPositionThreshold;
        public float sleepVelocityThreshold;

        public void Execute(int index)
        {
            float3 prevPos = positions[index];
            float3 newPos = predictPositions[index];
            float3 delta = newPos - prevPos;

            float3 v = delta / dt;

            if ((constraintTypes[index] & ConstraintType.Collision) == ConstraintType.Collision)
            {
                var c = collisionConstraintInfos[index];
                float3 n = math.normalizesafe(c.normal);
                float nLenSq = math.lengthsq(n);
                if (nLenSq > 1e-8f)
                {
                    float targetNormalSpeed = math.dot(c.velocity, n);
                    float relNormalSpeed = math.dot(v, n) - targetNormalSpeed;

                    if (relNormalSpeed < 0f)
                    {
                        // 仍在继续往碰撞体里钻：直接消掉这部分法向速度
                        v -= n * relNormalSpeed;
                    }
                    else if (relNormalSpeed > 0f)
                    {
                        // 正在离面：对法向反弹做阻尼，减少静止查询体附近的来回跳动
                        v -= n * (relNormalSpeed * math.saturate(collisionNormalDamping));
                    }
                }
            }

            float posThr = math.max(0f, sleepPositionThreshold);
            float velThr = math.max(0f, sleepVelocityThreshold);
            float posThrSq = posThr * posThr;
            float velThrSq = velThr * velThr;

            // 通用静止阈值：
            // 位置变化和速度都已经很小，就直接钉成静止，彻底消掉“微小来回抖”
            if (math.lengthsq(delta) <= posThrSq && math.lengthsq(v) <= velThrSq)
            {
                newPos = prevPos;
                v = float3.zero;
            }

            velocities[index] = v;
            positions[index] = newPos;
        }
    }


    [Unity.Burst.BurstCompile]
    public struct NormalCalulateJob : IJob
    {
        [ReadOnly]
        public NativeArray<int> indices;
        [ReadOnly]
        public NativeArray<float3> positions;

        public NativeArray<float3> normals;

        public void Execute()
        {
            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = 0;
            }

            for (var i = 0; i < indices.Length / 3; i++)
            {
                var offset = i * 3;
                var vIndex0 = indices[offset];
                var vIndex1 = indices[offset + 1];
                var vIndex2 = indices[offset + 2];
                var p0 = positions[vIndex0];
                var p1 = positions[vIndex1];
                var p2 = positions[vIndex2];
                var n = math.normalize(math.cross(p1 - p0, p2 - p0));
                normals[vIndex0] += n;
                normals[vIndex1] += n;
                normals[vIndex2] += n;
            }

            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = math.normalizesafe(normals[i]);
            }
        }
    }
}
