using System.Collections;
using System.Numerics;
using System.Reflection;

// Run the compiled production planner; independently sample the complete
// capsule against a cylinder/plane, not just the planner's reported status.
internal static class ExteriorNeedleRouteRegression
{
    public static void Run(Type controller)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo build = controller.GetMethod("TryBuildExteriorPoseRoute", flags)!;
        MethodInfo preEntry = controller.GetMethod("ResolveExteriorPreEntryDistance", flags)!;
        Type vectorType = build.GetParameters()[0].ParameterType;
        object V(Vector3 v) => Activator.CreateInstance(vectorType, v.X, v.Y, v.Z)!;
        Vector3 Read(object v) => new(
            (float)vectorType.GetField("x")!.GetValue(v)!,
            (float)vectorType.GetField("y")!.GetValue(v)!,
            (float)vectorType.GetField("z")!.GetValue(v)!);
        IList List() => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vectorType))!;
        const float radius = .0025f, length = .08f, margin = .005f, outer = .05f;
        int routes = 0;
        foreach (float height in new[] { .24f, .20f })
        foreach (float tilt in new[] { 0f, .25f })
        {
            Vector3 axis = Vector3.Normalize(new(0f, tilt, 1f));
            Vector3 normal = Vector3.UnitY;
            Vector3 basisY = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitX));
            Vector3 center = new(0f, height, 0f);
            float[] angles = { 90f, 210f, 330f, 150f, 270f, 30f };
            for (int from = 0; from < angles.Length; from++)
            for (int to = 0; to < angles.Length; to++)
            {
                if (from == to) continue;
                Vector3 Radial(float degrees) => Vector3.UnitX * MathF.Cos(degrees * MathF.PI / 180f) +
                    basisY * MathF.Sin(degrees * MathF.PI / 180f);
                Vector3 r0 = Radial(angles[from]), r1 = Radial(angles[to]);
                Vector3 entry = center + axis * -.015f + r1 * outer;
                Vector3 outward = r1;
                if (Vector3.Dot(outward, r1) <= 0f)
                    throw new Exception("Outer endpoint direction reversed into vessel.");
                Vector3 endForward = -outward;
                Vector3 startForward = r0;
                Vector3 start = center + axis * .015f + r0 * outer + startForward * .115f;
                float distance = (float)preEntry.Invoke(null, new[] { V(entry), V(outward), V(center), V(axis),
                    (object)outer, radius, margin, .018f })!;
                Vector3 destination = entry + outward * distance;
                IList tips = List(), forwards = List();
                object[] args = { V(center), V(axis), V(start), V(startForward), V(destination), V(endForward),
                    outer, length, radius, .07f, margin, V(normal), 0f, tips, forwards, 0f, "" };
                if (!(bool)build.Invoke(null, args)!)
                    throw new Exception($"Exterior route height={height} tilt={tilt} {from}->{to} failed: {args[^1]}");
                Vector3 previous = start, previousForward = startForward;
                for (int i = 0; i < tips.Count; i++)
                {
                    Vector3 tip = Read(tips[i]!), forward = Read(forwards[i]!);
                    int samples = Math.Max(32, (int)Math.Ceiling(Vector3.Distance(previous, tip) / .0002f));
                    for (int s = 0; s <= samples; s++)
                    {
                        float t = s / (float)samples;
                        Vector3 p = Vector3.Lerp(previous, tip, t);
                        Vector3 f = Vector3.Normalize(Vector3.Lerp(previousForward, forward, t));
                        // Check the complete capsule centerline at dense spatial
                        // samples; each sample represents a sphere of radius r.
                        for (int k = 0; k <= 80; k++)
                        {
                            Vector3 c = p - f * (radius + (length - 2f * radius) * k / 80f);
                            Vector3 d = c - center;
                            float radial = (d - axis * Vector3.Dot(d, axis)).Length();
                            if (radial < outer + radius || c.Y < radius)
                                throw new Exception($"Capsule collision {from}->{to}, edge={i}, sample={s}, body={k}");
                        }
                    }
                    previous = tip; previousForward = forward;
                }
                if (Vector3.Distance(Read(tips[tips.Count - 1]!), destination) > 1e-7f)
                    throw new Exception("Exterior planner changed entry target.");
                Vector3 lastStep = destination - Read(tips[tips.Count - 2]!);
                if (Vector3.Dot(Vector3.Normalize(lastStep), endForward) < .999f)
                    throw new Exception("Final approach is not coaxial with entry.");
                routes++;
            }
        }
        // A background that intersects the vessel must not produce a success
        // route or leave a partially built route for the executor.
        IList badTips = List(), badForwards = List();
        object[] invalid = { V(Vector3.Zero), V(Vector3.UnitZ), V(new(0f, -.1f, 0f)), V(Vector3.UnitZ),
            V(new(0f, -.07f, .02f)), V(Vector3.UnitY), outer, length, radius, .07f, margin,
            V(Vector3.UnitY), 0f, badTips, badForwards, 0f, "" };
        if ((bool)build.Invoke(null, invalid)! || badTips.Count != 0 || badForwards.Count != 0)
            throw new Exception("Blocked exterior route escaped as a partial/unchecked success.");

        // The short arc passes through the background, but the opposite arc
        // can go over the vessel. This is constrained route selection, not a
        // straight-line fallback after a geometry failure.
        Vector3 lowCenter = new(0f, .054f, 0f);
        Vector3 lowStart = lowCenter + new Vector3(-.8660254f, -.5f, 0f) * .065f;
        Vector3 lowEnd = lowCenter + new Vector3(.8660254f, -.5f, 0f) * .065f;
        IList longTips = List(), longForwards = List();
        object[] aroundTop = { V(lowCenter), V(Vector3.UnitZ), V(lowStart), V(Vector3.UnitZ),
            V(lowEnd), V(Vector3.UnitZ), outer, length, radius, .07f, margin,
            V(Vector3.UnitY), 0f, longTips, longForwards, 0f, "" };
        if (!(bool)build.Invoke(null, aroundTop)! || (float)aroundTop[^2] > -200f)
            throw new Exception($"Exterior route did not select the feasible opposite arc: {aroundTop[^1]}");
        Vector3 prior = lowStart;
        foreach (object item in longTips)
        {
            Vector3 next = Read(item);
            for (int sample = 0; sample <= 100; sample++)
            {
                Vector3 p = Vector3.Lerp(prior, next, sample / 100f);
                Vector3 radial = p - lowCenter; radial.Z = 0f;
                if (p.Y < radius || radial.Length() < outer + radius)
                    throw new Exception("Opposite arc intersects a cylinder/plane obstacle.");
            }
            prior = next;
        }

        // The initial approach also uses the exterior planner, without a
        // preceding completed stitch or tissue object being required.
        IList initialTips = List(), initialForwards = List();
        object[] initial = { V(Vector3.Zero), V(Vector3.UnitZ), V(new(.22f, .25f, -.06f)), V(-Vector3.UnitY),
            V(new(0f, .075f, .01f)), V(-Vector3.UnitY), outer, length, radius, .07f, margin,
            V(Vector3.Zero), 0f, initialTips, initialForwards, 0f, "" };
        if (!(bool)build.Invoke(null, initial)!)
            throw new Exception($"Initial exterior approach without tissue failed: {initial[^1]}");
        // A narrow gap cannot accommodate the original bottom puncture pose.
        // Reject it before contact; never silently tilt the entry tangent while
        // leaving the Inner exit direction unchanged to squeeze through.
        IList narrowTips = List(), narrowForwards = List();
        object[] narrow = { V(new(0f, .085f, 0f)), V(Vector3.UnitZ), V(new(0f, .25f, 0f)), V(-Vector3.UnitY),
            V(new(0f, .017f, .01f)), V(Vector3.UnitY), outer, length, radius, .07f, margin,
            V(Vector3.UnitY), 0f, narrowTips, narrowForwards, 0f, "" };
        if ((bool)build.Invoke(null, narrow)! || narrowTips.Count != 0 || narrowForwards.Count != 0)
            throw new Exception("Infeasible bottom entry escaped as an adjusted/unchecked puncture pose.");
        MethodInfo frame = controller.GetMethod("TryCalculateExteriorOuterFrame", flags)!;
        MethodInfo advancePose = controller.GetMethod("ExteriorPoseStepFraction", flags)!;
        foreach (var motion in new (float distance, float angle)[] { (.08f, 90f), (.00001f, 180f), (.1f, .001f), (0f, 90f), (.1f, 0f) })
        {
            float remainingDistance = motion.distance, remainingAngle = motion.angle;
            int frames = 0;
            for (; frames < 1000; frames++)
            {
                float fraction = (float)advancePose.Invoke(null, new object[] { remainingDistance, remainingAngle, .00035f, 1.2f })!;
                if (fraction <= 0f || remainingDistance * fraction > .000351f || remainingAngle * fraction > 1.201f)
                    throw new Exception("Exterior pose step stalled or exceeded translation/rotation limits.");
                remainingDistance *= 1f - fraction;
                remainingAngle *= 1f - fraction;
                if (fraction == 1f) break;
            }
            if (frames >= 1000 || remainingDistance != 0f || remainingAngle != 0f)
                throw new Exception("Exterior pose waypoint advanced with residual motion.");
        }
        MethodInfo changed = controller.GetMethod("HasExteriorGeometryChanged", flags)!;
        Vector3 shifted = new(.021f, -.013f, .04f);
        Vector3 tiltedAxis = Vector3.Normalize(new(.12f, -.08f, 1f));
        Vector3 radialX = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, tiltedAxis));
        Vector3 radialY = Vector3.Cross(tiltedAxis, radialX);
        IList sideA = List(), sideB = List();
        for (int i = 0; i < 32; i++)
        {
            float angle = i * 2f * MathF.PI / 32f;
            Vector3 r = .061f * (radialX * MathF.Cos(angle) + radialY * MathF.Sin(angle));
            sideA.Add(V(shifted - tiltedAxis * .012f + r));
            sideB.Add(V(shifted + tiltedAxis * .012f + r));
        }
        object[] currentFrame = { sideA, sideB, V(Vector3.Zero), V(Vector3.Zero), 0f };
        if (!(bool)frame.Invoke(null, currentFrame)! ||
            Vector3.Distance(Read(currentFrame[2]), shifted) > 1e-6f ||
            Vector3.Dot(Read(currentFrame[3]), tiltedAxis) < .99999f ||
            MathF.Abs((float)currentFrame[4] - .061f) > 1e-6f)
            throw new Exception("Next-pass frame did not follow translated/tilted/resized material boundaries.");
        object[] frozen = { V(Vector3.Zero), V(Vector3.UnitX), V(Vector3.Zero), V(Vector3.UnitZ), .05f,
            V(Vector3.Zero), V(Vector3.UnitX), V(Vector3.Zero), V(Vector3.UnitZ), .05f };
        if ((bool)changed.Invoke(null, frozen)!) throw new Exception("Unchanged next-pass route invalidated.");
        foreach (int index in new[] { 5, 6, 7, 8, 9 })
        {
            object[] deformed = (object[])frozen.Clone();
            deformed[index] = index == 9 ? .061f : index == 6 ? V(Vector3.UnitY) :
                index == 8 ? V(tiltedAxis) : V(shifted);
            if (!(bool)changed.Invoke(null, deformed)!)
                throw new Exception($"Deformation field {index} reused a stale exterior route.");
        }
        Console.WriteLine($"PASS exterior whole-capsule routes: {routes}, original endpoint poses, tilted axis, coaxial arrival, opposite arc, first approach, infeasible narrow-gap rejection, current material frame and route invalidation.");
    }
}
