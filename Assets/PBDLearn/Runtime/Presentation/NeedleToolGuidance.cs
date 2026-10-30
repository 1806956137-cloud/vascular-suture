using System;
using PBDLearn.UnityDrivers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDLearn.Presentation
{
    // Non-interactive, depth-tested tool hints. They never drive the needle.
    public sealed class NeedleToolGuidance : IDisposable
    {
        private readonly Material material;
        private readonly LineRenderer[] lines = new LineRenderer[24];
        private int used;
        private float width;
        public NeedleToolGuidance(Transform owner)
        {
            material = new Material(Shader.Find("Sprites/Default")) { name = "Needle tool hint" };
            for (int i = 0; i < lines.Length; i++)
            {
                var go = new GameObject("Needle tool hint " + i);
                go.transform.SetParent(owner, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.numCapVertices = 2;
                line.enabled = false;
                lines[i] = line;
            }
        }
        public void Update(NeedleDesktopInput.NeedleTool tool, NeedleSceneControlRig rig, Camera camera, bool visible)
        {
            used = 0;
            if (visible && camera != null && rig != null)
            {
                Vector3 tip = rig.GetSemanticTipPoint(), forward = rig.GetSemanticTipForward().normalized;
                Vector3 center = rig.targetNeedle != null ? rig.targetNeedle.position : tip;
                float depth = Vector3.Dot(center - camera.transform.position, camera.transform.forward);
                if (depth > camera.nearClipPlane)
                {
                    float pixel = camera.orthographic ? 2f * camera.orthographicSize / Mathf.Max(1, camera.pixelHeight) :
                        2f * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Max(1, camera.pixelHeight);
                    float size = Mathf.Clamp(pixel * 48f, 0.012f, 0.055f);
                    width = pixel * 2f;
                    Vector3 right = camera.transform.right, up = camera.transform.up;
                    switch (tool)
                    {
                        case NeedleDesktopInput.NeedleTool.Move:
                            Arrow(center - right * size, center + right * size, up, true);
                            Arrow(center - up * size, center + up * size, right, true);
                            break;
                        case NeedleDesktopInput.NeedleTool.Advance:
                            Arrow(tip + forward * size * 0.12f, tip + forward * size * 1.6f, up, false);
                            break;
                        case NeedleDesktopInput.NeedleTool.Steer:
                            Arc(tip, right, up, size, 25f, 155f);
                            Arc(tip, right, forward, size * 0.85f, 25f, 155f);
                            break;
                        case NeedleDesktopInput.NeedleTool.Roll:
                            Vector3 radial = Vector3.ProjectOnPlane(rig.GetSemanticUp(), forward).normalized;
                            Arc(center, radial, Vector3.Cross(forward, radial), size * 0.65f, 10f, 325f);
                            Segment(center - forward * size * 0.55f, center + forward * size * 0.55f);
                            // Keep the roll cue legible when the physical ring is edge-on.
                            Arc(center + right * size * 1.15f, right, up, size * 0.42f, 20f, 320f);
                            break;
                    }
                }
            }
            for (int i = used; i < lines.Length; i++) lines[i].enabled = false;
        }
        private LineRenderer Begin(int count)
        {
            LineRenderer line = lines[used++];
            line.positionCount = count;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = new Color(0.12f, 0.75f, 0.65f, 0.55f);
            line.enabled = true;
            return line;
        }
        private void Segment(Vector3 a, Vector3 b)
        {
            LineRenderer line = Begin(2); line.SetPosition(0, a); line.SetPosition(1, b);
        }
        private void Head(Vector3 end, Vector3 direction, Vector3 planeHint, float length)
        {
            Vector3 across = Vector3.ProjectOnPlane(planeHint, direction).normalized;
            if (across.sqrMagnitude < 0.1f) across = Vector3.Cross(direction, Vector3.right).normalized;
            if (across.sqrMagnitude < 0.1f) across = Vector3.Cross(direction, Vector3.up).normalized;
            Segment(end, end - direction * length + across * length * 0.5f);
            Segment(end, end - direction * length - across * length * 0.5f);
        }
        private void Arrow(Vector3 a, Vector3 b, Vector3 plane, bool both)
        {
            Segment(a, b);
            float head = Vector3.Distance(a, b) * 0.16f;
            Vector3 direction = (b-a).normalized;
            Head(b, direction, plane, head);
            if (both) Head(a, -direction, plane, head);
        }
        private void Arc(Vector3 center, Vector3 u, Vector3 v, float radius, float start, float end)
        {
            const int count = 40;
            LineRenderer line = Begin(count);
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Lerp(start, end, i / (float)(count-1)) * Mathf.Deg2Rad;
                line.SetPosition(i, center + radius * (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)));
            }
            float t = end * Mathf.Deg2Rad;
            Head(line.GetPosition(count-1), (-u * Mathf.Sin(t) + v * Mathf.Cos(t)).normalized, u * Mathf.Cos(t) + v * Mathf.Sin(t), radius * 0.24f);
        }
        public void Dispose()
        {
            foreach (LineRenderer line in lines) Release(line.gameObject);
            Release(material);
        }
        private static void Release(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
