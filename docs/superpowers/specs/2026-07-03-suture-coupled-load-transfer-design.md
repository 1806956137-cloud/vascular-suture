# Suture Coupled Load Transfer Design

## 目标

本设计只修改 `CircumferentialSeparation` 的 Oriented PullLine 路径。非有向 PullLine、绿地切口、穿刺、可见中间层生成方式保持不变。

目标行为：

- PullLine 继续收紧并保持非零张力。
- 切口侧壁达到材料允许间隙后保持有向接触，不换边、不穿透。
- 不能继续成为侧壁法向接近的缩短量，不再直接施加到接触边缘顶点。
- 该缩短量成为持续接触压力，并驱动两侧 WholeWall 支撑带弯曲与沿接触面的切向滑移。
- Seam/contact 顶点保持接触，后方组织继续移动、旋转和折叠，孔位逐步靠拢。
- 完成不要求有限厚度组织占据同一点，也不要求目标距离归零。

## 当前结构缺陷

当前实现存在以下耦合：

1. `SutureLineConstraintJob` 忽略已有的 stiffness、max correction、compliance 和 XPBD lambda，直接求解完整距离误差。
2. Pull、ClosureBandSlide、CompressionFold 分别向 `positionCorrects` 写入，方向来源不统一。
3. Oriented Fold 使用固定 `foldDirectionHint`，Slide 使用独立的 `closureBandSlideDir`，两者不共享交叠方向。
4. Oriented Barrier 直接修改 `predictPositions`，并在最终 thickness solve 之后运行；其 feature correction 不保证 Outer/Inner 整壁柱一致。
5. patch 顶点可以同时拥有 PullAnchor、FoldColumn、SeamAnchor 等 flags，多个求解器可能重复修改同一顶点。
6. `workflowReady` 可以直接触发 Settled；Settled 随后清除 contact/fold/slide 状态，没有证明多余缩短已经被组织吸收。

这不是参数不足，而是所有权和负载传递边界不清晰。

## 总体架构

每条 Oriented PullLine 使用一个统一的负载与引导状态：

```text
finite rest shortening
        |
        v
Pull Proposal: tension + requested closure
        |
        v
Oriented Barrier: gap + pressure + overlap
        |
        v
Load Transfer
  | admitted normal closure
  | sustained contact pressure
  ` excess shortening
        |
        v
Unified Oblique Guide
  | pull-anchor traction
  | support-band bending
  ` contact-plane sliding
        |
        v
WholeWall coupling + final oriented barrier
```

架构原则：

- 单一事实源：Barrier 测量接触；Guide Selector 选择方向；其他求解器只读取。
- 单一写入者：每类顶点角色和每个自由度只有一个主要写入者。
- 接触法向、引导切向、WholeWall 厚度三个自由度相互正交或通过明确接口传递。
- 不用两个位置修正反复抵消来伪造压力。

## 每条线的状态

复用现有字段：

- `lambda`：Oriented PullLine 的持久、非负 line tension lambda，跨 solver iteration 和跨帧保存。
- `barrierNormalLoad`：当前 Barrier 法向反力。
- `compressionDemand`：当前 solve 中被阻止的法向闭合长度。
- `minimumBarrierGap`：最小有向材料间隙。
- `foldDirectionHint`：候选 guide axis 来源。
- `lastCompressionFoldPush`、`lastClosureBandSlidePush`：应用量诊断。

新增最小状态：

- `requestedClosure`：本轮线约束请求的闭合长度。
- `admittedClosure`：材料间隙允许的闭合长度。
- `blockedClosure`：被 Barrier 拒绝的闭合长度。
- `frameRestShorteningRequest`：本帧 rest 的实际缩短量，只在 rest progression 提交时写一次。
- `frameAdmittedNormalClosure`：本帧最终 Barrier 允许并实际保留下来的法向闭合量，只在帧末结算时写一次。
- `pendingSettlementFrame`：防止同一帧在多个 manifold 或 iteration 中重复结算的 frame stamp。
- `pendingShortening`：尚未被实际组织形变吸收的累计 rest 缩短。
- `foldAbsorbed`、`slideAbsorbed`：累计实际吸收量。
- `signedOverlap`：承压 manifold 在 guide axis 上的载荷加权相对偏移。
- `guideState`：`Unresolved`、`Provisional` 或 `Locked`。
- `guideSign`：`-1`、`0` 或 `+1`。
- `guideRatio`：当前斜向分量比例。
- `guideDirection`：统一、锁定后的接触面切向方向；可复用现有 `closureBandSlideDir` 存储。

