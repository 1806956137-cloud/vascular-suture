using UnityEngine;

namespace PBDLearn
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public class NeedleManualDriver : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("保留给旧的多视角工具使用；Scene 控制版本身不再读取 Game 输入。")]
        public Camera controlCamera;

        [Tooltip("如果针物体上有 Rigidbody，建议拖进来。为空时会自动 GetComponent<Rigidbody>()。")]
        public Rigidbody targetRigidbody;

        [Tooltip("自动从 UnityCapsuleColliderProxy 同步针尖端点语义。建议开启。")]
        public bool syncTipSideFromProxy = true;

        [Header("Needle Semantic")]
        [Tooltip("针尖是否位于 C1。若 syncTipSideFromProxy 开启，会自动由 proxy 覆盖。")]
        public bool tipAtC1 = false;

        [Tooltip("仅保留为语义选项：表示针的旋转通常围绕针尖理解。Scene Rig 会按该语义组织。")]
        public bool rotateAroundTip = true;

        [Header("Scene Rig")]
        [Tooltip("启用后，该组件不再处理任何 Game 输入，只作为语义/调试辅助。")]
        public bool sceneRigOnly = true;

        [Tooltip("可选：显式指定 Scene Rig。为空时会自动在场景中查找。")]
        public NeedleSceneControlRig sceneRig;

        [Tooltip("运行时若检测到 Scene Rig 在驱动，则自动把刚体设为 Kinematic，避免与物理打架。")]
        public bool forceKinematicWhenSceneRigDrives = true;

        [Header("Debug Draw")]
        [Tooltip("是否在 Scene 中绘制针尖、针轴与绑定锚点。")]
        public bool drawDebug = true;

        [Tooltip("针尖调试线长度倍率（相对世界半径）。")]
        [Min(0.1f)] public float debugLineLengthMul = 2.0f;

        [Tooltip("针尖调试点半径倍率（相对世界半径）。")]
        [Min(0.01f)] public float debugPointRadiusMul = 0.08f;

        private CapsuleCollider _capsule;
        private Transform _tr;
        private UnityCapsuleColliderProxy _capsuleProxy;

        public bool IsSceneRigDriving
        {
            get
            {
                ResolveSceneRig();
                return sceneRig != null && sceneRig.IsDrivingTarget && sceneRig.IsDriving(transform);
            }
        }

        private void Awake()
        {
            EnsureRefs();
            SyncTipSideIfNeeded();
            ApplyRigidbodySafety();
        }

        private void OnEnable()
        {
            EnsureRefs();
            SyncTipSideIfNeeded();
            ResolveSceneRig();
            ApplyRigidbodySafety();
        }

        private void Update()
        {
            EnsureRefs();
            SyncTipSideIfNeeded();
            ResolveSceneRig();
            ApplyRigidbodySafety();
        }

        private void EnsureRefs()
        {
            if (_tr == null)
                _tr = transform;

            if (_capsule == null)
                _capsule = GetComponent<CapsuleCollider>();

            if (_capsuleProxy == null)
                _capsuleProxy = GetComponent<UnityCapsuleColliderProxy>();

            if (targetRigidbody == null)
                targetRigidbody = GetComponent<Rigidbody>();

            if (controlCamera == null && Camera.main != null)
                controlCamera = Camera.main;
        }

        private void ResolveSceneRig()
        {
            if (sceneRig != null)
                return;

            sceneRig = FindObjectOfType<NeedleSceneControlRig>();
            if (sceneRig != null && sceneRig.targetNeedle == null)
                sceneRig.targetNeedle = transform;
        }

        private void SyncTipSideIfNeeded()
        {
            if (!syncTipSideFromProxy || _capsuleProxy == null)
                return;

            var d = _capsuleProxy.desc;
            if (d.hasNeedleTip != 0)
                tipAtC1 = d.needleTipEnd == 1;
        }

        private void ApplyRigidbodySafety()
        {
            if (targetRigidbody == null)
                return;

            if (sceneRigOnly || (Application.isPlaying && IsSceneRigDriving))
            {
                if (forceKinematicWhenSceneRigDrives)
                    targetRigidbody.isKinematic = true;

                targetRigidbody.useGravity = false;
            }
        }

        public Vector3 GetWorldTipForward()
        {
            EnsureRefs();
            return GetWorldTipForward(targetRigidbody != null && Application.isPlaying ? targetRigidbody.rotation : _tr.rotation);
        }

        public Vector3 GetWorldTipPoint()
        {
            EnsureRefs();
            Vector3 pos = targetRigidbody != null && Application.isPlaying ? targetRigidbody.position : _tr.position;
            Quaternion rot = targetRigidbody != null && Application.isPlaying ? targetRigidbody.rotation : _tr.rotation;
            return GetWorldTipPoint(pos, rot);
        }

        public Vector3 GetWorldBindAnchor()
        {
            return GetWorldTipPoint();
        }

        public Vector3 GetWorldTipForward(Quaternion rotation)
        {
            Vector3 localAxis = GetCapsuleLocalAxis();
            Vector3 localTipForward = tipAtC1 ? localAxis : -localAxis;
            Vector3 worldForward = rotation * localTipForward;

            if (worldForward.sqrMagnitude > 1e-8f)
                return worldForward.normalized;

            return _tr != null ? _tr.forward : Vector3.forward;
        }

        public Vector3 GetWorldTipPoint(Vector3 worldPosition, Quaternion worldRotation)
        {
            Vector3 scaledLocalTip = _capsule != null
                ? NeedleCapsuleGeometry.GetScaledTipOffset(_capsule, tipAtC1) : Vector3.zero;
            return worldPosition + worldRotation * scaledLocalTip;
        }

        public Vector3 GetCapsuleLocalTipPoint()
        {
            if (_capsule == null)
                return Vector3.zero;

            Vector3 center = _capsule.center;
            Vector3 axis = GetCapsuleLocalAxis();
            float halfExtent = Mathf.Max(0f, _capsule.height * 0.5f);
            return tipAtC1 ? center + axis * halfExtent : center - axis * halfExtent;
        }

        public Vector3 GetCapsuleLocalAxis()
        {
            if (_capsule == null)
                return Vector3.up;

            switch (_capsule.direction)
            {
                case 0: return Vector3.right;
                case 2: return Vector3.forward;
                default: return Vector3.up;
            }
        }

        private float GetApproxWorldCapsuleRadius()
        {
            if (_capsule == null)
                return 0.01f;

            Vector3 s = _tr != null ? _tr.lossyScale : Vector3.one;
            switch (_capsule.direction)
            {
                case 0:
                    return _capsule.radius * Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z));
                case 2:
                    return _capsule.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
                default:
                    return _capsule.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            }
        }

        private void Reset()
        {
            _capsule = GetComponent<CapsuleCollider>();
            _capsuleProxy = GetComponent<UnityCapsuleColliderProxy>();
            targetRigidbody = GetComponent<Rigidbody>();
            if (controlCamera == null) controlCamera = Camera.main;
            ResolveSceneRig();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebug)
                return;

            EnsureRefs();
            SyncTipSideIfNeeded();
            ResolveSceneRig();

            if (_capsule == null)
                return;

            Vector3 pos = Application.isPlaying && targetRigidbody != null ? targetRigidbody.position : transform.position;
            Quaternion rot = Application.isPlaying && targetRigidbody != null ? targetRigidbody.rotation : transform.rotation;

            Vector3 tip = GetWorldTipPoint(pos, rot);
            Vector3 fwd = GetWorldTipForward(rot);
            Vector3 bindAnchor = tip;

            float worldRadius = Mathf.Max(0.0005f, GetApproxWorldCapsuleRadius());
            float pointRadius = Mathf.Max(0.0005f, worldRadius * debugPointRadiusMul);
            float lineLength = Mathf.Max(0.002f, worldRadius * debugLineLengthMul);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(tip, pointRadius);
            Gizmos.DrawLine(tip, tip + fwd * lineLength);

            Gizmos.color = IsSceneRigDriving ? new Color(0.2f, 1f, 0.5f, 0.85f) : new Color(1f, 1f, 0f, 0.65f);
            Gizmos.DrawWireSphere(bindAnchor, pointRadius * 1.5f);
        }
    }
}
