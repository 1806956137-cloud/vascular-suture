# Oriented Suture Coupled Load Transfer Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the approved Oriented PullLine pipeline so pre-contact tension keeps producing observable WholeWall closure and post-contact Barrier, Guide, and support response run in the specified same-frame order.

**Architecture:** Preserve the explicit Outer/Inner portal and VirtualSidewall column identity added in phases 62-66. Repair from the earliest unproven boundary outward: first expose Pull Proposal output without changing behavior, then correct its numerical contract, then isolate and reorder the Oriented pipeline, and finally replace the two legacy support writers with one explicit-column response and measured post-solve absorption.

**Tech Stack:** Unity C#, Unity Jobs/Burst, Unity.Mathematics, NativeArray/NativeList, .NET 9 reflection/source-contract regression, Git checkpoints.

---

## File map

- `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`: Oriented state fields, Pull Proposal policy/job output, Barrier partition state, Guide job, explicit FoldColumn response, post-solve metrics.
- `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`: state initialization, scheduling, legacy/Oriented path isolation, unified runtime log and lifecycle mapping.
- `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`: numerical policy tests plus source/scheduling/ownership contracts.
- `docs/superpowers/specs/2026-07-05-suture-coupled-load-transfer-repair-design.md`: approved behavioral contract; do not change during implementation unless the user changes semantics.

## Task 1: Expose the existing Pull Proposal boundary

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`

- [ ] **Step 1: Write failing state and log contracts**

Extend the reflected `SutureLineConstraintInfo` field list with:

```csharp
foreach (string fieldName in new[]
{
    "requestedClosure",
    "admittedClosure",
    "blockedClosure",
    "frameStartPortalDistance",
    "frameFinalPortalDistance",
    "pullCorrectionMagnitude",
    "pullFailureReason",
})
{
    if (constraintType.GetField(fieldName) == null)
        throw new InvalidOperationException($"Pull Proposal diagnostics lack {fieldName}.");
}
```

Add source assertions requiring `SutureLineConstraintJob` to write `requestedClosure` and `pullCorrectionMagnitude`, and requiring the simulator source to contain `[Suture][LoadTransfer]`, `lineLambda=`, `req=`, `admitted=`, `blocked=`, `startDistance=`, `finalDistance=`, and `reason=`.

- [ ] **Step 2: Run RED verification**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build Assembly-CSharp.csproj -nologo
dotnet build Tools/Tests/CircumferentialLipIdentityRegression/CircumferentialLipIdentityRegression.csproj -nologo
dotnet Tools/Tests/CircumferentialLipIdentityRegression/bin/Debug/net9.0/CircumferentialLipIdentityRegression.dll .
```

Expected: Assembly build succeeds; regression fails on the first missing Pull Proposal field.

- [ ] **Step 3: Add diagnostic-only state**

Add failure constants and fields to `SutureLineConstraintInfo`:

```csharp
public const byte PullFailureNone = 0;
public const byte PullFailureZeroLambda = 1;
public const byte PullFailureZeroDelta = 2;
public const byte PullFailureImmobileColumn = 3;
public const byte PullFailureProposalNotRetained = 4;

public byte pullFailureReason;
public float requestedClosure;
public float admittedClosure;
public float blockedClosure;
public float frameStartPortalDistance;
public float frameFinalPortalDistance;
public float pullCorrectionMagnitude;
```

At the start of `UpdateSutureLineConstraintProgressMainThread`, reset only the per-frame diagnostic fields and capture `frameStartPortalDistance = actualDistance`. Do not reset lambda, Barrier state, pending, or lifecycle state.

In `SutureLineConstraintJob.Solve`, after `denom` and `pullMagnitude` are known, write:

```csharp
float positiveLambdaStep = math.max(0f, pullMagnitude);
float requestedClosure = positiveLambdaStep * denom;
c.requestedClosure = math.max(c.requestedClosure, requestedClosure);
c.pullCorrectionMagnitude = math.max(c.pullCorrectionMagnitude, requestedClosure);
c.pullFailureReason = requestedClosure > 1e-9f
    ? SutureLineConstraintInfo.PullFailureNone
    : (c.lambda <= 1e-9f
        ? SutureLineConstraintInfo.PullFailureZeroLambda
        : SutureLineConstraintInfo.PullFailureZeroDelta);
```

When `denom <= 1e-8f`, store `PullFailureImmobileColumn` before returning.

At the end of each Barrier constraint pass, expose the instantaneous partition without changing physics:

```csharp
constraint.blockedClosure = math.clamp(
    constraint.compressionDemand,
    0f,
    math.max(0f, constraint.requestedClosure));
constraint.admittedClosure = math.max(
    0f,
    constraint.requestedClosure - constraint.blockedClosure);
```

In `SutureLinePostSolveSettlementJob`, read the final explicit WholeWall portal distance, assign `frameFinalPortalDistance`, and set `PullFailureProposalNotRetained` only when Barrier is inactive, requested closure is nonzero, and the final distance failed to decrease beyond `1e-7f`.

- [ ] **Step 4: Emit the approved unified log**

Add one compact log at the existing throttled progress point:

```csharp
Debug.Log(
    $"[Suture][LoadTransfer] line={c.lineIndex} state={FormatSutureBarrierState(c.barrierState)} " +
    $"req={c.requestedClosure:F6} admitted={c.admittedClosure:F6} blocked={c.blockedClosure:F6} " +
    $"lineLambda={c.lambda:F9} pressure={c.barrierNormalLoad:F9} gap={c.minimumBarrierGap:F6} " +
    $"pending={c.pendingShortening:F6} foldAbs={c.foldAbsorbed:F6} slideAbs={c.slideAbsorbed:F6} " +
    $"guideState={c.guideState} guideSign={c.guideSign} guideRatio={c.guideRatio:F3} " +
    $"startDistance={c.frameStartPortalDistance:F6} finalDistance={c.frameFinalPortalDistance:F6} " +
    $"reason={FormatSuturePullFailure(c.pullFailureReason)}");
```

Add `FormatSuturePullFailure(byte)` with exact names `none`, `zero-lambda`, `zero-delta`, `immobile-pull-column`, and `proposal-not-retained`.

- [ ] **Step 5: Run GREEN verification and checkpoint**

Run both builds, the regression, and `git diff --check`. Expected: 0 build errors, PASS regression, and no whitespace errors.

Stage only the three named source/test files and commit:

```powershell
git add Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs Tools/Tests/CircumferentialLipIdentityRegression/Program.cs
git commit -m "phase67: expose oriented pull proposal boundary"
```

Stop for one Unity Play capture. Do not change Pull math until the new log identifies `zero-lambda`, `zero-delta`, `immobile-pull-column`, or `proposal-not-retained`.

## Task 2: Preserve Oriented Pull through the position commit sleep gate

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`

- [ ] **Step 1: Write failing sleep-policy and ownership regressions**

Reflect a pure policy method with this exact signature:

```csharp
public static bool ShouldSleepVertex(
    bool sutureDriven,
    float deltaLengthSq,
    float velocityLengthSq,
    float sleepPositionThreshold,
    float sleepVelocityThreshold)
```

Resolve it beside the existing `orientedPolicy` methods:

```csharp
MethodInfo shouldSleepVertex = orientedPolicy.GetMethod(
    "ShouldSleepVertex",
    BindingFlags.Public | BindingFlags.Static)
    ?? throw new InvalidOperationException("Missing SutureDriven sleep policy.");
```

Invoke it with the measured phase67 scale (`delta=7e-6`, `dt=0.02`, position threshold `2e-5`, velocity threshold `1e-3`) and assert:

```csharp
float delta = 7e-6f;
float velocity = delta / 0.02f;
object[] sleepArgs =
{
    false,
    delta * delta,
    velocity * velocity,
    2e-5f,
    1e-3f,
};
if (!(bool)shouldSleepVertex.Invoke(null, sleepArgs)!)
    throw new InvalidOperationException("Ordinary sub-threshold cloth motion no longer sleeps.");