基本关系：

```text
requestedClosure = admittedClosure + blockedClosure
frameRestShorteningRequest = max(0, previousCurrentRest - currentRest)
frameAdmittedNormalClosure = clamp(finalAcceptedNormalClosure,
                                   0,
                                   frameRestShorteningRequest)
newExcess = max(0, frameRestShorteningRequest
                   - frameAdmittedNormalClosure)
actualAbsorption = min(pendingShortening + newExcess,
                       actualFoldIncrease + actualSlideIncrease)
pendingShortening = max(0, pendingShortening
                           + newExcess
                           - actualAbsorption)
```

`frameAdmittedNormalClosure` 由本帧开始时的 committed material gap 与最后一次 WholeWall final Barrier 后的 gap 之差得到；它表示最终仍被允许保留的净法向闭合，而不是某个 iteration 的 proposal 总和。

`compressionDemand`、`requestedClosure`、`admittedClosure` 和 `blockedClosure` 是每次 solve 的瞬时诊断；`pendingShortening` 是跨帧执行器预算。只有帧末唯一结算点可以用上述 `newExcess` 增加 pending。manifold job、solver iteration 和中间 Barrier 都不得写 `pendingShortening`，也不得把各自的 blocked closure 求和后转成 pending。

以下量均不得生成 pending：孔位距离误差、`actualDistance - currentRestDistance`、PullLine 的约束值 `C`、累计 target delta、manifold 数量或 iteration 次数。它们可以参与 line proposal 或诊断，但不是 rest 缩短请求的事实源。

## 顶点角色所有权

角色优先级固定为：

```text
SeamAnchor > PullAnchor > FoldColumn > unclassified patch vertex
```

- `SeamAnchor`：只接受 Oriented Barrier 和 WholeWall 一致性修正。
- `PullAnchor`：只接受 PullLine 的法向/斜向孔位牵引，不接受 Fold 或独立 Slide。
- `FoldColumn`：只接受统一支撑带响应。
- 未分类 patch 顶点：只参与材料约束和必要接触，不参与主动引导。

如果一个顶点同时带有多个 flags，应用阶段按上述优先级选择唯一角色，不能叠加多个主动修正。

## Pull Proposal 与张力

`SutureLineConstraintJob` 对 Oriented 路径改为有限的 tension-only proposal：

1. 使用当前距离、有限 `currentRestDistance`、compliance、dt 和持久 lambda 计算 XPBD line tension。
2. 使用 stiffness 和 `sutureLineMaxPullPerIteration` 限制本轮请求闭合量。
3. Barrier 未激活时，PullAnchor WholeWall 孔柱按正常闭合方向移动。
4. Barrier 激活后，只有 `admittedClosure` 可以成为切口法向接近；超过材料间隙的部分进入 `blockedClosure`。
5. 被阻止的法向分量仍作为 PullAnchor 后方牵引和 line lambda 保留，但禁止写入 Seam/contact 顶点。

这使拉线张力通过孔位、支撑带和材料约束传递到接触面；Barrier 在接触面产生平衡反力，而不是同一顶点先拉入、再推出。

### Line lambda 生命周期

Oriented PullLine 必须使用同一条约束状态上的持久 lambda：

```text
lambdaNext = max(0, lambdaPrevious + deltaLambda)
```

