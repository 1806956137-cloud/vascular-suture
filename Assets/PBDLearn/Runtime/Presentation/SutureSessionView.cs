using System;
using System.Collections;
using PBDLearn.UnityDrivers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PBDLearn.Presentation
{
    [DefaultExecutionOrder(300)]
    public sealed class SutureSessionView : MonoBehaviour
    {
        private static readonly Color Background = new Color32(12, 23, 32, 255);
        private static readonly Color Surface = new Color32(22, 39, 50, 255);
        private static readonly Color Raised = new Color32(31, 53, 65, 255);
        private static readonly Color Teal = new Color32(104, 218, 198, 255);
        private static readonly Color Ink = new Color32(225, 239, 243, 255);
        private static readonly Color Muted = new Color32(158, 183, 196, 255);
        private SutureSessionController session;
        private SutureSurfaceGuidance surfaceGuidance;
        private NeedleToolGuidance toolGuidance;
        private bool enteringHome;
        private Button selectNeedle;
        private Button focusNeedle;
        private Slider speed;
        private Text speedLabel;
        private RectTransform needleSelection;
        private Canvas canvas;
        private Font font;
        private Sprite rounded;
        private Sprite ring;
        private Texture2D roundedTexture;
        private Texture2D ringTexture;
        private GameObject home;
        private GameObject work;
        private GameObject manual;
        private GameObject autoPanel;
        private GameObject targets;
        private GameObject modal;
        private Text homeNotice;
        private Text modeTitle;
        private Text stageTitle;
        private Text stageHint;
        private Text progressLabel;
        private Text toolHint;
        private Text guidanceQuality;
        private Text footer;
        private Text screenshotNotice;
        private Text dialogTitle;
        private Text dialogCopy;
        private Button dialogCancel;
        private Button dialogConfirm;
        private Button homeManual;
        private Button homeAuto;
        private Button start;
        private Button adjust;
        private Button pause;
        private Button pull;
        private Button fine;
        private Button guideToggle;
        private Button overviewToggle;
        private readonly Button[] tools = new Button[4];
        private readonly Image[] stitches = new Image[6];
        private readonly RectTransform[] targetPoints = new RectTransform[6];
        private Action confirmed;
        private bool pausedBeforeDialog;
        public bool ModalOpen => modal != null && modal.activeSelf;
        public Rect Viewport => new Rect(0f, 80f * canvas.scaleFactor / Mathf.Max(1, Screen.height),
            1f - 440f * canvas.scaleFactor / Mathf.Max(1, Screen.width),
            1f - 168f * canvas.scaleFactor / Mathf.Max(1, Screen.height));

        public void Build(SutureSessionController owner)
        {
            session = owner;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
            rounded = CreateSprite(false, out roundedTexture);
            ring = CreateSprite(true, out ringTexture);
            GameObject go = new GameObject("SutureSessionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            if (EventSystem.current == null)
            {
                GameObject events = new GameObject("SutureSessionEvents", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            RectTransform root = (RectTransform)go.transform;
            home = Stretch(root, "主页", Background).gameObject;
            work = Stretch(root, "缝合工作界面", Color.clear, false).gameObject;
            targets = Stretch((RectTransform)work.transform, "孔位引导", Color.clear, false).gameObject;
            BuildTargets();
            surfaceGuidance = new SutureSurfaceGuidance(transform);
            toolGuidance = new NeedleToolGuidance(transform);
            BuildHome((RectTransform)home.transform);
            BuildWork((RectTransform)work.transform);
            BuildDialog(root);
            work.SetActive(false);
        }

        private void BuildHome(RectTransform root)
        {
            Label(root, "VASCULAR  /  SIMULATION", 70, 42, 700, 40, 22, Teal);
            Label(root, "血管端对端吻合", 70, 85, 700, 32, 18, Muted);
            RectTransform content = Box(root, "模式选择", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-840, -330), new Vector2(840, 330), Color.clear, false);
            Label(content, "血管缝合\n仿真工作台", 0, 75, 760, 190, 62, Ink);
            Label(content, "从观察，到亲手操作。", 4, 304, 700, 56, 29, Teal);
            Label(content, "选择本次操作方式，进入独立的仿真流程。", 4, 375, 750, 52, 23, Muted);
            RectTransform vesselLeft = At(content, "近端结构示意", 8, 459, 185, 36, Raised, false);
            RectTransform vesselRight = At(content, "远端结构示意", 245, 459, 185, 36, Raised, false);
            vesselLeft.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            vesselRight.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            At(vesselLeft, "近端染色", 174, 0, 11, 36, Teal, false);
            At(vesselRight, "远端染色", 0, 0, 11, 36, Teal, false);
            Label(content, "双层血管  /  真实穿刺  /  缝线牵拉", 4, 540, 750, 48, 20, Muted);
            RectTransform cardA = At(content, "手动入口卡片", 850, 40, 800, 260, Surface, true);
            Label(cardA, "01   手动缝合", 34, 27, 720, 52, 33, Ink);
            Label(cardA, "键鼠操针 · 六针顺序引导 · 目标孔位提示", 36, 93, 730, 45, 22, Muted);
            homeManual = Button(cardA, "进入手动操作  →", 34, 165, 720, 60, () => StartCoroutine(EnterFromHome(true)), false);
            RectTransform cardB = At(content, "自动入口卡片", 850, 335, 800, 260, Surface, true);
            Label(cardB, "02   自动六针缝合", 34, 27, 720, 52, 33, Ink);
            Label(cardB, "观察三针定位与三针补充的完整自动流程", 36, 93, 730, 45, 22, Muted);
            homeAuto = Button(cardB, "进入自动演示  →", 34, 165, 720, 60, () => StartCoroutine(EnterFromHome(false)), false);
            RectTransform bottom = Box(root, "主页底栏", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 90), Color.clear, false);
            homeNotice = Label(bottom, "正在准备血管场景…", 70, 17, 1400, 45, 20, Muted);
            RectTransform help = Box(bottom, "说明入口", new Vector2(1, 0), Vector2.one, new Vector2(-290, 0), new Vector2(-70, 0), Color.clear, false);
            Button(help, "操作说明", 0, 8, 210, 52, ShowHelp, false);
        }

        private IEnumerator EnterFromHome(bool manualMode)
        {
            if (enteringHome || !session.Initialized) yield break;
            enteringHome = true;
            Button chosen = manualMode ? homeManual : homeAuto;
            chosen.GetComponent<Image>().color = Teal;
            chosen.GetComponentInChildren<Text>().color = Background;
            ColorBlock colors = chosen.colors; colors.disabledColor = Color.white; chosen.colors = colors;
            homeManual.interactable = homeAuto.interactable = false;
            yield return new WaitForSecondsRealtime(0.18f);
            if (manualMode) session.EnterManual(); else session.EnterAutomatic();
            enteringHome = false;
        }

        private void BuildWork(RectTransform root)
        {
            Box(root, "侧栏背景", new Vector2(1, 0), Vector2.one, new Vector2(-440, 0), Vector2.zero, Background, true);
            RectTransform header = Box(root, "顶部导航", new Vector2(0, 1), Vector2.one, new Vector2(0, -88), Vector2.zero, Background, true);
            Button(header, "←  返回主页", 24, 17, 200, 52, session.RequestHome, false);
            modeTitle = Label(header, "手动缝合", 260, 20, 920, 48, 27, Ink);
            RectTransform headerActions = Box(header, "公共操作", new Vector2(1, 0), Vector2.one, new Vector2(-480, 0), new Vector2(-24, 0), Color.clear, false);
            Button(headerActions, "操作帮助", 0, 17, 190, 52, ShowHelp, false);
            Button(headerActions, "截图 F12", 204, 17, 220, 52, Capture, false);

            RectTransform side = Box(root, "操作面板", new Vector2(1, 0), Vector2.one, new Vector2(-420, 88), new Vector2(-20, -108), Surface, true);
            progressLabel = Label(side, "第 1 针 / 共 6 针", 24, 22, 352, 42, 25, Ink);
            for (int i = 0; i < 6; i++)
            {
                RectTransform cell = At(side, "针次" + (i + 1), 24 + i * 59, 81, 52, 42, Raised, false);
                stitches[i] = cell.GetComponent<Image>();
                Text number = Label(cell, (i + 1).ToString(), 0, 0, 52, 42, 20, Ink);
                number.alignment = TextAnchor.MiddleCenter;
            }
            stageTitle = Label(side, "近端穿刺", 24, 145, 352, 42, 26, Teal);
            stageHint = Label(side, "", 24, 199, 352, 128, 21, Muted);
            manual = At(side, "手动操针工具", 0, 341, 400, 390, Color.clear, false).gameObject;
            string[] names = { "1  移动", "2  推进", "3  转向", "4  滚转" };
            for (int i = 0; i < 4; i++)
            {
                int tool = i;
                tools[i] = Button((RectTransform)manual.transform, names[i], 24 + i % 2 * 181, i / 2 * 57, 170, 47,
                    () => { session.Desktop.SelectTool((NeedleDesktopInput.NeedleTool)tool); Refresh(); }, false);
            }
            toolHint = Label((RectTransform)manual.transform, "", 24, 116, 352, 54, 19, Muted);
            speedLabel = Label((RectTransform)manual.transform, "操针速度  1.0×", 24, 181, 352, 28, 19, Muted);
            BuildSpeedSlider((RectTransform)manual.transform);
            fine = Button((RectTransform)manual.transform, "开启精细模式（Shift 临时）", 24, 260, 352, 42,
                () => { session.Desktop.FineControl = !session.Desktop.FineControl; Refresh(); }, false);
            pull = Button((RectTransform)manual.transform, "拉线", 24, 316, 352, 54, session.PullLine, true);
            autoPanel = At(side, "自动操作", 0, 341, 400, 180, Color.clear, false).gameObject;
            start = Button((RectTransform)autoPanel.transform, "开始六针缝合", 24, 0, 352, 60, session.StartAutomatic, true);
            adjust = Button((RectTransform)autoPanel.transform, "调整针位", 24, 79, 352, 48, session.TogglePlanEditing, false);
            guidanceQuality = Label(side, "", 24, 744, 352, 50, 19, Muted);
            RectTransform actions = Box(side, "本轮操作", Vector2.zero, new Vector2(1, 0), new Vector2(24, 20), new Vector2(-24, 78), Color.clear, false);
            pause = Button(actions, "暂停", 0, 0, 170, 52, session.TogglePause, false);
            Button(actions, "重置本轮", 181, 0, 170, 52, session.RequestReset, false);

            RectTransform bottom = Box(root, "底部提示", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 80), Background, true);
            footer = Label(bottom, "", 26, 15, 1360, 52, 19, Muted);
            RectTransform bottomActions = Box(bottom, "显示选项", new Vector2(1, 0), Vector2.one, new Vector2(-480, 0), new Vector2(-24, 0), Color.clear, false);
            guideToggle = Button(bottomActions, "隐藏引导", 0, 14, 200, 50, session.ToggleGuidance, false);
            overviewToggle = Button(bottomActions, "显示总览", 215, 14, 210, 50, session.ToggleOverview, false);
            Button(root, "聚焦目标 F", 28, 105, 170, 42, session.Automatic.FocusSessionTarget, false);
            Button(root, "整体视野 G", 210, 105, 170, 42, session.Automatic.FrameSessionPlan, false);
            Button(root, "恢复跟随 R", 392, 105, 170, 42, session.Automatic.RestoreSessionCamera, false);
            selectNeedle = Button(root, "取消锁定针具", 574, 105, 210, 42,
                () => { session.Desktop.ToggleNeedleSelection(); Refresh(); }, false);
            focusNeedle = Button(root, "聚焦针具", 796, 105, 170, 42, session.Automatic.FocusSessionNeedle, false);
            needleSelection = At(root, "针具选中提示", 0, 0, 34, 34, Teal, false);
            needleSelection.GetComponent<Image>().sprite = ring;
            needleSelection.anchorMin = needleSelection.anchorMax = new Vector2(0.5f, 0.5f);
            needleSelection.pivot = new Vector2(0.5f, 0.5f);
            needleSelection.gameObject.SetActive(false);
            screenshotNotice = Label(root, "", 28, 154, 1280, 40, 19, Teal);
        }

        private void BuildSpeedSlider(RectTransform parent)
        {
            RectTransform track = At(parent, "操针速度", 24, 212, 352, 32, Color.clear, true);
            Box(track, "轨道", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -3), new Vector2(0, 3), Raised, false);
            RectTransform fillArea = Box(track, "填充范围", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(10, -3), new Vector2(-10, 3), Color.clear, false);
            RectTransform fill = Box(fillArea, "当前速度", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Teal, false);
            RectTransform handleArea = Box(track, "滑块范围", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0), Color.clear, false);
            RectTransform handle = Box(handleArea, "速度滑块", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(-10, -10), new Vector2(10, 10), Ink, true);
            speed = track.gameObject.AddComponent<Slider>();
            speed.fillRect = fill;
            speed.handleRect = handle;
            speed.targetGraphic = handle.GetComponent<Image>();
            speed.direction = Slider.Direction.LeftToRight;
            speed.minValue = NeedleDesktopInput.MinSensitivity;
            speed.maxValue = NeedleDesktopInput.MaxSensitivity;
            speed.value = session.Desktop.Sensitivity;
            Navigation navigation = speed.navigation; navigation.mode = Navigation.Mode.None; speed.navigation = navigation;
            speed.onValueChanged.AddListener(value =>
            {
                session.Desktop.Sensitivity = value;
                Set(speedLabel, "操针速度  " + session.Desktop.Sensitivity.ToString("F1") + "×");
            });
        }

        private void BuildTargets()
        {
            for (int i = 0; i < targetPoints.Length; i++)
            {
                RectTransform point = At((RectTransform)targets.transform, "针次" + (i + 1), 0, 0, 1, 1, Color.clear, false);
                point.anchorMin = point.anchorMax = new Vector2(0.5f, 0.5f);
                point.pivot = new Vector2(0.5f, 0.5f);
                Text label = Label(point, "第 " + (i + 1) + " 针", -48, -34, 96, 28, 18, Teal);
                label.alignment = TextAnchor.MiddleCenter;
                label.gameObject.name = "针次标签";
                Outline outline = label.gameObject.AddComponent<Outline>();
                outline.effectColor = Background;
                outline.effectDistance = new Vector2(1, -1);
                targetPoints[i] = point;
            }
        }

        public void Refresh()
        {
            if (session == null) return;
            bool atHome = session.Mode == SutureSessionController.SessionMode.Home;
            home.SetActive(atHome);
            work.SetActive(!atHome);
            homeManual.interactable = homeAuto.interactable = session.Initialized && !enteringHome;
            Set(homeNotice, !string.IsNullOrEmpty(session.SessionError) ? session.SessionError : session.Initialized ? "场景已就绪，请选择操作方式。" : "正在准备血管场景…");
            if (atHome) return;
            bool isManual = session.IsManual;
            manual.SetActive(isManual);
            selectNeedle.gameObject.SetActive(isManual);
            focusNeedle.gameObject.SetActive(isManual);
            Set(selectNeedle, session.Desktop.NeedleSelected ? "取消锁定针具" : "选中并锁定针具");
            selectNeedle.GetComponent<Image>().color = session.Desktop.NeedleSelected ? new Color32(40, 92, 92, 255) : Raised;
            autoPanel.SetActive(!isManual);
            Set(modeTitle, isManual ? "手动缝合   /   六针引导" : "自动六针缝合");
            int current = isManual && session.Guide != null ? session.Guide.CompletedStitches : session.Automatic.CurrentPairIndex;
            int completed = isManual && session.Guide != null ? session.Guide.CompletedStitches : session.Automatic.State == AutoSutureState.Done ? 6 : current;
            Set(progressLabel, completed >= 6 ? "六针流程结束" : "第 " + (current + 1) + " 针 / 共 6 针" + (isManual ? "" : current < 3 ? " · 定位" : " · 补充"));
            for (int i = 0; i < 6; i++) stitches[i].color = i < completed ? Teal : i == current ? new Color32(44, 100, 99, 255) : Raised;
            pause.interactable = session.Mode != SutureSessionController.SessionMode.Failed && session.Automatic.State != AutoSutureState.ErrorPaused;
            Set(pause, session.Paused ? "继续" : "暂停");
            Set(guideToggle, session.GuidanceVisible ? "隐藏引导" : "显示引导");
            Set(overviewToggle, session.OverviewVisible ? "隐藏总览" : "显示总览");
            if (isManual) RefreshManual();
            else RefreshAutomatic();
            if (!string.IsNullOrEmpty(session.SessionError))
            {
                Set(stageTitle, "需要处理");
                Set(stageHint, session.SessionError);
            }
            else if (session.Paused && session.Automatic.State != AutoSutureState.ErrorPaused)
            {
                Set(stageTitle, "已暂停 · 可观察现场");
                Set(stageHint, "针具与组织保持暂停，仍可转动相机观察。点击继续恢复本轮。");
            }
            Set(footer, isManual ? "左键拖动操针 · 右键旋转 · 滚轮缩放" : "右键旋转视角 · 中键平移 · 滚轮缩放 · 空格暂停 / 继续 · Esc 菜单");
            Set(screenshotNotice, session.Automatic.ScreenshotNotice);
        }

        private void RefreshManual()
        {
            var guide = session.Guide;
            if (guide == null) return;
            pull.interactable = session.CanPull;
            Set(fine, session.Desktop.FineControl ? "精细模式：已开启" : "开启精细模式（Shift 临时）");
            for (int i = 0; i < 4; i++) tools[i].GetComponent<Image>().color = (int)session.Desktop.Tool == i ? new Color32(40, 92, 92, 255) : Raised;
            string[] tips = { "左键拖动：视平面移动\nQ / E：调整远近", "沿针尖朝向拖动推进，反向回退", "左键拖动：调整针尖朝向", "左键左右拖动：绕针轴滚转" };
            Set(toolHint, tips[(int)session.Desktop.Tool] + (session.Desktop.FineControl ? "\n精细模式已开启" : ""));
            speed.SetValueWithoutNotify(session.Desktop.Sensitivity);
            Set(speedLabel, "操针速度  " + session.Desktop.Sensitivity.ToString("F1") + "×");
            if (!session.Desktop.NeedleSelected && !guide.IsComplete)
                Set(toolHint, "针具未选中。点击上方按钮锁定，\n或直接点击针身。");
            if (guide.IsComplete)
            {
                Set(stageTitle, "六针引导流程结束");
                Set(stageHint, "已完成六组有效穿刺及拉线推进。请观察吻合区域的实际贴合情况。");
                Set(guidanceQuality, "流程完成不替代实际接触与医学质量判断。");
                return;
            }
            Set(stageHint, "近端、远端各完成一次穿刺后，拉线一次，再继续下一针。");
            if (guide.AwaitingPull)
            {
                Set(stageTitle, guide.PullRequested ? "正在拉线" : "请拉线");
                Set(guidanceQuality, "本针已完成 2 / 2 次穿刺");
                return;
            }
            Set(stageTitle, guide.PendingPass != 0 ? "请继续退出针体" : "穿刺与拉线");
            Set(guidanceQuality, "本针已完成 " + (guide.AcceptedPasses % 2) + " / 2 次穿刺");
        }

        private void RefreshAutomatic()
        {
            AutoSutureRuntimeController automatic = session.Automatic;
            bool ready = automatic.State == AutoSutureState.PreviewPlan;
            start.interactable = ready && !session.Paused && !ModalOpen;
            adjust.interactable = ready && !session.Paused && !ModalOpen;
            Set(start, ready ? "开始六针缝合" : automatic.State == AutoSutureState.Done ? "六针流程已结束" : "自动缝合执行中");
            Set(adjust, session.AdjustingPlan ? "完成针位调整" : "调整针位");
            string heading;
            switch (automatic.State)
            {
                case AutoSutureState.Idle: heading = "等待场景准备"; break;
                case AutoSutureState.PreviewPlan: heading = session.AdjustingPlan ? "调整预览针位" : "六针针位预览"; break;
                case AutoSutureState.AlignNeedle: heading = "对准进针方向"; break;
                case AutoSutureState.Puncture: heading = automatic.CurrentPassInPair == 0 ? "穿过近端血管壁" : "穿过远端血管壁"; break;
                case AutoSutureState.AdvanceUntilHist:
                case AutoSutureState.PostHistClearance:
                case AutoSutureState.Retract:
                case AutoSutureState.WaitPassComplete: heading = "退出针体并确认离壁"; break;
                case AutoSutureState.PullLineAfterPair:
                case AutoSutureState.WaitPullLineAdvanceReady: heading = "拉线收拢当前针位"; break;
                case AutoSutureState.MoveToNextPass: heading = automatic.CurrentPassInPair == 1 ? "前往远端进针位置" : "移动到下一针"; break;
                case AutoSutureState.Done: heading = "六针自动流程结束"; break;
                case AutoSutureState.ErrorPaused: heading = "自动流程已停止"; break;
                default: heading = "移动到进针位置"; break;
            }
            Set(stageTitle, heading);
            Set(stageHint, ready ? session.AdjustingPlan ? "左键拖动目标针位。调整完成后点击上方按钮退出编辑。" : "确认针位与观察视角后，点击开始。前三针定位，后三针补充。" :
                automatic.State == AutoSutureState.ErrorPaused ? "当前阶段未满足推进条件。请观察现场，重置后重试；详细原因见日志。" :
                automatic.State == AutoSutureState.Done ? "自动执行已结束。可继续观察、截图或重置本轮。" : "自动流程执行中，手动操针已锁定。可暂停后观察当前阶段。");
            Set(guidanceQuality, automatic.State == AutoSutureState.ErrorPaused ? automatic.ErrorReason : "每针依次完成两侧穿刺与拉线。\n流程结束不替代实际贴合验收。");
        }

        private void LateUpdate() => UpdateTargetOverlays();

        public void UpdateTargetOverlays()
        {
            Camera camera = session.Automatic.mainCamera;
            bool selected = session.IsManual && session.Desktop.NeedleSelected &&
                session.Desktop.TryGetNeedleScreenSegment(out _, out _);
            toolGuidance.Update(session.Desktop.Tool, session.NeedleRig, camera, selected && !ModalOpen);
            needleSelection.gameObject.SetActive(selected);
            if (selected)
            {
                session.Desktop.TryGetNeedleScreenSegment(out Vector3 a, out Vector3 b);
                Vector2 center = (a + b) * 0.5f;
                if (camera.pixelRect.Contains(center))
                    RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, center, null, out center);
                else needleSelection.gameObject.SetActive(false);
                needleSelection.anchoredPosition = center;
            }
            bool visible = session.IsManual && session.GuidanceVisible && session.Guide != null && !session.Guide.IsComplete;
            targets.SetActive(visible);
            surfaceGuidance.HideAll();
            if (!visible || camera == null) return;
            surfaceGuidance.Read(session.Automatic.driver.outerMeshFilter, session.Automatic.driver.innerMeshFilter);
            // Every stitch shows both punctures, on both material layers, with
            // the same appearance. Number labels belong to a stitch, not to a
            // prescribed entry/exit direction or a screen-space arrow.
            for (int pair = 0; pair < targetPoints.Length; pair++)
            {
                targetPoints[pair].gameObject.SetActive(false);
                bool labeled = false;
                for (int side = 0; side < 2; side++)
                {
                    if (!session.TryGetTarget(pair, side, out AutoSuturePass point)) continue;
                    bool entryOuter = point.direction == ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner;
                    int index = (pair * 2 + side) * 2;
                    bool entry = surfaceGuidance.Draw(index, entryOuter, point.entryPointWorld, 0.006f, Teal, out Vector3 entryWorld, out Vector3 entryNormal);
                    bool exit = surfaceGuidance.Draw(index + 1, !entryOuter, point.exitPointWorld, 0.006f, Teal, out Vector3 exitWorld, out Vector3 exitNormal);
                    // One number per stitch, next to an actual visible outer
                    // surface mark. Hidden inner marks remain depth occluded.
                    bool outerValid = entryOuter ? entry : exit;
                    Vector3 outerWorld = entryOuter ? entryWorld : exitWorld;
                    Vector3 outerNormal = entryOuter ? entryNormal : exitNormal;
                    if (!labeled && outerValid && Vector3.Dot(outerNormal, camera.transform.position - outerWorld) > 0f)
                        labeled = ProjectTarget(targetPoints[pair], outerWorld, camera);
                }
            }
        }

        private bool ProjectTarget(RectTransform point, Vector3 world, Camera camera)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            bool visible = screen.z > 0f && camera.pixelRect.Contains(screen);
            point.gameObject.SetActive(visible);
            if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, screen, null, out Vector2 local))
                point.anchoredPosition = local;
            return visible;
        }

        private void BuildDialog(RectTransform root)
        {
            modal = Stretch(root, "确认与帮助", new Color(0, 0, 0, 0.7f), true).gameObject;
            RectTransform panel = Box((RectTransform)modal.transform, "对话框", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380, -260), new Vector2(380, 260), Surface, true);
            dialogTitle = Label(panel, "", 38, 30, 684, 60, 31, Ink);
            dialogCopy = Label(panel, "", 38, 110, 684, 270, 23, Muted);
            dialogCancel = Button(panel, "取消", 38, 422, 320, 57, CancelDialog, false);
            dialogConfirm = Button(panel, "确定", 390, 422, 332, 57, ConfirmDialog, true);
            modal.SetActive(false);
        }

        public void Confirm(string title, string copy, Action action)
        {
            OpenDialog(title, copy, action, "确定", true);
        }

        private void OpenDialog(string title, string copy, Action action, string confirmLabel, bool cancel)
        {
            if (ModalOpen) return;
            pausedBeforeDialog = session.Paused;
            if (session.Mode != SutureSessionController.SessionMode.Home) session.SetPaused(true);
            confirmed = action;
            Set(dialogTitle, title);
            Set(dialogCopy, copy);
            Set(dialogConfirm, confirmLabel);
            dialogCancel.gameObject.SetActive(cancel);
            modal.SetActive(true);
            session.Desktop.InputAllowed = false;
            session.Desktop.CancelDrag();
        }

        private void CancelDialog()
        {
            modal.SetActive(false);
            confirmed = null;
            if (!pausedBeforeDialog && session.Mode != SutureSessionController.SessionMode.Home) session.SetPaused(false);
        }

        public void DismissModal() => CancelDialog();

        private void ConfirmDialog()
        {
            Action action = confirmed;
            CancelDialog();
            action?.Invoke();
        }

        public void OpenPauseMenu()
        {
            OpenDialog("本轮操作已暂停", "针具与血管保持暂停，关闭菜单后可继续操作。\n\n返回主页或重置本轮会清除当前操作进度。", null, "关闭菜单", false);
        }

        private void ShowHelp()
        {
            OpenDialog("键鼠操作说明", "1–4：移动 / 推进 / 转向 / 滚转\n默认锁定针具；锁定按钮再次点击取消选中\n左键拖动操针；Q / E 调整远近\n推进：沿针尖的屏幕方向拖动，反向回退\n速度：0.2×–3.0×；Shift：临时精细操作\n右键旋转；中键平移；滚轮缩放\nF / G / R：聚焦目标 / 整体 / 跟随\n空格暂停；Esc 菜单；孔位编号对应针次\n近端、远端各穿刺一次后，拉线再继续下一针。", null, "知道了", false);
        }

        private void Capture()
        {
            session.Automatic.CapturePatentScreenshot();
            Set(screenshotNotice, session.Automatic.ScreenshotNotice);
        }

        private RectTransform Stretch(RectTransform parent, string name, Color color, bool raycast = true) =>
            Box(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, color, raycast);

        private RectTransform Box(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, Color color, bool raycast)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
            return rect;
        }

        private RectTransform At(RectTransform parent, string name, float x, float y, float width, float height, Color color, bool raycast)
        {
            RectTransform rect = Box(parent, name, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero, color, raycast);
            rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private Text Label(RectTransform parent, string text, float x, float y, float width, float height, int size, Color color)
        {
            GameObject go = new GameObject("文字", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            Text label = go.GetComponent<Text>();
            label.font = font; label.fontSize = size; label.color = color; label.text = text;
            label.raycastTarget = false; label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private Button Button(RectTransform parent, string text, float x, float y, float width, float height, UnityEngine.Events.UnityAction action, bool primary)
        {
            RectTransform rect = At(parent, text, x, y, width, height, primary ? Teal : Raised, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.55f, 0.8f, 0.78f);
            colors.disabledColor = new Color(0.45f, 0.5f, 0.52f, 0.65f);
            button.colors = colors;
            Navigation navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            button.onClick.AddListener(action);
            Text label = Label(rect, text, 12, 0, width - 24, height, 21, primary ? Background : Ink);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private static void Set(Text label, string text) { if (label != null && label.text != text) label.text = text; }
        private static void Set(Button button, string text) => Set(button.GetComponentInChildren<Text>(), text);

        private static Sprite CreateSprite(bool circle, out Texture2D texture)
        {
            texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            texture.name = circle ? "SutureTargetRing" : "SuturePanelCorners";
            Color[] pixels = new Color[4096];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float distance = circle ? Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) :
                    Vector2.Distance(new Vector2(Mathf.Max(0, Mathf.Abs(x - 31.5f) - 21.5f), Mathf.Max(0, Mathf.Abs(y - 31.5f) - 21.5f)), Vector2.zero);
                float alpha = circle ? Mathf.Clamp01(31.5f - distance) * Mathf.Clamp01(distance - 25f) : Mathf.Clamp01(10f - distance);
                pixels[y * 64 + x] = new Color(1, 1, 1, alpha);
            }
            texture.SetPixels(pixels); texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, circle ? Vector4.zero : new Vector4(12, 12, 12, 12));
        }

        private void OnDestroy()
        {
            surfaceGuidance?.Dispose();
            toolGuidance?.Dispose();
            if (font != null) Destroy(font);
            if (rounded != null) Destroy(rounded);
            if (ring != null) Destroy(ring);
            if (roundedTexture != null) Destroy(roundedTexture);
            if (ringTexture != null) Destroy(ringTexture);
        }
    }
}
