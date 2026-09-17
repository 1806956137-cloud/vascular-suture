# Oriented Barrier Load Transfer Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent first-contact vessel collapse while retaining continuous PullLine tightening and circumferential one-sided contact.

**Architecture:** Keep registration and barrier identity unchanged. Make fold eligibility depend on current load-bearing contact, make fold correction a genuinely bounded positional displacement, then replace recursive manifold demand accumulation with per-solve maximum blocked closure.

**Tech Stack:** Unity C#, Unity.Mathematics, Unity.Collections/Jobs, reflection-based .NET regression executable, Git checkpoints.

---

## File responsibility map

- `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`: executable RED/GREEN contracts for the pure gate, mass-safe correction, and demand aggregation.
- `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`: pure policy methods plus oriented barrier and fold implementation.
- `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`: compact acceptance reporting only if a new applied-fold value is required; do not alter registration or rendering.

### Task 1: Gate fold on current load and enforce an actual positional bound

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`

- [ ] **Step 1: Write failing regression assertions**

Reflect pure policy methods with these behaviors:

```csharp
ShouldApplyCompressionFold(StateCompressionFold, 1, 0f, 0f, 0.01f) == false
ShouldApplyCompressionFold(StateCompressionFold, 1, 0.01f, 0f, 0.01f) == true
ComputeFoldVertexCorrection(new float3(0.00065f, 0f, 0f), 0.001f, 0.00065f)
    == new float3(0.00065f, 0f, 0f)
```

Require `SutureLineCompressionFoldJob` to use both methods and reject a source body that multiplies an already positional fold correction by inverse mass.

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet run --no-restore --project Tools/Tests/CircumferentialLipIdentityRegression/CircumferentialLipIdentityRegression.csproj -- .
```

Expected: FAIL because the load gate and mass-safe positional helper do not exist.

- [ ] **Step 3: Implement the minimal gate and bounded correction**

Add pure methods to `CircumferentialOrientedContactPolicy`. The gate requires current state, `activeBarrierManifolds > 0`, finite positive normal load, finite gap, and positive demand. The correction helper returns zero for fixed/invalid mass and otherwise clamps the positional vector length without multiplying by inverse mass.

Use the gate in `SutureLineCompressionFoldJob`. Pass `maxCorrection` into the helper at the final vertex write.

- [ ] **Step 4: Run GREEN and project verification**

Run the regression, `dotnet build Assembly-CSharp.csproj -nologo`, `dotnet build Assembly-CSharp-Editor.csproj -nologo`, and `git diff --check`.

- [ ] **Step 5: Commit**

Explicitly stage only the two modified files and commit:

```text
phase54: bound oriented fold correction
```

### Task 2: Aggregate fresh blocked closure once per solve

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`

- [ ] **Step 1: Write failing demand tests**

Require a pure aggregation method:

```csharp
AggregateCompressionDemand(0f, 0.004f) == 0.004f
AggregateCompressionDemand(0.004f, 0.003f) == 0.004f
AggregateCompressionDemand(0.004f, 0.006f) == 0.006f
```

Source assertions require demand reset at the start of each oriented constraint solve, forbid `constraint.compressionDemand * 0.9f`, and require current load-bearing manifold counting.

- [ ] **Step 2: Run RED**

Run the regression and verify it fails on the missing aggregation contract.

- [ ] **Step 3: Implement per-solve aggregation**

At the start of every oriented constraint solve, set `compressionDemand=0`. For each manifold compute:

```csharp
blockedClosure = max(forbiddenClosure, max(0, -constraintGap));
compressionDemand = max(compressionDemand, blockedClosure);
```

Do not add XPBD lambda to this length. Count a manifold as load-bearing only when it has positive normal lambda or is resolving a current non-positive gap. Retained positive-gap zero-lambda manifolds remain lifecycle entries but do not set `CompressionFold`.

- [ ] **Step 4: Run GREEN and full verification**

Run the regression, both Unity project builds, `git diff --check`, and inspect the exact diff. Confirm registration, middle render, and greenfield source assertions remain green.

- [ ] **Step 5: Commit**

Explicitly stage only the two modified files and commit:

```text
phase55: aggregate fresh barrier compression demand
```

### Task 3: Unity Play acceptance

- [ ] Verify `displayPts=41 collisionStations=40 requested=80 accepted=80 runtimeActive=80 complete=1`.
- [ ] Verify contact transition never reports `CompressionFold` with `load=0` or `M=0`.
- [ ] Verify endpoint and portal distances do not jump outward at first contact.
- [ ] Verify PullLine target continues shortening while the barrier carries load.
- [ ] Verify finite whole-wall folding without interpenetration, layer shear, zipper locking, or accumulated drift.
- [ ] Verify the visible cut and middle render layer remain present.
