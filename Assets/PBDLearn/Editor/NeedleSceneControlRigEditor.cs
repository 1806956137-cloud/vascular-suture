using UnityEditor;
using UnityEngine;

namespace PBDLearn
{
    [CustomEditor(typeof(NeedleSceneControlRig))]
    public class NeedleSceneControlRigEditor : Editor
    {
        private enum FocusHandle
        {
            None,
            Move,
            Entry,
            Advance,
            Steer,
            Roll
        }

        private static FocusHandle s_focus = FocusHandle.None;
        private static bool s_draggingPanel;
        private static Vector2 s_dragStartMouse;
        private static Vector2 s_dragStartOffset;

        private NeedleSceneControlRig Rig => (NeedleSceneControlRig)target;

        private void OnDisable()
        {
            Tools.hidden = false;
            s_draggingPanel = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDefaultInspector();
            EditorGUILayout.Space();

            DrawWarnings();
            DrawButtons();
            DrawUsageTips();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawWarnings()
        {
            if (Rig.targetNeedle == null)
            {
                EditorGUILayout.HelpBox("还没有指定 Target Needle。可以点“自动查找目标针”，或手动把 Capsule 拖到 targetNeedle。", MessageType.Info);
            }

            if (Rig.HasInvalidTargetHierarchy)
            {
                EditorGUILayout.HelpBox(
                    "当前层级关系不对：NeedleSceneControlRig 不能和 targetNeedle 互为父子，否则会出现“自己追自己”、位置不断累加、针飞走的问题。\n\n请让 Rig 和 Capsule 平级。",
                    MessageType.Error);
            }

            if (Rig.needleDriver == null && Rig.targetNeedle != null)
            {
                EditorGUILayout.HelpBox("targetNeedle 上还没有自动绑定到 NeedleManualDriver。可以点“自动绑定 ManualDriver”。", MessageType.Warning);
            }
        }

        private void DrawButtons()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Rig Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Create Or Repair Rig"))
            {
                Undo.RecordObject(Rig, "Create Or Repair Rig");
                Rig.CreateOrRepairRig();
                EditorUtility.SetDirty(Rig);
            }

            if (GUILayout.Button("Auto Find Target Needle"))
            {
                Undo.RecordObject(Rig, "Auto Find Target Needle");
                Rig.AutoFindTargetNeedle();
                EditorUtility.SetDirty(Rig);
            }

            if (GUILayout.Button("Auto Assign Manual Driver"))
            {
                Undo.RecordObject(Rig, "Auto Assign Manual Driver");
                Rig.AutoAssignDriver();
                EditorUtility.SetDirty(Rig);
            }

            if (GUILayout.Button("Snap Rig To Current Needle"))
            {
                RecordAllRigNodes("Snap Rig To Current Needle");
                Rig.SnapRigToCurrentNeedle();
                SetDirtyAll();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Reset Rig Offsets"))
            {
                RecordAllRigNodes("Reset Rig Offsets");
                Rig.ResetRigOffsets();
                Rig.ForceDrive();
                SetDirtyAll();
            }

            if (GUILayout.Button("Reset Orientation Offsets"))
            {
                RecordAllRigNodes("Reset Orientation Offsets");
                Rig.ResetOrientationOffsets();
                Rig.ForceDrive();
                SetDirtyAll();
            }

            if (GUILayout.Button("Reset Advance"))
            {
                RecordAllRigNodes("Reset Advance");
                Rig.ResetAdvance();
                Rig.ForceDrive();
                SetDirtyAll();
            }

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Advance +1 mm"))
            {
                RecordAllRigNodes("Advance Needle");
                Rig.NudgeAdvance(0.001f);
                Rig.ForceDrive();
                SetDirtyAll();
            }

            if (GUILayout.Button("Retract -1 mm"))
            {
                RecordAllRigNodes("Retract Needle");
                Rig.NudgeAdvance(-0.001f);
                Rig.ForceDrive();
                SetDirtyAll();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("拉线（下一段 Hist）"))
            {
                bool ok = Rig.RequestPullLine();
                if (!ok)
                {
                    Debug.LogWarning("[NeedleSceneControlRig] 未找到与当前 targetNeedle 对应的 UnityColliderProxy，无法提交“拉线”请求。");
                }
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("清除高亮"))
            {
                s_focus = FocusHandle.None;
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUsageTips()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Scene 操作说明", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. 推进 / 转向 / 滚转仍然是主控。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("2. 控制面板现在支持两种方式：跟随针尖，或固定在 Scene 四角。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("3. allowDragControlPanel 开启后，可以拖动面板标题栏。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("4. 想固定在左上角时，把 controlPanelAnchorMode 设成 TopLeft。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("5. “拉线”是手动触发：它会把当前完整孔位转 Hist，并创建下一段相邻 Hist 弱线段，例如 Hist1<->Hist2、Hist2<->Hist3。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("6. 拉线不会标 Done，也不会做最终打结；最终强拉/打结后续单独处理。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void OnSceneGUI()
        {
            if (Rig == null || !Rig.showSceneHandles)
                return;

            Rig.CreateOrRepairRig();
            if (Rig.root == null || Rig.needlePose == null)
                return;

            Tools.hidden = true;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            DrawFloatingControlPanel();

            if (ShouldDraw(FocusHandle.Move) && Rig.showRootMoveHandle)
                DrawRootMoveHandle();

            if (ShouldDraw(FocusHandle.Entry) && Rig.showEntryAimHandle)
                DrawEntryAimHandle();

            if (ShouldDraw(FocusHandle.Advance) && Rig.showNeedleAxisMoveHandle)
                DrawNeedleAxisMoveHandle();

            if (ShouldDraw(FocusHandle.Steer) && Rig.showSteerHandle)
                DrawSteerHandle();

            if (ShouldDraw(FocusHandle.Roll) && Rig.showRollHandle)
                DrawRollHandle();

            if (Rig.showRootRotateHandle && ShouldDraw(FocusHandle.Move))
                DrawRootRotateHandle();
        }

        private bool ShouldDraw(FocusHandle handle)
        {
            return s_focus == FocusHandle.None || s_focus == handle;
        }

        private Color FocusColor(Color c, FocusHandle handle)
        {
            if (s_focus == FocusHandle.None || s_focus == handle)
                return c;

            c.a *= 0.12f;
            return c;
        }

        private void DrawRootMoveHandle()
        {
            if (Rig.root == null)
                return;

            float size = Mathf.Max(Rig.handleSize * 0.95f, HandleUtility.GetHandleSize(Rig.root.position) * 0.09f);
            Handles.color = FocusColor(new Color(0.95f, 0.95f, 0.95f, 1f), FocusHandle.Move);

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.FreeMoveHandle(
                Rig.root.position,
                size,
                Vector3.zero,
                Handles.RectangleHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Rig.root, "Move Needle Rig");
                Rig.root.position = newPos;
                Rig.ForceDrive();
                SetDirtyAll();
            }
        }

        private void DrawRootRotateHandle()
        {
            if (Rig.root == null)
                return;

            Handles.color = FocusColor(new Color(0.85f, 0.85f, 0.85f, 0.9f), FocusHandle.Move);

            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(Rig.root.rotation, Rig.root.position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Rig.root, "Rotate Needle Rig");
                Rig.root.rotation = newRot;
                Rig.ForceDrive();
                SetDirtyAll();
            }
        }

        private void DrawEntryAimHandle()
        {
            if (Rig.root == null || Rig.entryAim == null)
                return;

            Vector3 origin = Rig.root.position;
            Vector3 handlePos = Rig.GetEntryHandleWorldPosition();
            Vector3 dir = handlePos - origin;
            if (dir.sqrMagnitude < 1e-10f) dir = Rig.root.forward;
            dir.Normalize();

            float size = Mathf.Max(Rig.handleSize * 0.9f, HandleUtility.GetHandleSize(handlePos) * 0.09f);
            Handles.color = FocusColor(new Color(1f, 0.86f, 0.15f, 1f), FocusHandle.Entry);

            Handles.DrawAAPolyLine(4f, origin, handlePos);
            Handles.ConeHandleCap(0, handlePos, Quaternion.LookRotation(dir, Vector3.up), size * 1.8f, EventType.Repaint);

            EditorGUI.BeginChangeCheck();
            Vector3 newHandlePos = Handles.FreeMoveHandle(
                handlePos,
                size * 0.75f,
                Vector3.zero,
                Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                RecordAllRigNodes("Adjust Entry Aim");
                Rig.SetEntryDirectionFromWorldPoint(newHandlePos);
                Rig.ForceDrive();
                SetDirtyAll();
            }
        }

        private void DrawNeedleAxisMoveHandle()
        {
            Vector3 origin = Rig.GetSemanticTipPoint();
            Vector3 axis = Rig.GetSemanticTipForward().normalized;
            Vector3 up = Rig.GetSemanticUp().normalized;
            Vector3 handlePos = origin + axis * Mathf.Max(0.001f, Rig.needleAxisHandleLength);

            float size = Mathf.Max(Rig.handleSize * 1.05f, HandleUtility.GetHandleSize(handlePos) * 0.14f);
            Handles.color = FocusColor(new Color(0.15f, 0.65f, 1f, 1f), FocusHandle.Advance);

            Handles.DrawAAPolyLine(6f, origin, handlePos);
            Handles.ArrowHandleCap(0, handlePos, Quaternion.LookRotation(axis, up), size * 2.7f, EventType.Repaint);

            EditorGUI.BeginChangeCheck();
            Vector3 newWorld = Handles.Slider(handlePos, axis, size, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                RecordAllRigNodes("Move Along Needle Tip Axis");
                float delta = Vector3.Dot(newWorld - handlePos, axis);
                Rig.MoveRootAlongNeedleAxis(delta);
                Rig.ForceDrive();
                SetDirtyAll();
            }
        }

        private void DrawSteerHandle()
        {
            if (Rig.steer == null)
                return;

            float size = HandleUtility.GetHandleSize(Rig.steer.position) * (Rig.compactHandleMode ? 0.36f : 0.52f);
            Handles.color = FocusColor(new Color(0.12f, 1f, 1f, 0.96f), FocusHandle.Steer);

            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.Disc(
                Rig.steer.rotation,
                Rig.steer.position,
                Rig.steer.up,
                size,
                false,
                0f);

            if (EditorGUI.EndChangeCheck())
            {
                RecordAllRigNodes("Steer Needle");
                Rig.steer.rotation = newRot;
                Rig.ClampSteerLocalRotation();
                Rig.ForceDrive();
                SetDirtyAll();
            }
        }

        private void DrawRollHandle()
        {
            if (Rig.rollPivot == null || Rig.needlePose == null)
                return;

            float size = HandleUtility.GetHandleSize(Rig.needlePose.position) * 0.34f;
            Handles.color = FocusColor(new Color(1f, 0.45f, 0.15f, 0.96f), FocusHandle.Roll);

            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.Disc(
                Rig.rollPivot.rotation,
                Rig.needlePose.position,
                Rig.GetSemanticTipForward(),
                size,
                false,
                0f);

            if (EditorGUI.EndChangeCheck())
            {
                RecordAllRigNodes("Roll Needle");
                Rig.rollPivot.rotation = newRot;
                Rig.ForceDrive();
                SetDirtyAll();
            }
        }

        private void DrawFloatingControlPanel()
        {
            if (!Rig.showHandleLabels)
                return;

            Vector3 anchor = Rig.GetSemanticTipPoint();
            Vector2 panelPos = GetPanelScreenPosition(anchor);
            float w = Mathf.Max(48f, Rig.controlPanelWidth);
            float h = Mathf.Max(20f, Rig.controlPanelButtonHeight);
            float gap = Mathf.Max(0f, Rig.controlPanelButtonGap);
            float headerH = Mathf.Max(18f, Rig.controlPanelHeaderHeight);

            Handles.BeginGUI();

            int count = 0;
            if (Rig.showNeedleAxisMoveHandle) count++;
            if (Rig.showSteerHandle) count++;
            if (Rig.showRollHandle) count++;
            if (Rig.showRootMoveHandle) count++;
            if (Rig.showEntryAimHandle) count++;
            count += 2;
            if (count == 0)
            {
                Handles.EndGUI();
                return;
            }

            float totalH = headerH + count * h + (count - 1) * gap + 12f;
            Rect bg = new Rect(panelPos.x, panelPos.y, w + 12f, totalH);
            Rect headerRect = new Rect(bg.x + 4f, bg.y + 4f, bg.width - 8f, headerH - 4f);

            HandlePanelDragging(bg, headerRect, anchor);

            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.46f);
            GUI.Box(bg, GUIContent.none);
            GUI.color = new Color(0f, 0f, 0f, 0.68f);
            GUI.Box(headerRect, GUIContent.none);
            GUI.color = old;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.alignment = TextAnchor.MiddleCenter;
            headerStyle.fontSize = 12;
            headerStyle.normal.textColor = new Color(0.95f, 0.95f, 0.95f, 1f);
            GUI.Label(headerRect, GetPanelHeaderText(), headerStyle);

            float y = bg.y + headerH + 4f;
            if (Rig.showNeedleAxisMoveHandle)
            {
                DrawPanelButton(new Rect(bg.x + 6f, y, w, h), "推进", FocusHandle.Advance, new Color(0.35f, 0.8f, 1f, 1f), Rig.GetSemanticTipPoint());
                y += h + gap;
            }

            if (Rig.showSteerHandle)
            {
                DrawPanelButton(new Rect(bg.x + 6f, y, w, h), "转向", FocusHandle.Steer, new Color(0.2f, 1f, 1f, 1f), Rig.steer != null ? Rig.steer.position : anchor);
                y += h + gap;
            }

            if (Rig.showRollHandle)
            {
                DrawPanelButton(new Rect(bg.x + 6f, y, w, h), "滚转", FocusHandle.Roll, new Color(1f, 0.58f, 0.28f, 1f), Rig.needlePose != null ? Rig.needlePose.position : anchor);
                y += h + gap;
            }

            if (Rig.showRootMoveHandle)
            {
                DrawPanelButton(new Rect(bg.x + 6f, y, w, h), "移动", FocusHandle.Move, new Color(0.96f, 0.96f, 0.96f, 1f), Rig.root != null ? Rig.root.position : anchor);
                y += h + gap;
            }

            if (Rig.showEntryAimHandle)
            {
                DrawPanelButton(new Rect(bg.x + 6f, y, w, h), "入针", FocusHandle.Entry, new Color(1f, 0.92f, 0.25f, 1f), Rig.GetEntryHandleWorldPosition());
                y += h + gap;
            }

            DrawPanelActionButton(new Rect(bg.x + 6f, y, w, h), "拉线", new Color(1f, 0.92f, 0.35f, 1f), () =>
            {
                if (!Rig.RequestPullLine())
                {
                    Debug.LogWarning("[NeedleSceneControlRig] 未找到与当前 targetNeedle 对应的 UnityColliderProxy，无法提交“拉线”请求。");
                }
                s_focus = FocusHandle.None;
                SceneView.RepaintAll();
            });

            Handles.EndGUI();
        }

        private Vector2 GetPanelScreenPosition(Vector3 anchorWorld)
        {
            SceneView view = SceneView.currentDrawingSceneView != null ? SceneView.currentDrawingSceneView : SceneView.lastActiveSceneView;
            if (view == null)
                return Rig.controlPanelScreenOffset;

            Rect vr = view.position;
            Vector2 anchor;
            switch (Rig.controlPanelAnchorMode)
            {
                case NeedleSceneControlRig.ControlPanelAnchorMode.TopLeft:
                    anchor = new Vector2(8f, 32f);
                    break;
                case NeedleSceneControlRig.ControlPanelAnchorMode.TopRight:
                    anchor = new Vector2(vr.width - Rig.controlPanelWidth - 24f, 32f);
                    break;
                case NeedleSceneControlRig.ControlPanelAnchorMode.BottomLeft:
                    anchor = new Vector2(8f, vr.height - 160f);
                    break;
                case NeedleSceneControlRig.ControlPanelAnchorMode.BottomRight:
                    anchor = new Vector2(vr.width - Rig.controlPanelWidth - 24f, vr.height - 160f);
                    break;
                default:
                    anchor = HandleUtility.WorldToGUIPoint(anchorWorld);
                    break;
            }

            return anchor + Rig.controlPanelScreenOffset;
        }

        private string GetPanelHeaderText()
        {
            switch (Rig.controlPanelAnchorMode)
            {
                case NeedleSceneControlRig.ControlPanelAnchorMode.TopLeft: return "控制面板（左上）";
                case NeedleSceneControlRig.ControlPanelAnchorMode.TopRight: return "控制面板（右上）";
                case NeedleSceneControlRig.ControlPanelAnchorMode.BottomLeft: return "控制面板（左下）";
                case NeedleSceneControlRig.ControlPanelAnchorMode.BottomRight: return "控制面板（右下）";
                default: return Rig.allowDragControlPanel ? "控制面板（可拖动）" : "控制面板";
            }
        }

        private void HandlePanelDragging(Rect panelRect, Rect headerRect, Vector3 anchorWorld)
        {
            if (!Rig.allowDragControlPanel)
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
                        s_dragStartOffset = Rig.controlPanelScreenOffset;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (s_draggingPanel && GUIUtility.hotControl == id)
                    {
                        Undo.RecordObject(Rig, "Drag Control Panel");
                        Rig.controlPanelScreenOffset = s_dragStartOffset + (e.mousePosition - s_dragStartMouse);
                        EditorUtility.SetDirty(Rig);
                        SceneView.RepaintAll();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (s_draggingPanel && GUIUtility.hotControl == id)
                    {
                        s_draggingPanel = false;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;
            }
        }

        private void DrawPanelButton(Rect rect, string text, FocusHandle handle, Color textColor, Vector3 pivotWorld)
        {
            bool active = s_focus == handle;

            Color old = GUI.color;
            GUI.color = active ? new Color(0.18f, 0.18f, 0.18f, 0.92f) : new Color(0f, 0f, 0f, 0.68f);
            if (GUI.Button(rect, GUIContent.none, GUI.skin.box))
            {
                s_focus = active ? FocusHandle.None : handle;
                if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.pivot = pivotWorld;
                    SceneView.lastActiveSceneView.Repaint();
                }
            }
            GUI.color = old;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 13;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = textColor;
            GUI.Label(rect, text, style);
        }

        private void DrawPanelActionButton(Rect rect, string text, Color textColor, System.Action onClick)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.74f);
            if (GUI.Button(rect, GUIContent.none, GUI.skin.box))
            {
                onClick?.Invoke();
            }
            GUI.color = old;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 13;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = textColor;
            GUI.Label(rect, text, style);
        }

