using UnityEngine;

namespace PBDLearn
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class NeedleSceneControlRig : MonoBehaviour
    {
        private static readonly System.Collections.Generic.HashSet<int> s_finishPunctureRequests = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.Dictionary<int, AutoSuturePositionPunctureRequest> s_autoSuturePositionFinishRequests = new System.Collections.Generic.Dictionary<int, AutoSuturePositionPunctureRequest>();
        private static readonly System.Collections.Generic.Dictionary<int, AutoSuturePullLineRequest> s_autoSuturePullLineRequests = new System.Collections.Generic.Dictionary<int, AutoSuturePullLineRequest>();
        private static readonly System.Collections.Generic.HashSet<int> s_pullLineRequests = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.HashSet<int> s_strongPullLineRequests = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.HashSet<int> s_releaseRetainedPortalRequests = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.List<NeedleSceneControlRig> s_activeRigs = new System.Collections.Generic.List<NeedleSceneControlRig>();
        private float autoSuturePositionPoseLockUntil = -1f;
        private float autoSuturePositionPunctureContactUntil = -1f;
        public struct AutoSuturePositionPunctureRequest
        {
            public Vector3 entryWorld;
            public Vector3 exitWorld;
            public NeedlePortalWallLayer entryLayer;
            public bool hasPath;
        }
        public struct AutoSuturePullLineRequest
        {
            public int passA;
            public int passB;
            public bool strongPullLine;
        }
        public enum ControlPanelAnchorMode
        {
            FollowTip = 0,
            TopLeft = 1,
            TopRight = 2,
            BottomLeft = 3,
            BottomRight = 4,
        }

        [Header("Target")]
        [Tooltip("󡣽ָ CapsuleCollider / UnityCapsuleColliderProxy / NeedleManualDriver 塣")]
        public Transform targetNeedle;

        [Tooltip("ѡ Rigidbodyʱдλˡ")]
        public Rigidbody targetRigidbody;

        [Tooltip("ѡĿ NeedleManualDriverԶͬ")]
        public NeedleManualDriver needleDriver;

        [Header("Rig Nodes")]
        [Tooltip("ƽƽڵ㡣ͨǱ塣")]
        public Transform root;

        [Tooltip("ǶȣҪ / ƫĬѹرʾֻΪѡֵ")]
        public Transform entryAim;

        [Tooltip("תڵ㣺г")]
        public Transform rollPivot;

        [Tooltip("ƽڵ㣺Աڲ㼶Ҫֶȥѡ")]
        public Transform advance;

        [Tooltip("ǻתڵ㣺ѪܺСǶת")]
        public Transform steer;

        [Tooltip("λ˽ڵ㡣")]
        public Transform needlePose;

        [Header("Drive")]
        public bool driveInEditMode = true;
        public bool driveInPlayMode = true;
        public bool autoCreateMissingNodes = true;
        public bool autoFindTargetNeedle = true;
        public bool autoAssignManualDriver = true;

        [Header("Scene Handles")]
        public bool showSceneHandles = true;
        public bool compactHandleMode = true;

        [Tooltip("ʾƶĬϹرգҪܡ")]
        public bool showRootMoveHandle = false;

        [Tooltip("ʾתĬϹرգҡ")]
        public bool showRootRotateHandle = false;

        [Tooltip("ʾ뷽ơĬϹرգҪڴֵʼǡ")]
        public bool showEntryAimHandle = false;

        [Tooltip("ʾʵƽ / س")]
        public bool showNeedleAxisMoveHandle = true;

        [Tooltip("ʾǻת")]
        public bool showSteerHandle = true;

        [Tooltip("ʾתĬϿ")]
        public bool showRollHandle = true;

        [Tooltip("ʾưť塣")]
        public bool showHandleLabels = true;

        [Min(0.001f)] public float handleSize = 0.012f;
        [Min(0.001f)] public float entryHandleLength = 0.02f;
        [Min(0.001f)] public float needleAxisHandleLength = 0.025f;
        [Min(0.0005f)] public float labelOffset = 0.008f;

        [Header("Control Panel")]
        [Tooltip("êʽ⣬̶ Scene ͼĽǡ")]
        public ControlPanelAnchorMode controlPanelAnchorMode = ControlPanelAnchorMode.TopLeft;

        [Tooltip(" Scene ϶塣")]
        public bool allowDragControlPanel = true;

        [Tooltip("ťêĻƫƣأʱʾ⣻̶ʱʾԽ䡣")]
        public Vector2 controlPanelScreenOffset = new Vector2(16f, 16f);

        [Min(48f)] public float controlPanelWidth = 76f;
        [Min(20f)] public float controlPanelButtonHeight = 26f;
        [Min(0f)] public float controlPanelButtonGap = 6f;
        [Min(18f)] public float controlPanelHeaderHeight = 22f;

        [Header("Motion Limits")]
        [Tooltip("ƽڵСֲ ZС 0ʾˡ")]
        public float minAdvanceDistance = -0.02f;

        [Tooltip("ƽڵֲ Z")]
        public float maxAdvanceDistance = 0.08f;

        [Tooltip("ǷǻתľֲǶȡ")]
        public bool clampSteer = true;

        [Range(0f, 89f)] public float maxSteerPitch = 55f;
        [Range(0f, 89f)] public float maxSteerYaw = 55f;

        [Header("Scene Guide")]
        public bool drawGuide = true;
        public bool guideForwardOnly = true;
        [Min(0.001f)] public float guideAxisLength = 0.014f;
        [Min(0.0001f)] public float guidePointRadius = 0.0014f;

        public bool IsDrivingTarget
        {
            get { return enabled && (Application.isPlaying ? driveInPlayMode : driveInEditMode); }
        }

        public bool HasInvalidTargetHierarchy
        {
            get
            {
                if (targetNeedle == null) return false;
                if (targetNeedle == transform) return true;
                if (targetNeedle.IsChildOf(transform)) return true;
                if (transform.IsChildOf(targetNeedle)) return true;
                return false;
            }
        }

        public float AdvanceDistance
        {
            get
            {
                CreateOrRepairRig();
                if (advance == null) return 0f;
                return advance.localPosition.z;
            }
        }

        public bool IsDriving(Transform needle)
        {
            return needle != null && targetNeedle == needle && IsDrivingTarget;
        }

        public int GetTargetColliderEntityId()
        {
            AutoFindTargetNeedle();

            Transform needle = targetNeedle;
            if (needle == null)
                return 0;

            UnityColliderProxy proxy = needle.GetComponent<UnityColliderProxy>();
            if (proxy == null) proxy = needle.GetComponentInParent<UnityColliderProxy>();
            if (proxy == null) proxy = needle.GetComponentInChildren<UnityColliderProxy>();
            return proxy != null ? proxy.entityId : 0;
        }

        public static bool TryApplyNeedleTipPiercerDamping(int colliderEntityId, Vector3 worldDelta)
        {
            if (colliderEntityId == 0 || worldDelta.sqrMagnitude <= 1e-12f)
                return false;

            for (int i = s_activeRigs.Count - 1; i >= 0; i--)
            {
                NeedleSceneControlRig rig = s_activeRigs[i];
                if (rig == null)
                {
                    s_activeRigs.RemoveAt(i);
                    continue;
                }

                if (!rig.IsDrivingTarget)
                    continue;
                if (rig.GetTargetColliderEntityId() != colliderEntityId)
                    continue;

                if (rig.IsAutoSuturePositionPoseLocked())
                    return true;

                rig.ApplyNeedleTipPiercerDamping(worldDelta);
                return true;
            }

            return false;
        }

        public static bool IsAutoSuturePositionPoseLocked(int colliderEntityId)
        {
            if (colliderEntityId == 0)
                return false;

            for (int i = s_activeRigs.Count - 1; i >= 0; i--)
            {
                NeedleSceneControlRig rig = s_activeRigs[i];
                if (rig == null)
                {
                    s_activeRigs.RemoveAt(i);
                    continue;
                }

                if (!rig.IsDrivingTarget)
                    continue;
                if (rig.GetTargetColliderEntityId() != colliderEntityId)
                    continue;

                return rig.IsAutoSuturePositionPoseLocked();
            }

            return false;
        }

        public static bool IsAutoSuturePositionPunctureContactAllowed(int colliderEntityId)
        {
            if (colliderEntityId == 0)
                return false;

            for (int i = s_activeRigs.Count - 1; i >= 0; i--)
            {
                NeedleSceneControlRig rig = s_activeRigs[i];
                if (rig == null)
                {
                    s_activeRigs.RemoveAt(i);
                    continue;
                }

                if (!rig.IsDrivingTarget)
                    continue;
                if (rig.GetTargetColliderEntityId() != colliderEntityId)
                    continue;

                return rig.IsAutoSuturePositionPunctureContactAllowed();
            }

            return false;
        }

        private void ApplyNeedleTipPiercerDamping(Vector3 worldDelta)
        {
            CreateOrRepairRig();
            if (root != null)
                root.position += worldDelta;
            else
                transform.position += worldDelta;

            ForceDrive();
        }

        [ContextMenu("Finish Puncture And Collect Needle")]
        public bool RequestFinishPuncture()
        {
            int entityId = GetTargetColliderEntityId();
            if (entityId == 0)
                return false;

            s_finishPunctureRequests.Add(entityId);
            return true;
        }

        public static bool ConsumeFinishPunctureRequest(int entityId)
        {
            if (entityId == 0)
                return false;

            return s_finishPunctureRequests.Remove(entityId);
        }

        public bool RequestAutoSuturePositionFinishPuncture(Vector3 entryWorld, Vector3 exitWorld, NeedlePortalWallLayer entryLayer)
        {
            int entityId = GetTargetColliderEntityId();
            if (entityId == 0)
                return false;

            AutoSuturePositionPunctureRequest request = new AutoSuturePositionPunctureRequest()
            {
                entryWorld = entryWorld,
                exitWorld = exitWorld,
                entryLayer = entryLayer,
                hasPath = true,
            };
            s_autoSuturePositionFinishRequests[entityId] = request;
            return true;
        }

        public static bool ConsumeAutoSuturePositionFinishPunctureRequest(int entityId, out AutoSuturePositionPunctureRequest request)
        {
            request = default;
            if (entityId == 0)
                return false;

            if (!s_autoSuturePositionFinishRequests.TryGetValue(entityId, out request))
                return false;

            s_autoSuturePositionFinishRequests.Remove(entityId);
            return true;
        }

        public bool RequestAutoSuturePullLine(int passA, int passB, bool strongPullLine)
        {
            int entityId = GetTargetColliderEntityId();
            if (entityId == 0 || passA <= 0 || passB <= 0 || passA == passB)
                return false;

            s_autoSuturePullLineRequests[entityId] = new AutoSuturePullLineRequest()
            {
                passA = passA,
                passB = passB,
                strongPullLine = strongPullLine,
            };
            return true;
        }

        public static bool ConsumeAutoSuturePullLineRequest(int entityId, out AutoSuturePullLineRequest request)
        {
            request = default;
            if (entityId == 0)
                return false;

            if (!s_autoSuturePullLineRequests.TryGetValue(entityId, out request))
                return false;

            s_autoSuturePullLineRequests.Remove(entityId);
            return true;
        }

        // 兼容旧调用名：现在“收针”只表示全部穿针结束后的最终收针，而不是每一针之间手动切换。
        public bool RequestPromoteCurrentPortalToHistory()
        {
            return RequestFinishPuncture();
        }

        public static bool ConsumePromoteCurrentPortalToHistoryRequest(int entityId)
        {
            return ConsumeFinishPunctureRequest(entityId);
        }

        [ContextMenu("拉线 / Pull Line")]
        public bool RequestPullLine()
        {
            int entityId = GetTargetColliderEntityId();
            if (entityId == 0)
                return false;

            s_pullLineRequests.Add(entityId);
            return true;
        }

        public static bool ConsumePullLineRequest(int entityId)
        {
            if (entityId == 0)
                return false;

            return s_pullLineRequests.Remove(entityId);
        }

        [ContextMenu("强拉线 / Strong Pull Line")]
        public bool RequestStrongPullLine()
        {
            int entityId = GetTargetColliderEntityId();
            if (entityId == 0)
                return false;

            s_strongPullLineRequests.Add(entityId);
            return true;
        }

        public static bool ConsumeStrongPullLineRequest(int entityId)
        {
            if (entityId == 0)
                return false;

            return s_strongPullLineRequests.Remove(entityId);
        }

        // 兼容旧调用名：旧“打结”按钮现在只触发本轮的弱“拉线”流程。
        public bool RequestTieKnot()
        {
            return RequestPullLine();
        }

        public static bool ConsumeTieKnotRequest(int entityId)
        {
            return ConsumePullLineRequest(entityId);
        }

        [ContextMenu("释放保留孔位约束 / Release Retained Portals")]
        public bool RequestReleaseRetainedPortals()
        {
            int entityId = GetTargetColliderEntityId();
            if (entityId == 0)
                return false;

            s_releaseRetainedPortalRequests.Add(entityId);
            return true;
        }

        public static bool ConsumeReleaseRetainedPortalRequest(int entityId)
        {
            if (entityId == 0)
                return false;

            return s_releaseRetainedPortalRequests.Remove(entityId);
        }

        private void Reset()
        {
            CreateOrRepairRig();
            AutoFindTargetNeedle();
            AutoAssignDriver();
            SnapRigToCurrentNeedle();
        }

        private void OnEnable()
        {
            if (!s_activeRigs.Contains(this))
                s_activeRigs.Add(this);

            if (autoCreateMissingNodes)
                CreateOrRepairRig();

            if (autoFindTargetNeedle)
                AutoFindTargetNeedle();

            if (autoAssignManualDriver)
                AutoAssignDriver();
        }

        private void OnDisable()
        {
            ClearPendingRequests();
            s_activeRigs.Remove(this);
        }

        public void ClearPendingRequests()
        {
            int entityId = GetTargetColliderEntityId();
            s_finishPunctureRequests.Remove(entityId);
            s_autoSuturePositionFinishRequests.Remove(entityId);
            s_autoSuturePullLineRequests.Remove(entityId);
            s_pullLineRequests.Remove(entityId);
            s_strongPullLineRequests.Remove(entityId);
            s_releaseRetainedPortalRequests.Remove(entityId);
            autoSuturePositionPoseLockUntil = -1f;
            autoSuturePositionPunctureContactUntil = -1f;
        }

        private void OnValidate()
        {
            if (autoCreateMissingNodes)
                CreateOrRepairRig();

            if (autoFindTargetNeedle)
                AutoFindTargetNeedle();

            if (autoAssignManualDriver)
                AutoAssignDriver();

            ClampAdvance();
            ClampSteerLocalRotation();

            if (!Application.isPlaying && driveInEditMode)
                ForceDrive();
        }

        private void Update()
        {
            if (!Application.isPlaying && driveInEditMode)
                ForceDrive();
        }

        private void LateUpdate()
        {
            if (Application.isPlaying && driveInPlayMode)
                ForceDrive();
        }

        [ContextMenu("Create Or Repair Rig")]
        public void CreateOrRepairRig()
        {
            if (root == null)
                root = transform;

            entryAim = EnsureChild(root, entryAim, "EntryAim");
            rollPivot = EnsureChild(entryAim, rollPivot, "Roll");
            advance = EnsureChild(rollPivot, advance, "Advance");
            steer = EnsureChild(advance, steer, "Steer");
            needlePose = EnsureChild(steer, needlePose, "NeedlePose");

            NormalizeLocal(entryAim);
            NormalizeLocal(rollPivot);
            NormalizeLocal(advance);
            NormalizeLocal(steer);
            NormalizeLocal(needlePose);
        }

        [ContextMenu("Auto Find Target Needle")]
        public void AutoFindTargetNeedle()
        {
            if (!autoFindTargetNeedle && targetNeedle != null)
                return;

            if (targetNeedle == null)
            {
                var proxy = FindObjectOfType<UnityCapsuleColliderProxy>();
                if (proxy != null && proxy.transform != transform && !proxy.transform.IsChildOf(transform))
                    targetNeedle = proxy.transform;
            }

            if (targetNeedle != null && targetRigidbody == null)
                targetRigidbody = targetNeedle.GetComponent<Rigidbody>();
        }

        [ContextMenu("Auto Assign Manual Driver")]
        public void AutoAssignDriver()
        {
            if (!autoAssignManualDriver)
                return;

            if (needleDriver == null && targetNeedle != null)
                needleDriver = targetNeedle.GetComponent<NeedleManualDriver>();

            if (needleDriver != null)
            {
                needleDriver.sceneRig = this;
                needleDriver.sceneRigOnly = true;
            }
        }

        [ContextMenu("Snap Rig To Current Needle")]
        public void SnapRigToCurrentNeedle()
        {
            CreateOrRepairRig();
            AutoFindTargetNeedle();
            AutoAssignDriver();

            if (targetNeedle == null)
                return;

            transform.position = targetNeedle.position;
            transform.rotation = targetNeedle.rotation;

            ResetRigOffsets();
            ForceDrive();
        }

        [ContextMenu("Reset Rig Offsets")]
        public void ResetRigOffsets()
        {
            CreateOrRepairRig();

            ZeroLocal(entryAim);
            ZeroLocal(rollPivot);
            ZeroLocal(advance);
            ZeroLocal(steer);
            ZeroLocal(needlePose);

            ClampAdvance();
            ClampSteerLocalRotation();
        }

        [ContextMenu("Reset Orientation Offsets")]
        public void ResetOrientationOffsets()
        {
            CreateOrRepairRig();

            if (entryAim != null) entryAim.localRotation = Quaternion.identity;
            if (rollPivot != null) rollPivot.localRotation = Quaternion.identity;
            if (steer != null) steer.localRotation = Quaternion.identity;
            if (needlePose != null) needlePose.localRotation = Quaternion.identity;

            ClampSteerLocalRotation();
        }

        [ContextMenu("Reset Advance")]
        public void ResetAdvance()
        {
            CreateOrRepairRig();
            if (advance == null) return;

            advance.localPosition = Vector3.zero;
            ClampAdvance();
        }

        public void SetAdvanceDistance(float localZ)
        {
            CreateOrRepairRig();
            if (advance == null) return;

            localZ = Mathf.Clamp(localZ, minAdvanceDistance, maxAdvanceDistance);
            advance.localPosition = new Vector3(0f, 0f, localZ);
        }

        public void NudgeAdvance(float deltaLocalZ)
        {
            SetAdvanceDistance(AdvanceDistance + deltaLocalZ);
        }

        public void ClampAdvance()
        {
            if (advance == null) return;

            Vector3 lp = advance.localPosition;
            lp.x = 0f;
            lp.y = 0f;
            lp.z = Mathf.Clamp(lp.z, minAdvanceDistance, maxAdvanceDistance);
            advance.localPosition = lp;
        }

        public void ClampSteerLocalRotation()
        {
            if (!clampSteer || steer == null) return;

            Vector3 e = steer.localEulerAngles;
            float x = NormalizeAngle180(e.x);
            float y = NormalizeAngle180(e.y);

            x = Mathf.Clamp(x, -maxSteerPitch, maxSteerPitch);
            y = Mathf.Clamp(y, -maxSteerYaw, maxSteerYaw);

            steer.localEulerAngles = new Vector3(x, y, 0f);
        }

        public Vector3 GetEntryHandleWorldPosition()
        {
            CreateOrRepairRig();
            Transform basis = entryAim != null ? entryAim : (root != null ? root : transform);
            return basis.position + basis.forward * Mathf.Max(0.001f, entryHandleLength);
        }

        public void SetEntryDirectionFromWorldPoint(Vector3 worldPoint)
        {
            CreateOrRepairRig();
            if (entryAim == null || root == null)
                return;

            Vector3 dir = worldPoint - root.position;
            if (dir.sqrMagnitude < 1e-10f)
                return;

            entryAim.rotation = Quaternion.LookRotation(dir.normalized, root.up);
        }

        public Vector3 GetSemanticTipForward()
        {
            AutoAssignDriver();

            if (needleDriver != null)
            {
                Vector3 f = needleDriver.GetWorldTipForward();
                if (f.sqrMagnitude > 1e-8f)
                    return f.normalized;
            }

            if (targetNeedle != null)
            {
                var cap = targetNeedle.GetComponent<CapsuleCollider>();
                if (cap != null)
                {
                    switch (cap.direction)
                    {
                        case 0: return targetNeedle.right.normalized;
                        case 2: return targetNeedle.forward.normalized;
                        default: return targetNeedle.up.normalized;
                    }
                }

                return targetNeedle.forward.normalized;
            }

            if (needlePose != null && needlePose.forward.sqrMagnitude > 1e-8f)
                return needlePose.forward.normalized;

            return transform.forward.normalized;
        }

        public Vector3 GetSemanticTipPoint()
        {
            AutoAssignDriver();

            if (needleDriver != null)
                return needleDriver.GetWorldTipPoint();

            if (needlePose != null)
                return needlePose.position;

            return transform.position;
        }

        public Vector3 GetSemanticUp()
        {
            if (targetNeedle != null && targetNeedle.up.sqrMagnitude > 1e-8f)
                return targetNeedle.up.normalized;

            if (needlePose != null && needlePose.up.sqrMagnitude > 1e-8f)
                return needlePose.up.normalized;

            return transform.up.normalized;
        }

        public Vector3 GetNeedleAxisMoveHandlePosition()
        {
            return GetSemanticTipPoint() + GetSemanticTipForward() * Mathf.Max(0.001f, needleAxisHandleLength);
        }

        public void MoveRootAlongNeedleAxis(float worldDelta)
        {
            if (root == null)
                return;

            root.position += GetSemanticTipForward() * worldDelta;
        }

        public bool TrySetSemanticTipPose(Vector3 tipWorld, Vector3 forwardWorld, Vector3 upHintWorld)
        {
            return TrySetSemanticTipPoseInternal(tipWorld, forwardWorld, upHintWorld, false);
        }

        public bool TrySetAutoSutureTipPose(Vector3 tipWorld, Vector3 forwardWorld, Vector3 upHintWorld, bool allowPunctureContact = false)
        {
            return TrySetSemanticTipPoseInternal(tipWorld, forwardWorld, upHintWorld, true, allowPunctureContact);
        }

        private bool TrySetSemanticTipPoseInternal(Vector3 tipWorld, Vector3 forwardWorld, Vector3 upHintWorld, bool lockAutoSuturePose, bool allowAutoSuturePunctureContact = false)
        {
            CreateOrRepairRig();
            AutoFindTargetNeedle();
            AutoAssignDriver();

            if (targetNeedle == null || root == null)
                return false;
            if (HasInvalidTargetHierarchy)
                return false;

            if (forwardWorld.sqrMagnitude < 1e-10f)
                return false;

            Vector3 forward = forwardWorld.normalized;
            Vector3 up = upHintWorld.sqrMagnitude > 1e-10f ? upHintWorld.normalized : Vector3.up;
            up = Vector3.ProjectOnPlane(up, forward);
            if (up.sqrMagnitude < 1e-10f)
                up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 1e-10f)
                up = Vector3.ProjectOnPlane(Vector3.right, forward);
            up.Normalize();

            Vector3 localTipPoint = Vector3.zero;
            Vector3 localTipForward = Vector3.forward;
            if (needleDriver != null)
            {
                // Refresh the driver's lazy refs before reading its capsule-space semantics.
                needleDriver.GetWorldTipPoint();
                localTipPoint = needleDriver.GetCapsuleLocalTipPoint();
                localTipForward = needleDriver.tipAtC1
                    ? needleDriver.GetCapsuleLocalAxis()
                    : -needleDriver.GetCapsuleLocalAxis();
            }
            else
            {
                CapsuleCollider capsule = targetNeedle.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    Vector3 axis = GetCapsuleLocalAxis(capsule);
                    bool tipAtC1 = true;
                    UnityCapsuleColliderProxy proxy = targetNeedle.GetComponent<UnityCapsuleColliderProxy>();
                    if (proxy != null && proxy.desc.hasNeedleTip != 0)
                        tipAtC1 = proxy.desc.needleTipEnd == 1;
                    localTipPoint = capsule.center + (tipAtC1 ? axis : -axis) * Mathf.Max(0f, capsule.height * 0.5f);
                    localTipForward = tipAtC1 ? axis : -axis;
                }
            }

            if (localTipForward.sqrMagnitude < 1e-10f)
                localTipForward = Vector3.forward;
            localTipForward.Normalize();

            Vector3 localUp = BuildLocalPoseUp(localTipForward);
            Quaternion localFrame = Quaternion.LookRotation(localTipForward, localUp);
            Quaternion worldFrame = Quaternion.LookRotation(forward, up);
            Quaternion targetRotation = worldFrame * Quaternion.Inverse(localFrame);
            Vector3 scaledLocalTip = Vector3.Scale(localTipPoint, targetNeedle.lossyScale);
            CapsuleCollider poseCapsule = targetNeedle.GetComponent<CapsuleCollider>();
            if (poseCapsule != null)
            {
                bool tipAtC1 = Vector3.Dot(localTipForward, GetCapsuleLocalAxis(poseCapsule)) >= 0f;
                scaledLocalTip = NeedleCapsuleGeometry.GetScaledTipOffset(poseCapsule, tipAtC1);
            }
            Vector3 targetPosition = tipWorld - targetRotation * scaledLocalTip;

            root.SetPositionAndRotation(targetPosition, targetRotation);
            ResetRigOffsets();
            if (lockAutoSuturePose)
                MarkAutoSuturePositionPoseOwnedForFrame(allowAutoSuturePunctureContact);
            ForceDrive();
            return true;
        }

        private void MarkAutoSuturePositionPoseOwnedForFrame(bool allowPunctureContact)
        {
            if (!Application.isPlaying)
                return;

            float until = Time.time + Mathf.Max(Time.deltaTime * 2f, 0.035f);
            autoSuturePositionPoseLockUntil = Mathf.Max(autoSuturePositionPoseLockUntil, until);
            if (allowPunctureContact)
                autoSuturePositionPunctureContactUntil = Mathf.Max(autoSuturePositionPunctureContactUntil, until);
        }

        private bool IsAutoSuturePositionPoseLocked()
        {
            return Application.isPlaying && Time.time <= autoSuturePositionPoseLockUntil;
        }

        private bool IsAutoSuturePositionPunctureContactAllowed()
        {
            return Application.isPlaying && Time.time <= autoSuturePositionPunctureContactUntil;
        }

        public void ForceDrive()
        {
            DriveTarget();
        }

        private void DriveTarget()
        {
            if (Application.isPlaying && Time.timeScale == 0f)
                return;

            if (!IsDrivingTarget)
                return;

            if (needlePose == null)
                return;

            if (targetNeedle == null)
                AutoFindTargetNeedle();

            if (targetNeedle == null)
                return;

            if (HasInvalidTargetHierarchy)
                return;

            AutoAssignDriver();
            ClampAdvance();
            ClampSteerLocalRotation();

            if (Application.isPlaying && targetRigidbody != null)
            {
                if (!targetRigidbody.isKinematic)
                    targetRigidbody.isKinematic = true;

                targetRigidbody.useGravity = false;
                targetRigidbody.MovePosition(needlePose.position);
                targetRigidbody.MoveRotation(needlePose.rotation);
            }
            else
            {
                targetNeedle.SetPositionAndRotation(needlePose.position, needlePose.rotation);
            }
        }

        private static Transform EnsureChild(Transform parent, Transform current, string nodeName)
        {
            if (current != null)
            {
                current.name = nodeName;
                if (current.parent != parent)
                    current.SetParent(parent, true);
                return current;
            }

            var existing = parent.Find(nodeName);
            if (existing != null)
                return existing;

            var go = new GameObject(nodeName);
            var tr = go.transform;
            tr.SetParent(parent, false);
            tr.localPosition = Vector3.zero;
            tr.localRotation = Quaternion.identity;
            tr.localScale = Vector3.one;
            return tr;
        }

        private static void ZeroLocal(Transform t)
        {
            if (t == null) return;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }

        private static void NormalizeLocal(Transform t)
        {
            if (t == null) return;
            if (t.localScale != Vector3.one)
                t.localScale = Vector3.one;
        }

        private static float NormalizeAngle180(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }

        private static Vector3 GetCapsuleLocalAxis(CapsuleCollider capsule)
        {
            if (capsule == null)
                return Vector3.up;

            switch (capsule.direction)
            {
                case 0: return Vector3.right;
                case 2: return Vector3.forward;
                default: return Vector3.up;
            }
        }

        private static Vector3 BuildLocalPoseUp(Vector3 localForward)
        {
            Vector3 forward = localForward.sqrMagnitude > 1e-10f ? localForward.normalized : Vector3.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.92f ? Vector3.up : Vector3.right;
            up = Vector3.ProjectOnPlane(up, forward);
            if (up.sqrMagnitude < 1e-10f)
                up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            return up.sqrMagnitude > 1e-10f ? up.normalized : Vector3.up;
        }

        private void OnDrawGizmos()
        {
            if (!drawGuide)
                return;

            CreateOrRepairRig();

            Vector3 tip = GetSemanticTipPoint();
            Vector3 fwd = GetSemanticTipForward();
            Vector3 up = GetSemanticUp();

            float len = Mathf.Max(0.001f, guideAxisLength);

            Gizmos.color = new Color(0.15f, 1f, 1f, 0.95f);
            Gizmos.DrawWireSphere(tip, guidePointRadius);

            if (guideForwardOnly)
            {
                Gizmos.color = new Color(0.2f, 0.55f, 1f, 0.95f);
                Gizmos.DrawLine(tip, tip + fwd * len * 1.8f);
            }
            else
            {
                Gizmos.color = new Color(0.2f, 0.55f, 1f, 0.95f);
                Gizmos.DrawLine(tip, tip + fwd * len * 1.8f);

                Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.95f);
                Gizmos.DrawLine(tip, tip + up * len);

                Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.95f);
                Vector3 right = Vector3.Cross(up, fwd).normalized;
                if (right.sqrMagnitude > 1e-8f)
                    Gizmos.DrawLine(tip, tip + right * len);
            }
        }
    }
}
