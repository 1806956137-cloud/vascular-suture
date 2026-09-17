# Circumferential Oriented Contact Barrier Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent circumferential cut-side interpenetration while PullLine continues tightening and transfers blocked normal closure into retained tension, tangential slip, and whole-wall folding.

**Architecture:** Preserve the completed `40/80/80/80` VirtualSidewall registration chain and add a circumferential-only oriented barrier path. VirtualSidewall identity supplies SideA/SideB and station ownership; a persistent manifold keeps the legal direction and XPBD lambda; PullLine and the barrier share compression demand instead of applying equal-and-opposite corrections to the same degree of freedom. Existing greenfield and non-circumferential contact paths remain unchanged.

**Tech Stack:** Unity C#, Unity.Mathematics, Unity.Collections/Jobs, reflection-based .NET regression executable, Git checkpoints.

---

## Execution budget and handoff protocol

This work must be executed in the numbered batches below. Do not begin a batch unless there is enough session capacity to complete its RED test, minimal implementation, both Unity builds, regression run, explicit staging, commit, and post-commit log.

At the end of every batch:

1. print `git diff --stat` and the exact changed-file list;
2. run the regression command and both Unity builds;
3. stage only the listed files, never `git add .`;
4. commit the compile-safe batch;
5. print `git log --oneline -5` and `git status --short`;
6. report the completed commit, next task number, next expected RED assertion, and next files before starting more work.

If available capacity is becoming insufficient, stop before the next batch and hand off at the last clean commit. Never leave a half-implemented solver change as the intended continuation point. If an unexpected failure makes a batch non-compilable, report the last good commit and the exact failing command before making more changes.

The dirty `Assets/TestScenes/Vessel_PBDTest/SingLayer.unity` and temporary `.dotnet/`, `.vs/`, `CodexBackups/`, test `bin/`, and `UserSettings/` paths are always excluded.

## File responsibility map

- `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`: extend the existing reflection regression with oriented-barrier math, metadata, scheduling, continuous-tightening, and legacy-isolation assertions.
- `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`: own barrier data structures, pure contact policy, persistent manifold state, oriented barrier job, and compression-demand output.
- `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`: propagate circumferential metadata, allocate/build/clear manifold storage, validate PullLine creation, schedule the barrier as the final geometric gate, and keep target progression independent of contact activation.
- `Assets/PBDLearn/Scripts/Cloth/ClothSimulationDriverDoubleLayer.cs`: mark circumferential edges as oriented-barrier inputs and register the committed SideA-to-SideB axis without changing visible guide or render state.
- `Assets/PBDLearn/Scripts/Cloth/ClothDebugGizmos.cs`: expose only final acceptance state (`BarrierActive`, compression demand, stable manifold count); do not add another exploratory diagnostic system.

### Task 1: Define pure oriented-barrier math

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`

- [ ] **Step 1: Add failing reflection tests for normal orientation, gap evaluation, and closure decomposition**

Append assertions that require a public static `PBDLearn.CircumferentialOrientedContactPolicy` with these signatures:

```csharp
public static bool TryOrientBarrierNormal(
    float3 geometricNormal,
    float3 committedSideAToB,
    out float3 barrierNormal);

public static float EvaluateGap(
    float3 pointA,
    float3 pointB,
    float3 barrierNormal,
    float materialGap);

public static float SplitForbiddenClosure(
    float3 sideADelta,
    float3 sideBDelta,
    float3 barrierNormal,
    out float3 admissibleSideADelta,
    out float3 admissibleSideBDelta);

public static bool IsOppositeCircumferentialPair(
    byte flagsA, byte sideA, byte modeA,
    byte flagsB, byte sideB, byte modeB);
```

Test these exact cases:

```csharp
// A flipped geometric normal must be reoriented to +X.
TryOrientBarrierNormal((-1,0,0), (1,0,0)) => normal=(1,0,0)

// A degenerate geometric normal must fall back to committed +X.
TryOrientBarrierNormal((0,0,0), (1,0,0)) => normal=(1,0,0)

