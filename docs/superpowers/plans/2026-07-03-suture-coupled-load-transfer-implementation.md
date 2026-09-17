# Suture Coupled Load Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有 Oriented PullLine/Barrier 路径内实现持久张力、单帧缩短预算、统一斜向引导和 WholeWall 柱接触修正，使侧壁保持接触而后方组织继续折叠滑移。

**Architecture:** 保留现有 PullLine、persistent manifold、closure-band patch 和 cross-layer link 容器。新增的纯策略函数负责 lambda、pending 和引导比例；主线程只提交一次本帧 rest 缩短，Barrier 只测量帧首/帧末 gap，单独的 post-solve job 只结算一次 pending。Oriented Barrier 的接触 feature 仍由真实三角形产生，但 XPBD denominator 与位置修正改用 Outer/Inner WholeWall 柱自由度。

**Tech Stack:** Unity C#、Unity Jobs/Burst、Unity.Mathematics、NativeArray/NativeList、.NET 9 反射与源码契约回归。

---

## File map

- `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`：纯策略、PullLine XPBD、Barrier 柱求解、post-solve pending 结算、fold/slide 的 Oriented budget 读取。
- `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`：状态初始化、每帧 rest 请求提交、guide lock、job scheduling、Settled gate、运行日志。
- `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`：策略数值回归与关键调用链源码契约。

## Task 1: Add policy and state contracts

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`

- [ ] **Step 1: Write the failing policy/state regression**

在 `constraintType` 反射检查后加入字段断言：

```csharp
foreach (string fieldName in new[]
{
    "frameRestShorteningRequest",
    "frameStartBarrierGap",
    "frameFinalBarrierGap",
    "frameAdmittedNormalClosure",
    "pendingShortening",
    "foldAbsorbed",
    "slideAbsorbed",
    "pendingSettlementFrame",
    "guideState",
    "guideSign",
    "guideRatio",
})
{
    if (constraintType.GetField(fieldName) == null)
        throw new InvalidOperationException($"Coupled PullLine state lacks {fieldName}.");
}
```

反射调用下列公开纯策略并断言数值：

```csharp
AccumulateTensionLambda(2f, -0.5f) == 1.5f
AccumulateTensionLambda(0.2f, -1f) == 0f
ComputeFrameRestShorteningRequest(0.030f, 0.024f) == 0.006f
ComputeFrameAdmittedNormalClosure(0.006f, 0.004f, 0.001f, true) == 0.003f
SettlePendingShortening(0.010f, 0.006f, 0.003f, 0.004f, out newExcess, out absorbed) == 0.009f
```

- [ ] **Step 2: Run RED regression**

Run:

```powershell
dotnet build Assembly-CSharp.csproj -nologo
dotnet Tools/Tests/CircumferentialLipIdentityRegression/bin/Debug/net9.0/CircumferentialLipIdentityRegression.dll .
```

Expected: regression fails because the coupled state fields/policy methods do not exist.

- [ ] **Step 3: Add the minimal state and pure policy**

向 `SutureLineConstraintInfo` 添加上述字段。向 `CircumferentialOrientedContactPolicy` 添加：

```csharp
public const byte GuideUnresolved = 0;
public const byte GuideProvisional = 1;
public const byte GuideLocked = 2;

public static float AccumulateTensionLambda(float previousLambda, float deltaLambda)
{
    float previous = math.isfinite(previousLambda) ? math.max(0f, previousLambda) : 0f;
    float delta = math.isfinite(deltaLambda) ? deltaLambda : 0f;
    return math.max(0f, previous + delta);
}

public static float ComputeFrameRestShorteningRequest(float previousRest, float currentRest)
{
    if (!math.isfinite(previousRest) || !math.isfinite(currentRest)) return 0f;
    return math.max(0f, previousRest - currentRest);
}

public static float ComputeFrameAdmittedNormalClosure(
    float restRequest, float frameStartGap, float frameFinalGap, bool barrierActive)
{
    float request = math.isfinite(restRequest) ? math.max(0f, restRequest) : 0f;
    if (request <= 0f || !barrierActive) return request;
    if (!math.isfinite(frameStartGap) || !math.isfinite(frameFinalGap)) return 0f;
    return math.clamp(frameStartGap - frameFinalGap, 0f, request);
}

