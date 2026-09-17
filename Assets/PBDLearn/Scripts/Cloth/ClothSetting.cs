using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBDLearn
{

    [System.Serializable]
    public class ClothSetting
    {

        /// <summary>
        /// 布料密度，单位kg/m^2
        /// </summary>
        [Range(0.01f, 1)]
        [SerializeField]
        private float _density = 1;

        /// <summary>
        /// 约束求解迭代次数，必须为2的倍数
        /// </summary>
        [Range(1, 20)]
        [SerializeField]
        private int _constraintSolverIteratorCount = 2;

        /// <summary>
        /// 压缩约束系数
        /// </summary>
        [Range(0.01f, 1f)]
        [SerializeField]
        private float _compressStiffness = 0.8f;

        /// <summary>
        /// 拉伸约束系数
        /// </summary>
        [Range(0.01f, 1f)]
        [SerializeField]
        private float _stretchStiffness = 0.8f;

        /// <summary>
        /// 弯曲约束系数
        /// </summary>
        [Range(0.01f, 1)]
        [SerializeField]
        private float _bendStiffness = 0.06f;

        /// <summary>
        /// 阻尼系数
        /// </summary>
        [Range(0.01f, 10)]
        [SerializeField]

        private float _damper = 1;

        [Header("Solver Sleep")]
        [Tooltip("最终位置更新的静止位移阈值（世界单位）。低于该值且速度也很小时，直接视为静止。")]
        [Min(0f)]
        [SerializeField]
        private float _sleepPositionThreshold = 2e-5f;

        [Tooltip("最终速度更新的静止速度阈值。低于该值且位移也很小时，直接归零。")]
        [Min(0f)]
        [SerializeField]
        private float _sleepVelocityThreshold = 1e-3f;

        [Header("Collision (One-sided Projection)")]
        [Tooltip("碰撞壳层厚度（skin）。用于减少贴着表面来回触发导致的跳动。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _collisionSkin = 0.0012f;

        [Tooltip("碰撞进入阈值（enter）：d < enter 才进入碰撞状态。d=dot(p-surfacePoint, normal)")]
        [Range(-0.02f, 0.02f)]
        [SerializeField]
        private float _collisionEnter = 0.0015f;

        [Tooltip("碰撞退出阈值（exit）：只有 d > exit 才退出碰撞状态（滞回防抖）。建议 exit > enter。")]
        [Range(0.0f, 0.05f)]
        [SerializeField]
        private float _collisionExit = 0.0025f;


        [Tooltip("碰撞位置投影刚度。1=满额硬推；越小越软。")]
        [Range(0.01f, 1f)]
        [SerializeField]
        private float _collisionStiffness = 0.65f;

        [Tooltip("接触后的法向速度阻尼。0=不阻尼；1=完全压掉离面反弹。")]
        [Range(0.0f, 1f)]
        [SerializeField]
        private float _collisionNormalDamping = 0.7f;

        [Tooltip("每轮碰撞求解允许的最大推开量。用于避免深穿透时单轮一下子顶太多，手感更软。0 表示不限制。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _collisionMaxPushPerIteration = 0.0015f;


        [Header("Needle Tip Indentation (Phase 1)")]
        [Tooltip("是否启用针尖影响区的凹陷版。开启后，不再完全依赖\"某个质点正好撞到针尖\"，而是让针尖附近一小片区域都能受到压入影响。")]
        [SerializeField]
        private bool _enableNeedleTipIndentation = true;

        [Tooltip("针尖影响区半径（相对针尖表面向外扩的距离）。越大，越不依赖恰好命中单个质点。")]
        [Range(0.0f, 0.1f)]
        [SerializeField]
        private float _needleTipInfluenceRadius = 0.008f;

        [Tooltip("针尖影响区的软推刚度。越大，凹陷更明显；越小，更柔。")]
        [Range(0.0f, 1f)]
        [SerializeField]
        private float _needleTipIndentationStiffness = 0.45f;

        [Tooltip("针尖影响区每轮允许的最大推开量。用于把\"先凹陷\"做得更肉一些，避免一下子推太多。0 表示不限制。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _needleTipMaxPushPerIteration = 0.0008f;

        [Tooltip("针尖影响区的距离衰减指数。越大，越集中在针尖附近；越小，影响范围更平缓。")]
        [Range(0.5f, 4f)]
        [SerializeField]
        private float _needleTipFalloffPower = 2.0f;



        [Header("Triangle Surface Contact (Needle Tip vs Triangle)")]
        [Tooltip("启用针尖球帽 vs 三角面的连续接触。开启后，即使没正好命中某个质点，只要针尖接近三角面，也会对该面片产生压入反应。")]
        [SerializeField]
        private bool _enableTriangleSurfaceContact = true;

        [Tooltip("三角面连续接触的推开刚度。越大越硬，越小越软。")]
        [Range(0.0f, 1f)]
        [SerializeField]
        private float _triangleSurfaceContactStiffness = 0.75f;

        [Tooltip("三角面连续接触的额外接触边界。>0 时会比严格的针尖半径更早开始产生压入，改善擦边漏反应。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _triangleSurfaceContactMargin = 0.0025f;

        [Tooltip("每轮三角面连续接触允许的最大推开量。0 表示不限制。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _triangleSurfaceContactMaxPushPerIteration = 0.0012f;



        [Header("Needle Puncture (Phase 2 - Portal / Virtual Channel)")]
        [Tooltip("启用针刺阶段的“虚拟穿刺通道”。开启后，针尖在满足阈值时会在局部建立 portal，允许从“只能压凹”过渡到“可刺入”。")]
        [SerializeField]
        private bool _enableNeedlePuncture = true;

        [Tooltip("达到穿刺所需的最小压入深度（相对 tip probe + 接触 margin 的有效半径）。越小越容易刺破。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _needlePunctureDepthThreshold = 0.0008f;

        [Tooltip("穿刺时要求针尖与面法线大致对向的最小阈值。越大越接近“正对着扎”，越小越允许斜扎。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needlePunctureNormalBias = 0.35f;

        [Tooltip("portal 的局部放松半径。portal 半径内会减弱普通碰撞/分层约束，给针尖一个进入通道。")]
        [Range(0.0f, 0.03f)]
        [SerializeField]
        private float _needlePunctureRelaxRadius = 0.006f;

        [Tooltip("portal 建立后保持的小开口深度。用于避免刚刺破就又被完全弹回。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _needlePunctureCaptureDepth = 0.0016f;

        [Tooltip("portal 跟随针尖的推开刚度。越大，刺入后局部开口跟针更紧。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needlePuncturePortalStiffness = 0.45f;

        [Tooltip("portal 每轮允许的最大推开量。0 表示不限制。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _needlePuncturePortalMaxPushPerIteration = 0.0012f;

        [Tooltip("portal 半径内保留多少比例的普通碰撞推开。0=完全放开；1=不放松。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needlePunctureCollisionRelaxScale = 0.18f;

        [Tooltip("portal 半径内 layer separation 仍保留多少比例的最小分层厚度。0=完全局部放开；1=不放松。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needlePunctureSeparationScale = 0.2f;

        [Tooltip("portal 关闭后的冷却时间。避免针尖在阈值附近一进一出时疯狂重复创建。")]
        [Range(0.0f, 0.5f)]
        [SerializeField]
        private float _needlePunctureCooldown = 0.08f;

        [Tooltip("双层血管时，允许继续识别内壁穿刺。关闭后只做外壁 portal。")]
        [SerializeField]
        private bool _enableNeedleInnerWallPuncture = true;

        [Tooltip("从外壁 portal 往里推进到多深后，才开始尝试建立内壁 portal。用于避免外/内壁同帧误触发。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _needleInnerPunctureAdvanceThreshold = 0.0012f;

        [Tooltip("portal 横向偏离超过 relaxRadius * 该倍率时关闭。越大越不容易因为侧向微抖而关掉。")]
        [Range(1.0f, 4.0f)]
        [SerializeField]
        private float _needlePortalLateralCloseScale = 1.8f;

        [Tooltip("针回撤超过该距离时关闭 portal。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _needlePortalRetractCloseDistance = 0.0012f;

        [Header("Needle Tip Piercer")]
        [Tooltip("启用方向性针尖穿刺器。命中后先让针尖 patch 沿针轴/局部法向压入，不直接算正式穿刺。字段名保留 SurfaceSticky 以兼容旧序列化数据。")]
        [SerializeField]
        private bool _enableNeedleSurfaceSticky = true;

        [Tooltip("针尖 tip/probe/前段扫线到三角面的最大穿刺器接触半径。该判断不使用 normalBias。")]
        [Range(0.0f, 0.03f)]
        [SerializeField]
        private float _needleSurfaceStickyRadius = 0.0075f;

        [Tooltip("Tip Piercer 接触后沿针轴继续推进多少，才允许进入 provisional PreChannel。")]
        [Range(0.0f, 0.03f)]
        [SerializeField]
        private float _needleSurfaceStickyAdvanceThreshold = 0.0012f;

        [Tooltip("Tip Piercer 的局部 one-ring/two-ring patch 影响半径。")]
        [Range(0.0f, 0.04f)]
        [SerializeField]
        private float _needleSurfaceStickyPatchRadius = 0.009f;

        [Tooltip("Tip Piercer 法向压入强度。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needleSurfaceStickyNormalStiffness = 0.9f;

        [Tooltip("Tip Piercer 沿针轴推进强度。旧字段名为 Tangential，但这里不再表示胶水式切向粘滞。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needleSurfaceStickyTangentialStiffness = 0.9f;

        [Tooltip("Tip Piercer 每轮最大压入距离，避免斜角接触时直接拉爆 mesh。0 表示使用 radius 的安全比例。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _needleSurfaceStickyMaxPullPerIteration = 0.006f;

        [Tooltip("针尖穿刺器附着点离固定材料点超过该距离时断开。0 表示使用半径的 2 倍。")]
        [Range(0.0f, 0.05f)]
        [SerializeField]
        private float _needleSurfaceStickyBreakDistance = 0.025f;

        [Tooltip("Tip Piercer 期间局部 patch 的普通圆钝针-布碰撞放松倍率。0=完全不被普通碰撞推开；1=不放松。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needleSurfaceStickyCollisionRelaxScale = 0.05f;

        [Tooltip("虚拟针尖锥体长度。0=按 capsule 半径自动推导。该锥体只用于穿刺 narrow phase，不要求可见模型也是锥体。")]
        [Range(0.0f, 0.08f)]
        [SerializeField]
        private float _needleTipPiercerConeLength = 0f;

        [Tooltip("虚拟针尖锥体 base 半径相对 capsule 半径的倍率。越小越尖，斜角进针越不容易表现成钝头推开。")]
        [Range(0.05f, 2.0f)]
        [SerializeField]
        private float _needleTipPiercerConeBaseRadiusScale = 0.45f;

        [Tooltip("虚拟针尖锥面接触容差。用于捕捉刚接触锥面但尚未进入的布面三角。")]
        [Range(0.0f, 0.01f)]
        [SerializeField]
        private float _needleTipPiercerConeSurfaceMargin = 0.0012f;

        [Tooltip("Tip Piercer 接触成形时是否把局部阻力反馈给针。非 Kinematic Rigidbody 使用速度修正；Scene Rig/Kinematic 针使用沿针轴的反向小位移。")]
        [SerializeField]
        private bool _enableNeedleTipPiercerFeedback = true;

        [Tooltip("Tip Piercer 阻力换算到 Rigidbody VelocityChange 的倍率。")]
        [Range(0.0f, 4.0f)]
        [SerializeField]
        private float _needleTipPiercerFeedbackVelocityScale = 0.65f;

        [Tooltip("Tip Piercer 每帧允许给 Rigidbody 的最大 VelocityChange。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needleTipPiercerFeedbackMaxVelocityChange = 0.08f;

        [Tooltip("Tip Piercer 阻力换算到 Scene Rig 反向位移的倍率。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _needleTipPiercerFeedbackRigDistanceScale = 0.35f;

        [Tooltip("Tip Piercer 每帧允许让 Scene Rig 沿针轴反向退让的最大距离。")]
        [Range(0.0f, 0.01f)]
        [SerializeField]
        private float _needleTipPiercerFeedbackMaxRigRetreatPerFrame = 0.0015f;

        [Header("Suture Line Constraints")]
        [Tooltip("拉线后是否启用两侧 WholeWall 孔位 patch 之间的缝线段约束。")]
        [SerializeField]
        private bool _enableSutureLineConstraints = true;

        [Tooltip("拉线闭合约束强度。0=不拉；1=每轮尽量拉到当前目标距离。该约束只抗拉，不抗压。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLineWeakStiffness = 0.55f;

        [Tooltip("兼容旧弱拉线字段。创建线段时希望从当前孔距中收短的距离；Phase21 优先使用 Closure Target Distance。")]
        [Range(0.0f, 0.05f)]
        [SerializeField]
        private float _sutureLineWeakPullDistance = 0.003f;

        [Tooltip("拉线允许的最小目标距离，避免两侧孔位被拉到重合。")]
        [Range(0.0f, 0.05f)]
        [SerializeField]
        private float _sutureLineMinRestDistance = 0.006f;

        [Tooltip("拉线约束每轮最大修正量，防止瞬间强拉导致抖动。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _sutureLineMaxPullPerIteration = 0.0015f;

        [Tooltip("手动拉线后，当前 rest distance 每秒向闭合目标距离收紧多少。不会标 Done。")]
        [Range(0.0f, 0.2f)]
        [SerializeField]
        private float _sutureLineTightenSpeed = 0.035f;

        [Tooltip("Approach 阶段 rest 收紧速度倍率。接触前不再被 bite depth 截停，可更快把两侧 ROI 拉近。")]
        [Range(0.0f, 6.0f)]
        [SerializeField]
        private float _sutureLineApproachTightenSpeedScale = 2.0f;

        [Tooltip("ContactLatched 后 rest 收紧速度倍率。接触后仍继续拉线，但主要依靠局部 triangle contact 防穿并形成褶皱。")]
        [Range(0.0f, 3.0f)]
        [SerializeField]
        private float _sutureLineLatchedTightenSpeedScale = 0.75f;

        [Tooltip("Phase21 闭合目标距离。实际 targetRest 会取 max(该值, contactGap, minRestDistance)，且不大于创建时两端实际距离。")]
        [Range(0.0f, 0.05f)]
        [SerializeField]
        private float _sutureLineClosureTargetDistance = 0.006f;

        [Tooltip("拉线目标距离是否按孔到断端距离、壁厚、网格边长、当前开口做几何自适应。开启后远孔位不会被拉到固定小距离。")]
        [SerializeField]
        private bool _enableSutureLineAdaptiveClosureTarget = true;

        [Tooltip("兼容旧字段名：bite depth 只用于日志，不再加回 targetRest；Approach 是否继续拉近由 contact latch 和收紧速度控制。")]
        [Range(0.0f, 1.5f)]
        [SerializeField]
        private float _sutureLineBiteDepthTargetScale = 1.0f;

        [Tooltip("局部壁厚用于闭合材料间隙下限的倍率。该值只提供贴合下限，不应提前截停 Approach 拉近。")]
        [Range(0.0f, 3.0f)]
        [SerializeField]
        private float _sutureLineAdaptiveThicknessScale = 0.55f;

        [Tooltip("局部平均边长用于闭合材料间隙下限的倍率。保持较小，避免粗网格把 targetRest 托得过大。")]
        [Range(0.0f, 3.0f)]
        [SerializeField]
        private float _sutureLineAdaptiveEdgeScale = 0.12f;

        [Tooltip("拉线约束是否升级为孔位 Patch 约束。开启后不只拉一个三角面重心，而是拉孔周围一小圈顶点。")]
        [SerializeField]
        private bool _enableSutureLinePatchPull = true;

        [Tooltip("拉线 Patch 的半径。小于等于 0 时只使用穿刺三角面及其一环邻域，不做距离过滤。建议略大于孔周围网格边长。")]
        [Range(0.0f, 0.08f)]
        [SerializeField]
        private float _sutureLinePatchRadius = 0.024f;

        [Tooltip("拉线 Patch 一侧最多参与的顶点数量，避免孔周一环过大导致拉动范围过宽。")]
        [Range(3, 64)]
        [SerializeField]
        private int _sutureLinePatchMaxVertexCount = 32;

        [Tooltip("拉线 Patch 中一环邻居顶点的基础权重。孔所在三角面的三个顶点仍然权重最高。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLinePatchRingWeight = 0.35f;

        [Tooltip("是否把孔位到最近断端边界之间的局部组织带加入 PullLine 受力 patch。")]
        [SerializeField]
        private bool _enableSutureClosureBand = true;

        [Tooltip("BiteBand 收集半径相对 max(avgEdge, thickness) 的倍率。")]
        [Range(0.2f, 5.0f)]
        [SerializeField]
        private float _sutureClosureBandRadiusScale = 1.4f;

        [Tooltip("每个 PullLine 端点最多额外加入多少个 BiteBand 顶点。")]
        [Range(0, 128)]
        [SerializeField]
        private int _sutureClosureBandMaxExtraVertexCount = 48;

        [Tooltip("BiteBand 顶点基础权重。孔周顶点仍然权重最高，组织带负责带动中间组织闭合/褶皱。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureClosureBandWeight = 0.45f;

        [Tooltip("WholeWall 拉线时 Outer subpatch 权重倍率。Outer/Inner 会作为同一个整壁 patch 求解。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLineOuterPatchWeight = 0.55f;

        [Tooltip("WholeWall 拉线时 Inner subpatch 权重倍率。Outer/Inner 会作为同一个整壁 patch 求解。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLineInnerPatchWeight = 0.45f;

        [Tooltip("WholeWall subpatch 搜索环数。搜索仍然 respect layer slice，不跨 Outer/Inner。")]
        [Range(1, 2)]
        [SerializeField]
        private int _sutureLinePatchRingDepth = 2;

        [Tooltip("拉线约束是否使用 XPBD 求解。推荐开启：距离/收紧类约束通常比弯曲约束更适合 XPBD。")]
        [SerializeField]
        private bool _enableSutureLineXPBD = true;

        [Tooltip("拉线 XPBD compliance。值越小越硬，值越大越软。Phase21 默认比旧弱拉线更偏闭合。")]
        [Range(0.0f, 0.001f)]
        [SerializeField]
        private float _sutureLineXPBDCompliance = 0.00003f;

        [Tooltip("每帧重置一次拉线 XPBD lambda。拉线目标距离会逐渐变化，默认重置更稳。")]
        [SerializeField]
        private bool _resetSutureLineLambdaEachFrame = true;

        [Tooltip("currentRestDistance 单帧变化超过该阈值时重置 XPBD lambda；较小变化时按 Lambda Decay 衰减。")]
        [Range(0.0f, 0.01f)]
        [SerializeField]
        private float _sutureLineLambdaResetRestDelta = 0.00025f;

        [Tooltip("未重置时 XPBD lambda 的保留比例，用于目标距离连续收紧时避免旧 lambda 过度干扰。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLineLambdaDecay = 0.8f;

        [Tooltip("是否启用已拉线两端 WholeWall patch 的局部软接触，只防互穿，不做全局 self collision。")]
        [SerializeField]
        private bool _enableSutureLineContact = true;

        [Tooltip("已拉线两端 WholeWall patch 的最小软接触间隙。低于该值时做有限防穿修正。")]
        [Range(0.0f, 0.03f)]
        [SerializeField]
        private float _sutureLineContactGap = 0.006f;

        [Tooltip("已拉线两端 WholeWall patch 的局部软接触强度。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLineContactStiffness = 0.65f;

        [Tooltip("已拉线两端 WholeWall patch 接触每轮最大防穿修正量。")]
        [Range(0.0f, 0.01f)]
        [SerializeField]
        private float _sutureLineContactMaxPushPerIteration = 0.001f;

        [Tooltip("是否启用 PullLine Approach -> ContactLatched 两阶段闭合。闩锁后增强局部接触并继续按 Latched 速度收紧 rest。")]
        [SerializeField]
        private bool _enableSutureLineContactLatch = true;

        [Tooltip("ContactLatched 瞬间 rest 可略小于当前实际距离的余量，用于保持轻微拉力；后续仍会继续向闭合目标收紧。")]
        [Range(0.0f, 0.005f)]
        [SerializeField]
        private float _sutureLineContactLatchRestSlack = 0.00035f;

        [Tooltip("ContactLatched 后局部 triangle/contact 的强度倍率。")]
        [Range(1.0f, 4.0f)]
        [SerializeField]
        private float _sutureLineContactLatchedStiffnessScale = 1.75f;

        [Tooltip("是否把 PullLine 两端 WholeWall patch 做局部点对接触。只检查已缝合 ROI，不做全局 self collision。")]
        [SerializeField]
        private bool _enableSutureLineLocalPatchContact = true;

        [Tooltip("每条 PullLine 每轮最多检查的局部 patch 点对数量，用于限制断端局部多体接触开销。")]
        [Range(16, 1024)]
        [SerializeField]
        private int _sutureLineLocalContactMaxPairChecksPerLine = 256;

        [Tooltip("是否启用 PullLine 闭合 ROI 的 vertex-triangle 局部接触。只覆盖当前缝线的 BiteBand/孔周 ROI。")]
        [SerializeField]
        private bool _enableSutureLineLocalTriangleContact = true;

        [Tooltip("每个 PullLine 端点最多收集多少个 ROI 三角面用于局部接触。")]
        [Range(8, 256)]
        [SerializeField]
        private int _sutureLineLocalContactMaxTrianglesPerEndpoint = 96;

        [Tooltip("每条 PullLine 每轮最多检查的 vertex-triangle 接触对数量。")]
        [Range(16, 2048)]
        [SerializeField]
        private int _sutureLineLocalContactMaxTriangleChecksPerLine = 384;

        [Header("Suture Local Self Contact")]
        [Tooltip("是否启用 PullLineLocalWallContact。开启后每条 PullLine 只使用这一套局部壁-壁接触 ROI，旧代表点/点对/patch-triangle 接触只作为关闭时的 fallback。")]
        [SerializeField]
        private bool _enableSutureLineSelfContact = true;

        [Tooltip("局部壁厚进入 self-contact 厚度阈值的倍率。")]
        [Range(0.0f, 2.0f)]
        [SerializeField]
        private float _sutureLineSelfContactThicknessScale = 0.35f;

        [Tooltip("局部平均边长进入 self-contact 厚度阈值的倍率。保持较小，避免粗网格过早 latch。")]
        [Range(0.0f, 2.0f)]
        [SerializeField]
        private float _sutureLineSelfContactEdgeScale = 0.08f;

        [Tooltip("self-contact 接触 pair 触发 ContactLatched 前，actualDistance 必须接近 targetRest 的额外余量。")]
        [Range(0.0f, 0.05f)]
        [SerializeField]
        private float _sutureLineSelfContactLatchMargin = 0.004f;

        [Tooltip("断端局部 self-contact 法向 XPBD/PBD 修正强度。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLineSelfContactNormalStiffness = 0.85f;

        [Tooltip("断端局部 self-contact 切向阻尼/粘滞强度。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLineSelfContactTangentialDamping = 0.35f;

        [Tooltip("切向修正相对法向推量的上限倍率，用于避免两侧断端轻易滑过去。")]
        [Range(0.0f, 2.0f)]
        [SerializeField]
        private float _sutureLineSelfContactFriction = 0.6f;

        [Tooltip("每轮 self-contact 最大法向/切向修正，防止局部抖动。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _sutureLineSelfContactMaxPushPerIteration = 0.0025f;

        [Tooltip("VT/EE pair 命中后保持 ContactLatched 的帧数。")]
        [Range(0, 12)]
        [SerializeField]
        private int _sutureLineSelfContactPersistentFrames = 4;

        [Tooltip("Persistent contact 释放时额外允许的间隙。")]
        [Range(0.0f, 0.02f)]
        [SerializeField]
        private float _sutureLineSelfContactReleaseMargin = 0.002f;

        [Tooltip("每条 PullLine 每轮最多求解的 vertex-triangle local wall contact pair 数。")]
        [Range(8, 4096)]
        [SerializeField]
        private int _sutureLineSelfContactMaxVTPairs = 256;

        [Tooltip("是否启用 tri-tri manifold 内的少量 edge-edge self-contact。只在相交/近相交三角 pair 内生成，不再做整片 ROI 边全交叉。")]
        [SerializeField]
        private bool _enableSutureLineSelfContactEE = true;

        [Tooltip("每条 PullLine 每轮最多求解的 limited edge-edge self-contact pair 数。")]
        [Range(0, 512)]
        [SerializeField]
        private int _sutureLineSelfContactMaxEEPairs = 24;

        [Header("Suture Compression Fold")]
        [Tooltip("ContactLatched 后是否给 PullLine 两端 WholeWall ROI 一个小的侧向屈曲预测。它移动整壁 patch center，不单独压缩 outer/inner。")]
        [SerializeField]
        private bool _enableSutureLineCompressionFold = true;

        [Tooltip("屈曲预测强度。值越大，接触后越容易向侧方折出；厚度长度仍由 thickness links 维持。")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float _sutureLineCompressionFoldStiffness = 0.22f;

        [Tooltip("每轮屈曲预测最大侧向修正。")]
        [Range(0.0f, 0.01f)]
        [SerializeField]
        private float _sutureLineCompressionFoldMaxPushPerIteration = 0.00065f;

        [Tooltip("targetDelta 小于该值时不施加屈曲预测，避免已经贴合后持续抖动。")]
        [Range(0.0f, 0.01f)]
        [SerializeField]
        private float _sutureLineCompressionFoldMinTargetDelta = 0.0002f;

        [Header("Incision Local Constraints")]
        [Tooltip("是否启用切口局部约束与局部厚度连接放松。关闭后，切口只保留拓扑裂开，不再持续做唇边回弹。")]
        [SerializeField]
        private bool _enableIncisionLocalConstraints = true;

        [Tooltip("是否启用切口唇边的运行时局部张口/回弹。")]
        [SerializeField]
        private bool _enableIncisionLipRelaxation = true;

        [Tooltip("沿切口长度做非均匀开口变化。0=几乎均匀；1=变化更明显。")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _incisionProfileVariation = 0.5f;

        [Tooltip("曲率较大的切口位置，额外放大的开口倍率。")]
        [Range(0f, 3f)]
        [SerializeField]
        private float _incisionCurvatureWidthScale = 1.25f;

        [Tooltip("切口唇边回弹强度。")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _incisionLipRelaxation = 0.65f;

        [Tooltip("切口唇边回弹速度。")]
        [Range(0f, 60f)]
        [SerializeField]
        private float _incisionLipRelaxationSpeed = 12f;

        [Tooltip("每帧允许的最大局部张口推进量。0 表示不限制。")]
        [Min(0f)]
        [SerializeField]
        private float _incisionLipMaxPushPerFrame = 0.0008f;

        [Tooltip("切断后是否把可见开口解释为自然小幅回缩，而不是完整人工张开宽度。")]
        [SerializeField]
        private bool _enableIncisionNaturalRetraction = true;

        [Tooltip("自然回缩的最大可见开口距离。第一版用于演示残余弹性释放，不等同于真实表面张力参数。")]
        [Range(0f, 0.05f)]
        [SerializeField]
        private float _incisionNaturalRetractionDistance = 0.01f;

        [Tooltip("自然回缩目标相对于原始切口 targetOpen 的比例。最终目标取 min(distance, targetOpen * scale)。")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _incisionNaturalRetractionScale = 0.45f;

        [Tooltip("自然回缩目标建立速度。值越大，切断后越快进入小幅开口状态。")]
        [Range(0f, 60f)]
        [SerializeField]
        private float _incisionNaturalRetractionSpeed = 10f;

        [Tooltip("自然回缩每帧允许的最大 lip pair 位移，避免切断后一帧内瞬移。")]
        [Min(0f)]
        [SerializeField]
        private float _incisionNaturalRetractionMaxPushPerFrame = 0.0012f;

        [Header("Compliant Clamp Pins")]
        [Tooltip("端部固定是否改为夹具整体让位。开启后端部仍被夹持，但 Start/End clamp 可作为整体产生有限位移。")]
        [SerializeField]
        private bool _enableCompliantClampPins = true;

        [Tooltip("夹具 Pin 回拉强度。0=不回拉；1=每轮尽量回到当前 clamp target。")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _clampPinStiffness = 0.45f;

        [Tooltip("夹具整体允许让位的最大距离。超过后 SoftClamp target 不再继续跟随。")]
        [Range(0f, 0.05f)]
        [SerializeField]
        private float _clampPinMaxLetDistance = 0.012f;

        [Tooltip("夹具 Pin 每轮最大回拉修正量，避免夹具端部突然弹回。")]
        [Range(0f, 0.02f)]
        [SerializeField]
        private float _clampPinMaxCorrectionPerIteration = 0.0008f;

        [Tooltip("夹具整体 offset 跟随当前夹持区域质心的阻尼速度。越大越容易让位，越小越像固定支架。")]
        [Range(0f, 60f)]
        [SerializeField]
        private float _clampPinFollowDamping = 12f;

        [Tooltip("切口附近厚度连接/分层放松的作用半径。0 表示不做局部放松。")]
        [Min(0f)]
        [SerializeField]
        private float _incisionThicknessRelaxRadius = 0.004f;

        [Tooltip("切口附近保留多少比例的厚度连接。0=完全去掉这些局部 link；1=完全保留。")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _incisionThicknessRelaxScale = 0.35f;

        [Tooltip("切口附近 layer separation 仍保留多少比例。0=完全局部放开；1=不放松。")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _incisionSeparationRelaxScale = 0.25f;

        [Header("XPBD In-plane Distance")]
        [Tooltip("启用面内距离约束的 XPBD 解法。开启后，结构/剪切边不再走旧 PBD 距离投影。")]
        [SerializeField]
        private bool _enableXPBDInPlaneDistance = true;

        [Tooltip("面内距离 XPBD compliance。越大越软；0 表示近似刚性。建议从 1e-8 ~ 1e-6 调。")]
        [Min(0f)]
        [SerializeField]
        private float _distanceCompliance = 1e-7f;

        [Tooltip("是否每帧清零面内距离约束的 lambda。开启更易排查；关闭可 warm-start。")]
        [SerializeField]
        private bool _resetDistanceLambdaEachFrame = true;

        [Header("XPBD In-plane Bend")]
        [Tooltip("启用面内弯曲约束的 XPBD 解法。开启后，相邻三角夹角不再走旧 PBD bending。")]
        [SerializeField]
        private bool _enableXPBDBend = true;

        [Tooltip("面内弯曲 XPBD compliance。越大越软；0 表示近似刚性。\n注意：现实现已额外乘 bendStiffness 与每轮 di 缩放；建议从 1e-6 ~ 1e-4 调。")]
        [Min(0f)]
        [SerializeField]
        private float _bendCompliance = 1e-5f;

        [Tooltip("是否每帧清零面内弯曲约束的 lambda。\n默认建议关闭，让 XPBD Bend warm-start，减少每帧重新猛推带来的跳动。")]
        [SerializeField]
        private bool _resetBendLambdaEachFrame = true;

        [Tooltip("弯曲角度死区（弧度）。误差低于该值时，不再继续做 XPBD bend 微调，防止静态抖动。")]
        [Min(0f)]
        [SerializeField]
        private float _bendAngleDeadZone = 2e-4f;

        [Tooltip("弯曲 lambda 死区。历史 lambda 足够小时直接清零，防止极小残余长期存在。")]
        [Min(0f)]
        [SerializeField]
        private float _bendLambdaDeadZone = 1e-6f;

        [Tooltip("单轮弯曲位置修正死区（世界单位）。修正量小于该值时直接忽略，避免静态来回抖。")]
        [Min(0f)]
        [SerializeField]
        private float _bendPositionCorrectionDeadZone = 2e-5f;

        [Header("XPBD Thickness Links (Cross-layer)")]
        [Tooltip("启用 XPBD 版本的跨层厚度连接（更稳定，可通过 compliance 控制可压缩性）。\n启用后，DoubleLayerDriver 会停止用 PBD 方式添加 thickness distance 约束，避免双重约束打架。")]
        [SerializeField]
        private bool _enableXPBDThicknessLinks = true;

        [Tooltip("XPBD compliance（可压缩性）。越大越软；0 表示近似刚性。\n建议从 1e-6 ~ 1e-4 调。")]
        [Min(0f)]
        [SerializeField]
        private float _thicknessCompliance = 8e-6f;

        [Tooltip("是否每帧清零厚度约束的 lambda。\n开启更易排查；关闭可 warm-start（更稳更快）。")]
        [SerializeField]
        private bool _resetThicknessLambdaEachFrame = false;

        [Tooltip("额外的厚度约束迭代次数（在主 solver 迭代内，每轮再补几次 thickness 求解）。\n排查用：先保持 0 或 1。")]
        [Range(0, 5)]
        [SerializeField]
        private int _thicknessSolveExtraIterations = 0;


        public float density
        {
            get
            {
                return _density;
            }
        }

        public int constraintSolverIteratorCount
        {
            get
            {
                return _constraintSolverIteratorCount;
            }
        }

        public float compressStiffness
        {
            get
            {
                return this._compressStiffness;
            }
        }

        public float stretchStiffness
        {
            get
            {
                return _stretchStiffness;
            }
        }

        public float bendStiffness
        {
            get
            {
                return _bendStiffness;
            }
        }

        public float damper
        {
            get
            {
                return _damper;
            }
        }

        // ---------------- XPBD thickness ----------------

        public bool enableXPBDThicknessLinks
        {
            get { return _enableXPBDThicknessLinks; }
        }

        public float thicknessCompliance
        {
            get { return _thicknessCompliance; }
        }

        public bool enableIncisionLocalConstraints
        {
            get { return _enableIncisionLocalConstraints; }
        }

        public bool enableIncisionLipRelaxation
        {
            get { return _enableIncisionLipRelaxation; }
        }

        public float incisionProfileVariation
        {
            get { return _incisionProfileVariation; }
        }

        public float incisionCurvatureWidthScale
        {
            get { return _incisionCurvatureWidthScale; }
        }

        public float incisionLipRelaxation
        {
            get { return _incisionLipRelaxation; }
        }

        public float incisionLipRelaxationSpeed
        {
            get { return _incisionLipRelaxationSpeed; }
        }

        public float incisionLipMaxPushPerFrame
        {
            get { return _incisionLipMaxPushPerFrame; }
        }

        public bool enableIncisionNaturalRetraction
        {
            get { return _enableIncisionNaturalRetraction; }
        }

        public float incisionNaturalRetractionDistance
        {
            get { return _incisionNaturalRetractionDistance; }
        }

        public float incisionNaturalRetractionScale
        {
            get { return _incisionNaturalRetractionScale; }
        }

        public float incisionNaturalRetractionSpeed
        {
            get { return _incisionNaturalRetractionSpeed; }
        }

        public float incisionNaturalRetractionMaxPushPerFrame
        {
            get { return _incisionNaturalRetractionMaxPushPerFrame; }
        }

        public bool enableCompliantClampPins
        {
            get { return _enableCompliantClampPins; }
        }

        public float clampPinStiffness
        {
            get { return _clampPinStiffness; }
        }

        public float clampPinMaxLetDistance
        {
            get { return _clampPinMaxLetDistance; }
        }

        public float clampPinMaxCorrectionPerIteration
        {
            get { return _clampPinMaxCorrectionPerIteration; }
        }

        public float clampPinFollowDamping
        {
            get { return _clampPinFollowDamping; }
        }

        public float incisionThicknessRelaxRadius
        {
            get { return _incisionThicknessRelaxRadius; }
        }

        public float incisionThicknessRelaxScale
        {
            get { return _incisionThicknessRelaxScale; }
        }

        public float incisionSeparationRelaxScale
        {
            get { return _incisionSeparationRelaxScale; }
        }

        public bool resetThicknessLambdaEachFrame
        {
            get { return _resetThicknessLambdaEachFrame; }
        }

        public int thicknessSolveExtraIterations
        {
            get { return _thicknessSolveExtraIterations; }
        }


        public bool enableXPBDInPlaneDistance
        {
            get { return _enableXPBDInPlaneDistance; }
        }

        public float distanceCompliance
        {
            get { return _distanceCompliance; }
        }

        public bool resetDistanceLambdaEachFrame
        {
            get { return _resetDistanceLambdaEachFrame; }
        }

        public bool enableXPBDBend
        {
            get { return _enableXPBDBend; }
        }

        public float bendCompliance
        {
            get { return _bendCompliance; }
        }

        public bool resetBendLambdaEachFrame
        {
            get { return _resetBendLambdaEachFrame; }
        }


        public float bendAngleDeadZone
        {
            get { return _bendAngleDeadZone; }
        }

        public float bendLambdaDeadZone
        {
            get { return _bendLambdaDeadZone; }
        }

        public float bendPositionCorrectionDeadZone
        {
            get { return _bendPositionCorrectionDeadZone; }
        }

        public float sleepPositionThreshold
        {
            get { return _sleepPositionThreshold; }
        }

        public float sleepVelocityThreshold
        {
            get { return _sleepVelocityThreshold; }
        }

        // --- Collision parameters (added) ---
        public float collisionSkin
        {
            get { return _collisionSkin; }
        }

        public float collisionEnter
        {
            get { return _collisionEnter; }
        }

        public float collisionExit
        {
            get { return _collisionExit; }
        }


        public float collisionStiffness
        {
            get { return _collisionStiffness; }
        }

        public float collisionNormalDamping
        {
            get { return _collisionNormalDamping; }
        }


        public float collisionMaxPushPerIteration
        {
            get { return _collisionMaxPushPerIteration; }
        }

        public bool enableNeedleTipIndentation
        {
            get { return _enableNeedleTipIndentation; }
        }

        public float needleTipInfluenceRadius
        {
            get { return _needleTipInfluenceRadius; }
        }

        public float needleTipIndentationStiffness
        {
            get { return _needleTipIndentationStiffness; }
        }

        public float needleTipMaxPushPerIteration
        {
            get { return _needleTipMaxPushPerIteration; }
        }

        public float needleTipFalloffPower
        {
            get { return _needleTipFalloffPower; }
        }

        public bool enableTriangleSurfaceContact
        {
            get { return _enableTriangleSurfaceContact; }
        }

        public float triangleSurfaceContactStiffness
        {
            get { return _triangleSurfaceContactStiffness; }
        }

        public float triangleSurfaceContactMargin
        {
            get { return _triangleSurfaceContactMargin; }
        }

        public float triangleSurfaceContactMaxPushPerIteration
        {
            get { return _triangleSurfaceContactMaxPushPerIteration; }
        }



        public bool enableNeedlePuncture
        {
            get { return _enableNeedlePuncture; }
        }

        public float needlePunctureDepthThreshold
        {
            get { return _needlePunctureDepthThreshold; }
        }

        public float needlePunctureNormalBias
        {
            get { return _needlePunctureNormalBias; }
        }

        public float needlePunctureRelaxRadius
        {
            get { return _needlePunctureRelaxRadius; }
        }

        public float needlePunctureCaptureDepth
        {
            get { return _needlePunctureCaptureDepth; }
        }

        public float needlePuncturePortalStiffness
        {
            get { return _needlePuncturePortalStiffness; }
        }

        public float needlePuncturePortalMaxPushPerIteration
        {
            get { return _needlePuncturePortalMaxPushPerIteration; }
        }

        public float needlePunctureCollisionRelaxScale
        {
            get { return _needlePunctureCollisionRelaxScale; }
        }

        public float needlePunctureSeparationScale
        {
            get { return _needlePunctureSeparationScale; }
        }

        public float needlePunctureCooldown
        {
            get { return _needlePunctureCooldown; }
        }

        public bool enableNeedleInnerWallPuncture
        {
            get { return _enableNeedleInnerWallPuncture; }
        }

        public float needleInnerPunctureAdvanceThreshold
        {
            get { return _needleInnerPunctureAdvanceThreshold; }
        }

        public float needlePortalLateralCloseScale
        {
            get { return _needlePortalLateralCloseScale; }
        }

        public float needlePortalRetractCloseDistance
        {
            get { return _needlePortalRetractCloseDistance; }
        }

        public bool enableNeedleSurfaceSticky
        {
            get { return _enableNeedleSurfaceSticky; }
        }

        public float needleSurfaceStickyRadius
        {
            get { return _needleSurfaceStickyRadius; }
        }

        public float needleSurfaceStickyAdvanceThreshold
        {
            get { return _needleSurfaceStickyAdvanceThreshold; }
        }

        public float needleSurfaceStickyPatchRadius
        {
            get { return _needleSurfaceStickyPatchRadius; }
        }

        public float needleSurfaceStickyNormalStiffness
        {
            get { return _needleSurfaceStickyNormalStiffness; }
        }

        public float needleSurfaceStickyTangentialStiffness
        {
            get { return _needleSurfaceStickyTangentialStiffness; }
        }

        public float needleSurfaceStickyMaxPullPerIteration
        {
            get { return _needleSurfaceStickyMaxPullPerIteration; }
        }

        public float needleSurfaceStickyBreakDistance
        {
            get { return _needleSurfaceStickyBreakDistance; }
        }

        public float needleSurfaceStickyCollisionRelaxScale
        {
            get { return _needleSurfaceStickyCollisionRelaxScale; }
        }

        public bool enableNeedleTipPiercer
        {
            get { return _enableNeedleSurfaceSticky; }
        }

        public float needleTipPiercerRadius
        {
            get { return _needleSurfaceStickyRadius; }
        }

        public float needleTipPiercerAdvanceThreshold
        {
            get { return _needleSurfaceStickyAdvanceThreshold; }
        }

        public float needleTipPiercerPatchRadius
        {
            get { return _needleSurfaceStickyPatchRadius; }
        }

        public float needleTipPiercerNormalStiffness
        {
            get { return _needleSurfaceStickyNormalStiffness; }
        }

        public float needleTipPiercerAxialStiffness
        {
            get { return _needleSurfaceStickyTangentialStiffness; }
        }

        public float needleTipPiercerMaxPushPerIteration
        {
            get { return _needleSurfaceStickyMaxPullPerIteration; }
        }

        public float needleTipPiercerBreakDistance
        {
            get { return _needleSurfaceStickyBreakDistance; }
        }

        public float needleTipPiercerCollisionRelaxScale
        {
            get { return _needleSurfaceStickyCollisionRelaxScale; }
        }

        public float needleTipPiercerConeLength
        {
            get { return _needleTipPiercerConeLength; }
        }

        public float needleTipPiercerConeBaseRadiusScale
        {
            get { return _needleTipPiercerConeBaseRadiusScale; }
        }

        public float needleTipPiercerConeSurfaceMargin
        {
            get { return _needleTipPiercerConeSurfaceMargin; }
        }

        public bool enableNeedleTipPiercerFeedback
        {
            get { return _enableNeedleTipPiercerFeedback; }
        }

        public float needleTipPiercerFeedbackVelocityScale
        {
            get { return _needleTipPiercerFeedbackVelocityScale; }
        }

        public float needleTipPiercerFeedbackMaxVelocityChange
        {
            get { return _needleTipPiercerFeedbackMaxVelocityChange; }
        }

        public float needleTipPiercerFeedbackRigDistanceScale
        {
            get { return _needleTipPiercerFeedbackRigDistanceScale; }
        }

        public float needleTipPiercerFeedbackMaxRigRetreatPerFrame
        {
            get { return _needleTipPiercerFeedbackMaxRigRetreatPerFrame; }
        }

        public bool enableNeedleTipHook
        {
            get { return enableNeedleTipPiercer; }
        }

        public float needleTipHookRadius
        {
            get { return needleTipPiercerRadius; }
        }

        public float needleTipHookAdvanceThreshold
        {
            get { return needleTipPiercerAdvanceThreshold; }
        }

        public float needleTipHookDepthThreshold
        {
            get { return Mathf.Max(needleTipPiercerAdvanceThreshold, needleTipPiercerRadius * 0.55f); }
        }

        public float needleTipHookPatchRadius
        {
            get { return needleTipPiercerPatchRadius; }
        }

        public float needleTipHookStiffness
        {
            get { return Mathf.Max(needleTipPiercerNormalStiffness, needleTipPiercerAxialStiffness); }
        }

        public bool enableSutureLineConstraints
        {
            get { return _enableSutureLineConstraints; }
        }

        public float sutureLineWeakStiffness
        {
            get { return _sutureLineWeakStiffness; }
        }

        public float sutureLineWeakPullDistance
        {
            get { return _sutureLineWeakPullDistance; }
        }

        public float sutureLineMinRestDistance
        {
            get { return _sutureLineMinRestDistance; }
        }

        public float sutureLineMaxPullPerIteration
        {
            get { return _sutureLineMaxPullPerIteration; }
        }

        public float sutureLineTightenSpeed
        {
            get { return _sutureLineTightenSpeed; }
        }

        public float sutureLineApproachTightenSpeedScale
        {
            get { return _sutureLineApproachTightenSpeedScale; }
        }

        public float sutureLineLatchedTightenSpeedScale
        {
            get { return _sutureLineLatchedTightenSpeedScale; }
        }

        public float sutureLineClosureTargetDistance
        {
            get { return _sutureLineClosureTargetDistance; }
        }

        public bool enableSutureLineAdaptiveClosureTarget
        {
            get { return _enableSutureLineAdaptiveClosureTarget; }
        }

        public float sutureLineBiteDepthTargetScale
        {
            get { return _sutureLineBiteDepthTargetScale; }
        }

        public float sutureLineAdaptiveThicknessScale
        {
            get { return _sutureLineAdaptiveThicknessScale; }
        }

        public float sutureLineAdaptiveEdgeScale
        {
            get { return _sutureLineAdaptiveEdgeScale; }
        }

        public bool enableSutureLinePatchPull
        {
            get { return _enableSutureLinePatchPull; }
        }

        public float sutureLinePatchRadius
        {
            get { return _sutureLinePatchRadius; }
        }

        public int sutureLinePatchMaxVertexCount
        {
            get { return _sutureLinePatchMaxVertexCount; }
        }

        public float sutureLinePatchRingWeight
        {
            get { return _sutureLinePatchRingWeight; }
        }

        public bool enableSutureClosureBand
        {
            get { return _enableSutureClosureBand; }
        }

        public float sutureClosureBandRadiusScale
        {
            get { return _sutureClosureBandRadiusScale; }
        }

        public int sutureClosureBandMaxExtraVertexCount
        {
            get { return _sutureClosureBandMaxExtraVertexCount; }
        }

        public float sutureClosureBandWeight
        {
            get { return _sutureClosureBandWeight; }
        }

        public float sutureLineOuterPatchWeight
        {
            get { return _sutureLineOuterPatchWeight; }
        }

        public float sutureLineInnerPatchWeight
        {
            get { return _sutureLineInnerPatchWeight; }
        }

        public int sutureLinePatchRingDepth
        {
            get { return _sutureLinePatchRingDepth; }
        }

        public bool enableSutureLineXPBD
        {
            get { return _enableSutureLineXPBD; }
        }

        public float sutureLineXPBDCompliance
        {
            get { return _sutureLineXPBDCompliance; }
        }

        public bool resetSutureLineLambdaEachFrame
        {
            get { return _resetSutureLineLambdaEachFrame; }
        }

        public float sutureLineLambdaResetRestDelta
        {
            get { return _sutureLineLambdaResetRestDelta; }
        }

        public float sutureLineLambdaDecay
        {
            get { return _sutureLineLambdaDecay; }
        }

        public bool enableSutureLineContact
        {
            get { return _enableSutureLineContact; }
        }

        public float sutureLineContactGap
        {
            get { return _sutureLineContactGap; }
        }

        public float sutureLineContactStiffness
        {
            get { return _sutureLineContactStiffness; }
        }

        public float sutureLineContactMaxPushPerIteration
        {
            get { return _sutureLineContactMaxPushPerIteration; }
        }

        public bool enableSutureLineContactLatch
        {
            get { return _enableSutureLineContactLatch; }
        }

        public float sutureLineContactLatchRestSlack
        {
            get { return _sutureLineContactLatchRestSlack; }
        }

        public float sutureLineContactLatchedStiffnessScale
        {
            get { return _sutureLineContactLatchedStiffnessScale; }
        }

        public bool enableSutureLineLocalPatchContact
        {
            get { return _enableSutureLineLocalPatchContact; }
        }

        public int sutureLineLocalContactMaxPairChecksPerLine
        {
            get { return _sutureLineLocalContactMaxPairChecksPerLine; }
        }

        public bool enableSutureLineLocalTriangleContact
        {
            get { return _enableSutureLineLocalTriangleContact; }
        }

        public int sutureLineLocalContactMaxTrianglesPerEndpoint
        {
            get { return _sutureLineLocalContactMaxTrianglesPerEndpoint; }
        }

        public int sutureLineLocalContactMaxTriangleChecksPerLine
        {
            get { return _sutureLineLocalContactMaxTriangleChecksPerLine; }
        }

        public bool enableSutureLineSelfContact
        {
            get { return _enableSutureLineSelfContact; }
        }

        public float sutureLineSelfContactThicknessScale
        {
            get { return _sutureLineSelfContactThicknessScale; }
        }

        public float sutureLineSelfContactEdgeScale
        {
            get { return _sutureLineSelfContactEdgeScale; }
        }

        public float sutureLineSelfContactLatchMargin
        {
            get { return _sutureLineSelfContactLatchMargin; }
        }

        public float sutureLineSelfContactNormalStiffness
        {
            get { return _sutureLineSelfContactNormalStiffness; }
        }

        public float sutureLineSelfContactTangentialDamping
        {
            get { return _sutureLineSelfContactTangentialDamping; }
        }

        public float sutureLineSelfContactFriction
        {
            get { return _sutureLineSelfContactFriction; }
        }

        public float sutureLineSelfContactMaxPushPerIteration
        {
            get { return _sutureLineSelfContactMaxPushPerIteration; }
        }

        public int sutureLineSelfContactPersistentFrames
        {
            get { return _sutureLineSelfContactPersistentFrames; }
        }

        public float sutureLineSelfContactReleaseMargin
        {
            get { return _sutureLineSelfContactReleaseMargin; }
        }

        public int sutureLineSelfContactMaxVTPairs
        {
            get { return _sutureLineSelfContactMaxVTPairs; }
        }

        public bool enableSutureLineSelfContactEE
        {
            get { return _enableSutureLineSelfContactEE; }
        }

        public int sutureLineSelfContactMaxEEPairs
        {
            get { return _sutureLineSelfContactMaxEEPairs; }
        }

        public bool enableSutureLineCompressionFold
        {
            get { return _enableSutureLineCompressionFold; }
        }

        public float sutureLineCompressionFoldStiffness
        {
            get { return _sutureLineCompressionFoldStiffness; }
        }

        public float sutureLineCompressionFoldMaxPushPerIteration
        {
            get { return _sutureLineCompressionFoldMaxPushPerIteration; }
        }

        public float sutureLineCompressionFoldMinTargetDelta
        {
            get { return _sutureLineCompressionFoldMinTargetDelta; }
        }

        public bool enableSutureKnotConstraints
        {
            get { return enableSutureLineConstraints; }
        }

        public float sutureKnotPullStiffness
        {
            get { return sutureLineWeakStiffness; }
        }

        public float sutureKnotRestDistance
        {
            get { return sutureLineMinRestDistance; }
        }

        public float sutureKnotMaxPullPerIteration
        {
            get { return sutureLineMaxPullPerIteration; }
        }

        public float sutureKnotTightenSpeed
        {
            get { return sutureLineTightenSpeed; }
        }

        public bool enableSutureKnotPatchPull
        {
            get { return enableSutureLinePatchPull; }
        }

        public float sutureKnotPatchRadius
        {
            get { return sutureLinePatchRadius; }
        }

        public int sutureKnotPatchMaxVertexCount
        {
            get { return sutureLinePatchMaxVertexCount; }
        }

        public float sutureKnotPatchRingWeight
        {
            get { return sutureLinePatchRingWeight; }
        }

        public bool enableSutureKnotXPBD
        {
            get { return enableSutureLineXPBD; }
        }

        public float sutureKnotXPBDCompliance
        {
            get { return sutureLineXPBDCompliance; }
        }

        public bool resetSutureKnotLambdaEachFrame
        {
            get { return resetSutureLineLambdaEachFrame; }
        }


    }
}
