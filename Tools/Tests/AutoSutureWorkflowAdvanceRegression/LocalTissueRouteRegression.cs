using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class LocalTissueRouteRegression
{
    public static void Run(Type controller, string root)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo build = controller.GetMethod("TryBuildExteriorPoseRouteCore", flags)!;
        Type vector = build.GetParameters()[0].ParameterType;
        Type surface = controller.GetNestedType("TissueSurfaceSnapshot", BindingFlags.NonPublic)!;
        MethodInfo gap = surface.GetMethod("CapsuleGap")!;
        object V(Vector3 p) => Activator.CreateInstance(vector, p.X, p.Y, p.Z)!;
        Vector3 Read(object p) => new((float)vector.GetField("x")!.GetValue(p)!,
            (float)vector.GetField("y")!.GetValue(p)!, (float)vector.GetField("z")!.GetValue(p)!);
        object Surface(Vector3[] points, int[] indices, Vector3 normal)
        {
            Array array = Array.CreateInstance(vector, points.Length);
            for (int i = 0; i < points.Length; i++) array.SetValue(V(points[i]), i);
            return Activator.CreateInstance(surface, array, indices, V(normal))!;
        }
        float Gap(object mesh, Vector3 a, Vector3 b, float r = .0025f) =>
            (float)gap.Invoke(mesh, new[] { V(a), V(b), (object)r, .008f, .005f })!;
        IList List() => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vector))!;
        int[] quad = { 0, 1, 2, 0, 2, 3 };
        Vector3[] plane = { new(-1f, -.2f, -1f), new(1f, -.2f, -1f), new(1f, -.2f, 1f), new(-1f, -.2f, 1f) };
        // A remote peak must not become a plane under every needle pose.
        Vector3[] remotePeak = plane.Concat(new[] { new Vector3(2f, 2f, 2f), new Vector3(3f, 3f, 2f), new Vector3(3f, 3f, 3f) }).ToArray();
        object lowSurface = Surface(remotePeak, quad.Concat(new[] { 4, 5, 6 }).ToArray(), Vector3.UnitY);
        float expected = .2f - .0025f - .008f;
        float actual = Gap(lowSurface, new(-.02f, 0f, 0f), new(.02f, 0f, 0f));
        if (MathF.Abs(actual - expected) > 1e-6f) throw new Exception("Remote tissue peak contaminated local clearance.");
        // No triangle vertex is inside this small footprint. The intersecting
        // triangle interior must still block the needle.
        object crossing = Surface(plane.Select(p => p + Vector3.UnitY * .3f).ToArray(), quad, Vector3.UnitY);
        if (Gap(crossing, new(-.02f, 0f, 0f), new(.02f, 0f, 0f)) >= 0f)
            throw new Exception("Crossing triangle interior was skipped by local clearance.");
        // A sloped crossing triangle must be clipped, not represented by its
        // highest distant vertex. The same computation must work when rotated.
        Vector3[] slope = plane.Select(p => p + Vector3.UnitY * p.X * .1f).ToArray();
        object sloped = Surface(slope, quad, Vector3.UnitY);
        float slopeGap = Gap(sloped, new(-.02f, 0f, 0f), new(.02f, 0f, 0f));
        float expectedSlope = expected - (.02f + .0025f + .008f + .005f) * .1f;
        if (MathF.Abs(slopeGap - expectedSlope) > 1e-6f) throw new Exception("Local slope bound did not clip triangle heights.");
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, .37f);
        Vector3 shift = new(.3f, .6f, -.4f);
        object rotated = Surface(plane.Select(p => Vector3.Transform(p, rotation) + shift).ToArray(), quad,
            Vector3.Transform(Vector3.UnitY, rotation));
        if (MathF.Abs(Gap(rotated, Vector3.Transform(new Vector3(-.02f, 0f, 0f), rotation) + shift,
                Vector3.Transform(new Vector3(.02f, 0f, 0f), rotation) + shift) - expected) > 1e-6f)
            throw new Exception("Local tissue clearance changed under rigid transformation.");

        string asset = File.ReadAllText(Path.Combine(root, "Assets/PBDLearn/Content/Vascular/Generated/HybridTissueSurface.asset"));
        int count = int.Parse(Regex.Match(asset, @"m_VertexCount: (\d+)").Groups[1].Value);
        byte[] data = Convert.FromHexString(Regex.Match(asset, @"_typelessdata: ([0-9a-fA-F]+)").Groups[1].Value);
        int stride = data.Length / count;
        if (stride != 32 || !Regex.IsMatch(asset, @"m_IndexFormat: 0")) throw new Exception("Unexpected tissue mesh serialization layout.");
        Vector3[] vertices = Enumerable.Range(0, count).Select(i => new Vector3(
            BitConverter.ToSingle(data, i * stride), BitConverter.ToSingle(data, i * stride + 4), BitConverter.ToSingle(data, i * stride + 8))).ToArray();
        byte[] indexData = Convert.FromHexString(Regex.Match(asset, @"m_IndexBuffer: ([0-9a-fA-F]+)").Groups[1].Value);
        int[] indices = Enumerable.Range(0, indexData.Length / 2).Select(i => (int)BitConverter.ToUInt16(indexData, 2 * i)).ToArray();
        object realSurface = Surface(vertices, indices, Vector3.UnitY);
        // Use the real scene scale and measured guide frame, including the
        // lower entry that the old global maximum plane incorrectly rejected.
        Vector3 center = new(-.0081f, .16f, .6569f), axis = Vector3.Normalize(new(-.0366f, 0f, .9993f));
        Vector3 basisX = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, axis)), basisY = Vector3.Cross(axis, basisX);
        Vector3 Radial(float angle) => basisX * MathF.Cos(angle * MathF.PI / 180f) + basisY * MathF.Sin(angle * MathF.PI / 180f);
        const float outer = .16886f, rNeedle = .0025f, length = .08f;
        float[] angles = { 90f, 210f, 330f, 150f, 270f, 30f };
        // Independent vertical triangle intersection for physical spot checks.
        var localTriangles = Enumerable.Range(0, indices.Length / 3).Select(i =>
            (a: vertices[indices[3 * i]], b: vertices[indices[3 * i + 1]], c: vertices[indices[3 * i + 2]]))
            .Where(t => MathF.Max(t.a.X, MathF.Max(t.b.X, t.c.X)) >= -.5f && MathF.Min(t.a.X, MathF.Min(t.b.X, t.c.X)) <= .5f &&
                MathF.Max(t.a.Z, MathF.Max(t.b.Z, t.c.Z)) >= .4f && MathF.Min(t.a.Z, MathF.Min(t.b.Z, t.c.Z)) <= .9f).ToArray();
        float Height(Vector3 p)
        {
            float height = float.NegativeInfinity;
            foreach (var t in localTriangles)
            {
                float det = (t.b.Z - t.c.Z) * (t.a.X - t.c.X) + (t.c.X - t.b.X) * (t.a.Z - t.c.Z);
                if (MathF.Abs(det) < 1e-10f) continue;
                float u = ((t.b.Z - t.c.Z) * (p.X - t.c.X) + (t.c.X - t.b.X) * (p.Z - t.c.Z)) / det;
                float v = ((t.c.Z - t.a.Z) * (p.X - t.c.X) + (t.a.X - t.c.X) * (p.Z - t.c.Z)) / det;
                if (u >= -1e-5f && v >= -1e-5f && u + v <= 1.00001f)
                    height = MathF.Max(height, u * t.a.Y + v * t.b.Y + (1f - u - v) * t.c.Y);
            }
            return height;
        }
        int routes = 0;
        foreach (float from in angles)
        foreach (float to in angles)
        {
            if (from == to) continue;
            Vector3 startForward = Radial(from), endForward = -Radial(to);
            Vector3 start = center + axis * .025f + Radial(from) * (outer + .115f);
            Vector3 destination = center - axis * .025f + Radial(to) * (outer + .018f);
            IList tips = List(), forwards = List();
            object[] args = { V(center), V(axis), V(start), V(startForward), V(destination), V(endForward),
                outer, length, rNeedle, .07f, .005f, V(Vector3.UnitY), 0f, tips, forwards, 0f, "", realSurface, .008f, null! };
            if (!(bool)build.Invoke(null, args)!) throw new Exception($"Real tissue route {from}->{to}: {args[16]}");
            Vector3 previous = start, previousForward = startForward;
            for (int i = 0; i < tips.Count; i++)
            {
                Vector3 next = Read(tips[i]!), nextForward = Read(forwards[i]!);
                for (int step = 0; step <= 8; step++)
                {
                    Vector3 tip = Vector3.Lerp(previous, next, step / 8f);
                    Vector3 forward = Vector3.Normalize(Vector3.Lerp(previousForward, nextForward, step / 8f));
                    for (int body = 0; body <= 8; body++)
                    {
                        Vector3 p = tip - forward * (rNeedle + (length - 2f * rNeedle) * body / 8f);
                        Vector3 d = p - center;
                        if ((d - axis * Vector3.Dot(d, axis)).Length() < outer + rNeedle || p.Y - rNeedle < Height(p) + .008f)
                            throw new Exception($"Real tissue/cylinder collision on {from}->{to} edge={i} body={body}");
                    }
                }
                previous = next; previousForward = nextForward;
            }
            if (Vector3.Dot(Read(forwards[forwards.Count - 1]!), endForward) < .99999f)
                throw new Exception("Local tissue planner changed puncture pose.");
            routes++;
        }
        // Keep real collision rejection: replacing a remote peak with a local
        // raised surface at the endpoint must still make both arcs fail.
        object blocked = Surface(plane.Select(p => new Vector3(p.X, -.0343058f, p.Z + .65f)).ToArray(), quad, Vector3.UnitY);
        IList badTips = List(), badForwards = List();
        object[] bad = { V(center), V(axis), V(center + Vector3.UnitY * .3f), V(Vector3.UnitY),
            V(center - Vector3.UnitY * (outer + .018f)), V(Vector3.UnitY), outer, length, rNeedle, .07f, .005f,
            V(Vector3.UnitY), 0f, badTips, badForwards, 0f, "", blocked, .008f, null! };
        if ((bool)build.Invoke(null, bad)! || badTips.Count != 0 || badForwards.Count != 0)
            throw new Exception("A genuinely blocked local endpoint escaped as a valid route.");
        Console.WriteLine($"PASS local tissue: remote peak exclusion, triangle clipping, tilted background, {routes} real-mesh routes at scene scale, lower-entry posture, actual local obstruction rejection.");
    }
}