public static float SettlePendingShortening(
    float pending, float restRequest, float admittedClosure, float retainedAbsorption,
    out float newExcess, out float actualAbsorption)
{
    float oldPending = math.isfinite(pending) ? math.max(0f, pending) : 0f;
    float request = math.isfinite(restRequest) ? math.max(0f, restRequest) : 0f;
    float admitted = math.isfinite(admittedClosure) ? math.clamp(admittedClosure, 0f, request) : 0f;
    newExcess = math.max(0f, request - admitted);
    float available = oldPending + newExcess;
    float retained = math.isfinite(retainedAbsorption) ? math.max(0f, retainedAbsorption) : 0f;
    actualAbsorption = math.min(available, retained);
    return math.max(0f, available - actualAbsorption);
}
```

- [ ] **Step 4: Run GREEN verification**

Expected: Assembly-CSharp builds and the regression prints its PASS line.

- [ ] **Step 5: Checkpoint**

Stage only `ClothJobs.cs` and `Program.cs`; commit `phase56: add coupled suture state policy`.

## Task 2: Make Oriented PullLine lambda persistent

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`

- [ ] **Step 1: Write failing lifecycle/source contracts**

提取 `SutureLineConstraintJob` 和 `UpdateSutureLineConstraintProgressMainThread` 的源码片段，要求：

```csharp
lineJobBody.Contains("AccumulateTensionLambda")
lineJobBody.Contains("alpha")
lineJobBody.Contains("maxCorrection")
progressBody.Contains("bool persistentOrientedLambda")
progressBody.Contains("if (!persistentOrientedLambda)")
```

并要求 `SutureLineConstraintInfo.lambdaWarmStartFrame`、job `frameId` 和每帧一次的 `ApplyPersistentLambdaWarmStart`。拒绝 Oriented 分支中的 latch、rest、frame、Settled reset。`TryFinalizeAutoSuturePullLine` 和 `TryAdvanceSutureLineSettledLifecycle` 的 Oriented 路径也必须保留 lambda。

- [ ] **Step 2: Run RED regression**

Expected: fails because the job still executes `c.lambda = 0f` and the main thread still resets/decays every line.

- [ ] **Step 3: Implement tension-only XPBD update**

在 `SutureLineConstraintJob.Solve` 中，仅对 `orientedBarrierEnabled != 0 && barrierContractValid != 0` 使用：

```csharp
float alpha = useXPBD != 0 ? math.max(0f, compliance) / math.max(dt * dt, 1e-8f) : 0f;
float deltaTension = (C * math.saturate(stiffness) - alpha * c.lambda) / (denom + alpha);
float nextLambda = CircumferentialOrientedContactPolicy.AccumulateTensionLambda(c.lambda, deltaTension);
deltaTension = nextLambda - c.lambda;
```

每帧第一次 solve 先按 `maxCorrection` 限制并施加上一帧保留的 `c.lambda`，写入 `lambdaWarmStartFrame = frameId`；该次只做 warm start，同帧后续 iteration 再按上述公式限制并施加 `deltaTension`。当 `C <= 0` 时仍允许 complementarity 把 lambda 降至零；不能提前 return 并留下假张力。

- [ ] **Step 4: Gate legacy reset/decay**

在主线程定义：

```csharp
bool persistentOrientedLambda = c.orientedBarrierEnabled != 0 && c.barrierContractValid != 0;
```

只有 `!persistentOrientedLambda` 时才执行 latch reset、`resetSutureLineLambdaEachFrame`、`lambdaResetRestDelta` 和 `lambdaDecay`。Settled/auto-finalize 对 Oriented 路径只设置 `lambdaResetLastFrame = 0`，不清 lambda。

- [ ] **Step 5: Run GREEN verification and checkpoint**

Build, run regression, stage the three named files, commit `phase57: persist oriented suture tension lambda`.