- 新帧开始、rest target 变化、Barrier 激活、进入 `workflowReady` 或进入 Settled 都不能清零 lambda。
- 现有 `resetSutureLineLambdaEachFrame`、`lambdaResetRestDelta` 和逐帧 `lambdaDecay` 不得作用于 Oriented 路径。
- 只允许在线刚创建、约束失效/删除，或经 complementarity 判定为真实松弛并越过 release hysteresis 时释放 lambda。
- Settled 不是 release；只要线仍受拉且 Barrier 仍承压，line lambda 必须继续参与下一帧 warm start。
- 持久 lambda 不能只出现在 `deltaLambda` 公式的 `alpha * lambda` 项中。每个 simulation frame 第一次执行 PullLine 时，必须把上一帧保留的 lambda 作为一次 warm-start correction 施加；同帧后续 solver iteration 只解并施加 `deltaLambda`。
- `lambdaWarmStartFrame` 是一次性消费戳：同一 frame 禁止重复重放持久 lambda。没有 warm start 的“只保存 lambda”会在 `alpha * lambda == C * stiffness` 后得到零增量，使组织在 rest 已缩短时停止移动。

## Oriented Barrier 与持续压力

Barrier 继续使用：

```text
C = dot(pointB - pointA, barrierNormal) - materialGap >= 0
```

其职责限定为：

- 维护 committed SideA/SideB 合法方向。
- CCD 防止首次接触跨面。
- 保留 persistent manifold 和跨 iteration、跨帧的非负 normal lambda。
- 输出 `minimumBarrierGap`、`barrierNormalLoad`、`signedOverlap`。
- 计算 `blockedClosure`，但不选择折叠方向。

Barrier 不能调用 Fold/Slide 逻辑。Guide Selector 不能修改 barrier normal。

Settled 后 Barrier 和 line lambda 继续活动；压力不能通过清零 contact 状态来“保持”。

### Barrier normal lambda 生命周期

每个 `SutureOrientedContactManifoldInfo.normalLambda` 属于 persistent manifold，而不是某次 job 调用：

- 进入新帧、进入下一 solver iteration、执行 final Barrier 或状态切换时都不能清零。
- 只有 manifold 经过 release hysteresis 确认真实分离、SideA/SideB 契约失效，或所属 PullLine 被删除时才释放。
- 每次 solve 前可以清零并重算 `barrierNormalLoad`、active count、`compressionDemand` 等聚合诊断，但禁止借清聚合量之名清除底层 manifold `normalLambda`。
- 第一次 Barrier 与 final Barrier 读取并更新同一份 manifold lambda；final pass 不是一组临时 contact constraints。

## 对称破缺与方向锁定

统一 `guideDirection` 是 `foldDirectionHint` 去除 barrier-axis 分量后的单位向量。方向符号使用以下优先级：

1. 承压 manifold 的载荷加权真实 `signedOverlap`。
2. 上一帧到 predicted positions 的相对 guide-axis 速度。
3. portal `insertDirection`、entry/exit 顺序和局部切口 frame 得到的缝线路线手性。
4. 仅当上述全部退化时，使用 pass/line identity 产生可复现的极弱符号。

状态机：

```text
Unresolved -> Locked       when real geometry is confident
Unresolved -> Provisional  when only route/identity bias exists
Provisional -> Locked      when measurable overlap stabilizes
Locked -> Unresolved       only after full manifold release hysteresis
```

默认判据由局部尺度 `L = max(localWallThickness, localAverageEdgeLength)` 派生：

- overlap deadband：`max(1e-5, 0.05 * L)`。
- 几何符号连续稳定三个 barrier evaluations 后锁定。
- Provisional 只允许最大引导量的约 20%，只负责启动非对称。
- `Locked` 在同一个接触周期内禁止翻转。

世界 Y、当前 triangle winding 和每帧随机数都不能决定上下关系。

## 斜向 PullLine

接触承压后，PullAnchor 使用同一个 guide state：

```text
sideA correction = +close * closureAxis
                   -guide * guideSign * guideDirection

sideB correction = -close * closureAxis
                   +guide * guideSign * guideDirection
```

`guideRatio` 满足：

- Barrier 未承压时为零。
- Provisional 时保持弱偏置。
- Locked 后随 contact pressure 和 pending shortening 平滑增加。
- 达到目标交叠量后平滑衰减到零，但不反向。
- 使用一个明确的最大斜向角限制；初始设计采用 25 度，作为唯一新增的外部可调引导参数。