sleepArgs[0] = true;
if ((bool)shouldSleepVertex.Invoke(null, sleepArgs)!)
    throw new InvalidOperationException("SutureDriven portal correction is still discarded by sleep.");
```

Add source contracts requiring:

```text
ConstraintType.SutureDriven
RefreshSutureDrivenConstraintTypes
MarkSutureDrivenPortal
constraint.enabled != 0
constraint.orientedBarrierEnabled != 0
constraint.barrierContractValid != 0
constraint.portalA
constraint.innerPortalA
constraint.portalB
constraint.innerPortalB
```

Assert `Step` orders `handle.Complete()`, `RefreshSutureDrivenConstraintTypes()`, then `UpdateSutureLineConstraintProgressMainThread(dt)`. Assert the refresh body does not reference patch ranges, FoldColumn, SeamAnchor, stiffness, compliance, or sleep-threshold values.

- [ ] **Step 2: Run RED verification**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build Tools/Tests/CircumferentialLipIdentityRegression/CircumferentialLipIdentityRegression.csproj -nologo
dotnet Tools/Tests/CircumferentialLipIdentityRegression/bin/Debug/net9.0/CircumferentialLipIdentityRegression.dll .
```

Expected: FAIL because `ShouldSleepVertex` and `ConstraintType.SutureDriven` do not exist.

- [ ] **Step 3: Add the testable sleep policy**

Add to `CircumferentialOrientedContactPolicy`:

```csharp
public static bool ShouldSleepVertex(
    bool sutureDriven,
    float deltaLengthSq,
    float velocityLengthSq,
    float sleepPositionThreshold,
    float sleepVelocityThreshold)
{
    float posThreshold = math.max(0f, sleepPositionThreshold);
    float velThreshold = math.max(0f, sleepVelocityThreshold);
    return !sutureDriven &&
        deltaLengthSq <= posThreshold * posThreshold &&
        velocityLengthSq <= velThreshold * velThreshold;
}
```

In `UpdateVelocitiesAndPositionsJob.Execute`, replace only the inline threshold decision:

```csharp
bool sutureDriven =
    (constraintTypes[index] & ConstraintType.SutureDriven) == ConstraintType.SutureDriven;
if (CircumferentialOrientedContactPolicy.ShouldSleepVertex(
        sutureDriven,
        math.lengthsq(delta),
        math.lengthsq(v),
        sleepPositionThreshold,
        sleepVelocityThreshold))
{
    newPos = prevPos;
    v = float3.zero;
}
```

Do not change either threshold or the velocity/position assignments.

- [ ] **Step 4: Rebuild the existing constraint identity each frame**

Add the existing-channel flag:

```csharp
SutureDriven = 1 << 3,
```

Add `RefreshSutureDrivenConstraintTypes()` to `ClothSimulator`. It first clears only this bit from every vertex, then marks barycentrically participating vertices from `portalA`, `innerPortalA`, `portalB`, and `innerPortalB` for constraints satisfying all three validity gates. Use:

```csharp
private void RefreshSutureDrivenConstraintTypes()
{
    for (int vertexIndex = 0; vertexIndex < _vertexCount; vertexIndex++)
        _constraintTypes[vertexIndex] &= ~ConstraintType.SutureDriven;

    if (!_sutureLineConstraints.IsCreated)
        return;

    for (int constraintIndex = 0; constraintIndex < _sutureLineConstraints.Length; constraintIndex++)
    {
        SutureLineConstraintInfo constraint = _sutureLineConstraints[constraintIndex];
        if (constraint.enabled == 0 || constraint.orientedBarrierEnabled == 0 ||
            constraint.barrierContractValid == 0)
            continue;

        MarkSutureDrivenPortal(constraint.portalA);
        MarkSutureDrivenPortal(constraint.innerPortalA);
        MarkSutureDrivenPortal(constraint.portalB);
        MarkSutureDrivenPortal(constraint.innerPortalB);
    }
}

private void MarkSutureDrivenPortal(NeedlePortalInfo portal)
{
    MarkSutureDrivenVertex(portal.v0, portal.bary.x);
    MarkSutureDrivenVertex(portal.v1, portal.bary.y);
    MarkSutureDrivenVertex(portal.v2, portal.bary.z);
}

private void MarkSutureDrivenVertex(int vertexIndex, float barycentricWeight)
{
    if (vertexIndex < 0 || vertexIndex >= _vertexCount || barycentricWeight <= 1e-6f)
        return;
    _constraintTypes[vertexIndex] |= ConstraintType.SutureDriven;
}
```