## Task 3: Settle pending shortening exactly once per frame

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`

- [ ] **Step 1: Write failing single-writer contracts**

要求源码包含 `SutureLinePostSolveSettlementJob`、`frameRestShorteningRequest`、`captureFrameStartGap`、`captureFrameFinalGap` 和 `pendingSettlementFrame`。要求 `SutureOrientedBarrierJob` 不写 `pendingShortening`；要求 post-solve job 只调用一次 `SettlePendingShortening`。

- [ ] **Step 2: Run RED regression**

Expected: fails because there is no once-per-frame settlement job.

- [ ] **Step 3: Submit the rest request once**

在 `UpdateSutureLineConstraintProgressMainThread` 每条线完成 `currentRestDistance` 推进后执行：

```csharp
c.frameRestShorteningRequest =
    CircumferentialOrientedContactPolicy.ComputeFrameRestShorteningRequest(previousRest, c.currentRestDistance);
```

Settled、invalid 和未推进路径写零。不得使用 `targetDelta` 或孔位距离生成该字段。

- [ ] **Step 4: Capture Barrier frame endpoints**

给 Barrier job 添加 `captureFrameStartGap`、`captureFrameFinalGap`。第一次 iteration 的第一次 Barrier 以 `previousGap` 更新 `frameStartBarrierGap`；最后 iteration 的 final Barrier 以 `constraintGap` 更新 `frameFinalBarrierGap`。中间 pass 只更新瞬时聚合量。

- [ ] **Step 5: Add one post-solve settlement job**

新增 `SutureLinePostSolveSettlementJob`，每帧 solver loop 结束后 schedule 一次。它检查 `pendingSettlementFrame != frameId`，计算 admitted closure、测量 FoldColumn WholeWall 柱在锁定 guide 上最终保留的相对位移，调用 `SettlePendingShortening`，按本帧 fold/slide drive 比例增加 `foldAbsorbed`/`slideAbsorbed`，最后写 frame stamp。

- [ ] **Step 6: Run GREEN verification and checkpoint**

Build, run regression, stage the three named files, commit `phase58: settle suture shortening once per frame`.

## Task 4: Lock one guide and use pending for coupled load transfer

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`

- [ ] **Step 1: Write failing guide/source contracts**

要求 `UpdateSutureLineGuideState` 具有 `Unresolved -> Provisional -> Locked` 路径；真实 closure-band overlap 可覆盖 Provisional；Locked 分支直接保留原方向。要求 PullLine、ClosureBandSlide 和 CompressionFold 都读取 `closureBandSlideDir`，且 Oriented fold/slide 的预算读取 `pendingShortening` 而不是 `targetDelta`/`compressionDemand`。

- [ ] **Step 2: Run RED regression**

Expected: fails because fold and slide still own separate direction and instantaneous demand.

- [ ] **Step 3: Implement deterministic provisional and geometry lock**

统一 guide axis 使用 wall tangent。真实 overlap 超过局部尺度 epsilon 时锁定几何符号；完全对称时以稳定 `(lineIndex, passIndexA, passIndexB)` parity 给出弱 Provisional。Locked 在 manifold release 前不翻转。将完整有符号方向存入 `closureBandSlideDir`，同步 `foldDir`。

- [ ] **Step 4: Add bounded oblique PullAnchor direction**

Oriented PullLine 在 Barrier 承压且 guide 非 Unresolved 时使用：

```csharp
float3 pullDirectionA = math.normalizesafe(n + guideDirection * guideRatio, n);
float3 pullDirectionB = -pullDirectionA;
```

`guideRatio` 最大为 `tan(25°)`；Provisional 只用 25% 强度，Locked 按 pressure 与 pending 平滑达到上限。Seam/contact vertex 不进入 PullLine writer。

- [ ] **Step 5: Drive FoldColumn slide/fold from pending**

Oriented Slide/Fold 只处理 WholeWall FoldColumn，使用同一 signed guide。每 iteration amount 受 `pendingShortening`、pressure 和原有 max correction 限制；Oriented 路径禁止 patch fallback。非 Oriented 路径保持旧行为。

- [ ] **Step 6: Run GREEN verification and checkpoint**

Build, run regression, stage the three named files, commit `phase59: couple oriented guide load transfer`.