斜向分量属于同一条线张力，不是额外独立力场。

## 统一支撑带响应

Oriented 路径不再让 `SutureLineClosureBandSlideJob` 与 `SutureLineCompressionFoldJob` 独立选择方向。两者合并为一个 support response，或由一个 Job 一次性写入合成修正。

对现有 `bandT` 使用：

```text
slideProfile(t) = 1 - t
foldProfile(t)  = 4 * t * (1 - t)

response(t) = guideDirection * guideSign * sideSign
              * (slideAmount * slideProfile(t)
                 + foldAmount * foldProfile(t))
```

- `t=0` 是孔位侧，`t=1` 是接触 Seam。
- SeamAnchor 不接受 support response。
- FoldColumn 在中间获得最大弯曲响应。
- slide profile 允许后方组织相对接触边缘沿接触面移动。
- SideA 与 SideB 使用相反 `sideSign`，延续已经锁定的交叠，而不是把 overlap 压回零。

fold/slide 的幅值由 `pendingShortening`、pressure 和现有每轮最大修正限制共同决定。禁止用静态 line tension 每个迭代无限重放 fold demand。

## 实际吸收量

完成判断不能只统计“请求过多少修正”。必须测量最终 solve 后仍存在的形变。

每帧记录：

- `slideMetric`：`guideSign * dot(centerB - centerA, guideDirection)`，其中 center 只由唯一归类为 FoldColumn 的 WholeWall 柱加权得到。
- `foldMetric`：对每个 FoldColumn，先用 `bandT` 在 PullAnchor center 与 SeamAnchor center 之间构造线性弦点，再计算该柱相对弦点沿锁定 guide 方向的正向偏移；SideA/SideB 使用相反 side sign，最后按 WholeWall 柱权重求和。

只累计正向、在最终 Barrier 和 thickness solve 后仍保留的变化。结算发生在每帧恰好一次的 post-solve 点：

```text
newExcess = max(0, frameRestShorteningRequest
                   - frameAdmittedNormalClosure)
slideAbsorbedStep = max(0, slideMetricNow - slideMetricPrevious)
foldAbsorbedStep  = max(0, foldMetricNow - foldMetricPrevious)
actualAbsorption  = min(pendingShortening + newExcess,
                        slideAbsorbedStep + foldAbsorbedStep)
pendingShortening = max(0, pendingShortening
                           + newExcess
                           - actualAbsorption)
pendingSettlementFrame = currentFrame
```

slide metric 描述近似刚体相对滑移；fold metric 描述去除线性移动后的曲率，因此两者不重复计数同一位移。

主线程在本帧 rest progression 提交时记录一次 `frameRestShorteningRequest`；最终 Barrier 完成后，post-solve 结算器测量一次 `frameAdmittedNormalClosure` 并检查 frame stamp。任何 constraint job 都只输出瞬时量，不能直接增加或减少 pending。这样 manifold 数量和 solver iteration 数变化不会改变本帧新增的 pending。

## WholeWall 一致性

所有主动响应以明确的 Outer/Inner 配对为 WholeWall 柱单位。`crossLayerLinks` 是可一对多的厚度约束网络，不是柱身份表：

