using System.Collections;
using System.Numerics;
using System.Reflection;

internal static class ExitChannelContactRegression
{
    public static void Run(Type controller)
    {
        Type surface = controller.GetNestedType("VesselSurfaceSnapshot", BindingFlags.NonPublic)!;
        MethodInfo capture = surface.GetMethod("CaptureExitChannelContacts")!;
        MethodInfo plan = surface.GetMethod("TryPlanAxialExit")!;
        MethodInfo retire = surface.GetMethod("RetireClearedExitContacts")!;
        Type vector = plan.GetParameters()[0].ParameterType;
        object V(Vector3 p) => Activator.CreateInstance(vector, p.X, p.Y, p.Z)!;
        // Rounded local geometry recorded by the actual pair=3 P8 failure.
        // Both faces contacted the needle before Hist; tri 503 shares an edge
        // with exit 502, but the former point-distance gate rejected it.
        Vector3[] points = { new(-.1869f, .2087f, .6769f), new(-.1899f, .1628f, .6766f),
            new(-.1860f, .2086f, .7365f), new(-.1844f, .1638f, .7357f) };
        int[] indices = { 0, 1, 2, 1, 3, 2 };
        Vector3 tip = new(-.2665f, .1497f, .6879f);
        Vector3 forward = Vector3.Normalize(new(-.9675f, -.2436f, .0682f));
        Vector3 exitPoint = new(-.1900f, .1716f, .6824f);
        object Mesh(Vector3[] vertices, int[] triangles)
        {
            Array array = Array.CreateInstance(vector, vertices.Length);
            for (int i = 0; i < vertices.Length; i++) array.SetValue(V(vertices[i]), i);
            return Activator.CreateInstance(surface, array, triangles)!;
        }
        object mesh = Mesh(points, indices);
        object Capture(object source, int outerCount = 2, float support = .0075f) => capture.Invoke(source,
            new object[] { V(tip), V(forward), .08f, .0025f, .00025f, V(exitPoint), 0, outerCount, support })!;
        object[] Args(object? channel) => new object[] { V(tip), V(forward), V(exitPoint), V(forward),
            .08f, .0025f, .00025f, .012f, .08f, null!, .008f, 0f, "", .0025f, channel! };
        object channel = Capture(mesh);
        var contactIds = (IEnumerable)channel.GetType().GetField("triangles")!.GetValue(channel)!;
        if (contactIds.Cast<int>().Order().SequenceEqual(new[] { 0, 1 }) == false)
            throw new Exception("Original connected exit contacts were not captured.");
        object[] good = Args(channel);
        if (!(bool)plan.Invoke(mesh, good)! || (float)good[11] < .012f)
            throw new Exception($"Recorded adjacent exit patch could not depart: {good[12]}");
        void Reject(object target, object[] args, string scenario)
        {
            if ((bool)plan.Invoke(target, args)!) throw new Exception($"Unsafe channel inheritance: {scenario}");
        }
        Reject(mesh, Args(null), "missing contact provenance");
        Reject(mesh, Args(Capture(mesh, 1)), "other wall layer");
        Reject(mesh, Args(Capture(mesh, 2, .00275f)), "outside original portal support");
        object clearPrevious = Mesh(points.Select(p => p - forward * .01f).ToArray(), indices);
        Reject(mesh, Args(Capture(clearPrevious)), "newly appearing contact after Hist");
        // Geometric proximity must not substitute for shared material edges.
        Vector3[] separate = points.Concat(new[] { points[1], points[2] }).ToArray();
        object disconnected = Mesh(separate, new[] { 0, 1, 2, 4, 3, 5 });
        Reject(disconnected, Args(Capture(disconnected)), "geometrically coincident but disconnected face");
        object vertexOnly = Mesh(separate, new[] { 0, 1, 2, 1, 3, 5 });
        Reject(vertexOnly, Args(Capture(vertexOnly)), "shared vertex without a shared edge");
        object[] deepening = Args(Capture(mesh));
        deepening[0] = V(tip - forward * .08f); deepening[1] = V(-forward); deepening[3] = V(-forward);
        Reject(mesh, deepening, "deeper movement into an inherited contact");
        // Retiring contacts uses only the currently achieved pose. A future
        // preflight must not discard contacts needed during actual departure.
        retire.Invoke(mesh, new object[] { channel, V(tip + forward * .02f), V(forward), .08f, .0025f, .00025f });
        Reject(mesh, Args(channel), "recontact after verified separation");
        Reject(vertexOnly, Args(Capture(mesh)), "triangle identity changed after capture");
        Console.WriteLine("PASS exit channel provenance: recorded P8 shared-edge patch departs; missing/new/disconnected/other-layer/out-of-support/deepening/recontact/changed-identity cases remain blocked.");
    }
}