// A=0, B=0.01, normal=+X, materialGap=0.006 gives C=0.004.
EvaluateGap(...) => 0.004

// A moves +0.004X and B moves -0.004X: 0.008 normal closure is forbidden,
// while a +0.003Y tangential component remains admissible.
SplitForbiddenClosure(...) => compressionDemand=0.008 and Y unchanged

// Only VirtualSidewall + circumferential mode + opposite SideA/SideB is valid.
```

- [ ] **Step 2: Run the regression and verify RED**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build Assembly-CSharp.csproj -nologo
dotnet run --no-restore --project Tools/Tests/CircumferentialLipIdentityRegression/CircumferentialLipIdentityRegression.csproj -- .
```

Expected: FAIL because `CircumferentialOrientedContactPolicy` is absent.

- [ ] **Step 3: Add the minimal pure policy**

Add to `ClothJobs.cs`:

```csharp
public static class CircumferentialOrientedContactPolicy
{
    public const byte BarrierModeCircumferential = 1;
    public const float BarrierGapEpsilon = 1e-5f;

    public static bool TryOrientBarrierNormal(float3 geometricNormal, float3 committedSideAToB, out float3 barrierNormal)
    {
        float3 axis = math.normalizesafe(committedSideAToB, float3.zero);
        if (math.lengthsq(axis) < 1e-8f)
        {
            barrierNormal = float3.zero;
            return false;
        }

        float3 normal = math.normalizesafe(geometricNormal, axis);
        if (math.dot(normal, axis) < 0f)
            normal = -normal;
        if (math.abs(math.dot(normal, axis)) < 0.1f)
            normal = axis;
        barrierNormal = normal;
        return true;
    }

    public static float EvaluateGap(float3 pointA, float3 pointB, float3 barrierNormal, float materialGap)
    {
        float3 normal = math.normalizesafe(barrierNormal, float3.zero);
        return math.dot(pointB - pointA, normal) - math.max(0f, materialGap);
    }

    public static float SplitForbiddenClosure(
        float3 sideADelta, float3 sideBDelta, float3 barrierNormal,
        out float3 admissibleSideADelta, out float3 admissibleSideBDelta)
    {
        float3 normal = math.normalizesafe(barrierNormal, float3.zero);
        float closingA = math.max(0f, math.dot(sideADelta, normal));
        float closingB = math.max(0f, math.dot(-sideBDelta, normal));
        admissibleSideADelta = sideADelta - normal * closingA;
        admissibleSideBDelta = sideBDelta + normal * closingB;
        return closingA + closingB;
    }

    public static bool IsOppositeCircumferentialPair(
        byte flagsA, byte sideA, byte modeA,
        byte flagsB, byte sideB, byte modeB)
    {
        bool virtualA = (flagsA & SutureLineContactTriangleInfo.FlagVirtualSidewall) != 0;
        bool virtualB = (flagsB & SutureLineContactTriangleInfo.FlagVirtualSidewall) != 0;
        return virtualA && virtualB &&
               modeA == BarrierModeCircumferential && modeB == BarrierModeCircumferential &&
               sideA != sideB &&
               (sideA == SutureSidewallBoundaryEdgeInfo.SideA || sideA == SutureSidewallBoundaryEdgeInfo.SideB) &&
               (sideB == SutureSidewallBoundaryEdgeInfo.SideA || sideB == SutureSidewallBoundaryEdgeInfo.SideB);
    }
}
```

- [ ] **Step 4: Run GREEN verification and checkpoint**

Run regression, `dotnet build Assembly-CSharp.csproj -nologo`, and `dotnet build Assembly-CSharp-Editor.csproj -nologo`.

Commit only the two files:

```powershell
git add Tools/Tests/CircumferentialLipIdentityRegression/Program.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs
git commit -m "phase48: define oriented sidewall barrier policy"
```

### Task 2: Propagate committed side, station, and barrier mode

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulationDriverDoubleLayer.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`

- [ ] **Step 1: Add failing metadata-chain assertions**

Require these fields:

```csharp
// SutureSidewallBoundaryEdgeInfo
public byte barrierMode;

// SutureLineContactTriangleInfo
public int stationIndex;
public byte barrierMode;