Call refresh after the pre-solve `handle.Complete()` and before progress/log reset:

```csharp
handle.Complete();
RefreshSutureDrivenConstraintTypes();
UpdateSutureLineConstraintProgressMainThread(dt);
```

This preserves valid shared portals by rebuilding from all constraints and removes stale bits from invalidated constraints.

- [ ] **Step 5: Run GREEN verification and checkpoint**

Run both Unity builds, the regression build/run, and `git diff --check`. Expected: zero build errors, PASS regression, no whitespace errors. Stage only the three named implementation/test files and commit:

```text
phase68: preserve oriented pull through sleep commit
```

Unity acceptance: while Barrier is Inactive, consecutive throttled logs must show `req > 0`, `lineLambda > 0`, and a decreasing cross-frame `startDistance`; ordinary non-suture cloth retains its configured sleep behavior. If `startDistance` still plateaus, stop at Position Commit and add read-only per-portal commit diagnostics rather than changing Pull math.

## Task 3: Transfer Hist portal ownership to the valid Oriented PullLine

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`

- [ ] **Step 1: Write failing ownership policy and source contracts**

Reflect this exact non-public static policy from `ClothSimulator`:

```csharp
private static bool DoesActiveOrientedPullLineOwnHistoryPass(
    SutureLineConstraintInfo constraint,
    int colliderEntityId,
    int passIndex)
```

Create a reflected `SutureLineConstraintInfo` with `enabled=1`, `orientedBarrierEnabled=1`, `barrierContractValid=1`, matching collider, `passIndexA=1`, and `passIndexB=2`. Assert pass 1 and pass 2 are owned. Independently set each validity field to zero and assert ownership is false. Assert a different collider, pass 3, and a valid non-Oriented line are not owned.

Add source contracts for `AddRetainedHistoryPortalsForHoldDisplay` requiring all display behavior to remain:

```text
_activeNeedlePortals.Add(portal)
_activeNeedlePortalDisplayInfos.Add(display)
```

Require the retained writer to be guarded through:

```text
IsHistoryPassOwnedByActiveOrientedPullLine(entry.colliderEntityId, entry.passIndex)
_retainedHistoryHoldPortals.Add(portal)
```

Require `IsHistoryPassOwnedByActiveOrientedPullLine` to scan all `_sutureLineConstraints` and call `DoesActiveOrientedPullLineOwnHistoryPass`. The hold Job and its scheduler must remain present, proving non-Oriented and unowned Hist behavior is not globally disabled.

- [ ] **Step 2: Run RED verification**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build Tools/Tests/CircumferentialLipIdentityRegression/CircumferentialLipIdentityRegression.csproj -nologo
dotnet Tools/Tests/CircumferentialLipIdentityRegression/bin/Debug/net9.0/CircumferentialLipIdentityRegression.dll .
```

Expected: FAIL with `Missing active Oriented PullLine Hist ownership policy.`

- [ ] **Step 3: Implement the ownership handoff without changing puncture display**

Add the pure policy and scanner to `ClothSimulator`:

