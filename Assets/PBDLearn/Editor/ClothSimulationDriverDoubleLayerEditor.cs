using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PBDLearn
{
    [CustomEditor(typeof(ClothSimulationDriverDoubleLayer))]
    public class ClothSimulationDriverDoubleLayerEditor : Editor
    {
        private static readonly string[] s_excludedInspectorProperties =
        {
            "m_Script",
            "showIncisionAuthoringPanel",
            "allowDragIncisionPanel",
            "incisionPanelScreenOffset",
            "incisionPanelWidth",
            "incisionPanelRowHeight",
            "drawIncisionDebug",
            "drawIncisionDebugLabels",
            "incisionCenterlineColor",
            "incisionLeftLipColor",
            "incisionRightLipColor",
            "enableIncisionTargetTrace",
            "incisionTargetTraceCount",
            "incisionTargetTraceNeighborRadius",
            "incisionTargetTraceLogToConsole",
            "incisionPairMinSpacing",
            "incisionDebugMinSpacing",
            "incisionDebugDrawStride",
        };

        private static bool s_draggingPanel;
        private static Vector2 s_dragStartMouse;
        private static Vector2 s_dragStartOffset;
        private static readonly string[] s_incisionPanelModeLabels = { "切切口", "整圈切断" };

        private ClothSimulationDriverDoubleLayer Driver => target as ClothSimulationDriverDoubleLayer;

        public override void OnInspectorGUI()
        {
            var driver = Driver;
            if (driver == null)
                return;

            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                SerializedProperty scriptProperty = serializedObject.FindProperty("m_Script");
                if (scriptProperty != null)
                    EditorGUILayout.PropertyField(scriptProperty);
            }

            DrawPropertiesExcluding(serializedObject, s_excludedInspectorProperties);
            serializedObject.ApplyModifiedProperties();

            if (driver.HasSutureSideGuides)
                EditorGUILayout.HelpBox(driver.SutureSideGuideSummary, MessageType.None);
            if (!string.IsNullOrEmpty(driver.SuturePassSequenceSummary))
                EditorGUILayout.HelpBox(driver.SuturePassSequenceSummary, MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("切口 Scene 面板", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这里现在只保留切口 authoring / 预览面板。切口调试显示与调试参数请放到 ClothDebugGizmos。",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(driver.showIncisionAuthoringPanel ? "隐藏 Scene 面板" : "显示 Scene 面板"))
                {
                    Undo.RecordObject(driver, "Toggle Incision Scene Panel");
                    driver.showIncisionAuthoringPanel = !driver.showIncisionAuthoringPanel;
                    EditorUtility.SetDirty(driver);
                    SceneView.RepaintAll();
                }

                GUI.enabled = !Application.isPlaying;
                if (GUILayout.Button("生成编辑态预览"))
                {
                    Undo.RecordObject(driver, "Generate Procedural Incision Stroke");
                    driver.TryGenerateProceduralIncisionStroke(clearExisting: true, logWarnings: true);
                    EditorUtility.SetDirty(driver);
                    SceneView.RepaintAll();
                }
                GUI.enabled = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !Application.isPlaying;
                if (GUILayout.Button("清空预览"))
                {
                    Undo.RecordObject(driver, "Clear Incision Preview");
                    driver.ClearIncisionPreviewAndDebug(false);
                    driver.StopIncisionDraggingFromEditor();
                    EditorUtility.SetDirty(driver);
                    SceneView.RepaintAll();
                }

                bool autoOnce = EditorGUILayout.ToggleLeft("Play 时自动生成一次", driver.autoApplyProceduralIncisionOnStart, GUILayout.Width(180f));
                GUI.enabled = true;
                if (autoOnce != driver.autoApplyProceduralIncisionOnStart)
                {
                    Undo.RecordObject(driver, "Toggle Auto Apply Procedural Incision");
                    driver.autoApplyProceduralIncisionOnStart = autoOnce;
                    EditorUtility.SetDirty(driver);
                }
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play 中已锁定为“自动生成一次”工作流。运行后不要再手动生成预览、清空预览或再次提交切口。", MessageType.None);
            }
        }

        private void OnDisable()
        {
            s_draggingPanel = false;
            var driver = Driver;
            if (driver != null)
                driver.StopIncisionDraggingFromEditor();
        }

        private void OnSceneGUI()
        {
            var driver = Driver;
            if (driver == null)
                return;

            Handles.zTest = CompareFunction.Always;

            HandleIncisionTrianglePicking(driver);
            DrawSceneIncisionPreview(driver);

            if (driver.showIncisionAuthoringPanel)
                DrawFloatingIncisionPanel(driver);
        }

        private void HandleIncisionTrianglePicking(ClothSimulationDriverDoubleLayer driver)
        {
            IncisionTrianglePickingEditorUtil.HandlePickingForDriver(driver, null);
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

        private void DrawSceneIncisionPreview(ClothSimulationDriverDoubleLayer driver)
        {
            if (driver == null)
                return;
            if (!driver.drawIncisionPreview)
            {
                DrawSutureSideGuide(driver);
                return;
            }

            var targetStroke = driver.IncisionTargetStrokeWorld;
            if (targetStroke != null && targetStroke.Count >= 2)
            {
                Handles.color = driver.incisionPreviewColor;
                Handles.DrawAAPolyLine(5f, ToArray(targetStroke));
                DrawPolylinePoints(targetStroke, driver.incisionPreviewColor, 0.0024f);
            }

            DrawPreviewPolyline(driver.IncisionMasterChainWorld, Color.black, 4f, 0.0021f);
            DrawPreviewPolyline(driver.IncisionCenterCurveWorld, new Color(1f, 0.58f, 0.12f, 1f), 4.5f, 0.0022f);
            DrawPreviewPolyline(driver.IncisionOuterLeftBoundaryWorld, new Color(0.1f, 0.95f, 0.45f, 1f), 3.5f, 0.0019f);
            DrawPreviewPolyline(driver.IncisionOuterRightBoundaryWorld, new Color(0.05f, 0.7f, 0.25f, 1f), 3.5f, 0.0019f);
            DrawPreviewPolyline(driver.IncisionInnerLeftBoundaryWorld, new Color(0.2f, 0.75f, 1f, 1f), 3f, 0.0017f);
            DrawPreviewPolyline(driver.IncisionInnerRightBoundaryWorld, new Color(0.0f, 0.45f, 0.9f, 1f), 3f, 0.0017f);
            DrawPreviewPoints(driver.IncisionOuterStationCentersWorld, new Color(1f, 0.72f, 0.12f, 1f), 0.0019f);
            DrawPreviewPoints(driver.IncisionInnerStationCentersWorld, new Color(0.15f, 0.95f, 1f, 1f), 0.0016f);
            DrawSutureSideGuide(driver);
        }

        private static void DrawSutureSideGuide(ClothSimulationDriverDoubleLayer driver)
        {
            if (driver == null || !driver.drawSutureSideGuide || !driver.HasSutureSideGuides)
                return;

            Color sideA = driver.sutureSideAColor;
            Color sideB = driver.sutureSideBColor;
            DrawPreviewPolyline(driver.SutureSideAOuterBoundaryWorld, sideA, 6f, 0.0025f);
            DrawPreviewPolyline(driver.SutureSideAInnerBoundaryWorld, sideA, 4.5f, 0.0021f);
            DrawPreviewPolyline(driver.SutureSideBOuterBoundaryWorld, sideB, 6f, 0.0025f);
            DrawPreviewPolyline(driver.SutureSideBInnerBoundaryWorld, sideB, 4.5f, 0.0021f);

            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel);
            labelStyle.normal.textColor = Color.white;
            labelStyle.alignment = TextAnchor.MiddleCenter;

            Handles.color = sideA;
            Vector3 centerA = driver.SutureSideAGuideCenterWorld;
            Handles.SphereHandleCap(0, centerA, Quaternion.identity, HandleUtility.GetHandleSize(centerA) * 0.035f, EventType.Repaint);
            Handles.Label(centerA, driver.GetSutureSideDisplayName(ClothSimulationDriverDoubleLayer.SutureVesselSide.SideA), labelStyle);

            Handles.color = sideB;
            Vector3 centerB = driver.SutureSideBGuideCenterWorld;
            Handles.SphereHandleCap(0, centerB, Quaternion.identity, HandleUtility.GetHandleSize(centerB) * 0.035f, EventType.Repaint);
            Handles.Label(centerB, driver.GetSutureSideDisplayName(ClothSimulationDriverDoubleLayer.SutureVesselSide.SideB), labelStyle);
        }


        private static void DrawPreviewPolyline(IReadOnlyList<Vector3> points, Color color, float width, float pointSize)
        {
            if (points == null || points.Count < 2)
                return;

            Handles.color = color;
            Handles.DrawAAPolyLine(width, ToArray(points));
            DrawPolylinePoints(points, color, pointSize);
        }

        private static void DrawPreviewPoints(IReadOnlyList<Vector3> points, Color color, float pointSize)
        {
            if (points == null || points.Count == 0)
                return;

            DrawPolylinePoints(points, color, pointSize);
        }

        private void DrawFloatingIncisionPanel(ClothSimulationDriverDoubleLayer driver)
        {
            SceneView sceneView = SceneView.currentDrawingSceneView != null ? SceneView.currentDrawingSceneView : SceneView.lastActiveSceneView;
            if (sceneView == null)
                return;

            bool isSeparationMode = driver.incisionCircumferentialSeparationCut;

            float width = Mathf.Max(300f, driver.incisionPanelWidth);
            float lineH = 20f;
            float sliderH = 18f;
            float gap = 6f;
            float headerH = 26f;
            float buttonH = 26f;
            float rowY = 0f;

            // 二级面板：一级只决定“切切口 / 整圈切断”。
            // 切口页保留旧切口参数；切断页只暴露纵向材质位置和中心线切向分离距离。
            float bodyH = headerH;
            bodyH += lineH + gap;          // 一级模式选择
            bodyH += lineH + gap;          // common preview toggles
            bodyH += lineH + gap;          // auto apply
            bodyH += isSeparationMode ? (sliderH * 2f + gap * 2f) : (sliderH * 5f + gap * 5f);
            bodyH += lineH + gap;          // count
            bodyH += lineH * 2f + 8f + gap;// hint
            bodyH += lineH + gap;          // note
            bodyH += buttonH * 2f + gap * 2f;
            bodyH += 12f;

            Vector2 pos = new Vector2(10f, 24f) + driver.incisionPanelScreenOffset;
            Rect panelRect = new Rect(pos.x, pos.y, width, bodyH);
            Rect headerRect = new Rect(panelRect.x + 4f, panelRect.y + 4f, panelRect.width - 8f, headerH - 4f);

            Handles.BeginGUI();
            HandlePanelDragging(driver, headerRect);

            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.46f);
            GUI.Box(panelRect, GUIContent.none);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Box(headerRect, GUIContent.none);
            GUI.color = old;

            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal = { textColor = new Color(0.95f, 0.95f, 0.95f, 1f) }
            };
            GUI.Label(headerRect, driver.allowDragIncisionPanel ? "切口面板（可拖动）" : "切口面板", headerStyle);

            Rect content = new Rect(panelRect.x + 8f, panelRect.y + headerH + 4f, panelRect.width - 16f, panelRect.height - headerH - 8f);
            rowY = content.y;

            bool lockRuntimeAuthoring = Application.isPlaying && driver.runtimeSingleIncisionMode;

            int oldMode = driver.incisionCircumferentialSeparationCut ? 1 : 0;
            int newMode = oldMode;
            bool enableSceneIncisionMode = driver.enableSceneIncisionMode;
            bool livePreview = driver.proceduralIncisionLivePreview;
            bool drawPreview = driver.drawIncisionPreview;
            bool autoApplyOnce = driver.autoApplyProceduralIncisionOnStart;
            float newZ = driver.proceduralIncisionNormalizedZ;
            float newTheta = driver.proceduralIncisionCircumferenceAngleDeg;
            float newDir = driver.proceduralIncisionDirectionDeg;
            float newLength = driver.proceduralIncisionLength;
            float newOpen = driver.incisionOpenWidth;
            float newSeparation = driver.incisionSeparationDistance;

            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginDisabledGroup(lockRuntimeAuthoring);

            newMode = GUI.Toolbar(new Rect(content.x, rowY, content.width, lineH), oldMode, s_incisionPanelModeLabels);
            rowY += lineH + gap;

            bool editingSeparationMode = newMode == 1;

            if (editingSeparationMode)
            {
                livePreview = EditorGUI.ToggleLeft(new Rect(content.x, rowY, 96f, lineH), "实时预览", livePreview);
                drawPreview = EditorGUI.ToggleLeft(new Rect(content.x + 100f, rowY, 96f, lineH), "显示预览", drawPreview);
            }
            else
            {
                enableSceneIncisionMode = EditorGUI.ToggleLeft(new Rect(content.x, rowY, 96f, lineH), "切口编辑", enableSceneIncisionMode);
                livePreview = EditorGUI.ToggleLeft(new Rect(content.x + 100f, rowY, 96f, lineH), "实时预览", livePreview);
                drawPreview = EditorGUI.ToggleLeft(new Rect(content.x + 200f, rowY, 96f, lineH), "显示预览", drawPreview);
            }
            rowY += lineH + gap;

            autoApplyOnce = EditorGUI.ToggleLeft(new Rect(content.x, rowY, content.width, lineH), editingSeparationMode ? "Play 开始自动生成一次截断" : "Play 开始自动生成一次切口", autoApplyOnce);
            rowY += lineH + gap;

            newZ = EditorGUI.Slider(new Rect(content.x, rowY, content.width, sliderH), "纵向位置 (UV.v)", newZ, 0f, 1f);
            rowY += sliderH + gap;

            if (editingSeparationMode)
            {
                float maxSeparation = Mathf.Max(0.001f, EstimateReasonableLengthMax(driver));
                newSeparation = EditorGUI.Slider(new Rect(content.x, rowY, content.width, sliderH), "沿中心线切向拉开距离", newSeparation, 0f, maxSeparation);
                rowY += sliderH + gap;
            }
            else
            {
                newTheta = EditorGUI.Slider(new Rect(content.x, rowY, content.width, sliderH), "圆周角", newTheta, -180f, 180f);
                rowY += sliderH + gap;

                newDir = EditorGUI.Slider(new Rect(content.x, rowY, content.width, sliderH), "方向角", newDir, -90f, 90f);
                rowY += sliderH + gap;

                float maxLength = EstimateReasonableLengthMax(driver);
                newLength = EditorGUI.Slider(new Rect(content.x, rowY, content.width, sliderH), "切口长度", newLength, 0.0005f, maxLength);
                rowY += sliderH + gap;

                float maxOpen = Mathf.Max(0.0005f, EstimateReasonableOpenWidthMax(driver));
                newOpen = EditorGUI.Slider(new Rect(content.x, rowY, content.width, sliderH), "开口宽度", newOpen, 0.00005f, maxOpen);
                rowY += sliderH + gap;
            }
            EditorGUI.EndDisabledGroup();

            int pendingCount = driver.PendingIncisionPointCount;
            EditorGUI.LabelField(new Rect(content.x, rowY, content.width, lineH), editingSeparationMode ? $"当前整圈采样点：{pendingCount}" : $"当前目标线采样点：{pendingCount}");
            rowY += lineH + gap;

            string hint;
            if (Application.isPlaying)
            {
                hint = editingSeparationMode
                    ? "当前为整圈截断：运行时只提交一次，Start 已自动生成后不要再次提交。"
                    : "当前建议：运行时只保留单次切口。若 Start 已自动生成，就不要再手动提交第二次。";
            }
            else
            {
                hint = editingSeparationMode
                    ? "整圈截断使用纵向材质坐标定位拓扑站，并沿切口处真实中心线切向拉开；不使用局部 Z 对弯曲模型重新分类。"
                    : "编辑态这里只保留参数调整和预览。切口调试显示请交给 ClothDebugGizmos。";
            }
            EditorGUI.HelpBox(new Rect(content.x, rowY, content.width, lineH * 2f + 8f), hint, MessageType.None);
            rowY += lineH * 2f + 8f + gap;

            EditorGUI.LabelField(new Rect(content.x, rowY, content.width, lineH), editingSeparationMode ? "切断页只做整圈断开与两段中心线切向分离。" : "按钮只保留“预览/清空”。真正切口不要再走手动提交。", EditorStyles.miniLabel);
            rowY += lineH + gap;

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(driver, editingSeparationMode ? "Edit Circumferential Separation Cut" : "Edit Incision Authoring");
                driver.incisionCircumferentialSeparationCut = editingSeparationMode;
                driver.enableSceneIncisionMode = editingSeparationMode ? false : enableSceneIncisionMode;
                driver.proceduralIncisionLivePreview = livePreview;
                driver.drawIncisionPreview = drawPreview;
                driver.autoApplyProceduralIncisionOnStart = autoApplyOnce;
                driver.proceduralIncisionNormalizedZ = newZ;
                driver.proceduralIncisionCircumferenceAngleDeg = newTheta;
                driver.proceduralIncisionDirectionDeg = newDir;
                driver.proceduralIncisionLength = newLength;
                driver.incisionOpenWidth = newOpen;
                driver.incisionSeparationDistance = newSeparation;
                driver.StopIncisionDraggingFromEditor();
                if (!Application.isPlaying && driver.enableProceduralIncisionAuthoring && (driver.proceduralIncisionLivePreview || driver.PendingIncisionPointCount >= 2))
                    driver.TryGenerateProceduralIncisionStroke(clearExisting: true, logWarnings: false);
                EditorUtility.SetDirty(driver);
                SceneView.RepaintAll();
            }

            Rect previewRect = new Rect(content.x, rowY, content.width, buttonH);
            EditorGUI.BeginDisabledGroup(lockRuntimeAuthoring);
            if (GUI.Button(previewRect, editingSeparationMode ? "生成 / 刷新截断预览" : "生成 / 刷新编辑态预览"))
            {
                Undo.RecordObject(driver, editingSeparationMode ? "Generate Circumferential Separation Preview" : "Generate Procedural Incision Stroke");
                driver.TryGenerateProceduralIncisionStroke(clearExisting: true, logWarnings: true);
                EditorUtility.SetDirty(driver);
                SceneView.RepaintAll();
            }
            rowY += buttonH + gap;

            Rect clearRect = new Rect(content.x, rowY, content.width, buttonH);
            if (GUI.Button(clearRect, "清空预览"))
            {
                Undo.RecordObject(driver, "Clear Incision Preview");
                driver.ClearIncisionPreviewAndDebug(false);
                driver.StopIncisionDraggingFromEditor();
                EditorUtility.SetDirty(driver);
                SceneView.RepaintAll();
            }
            EditorGUI.EndDisabledGroup();

            Handles.EndGUI();
        }

        private void HandlePanelDragging(ClothSimulationDriverDoubleLayer driver, Rect headerRect)
        {
            if (!driver.allowDragIncisionPanel)
                return;

            Event e = Event.current;
            if (e == null)
                return;

            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && headerRect.Contains(e.mousePosition))
                    {
                        s_draggingPanel = true;
                        s_dragStartMouse = e.mousePosition;
                        s_dragStartOffset = driver.incisionPanelScreenOffset;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (s_draggingPanel && GUIUtility.hotControl == id)
                    {
                        Undo.RecordObject(driver, "Drag Incision Panel");
                        driver.incisionPanelScreenOffset = s_dragStartOffset + (e.mousePosition - s_dragStartMouse);
                        driver.StopIncisionDraggingFromEditor();
                        EditorUtility.SetDirty(driver);
                        SceneView.RepaintAll();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (s_draggingPanel && GUIUtility.hotControl == id)
                    {
                        s_draggingPanel = false;
                        driver.StopIncisionDraggingFromEditor();
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        private static void DrawPolylinePoints(System.Collections.Generic.IReadOnlyList<Vector3> points, Color color, float scale)
        {
            if (points == null)
                return;

            Handles.color = color;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                Handles.DotHandleCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * Mathf.Max(0.008f, scale * 8f), EventType.Repaint);
            }
        }

        private static Vector3[] ToArray(System.Collections.Generic.IReadOnlyList<Vector3> points)
        {
            if (points == null)
                return System.Array.Empty<Vector3>();

            Vector3[] arr = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
                arr[i] = points[i];
            return arr;
        }

        private static float EstimateReasonableLengthMax(ClothSimulationDriverDoubleLayer driver)
        {
            MeshFilter filter = driver != null ? driver.outerMeshFilter : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return 0.05f;

            Bounds b = mesh.bounds;
            float radius = 0.5f * (Mathf.Abs(b.extents.x) + Mathf.Abs(b.extents.y));
            float circumference = Mathf.Max(0.005f, 2f * Mathf.PI * Mathf.Max(0.0005f, radius));
            float axial = Mathf.Max(0.005f, Mathf.Abs(b.size.z));
            return Mathf.Max(0.005f, Mathf.Min(Mathf.Max(axial, circumference), Mathf.Max(axial, circumference * 0.9f)));
        }

        private static float EstimateReasonableOpenWidthMax(ClothSimulationDriverDoubleLayer driver)
        {
            MeshFilter filter = driver != null ? driver.outerMeshFilter : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return 0.006f;

            Bounds b = mesh.bounds;
            float minDim = Mathf.Min(Mathf.Abs(b.size.x), Mathf.Abs(b.size.y), Mathf.Abs(b.size.z));
            return Mathf.Max(0.0005f, minDim * 0.12f);
        }
    }
}
