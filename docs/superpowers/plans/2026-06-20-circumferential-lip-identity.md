# Circumferential VirtualSidewall Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve authoritative circumferential cut-ring vertex identities, close both sidewall chains, and register non-empty VirtualSidewall proxies with the current cloth simulator.

**Architecture:** Keep identity at its producer: pass the existing output-mesh `lowerCutRing` and `upperCutRing` indices into station construction. Keep closed/open sequencing in an explicit pure policy helper used by the driver. Circumferential SideGuide readiness becomes conditional on a complete `2 * ringCount` edge set; no nearest-point recovery, threshold changes, new particles, or render changes are permitted.

**Tech Stack:** Unity 2022.3, C#, Unity Mathematics, PowerShell reflection regressions, dotnet build, Git checkpoints.

---

### Task 1: Add the failing circumferential identity regression

**Files:**
- Create: `Tools/Tests/Test-CircumferentialLipIdentity.ps1`

- [ ] **Step 1: Write the reflection regression**

```powershell
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unityCore = Join-Path $root 'Library\ScriptAssemblies\UnityEngine.CoreModule.dll'
$assemblyPath = Join-Path $root 'Temp\bin\Debug\Assembly-CSharp.dll'

[System.Reflection.Assembly]::LoadFrom($unityCore) | Out-Null
$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$generator = $assembly.GetType('PBDLearn.DoubleLayerIncisionGenerator', $true)
$flags = [System.Reflection.BindingFlags]'Static,NonPublic'
$method = $generator.GetMethods($flags) |
    Where-Object { $_.Name -eq 'BuildCircumferentialStations' -and $_.GetParameters().Count -eq 5 } |
    Select-Object -First 1
if ($null -eq $method) {
    throw 'Expected BuildCircumferentialStations to accept boundary points and both ring-index arrays.'
}

$layerType = $generator.GetNestedType('LayerResult', [System.Reflection.BindingFlags]'NonPublic')
$stationType = $generator.GetNestedType('StationResult', [System.Reflection.BindingFlags]'NonPublic')
$fieldFlags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
$stationsField = $layerType.GetField('stations', $fieldFlags)
$leftIndexField = $stationType.GetField('leftVertexIndex', $fieldFlags)
$rightIndexField = $stationType.GetField('rightVertexIndex', $fieldFlags)

function New-BoundaryList([float]$z) {
    $list = [System.Collections.Generic.List[UnityEngine.Vector3]]::new()
    $list.Add([UnityEngine.Vector3]::new(1, 0, $z))
    $list.Add([UnityEngine.Vector3]::new(0, 1, $z))
    $list.Add([UnityEngine.Vector3]::new(-1, 0, $z))
    $list.Add([UnityEngine.Vector3]::new(1, 0, $z))
    return ,$list
}

function Invoke-StationBuild([int[]]$lowerIds, [int[]]$upperIds) {
    $layer = [System.Activator]::CreateInstance($layerType, $true)
    $arguments = @($layer, (New-BoundaryList -z -0.1), (New-BoundaryList -z 0.1), $lowerIds, $upperIds)
    $method.Invoke($null, $arguments) | Out-Null
    return $stationsField.GetValue($layer)
}

$stations = Invoke-StationBuild -lowerIds ([int[]](11, 12, 13)) -upperIds ([int[]](21, 22, 23))
if ($stations.Count -ne 3) { throw "Expected 3 unique ring stations, got $($stations.Count)." }
for ($i = 0; $i -lt $stations.Count; $i++) {
    if ($leftIndexField.GetValue($stations[$i]) -ne (11 + $i) -or
        $rightIndexField.GetValue($stations[$i]) -ne (21 + $i)) {
        throw "Station $i did not preserve its lower/upper ring identities."
    }
}

$invalid = Invoke-StationBuild -lowerIds ([int[]](11, 12)) -upperIds ([int[]](21, 22, 23))
if ($invalid.Count -ne 0) { throw "Mismatched identity chains emitted $($invalid.Count) partial stations." }

Write-Output 'PASS: circumferential stations preserve complete cut-ring identity chains.'
```

