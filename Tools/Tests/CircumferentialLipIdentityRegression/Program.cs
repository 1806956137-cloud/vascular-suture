using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;

if (args.Length != 1)
    throw new ArgumentException("Expected the Unity project root as the only argument.");

string root = Path.GetFullPath(args[0]);
string projectPath = Path.Combine(root, "Assembly-CSharp.csproj");
string assemblyPath = Path.Combine(root, "Temp", "bin", "Debug", "Assembly-CSharp.dll");
var referencePaths = XDocument.Load(projectPath)
    .Descendants()
    .Where(element => element.Name.LocalName == "Reference")
    .Select(reference => new
    {
        Name = ((string?)reference.Attribute("Include"))?.Split(',')[0],
        HintPath = (string?)reference.Elements().FirstOrDefault(element => element.Name.LocalName == "HintPath"),
    })
    .Where(reference => !string.IsNullOrWhiteSpace(reference.Name) && !string.IsNullOrWhiteSpace(reference.HintPath))
    .ToDictionary(
        reference => reference.Name!,
        reference => Path.GetFullPath(reference.HintPath!, root),
        StringComparer.OrdinalIgnoreCase);

var loadContext = new AssemblyLoadContext("CircumferentialSidewallRegistrationRegression", isCollectible: true);
loadContext.Resolving += (context, name) =>
{
    if (!string.IsNullOrWhiteSpace(name.Name) &&
        referencePaths.TryGetValue(name.Name, out string? path) &&
        File.Exists(path))
    {
        return context.LoadFromAssemblyPath(path);
    }

    return null;
};

Assembly coreAssembly = referencePaths.TryGetValue("UnityEngine.CoreModule", out string? corePath)
    ? loadContext.LoadFromAssemblyPath(corePath)
    : throw new InvalidOperationException("UnityEngine.CoreModule reference is unavailable.");
Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

Type policy = assembly.GetType("PBDLearn.IncisionSidewallContactPolicy", throwOnError: true)!;
MethodInfo segmentMethod = policy.GetMethod("TryGetSidewallStationSegment", BindingFlags.Public | BindingFlags.Static)
    ?? throw new InvalidOperationException("Missing closed/open sidewall segment policy.");

void AssertSegment(int stationCount, bool closedLoop, int segmentIndex, bool expectedValid, int expectedStart, int expectedEnd)
{
    object[] arguments = { stationCount, closedLoop, segmentIndex, 0, 0 };
    bool valid = (bool)segmentMethod.Invoke(null, arguments)!;
    if (valid != expectedValid)
        throw new InvalidOperationException($"Unexpected segment validity count={stationCount} closed={closedLoop} segment={segmentIndex}.");
    if (valid && ((int)arguments[3] != expectedStart || (int)arguments[4] != expectedEnd))
        throw new InvalidOperationException($"Expected {expectedStart}->{expectedEnd}, got {arguments[3]}->{arguments[4]}.");
}

AssertSegment(2, true, 0, false, -1, -1);
AssertSegment(3, true, 0, true, 0, 1);
AssertSegment(3, true, 1, true, 1, 2);
AssertSegment(3, true, 2, true, 2, 0);
AssertSegment(3, false, 0, true, 0, 1);
AssertSegment(3, false, 1, true, 1, 2);
AssertSegment(3, false, 2, false, -1, -1);

MethodInfo alignmentMethod = policy.GetMethod(
    "ValidateCircumferentialOrdinalAlignment",
    BindingFlags.Public | BindingFlags.Static)
    ?? throw new InvalidOperationException("Missing shared-seam ordinal alignment policy.");

bool ValidateAngles(float[] outer, float[] inner, out string reason)
{
    object[] arguments = { outer, inner, string.Empty };
    bool valid = (bool)alignmentMethod.Invoke(null, arguments)!;
    reason = (string)arguments[2];
    return valid;
}

