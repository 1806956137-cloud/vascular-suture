using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace PBDLearn
{
    // ============ 接口：UnityColliderProxy 会实现它 ============
    public interface ICollider
    {
        int entityId { get; }
        // ✅ 新增：给 ClothSimulator 用于把速度变化施加到刚体（可能为 null）
        Rigidbody attachedRigidbody { get; }
        void FillColliderDesc(CollidersGroup collidersGroup);

        // 可选：如果你工程里 UnityColliderProxy 有这个方法，就保留；没有就删掉
        bool GetClosestSurfacePoint(float3 point, out ConcatInfo closestPoint);
    }


    // 你原来有这个接口，保留（目前可不用）
    public interface IColliderDesc { }

    // ============ 基础 Desc（Jobs/Narrowphase 使用） ============
    public struct SphereDesc : IColliderDesc
    {
        public float3 center;
        public float radius;
    }

    public struct BoxDesc : IColliderDesc
    {
        public float3 min; // 一个角点
        public float4 ax;  // xyz为单位化方向，w为长度
        public float4 ay;
        public float4 az;
    }

    public struct CapsuleDesc : IColliderDesc
    {
        public float3 c0;     // 端点0
        public float4 axis;   // xyz为单位化方向，w为长度（c1 = c0 + axis.xyz * axis.w）
        public float radius;

        // ---- Needle semantic (Phase 1) ----
        // 是否把该 capsule 当作“有固定针头端”的针来处理。
        // 0：普通 capsule；1：针 capsule。
        public byte hasNeedleTip;

        // 固定针头端：0 = c0，1 = c1。
        public byte needleTipEnd;

        // 预留对齐字节，避免后续 Burst/Native 布局过于零碎。
        public byte reserved0;
        public byte reserved1;

        // 固定针头端/尾端的世界空间信息。
        public float3 tipCenter;
        // 针尖语义探针：用于更靠前的表面接触 / 穿刺判定。
        public float3 tipProbeCenter;
        public float tipProbeRadius;
        public float3 tipForward;
        public float3 tailCenter;
        public float3 tailForward;

        // 针头当前速度与运动模式（供后续阶段状态机使用）。
        // tipAdvanceSpeed = dot(tipVelocity, tipForward)
        // needleMotionMode: 0 = StillOrLateral, 1 = Advancing, 2 = Retracting
        public float3 tipVelocity;
        public float tipAdvanceSpeed;
        public byte needleMotionMode;
        public byte reserved2;
        public byte reserved3;
        public byte reserved4;
    }

    public struct RigidbodyDesc
    {
        public float mass;
        public float bounciness;
        public float3 velocity;
    }

    // ============ wrapper：把 collider + rigidbody + id 绑在一起 ============
    public struct SphereColliderDesc
    {
        public SphereDesc collider;
        public RigidbodyDesc rigidbody;
        public int entityId;
    }

    public struct BoxColliderDesc
    {
        public BoxDesc collider;
        public RigidbodyDesc rigidbody;
        public int entityId;
    }

    public struct CapsuleColliderDesc
    {
        public CapsuleDesc collider;
        public RigidbodyDesc rigidbody;
        public int entityId;
    }

    // ============ CollidersGroup：每帧由 ClothSimulator 填充，Jobs 只读 ============
    public struct CollidersGroup
    {
        private NativeList<SphereColliderDesc> _spheres;
        private NativeList<BoxColliderDesc> _boxes;
        private NativeList<CapsuleColliderDesc> _capsules;

        public NativeArray<SphereColliderDesc> spheres => _spheres.AsArray();
        public NativeArray<BoxColliderDesc> boxes => _boxes.AsArray();
        public NativeArray<CapsuleColliderDesc> capsules => _capsules.AsArray();

        public CollidersGroup(Allocator allocator)
        {
            _spheres = new NativeList<SphereColliderDesc>(allocator);
            _boxes = new NativeList<BoxColliderDesc>(allocator);
            _capsules = new NativeList<CapsuleColliderDesc>(allocator);
        }

        public void AddSphere(in SphereDesc desc, in RigidbodyDesc rigidbody, int entityId)
        {
            _spheres.Add(new SphereColliderDesc { collider = desc, rigidbody = rigidbody, entityId = entityId });
        }

        public void AddBox(in BoxDesc desc, in RigidbodyDesc rigidbody, int entityId)
        {
            _boxes.Add(new BoxColliderDesc { collider = desc, rigidbody = rigidbody, entityId = entityId });
        }

        public void AddCapsule(in CapsuleDesc desc, in RigidbodyDesc rigidbody, int entityId)
        {
            _capsules.Add(new CapsuleColliderDesc { collider = desc, rigidbody = rigidbody, entityId = entityId });
        }

        public void Clear()
        {
            _spheres.Clear();
            _boxes.Clear();
            _capsules.Clear();
        }

        public void Dispose()
        {
            if (_spheres.IsCreated) _spheres.Dispose();
            if (_boxes.IsCreated) _boxes.Dispose();
            if (_capsules.IsCreated) _capsules.Dispose();
        }
    }

    // ============ 供 Jobs 写回：刚体速度修正 ============
    public struct RigidBodyForceApply
    {
        public int entityId;
        public float3 velocity;
    }
}