// SutureLineConstraintInfo
public float3 barrierAxis;
public float3 foldDirectionHint;
public byte orientedBarrierEnabled;
public byte barrierContractValid;
```

Source assertions must prove:

- only `BuildCircumferentialSutureSidewallBoundaryEdges` assigns `BarrierModeCircumferential`;
- `AddVirtualSidewallContactTriangle` copies `stationIndex`, `sourceSide`, and `barrierMode`;
- ordinary mesh triangles keep `barrierMode=0`;
- circumferential registration still reports `80/80/80`.

- [ ] **Step 2: Run RED**

Expected: FAIL on missing `barrierMode` or `stationIndex` propagation.

- [ ] **Step 3: Implement metadata propagation**

Add fields to the structs above. In the circumferential builder set:

```csharp
edge.stationIndex = stationIndex;
edge.barrierMode = CircumferentialOrientedContactPolicy.BarrierModeCircumferential;
```

Leave `barrierMode=0` in the legacy builder. In `AddVirtualSidewallContactTriangle` copy:

```csharp
stationIndex = edge.stationIndex,
sourceSide = edge.side,
barrierMode = edge.barrierMode,
```

At PullLine creation set:

```csharp
bool hasCircumferentialA = RangeHasCircumferentialVirtualTriangles(contactTriStartA, contactTriCountA);
bool hasCircumferentialB = RangeHasCircumferentialVirtualTriangles(contactTriStartB, contactTriCountB);
constraint.orientedBarrierEnabled = (byte)(hasCircumferentialA && hasCircumferentialB ? 1 : 0);
constraint.barrierContractValid = 1;
constraint.barrierAxis = closureDirection;
constraint.foldDirectionHint = contactNormalHint;
```

If exactly one endpoint has circumferential VirtualSidewall input, reject PullLine creation with `reason=oriented-barrier-side-incomplete` before adding the constraint.

- [ ] **Step 4: Run GREEN verification and checkpoint**

Run regression and both builds. Commit the four files:

```powershell
git add Tools/Tests/CircumferentialLipIdentityRegression/Program.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothSimulationDriverDoubleLayer.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs
git commit -m "phase49: propagate oriented barrier identity"
```

### Task 3: Build and retain opposite-side manifold entries

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`

- [ ] **Step 1: Add failing manifold contract assertions**

Require:

```csharp
public struct SutureOrientedContactManifoldInfo
{
    public const int MaxManifoldsPerLine = 192;
    public int lineIndex;
    public int triangleIndexA;
    public int triangleIndexB;
    public int stationIndexA;
    public int stationIndexB;
    public ulong featureKey;
    public float3 barrierNormal;
    public float normalLambda;
    public float lastGap;
    public float compressionDemand;
    public byte state;
    public byte releaseFrames;
    public byte enabled;
}
```

Require `manifoldStart` and `manifoldCount` on `SutureLineConstraintInfo`, a persistent `_sutureOrientedContactManifolds` NativeList in Simulator, disposal/clear paths, and a builder named `TryBuildCircumferentialOrientedManifolds`.

The regression source audit must reject builders that accept same-side pairs or use nearest-station rematching. It must require station equality or circular neighbor distance `<=1` using the already committed ordinal.

- [ ] **Step 2: Run RED**

Expected: FAIL because manifold state and builder do not exist.

- [ ] **Step 3: Implement deterministic creation-time manifold ranges**

Add lifecycle constants:

```csharp
public const byte StateInactive = 0;
public const byte StateSweptCandidate = 1;
public const byte StateBarrierActive = 2;
public const byte StateCompressionFold = 3;
public const byte StateSettled = 4;
```

In `TryBuildCircumferentialOrientedManifolds`, iterate the two endpoint triangle ranges, keep only `IsOppositeCircumferentialPair`, and retain equal/neighbor station intervals. Use a stable key from the two sorted triangle keys and station ordinals. Preallocate before adding; if `SutureOrientedContactManifoldInfo.MaxManifoldsPerLine` (`192`) would be exceeded, return `reason=oriented-manifold-capacity` before the PullLine constraint is added.