if (!ValidateAngles(new[] { 0.02f, 2.10f, 4.20f }, new[] { 0.03f, 2.11f, 4.19f }, out string validReason))
    throw new InvalidOperationException($"Aligned shared-seam rings were rejected: {validReason}");
if (ValidateAngles(new[] { 0.02f, 2.10f, 4.20f }, new[] { 2.11f, 4.19f, 0.03f }, out _))
    throw new InvalidOperationException("Cyclically shifted ordinals were accepted.");
if (ValidateAngles(new[] { 0.02f, 2.10f, 4.20f }, new[] { 4.19f, 2.11f, 0.03f }, out _))
    throw new InvalidOperationException("Reversed ordinals were accepted.");

Type generator = assembly.GetType("PBDLearn.DoubleLayerIncisionGenerator", throwOnError: true)!;
Type cutType = generator.GetNestedType("IncisionCutType", BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("GenerateResult must expose an authoritative incision cut type.");
Type generateResultType = generator.GetNestedType("GenerateResult", BindingFlags.NonPublic)!;
FieldInfo cutTypeField = generateResultType.GetField("cutType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("GenerateResult.cutType is missing.");
if (cutTypeField.FieldType != cutType)
    throw new InvalidOperationException("GenerateResult.cutType uses the wrong enum.");

Type layerType = generator.GetNestedType("LayerResult", BindingFlags.NonPublic)!;
FieldInfo collisionStationsField = layerType.GetField(
    "circumferentialCollisionStations",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("LayerResult lacks dedicated circumferential collision stations.");
Type stationType = generator.GetNestedType("CircumferentialCollisionStation", BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing circumferential collision station type.");
MethodInfo collisionBuild = generator.GetMethod(
    "BuildCircumferentialCollisionStations",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing unique collision-station builder.");

Type vectorType = coreAssembly.GetType("UnityEngine.Vector3", throwOnError: true)!;
Type vectorListType = typeof(List<>).MakeGenericType(vectorType);
ConstructorInfo vectorConstructor = vectorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) })!;
IList NewBoundary(float z)
{
    var list = (IList)Activator.CreateInstance(vectorListType)!;
    list.Add(vectorConstructor.Invoke(new object[] { 1f, 0f, z }));
    list.Add(vectorConstructor.Invoke(new object[] { -0.5f, 0.8660254f, z }));
    list.Add(vectorConstructor.Invoke(new object[] { -0.5f, -0.8660254f, z }));
    list.Add(vectorConstructor.Invoke(new object[] { 1f, 0f, z }));
    return list;
}

object layer = Activator.CreateInstance(layerType, nonPublic: true)!;
collisionBuild.Invoke(null, new object[]
{
    layer,
    NewBoundary(-0.1f),
    NewBoundary(0.1f),
    new[] { 11, 12, 13 },
    new[] { 21, 22, 23 },
});
IList collisionStations = (IList)collisionStationsField.GetValue(layer)!;
if (collisionStations.Count != 3)
    throw new InvalidOperationException($"A 4-point closed display polyline must produce 3 collision stations, got {collisionStations.Count}.");

FieldInfo lowerIndex = stationType.GetField("lowerVertexIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
FieldInfo upperIndex = stationType.GetField("upperVertexIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
for (int i = 0; i < collisionStations.Count; i++)
{
    if ((int)lowerIndex.GetValue(collisionStations[i])! != 11 + i ||
        (int)upperIndex.GetValue(collisionStations[i])! != 21 + i)
        throw new InvalidOperationException($"Collision station {i} lost its topology identity.");
}

Type driver = assembly.GetType("PBDLearn.ClothSimulationDriverDoubleLayer", throwOnError: true)!;
if (driver.GetField("_committedIncisionCutType", BindingFlags.Instance | BindingFlags.NonPublic) == null)
    throw new InvalidOperationException("Driver does not persist the committed incision cut type.");
if (driver.GetField("_outerCircumferentialSidewallStations", BindingFlags.Instance | BindingFlags.NonPublic) == null ||
    driver.GetField("_innerCircumferentialSidewallStations", BindingFlags.Instance | BindingFlags.NonPublic) == null)
    throw new InvalidOperationException("Driver lacks dedicated committed Outer/Inner collision rings.");

Type edgeType = assembly.GetType("PBDLearn.SutureSidewallBoundaryEdgeInfo", throwOnError: true)!;
if (edgeType.GetField("stationIndex") == null)
    throw new InvalidOperationException("Sidewall edges do not retain their station ordinal.");
Type contactTriangleType = assembly.GetType("PBDLearn.SutureLineContactTriangleInfo", throwOnError: true)!;
if (contactTriangleType.GetField("stationIndex") == null)
    throw new InvalidOperationException("Virtual sidewall contact triangles do not retain the source station ordinal.");
Type receiptType = assembly.GetType("PBDLearn.SutureSidewallRegistrationResult", throwOnError: true)!;
foreach (string fieldName in new[] { "requested", "accepted", "runtimeActive" })
{
    if (receiptType.GetField(fieldName) == null)
        throw new InvalidOperationException($"Registration receipt lacks {fieldName}.");
}

Type simulator = assembly.GetType("PBDLearn.ClothSimulator", throwOnError: true)!;
MethodInfo setter = simulator.GetMethod("SetSutureSidewallBoundaryEdges", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Simulator sidewall setter is missing.");
if (setter.ReturnType != receiptType)
    throw new InvalidOperationException("Simulator sidewall setter does not return the registration receipt.");

MethodInfo rangePolicy = simulator.GetMethod(
    "IsSutureSidewallBoundaryEdgeInLayerRanges",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Simulator lacks explicit Outer/Inner layer-range validation.");
object validEdge = Activator.CreateInstance(edgeType)!;
edgeType.GetField("outerA")!.SetValue(validEdge, 0);
edgeType.GetField("outerB")!.SetValue(validEdge, 1);
edgeType.GetField("innerA")!.SetValue(validEdge, 10);
edgeType.GetField("innerB")!.SetValue(validEdge, 11);
edgeType.GetField("side")!.SetValue(validEdge, (byte)1);
edgeType.GetField("enabled")!.SetValue(validEdge, (byte)1);
if (!(bool)rangePolicy.Invoke(null, new[] { validEdge, (object)10, 20 })!)
    throw new InvalidOperationException("A correctly offset Outer/Inner edge was rejected.");

object localInnerEdge = validEdge;
edgeType.GetField("innerA")!.SetValue(localInnerEdge, 0);
edgeType.GetField("innerB")!.SetValue(localInnerEdge, 1);
if ((bool)rangePolicy.Invoke(null, new[] { localInnerEdge, (object)10, 20 })!)
    throw new InvalidOperationException("Un-offset Inner local indices were accepted as Simulator globals.");

string driverSource = File.ReadAllText(Path.Combine(root, "Assets", "PBDLearn", "Scripts", "Cloth", "ClothSimulationDriverDoubleLayer.cs"));
int rebuildStart = driverSource.IndexOf("RebuildSutureSideGuidesFromCurrentIncisionState", StringComparison.Ordinal);
int rebuildEnd = driverSource.IndexOf("BuildSutureSidewallBoundaryEdges", rebuildStart, StringComparison.Ordinal);
if (rebuildStart < 0 || rebuildEnd <= rebuildStart)
    throw new InvalidOperationException("Unable to audit the side-guide rebuild source.");
string rebuildBody = driverSource.Substring(rebuildStart, rebuildEnd - rebuildStart);
if (rebuildBody.Contains("HasAnyIncisionPairs", StringComparison.Ordinal))
    throw new InvalidOperationException("Side-guide rebuild is still gated by legacy incision pairs.");
if (!rebuildBody.Contains("circumferential-side-semantic-mismatch", StringComparison.Ordinal))
    throw new InvalidOperationException("Outer/Inner circumferential side semantics are not independently validated.");
if (!driverSource.Contains("result.cutType != DoubleLayerIncisionGenerator.IncisionCutType.CircumferentialSeparation", StringComparison.Ordinal))
    throw new InvalidOperationException("Circumferential commit can still enter legacy geometric-collapse behavior.");
if (!driverSource.Contains("_outerCircumferentialSidewallStations.AddRange", StringComparison.Ordinal) ||
    !driverSource.Contains("_innerCircumferentialSidewallStations.AddRange", StringComparison.Ordinal))
    throw new InvalidOperationException("Committed circumferential collision rings are not captured independently.");

int middleWallStart = driverSource.IndexOf("private void UpdateMiddleWallFillMesh()", StringComparison.Ordinal);
int middleWallEnd = driverSource.IndexOf("private void UpdateIncisionMarkerMesh()", middleWallStart, StringComparison.Ordinal);
if (middleWallStart < 0 || middleWallEnd <= middleWallStart)
    throw new InvalidOperationException("Unable to audit middle-wall render isolation.");
string middleWallBody = driverSource.Substring(middleWallStart, middleWallEnd - middleWallStart);
if (middleWallBody.Contains("CircumferentialSidewall", StringComparison.Ordinal))
    throw new InvalidOperationException("Middle-wall rendering depends on collision-only circumferential state.");

string jobsSource = File.ReadAllText(Path.Combine(root, "Assets", "PBDLearn", "Scripts", "Cloth", "ClothJobs.cs"));
string simulatorSource = File.ReadAllText(Path.Combine(root, "Assets", "PBDLearn", "Scripts", "Cloth", "ClothSimulator.cs"));
string settingSource = File.ReadAllText(Path.Combine(root, "Assets", "PBDLearn", "Scripts", "Cloth", "ClothSetting.cs"));

if (!jobsSource.Contains("public struct SutureLineEversionMeasureJob", StringComparison.Ordinal) ||
    !jobsSource.Contains("FlagOuterLayer", StringComparison.Ordinal) ||
    !jobsSource.Contains("FlagInnerLayer", StringComparison.Ordinal))
    throw new InvalidOperationException("The read-only double-layer eversion measurement or explicit layer identity is missing.");
if (!jobsSource.Contains("float3 guidePoint = (pointA + pointB) * 0.5f + outward * height", StringComparison.Ordinal) ||
    !jobsSource.Contains("float ropeRest = 2f * math.sqrt", StringComparison.Ordinal) ||
    !jobsSource.Contains("gradientA = segmentA / lengthA", StringComparison.Ordinal) ||
    !jobsSource.Contains("gradientB = segmentB / lengthB", StringComparison.Ordinal))
    throw new InvalidOperationException("PullLine is not solved as an outward V-shaped guided rope.");
if (!jobsSource.Contains("math.max(0f, solveCompliance) / (dt * dt)", StringComparison.Ordinal) ||
    !jobsSource.Contains("-solveMaxCorrection / maxInvWeight", StringComparison.Ordinal))
    throw new InvalidOperationException("Guided Pull does not honor XPBD compliance and per-iteration correction limits.");
if (!jobsSource.Contains("priorityClosure ? guidedStiffness : stiffness", StringComparison.Ordinal) ||
    !settingSource.Contains("_sutureLineGuidedPullCompliance", StringComparison.Ordinal))
    throw new InvalidOperationException("Mandatory closure does not retain the strong guided-pull material.");
if (!jobsSource.Contains("bool eversionComplete", StringComparison.Ordinal) ||
    !jobsSource.Contains("c.guidedPullEnabled != 0 && !eversionComplete", StringComparison.Ordinal) ||
    !simulatorSource.Contains("pullMode={pullMode}", StringComparison.Ordinal))
    throw new InvalidOperationException("Completed eversion does not hand off from the V-guide to direct A-B closure.");
if (jobsSource.Contains("c.guidedPullRequired != 0 && c.guidedPullEnabled == 0", StringComparison.Ordinal) ||
    !jobsSource.Contains("if (!guidedPull)", StringComparison.Ordinal))
    throw new InvalidOperationException("Invalid eversion guidance can still disable the mandatory direct closure contract.");
if (!simulatorSource.Contains("!IsHistoryPortalOwnedBySutureLine(entry)", StringComparison.Ordinal))
    throw new InvalidOperationException("PullLine-owned Hist portals are still constrained by the legacy axial hold.");
if (!jobsSource.Contains("ConstraintType.SuturePull", StringComparison.Ordinal) ||
    !simulatorSource.Contains("RefreshSuturePullMotionFlags", StringComparison.Ordinal))
    throw new InvalidOperationException("Active WholeWall PullLine vertices can still be erased by generic sleep.");
if (!jobsSource.Contains("GetSemanticEversionSign(c.endpointSideA)", StringComparison.Ordinal) ||
    !jobsSource.Contains("side == SutureSidewallBoundaryEdgeInfo.SideB ? -1f : 1f", StringComparison.Ordinal))
    throw new InvalidOperationException("SideA/SideB eversion signs are not normalized to physical outward motion.");
if (jobsSource.Contains("ApplyLayerCorrection", StringComparison.Ordinal))
    throw new InvalidOperationException("The removed artificial eversion force is still present.");
if (jobsSource.Contains("SutureLineCompressionFoldJob", StringComparison.Ordinal) ||
    simulatorSource.Contains("ClosureBandSlide", StringComparison.Ordinal) ||
    settingSource.Contains("Suture Compression Fold", StringComparison.Ordinal))
    throw new InvalidOperationException("Legacy fold/slide execution paths still contaminate the eversion chain.");

if (!jobsSource.Contains("public struct SutureSidewallPointContactXPBDJob", StringComparison.Ordinal) ||
    !jobsSource.Contains("float signedGap = math.dot(pp - closest, semanticNormal)", StringComparison.Ordinal) ||
    !jobsSource.Contains("if (C >= 0f)", StringComparison.Ordinal))
    throw new InvalidOperationException("Virtual sidewall is not a semantic unilateral XPBD barrier.");
if (!simulatorSource.Contains("activeBarrierContacts", StringComparison.Ordinal) ||
    !simulatorSource.Contains("maxBarrierLambda", StringComparison.Ordinal))
    throw new InvalidOperationException("Virtual sidewall barrier feedback is not exposed to the suture state machine.");
if (simulatorSource.Contains("AddSutureSidewallSurfaceDistanceConstraint(edge.outerA, edge.innerB", StringComparison.Ordinal) ||
    simulatorSource.Contains("AddSutureSidewallSurfaceDistanceConstraint(edge.outerB, edge.innerA", StringComparison.Ordinal))
    throw new InvalidOperationException("Virtual sidewall diagonals still lock the shear/rotation needed for eversion.");

int solverStart = simulatorSource.IndexOf("private JobHandle StartConstraintsSolveJob", StringComparison.Ordinal);
int solverEnd = simulatorSource.IndexOf("private JobHandle StartUpdateVelocitiesAndPositions", solverStart, StringComparison.Ordinal);
if (solverStart < 0 || solverEnd <= solverStart)
    throw new InvalidOperationException("Unable to audit the solver ordering.");
string solverBody = simulatorSource.Substring(solverStart, solverEnd - solverStart);
int pullOrder = solverBody.IndexOf("StartSutureLineConstraintJob", StringComparison.Ordinal);
int shapeOrder = solverBody.IndexOf("StartSutureSidewallSurfaceDistanceConstraintsJob", StringComparison.Ordinal);
int barrierOrder = solverBody.IndexOf("StartSutureSidewallContactConstraintsJob", StringComparison.Ordinal);
int measureOrder = solverBody.IndexOf("StartSutureLineEversionMeasureJob", StringComparison.Ordinal);
int finalProjectionOrder = solverBody.LastIndexOf("StartFinalConstraintsJob", StringComparison.Ordinal);
int thicknessOrder = solverBody.LastIndexOf("StartXPBDCrossLayerThicknessJob", StringComparison.Ordinal);
int separationOrder = solverBody.IndexOf("StartLayerSeparationJob", StringComparison.Ordinal);
if (shapeOrder < 0 || thicknessOrder <= shapeOrder || separationOrder <= thicknessOrder ||
    pullOrder <= separationOrder || barrierOrder <= pullOrder || finalProjectionOrder <= barrierOrder ||
    measureOrder <= finalProjectionOrder)
    throw new InvalidOperationException("Expected recoverable shape/thickness/separation -> priority PullLine -> hard barrier/final projection -> read-only measure ordering.");
if (!simulatorSource.Contains("[Suture][ClosureProgress]", StringComparison.Ordinal) ||
    !simulatorSource.Contains("closureStallFrames=", StringComparison.Ordinal))
    throw new InvalidOperationException("Net closure progress/cancellation is not exposed by the PullLine diagnostics.");
if (!simulatorSource.Contains("StartFinalConstraintsJob(jobHandle, i, hardSafetyOnly: true)", StringComparison.Ordinal) ||
    !jobsSource.Contains("softClampPin && hardSafetyOnly == 0 && !activeSuturePull", StringComparison.Ordinal) ||
    !jobsSource.Contains("hardSafetyOnly == 0 && !activeSuturePull && enableNeedleTipIndentation", StringComparison.Ordinal) ||
    !jobsSource.Contains("!softClampPin && !activeSuturePull", StringComparison.Ordinal))
    throw new InvalidOperationException("An active PullLine can still be cancelled by recoverable soft clamp or needle indentation corrections.");
if (!jobsSource.Contains("public struct SutureLineClosureAuditJob", StringComparison.Ordinal) ||
    !simulatorSource.Contains("afterPull=", StringComparison.Ordinal) ||
    !simulatorSource.Contains("afterSafety=", StringComparison.Ordinal) ||
    !simulatorSource.Contains("hardPinBlockers=", StringComparison.Ordinal) ||
    !simulatorSource.Contains("collisionBlockers=", StringComparison.Ordinal))
    throw new InvalidOperationException("PullLine closure is missing after-pull/after-safety cancellation diagnostics.");

Type constraintInfoType = assembly.GetType("PBDLearn.SutureLineConstraintInfo", throwOnError: true)!;
Type simulatorType = assembly.GetType("PBDLearn.ClothSimulator", throwOnError: true)!;
MethodInfo eversionReadyMethod = simulatorType.GetMethod(
    "IsSutureLineEversionReady",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Missing eversion completion contract.");

object MakeEversionState(byte enabled, int reject, float target, float measureA, float measureB)
{
    object state = Activator.CreateInstance(constraintInfoType)!;
    constraintInfoType.GetField("guidedPullEnabled")!.SetValue(state, enabled);
    constraintInfoType.GetField("eversionRejectReason")!.SetValue(state, reject);
    constraintInfoType.GetField("eversionTarget")!.SetValue(state, target);
    constraintInfoType.GetField("eversionMeasureA")!.SetValue(state, measureA);
    constraintInfoType.GetField("eversionMeasureB")!.SetValue(state, measureB);
    return state;
}

bool IsEversionReady(object state) => (bool)eversionReadyMethod.Invoke(null, new[] { state })!;
if (IsEversionReady(MakeEversionState(0, 0, 0.01f, 0.01f, 0.01f)))
    throw new InvalidOperationException("Disabled/invalid eversion was accepted.");
if (IsEversionReady(MakeEversionState(1, 0, 0.01f, 0.007f, 0.01f)))
    throw new InvalidOperationException("One under-everted side was accepted.");
if (!IsEversionReady(MakeEversionState(1, 0, 0.01f, 0.008f, 0.008f)))
    throw new InvalidOperationException("Both-layer, both-side eversion target was not accepted.");

Console.WriteLine("PASS: guided V-rope PullLine, joint Outer/Inner eversion measurement, and flexible unilateral VirtualSidewall contracts are present.");
