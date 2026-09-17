using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PBDLearn
{
    // Shared by the physical proxy, semantic tip and whole-needle travel.
    // Scale total height first, then subtract world-space spherical caps.
    public static class NeedleCapsuleGeometry
    {
        public static void ResolveDimensions(float height, float localRadius, Vector3 scale, int direction,
            out float length, out float radius)
        {
            float axial = Mathf.Abs(scale[direction]);
            float radial = Mathf.Max(Mathf.Abs(scale[(direction + 1) % 3]), Mathf.Abs(scale[(direction + 2) % 3]));
            radius = Mathf.Max(0f, localRadius) * radial;
            length = Mathf.Max(Mathf.Max(0f, height) * axial, radius * 2f);
        }

        public static void GetDimensions(CapsuleCollider capsule, out float length, out float radius)
        {
            ResolveDimensions(capsule.height, capsule.radius, capsule.transform.lossyScale, capsule.direction, out length, out radius);
        }

        public static Vector3 GetScaledTipOffset(CapsuleCollider capsule, bool tipAtC1)
        {
            GetDimensions(capsule, out float length, out _);
            Vector3 offset = Vector3.Scale(capsule.center, capsule.transform.lossyScale);
            offset[capsule.direction] += (tipAtC1 ? 1f : -1f) * length * 0.5f;
            return offset;
        }

        public static void GetSphereCenters(Vector3 center, Vector3 axis, float length, float radius,
            out Vector3 c0, out Vector3 c1)
        {
            Vector3 offset = axis.normalized * Mathf.Max(0f, length * 0.5f - radius);
            c0 = center - offset;
            c1 = center + offset;
        }
    }

    public class UnityCapsuleColliderProxy : UnityColliderProxy
    {
        [Header("Needle Semantic")]
        [Tooltip("把该 Capsule 当作针来处理。开启后，只有固定针头端会参与 Needle Tip Indentation。")]
        [SerializeField]
        private bool _enableNeedleTipSemantic = true;

        [Tooltip("固定针头端是否位于 c1 端。关闭则表示 c0 端是针头。这个身份是固定的，不会因前进/回撤自动切换。")]
        [SerializeField]
        private bool _needleTipAtC1 = true;

        [Header("Needle Tip Probe")]
        [Tooltip("针尖语义探针向前额外前探的距离倍率（相对胶囊半径）。0=不前探；1=前探 1 个半径。")]
        [Min(0f)]
        [SerializeField]
        private float _tipProbeForwardOffsetInRadius = 1.0f;

        [Tooltip("针尖语义探针半径倍率。1=与胶囊半径一致；>1 会更早开始起效。")]
        [Min(0f)]
        [SerializeField]
        private float _tipProbeRadiusScale = 1.5f;

        [Tooltip("针头推进/回撤速度判定阈值。低于它时记为静止或横向运动，不做状态切换。")]
        [Min(0f)]
        [SerializeField]
        private float _tipAdvanceSpeedThreshold = 0.002f;

        [Tooltip("推进/回撤状态的额外滞回。用于减少速度在阈值附近抖动时的模式闪烁。")]
        [Min(0f)]
        [SerializeField]
        private float _tipMotionHysteresis = 0.001f;

        private CapsuleDesc _desc;
        private CapsuleCollider _collider;
        private int _lastUpdateFrame = -1;

        private bool _hasPrevTipCenter;
        private float3 _prevTipCenter;
        private byte _lastNeedleMotionMode;

        private CapsuleCollider unityCollider
        {
            get
            {
                if (!_collider)
                {
                    _collider = GetComponent<CapsuleCollider>();
                }
                return _collider;
            }
        }

        public CapsuleDesc desc
        {
            get
            {
                if (_lastUpdateFrame == Time.frameCount)
                {
                    return _desc;
                }

                _lastUpdateFrame = Time.frameCount;
                var collider = this.unityCollider;
                var localToWorld = collider.transform.localToWorldMatrix;

                float3 axisDir = collider.transform.up;
                if (collider.direction == 0)
                    axisDir = collider.transform.right;
                else if (collider.direction == 2)
                    axisDir = collider.transform.forward;
                axisDir = math.normalizesafe(axisDir, new float3(0f, 1f, 0f));
                NeedleCapsuleGeometry.GetDimensions(collider, out float length, out float radius);
                NeedleCapsuleGeometry.GetSphereCenters(localToWorld.MultiplyPoint3x4(collider.center),
                    new Vector3(axisDir.x, axisDir.y, axisDir.z), length, radius, out Vector3 start, out Vector3 end);
                float3 c0 = new float3(start.x, start.y, start.z);
                float3 c1 = new float3(end.x, end.y, end.z);
                float axisLen = math.max(0f, length - 2f * radius);

                _desc.c0 = c0;
                _desc.axis = new float4(axisDir, axisLen);
                _desc.radius = radius;

                if (_enableNeedleTipSemantic)
                {
                    float3 tipCenter = _needleTipAtC1 ? c1 : c0;
                    float3 tailCenter = _needleTipAtC1 ? c0 : c1;
                    float3 tipForward = _needleTipAtC1 ? axisDir : -axisDir;
                    float3 tailForward = -tipForward;

                    float tipProbeOffset = math.max(0f, _tipProbeForwardOffsetInRadius) * radius;
                    float tipProbeRadius = math.max(0f, _tipProbeRadiusScale) * radius;
                    float3 tipProbeCenter = tipCenter + tipForward * tipProbeOffset;

                    _desc.hasNeedleTip = 1;
                    _desc.needleTipEnd = (byte)(_needleTipAtC1 ? 1 : 0);
                    _desc.tipCenter = tipCenter;
                    _desc.tipProbeCenter = tipProbeCenter;
                    _desc.tipProbeRadius = tipProbeRadius;
                    _desc.tipForward = tipForward;
                    _desc.tailCenter = tailCenter;
                    _desc.tailForward = tailForward;

                    float3 tipVelocity = float3.zero;
                    float tipAdvanceSpeed = 0f;
                    byte motionMode = 0;
                    float dt = math.max(Time.deltaTime, 1e-6f);

                    if (_hasPrevTipCenter)
                    {
                        tipVelocity = (tipCenter - _prevTipCenter) / dt;
                        tipAdvanceSpeed = math.dot(tipVelocity, tipForward);

                        float enterThreshold = math.max(0f, _tipAdvanceSpeedThreshold);
                        float hysteresis = math.max(0f, _tipMotionHysteresis);
                        float switchThreshold = enterThreshold + hysteresis;

                        if (_lastNeedleMotionMode == 1)
                        {
                            if (tipAdvanceSpeed >= enterThreshold)
                                motionMode = 1;
                            else if (tipAdvanceSpeed <= -switchThreshold)
                                motionMode = 2;
                            else
                                motionMode = 0;
                        }
                        else if (_lastNeedleMotionMode == 2)
                        {
                            if (tipAdvanceSpeed <= -enterThreshold)
                                motionMode = 2;
                            else if (tipAdvanceSpeed >= switchThreshold)
                                motionMode = 1;
                            else
                                motionMode = 0;
                        }
                        else
                        {
                            if (tipAdvanceSpeed >= switchThreshold)
                                motionMode = 1;
                            else if (tipAdvanceSpeed <= -switchThreshold)
                                motionMode = 2;
                        }
                    }

                    _desc.tipVelocity = tipVelocity;
                    _desc.tipAdvanceSpeed = tipAdvanceSpeed;
                    _desc.needleMotionMode = motionMode;

                    _prevTipCenter = tipCenter;
                    _hasPrevTipCenter = true;
                    _lastNeedleMotionMode = motionMode;
                }
                else
                {
                    _desc.hasNeedleTip = 0;
                    _desc.needleTipEnd = 0;
                    _desc.tipCenter = float3.zero;
                    _desc.tipProbeCenter = float3.zero;
                    _desc.tipProbeRadius = 0f;
                    _desc.tipForward = float3.zero;
                    _desc.tailCenter = float3.zero;
                    _desc.tailForward = float3.zero;
                    _desc.tipVelocity = float3.zero;
                    _desc.tipAdvanceSpeed = 0f;
                    _desc.needleMotionMode = 0;
                    _hasPrevTipCenter = false;
                    _lastNeedleMotionMode = 0;
                }

                return _desc;
            }
        }

        public override bool PointInside(float3 p)
        {
            return IntersectUtil.PointInside(p, desc);
        }

        public override bool GetClosestSurfacePoint(float3 p, out ConcatInfo concatInfo)
        {
            return IntersectUtil.GetClosestSurfacePoint(p, desc, out concatInfo);
        }

        public override void FillColliderDesc(CollidersGroup collidersGroup)
        {
            RigidbodyDesc rigidbodyDesc = default(RigidbodyDesc);
            if (_rigidbody && !_rigidbody.isKinematic)
            {
                rigidbodyDesc.mass = _rigidbody.mass;
                rigidbodyDesc.velocity = _rigidbody.velocity;
                rigidbodyDesc.bounciness = bounciness;
            }
            collidersGroup.AddCapsule(this.desc, rigidbodyDesc, this.entityId);
        }

        private void OnDrawGizmosSelected()
        {
            var capsule = unityCollider;
            if (capsule == null) return;

            var d = desc;

            float3 axisDir = math.normalizesafe(d.axis.xyz, new float3(0, 1, 0));
            float3 c0 = d.c0;
            float3 c1 = d.c0 + axisDir * d.axis.w;

            // 轴线
            Gizmos.color = Color.white;
            Gizmos.DrawLine(c0, c1);

            // C0 / C1
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(c0, d.radius * 0.18f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(c1, d.radius * 0.18f);

            // 固定针头 / 尾端
            if (d.hasNeedleTip != 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(d.tipCenter, d.radius * 0.28f);
                Gizmos.DrawLine(d.tipCenter, d.tipCenter + d.tipForward * (d.radius * 1.5f));

                Gizmos.color = new Color(1f, 0.35f, 0.1f, 1f);
                float probeRadius = math.max(0f, d.tipProbeRadius);
                Gizmos.DrawWireSphere(d.tipProbeCenter, math.max(probeRadius, d.radius * 0.12f));
                Gizmos.DrawLine(d.tipCenter, d.tipProbeCenter);
                Gizmos.DrawLine(d.tipProbeCenter, d.tipProbeCenter + d.tipForward * (probeRadius * 1.2f));

                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(d.tailCenter, d.radius * 0.24f);
                Gizmos.DrawLine(d.tailCenter, d.tailCenter + d.tailForward * (d.radius * 1.2f));
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.Label(c0, "C0");

            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.Label(c1, "C1");

            if (d.hasNeedleTip != 0)
            {
                UnityEditor.Handles.color = Color.red;
                UnityEditor.Handles.Label(d.tipCenter, $"TIP ({(d.needleTipEnd == 1 ? "C1" : "C0")})");

                UnityEditor.Handles.color = new Color(1f, 0.35f, 0.1f, 1f);
                UnityEditor.Handles.Label(d.tipProbeCenter, $"TIP PROBE (r={d.tipProbeRadius:F4})");

                UnityEditor.Handles.color = Color.blue;
                UnityEditor.Handles.Label(d.tailCenter, $"TAIL ({(d.needleTipEnd == 1 ? "C0" : "C1")})");
            }
#endif
        }
    }
}
