using UnityEngine;

public interface IAABBNodeAdaptor<T>
{
    AABBTree<T> AABBTree { get; }
    void SetAABB(AABBTree<T> aabbTree);

    Vector3 GetCenter(T obj);
    Vector3 GetMin(T obj);

    Vector3 GetMax(T obj);
    void MapObjectToAABBLeaf(T obj, AABBNode<T> leaf);
    void UnmapObject(T obj);
    AABBNode<T> GetLeaf(T obj);
}