        private void RecordAllRigNodes(string undoName)
        {
            if (Rig == null) return;

            Undo.RecordObject(Rig, undoName);
            if (Rig.root != null) Undo.RecordObject(Rig.root, undoName);
            if (Rig.entryAim != null) Undo.RecordObject(Rig.entryAim, undoName);
            if (Rig.rollPivot != null) Undo.RecordObject(Rig.rollPivot, undoName);
            if (Rig.advance != null) Undo.RecordObject(Rig.advance, undoName);
            if (Rig.steer != null) Undo.RecordObject(Rig.steer, undoName);
            if (Rig.needlePose != null) Undo.RecordObject(Rig.needlePose, undoName);
            if (Rig.targetNeedle != null) Undo.RecordObject(Rig.targetNeedle, undoName);
        }

        private void SetDirtyAll()
        {
            if (Rig == null) return;

            EditorUtility.SetDirty(Rig);
            if (Rig.root != null) EditorUtility.SetDirty(Rig.root);
            if (Rig.entryAim != null) EditorUtility.SetDirty(Rig.entryAim);
            if (Rig.rollPivot != null) EditorUtility.SetDirty(Rig.rollPivot);
            if (Rig.advance != null) EditorUtility.SetDirty(Rig.advance);
            if (Rig.steer != null) EditorUtility.SetDirty(Rig.steer);
            if (Rig.needlePose != null) EditorUtility.SetDirty(Rig.needlePose);
            if (Rig.targetNeedle != null) EditorUtility.SetDirty(Rig.targetNeedle);
        }
    }
}
