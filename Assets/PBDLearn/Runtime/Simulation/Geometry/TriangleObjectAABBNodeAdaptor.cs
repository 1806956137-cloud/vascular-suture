using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An adaptor for AABB to understand TriangleObject nodes.
/// </summary>
public class TriangleObjectAABBNodeAdaptor : IAABBNodeAdaptor<TriangleObject>
{
    Dictionary<TriangleObject, AABBNode<TriangleObject>> ssToLeafMap = new Dictionary<TriangleObject, AABBNode<TriangleObject>>();
    public void UnmapObject(TriangleObject obj)
    {
        ssToLeafMap.Remove(obj);
    }
    public void MapObjectToAABBLeaf(TriangleObject obj, AABBNode<TriangleObject> leaf)
    {
        // this allows us to be notified when an object moves, so we can adjust the aabbTree
        //obj.OnPositionOrSizeChanged += obj_OnChanged;

        // TODO: add a hook to handle TriangleObject deletion... (either a weakref GC notify, or OnDestroy)

        ssToLeafMap[obj] = leaf;
    }
    public AABBNode<TriangleObject> GetLeaf(TriangleObject obj)
    {
        return ssToLeafMap[obj];
    }

    AABBTree<TriangleObject> aabbTree;
    public AABBTree<TriangleObject> AABBTree { get { return aabbTree; } }

    public void SetAABB(AABBTree<TriangleObject> aabbTree)
    {
        this.aabbTree = aabbTree;
    }

    public Vector3 GetCenter(TriangleObject obj)
    {
        return obj.Center;
    }

    public Vector3 GetMin(TriangleObject obj)
    {
        return obj.Min;
    }

    public Vector3 GetMax(TriangleObject obj)
    {
        return obj.Max;
    }

    public TriangleObjectAABBNodeAdaptor() { }
}