Initialize every entry with:

```csharp
barrierNormal = math.normalizesafe(constraint.barrierAxis, float3.zero);
normalLambda = 0f;
lastGap = float.PositiveInfinity;
compressionDemand = 0f;
state = StateInactive;
releaseFrames = 0;
enabled = 1;
```

On PullLine creation failure, resize the NativeList back to its saved pre-build length so no orphan manifold entries remain.

- [ ] **Step 4: Run GREEN verification and checkpoint**

Run regression and both builds. Commit the three files:

```powershell
git add Tools/Tests/CircumferentialLipIdentityRegression/Program.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs
git commit -m "phase50: retain circumferential contact manifolds"
```

### Task 4: Solve the one-sided barrier and make it the final gate

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`

- [ ] **Step 1: Add failing solve and schedule assertions**

Require a `SutureOrientedBarrierJob` and `StartSutureOrientedBarrierJob`. Source assertions must prove:

- the oriented path never calls `ProjectAwayFromClosureAxis` for its barrier normal;
- the job receives previous positions, predicted positions, masses, contact triangles, manifold state, and constraints;
- `StartSutureOrientedBarrierJob` runs after the existing PullLine/contact work and again after the last position-changing constraint in every solver iteration;
- the final call is later than the final `StartFinalConstraintsJob` call;
- oriented barrier projection continues for `LifecycleSettledHold` lines so a settled stitch cannot later cross;
- ordinary `SutureLineContactJob` remains available for non-oriented lines.

- [ ] **Step 2: Run RED**

Expected: FAIL on missing oriented job/final-gate scheduling.

- [ ] **Step 3: Implement continuous one-sided projection**

For each enabled manifold:

1. read its triangle pair and compute the closest VT/EE feature using current predicted positions;
2. orient the geometric normal through `TryOrientBarrierNormal`;
3. evaluate current and previous `C` values;
4. activate when current `C <= 0` or the sweep crosses from positive to non-positive;
5. solve XPBD inequality with non-negative lambda;
6. distribute corrections by inverse mass and barycentric/edge weights;
7. retain lambda and oriented normal while active;
8. release only after positive release gap, separating velocity, and the existing persistent-frame count.

Use the standard inequality update:

```csharp
float alpha = compliance / math.max(dt * dt, 1e-8f);
float deltaLambda = (-C - alpha * manifold.normalLambda) / (weightSum + alpha);
float nextLambda = math.max(0f, manifold.normalLambda + deltaLambda);
deltaLambda = nextLambda - manifold.normalLambda;
manifold.normalLambda = nextLambda;
```

Do not fabricate a successful correction when the axis, feature, or mass denominator is invalid. Mark the line's barrier contract invalid before subsequent target progression and emit the named reason; never switch the pair to the unsigned nearest-side solver.

- [ ] **Step 4: Run GREEN verification and checkpoint**

Run regression and both builds. Commit the three files:

```powershell
git add Tools/Tests/CircumferentialLipIdentityRegression/Program.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs
git commit -m "phase51: solve oriented sidewall barrier"
```

### Task 5: Couple retained line tension to compression and fold

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`

- [ ] **Step 1: Add failing no-deadlock assertions**

Require these fields on `SutureLineConstraintInfo`:

```csharp
public float compressionDemand;
public float barrierNormalLoad;
public float minimumBarrierGap;
public int activeBarrierManifolds;
public byte barrierState;
```

Require source evidence that:

- `currentRestDistance` continues moving toward `targetRestDistance` while `BarrierActive` or `CompressionFold`;
- barrier activation does not set `restFrozen` and does not zero PullLine stiffness;
- `compressionDemand` is derived from forbidden normal closure and/or positive normal-lambda growth;
- `SutureLineCompressionFoldJob` consumes `compressionDemand` along `foldDirectionHint`;
- fold corrections include compatible Outer/Inner patch weights rather than moving only one layer;
- workflow completion accepts stable material gap + retained tension + stable fold without requiring zero portal distance.

- [ ] **Step 2: Run RED**

Expected: FAIL on missing compression-demand coupling.

