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

var loadContext = new AssemblyLoadContext("AutoSutureWorkflowAdvanceRegression", isCollectible: true);
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

if (referencePaths.TryGetValue("UnityEngine.CoreModule", out string? corePath))
    loadContext.LoadFromAssemblyPath(corePath);
else
    throw new InvalidOperationException("UnityEngine.CoreModule reference is unavailable.");

Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
Type simulatorType = assembly.GetType("PBDLearn.ClothSimulator", throwOnError: true)!;
Type constraintType = assembly.GetType("PBDLearn.SutureLineConstraintInfo", throwOnError: true)!;
Type controllerType = assembly.GetType("PBDLearn.AutoSutureRuntimeController", throwOnError: true)!;
NeedleCapsuleGeometryRegression.Run(assembly);
PostHistExitRegression.Run(controllerType);
ExitContactAuditRegression.Run(controllerType);
ExitChannelContactRegression.Run(controllerType);
LumenPoseRouteRegression.Run(controllerType);
ExteriorNeedleRouteRegression.Run(controllerType);
LocalTissueRouteRegression.Run(controllerType, root);
DeformedVesselRouteRegression.Run(controllerType);
PuncturePoseRegression.Run(controllerType);
const BindingFlags StaticInternal = BindingFlags.NonPublic | BindingFlags.Static;

MethodInfo advanceReady = simulatorType.GetMethod(
    "IsSutureLineWorkflowAdvanceReadyCandidate",
    StaticInternal) ?? throw new InvalidOperationException("Missing workflow advance predicate.");
MethodInfo physicalReady = simulatorType.GetMethod(
    "IsSutureLineWorkflowReadyCandidate",
    StaticInternal) ?? throw new InvalidOperationException("Missing physical workflow predicate.");
MethodInfo release = simulatorType.GetMethod(
    "ApplyAutoSutureWorkflowRelease",
    StaticInternal) ?? throw new InvalidOperationException("Missing workflow release transition.");
MethodInfo bodyClearance = controllerType.GetMethod(
    "ResolveNeedleBodyTravelClearance",
    StaticInternal) ?? throw new InvalidOperationException("Missing whole-needle travel clearance policy.");

AssertFloatResult(bodyClearance, new object[] { 0.07f, 0.08f, 0.0025f, 0.005f }, 0.0875f);
AssertFloatResult(bodyClearance, new object[] { 0.10f, 0.08f, 0.0025f, 0.005f }, 0.10f);

object[] fifthStitchClosure =
{
    0.009028f,
    0.006f,
    0.010506f,
    0.003028f,
    0.006f,
    0.004f,
    0.009631f,
};
if (!(bool)advanceReady.Invoke(null, fifthStitchClosure)!)
    throw new InvalidOperationException("The measured P9/P10 WholeWall closure was not AdvanceReady.");

object[] noContactEvidence =
{
    0.009028f,
    0.006f,
    0.010506f,
    0.003028f,
    0.006f,
    0.004f,
    0.009631f,
    false,
    false,
};
if ((bool)physicalReady.Invoke(null, noContactEvidence)!)
    throw new InvalidOperationException("No-contact closure was incorrectly marked physically ready.");

object constraint = Activator.CreateInstance(constraintType)!;
SetByte(constraint, "workflowAdvanceReady", 1);
SetByte(constraint, "workflowAdvanceReadyFrames", 6);
SetByte(constraint, "reserved0", GetConstByte(constraintType, "LifecycleActive"));
SetFloat(constraint, "currentRestDistance", 0.006f);
SetFloat(constraint, "targetRestDistance", 0.010506f);
object[] releaseArgs = { constraint, 0.009028f };
release.Invoke(null, releaseArgs);
constraint = releaseArgs[0];

AssertByte(constraint, "workflowReleased", 1);
AssertByte(constraint, "contactLatched", 1);
AssertByte(constraint, "restFrozen", 1);
AssertByte(constraint, "reserved0", GetConstByte(constraintType, "LifecycleSettledHold"));
AssertByte(constraint, "completedWithoutRealSidewallContact", 1);
AssertFloat(constraint, "currentRestDistance", 0.010506f);

object noContactConstraint = constraint;
releaseArgs = new[] { noContactConstraint, (object)0.009028f };
release.Invoke(null, releaseArgs);
noContactConstraint = releaseArgs[0];
AssertByte(noContactConstraint, "completedWithoutRealSidewallContact", 1);

object contactConstraint = Activator.CreateInstance(constraintType)!;
SetByte(contactConstraint, "workflowAdvanceReady", 1);
SetByte(contactConstraint, "workflowAdvanceReadyFrames", 6);
SetByte(contactConstraint, "workflowReady", 1);
SetByte(contactConstraint, "reserved0", GetConstByte(constraintType, "LifecycleActive"));
SetFloat(contactConstraint, "currentRestDistance", 0.010506f);
SetFloat(contactConstraint, "targetRestDistance", 0.010506f);
releaseArgs = new[] { contactConstraint, (object)0.009028f };
release.Invoke(null, releaseArgs);
contactConstraint = releaseArgs[0];
AssertByte(contactConstraint, "completedWithoutRealSidewallContact", 0);
AssertCompletionStateMatchesExceptAnnotation(constraint, contactConstraint);

Console.WriteLine("Auto suture workflow advance regression passed.");

static byte GetConstByte(Type type, string name)
{
    FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Missing constant {name}.");
    return (byte)field.GetRawConstantValue()!;
}

static void SetByte(object value, string name, byte fieldValue)
{
    FieldInfo field = value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Missing field {name}.");
    field.SetValue(value, fieldValue);
}

static void SetFloat(object value, string name, float fieldValue)
{
    FieldInfo field = value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Missing field {name}.");
    field.SetValue(value, fieldValue);
}

static void AssertByte(object value, string name, byte expected)
{
    FieldInfo field = value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Missing field {name}.");
    byte actual = (byte)field.GetValue(value)!;
    if (actual != expected)
        throw new InvalidOperationException($"Expected {name}={expected}, got {actual}.");
}

static void AssertFloat(object value, string name, float expected)
{
    FieldInfo field = value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Missing field {name}.");
    float actual = (float)field.GetValue(value)!;
    if (MathF.Abs(actual - expected) > 1e-7f)
        throw new InvalidOperationException($"Expected {name}={expected}, got {actual}.");
}

static void AssertFloatResult(MethodInfo method, object[] args, float expected)
{
    float actual = (float)method.Invoke(null, args)!;
    if (MathF.Abs(actual - expected) > 1e-7f)
        throw new InvalidOperationException($"Expected {method.Name}={expected}, got {actual}.");
}

static void AssertCompletionStateMatchesExceptAnnotation(object expected, object actual)
{
    foreach (FieldInfo field in expected.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        if (field.Name == "completedWithoutRealSidewallContact")
            continue;

        object? expectedValue = field.GetValue(expected);
        object? actualValue = field.GetValue(actual);
        if (!Equals(expectedValue, actualValue))
        {
            throw new InvalidOperationException(
                $"Expected completion field {field.Name}={expectedValue}, got {actualValue}.");
        }
    }
}
