using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PBDLearn
{
    [CustomEditor(typeof(ClothDebugGizmos))]
    public class ClothDebugGizmosEditor : Editor
    {
        private ClothDebugGizmos GizmosTarget => target as ClothDebugGizmos;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var gizmos = GizmosTarget;
            if (gizmos == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Focus：按住 Shift 并用左键拖框；框中心附近最前方的真实运行时 mesh 三角会成为焦点。\n" +
                "Focus 显示受影响热区的内部三角网格、当前自动缝合状态卡，以及选中三角和一圈共享边邻接。",
                MessageType.Info);
        }

        private void OnSceneGUI()
        {
            var gizmos = GizmosTarget;
            if (gizmos == null)
                return;

            Handles.zTest = CompareFunction.Always;
            DrawFocusAutoSutureStatusCard(gizmos);
            IncisionTrianglePickingEditorUtil.HandlePickingForGizmos(gizmos);
        }

        private static void DrawFocusAutoSutureStatusCard(ClothDebugGizmos gizmos)
        {
            if (gizmos == null || !gizmos.focus || !gizmos.drawFocusAutoSutureStatusCard)
                return;

            AutoSutureRuntimeController controller = Object.FindObjectOfType<AutoSutureRuntimeController>();
            if (controller == null)
                return;

            string status = controller.FocusDebugStatusText;
            if (string.IsNullOrEmpty(status))
                return;

            int lineCount = status.Split('\n').Length;
            float height = 18f + lineCount * 19f;
            Rect card = new Rect(14f, 14f, 430f, height);
            Handles.BeginGUI();
            EditorGUI.DrawRect(card, new Color(0.025f, 0.04f, 0.065f, 0.88f));
            EditorGUI.DrawRect(new Rect(card.x, card.y, 4f, card.height), new Color(0.10f, 0.72f, 1f, 0.95f));
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                richText = false,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(12, 8, 8, 6)
            };
            style.normal.textColor = Color.white;
            GUI.Label(card, status, style);
            Handles.EndGUI();
        }
    }

    internal static class IncisionTrianglePickingEditorUtil
    {
        private const float ClickThresholdPixels = 6f;
        private const float CenterTiePixelsSq = 4f;
        private static bool _boxSelecting;
        private static int _boxSelectingGizmoId;
        private static Vector2 _boxStart;
        private static Vector2 _boxCurrent;

        public static void HandlePickingForDriver(ClothSimulationDriverDoubleLayer driver, ClothDebugGizmos preferredGizmos)
        {
            if (driver == null)
                return;

            ClothDebugGizmos gizmos = preferredGizmos != null ? preferredGizmos : FindBoundDebugGizmos(driver);
            HandlePicking(driver, gizmos);
        }

        public static void HandlePickingForGizmos(ClothDebugGizmos gizmos)
        {
            if (gizmos == null)
                return;

            ClothSimulationDriverDoubleLayer driver = ResolveDriver(gizmos);
            HandlePicking(driver, gizmos);
        }

        private static void HandlePicking(ClothSimulationDriverDoubleLayer driver, ClothDebugGizmos gizmos)
        {
            if (driver == null || gizmos == null || !gizmos.enableIncisionTrianglePicking)
                return;

            Event evt = Event.current;
            if (evt == null)
                return;

            int gizmoId = gizmos.GetInstanceID();
            bool ownsActiveBox = _boxSelecting && _boxSelectingGizmoId == gizmoId;
            bool wantsPickGesture = evt.shift || ownsActiveBox;
            int controlId = GUIUtility.GetControlID(
                "IncisionFocusBox".GetHashCode(),
                FocusType.Passive);

            if (wantsPickGesture && evt.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            if (ownsActiveBox && evt.type == EventType.Repaint)
                DrawFocusSelectionRectangle(BuildGuiRect(_boxStart, _boxCurrent));

            if (ownsActiveBox && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                CancelBoxSelection(controlId);
                evt.Use();
                SceneView.RepaintAll();
                return;
            }

            if (evt.shift && evt.type == EventType.MouseDown && evt.button == 0)
            {
                _boxSelecting = true;
                _boxSelectingGizmoId = gizmoId;
                _boxStart = evt.mousePosition;
                _boxCurrent = evt.mousePosition;
                GUIUtility.hotControl = controlId;
                evt.Use();
                SceneView.RepaintAll();
                return;
            }

            if (ownsActiveBox && evt.type == EventType.MouseDrag && evt.button == 0)
            {
                _boxCurrent = evt.mousePosition;
                evt.Use();
                SceneView.RepaintAll();
                return;
            }

            if (!ownsActiveBox || evt.type != EventType.MouseUp || evt.button != 0)
                return;

            _boxCurrent = evt.mousePosition;
            Vector2 dragDelta = _boxCurrent - _boxStart;
            Rect selectionRect = BuildGuiRect(_boxStart, _boxCurrent);
            bool treatAsClick = dragDelta.sqrMagnitude <=
                                ClickThresholdPixels * ClickThresholdPixels;
            CancelBoxSelection(controlId);

            bool hitAny = false;
            float bestDistance = float.MaxValue;
            float bestDepth = float.MaxValue;
            int bestLayer = -1;
            int bestTriIndex = -1;
            int bestIa = -1;
            int bestIb = -1;
            int bestIc = -1;
            Vector3 bestCenter = Vector3.zero;

            if (treatAsClick)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(_boxCurrent);
                TryPickClosestRuntimeMeshTriangle(driver, driver.outerMeshFilter, (int)ClothSimulationDriverDoubleLayer.VesselLayer.Outer, ray,
                    ref hitAny, ref bestDistance, ref bestLayer, ref bestTriIndex, ref bestIa, ref bestIb, ref bestIc, ref bestCenter);
                TryPickClosestRuntimeMeshTriangle(driver, driver.innerMeshFilter, (int)ClothSimulationDriverDoubleLayer.VesselLayer.Inner, ray,
                    ref hitAny, ref bestDistance, ref bestLayer, ref bestTriIndex, ref bestIa, ref bestIb, ref bestIc, ref bestCenter);
            }
            else
            {
                Camera sceneCamera = SceneView.currentDrawingSceneView != null
                    ? SceneView.currentDrawingSceneView.camera
                    : SceneView.lastActiveSceneView != null
                        ? SceneView.lastActiveSceneView.camera
                        : null;
                TryPickClosestRuntimeMeshTriangleInRect(
                    driver.outerMeshFilter,
                    (int)ClothSimulationDriverDoubleLayer.VesselLayer.Outer,
                    selectionRect,
                    sceneCamera,
                    ref hitAny,
                    ref bestDistance,
                    ref bestDepth,
                    ref bestLayer,
                    ref bestTriIndex,
                    ref bestIa,
                    ref bestIb,
                    ref bestIc,
                    ref bestCenter);
                TryPickClosestRuntimeMeshTriangleInRect(
                    driver.innerMeshFilter,
                    (int)ClothSimulationDriverDoubleLayer.VesselLayer.Inner,
                    selectionRect,
                    sceneCamera,
                    ref hitAny,
                    ref bestDistance,
                    ref bestDepth,
                    ref bestLayer,
                    ref bestTriIndex,
                    ref bestIa,
                    ref bestIb,
                    ref bestIc,
                    ref bestCenter);
            }

            evt.Use();

            if (!hitAny)
            {
                SceneView.RepaintAll();
                return;
            }

            ApplyPickedTriangle(
                gizmos,
                bestLayer,
                bestTriIndex,
                bestIa,
                bestIb,
                bestIc,
                bestCenter);
        }

        private static void ApplyPickedTriangle(
            ClothDebugGizmos gizmos,
            int layer,
            int triangleIndex,
            int ia,
            int ib,
            int ic,
            Vector3 center)
        {
            Undo.RecordObject(gizmos, "Pick Runtime Mesh Triangle");
            SerializedObject gizmosSO = new SerializedObject(gizmos);
            gizmosSO.FindProperty("pickedIncisionTriangleLayer").intValue = layer;
            gizmosSO.FindProperty("pickedIncisionTriangleIndex").intValue = triangleIndex;
            gizmosSO.FindProperty("pickedIncisionTriangleIa").intValue = ia;
            gizmosSO.FindProperty("pickedIncisionTriangleIb").intValue = ib;
            gizmosSO.FindProperty("pickedIncisionTriangleIc").intValue = ic;
            gizmosSO.FindProperty("pickedIncisionTriangleKind").intValue = -1;
            gizmosSO.FindProperty("pickedIncisionTriangleCenter").vector3Value = center;
            gizmosSO.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gizmos);
            gizmos.LogPickedIncisionTriangleToConsoleNow();
            SceneView.RepaintAll();
        }

        private static Rect BuildGuiRect(Vector2 start, Vector2 end)
        {
            return Rect.MinMaxRect(
                Mathf.Min(start.x, end.x),
                Mathf.Min(start.y, end.y),
                Mathf.Max(start.x, end.x),
                Mathf.Max(start.y, end.y));
        }

        private static void DrawFocusSelectionRectangle(Rect rect)
        {
            Handles.BeginGUI();
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.58f, 1f, 0.10f));
            Color border = new Color(0.12f, 0.72f, 1f, 0.95f);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), border);
            Handles.EndGUI();
        }

        private static void CancelBoxSelection(int controlId)
        {
            if (GUIUtility.hotControl == controlId)
                GUIUtility.hotControl = 0;

            _boxSelecting = false;
            _boxSelectingGizmoId = 0;
        }

        private static void TryPickClosestRuntimeMeshTriangle(
            ClothSimulationDriverDoubleLayer driver,
            MeshFilter meshFilter,
            int layer,
            Ray ray,
            ref bool hitAny,
            ref float bestDistance,
            ref int bestLayer,
            ref int bestTriIndex,
            ref int bestIa,
            ref int bestIb,
            ref int bestIc,
            ref Vector3 bestCenter)
        {
            if (driver == null || meshFilter == null || meshFilter.sharedMesh == null)
                return;

            Mesh mesh = meshFilter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || triangles == null || triangles.Length < 3)
                return;

            Transform tf = meshFilter.transform;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int ia = triangles[t];
                int ib = triangles[t + 1];
                int ic = triangles[t + 2];
                if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                    continue;

                Vector3 a = tf.TransformPoint(vertices[ia]);
                Vector3 b = tf.TransformPoint(vertices[ib]);
                Vector3 c = tf.TransformPoint(vertices[ic]);
                if (!RayIntersectsTriangle(ray, a, b, c, out float hitDistance))
                    continue;
                if (hitDistance >= bestDistance)
                    continue;

                hitAny = true;
                bestDistance = hitDistance;
                bestLayer = layer;
                bestTriIndex = t / 3;
                bestIa = ia;
                bestIb = ib;
                bestIc = ic;
                bestCenter = (a + b + c) / 3f;
            }
        }

        private static void TryPickClosestRuntimeMeshTriangleInRect(
            MeshFilter meshFilter,
            int layer,
            Rect selectionRect,
            Camera sceneCamera,
            ref bool hitAny,
            ref float bestScreenDistanceSq,
            ref float bestDepth,
            ref int bestLayer,
            ref int bestTriIndex,
            ref int bestIa,
            ref int bestIb,
            ref int bestIc,
            ref Vector3 bestCenter)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null || sceneCamera == null)
                return;

            Mesh mesh = meshFilter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || triangles == null || triangles.Length < 3)
                return;

            Transform tf = meshFilter.transform;
            Vector2 rectCenter = selectionRect.center;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int ia = triangles[t];
                int ib = triangles[t + 1];
                int ic = triangles[t + 2];
                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                {
                    continue;
                }

                Vector3 a = tf.TransformPoint(vertices[ia]);
                Vector3 b = tf.TransformPoint(vertices[ib]);
                Vector3 c = tf.TransformPoint(vertices[ic]);
                Vector3 center = (a + b + c) / 3f;
                float depth = Vector3.Dot(
                    center - sceneCamera.transform.position,
                    sceneCamera.transform.forward);
                if (depth <= sceneCamera.nearClipPlane)
                    continue;

                Vector2 guiPoint = HandleUtility.WorldToGUIPoint(center);
                if (!selectionRect.Contains(guiPoint))
                    continue;

                float screenDistanceSq = (guiPoint - rectCenter).sqrMagnitude;
                bool closerToCenter = screenDistanceSq <
                                      bestScreenDistanceSq - CenterTiePixelsSq;
                bool sameCenterButFrontmost =
                    Mathf.Abs(screenDistanceSq - bestScreenDistanceSq) <=
                    CenterTiePixelsSq && depth < bestDepth;
                if (hitAny && !closerToCenter && !sameCenterButFrontmost)
                    continue;

                hitAny = true;
                bestScreenDistanceSq = screenDistanceSq;
                bestDepth = depth;
                bestLayer = layer;
                bestTriIndex = t / 3;
                bestIa = ia;
                bestIb = ib;
                bestIc = ic;
                bestCenter = center;
            }
        }

        private static ClothSimulationDriverDoubleLayer ResolveDriver(ClothDebugGizmos gizmos)
        {
            if (gizmos == null)
                return null;

            if (gizmos.simulationDouble != null)
                return gizmos.simulationDouble;

            ClothSimulationDriverDoubleLayer sameObject = gizmos.GetComponent<ClothSimulationDriverDoubleLayer>();
            if (sameObject != null)
                return sameObject;

            return Object.FindObjectOfType<ClothSimulationDriverDoubleLayer>();
        }

        private static ClothDebugGizmos FindBoundDebugGizmos(ClothSimulationDriverDoubleLayer driver)
        {
            if (driver == null)
                return null;

            ClothDebugGizmos sameObject = driver.GetComponent<ClothDebugGizmos>();
            if (sameObject != null)
                return sameObject;

            ClothDebugGizmos[] all = Object.FindObjectsOfType<ClothDebugGizmos>();
            for (int i = 0; i < all.Length; i++)
            {
                ClothDebugGizmos gizmos = all[i];
                if (gizmos == null)
                    continue;

                if (gizmos.simulationDouble == driver)
                    return gizmos;
            }

            return null;
        }

        private static bool TryGetSelectedTargetTraceRecord(
            ClothSimulationDriverDoubleLayer driver,
            ClothDebugGizmos gizmos,
            out ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord record)
        {
            record = default;
            if (driver == null || gizmos == null)
                return false;

            var records = driver.IncisionTargetTraceDebugRecords;
            if (records == null || records.Count == 0)
                return false;

            int selectedIndex = Mathf.Clamp(gizmos.selectedTargetTraceFocusIndex, 0, records.Count - 1);
            record = records[selectedIndex];
            return true;
        }

        private static bool IsIncisionRemovedTriangleKind(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedDegenerateArea ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedDegenerateEdge ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedBridge ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior;
        }

        private static bool IsIncisionTargetTraceTriangleKind(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.TargetTraceFocus ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.TargetTraceNeighbor;
        }

        private static bool IsIncisionCorridorFamilyTriangleKind(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.CorridorCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.MiddleBandCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.StripInteriorCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.BridgeCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedBridge ||
                   IsIncisionTargetTraceTriangleKind(kind);
        }

        private static bool IsTriangleSelectedFocusTriangle(
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord selectedFocusRecord)
        {
            if (tri.triIndex >= 0 && selectedFocusRecord.triIndex >= 0 && tri.triIndex == selectedFocusRecord.triIndex && tri.layer == selectedFocusRecord.layer)
                return true;

            return tri.layer == selectedFocusRecord.layer &&
                   tri.ia == selectedFocusRecord.ia &&
                   tri.ib == selectedFocusRecord.ib &&
                   tri.ic == selectedFocusRecord.ic;
        }

        private static bool SharesVertexWithSelectedFocus(
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord selectedFocusRecord)
        {
            if (tri.layer != selectedFocusRecord.layer)
                return false;

            int a = tri.ia;
            int b = tri.ib;
            int c = tri.ic;
            return a == selectedFocusRecord.ia || a == selectedFocusRecord.ib || a == selectedFocusRecord.ic ||
                   b == selectedFocusRecord.ia || b == selectedFocusRecord.ib || b == selectedFocusRecord.ic ||
                   c == selectedFocusRecord.ia || c == selectedFocusRecord.ib || c == selectedFocusRecord.ic;
        }

        private static bool IsTriangleWithinSelectedFocusNeighborhood(
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            bool hasSelectedFocus,
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord selectedFocusRecord,
            float neighborRadiusSq)
        {
            var kind = (ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind)tri.kind;
            if (!hasSelectedFocus)
                return IsIncisionTargetTraceTriangleKind(kind);

            if (IsTriangleSelectedFocusTriangle(tri, selectedFocusRecord))
                return true;

            if (SharesVertexWithSelectedFocus(tri, selectedFocusRecord))
                return true;

            if ((tri.center - selectedFocusRecord.center).sqrMagnitude <= neighborRadiusSq)
                return true;

            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.TargetTraceNeighbor;
        }

        private static bool MatchesStageFamily(ClothDebugGizmos gizmos, ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            switch (gizmos.incisionTriangleStageFilter)
            {
                case ClothDebugGizmos.IncisionTriangleStageFilter.CorridorFamily:
                    return IsIncisionCorridorFamilyTriangleKind(kind);
                case ClothDebugGizmos.IncisionTriangleStageFilter.MiddleBandOnly:
                    return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.MiddleBandCandidate;
                case ClothDebugGizmos.IncisionTriangleStageFilter.StripInteriorOnly:
                    return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.StripInteriorCandidate ||
                           kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior;
                case ClothDebugGizmos.IncisionTriangleStageFilter.RemovedOnly:
                    return IsIncisionRemovedTriangleKind(kind);
                case ClothDebugGizmos.IncisionTriangleStageFilter.TargetTraceOnly:
                    return IsIncisionTargetTraceTriangleKind(kind);
                case ClothDebugGizmos.IncisionTriangleStageFilter.FocusOnly:
                    return true;
                default:
                    return true;
            }
        }

        private static bool ShouldDrawIncisionTriangleByStageFilter(
            ClothDebugGizmos gizmos,
            ClothSimulationDriverDoubleLayer.IncisionDebugTriangle tri,
            bool hasSelectedFocus,
            ClothSimulationDriverDoubleLayer.IncisionTargetTraceDebugRecord selectedFocusRecord,
            float neighborRadiusSq)
        {
            var kind = (ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind)tri.kind;
            bool matchesStage = MatchesStageFamily(gizmos, kind);
            if (!matchesStage)
                return false;

            if (gizmos.incisionTriangleStageFilter == ClothDebugGizmos.IncisionTriangleStageFilter.FocusOnly)
                return IsTriangleWithinSelectedFocusNeighborhood(tri, hasSelectedFocus, selectedFocusRecord, neighborRadiusSq);

            if (gizmos.drawOnlySelectedFocusNeighborhood && IsIncisionTargetTraceTriangleKind(kind))
                return IsTriangleWithinSelectedFocusNeighborhood(tri, hasSelectedFocus, selectedFocusRecord, neighborRadiusSq);

            return true;
        }

        private static bool IsLeftRightBoundaryInteriorTriangleKind(ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind kind)
        {
            return kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.StripInteriorCandidate ||
                   kind == ClothSimulationDriverDoubleLayer.IncisionPatchDebugKind.RemovedStripInterior;
        }

        private static bool RayIntersectsTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            const float eps = 1e-8f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 pvec = Vector3.Cross(ray.direction, edge2);
            float det = Vector3.Dot(edge1, pvec);
            if (Mathf.Abs(det) < eps)
                return false;

            float invDet = 1f / det;
            Vector3 tvec = ray.origin - a;
            float u = Vector3.Dot(tvec, pvec) * invDet;
            if (u < 0f || u > 1f)
                return false;

            Vector3 qvec = Vector3.Cross(tvec, edge1);
            float v = Vector3.Dot(ray.direction, qvec) * invDet;
            if (v < 0f || u + v > 1f)
                return false;

            float t = Vector3.Dot(edge2, qvec) * invDet;
            if (t < 0f)
                return false;

            distance = t;
            return true;
        }
    }
}
