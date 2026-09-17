using UnityEngine;

public class TriangleObject
{
    private Vector3 pos1;
    private Vector3 pos2;
    private Vector3 pos3;

    private Vector3 min;
    private Vector3 max;

    private Vector3 center;

    public Vector3 Pos1
    {
        get { return pos1; }
    }

    public Vector3 Pos2
    {
        get { return pos2; }
    }

    public Vector3 Pos3
    {
        get { return pos3; }
    }

    public Vector3 Min
    {
        get { return min; }
    }

    public Vector3 Max
    {
        get { return max; }
    }

    public Vector3 Center
    {
        get { return center; }
    }

    public TriangleObject(Vector3 pos1, Vector3 pos2, Vector3 pos3)
    {
        this.pos1 = pos1;
        this.pos2 = pos2;
        this.pos3 = pos3;

        float minX = Mathf.Min(this.pos1.x, this.pos2.x, this.pos3.x);
        float minY = Mathf.Min(this.pos1.y, this.pos2.y, this.pos3.y);
        float minZ = Mathf.Min(this.pos1.z, this.pos2.z, this.pos3.z);

        float maxX = Mathf.Max(this.pos1.x, this.pos2.x, this.pos3.x);
        float maxY = Mathf.Max(this.pos1.y, this.pos2.y, this.pos3.y);
        float maxZ = Mathf.Max(this.pos1.z, this.pos2.z, this.pos3.z);

        this.min = new Vector3(minX, minY, minZ);
        this.max = new Vector3(maxX, maxY, maxZ);

        this.center = (this.min + this.max) * 0.5f;
    }
}