## Task 5: Solve Oriented Barrier on WholeWall columns

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`

- [ ] **Step 1: Write failing column-barrier contracts**

反射检查 `SutureLineContactTriangleInfo` 包含 `columnPartner0/1/2`。源码检查普通 mesh triangle 把 partner 初始化为 `-1`，虚拟侧壁 triangle 从 `outerA ↔ innerA`、`outerB ↔ innerB` 填充 partner。要求 `SutureOrientedBarrierJob` 不再接收 `crossLayerLinks`，且 `TryBuildWholeWallColumns` 按 triangle slot 读取明确 partner；拒绝旧 `ApplyWeightedCorrection(feature vertex...)` 成为 Oriented 成功路径。

- [ ] **Step 2: Run RED regression**

Expected: fails because contact triangle 尚未保存 column partner，Barrier 仍把一对多 `crossLayerLinks` 当成唯一柱身份表。

- [ ] **Step 3: Build fixed-size feature columns**

为虚拟侧壁 triangle 的每个顶点槽位保存明确 partner。对 feature 的六个 `(triangle side, vertex, signedWeight)` 逐个按所属 triangle slot 查 partner，按排序后的 `(min(vertex, partner), max(vertex, partner))` 合并 signed gradient。缺失、越界或自配对 partner 返回 `FailureInvalidColumn`；单层 fallback 不存在。

PullLine constraint 同时保存每侧的 Outer portal 与 Inner portal。Oriented 路径的 constrained point、inverse weight 和 correction 直接使用对应 portal 对；禁止通过 `TryFindLinkedPatchVertex` 从厚度约束图选择孔柱。legacy 非 Oriented patch 路径可以继续使用现有图网络。

- [ ] **Step 4: Replace denominator and correction**

每柱：

```csharp
combinedMass = masses[outer] + masses[inner];
columnInvMass = outerMovable && innerMovable ? 1f / combinedMass : 0f;
weightSum += signedWeight * signedWeight * columnInvMass;
deltaXColumn = normal * (deltaLambda * signedWeight * columnInvMass);
predictPositions[outer] += deltaXColumn;
predictPositions[inner] += deltaXColumn;
```

第一次与 final Barrier 共用该核；final pass 不允许单层 feature correction。

每个 manifold 添加 `lambdaWarmStartFrame`，Barrier job 接收当前 `frameId`。每帧第一次激活该 manifold 时只施加一次持久 `normalLambda` warm start，同帧后续 Barrier pass 只施加 `deltaLambda`。

- [ ] **Step 5: Run GREEN verification and checkpoint**

Build, run regression, stage the three named files, commit `phase60: solve oriented barrier by whole wall columns`.

## Task 6: Tighten completion gate and diagnostics

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothDebugGizmos.cs`

- [ ] **Step 1: Write failing acceptance contracts**

要求 Oriented Settled 同时检查合法 Barrier gap、`lambda > 0`、`pendingShortening <= residualTolerance`、`guideState == GuideLocked` 与有效 WholeWall contract。要求 compact log/display 包含 `pending=`、`admit=`、`guide=`、`absorbed=`。

- [ ] **Step 2: Run RED regression**

Expected: fails because current Settled only requires rest target/workflow state.

- [ ] **Step 3: Update lifecycle and display mapping**

在 `TryAdvanceSutureLineSettledLifecycle` 中加入上述 gate；`actualDistance == targetRest` 不再是 Oriented 必要条件。Settled 继续运行 PullLine 与 Barrier，只停止新增 rest、fold 和 slide drive。把 frame request/admitted/pending/absorbed/guide 写入 display info 与 compact log。

- [ ] **Step 4: Run full verification**

Run:

```powershell
dotnet build Assembly-CSharp.csproj -nologo
dotnet build Assembly-CSharp-Editor.csproj -nologo
dotnet Tools/Tests/CircumferentialLipIdentityRegression/bin/Debug/net9.0/CircumferentialLipIdentityRegression.dll .
git diff --check
```

Expected: both builds have 0 errors, regression prints PASS, and diff check reports no whitespace errors in modified source files.

- [ ] **Step 5: Final checkpoint**

Stage only `Program.cs`, `ClothSimulator.cs`, and `ClothDebugGizmos.cs`; commit `phase61: gate suture settlement on absorbed shortening` and print `git log --oneline -5`.
