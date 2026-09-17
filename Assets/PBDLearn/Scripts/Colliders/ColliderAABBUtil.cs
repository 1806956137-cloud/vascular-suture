using Unity.Mathematics;
using UnityEngine;

namespace PBDLearn
{
    public static class ColliderAABBUtil
    {
        public static AABB FromSphere(in SphereDesc s, float extra = 0f)
        {
            float r = s.radius + Mathf.Max(0f, extra);
            var c = new Vector3(s.center.x, s.center.y, s.center.z);
            var e = new Vector3(r, r, r);
            return new AABB(c - e, c + e);
        }

        public static AABB FromCapsule(in CapsuleDesc c, float extra = 0f)
        {
            float r = c.radius + Mathf.Max(0f, extra);
            float3 p1 = c.c0 + c.axis.xyz * c.axis.w;

            var a = new Vector3(c.c0.x, c.c0.y, c.c0.z);
            var b = new Vector3(p1.x, p1.y, p1.z);

            var min = new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
            var max = new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));

            var e = new Vector3(r, r, r);
            return new AABB(min - e, max + e);
        }

        public static AABB FromNeedleShaftCapsule(in CapsuleDesc c, float coneLength, float extra = 0f)
        {
            if (c.hasNeedleTip == 0)
                return FromCapsule(c, extra);

            float3 fallbackAxis = math.normalizesafe(c.axis.xyz, new float3(0f, 1f, 0f));
            float3 dir = math.normalizesafe(c.tipForward, fallbackAxis);
            float3 tail = c.tailCenter;
            float3 tipConeBase = c.tipProbeCenter - dir * math.max(0f, coneLength);
            float r = c.radius + Mathf.Max(0f, extra);

            var a = new Vector3(tail.x, tail.y, tail.z);
            var b = new Vector3(tipConeBase.x, tipConeBase.y, tipConeBase.z);

            var min = new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
            var max = new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));

            var e = new Vector3(r, r, r);
            return new AABB(min - e, max + e);
        }

        public static AABB FromNeedleTipProbe(in CapsuleDesc c, float extra = 0f)
        {
            if (c.hasNeedleTip == 0)
                return FromCapsule(c, extra);

            float r = math.max(0f, c.tipProbeRadius) + Mathf.Max(0f, extra);
            var center = new Vector3(c.tipProbeCenter.x, c.tipProbeCenter.y, c.tipProbeCenter.z);
            var e = new Vector3(r, r, r);
            return new AABB(center - e, center + e);
        }

        public static AABB FromNeedleTipPiercerCone(in CapsuleDesc c, float coneLength, float coneBaseRadius, float extra = 0f)
        {
            if (c.hasNeedleTip == 0)
                return FromCapsule(c, extra);

            float3 dir = math.normalizesafe(c.tipForward, new float3(0f, 1f, 0f));
            float3 apex = c.tipProbeCenter;
            float3 baseCenter = apex - dir * math.max(0f, coneLength);
            float r = math.max(math.max(0f, coneBaseRadius), math.max(0f, c.tipProbeRadius)) + Mathf.Max(0f, extra);

            var a = new Vector3(apex.x, apex.y, apex.z);
            var b = new Vector3(baseCenter.x, baseCenter.y, baseCenter.z);
            var min = new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
            var max = new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));
            var e = new Vector3(r, r, r);
            return new AABB(min - e, max + e);
        }

        public static AABB FromBox(in BoxDesc b, float extra = 0f)
        {
            var o = new Vector3(b.min.x, b.min.y, b.min.z);
            var ax = new Vector3(b.ax.x, b.ax.y, b.ax.z) * b.ax.w;
            var ay = new Vector3(b.ay.x, b.ay.y, b.ay.z) * b.ay.w;
            var az = new Vector3(b.az.x, b.az.y, b.az.z) * b.az.w;

            Vector3 c000 = o;
            Vector3 c100 = o + ax;
            Vector3 c010 = o + ay;
            Vector3 c001 = o + az;
            Vector3 c110 = o + ax + ay;
            Vector3 c101 = o + ax + az;
            Vector3 c011 = o + ay + az;
            Vector3 c111 = o + ax + ay + az;

            Vector3 min = c000;
            Vector3 max = c000;
            Expand(ref min, ref max, c100);
            Expand(ref min, ref max, c010);
            Expand(ref min, ref max, c001);
            Expand(ref min, ref max, c110);
            Expand(ref min, ref max, c101);
            Expand(ref min, ref max, c011);
            Expand(ref min, ref max, c111);

            float e = Mathf.Max(0f, extra);
            var ext = new Vector3(e, e, e);
            return new AABB(min - ext, max + ext);
        }

        private static void Expand(ref Vector3 min, ref Vector3 max, in Vector3 p)
        {
            min.x = Mathf.Min(min.x, p.x);
            min.y = Mathf.Min(min.y, p.y);
            min.z = Mathf.Min(min.z, p.z);
            max.x = Mathf.Max(max.x, p.x);
            max.y = Mathf.Max(max.y, p.y);
            max.z = Mathf.Max(max.z, p.z);
        }
    }
}
