using System.Numerics;
using System.Reflection;

internal static class ExitContactAuditRegression
{
    public static void Run(Type controller)
    {
        Type surfaceType = controller.GetNestedType("VesselSurfaceSnapshot", BindingFlags.NonPublic)!;
        MethodInfo audit = surfaceType.GetMethod("DescribeExitContactHandoff")!;
        MethodInfo plan = surfaceType.GetMethod("TryPlanAxialExit")!;
        MethodInfo capture = surfaceType.GetMethod("CaptureExitChannelContacts")!;
        Type vector = audit.GetParameters()[0].ParameterType;
        object V(Vector3 p) => Activator.CreateInstance(vector, p.X, p.Y, p.Z)!;
        object Mesh(float z, bool changedIdentity = false)
        {
            Vector3[] p = { new(-.01f, 0, z), new(.01f, 0, z), new(0, .01f, z), new(0, -.01f, z),
                new(.02f, .001f, z), new(.02f, -.001f, z), new(-.01f, 0, z), new(.01f, 0, z), new(0, .01f, z) };
            int[] indices = { 0, 1, 2, 1, 0, 3, 0, 4, 5, 6, 7, 8 };
            if (changedIdentity) for (int t = 0; t < 4; t++) (indices[t * 3], indices[t * 3 + 1]) = (indices[t * 3 + 1], indices[t * 3]);
            Array points = Array.CreateInstance(vector, p.Length);
            for (int i = 0; i < p.Length; i++) points.SetValue(V(p[i]), i);
            return Activator.CreateInstance(surfaceType, points, indices)!;
        }
        object mesh = Mesh(0f);
        object[] Args(object? previous) => new object[] { V(new(0, 0, .0788f)), V(Vector3.UnitZ), .08f, .0025f, .00025f,
            V(new(0, .008f, 0)), 0, 3, .001f, previous!, V(new(0, 0, .078f)), V(Vector3.UnitZ) };
        string Report(object? previous) => (string)audit.Invoke(mesh, Args(previous))!;
        void MustContain(string report, params string[] pieces)
        {
            foreach (string piece in pieces) if (!report.Contains(piece))
                throw new Exception($"Missing exit-contact audit evidence '{piece}': {report}");
        }
        string existing = Report(Mesh(0f));
        MustContain(existing, "count=4", "byRelation=[exit:1,edge:1,vertex:1,none:1]", "otherLayer=1", "alreadyCurrent=4",
            "pointGateRejected=2", "trend=[improving:4,deepening:0,unchanged:0]", "previewCount=4");
        MustContain(Report(Mesh(-.003f)), "newSinceCurrent=4", "meshOnlyDelta=-0.003000");
        MustContain(Report(null), "unknownPrevious=4");
        MustContain(Report(Mesh(0f, true)), "unknownPrevious=4");
        // An adjacent contact rejected by the point gate is evidence to review,
        // not automatic permission to move. Auditing must not change the result.
        object channel = capture.Invoke(mesh, new object[] { V(new(0, 0, .078f)), V(Vector3.UnitZ),
            .08f, .0025f, .00025f, V(new(0, .008f, 0)), 0, 3, .00275f })!;
        object[] exit = { V(new(0, 0, .0788f)), V(Vector3.UnitZ), V(new(0, .008f, 0)), V(Vector3.UnitZ),
            .08f, .0025f, .00025f, .012f, .08f, null!, .008f, 0f, "", .0025f, channel };
        bool before = (bool)plan.Invoke(mesh, exit)!;
        string failure = (string)exit[12];
        Report(Mesh(0f));
        bool after = (bool)plan.Invoke(mesh, exit)!;
        if (before || after || failure != (string)exit[12] || !failure.Contains("outside exit channel tri=1"))
            throw new Exception("Read-only handoff audit changed collision authorization.");
        Console.WriteLine("PASS exit contact audit: all contacts classified by identity/layer, existing vs newly deformed contacts, forward trend, unknown history and unchanged collision rejection.");
    }
}
