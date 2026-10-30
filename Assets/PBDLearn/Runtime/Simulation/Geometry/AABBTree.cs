using System;
using System.Collections.Generic;

public delegate bool NodeTest(AABB box);

public class AABBTree<T>
{
    public readonly int LEAF_OBJ_MAX = 1;
    public AABBNode<T> rootAABB;
    public IAABBNodeAdaptor<T> adaptor;
    public int nodeCount = 0;
    public int maxDepth = 0;

    private void Traverse(AABBNode<T> curNode, NodeTest hitTest, List<AABBNode<T>> hitlist)
    {
        if (curNode == null) return;
        if (curNode.box == null) return;

        if (hitTest(curNode.box))
        {
            hitlist.Add(curNode);
            Traverse(curNode.left, hitTest, hitlist);
            Traverse(curNode.right, hitTest, hitlist);
        }
    }

    public List<AABBNode<T>> Traverse(NodeTest hitTest)
    {
        var hits = new List<AABBNode<T>>();
        Traverse(rootAABB, hitTest, hits);
        return hits;
    }

    /// <summary>
    /// 返回与 query 相交的所有叶子节点里包含的对象（可能有重复，取决于你的 adaptor 映射策略）。
    /// ClothSimulator 用它来做 BVH broadphase 候选点收集。
    /// </summary>
    public List<T> GetAllObjects(AABB query)
    {
        var result = new List<T>();
        if (rootAABB == null || rootAABB.box == null || query == null) return result;

        CollectObjects(rootAABB, query, result);
        return result;
    }

    private void CollectObjects(AABBNode<T> node, AABB query, List<T> result)
    {
        if (node == null || node.box == null) return;
        if (!node.box.IntersectsAABB(query)) return;

        if (node.IsLeaf)
        {
            if (node.gobjects != null && node.gobjects.Count > 0)
            {
                result.AddRange(node.gobjects);
            }
            return;
        }

        CollectObjects(node.left, query, result);
        CollectObjects(node.right, query, result);
    }

    public AABBTree(IAABBNodeAdaptor<T> nodeAdaptor, List<T> objects)
    {
        nodeAdaptor.SetAABB(this);
        adaptor = nodeAdaptor;

        if (objects != null && objects.Count > 0)
        {
            rootAABB = new AABBNode<T>(this, objects);
        }
        else
        {
            rootAABB = new AABBNode<T>(this);
            rootAABB.gobjects = new List<T>();
        }
    }
}