```csharp
private static bool DoesActiveOrientedPullLineOwnHistoryPass(
    SutureLineConstraintInfo constraint,
    int colliderEntityId,
    int passIndex)
{
    return constraint.enabled != 0 &&
        constraint.orientedBarrierEnabled != 0 &&
        constraint.barrierContractValid != 0 &&
        constraint.colliderEntityId == colliderEntityId &&
        (constraint.passIndexA == passIndex || constraint.passIndexB == passIndex);
}

private bool IsHistoryPassOwnedByActiveOrientedPullLine(int colliderEntityId, int passIndex)
{
    if (!_sutureLineConstraints.IsCreated)
        return false;

    for (int lineIndex = 0; lineIndex < _sutureLineConstraints.Length; lineIndex++)
    {
        if (DoesActiveOrientedPullLineOwnHistoryPass(
                _sutureLineConstraints[lineIndex], colliderEntityId, passIndex))
            return true;
    }
    return false;
}
```

Keep the existing display additions unchanged. Guard only the retained Hold writer:

```csharp
if (_retainedHistoryHoldPortals.IsCreated &&
    !IsHistoryPassOwnedByActiveOrientedPullLine(entry.colliderEntityId, entry.passIndex))
{
    _retainedHistoryHoldPortals.Add(portal);
}
```

This transfers both Outer and Inner history entries for pass A/B because they share the committed collider/pass identity. A disabled, invalid, deleted, non-Oriented, or unrelated line does not claim ownership, so the original puncture Hold resumes or remains unchanged.

- [ ] **Step 4: Run GREEN verification and checkpoint**

Run both Unity builds, regression build/run, and `git diff --check`. Expected: zero build errors, PASS regression, and no whitespace errors. Stage only `ClothSimulator.cs` and `Program.cs`; commit:

```text
phase69: hand off oriented pull anchor ownership
```

Unity acceptance: while Barrier is Inactive, `req` and `lineLambda` remain nonzero and cross-frame `startDistance` continues decreasing past the previous `0.105033` plateau. Hist display remains present. If the same plateau remains, stop and add a read-only Hold/Pull projection diagnostic; do not alter Pull parameters or disable any other constraint.

## Task 4: Select Guide from current Barrier evidence

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`

- [ ] **Step 1: Write failing Guide state tests**

Add state fields `signedOverlap`, `previousGuideMetric`, and `guideStableEvaluations`. Test:

```text
Unresolved + confident overlap -> stable count 1, not Locked
same sign three evaluations -> Locked
opposite identity fallback while Locked -> sign unchanged
full release -> Unresolved and stable count 0
```

Require source order `Barrier < GuideSelector < FinalBarrier` and reject world-Y or triangle-winding sign selection inside the Oriented Guide job.

- [ ] **Step 2: Run RED verification**

Expected: missing fields/policy methods fail.

- [ ] **Step 3: Aggregate signed overlap in Barrier**

For each load-bearing manifold, project the SideB-SideA feature displacement onto the candidate guide axis and accumulate with `normalLambda` weight. Store the normalized aggregate on the constraint after the manifold loop. Do not choose the sign in Barrier.

- [ ] **Step 4: Implement `SutureOrientedGuideSelectorJob`**

Use the approved priority: signed overlap, relative guide-axis motion, route/portal handedness, stable identity parity. Provisional uses 25% of the maximum guide ratio; Locked uses the full policy ratio. Only full release resets Locked.

Schedule this job immediately after First Barrier. It writes state only and does not write positions.

- [ ] **Step 5: Run GREEN verification and checkpoint**

Commit the three files as:

```text
phase70: select suture guide after barrier
```

## Task 5: Replace the two Oriented support writers with explicit columns

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`

- [ ] **Step 1: Write failing explicit-column and single-writer contracts**

Define `SutureFoldColumnInfo` with `outer`, `inner`, `bandT`, `weight`, and `side`. Add per-constraint `foldColumnStart/foldColumnCount`. Assert the Oriented support job reads this array and does not read `crossLayerLinks`.

Require the Oriented scheduler to contain exactly one support-response job and no Oriented execution in the legacy Slide/Fold writers.

- [ ] **Step 2: Run RED verification**

Expected: missing explicit FoldColumn storage fails.

- [ ] **Step 3: Build explicit FoldColumn pairs at PullLine creation**

