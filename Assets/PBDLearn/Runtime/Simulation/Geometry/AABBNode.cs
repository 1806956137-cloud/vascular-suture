using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class AABBNode<T>
{
    public AABB box;

    public AABBNode<T> parent;
    public AABBNode<T> left;
    public AABBNode<T> right;

    public int depth;
    public int nodeNumber;

    public List<T> gobjects;

    public override string ToString()
    {
        return string.Format($"AABBNode{typeof(T)}:{nodeNumber}");
    }

    public bool IsLeaf
    {
        get
        {
            bool isLeaf = (this.gobjects != null);
            if (isLeaf && ((right != null) || (left != null)))
            {
                throw new Exception("AABB叶节点竟然有子节点！");
            }
            return isLeaf;
        }
    }

    private void ExpandVolume(IAABBNodeAdaptor<T> nAda, Vector3 otherMin, Vector3 otherMax)
    {
        bool expanded = false;

        Vector3 min = box.Min;
        Vector3 max = box.Max;

        if (otherMin.x < min.x) { min.x = otherMin.x; expanded = true; }
        if (otherMin.y < min.y) { min.y = otherMin.y; expanded = true; }
        if (otherMin.z < min.z) { min.z = otherMin.z; expanded = true; }

        if (otherMax.x > max.x) { max.x = otherMax.x; expanded = true; }
        if (otherMax.y > max.y) { max.y = otherMax.y; expanded = true; }
        if (otherMax.z > max.z) { max.z = otherMax.z; expanded = true; }

        box.Min = min;
        box.Max = max;

        if (expanded && parent != null)
        {
            parent.ChildExpanded(nAda, this);
        }
    }

    internal void ComputeVolume(IAABBNodeAdaptor<T> nAda)
    {
        box = new AABB(nAda.GetMin(gobjects[0]), nAda.GetMax(gobjects[0]));
        for (int i = 1; i < gobjects.Count; i++)
        {
            ExpandVolume(nAda, nAda.GetMin(gobjects[i]), nAda.GetMax(gobjects[i]));
        }
    }

    internal static float SA(AABB box)
    {
        float x_size = box.Max.x - box.Min.x;
        float y_size = box.Max.y - box.Min.y;
        float z_size = box.Max.z - box.Min.z;

        return 2.0f * ((x_size * y_size) + (x_size * z_size) + (y_size * z_size));
    }


    internal static AABB AABBofOBJ(IAABBNodeAdaptor<T> nAda, T obj)
    {
        return new AABB(nAda.GetMin(obj), nAda.GetMax(obj));
    }

    internal float SAofList(IAABBNodeAdaptor<T> nAda, List<T> list)
    {
        var box = AABBofOBJ(nAda, list[0]);

        list.ToList<T>().GetRange(1, list.Count - 1).ForEach(obj => {
            var newbox = AABBofOBJ(nAda, obj);
            box.ExpandBy(newbox);
        });
        return SA(box);
    }

    // The list of all candidate rotations, from "Fast, Effective BVH Updates for Animated Scenes", Figure 1.
    internal enum Rot
    {
        NONE, L_RL, L_RR, R_LL, R_LR, LL_RR, LL_RL,
    }

    internal class rotOpt : IComparable<rotOpt>
    {  // rotation option
        public float SAH;
        public Rot rot;
        internal rotOpt(float SAH, Rot rot)
        {
            this.SAH = SAH;
            this.rot = rot;
        }
        public int CompareTo(rotOpt other)
        {
            return SAH.CompareTo(other.SAH);
        }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private static List<Axis> eachAxis
    {
        get
        {
            return new List<Axis>((Axis[])Enum.GetValues(typeof(Axis)));
        }
    }
    internal class SplitAxisOpt<TT> : IComparable<SplitAxisOpt<TT>>
    {  // split Axis option
        public float SAH;
        public Axis axis;
        public List<TT> left, right;
        internal SplitAxisOpt(float SAH, Axis axis, List<TT> left, List<TT> right)
        {
            this.SAH = SAH;
            this.axis = axis;
            this.left = left;
            this.right = right;
        }
        public int CompareTo(SplitAxisOpt<TT> other)
        {
            return SAH.CompareTo(other.SAH);
        }
    }

    internal void SplitNode(IAABBNodeAdaptor<T> nAda)
    {
        // second, decide which axis to split on, and sort..
        List<T> splitlist = gobjects;
        splitlist.ForEach(o => nAda.UnmapObject(o));
        int center = (int)(splitlist.Count / 2); // find the center object

        SplitAxisOpt<T> bestSplit = eachAxis.Min((axis) => {
            var orderedlist = new List<T>(splitlist);
            switch (axis)
            {
                case Axis.X:
                    orderedlist.Sort(delegate (T go1, T go2) { return nAda.GetCenter(go1).x.CompareTo(nAda.GetCenter(go2).x); });
                    break;
                case Axis.Y:
                    orderedlist.Sort(delegate (T go1, T go2) { return nAda.GetCenter(go1).y.CompareTo(nAda.GetCenter(go2).y); });
                    break;
                case Axis.Z:
                    orderedlist.Sort(delegate (T go1, T go2) { return nAda.GetCenter(go1).z.CompareTo(nAda.GetCenter(go2).z); });
                    break;
                default:
                    throw new NotImplementedException("unknown split axis: " + axis.ToString());
            }

            var left_s = orderedlist.GetRange(0, center);
            var right_s = orderedlist.GetRange(center, splitlist.Count - center);

            float SAH = SAofList(nAda, left_s) * left_s.Count + SAofList(nAda, right_s) * right_s.Count;
            return new SplitAxisOpt<T>(SAH, axis, left_s, right_s);
        });

        // perform the split
        gobjects = null;
        this.left = new AABBNode<T>(nAda.AABBTree, this, bestSplit.left, bestSplit.axis, this.depth + 1); // Split the Hierarchy to the left
        this.right = new AABBNode<T>(nAda.AABBTree, this, bestSplit.right, bestSplit.axis, this.depth + 1); // Split the Hierarchy to the right                            
    }

    internal void splitIfNecessary(IAABBNodeAdaptor<T> nAda)
    {
        if (gobjects.Count > nAda.AABBTree.LEAF_OBJ_MAX)
        {
            SplitNode(nAda);
        }
    }

    //internal int countAABBNodes()
    //{
    //    if (gobjects != null)
    //    {
    //        return 1;
    //    }
    //    else
    //    {
    //        return left.countAABBNodes() + right.countAABBNodes();
    //    }
    //}

    //internal AABBNode<T> rootNode()
    //{
    //    AABBNode<T> cur = this;
    //    while (cur.parent != null) { cur = cur.parent; }
    //    return cur;
    //}

    internal void ChildExpanded(IAABBNodeAdaptor<T> nAda, AABBNode<T> child)
    {
        bool expanded = false;

        Vector3 min = box.Min;
        Vector3 max = box.Max;
        if (child.box.Min.x < box.Min.x)
        {
            min.x = child.box.Min.x; expanded = true;
        }
        if (child.box.Max.x > box.Max.x)
        {
            max.x = child.box.Max.x; expanded = true;
        }
        if (child.box.Min.y < box.Min.y)
        {
            min.y = child.box.Min.y; expanded = true;
        }
        if (child.box.Max.y > box.Max.y)
        {
            max.y = child.box.Max.y; expanded = true;
        }
        if (child.box.Min.z < box.Min.z)
        {
            min.z = child.box.Min.z; expanded = true;
        }
        if (child.box.Max.z > box.Max.z)
        {
            max.z = child.box.Max.z; expanded = true;
        }

        this.box.Min = min;
        this.box.Max = max;

        if (expanded && parent != null)
        {
            parent.ChildExpanded(nAda, this);
        }
    }

    internal void ChildRefit(IAABBNodeAdaptor<T> nAda, bool propagate = true)
    {
        ChildRefit(nAda, this, propagate: propagate);
    }

    internal static void ChildRefit(IAABBNodeAdaptor<T> nAda, AABBNode<T> curNode, bool propagate = true)
    {
        do
        {
            //AABB oldbox = curNode.box;
            AABBNode<T> left = curNode.left;
            AABBNode<T> right = curNode.right;

            // start with the left box
            AABB newBox = left.box;

            Vector3 min = newBox.Min;
            Vector3 max = newBox.Max;
            // expand any dimension bigger in the right node
            if (right.box.Min.x < min.x) { min.x = right.box.Min.x; }
            if (right.box.Min.y < min.y) { min.y = right.box.Min.y; }
            if (right.box.Min.z < min.z) { min.z = right.box.Min.z; }

            if (right.box.Max.x > max.x) { max.x = right.box.Max.x; }
            if (right.box.Max.y > max.y) { max.y = right.box.Max.y; }
            if (right.box.Max.z > max.z) { max.z = right.box.Max.z; }

            newBox.Min = min;
            newBox.Max = max;

            // now set our box to the newly created box
            curNode.box = newBox;

            // and walk up the tree
            curNode = curNode.parent;
        } while (propagate && curNode != null);
    }

    internal AABBNode(AABBTree<T> tree)
    {
        gobjects = new List<T>();
        left = right = null;
        parent = null;
        this.nodeNumber = tree.nodeCount++;
    }

    internal AABBNode(AABBTree<T> tree, List<T> gobjectlist) : this (tree,null, gobjectlist, Axis.X,0)
    { }

    private AABBNode(AABBTree<T> tree, AABBNode<T> lparent, List<T> gobjectlist, Axis lastSplitAxis, int curdepth)
    {
        IAABBNodeAdaptor<T> nAda = tree.adaptor;
        this.nodeNumber = tree.nodeCount++;

        this.parent = lparent; 
        this.depth = curdepth;

        if (tree.maxDepth < curdepth)
        {
            tree.maxDepth = curdepth;
        }

        if (gobjectlist == null || gobjectlist.Count < 1)
        {
            throw new Exception("AABBNode constructed with invalid paramaters");
        }

        // Check if we’re at our LEAF node, and if so, save the objects and stop recursing.  Also store the min/max for the leaf node and update the parent appropriately
        if (gobjectlist.Count <= tree.LEAF_OBJ_MAX)
        {
            // once we reach the leaf node, we must set prev/next to null to signify the end
            left = null;
            right = null;
            // at the leaf node we store the remaining objects, so initialize a list
            gobjects = gobjectlist;
            gobjects.ForEach(o => nAda.MapObjectToAABBLeaf(o, this));
            ComputeVolume(nAda);
            splitIfNecessary(nAda);
        }
        else
        {
            // --------------------------------------------------------------------------------------------
            // if we have more than (aabbTree.LEAF_OBJECT_COUNT) objects, then compute the volume and split
            gobjects = gobjectlist;
            ComputeVolume(nAda);
            SplitNode(nAda);
            //ChildRefit(nAda, propagate: false);
        }
    }
}
