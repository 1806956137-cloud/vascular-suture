using System.Collections;
using System.Numerics;
using System.Reflection;

internal static class LumenPoseRouteRegression
{
    public static void Run(Type controller)
    {
        MethodInfo build = controller.GetMethod("TryBuildLumenPoseRoute", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo local = controller.GetMethod("TryBuildLumenLocalPoseRoute", BindingFlags.NonPublic | BindingFlags.Static)!;
        Type vector = build.GetParameters()[0].ParameterType;
        Type surfaceType = controller.GetNestedType("VesselSurfaceSnapshot", BindingFlags.NonPublic)!;
        object V(Vector3 p) => Activator.CreateInstance(vector, p.X, p.Y, p.Z)!;
        Vector3 Read(object p) => new((float)vector.GetField("x")!.GetValue(p)!,
            (float)vector.GetField("y")!.GetValue(p)!, (float)vector.GetField("z")!.GetValue(p)!);
        IList List() => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vector))!;
        const float length = .08f, radius = .0025f, margin = .005f, innerX = .15f;
        int routes = 0;
        foreach (float innerY in new[] { .12f, .15f })
        foreach (float tilt in new[] { 0f, .35f })
        {
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, tilt);
            Vector3 center = new(.3f, .2f, -.1f);
            Vector3 P(Vector3 p) => Vector3.Transform(p, rotation) + center;
            Vector3 F(Vector3 f) => Vector3.Transform(f, rotation);
            Vector3 Local(Vector3 p) => Vector3.Transform(p - center, Quaternion.Inverse(rotation));
            object Mesh(int obstruction)
            {
                var vertices = new List<Vector3>();
                var indices = new List<int>();
                for (int layer = 0; layer < 2; layer++)
                {
                    int offset = vertices.Count;
                    for (int bank = 0; bank < 2; bank++)
                    for (int i = 0; i < 64; i++)
                    {
                        float theta = 2f * MathF.PI * i / 64f;
                        vertices.Add(P(new((innerX + layer * .02f) * MathF.Cos(theta),
                            (innerY + layer * .02f) * MathF.Sin(theta), bank == 0 ? -.2f : .2f)));
                    }
                    for (int i = 0; i < 64; i++)
                    {
                        int a = offset + i, b = offset + (i + 1) % 64, c = a + 64, d = b + 64;
                        indices.AddRange(new[] { a, b, c, b, d, c });
                    }
                }
                if (obstruction == 1)
                {
                    int first = vertices.Count;
                    vertices.AddRange(new[] { P(new(-1, -1, 0)), P(new(1, -1, 0)), P(new(0, 1, 0)) });
                    indices.AddRange(new[] { first, first + 1, first + 2 });
                }
                else if (obstruction == 2)
                {
                    // Blocks the initial radial move toward the center lane,
                    // but leaves room to turn at the current capsule center.
                    int first = vertices.Count;
                    vertices.AddRange(new[] { P(new(.025f, -.004f, -.064f)),
                        P(new(.025f, .004f, -.064f)), P(new(.025f, 0f, -.056f)) });
                    indices.AddRange(new[] { first, first + 1, first + 2 });
                }
                Array points = Array.CreateInstance(vector, vertices.Count);
                for (int i = 0; i < vertices.Count; i++) points.SetValue(V(vertices[i]), i);
                return Activator.CreateInstance(surfaceType, points, indices.ToArray())!;
            }
            object mesh = Mesh(0);
            for (int angle = 0; angle < 6; angle++)
            {
                float theta = 2f * MathF.PI * angle / 6f;
                Vector3 radial = new(MathF.Cos(theta), MathF.Sin(theta), 0f);
                float wallDistance = 1f / MathF.Sqrt(radial.X * radial.X / (innerX * innerX) + radial.Y * radial.Y / (innerY * innerY));
                Vector3 start = P(radial * (wallDistance - length - .018f) - Vector3.UnitZ * .06f);
                Vector3 end = P(radial * (wallDistance - .018f) + Vector3.UnitZ * .06f);
                Vector3 startForward = F(-radial), endForward = F(radial);
                object[] Args(object surface) => new object[] { V(center), V(F(Vector3.UnitZ)), V(start), V(startForward),
                    V(end), V(endForward), length, radius, margin, surface, List(), List(), new List<int>(), 0, "" };
                object[] args = Args(mesh);
                if (!(bool)build.Invoke(null, args)!) throw new Exception($"Lumen route rejected: {args[14]}");
                IList tips = (IList)args[10], forwards = (IList)args[11];
                var phases = (List<int>)args[12];
                if (tips.Count != forwards.Count || tips.Count != phases.Count || tips.Count == 0)
                    throw new Exception("Lumen pose and phase counts differ.");
                Vector3 previous = start, previousForward = startForward;
                for (int edge = 0; edge < tips.Count; edge++)
                {
                    Vector3 next = Read(tips[edge]!), nextForward = Read(forwards[edge]!);
                    if ((phases[edge] == 1 || phases[edge] == 3) &&
                        Vector3.Distance(previous - previousForward * length / 2f, next - nextForward * length / 2f) > 1e-6f)
                        throw new Exception("Lumen turn displaced the capsule center.");
                    for (int step = 0; step <= 16; step++)
                    for (int body = 0; body <= 16; body++)
                    {
                        Vector3 tip = Vector3.Lerp(previous, next, step / 16f);
                        Vector3 forward = Vector3.Normalize(Vector3.Lerp(previousForward, nextForward, step / 16f));
                        Vector3 p = Local(tip - forward * (radius + (length - 2f * radius) * body / 16f));
                        float rho = MathF.Sqrt(p.X * p.X / (innerX * innerX) + p.Y * p.Y / (innerY * innerY));
                        if (rho > MathF.Cos(MathF.PI / 64f) - radius / innerY)
                            throw new Exception("Accepted capsule is outside the independent inscribed ellipse bound.");
                    }
                    previous = next; previousForward = nextForward;
                }
                if (Vector3.Distance(previous, end) > 1e-6f || Vector3.Dot(previousForward, endForward) < .99999f)
                    throw new Exception("Lumen arrival lost the exact puncture pose.");
                Vector3 adjustedEnd = end + F(Vector3.UnitZ * .0003f);
                Vector3 adjustedForward = Vector3.Normalize(endForward + F(Vector3.UnitZ * .003f));
                object[] adjustment = { V(end), V(endForward), V(adjustedEnd), V(adjustedForward),
                    length, radius, margin, mesh, List(), List(), "" };
                if (!(bool)local.Invoke(null, adjustment)! || ((IList)adjustment[8]).Count != 1 ||
                    Vector3.Distance(Read(((IList)adjustment[8])[0]!), adjustedEnd) > 1e-6f ||
                    Vector3.Dot(Read(((IList)adjustment[9])[0]!), adjustedForward) < .99999f)
                    throw new Exception($"Small endpoint drift did not use an exact local connector: {adjustment[10]}");
                object[] obstructedAdjustment = { V(start), V(startForward), V(end), V(endForward),
                    length, radius, margin, Mesh(1), List(), List(), "" };
                if ((bool)local.Invoke(null, obstructedAdjustment)! || ((IList)obstructedAdjustment[8]).Count != 0)
                    throw new Exception("Local connector skipped the obstacle between clear endpoints.");
                void Reject(object[] blocked, string scenario)
                {
                    if ((bool)build.Invoke(null, blocked)! || ((IList)blocked[10]).Count != 0 ||
                        ((IList)blocked[11]).Count != 0 || ((List<int>)blocked[12]).Count != 0 || (int)blocked[13] != -1)
                        throw new Exception($"Lumen collision escaped: {scenario}");
                    if (!((string)blocked[14]).Contains("all 8 pose candidates blocked"))
                        throw new Exception($"Missing full candidate failure report: {scenario}: {blocked[14]}");
                }
                Reject(Args(Mesh(1)), "obstacle between clear endpoints");
                if (angle == 0)
                {
                    object[] alternative = Args(Mesh(2));
                    if (!(bool)build.Invoke(null, alternative)! || (int)alternative[13] == 0)
                        throw new Exception($"Valid alternative turn site was not selected: {alternative[14]}");
                }
                object[] badStart = Args(mesh);
                badStart[2] = V(P(radial * (wallDistance - length + .015f) - Vector3.UnitZ * .06f));
                Reject(badStart, "tail across the departure wall");
                object[] badEnd = Args(mesh);
                badEnd[4] = V(P(radial * (wallDistance + .01f) + Vector3.UnitZ * .06f));
                Reject(badEnd, "tip across the arrival wall");
                routes++;
            }
        }
        Console.WriteLine($"PASS lumen routes: {routes} circumferential/flattened/tilted cases; local endpoint adjustments and blocked local sweeps; capsule-center turns and exact arrival; departure, arrival and intermediate collisions rejected across all candidates.");
    }
}