Resolve each Outer FoldColumn to exactly one Inner FoldColumn during construction. Reject missing, self, or ambiguous partners with a named Oriented contract failure. Store pairs in a persistent NativeList and ranges in the constraint. Runtime support solving must not search `crossLayerLinks`.

- [ ] **Step 4: Implement the unified profile**

For each explicit column:

```csharp
float t = math.saturate(column.bandT);
float slideProfile = 1f - t;
float foldProfile = 4f * t * (1f - t);
float amount = slideAmount * slideProfile + foldAmount * foldProfile;
float3 correction = guideDirection * guideSign * sideSign * amount * column.weight;
predictPositions[column.outer] += correction;
predictPositions[column.inner] += correction;
```

Clamp the final positional correction by the existing per-iteration support limit. PullAnchor and SeamAnchor are absent from the explicit FoldColumn list and therefore cannot receive this writer.

Schedule the unified response immediately after Guide Selector and before cross-layer thickness coupling. At this checkpoint the complete approved order becomes Pull, Barrier, Guide, Unified Support, Thickness, FinalBarrier.

- [ ] **Step 5: Run GREEN verification and checkpoint**

Commit as:

```text
phase71: unify whole-wall suture support response
```

## Task 6: Measure retained absorption and enforce completion gates

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`

- [ ] **Step 1: Write failing metric and settlement tests**

Add previous/current slide and fold metrics. Test that rigid guide translation contributes only to slide, curvature relative to the PullAnchor-Seam chord contributes only to fold, negative/reversed motion contributes zero absorption, and settlement runs once per frame.

- [ ] **Step 2: Run RED verification**

Expected: missing metrics fail.

- [ ] **Step 3: Measure after Final Barrier**

Compute `frameAdmittedNormalClosure` from the captured frame-start gap and Final Barrier gap. Compute fold/slide metrics from explicit FoldColumn pairs in `SutureLinePostSolveSettlementJob`, then call the existing `SettlePendingShortening` policy once. Update `foldAbsorbed/slideAbsorbed` only from retained positive metric increments.

- [ ] **Step 4: Enforce the approved Settled gate**

Require valid Barrier gap, nonzero line tension, residual pending below tolerance, stable metrics, Guide Locked, and WholeWall column integrity. Settled freezes new rest/Fold/Slide drive but preserves line lambda, manifolds, Barrier pressure, and contact latch.

- [ ] **Step 5: Run GREEN verification and checkpoint**

Commit as:

```text
phase72: settle suture from retained absorption
```

## Task 7: Full verification and Unity handoff

**Files:**
- No new files unless a compile correction is required in one of the three implementation files.

- [ ] **Step 1: Run complete static verification**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build Assembly-CSharp.csproj -nologo
dotnet build Assembly-CSharp-Editor.csproj -nologo
dotnet build Tools/Tests/CircumferentialLipIdentityRegression/CircumferentialLipIdentityRegression.csproj -nologo
dotnet Tools/Tests/CircumferentialLipIdentityRegression/bin/Debug/net9.0/CircumferentialLipIdentityRegression.dll .
git diff --check
```

Expected: all builds have 0 errors, regression prints PASS, and diff check is clean.

- [ ] **Step 2: Run the single-line Unity scenario**

Read `[Suture][LoadTransfer]` in phase order: Approach must show sustained distance decrease; ContactCandidate must show nonzero pressure; LoadTransfer must show a stable Guide and pending creation only from blocked rest shortening; Absorbing must show retained fold/slide increase and pending decrease; SettledHold must keep line tension and pressure.

- [ ] **Step 3: Run sequential multi-line regression**

Verify each line owns independent Guide/pending state, later lines do not flip earlier Guide signs, and Outer/Inner plus visible incision remain continuous.

- [ ] **Step 4: Print final checkpoint evidence**

Print `git diff --stat`, the explicit modified file list, the verification outputs, and `git log --oneline -5`. Do not push, merge, delete branches, or alter the user's scene file.
