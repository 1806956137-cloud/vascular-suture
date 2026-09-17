using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace PBDLearn
{
    public class ParticleAABBNodeAdaptor : IAABBNodeAdaptor<int>
    {
        public AABBTree<int> AABBTree { get; private set; }

        private NativeArray<float3> _positions;
        private float _inflate;

        private readonly Dictionary<int, AABBNode<int>> _leafMap = new Dictionary<int, AABBNode<int>>(1024);

        public ParticleAABBNodeAdaptor(NativeArray<float3> positions, float inflate)
        {
            _positions = positions;
            _inflate = Mathf.Max(0f, inflate);
        }

        public void SetAABB(AABBTree<int> aabbTree)
        {
            AABBTree = aabbTree;
        }

        public void SetPositions(NativeArray<float3> positions)
        {
            _positions = positions;
        }

        public void SetInflate(float inflate)
        {
            _inflate = Mathf.Max(0f, inflate);
        }

        public Vector3 GetCenter(int obj)
        {
            float3 p = _positions[obj];
            return new Vector3(p.x, p.y, p.z);
        }

        public Vector3 GetMin(int obj)
        {
            float3 p = _positions[obj];
            return new Vector3(p.x - _inflate, p.y - _inflate, p.z - _inflate);
        }

        public Vector3 GetMax(int obj)
        {
            float3 p = _positions[obj];
            return new Vector3(p.x + _inflate, p.y + _inflate, p.z + _inflate);
        }

        public void MapObjectToAABBLeaf(int obj, AABBNode<int> leaf)
        {
            _leafMap[obj] = leaf;
        }

        public void UnmapObject(int obj)
        {
            _leafMap.Remove(obj);
        }

        public AABBNode<int> GetLeaf(int obj)
        {
            _leafMap.TryGetValue(obj, out var leaf);
            return leaf;
        }
    }
}
