# Circumferential VirtualSidewall Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the circumferential VirtualSidewall generator-to-Simulator registration chain while preserving the restored cut and middle-wall behavior.

**Architecture:** The generator emits a committed cut type plus a unique, shared-seam circumferential collision ring separate from the 41-point display polyline. The driver captures only committed collision state, validates closed Outer/Inner ordinal correspondence, converts local indices once, and requires a Simulator registration receipt with requested, accepted, and runtime-active counts.

**Tech Stack:** Unity C#, reflection-based .NET regression executable, NUnit editor tests, Git checkpoints.

---

### Task 1: Define the closed-ring and shared-seam contract

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Modify: `Assets/PBDLearn/Scripts/AutoSuture/AutoSutureLifecyclePolicy.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/Incision/DoubleLayerIncisionGenerator.cs`

- [ ] Rewrite the regression executable to require a three-point minimum, unique collision stations, explicit closing segments, cut-type metadata, and shared-seam ordinal validation.
- [ ] Force rebuild `Assembly-CSharp.csproj`, run the executable, and verify it fails because the new contract is absent.
- [ ] Add the minimal pure closed-ring/alignment policy and generator result fields needed by the test.
- [ ] Emit unique circumferential collision stations from `lowerCutRing`/`upperCutRing` while leaving display polylines closed and legacy station identities unset.
- [ ] Run the regression executable and force rebuild both Unity projects.
- [ ] Checkpoint only the test, policy, and generator files.

### Task 2: Capture committed type and build complete edges

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulationDriverDoubleLayer.cs`
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`

- [ ] Add failing reflection assertions for committed cut type, dedicated collision collections, no `HasAnyIncisionPairs` gate, 40-to-80 edge construction, and a single Inner offset boundary.
- [ ] Run the regression executable and verify the new assertions fail for missing members.
- [ ] Save committed cut type only after mesh commit; keep preview registration disabled.
- [ ] Capture dedicated Outer/Inner collision stations without populating legacy stations or lip pairs, and skip circumferential geometric collapse explicitly.
- [ ] Rebuild from committed type, validate seam/direction/ordinal, emit all closed segments, and convert local indices once.
- [ ] Run the regression executable and force rebuild both Unity projects.
- [ ] Checkpoint the driver and regression executable.

### Task 3: Add Simulator registration receipts

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulationDriverDoubleLayer.cs`
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`

- [ ] Add failing assertions for a registration result containing requested, accepted, and runtime-active counts and for layer-range validation.
- [ ] Run the regression executable and verify it fails for the missing receipt contract.
- [ ] Make `SetSutureSidewallBoundaryEdges` return a receipt, reject invalid side/layer/range/duplicates, and recalculate live runtime-active edges.
- [ ] Require the complete receipt in the driver; clear only VirtualSidewall state on mismatch and log all three counts.
- [ ] Include receipt counts in relevant runtime diagnostics without changing the stable suture solver behavior.
- [ ] Run the regression executable and force rebuild both Unity projects.
- [ ] Checkpoint the jobs, Simulator, driver, and test files.

### Task 4: Verify render isolation and final contract

**Files:**
- Modify: `Tools/Tests/CircumferentialLipIdentityRegression/Program.cs`
- Inspect: `Assets/PBDLearn/Scripts/Cloth/ClothSimulationDriverDoubleLayer.cs`

- [ ] Add regression assertions that circumferential collision state is separate from legacy incision state and that middle-wall suppression still depends only on legacy cut semantics.
- [ ] Run the regression executable, both forced Unity rebuilds, and inspect the final diff for forbidden fallback or mutable-mode reads in committed registration.
- [ ] Report the Unity Play checks still requiring user execution: visible ring, middle layer, 40/41 split, 80/80/80 registration, and non-zero VirtualSidewall triangles after a PullLine reaches the cut.
- [ ] Checkpoint the final regression update and output `git log --oneline -5`.
