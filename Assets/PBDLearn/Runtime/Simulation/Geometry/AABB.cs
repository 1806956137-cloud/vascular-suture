using System;
using UnityEngine;
using System.Collections;
using Utils;

public class AABB : IEquatable<AABB>
{
    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }

    public AABB(Vector3 min, Vector3 max)
    {
        this.Min = min;
        this.Max = max;
    }

    public void RefreshAABB(Vector3 min, Vector3 max)
    {
        this.Min = min;
        this.Max = max;
    }

    public void Combine(ref AABB other)
    {
        Min = Vector3Helper.ComponentMin(Min, other.Min);
        Max = Vector3Helper.ComponentMax(Max, other.Max);
    }

    public bool IntersectsSphere(Vector3 origin, float radius)
    {
        if (
            (origin.x + radius < Min.x) ||
            (origin.y + radius < Min.y) ||
            (origin.z + radius < Min.z) ||
            (origin.x - radius > Max.x) ||
            (origin.y - radius > Max.y) ||
            (origin.z - radius > Max.z)
        )
        {
            return false;
        }
        else
        {
            return true;
        }
    }

    public bool IntersectsAABB(AABB box)
    {
        return ((Max.x > box.Min.x) && (Min.x < box.Max.x) &&
                (Max.y > box.Min.y) && (Min.y < box.Max.y) &&
                (Max.z > box.Min.z) && (Min.z < box.Max.z));
    }

    public bool Equals(AABB other)
    {
        return this.Min == other.Min && this.Max == other.Max;
    }

    public void UpdateMin(Vector3 localMin)
    {
        Min = Vector3Helper.ComponentMin(Min, localMin);
    }

    public void UpdateMax(Vector3 localMax)
    {
        Max = Vector3Helper.ComponentMax(Max, localMax);
    }

    public Vector3 Center()
    {
        return (Min + Max) / 2f;
    }

    public Vector3 Diff()
    {
        return Max - Min;
    }

    internal void ExpandToFit(AABB b)
    {
        Vector3 min = this.Min;
        Vector3 max = this.Max;
        if (b.Min.x < this.Min.x) { min.x = b.Min.x; }
        if (b.Min.y < this.Min.y) { min.y = b.Min.y; }
        if (b.Min.z < this.Min.z) { min.z = b.Min.z; }

        if (b.Max.x > this.Max.x) { max.x = b.Max.x; }
        if (b.Max.y > this.Max.y) { max.y = b.Max.y; }
        if (b.Max.z > this.Max.z) { max.z = b.Max.z; }

        this.Min = min;
        this.Max = max;
    }

    public void ExpandBy(AABB b)
    {
        Vector3 min = this.Min;
        Vector3 max = this.Max;
        if (b.Min.x < this.Min.x) { min.x = b.Min.x; }
        if (b.Min.y < this.Min.y) { min.y = b.Min.y; }
        if (b.Min.z < this.Min.z) { min.z = b.Min.z; }

        if (b.Max.x > this.Max.x) { max.x = b.Max.x; }
        if (b.Max.y > this.Max.y) { max.y = b.Max.y; }
        if (b.Max.z > this.Max.z) { max.z = b.Max.z; }

        this.Min = min;
        this.Max = max;
    }

    public AABB ExpandedBy(AABB b)
    {
        Vector3 min = this.Min;
        Vector3 max = this.Max;
        if (b.Min.x < this.Min.x) { min.x = b.Min.x; }
        if (b.Min.y < this.Min.y) { min.y = b.Min.y; }
        if (b.Min.z < this.Min.z) { min.z = b.Min.z; }

        if (b.Max.x > this.Max.x) { max.x = b.Max.x; }
        if (b.Max.y > this.Max.y) { max.y = b.Max.y; }
        if (b.Max.z > this.Max.z) { max.z = b.Max.z; }

        this.Min = min;
        this.Max = max;

        return this;
    }
}