- [ ] **Step 2: Build unchanged production code**

Run: `dotnet build Assembly-CSharp.csproj -nologo`

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Verify RED**

Run: `powershell -ExecutionPolicy Bypass -File Tools/Tests/Test-CircumferentialLipIdentity.ps1`

Expected: FAIL because the current station builder has three parameters and emits `-1/-1` identities.

- [ ] **Step 4: Commit the RED checkpoint**

Explicitly stage only `Tools/Tests/Test-CircumferentialLipIdentity.ps1` and commit with `test: cover circumferential lip identity propagation`.

### Task 2: Propagate authoritative cut-ring identities

**Files:**
- Modify: `Assets/PBDLearn/Scripts/Cloth/Incision/DoubleLayerIncisionGenerator.cs:1886`
- Modify: `Assets/PBDLearn/Scripts/Cloth/Incision/DoubleLayerIncisionGenerator.cs:2371-2424`

- [ ] **Step 1: Pass the authoritative arrays**

```csharp
BuildCircumferentialStations(
    layer,
    work.lowerBoundaryWorld,
    work.upperBoundaryWorld,
    lowerCutRing,
    upperCutRing);
```

- [ ] **Step 2: Validate the complete chain before emitting stations**

```csharp
private static void BuildCircumferentialStations(
    LayerResult layer,
    List<Vector3> lowerBoundary,
    List<Vector3> upperBoundary,
    int[] lowerVertexIndices,
    int[] upperVertexIndices)
{
    if (layer == null)
        return;

    layer.stations.Clear();
    if (lowerBoundary == null || upperBoundary == null ||
        lowerVertexIndices == null || upperVertexIndices == null)
        return;

    int count = lowerVertexIndices.Length;
    bool boundaryCountsValid =
        (lowerBoundary.Count == count || lowerBoundary.Count == count + 1) &&
        (upperBoundary.Count == count || upperBoundary.Count == count + 1);
    if (count < 2 || upperVertexIndices.Length != count || !boundaryCountsValid)
        return;

    for (int i = 0; i < count; i++)
    {
        if (lowerVertexIndices[i] < 0 || upperVertexIndices[i] < 0 ||
            lowerVertexIndices[i] == upperVertexIndices[i])
            return;
    }
```

Keep the existing geometry calculation, iterate exactly `count`, and assign:

```csharp
leftVertexIndex = lowerVertexIndices[i],
rightVertexIndex = upperVertexIndices[i]
```

- [ ] **Step 3: Verify GREEN**

Run the runtime build and `Test-CircumferentialLipIdentity.ps1`.

Expected: build succeeds and the script prints its PASS line.

- [ ] **Step 4: Commit the identity checkpoint**

Output `git diff --stat`, explicitly stage only the generator file, and commit with `phase43: propagate circumferential lip identities`.

### Task 3: Add the failing closed-ring sequencing regression

**Files:**
- Create: `Tools/Tests/Test-CircumferentialSidewallClosure.ps1`

- [ ] **Step 1: Write the policy regression**

```powershell
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unityCore = Join-Path $root 'Library\ScriptAssemblies\UnityEngine.CoreModule.dll'
$assemblyPath = Join-Path $root 'Temp\bin\Debug\Assembly-CSharp.dll'
[System.Reflection.Assembly]::LoadFrom($unityCore) | Out-Null
$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$policy = $assembly.GetType('PBDLearn.IncisionSidewallContactPolicy', $true)
$method = $policy.GetMethod('TryGetSidewallStationSegment', [System.Reflection.BindingFlags]'Public,Static')
if ($null -eq $method) { throw 'Expected TryGetSidewallStationSegment policy helper.' }

function Assert-Segment([int]$count, [bool]$closed, [int]$segment, [bool]$expectedOk, [int]$expectedA, [int]$expectedB) {
    $arguments = [object[]]@($count, $closed, $segment, 0, 0)
    $ok = [bool]$method.Invoke($null, $arguments)
    if ($ok -ne $expectedOk) { throw "Unexpected validity for segment $segment." }
    if ($ok -and ($arguments[3] -ne $expectedA -or $arguments[4] -ne $expectedB)) {
        throw "Segment $segment expected $expectedA->$expectedB, got $($arguments[3])->$($arguments[4])."
    }
}

Assert-Segment 3 $false 0 $true 0 1
Assert-Segment 3 $false 1 $true 1 2
Assert-Segment 3 $false 2 $false -1 -1
Assert-Segment 3 $true 2 $true 2 0
Write-Output 'PASS: circumferential sidewall sequence closes last-to-first without closing slit mode.'
```

