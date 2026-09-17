using System.Numerics;
using System.Reflection;

internal static class PostHistExitRegression
{
    public static void Run(Type controller)
    {
        Type surface = controller.GetNestedType("VesselSurfaceSnapshot", BindingFlags.NonPublic)!;
        MethodInfo plan = surface.GetMethod("TryPlanAxialExit")!;
        MethodInfo capture = surface.GetMethod("CaptureExitChannelContacts")!;
        Type vector = plan.GetParameters()[0].ParameterType;
        object V(Vector3 p) => Activator.CreateInstance(vector, p.X, p.Y, p.Z)!;
        object Mesh(bool obstruction)
        {
            var points = new List<Vector3> { new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0) };
            if (obstruction) points.AddRange(new[] { new Vector3(-1, -1, .085f), new Vector3(1, -1, .085f), new Vector3(0, 1, .085f) });
            Array array = Array.CreateInstance(vector, points.Count);
            for (int i = 0; i < points.Count; i++) array.SetValue(V(points[i]), i);
            return Activator.CreateInstance(surface, array, Enumerable.Range(0, points.Count).ToArray())!;
        }
        object Channel(object snapshot, Vector3 tip) => capture.Invoke(snapshot, new object[] {
            V(tip), V(Vector3.UnitZ), .08f, .0025f, .00025f, V(Vector3.Zero), 0, 1, .0075f })!;
        object[] Args(Vector3 tip, Vector3 forward, float minimum = .012f, float maximum = .08f) => new object[] {
            V(tip), V(forward), V(Vector3.Zero), V(Vector3.UnitZ), .08f, .0025f, .00025f,
            minimum, maximum, null!, .008f, 0f, "", .0025f, Channel(Mesh(false), tip) };
        object mesh = Mesh(false);
        // Tail still occupies its existing exit channel. Coaxial departure must
        // clear the full 0.08 needle, never just the previous 0.045 proxy.
        object[] inside = Args(new(0f, 0f, .075f), Vector3.UnitZ);
        if (!(bool)plan.Invoke(mesh, inside)! || (float)inside[11] < .012f)
            throw new Exception($"Coaxial channel exit failed: {inside[12]}");
        object[] released = Args(new(0f, 0f, .09f), Vector3.UnitZ, 0f);
        if (!(bool)plan.Invoke(mesh, released)! || (float)released[11] != 0f)
            throw new Exception("Clear needle did not release at zero remaining travel.");
        // A gap sufficient for live collision checks is not yet sufficient
        // for the next route's planning margin. Verify the actual handoff.
        foreach (float planningGap in new[] { .0025f, .006f })
        {
            object[] handoff = Args(new(0f, 0f, .0805f), Vector3.UnitZ, 0f);
            handoff[13] = planningGap;
            if (!(bool)plan.Invoke(mesh, handoff)! || (float)handoff[11] < planningGap - .000501f)
                throw new Exception($"Release was too early for the next route: {handoff[12]}");
            float finalTip = .0805f + (float)handoff[11];
            MethodInfo validate = controller.GetMethod("ValidateExteriorPoseRoute", BindingFlags.Static | BindingFlags.NonPublic)!;
            var tips = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vector))!;
            var forwards = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vector))!;
            tips.Add(V(new(0f, 0f, finalTip + .001f))); forwards.Add(V(Vector3.UnitZ));
            object[] route = { V(Vector3.Zero), V(Vector3.UnitZ), 0f, .08f, .0025f, planningGap * 2f,
                V(Vector3.Zero), 0f, V(new(0f, 0f, finalTip)), V(Vector3.UnitZ), tips, forwards, "", null!, 0f, mesh };
            if (!(bool)validate.Invoke(null, route)!)
                throw new Exception($"Verified release failed at the first planned route edge: {route[12]}");
        }
        object[] blocked = Args(new(0f, 0f, .075f), Vector3.UnitZ);
        if ((bool)plan.Invoke(Mesh(true), blocked)! || !((string)blocked[12]).Contains("newOrDeepeningContact"))
            throw new Exception("Axial clearance crossed a new opposing wall.");
        object[] turn = Args(new(0f, 0f, .075f), Vector3.UnitX);
        if ((bool)plan.Invoke(mesh, turn)!) throw new Exception("Post-Hist departure accepted a lateral direction.");
        object[] insufficient = Args(new(0f, 0f, .075f), Vector3.UnitZ, 0f, .002f);
        if ((bool)plan.Invoke(mesh, insufficient)!) throw new Exception("Occupied needle escaped via exhausted exit budget.");
        Console.WriteLine("PASS Post-Hist geometry: full-body axial release, planning-gap handoff, existing channel only, no lateral turn, new obstacle rejection, bounded travel.");
    }
}
