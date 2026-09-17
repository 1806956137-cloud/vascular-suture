using System.Numerics;
using System.Reflection;

internal static class NeedleCapsuleGeometryRegression
{
    public static void Run(Assembly assembly)
    {
        Type geometry = assembly.GetType("PBDLearn.NeedleCapsuleGeometry", true)!;
        MethodInfo dimensions = geometry.GetMethod("ResolveDimensions")!;
        MethodInfo centers = geometry.GetMethod("GetSphereCenters")!;
        Type vector = centers.GetParameters()[0].ParameterType;
        object V(Vector3 p) => Activator.CreateInstance(vector, p.X, p.Y, p.Z)!;
        Vector3 Read(object p) => new((float)vector.GetField("x")!.GetValue(p)!,
            (float)vector.GetField("y")!.GetValue(p)!, (float)vector.GetField("z")!.GetValue(p)!);
        int cases = 0;
        for (int direction = 0; direction < 3; direction++)
        foreach (bool sphere in new[] { false, true })
        foreach (float sign in new[] { 1f, -1f })
        {
            Vector3 scale = new(.005f);
            if (direction == 0) scale.X = .04f * sign;
            if (direction == 1) scale.Y = .04f * sign;
            if (direction == 2) scale.Z = .04f * sign;
            object[] args = { sphere ? .01f : 2f, .5f, V(scale), direction, 0f, 0f };
            dimensions.Invoke(null, args);
            float length = (float)args[4], radius = (float)args[5];
            if (MathF.Abs(length - (sphere ? .005f : .08f)) > 1e-7f || MathF.Abs(radius - .0025f) > 1e-7f)
                throw new Exception("Canonical capsule dimensions changed tip extent or radius.");
            Vector3 center = new(.12f, -.03f, .66f), axis = Vector3.Normalize(new(.8f, -.2f, .3f));
            object[] frame = { V(center), V(axis), length, radius, V(Vector3.Zero), V(Vector3.Zero) };
            centers.Invoke(null, frame);
            Vector3 c0 = Read(frame[4]), c1 = Read(frame[5]);
            if (MathF.Abs(Vector3.Distance(c0, c1) + 2f * radius - length) > 1e-7f)
                throw new Exception("Physics capsule does not cover complete travel needle.");
            foreach (int end in new[] { -1, 1 })
            {
                Vector3 semanticTip = center + axis * (end * length * .5f);
                Vector3 tipCenter = end == 1 ? c1 : c0;
                Vector3 tailCenter = end == 1 ? c0 : c1;
                if (Vector3.Distance(semanticTip, tipCenter + axis * (end * radius)) > 1e-7f ||
                    Vector3.Distance(semanticTip - axis * (end * length), tailCenter - axis * (end * radius)) > 1e-7f)
                    throw new Exception("Tip/Hist-tail/travel capsule endpoints disagree.");
            }
            cases++;
        }
        Console.WriteLine($"PASS shared capsule geometry: {cases} axis/scale/sphere cases, both needle ends; physics and travel length=0.08000 at scene scale.");
    }
}
