using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class PuncturePoseRegression
{
    public static void Run(Type controllerType)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo evaluate = controllerType.GetMethod("EvaluatePuncturePath", instance)!;
        MethodInfo validate = controllerType.GetMethod("ValidateAutomaticPunctureDirections", statics)!;
        Type passType = evaluate.GetParameters()[0].ParameterType;
        Type vectorType = evaluate.GetParameters()[2].ParameterType.GetElementType()!;
        object V(Vector3 v) => Activator.CreateInstance(vectorType, v.X, v.Y, v.Z)!;
        Vector3 Read(object v) => new((float)vectorType.GetField("x")!.GetValue(v)!,
            (float)vectorType.GetField("y")!.GetValue(v)!, (float)vectorType.GetField("z")!.GetValue(v)!);
        void Set(object obj, string name, object value) => obj.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(obj, value);
        Type directionType = passType.GetField("direction")!.FieldType;
        // These methods are pure geometry; no Unity native scene is needed.
        object controller = RuntimeHelpers.GetUninitializedObject(controllerType);
        float[] angles = { 90f, 210f, 330f, 150f, 270f, 30f };
        int paths = 0;
        foreach (string direction in new[] { "OuterToInner", "InnerToOuter" })
        foreach (float angle in angles)
        {
            Vector3 radial = new(MathF.Cos(angle * MathF.PI / 180f), MathF.Sin(angle * MathF.PI / 180f), 0f);
            Vector3 outer = new Vector3(0f, .16f, .65f) + radial * .169f;
            Vector3 inner = new Vector3(0f, .16f, .65f) + radial * .152f;
            Vector3 entry = direction == "OuterToInner" ? outer : inner;
            Vector3 exit = direction == "OuterToInner" ? inner : outer;
            Vector3 forward = Vector3.Normalize(exit - entry);
            object pass = Activator.CreateInstance(passType)!;
            Set(pass, "direction", Enum.Parse(directionType, direction));
            Set(pass, "entryPointWorld", V(entry)); Set(pass, "exitPointWorld", V(exit));
            Set(pass, "entryDirectionWorld", V(forward)); Set(pass, "exitDirectionWorld", V(forward));
            Set(controller, "_punctureStartTip", V(entry - forward * .018f));
            Set(controller, "_puncturePostExit", V(exit + forward * .018f));
            // Toggle the real controller setting; it must not affect any
            // endpoint or wall sample, even for the bottom-side pass.
            foreach (bool avoidance in new[] { false, true })
            {
                Set(controller, "avoidTissueBackground", avoidance);
                Array sampled = Array.CreateInstance(vectorType, 129);
                for (int i = 0; i <= 128; i++)
                {
                    object[] args = { pass, i / 128f, V(Vector3.Zero), V(Vector3.Zero) };
                    evaluate.Invoke(controller, args);
                    Vector3 tip = Read(args[2]), actualForward = Read(args[3]);
                    Vector3 offset = tip - entry;
                    Vector3 lateral = offset - forward * Vector3.Dot(offset, forward);
                    if (Vector3.Dot(actualForward, forward) < .999999f || lateral.Length() > 1e-6f)
                        throw new Exception($"Transport pose leaked into {direction} puncture: station={angle} sample={i}");
                    sampled.SetValue(args[3], i);
                }
                object[] validation = { pass, sampled, "" };
                if (!(bool)validate.Invoke(null, validation)!)
                    throw new Exception($"Straight puncture rejected: {validation[2]}");
                {
                    // Recreate the reported failure: exterior alignment near
                    // the vessel axis while the Inner exit retains its radial
                    // direction. The new pre-entry contract must detect it.
                    sampled.SetValue(V(Vector3.UnitZ), 0);
                    sampled.SetValue(V(Vector3.Normalize(forward + Vector3.UnitZ)), 64);
                    validation = new object[] { pass, sampled, "" };
                    if ((bool)validate.Invoke(null, validation)!)
                        throw new Exception("Transport-to-radial wall rotation escaped the pre-entry guard.");
                }
                paths++;
            }
        }
        Console.WriteLine($"PASS puncture posture separation: {paths} production paths / 3096 samples, both pass directions, avoidance on/off, transport-bend rejection.");
    }
}