- [ ] **Step 3: Implement continuous tightening and load transfer**

Keep the existing target progression code active. In the barrier job accumulate:

```csharp
constraint.compressionDemand = math.max(
    0f,
    constraint.compressionDemand * 0.9f + forbiddenClosure + math.max(0f, deltaLambda));
constraint.barrierNormalLoad = math.max(0f, manifold.normalLambda);
constraint.barrierState = constraint.compressionDemand > 1e-7f
    ? SutureOrientedContactManifoldInfo.StateCompressionFold
    : SutureOrientedContactManifoldInfo.StateBarrierActive;
```

Update `SutureLineCompressionFoldJob` so its desired fold magnitude includes `compressionDemand` and its direction comes from `foldDirectionHint`. Preserve current max-correction safety, but do not make success depend on increasing it.

Update workflow readiness so an oriented line is eligible when all are true for the hold window:

```csharp
barrierContractValid != 0
barrierState >= StateBarrierActive
minimumBarrierGap >= -CircumferentialOrientedContactPolicy.BarrierGapEpsilon
currentRestDistance <= targetRestDistance + restTolerance
barrierNormalLoad > 0f
```

Do not require the physical face gap or portal distance to become zero.

- [ ] **Step 4: Run GREEN verification and checkpoint**

Run regression and both builds. Commit the three files:

```powershell
git add Tools/Tests/CircumferentialLipIdentityRegression/Program.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs
git commit -m "phase52: transfer suture load into whole-wall fold"
```

### Task 6: Final isolation, acceptance state, and Unity handoff

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothDebugGizmos.cs`

- [ ] **Step 1: Add final regression assertions**

Require final status fields in `SutureLineDisplayInfo`:

```csharp
public byte barrierState;
public int activeBarrierManifolds;
public float minimumBarrierGap;
public float barrierNormalLoad;
public float compressionDemand;
```

Audit that middle-wall rendering does not read manifold/contact state, greenfield paths do not enable the circumferential barrier, and registration remains `requested=accepted=runtimeActive=80`.

- [ ] **Step 2: Add compact acceptance output**

Extend the existing PullLine label/log with:

```text
Barrier=<state> M=<active>/<total> minC=<gap> load=<normalLambda> comp=<demand>
```

Do not add per-feature spam or another exploratory point cloud.

- [ ] **Step 3: Run complete automated verification**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build Assembly-CSharp.csproj -nologo
dotnet build Assembly-CSharp-Editor.csproj -nologo
dotnet run --no-restore --project Tools/Tests/CircumferentialLipIdentityRegression/CircumferentialLipIdentityRegression.csproj -- .
git diff --check -- Assets/PBDLearn/Scripts/Cloth Tools/Tests/CircumferentialLipIdentityRegression
```

Expected: both builds succeed with zero errors, regression prints PASS, and diff check is clean.

- [ ] **Step 4: Checkpoint final code state**

```powershell
git add Tools/Tests/CircumferentialLipIdentityRegression/Program.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs
git add Assets/PBDLearn/Scripts/Cloth/ClothDebugGizmos.cs
git commit -m "phase53: expose oriented barrier acceptance state"
git log --oneline -5
git status --short
```

- [ ] **Step 5: Perform Unity Play acceptance**

Verify in the current circumferential scene:

- `displayPts=41 collisionStations=40 requested=80 accepted=80 runtimeActive=80 complete=1`;
- non-zero VirtualSidewall triangles and active oriented manifolds;
- PullLine target continues shortening after `BarrierActive`;
- minimum barrier gap never becomes negative beyond numerical epsilon;
- stable retained tension and non-zero compression demand produce whole-wall folding;
- no visible interpenetration, rigid ordinal zipper, Outer/Inner shear, jitter, or latch chatter;
- visible circumferential cut and middle render layer remain present;
- one PullLine and multiple sequential PullLines both settle.

If Unity Play fails, stop at the earliest failed contract (identity, manifold build, barrier inequality, load transfer, final ordering, or render isolation). Do not tune thickness, stiffness, max push, or iteration counts before identifying that layer.