- [ ] **Step 2: Verify RED**

Run the runtime build and the closure script.

Expected: FAIL because `TryGetSidewallStationSegment` does not exist.

- [ ] **Step 3: Commit the RED checkpoint**

Explicitly stage only the closure script and commit with `test: cover circumferential sidewall closure`.

### Task 4: Close and validate circumferential VirtualSidewall registration

**Files:**
- Modify: `Assets/PBDLearn/Scripts/AutoSuture/AutoSutureLifecyclePolicy.cs:66-100`
- Modify: `Assets/PBDLearn/Scripts/Cloth/ClothSimulationDriverDoubleLayer.cs:2077-2263`

- [ ] **Step 1: Add the pure open/closed segment policy**

```csharp
public static bool TryGetSidewallStationSegment(
    int stationCount,
    bool closedLoop,
    int segmentIndex,
    out int startIndex,
    out int endIndex)
{
    startIndex = -1;
    endIndex = -1;
    if (stationCount < 2)
        return false;

    int segmentCount = closedLoop ? stationCount : stationCount - 1;
    if (segmentIndex < 0 || segmentIndex >= segmentCount)
        return false;

    startIndex = segmentIndex;
    endIndex = segmentIndex + 1;
    if (endIndex >= stationCount)
        endIndex = 0;
    return true;
}
```

- [ ] **Step 2: Use the policy in each sidewall chain**

Pass `closedLoop: separationMode` into both `AppendSutureSidewallBoundaryEdges` calls. Replace the open-only `for (int i = 1; i < count; i++)` loop with a segment loop using `TryGetSidewallStationSegment`; use `segmentIndex` as the emitted station index. This must generate `count - 1` segments in slit mode and `count` in circumferential mode.

- [ ] **Step 3: Reject false circumferential readiness**

After edge construction, compute `expectedEdgeCount = 2 * min(outerStations.Count, innerStations.Count)`. In circumferential mode, require `sidewallMissing == 0` and the actual count to equal this value. On failure, set `_sutureSideGuideValid = false`, include expected/actual/missing counts in `_sutureSideGuideSummary`, clear the simulator guides, and return before PullLine planning.

- [ ] **Step 4: Verify GREEN and compile both assemblies**

Run both PowerShell regressions, `dotnet build Assembly-CSharp.csproj -nologo`, and `dotnet build Assembly-CSharp-Editor.csproj -nologo`.

Expected: both scripts print PASS and both builds complete with 0 errors.

- [ ] **Step 5: Commit the registration checkpoint**

Output `git diff --stat`, explicitly stage the policy and driver files, and commit with `phase44: register closed circumferential sidewalls`. Output `git log --oneline -5`.

### Task 5: Runtime acceptance

**Files:**
- No source changes expected.

- [ ] **Step 1: Run the current scene with circumferential separation enabled**

Commit the incision, start a PullLine, and inspect the existing proxy-only label.

- [ ] **Step 2: Accept the registration chain**

For ring size `N`, require `sidewallMissing=0`, `SW=2*N`, `VW>0/0`, and `Proxy`. Require `SWPairs>0` only after SideA and SideB enter contact range. If `budgetHit` or another solver failure appears, stop at that new earliest failure and do not weaken thresholds.
