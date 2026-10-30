using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PBDLearn.UnityDrivers
{
    [DefaultExecutionOrder(-500)]
    public sealed class NeedleDesktopInput : MonoBehaviour
    {
        public enum NeedleTool { Move, Advance, Steer, Roll }

        private NeedleSceneControlRig rig;
        private AutoSutureRuntimeController presentation;
        private bool dragging;
        private Vector3 lastPointer;
        private Vector3 queuedPointerDelta;
        private bool focused = true;
        private EventSystem pointerEventSystem;
        private PointerEventData pointerEvent;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(16);

        private bool IsPointerOverUI(Vector2 pointer)
        {
            EventSystem current = EventSystem.current;
            if (current == null) return false;
            if (pointerEvent == null || pointerEventSystem != current)
            {
                pointerEventSystem = current;
                pointerEvent = new PointerEventData(current);
            }
            pointerEvent.Reset();
            pointerEvent.position = pointer;
            uiHits.Clear();
            current.RaycastAll(pointerEvent, uiHits);
            for (int i = 0; i < uiHits.Count; i++)
                if (uiHits[i].module is UnityEngine.UI.GraphicRaycaster) return true;
            return false;
        }
        public int AppliedMotionCount { get; private set; }
        public bool InputAllowed { get; set; }
        public bool FineControl { get; set; }
        public bool NeedleSelected { get; private set; }
        public void ToggleNeedleSelection()
        {
            if (NeedleSelected) ClearSelection(); else SelectNeedle();
        }
        public void SelectNeedle() { NeedleSelected = true; CancelDrag(); }
        public void ClearSelection() { NeedleSelected = false; CancelDrag(); }

        public bool TryGetNeedleScreenSegment(out Vector3 a, out Vector3 b)
        {
            a = b = default;
            Camera camera = presentation != null ? presentation.mainCamera : null;
            if (camera == null || rig == null || rig.targetNeedle == null) return false;
            Vector3 tip = rig.GetSemanticTipPoint();
            Collider collider = rig.targetNeedle.GetComponent<Collider>();
            Vector3 center = collider != null ? collider.bounds.center : rig.targetNeedle.position;
            a = camera.WorldToScreenPoint(tip);
            b = camera.WorldToScreenPoint(center * 2f - tip);
            return a.z > 0f && b.z > 0f;
        }

        public static bool IsNearScreenSegment(Vector2 pointer, Vector2 a, Vector2 b, float radius)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0.001f ? Mathf.Clamp01(Vector2.Dot(pointer - a, ab) / ab.sqrMagnitude) : 0f;
            return (pointer - (a + ab * t)).sqrMagnitude <= radius * radius;
        }
        public NeedleTool Tool { get; private set; } = NeedleTool.Move;
        public const float MinSensitivity = 0.2f;
        public const float MaxSensitivity = 3f;
        private float sensitivity = 1f;
        public float Sensitivity
        {
            get => sensitivity;
            set { sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity); CancelDrag(); }
        }

        public static float SignedAdvancePixels(Vector2 mouseDelta, Vector2 projectedNeedleAxis)
        {
            // Follow the needle's visible axis, including after the camera or
            // needle is rotated. A head-on view uses down=advance as a fixed convention.
            Vector2 axis = projectedNeedleAxis.sqrMagnitude >= 0.01f ? projectedNeedleAxis.normalized : Vector2.down;
            return Vector2.Dot(mouseDelta, axis);
        }

        public void Initialize(NeedleSceneControlRig needleRig, AutoSutureRuntimeController cameraController)
        {
            rig = needleRig;
            presentation = cameraController;
        }

        public void SelectTool(NeedleTool tool)
        {
            Tool = tool;
            CancelDrag();
        }

        public void CancelDrag() { dragging = false; queuedPointerDelta = Vector3.zero; }
        private void OnDisable() => CancelDrag();
        private void OnApplicationFocus(bool hasFocus) { focused = hasFocus; CancelDrag(); }

        private void Update()
        {
            Camera camera = presentation != null ? presentation.mainCamera : null;
            bool overUI = IsPointerOverUI(Input.mousePosition);
            bool overOverview = presentation != null && presentation.miniMapCamera != null &&
                presentation.miniMapCamera.enabled && presentation.miniMapCamera.pixelRect.Contains(Input.mousePosition);
            if (!InputAllowed || !focused || rig == null || camera == null ||
                Time.timeScale == 0f || overUI || overOverview)
            {
                CancelDrag();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectTool(NeedleTool.Move);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SelectTool(NeedleTool.Advance);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SelectTool(NeedleTool.Steer);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SelectTool(NeedleTool.Roll);

            float precision = FineControl || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 0.2f : 1f;
            float scale = Sensitivity * precision;
            Vector3 tip = rig.GetSemanticTipPoint();
            Vector3 forward = rig.GetSemanticTipForward();
            Vector3 up = AutoSutureLifecyclePolicy.TransportPoseUp(rig.GetSemanticUp(), forward);
            Vector3 delta = queuedPointerDelta;
            queuedPointerDelta = Vector3.zero;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
            float maxMove = (Tool == NeedleTool.Move ? 0.25f : 0.035f) * dt * scale;
            float depth = Tool == NeedleTool.Move ?
                (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f) : 0f;
            if (!NeedleSelected || (delta.sqrMagnitude <= 0f && depth == 0f)) return;

            switch (Tool)
            {
                case NeedleTool.Move:
                    float worldPerPixel = camera.orthographic ? 2f * camera.orthographicSize / Mathf.Max(1, camera.pixelHeight) :
                        2f * Vector3.Distance(camera.transform.position, tip) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Max(1, camera.pixelHeight);
                    Vector3 move = (camera.transform.right * delta.x + camera.transform.up * delta.y) * worldPerPixel * scale +
                        camera.transform.forward * depth * maxMove;
                    tip += Vector3.ClampMagnitude(move, maxMove);
                    break;
                case NeedleTool.Advance:
                    Vector2 screenAxis = camera.WorldToScreenPoint(tip + forward * 0.02f) - camera.WorldToScreenPoint(tip);
                    float advancePixels = SignedAdvancePixels(delta, screenAxis);
                    tip += forward * Mathf.Clamp(advancePixels * 0.00012f * scale, -maxMove, maxMove);
                    break;
                case NeedleTool.Steer:
                    float maxAngle = 100f * dt * scale;
                    Quaternion turn = Quaternion.AngleAxis(Mathf.Clamp(delta.x * 0.18f * scale, -maxAngle, maxAngle), camera.transform.up) *
                        Quaternion.AngleAxis(Mathf.Clamp(-delta.y * 0.18f * scale, -maxAngle, maxAngle), camera.transform.right);
                    forward = turn * forward;
                    up = turn * up;
                    break;
                case NeedleTool.Roll:
                    up = Quaternion.AngleAxis(Mathf.Clamp(delta.x * 0.3f * scale, -140f * dt * scale, 140f * dt * scale), forward) * up;
                    break;
            }
            if (rig.TrySetSemanticTipPose(tip, forward, up)) AppliedMotionCount++;
        }

        private void OnGUI()
        {
            // Preserve drag deltas even when press/move/release fit inside one simulation frame.
            Event evt = Event.current;
            if (evt == null || evt.button != 0 || !InputAllowed || !focused || Time.timeScale == 0f)
                return;
            if (evt.type != EventType.MouseDown && evt.type != EventType.MouseDrag && evt.type != EventType.MouseUp)
                return;
            Camera camera = presentation != null ? presentation.mainCamera : null;
            Vector3 pointer = new Vector3(evt.mousePosition.x, Screen.height - evt.mousePosition.y, 0f);
            bool blocked = camera == null || !camera.pixelRect.Contains(pointer) ||
                IsPointerOverUI(pointer) ||
                (presentation.miniMapCamera != null && presentation.miniMapCamera.enabled && presentation.miniMapCamera.pixelRect.Contains(pointer));
            if (blocked) { CancelDrag(); return; }
            if (evt.type == EventType.MouseDown)
            {
                if (!NeedleSelected)
                {
                    bool hitNeedle = TryGetNeedleScreenSegment(out Vector3 a, out Vector3 b) &&
                        IsNearScreenSegment(pointer, a, b, 18f * Mathf.Max(0.7f, Screen.height / 1080f));
                    if (!hitNeedle) return;
                    SelectNeedle();
                }
                // A selected needle remains the drag target across background
                // clicks and tool changes; only the selection toggle releases it.
                dragging = true;
                lastPointer = pointer;
            }
            else if (evt.type == EventType.MouseDrag && dragging)
            {
                queuedPointerDelta += pointer - lastPointer;
                lastPointer = pointer;
            }
            else if (evt.type == EventType.MouseUp)
            {
                if (dragging) queuedPointerDelta += pointer - lastPointer;
                dragging = false;
            }
        }
    }
}
