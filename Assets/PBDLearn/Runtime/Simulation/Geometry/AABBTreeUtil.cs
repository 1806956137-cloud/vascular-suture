using UnityEngine;

public static class AABBTreeUtil
{
    public static void RefitRecursive<T>(AABBNode<T> node, IAABBNodeAdaptor<T> adaptor)
    {
        if (node == null) return;

        if (node.IsLeaf)
        {
            if (node.gobjects == null || node.gobjects.Count == 0)
            {
                node.box = new AABB(Vector3.zero, Vector3.zero);
                return;
            }

            var first = node.gobjects[0];
            var box = new AABB(adaptor.GetMin(first), adaptor.GetMax(first));
            for (int i = 1; i < node.gobjects.Count; i++)
            {
                var obj = node.gobjects[i];
                box.ExpandBy(new AABB(adaptor.GetMin(obj), adaptor.GetMax(obj)));
            }

            node.box = box;
            return;
        }

        RefitRecursive(node.left, adaptor);
        RefitRecursive(node.right, adaptor);

        if (node.left == null && node.right == null)
        {
            node.box = new AABB(Vector3.zero, Vector3.zero);
            return;
        }
        if (node.left == null)
        {
            node.box = node.right.box;
            return;
        }
        if (node.right == null)
        {
            node.box = node.left.box;
            return;
        }

        var merged = new AABB(node.left.box.Min, node.left.box.Max);
        merged.ExpandBy(node.right.box);
        node.box = merged;
    }
}
