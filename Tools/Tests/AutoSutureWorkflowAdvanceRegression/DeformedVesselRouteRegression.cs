using System.Collections;
using System.Numerics;
using System.Reflection;

internal static class DeformedVesselRouteRegression
{
    public static void Run(Type controller)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo build = controller.GetMethod("TryBuildExteriorPoseRouteCore", flags)!;
        MethodInfo validate = controller.GetMethod("ValidateExteriorPoseRoute", flags)!;
        Type vector = build.GetParameters()[0].ParameterType;
        Type surfaceType = controller.GetNestedType("VesselSurfaceSnapshot", BindingFlags.NonPublic)!;
        object V(Vector3 p) => Activator.CreateInstance(vector, p.X, p.Y, p.Z)!;
        Vector3 Read(object p) => new((float)vector.GetField("x")!.GetValue(p)!,
            (float)vector.GetField("y")!.GetValue(p)!, (float)vector.GetField("z")!.GetValue(p)!);
        IList List() => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vector))!;
        const float length = .08f, radius = .0025f, margin = .005f, outerX = .2f;
        int routes = 0;
        foreach (float outerY in new[] { .14f, .16f })
        foreach (float tilt in new[] { 0f, .35f })
        {
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, tilt);
            Vector3 center = new(.3f, .2f, -.1f);
            Vector3 P(Vector3 p) => Vector3.Transform(p, rotation) + center;
            Vector3 F(Vector3 f) => Vector3.Transform(f, rotation);
            Vector3 Local(Vector3 p) => Vector3.Transform(p - center, Quaternion.Inverse(rotation));
            // Two deformed wall layers. The maximum-radius cylinder contains
            // a large empty region above and below the flattened vessel.
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (int layer = 0; layer < 2; layer++)
            {
                int offset = vertices.Count;
                for (int bank = 0; bank < 2; bank++)
                for (int i = 0; i < 64; i++)
                {
                    float theta = 2f * MathF.PI * i / 64f;
                    vertices.Add(P(new((outerX - layer * .02f) * MathF.Cos(theta),
                        (outerY - layer * .02f) * MathF.Sin(theta), bank == 0 ? -.2f : .2f)));
                }
                for (int i = 0; i < 64; i++)
                {
                    int a = offset + i, b = offset + (i + 1) % 64, c = a + 64, d = b + 64;
                    indices.AddRange(new[] { a, b, c, b, d, c });
                }
            }
            Array points = Array.CreateInstance(vector, vertices.Count);
            for (int i = 0; i < vertices.Count; i++) points.SetValue(V(vertices[i]), i);
            object surface = Activator.CreateInstance(surfaceType, points, indices.ToArray())!;
            Vector3 start = P(new(0f, -.27f, 0f)), startForward = F(-Vector3.UnitY);
            Vector3 end = P(new(.218f, 0f, .03f)), endForward = F(-Vector3.UnitX);
            IList tips = List(), forwards = List();
            object[] args = { V(center), V(F(Vector3.UnitZ)), V(start), V(startForward), V(end), V(endForward),
                outerX, length, radius, .07f, margin, V(Vector3.Zero), 0f, tips, forwards, 0f, "", null!, 0f, surface };
            // Reproduce the previous failure: the very first sample is inside
            // the envelope, even though the entire capsule is outside the wall.
            object[] envelopeOnly = (object[])args.Clone();
            envelopeOnly[^1] = null!;
            if ((bool)build.Invoke(null, envelopeOnly)! || !((string)envelopeOnly[16]).Contains("edge=0 sample=0/"))
                throw new Exception("Fixture did not reproduce start-pose cylinder rejection.");
            if (!(bool)build.Invoke(null, args)!)
                throw new Exception($"Valid deformed-vessel exterior route rejected: {args[16]}");
            Vector3 previous = start, previousForward = startForward;
            for (int edge = 0; edge < tips.Count; edge++)
            {
                Vector3 next = Read(tips[edge]!), nextForward = Read(forwards[edge]!);
                for (int step = 0; step <= 16; step++)
                {
                    Vector3 tip = Vector3.Lerp(previous, next, step / 16f);
                    Vector3 forward = Vector3.Normalize(Vector3.Lerp(previousForward, nextForward, step / 16f));
                    for (int body = 0; body <= 16; body++)
                    {
                        Vector3 p = Local(tip - forward * (radius + (length - 2f * radius) * body / 16f));
                        // This sufficient bound puts every sampled sphere
                        // outside the smooth ellipse containing the mesh.
                        float rho = MathF.Sqrt(p.X * p.X / (outerX * outerX) + p.Y * p.Y / (outerY * outerY));
                        if (rho < 1f + radius / outerY)
                            throw new Exception("Accepted sweep intersects the independent ellipse capsule bound.");
                    }
                }
                previous = next; previousForward = nextForward;
            }
            if (Vector3.Distance(Read(tips[tips.Count - 1]!), end) > 1e-6f ||
                Vector3.Dot(Read(forwards[forwards.Count - 1]!), endForward) < .99999f)
                throw new Exception("Mesh validation changed the planned puncture pose.");
            routes++;

            void MustReject(object[] blocked, string scenario)
            {
                if ((bool)build.Invoke(null, blocked)! || ((IList)blocked[13]).Count != 0 || ((IList)blocked[14]).Count != 0)
                    throw new Exception($"Real vessel collision escaped: {scenario}");
                if (!((string)blocked[16]).Contains("wallGeometry=CurrentVesselMesh"))
                    throw new Exception($"Real vessel collision did not identify triangle geometry: {scenario}: {blocked[16]}");
            }
            object[] badStart = (object[])args.Clone();
            badStart[2] = V(P(new(0f, -(outerY + .045f), 0f)));
            badStart[13] = List(); badStart[14] = List();
            MustReject(badStart, "body crosses wall at departure");
            object[] badEnd = (object[])args.Clone();
            badEnd[4] = V(P(new(.15f, 0f, .03f)));
            badEnd[13] = List(); badEnd[14] = List();
            MustReject(badEnd, "body crosses wall at arrival");
            // Safe endpoints are insufficient: the segment between them cuts
            // through both walls. Validate the swept body, not just endpoints.
            IList crossedTips = List(), crossedForwards = List();
            crossedTips.Add(V(P(new(.25f, 0f, 0f)))); crossedForwards.Add(V(F(Vector3.UnitZ)));
            object[] cross = { V(center), V(F(Vector3.UnitZ)), outerX, length, radius, margin,
                V(Vector3.Zero), 0f, V(P(new(-.25f, 0f, 0f))), V(F(Vector3.UnitZ)), crossedTips, crossedForwards,
                "", null!, 0f, surface };
            if ((bool)validate.Invoke(null, cross)!) throw new Exception("Swept vessel intersection was skipped.");
        }
        Console.WriteLine($"PASS deformed vessel: {routes} flattened/tilted mesh routes recover false start-cylinder rejection; real departure, arrival and intermediate collisions remain blocked.");
    }
}