- PullAnchor 的 Outer/Inner 孔柱接受兼容修正。
- FoldColumn 的 Outer/Inner 使用相同支撑响应。
- Oriented Barrier 可以继续用实际 Outer/Inner 侧壁三角形计算接触 feature、法向、barycentric weight 和 gap，但约束质量与位置修正必须提升为 WholeWall 柱自由度。
- `SutureSidewallBoundaryEdgeInfo` 的 `outerA ↔ innerA`、`outerB ↔ innerB` 是 Oriented Barrier 的柱身份真相源。虚拟侧壁三角形必须为每个顶点保留其明确的跨层 partner；不得在求解时从 `crossLayerLinks` 反推或任选邻接 link。
- PullLine 孔柱的身份真相源是同一次穿刺记录中的 Outer portal 与 Inner portal。`SutureLineConstraintInfo` 必须同时保存两层 portal；孔柱点、有效质量和位置修正直接由这两个 portal 计算，不能调用“第一条匹配的 `crossLayerLinks`”。
- feature vertex 按所属接触三角形的顶点槽位读取 partner，并组成排序后的 column id；同一 feature 中属于同一柱的权重和梯度先合并，再计算 XPBD denominator 和 lambda。
- 每个柱的有效质量是 Outer/Inner 组合质量；求出的同一个柱位移 `deltaXColumn` 必须等量写入该柱的 Outer 与 Inner，不能只写参与接触 feature 的单层顶点。
- 若柱的任一层按约束契约不可参与，则该柱整体按 immobile/invalid 处理；禁止退回单层修正。
- 第一次与 final Oriented Barrier 必须使用同一份虚拟侧壁 column mapping 和上述柱求解核。缺失、无效或自配对的 partner 使 Oriented contract 失败；禁止 per-feature-vertex fallback。
- Barrier normal lambda 与 line lambda 使用同样的每帧一次 warm-start 契约；manifold 以独立 frame stamp 防止第一次 Barrier、final Barrier 和多个 iteration 重复预载。
- 最后一次 Barrier 后不能留下未经验证的 Outer/Inner 相对剪切。

第一次 Barrier 与 final Barrier 应共享同一 WholeWall 柱应用核；其中 final Barrier 的柱一致性是一票否决项。禁止采用 Barrier、Thickness、Barrier、Thickness 无限交替的兜底方式。最终 Barrier 本身必须保持柱一致。

## 求解顺序

每个 solver iteration 的 Oriented 路径顺序为：

1. material distance constraints。
2. material bend constraints。
3. Pull Proposal：从持久 line lambda 更新 tension 和 requested closure；不得做逐帧 lambda reset。
4. 第一次 Oriented Barrier：更新 gap、pressure、overlap，阻止法向非法量。
5. Guide Selector：只更新共享 guide state。
6. Load Transfer：应用 PullAnchor 斜向牵引与统一 FoldColumn response。
7. cross-layer thickness coupling。
8. 其他 final constraints。
9. WholeWall-column-coherent final Oriented Barrier。

非 Oriented 路径保持现有 scheduling，不隐式切换到新策略。

`frameRestShorteningRequest` 在进入本帧 solver iteration 之前由 rest progression 写一次；`pendingShortening` 的新增和实际吸收在步骤 9 完成后的 post-solve 结算点写一次。两者都不属于 iteration 内 scheduling。

## 生命周期与完成判据

生命周期：

```text
Approach
-> ContactCandidate
-> LoadTransfer
-> Absorbing
-> SettledHold
```

- `Approach`：正常 tension-only 拉近。
- `ContactCandidate`：Barrier 首次激活，选择或准备 guide sign。
- `LoadTransfer`：rest 继续收紧，blocked closure 只形成瞬时压力需求；帧末由 rest 缩短请求与最终 admitted closure 的差值生成 pending shortening。
- `Absorbing`：不再新增 rest shortening；pending shortening 继续驱动支撑带响应。
- `SettledHold`：停止新 Fold/Slide 驱动，但保留 line tension 和 Barrier pressure。

完成必须连续保持现有 settled hold 帧数，并同时满足：

1. 所有 load-bearing manifolds 的 gap 位于材料允许带内，无 penetration 和 side inversion。
2. 持久 line lambda 高于最小张力阈值。
3. `pendingShortening` 低于局部尺度派生的 residual tolerance。
4. fold/slide metrics 已稳定，没有被后续约束撤销。
5. guide state 为 Locked，接触周期内没有翻转。
6. WholeWall column 最大 Outer/Inner 误差低于 thickness tolerance。

`actualDistance == targetRest`、`targetRest == 0`、`workflowReady != 0` 均不能单独触发完成。

`ApplySutureLineSettledHold` 必须：

- 冻结新的 rest progression。
- 保留 line lambda、manifolds、barrier state、pressure 和 contact latch。
- 清除主动 fold/slide drive，而不是清除接触事实。

## 创建时验证与失败行为

Oriented PullLine 创建前验证：

- SideA/SideB VirtualSidewall 注册完整。
- persistent manifolds 可构造。
- barrier axis 和 guide axis 有效。
- 两侧至少存在可移动 PullAnchor、FoldColumn、SeamAnchor。
- 所需 WholeWall columns 有有效 cross-layer links。
- 顶点角色归一化后仍满足每个通道的最小数量。

失败时拒绝 PullLine 创建并报告最早失败项。禁止回退到 unsigned self-contact、patch fallback 或继续收紧后冻结。

运行时若 `pendingShortening` 长期不下降：

- 状态保持 LoadTransfer/Absorbing。
- 输出 stalled reason、role counts、link error、pressure 和 absorption metrics。
- 不提高阈值、不翻转 guide sign、不强制 Settled。

## 诊断

新增统一日志：

```text
[Suture][LoadTransfer]
line= state=
req= admitted= blocked=
lineLambda= pressure= gap=
pending= foldAbs= slideAbs=
overlap= guideState= guideSign= guideRatio=
pullCount= foldCount= seamCount=
linkError= settleGate= reason=
```

诊断必须区分：

- 请求闭合量与实际允许闭合量。
- 瞬时 compression demand 与累计 pending shortening。
- 请求 fold/slide 与最终保留的 absorbed fold/slide。
- line tension 与 Barrier pressure。
- guide candidate、provisional、locked 状态。

## 自动测试

纯策略测试：

- requested closure 能严格拆为 admitted + blocked。
- Oriented line lambda 跨帧、rest target 变化、Barrier 激活和 Settled 保持；只有真实 release/删除才清除。
- manifold normal lambda 跨帧和跨 Barrier pass 保持；清零聚合诊断不会清零 manifold lambda。
- 同一帧改变 solver iteration 数或 manifold 数，不改变 `newExcess` 和 pending 结算结果。
- pending 只接受一次 `frameRestShorteningRequest - frameAdmittedNormalClosure`；孔位误差和 line constraint `C` 不能生成 pending。
- blocked normal closure 不写 Seam/contact 顶点。
- guide 优先级为 geometry、velocity、route、identity。
- Provisional 可被真实几何覆盖；Locked 在 release 前不能翻转。
- 顶点 role precedence 保证每个主动顶点只有一个写入者。
- fold/slide profile 在 Seam 为零，FoldColumn 中部为正。
- Barrier feature 权重按 column id 合并，XPBD denominator 使用 WholeWall 柱质量。
- Outer/Inner 柱修正完全一致；缺失/歧义 link 被拒绝且不发生单层 fallback。
- Settled predicate 同时要求 gap、tension、absorption、guide 和 column integrity。

集成测试：

- Oriented scheduling 为 Proposal、Barrier、Guide、LoadTransfer、Thickness、FinalBarrier。
- Oriented 路径不再同时运行独立 ClosureBandSlide 与 CompressionFold writers。
- final Barrier 具有 WholeWall link 输入并保持 column coherence。
- 非 Oriented 和绿地路径源码/行为隔离。

## Unity Play 验收

单条 PullLine：

- 首次接触无 side inversion、无中间层穿模。
- PullLine rest 在接触后继续收紧。
- line lambda 和 barrier pressure 持续非零。
- guide sign 最多锁定一次，不发生上下翻转。
- pending shortening 随实际折叠/滑移下降。
- Seam 保持接触，后方 WholeWall 支撑带继续移动和折叠。
- 孔位逐步靠拢，但有限厚度侧壁不被要求重合。
- Settled 后仍保持张力和压力。

多条顺序 PullLine：

- 每条线独立拥有 guide state 和 pending shortening。
- 新线不能修改旧线已经锁定的 guide sign。
- 多条线不形成统一规则锯齿或 ordinal zipper。
- Outer/Inner、可见切口和中间渲染层保持连续。

参数增大、日志显示 Active 或编译通过都不是验收；必须观察到合法接触、持续张力、真实支撑带形变和稳定吸收同时成立。
