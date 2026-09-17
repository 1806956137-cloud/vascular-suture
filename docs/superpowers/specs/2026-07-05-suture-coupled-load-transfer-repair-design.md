# Oriented Suture Coupled Load Transfer Repair Design

## 目标

修复 `CircumferentialSeparation` 的 Oriented PullLine 在首次接触前停止推进，以及后续 Barrier、Guide、LoadTransfer 没有形成同帧闭环的问题。

本修订不引入新的驱动模型，而是恢复既有《Suture Coupled Load Transfer Design》已经定义的职责、状态、求解顺序和验收逻辑。

必须达到：

- Barrier 未激活时，有限 tension-only Pull Proposal 持续推动明确的 WholeWall PullAnchor 孔柱靠近。
- 首次有向接触后，Barrier 成为接触事实源，阻止非法法向闭合并输出压力与交叠。
- 同一帧内根据 Barrier 输出选择 Guide，并将 blocked closure 传给统一支撑带响应。
- Seam/contact 顶点、PullAnchor、FoldColumn 各自只有一个主动写入者。
- Outer/Inner 使用明确身份配对，不从一对多 `crossLayerLinks` 临时猜测柱身份。
- 非 Oriented PullLine、绿地切口、穿刺和中间渲染层保持不变。

## 已确认问题

最近运行中，`currentRestDistance` 持续下降，而 `actualDistance` 在首次接触前停止下降；同时 Oriented Barrier 保持 `Inactive`，`barrierNormalLoad` 和 `pendingShortening` 均为零。

这说明最早未证实层是 Pull Proposal 的实际输出与保留位移。当前实现又存在以下结构性偏离，使后续链路即使局部代码存在，也没有按原设计组合：

1. 当前 Oriented 顺序实际为 Pull、独立 Slide、Contact、独立 Fold、Barrier；原设计要求 Pull、Barrier、Guide、统一 LoadTransfer。
2. Guide 在主线程根据上一帧 Barrier 状态更新，不读取本轮首次 Barrier 的输出。
3. Oriented Slide 与 Fold 仍由两个 Job 分别写入，不满足单一写入者。
4. `requestedClosure`、`admittedClosure`、`blockedClosure`、`signedOverlap`、最终 fold/slide metric 和统一 `[Suture][LoadTransfer]` 诊断未完整落地。
5. 现有回归主要检查字段或源码 token 存在，没有证明 Barrier 未激活时跨帧 Pull Proposal 持续产生并保留闭合位移。

## 不采用的方向

- 不新增 Approach Servo 或另一套外部力场。
- 不用 `actualDistance - currentRestDistance`、约束值 `C` 或累计 `targetDelta` 生成 `pendingShortening`。
- 不通过增大 stiffness、max correction、迭代数或放宽阈值让当前样例通过。
- 不回退已经建立的显式 Outer/Inner portal、VirtualSidewall column partner、WholeWall Barrier 和 persistent lambda 身份契约。
- 不让 Oriented 路径在失败后回退到 unsigned contact、单层修正或 legacy patch writer。

## 保留的可信契约

以下实现继续作为修订基础：

- `SutureLineConstraintInfo` 保存每侧 Outer/Inner portal。
- Oriented PullAnchor 点、有效质量和修正直接由明确 portal pair 计算。
- VirtualSidewall contact triangle 保存逐槽位 `columnPartner`。
- Barrier feature 按明确 column id 合并梯度，并等量修正 Outer/Inner。
- line lambda 和 manifold normal lambda 均持久化，并使用独立的每帧 warm-start stamp。
- `frameRestShorteningRequest` 在主线程每帧只提交一次。
- `pendingShortening` 只允许在 final Barrier 后的 post-solve 结算点写一次。

## 修订后的职责边界

### Pull Proposal

Pull Proposal 只负责 Oriented line tension 和 PullAnchor 牵引：

- 输入：当前 WholeWall portal pair 距离、`currentRestDistance`、dt、compliance、stiffness、max correction、persistent line lambda。
- 输出：更新后的非负 line lambda、`requestedClosure`、实际提交给 PullAnchor 的有界 correction。
- Barrier 未激活时，correction 只沿正常闭合方向作用于明确的 PullAnchor WholeWall 孔柱。
- 每帧第一次求解只消费一次上一帧 lambda warm start；同帧后续求解只应用 `deltaLambda`。
- 当 `C > epsilon`、两侧存在可移动自由度且 max correction 非零时，不允许在没有命名失败原因的情况下连续输出零 proposal。
- Pull Proposal 不写 SeamAnchor、FoldColumn、Barrier 状态或 `pendingShortening`。

接触前失败必须归属 Pull Proposal：日志要能区分 line lambda 为零、proposal 被限幅、无可移动质量、修正已提交但被后续约束撤销。

### Position Commit / Sleep Boundary

Unity 实测已经确认：Pull Proposal 每帧持续输出 `req=0.000734`，line lambda 持续增长，WholeWall portal distance 在求解结束时由 `0.105157` 降到 `0.105150`；但下一帧开始距离又恢复为 `0.105157`。当前场景的 `sleepPositionThreshold=0.00002`、`sleepVelocityThreshold=0.001`，末尾 `UpdateVelocitiesAndPositionsJob` 因此把显式 portal 的微小约束位移恢复为 `prevPos`。这使 phase66 已产生的 Pull 修正无法跨帧提交。

修复必须复用现有 `ConstraintType` 通道增加 `SutureDriven` 身份，而不是改 Pull 数值或放宽全局 sleep 阈值：

- 每帧在前置 Job 完成后，先清除所有顶点上一帧的 `SutureDriven` bit，再从当前有效的 Oriented PullLine 重建。
- 只标记 `enabled != 0`、`orientedBarrierEnabled != 0`、`barrierContractValid != 0` 的约束。
- 标记范围严格限定为两侧明确 Outer/Inner portal 中 barycentric weight 非零的真实顶点；不扩张到 patch、SeamAnchor 或普通 cloth 顶点。
- `UpdateVelocitiesAndPositionsJob` 仍对普通顶点执行原 sleep 判定；仅 `SutureDriven` 顶点保留求解器已经写入的微小 `predictPositions` 修正。
- Hard Pin、质量为零、Barrier、Guide、lambda、rest progression 与全局 sleep 参数均不改变。
- 约束失效时，下一帧重建会移除对应 bit；共享 portal 被其他有效 Oriented PullLine 使用时仍保持 bit，避免残留或错误清除。

该边界只负责把已完成的求解结果提交到 `positions`，不生成新驱动力，也不成为第二个位置 writer。

### Oriented Barrier

Barrier 只负责有向接触事实：

- 输入：当前预测位置、Pull Proposal 已写入的 `requestedClosure`、明确 VirtualSidewall column mapping、persistent manifolds。
- 输出：`minimumBarrierGap`、`barrierNormalLoad`、`compressionDemand`、`admittedClosure/blockedClosure`、载荷加权 `signedOverlap`。
- Barrier 不选择 Guide，不调用 Fold/Slide，不写 `pendingShortening`。
- 第一次与 final Barrier 共用同一 WholeWall 柱求解核和同一份 manifold lambda。
- column 身份缺失、歧义、自配对或任一层不可参与时，Oriented contract 在本层失败，不发生单层 fallback。

### Guide Selector

Guide Selector 必须位于本轮首次 Barrier 之后：

- 只读取 Barrier 输出和既有路线身份，写 `guideState`、`guideSign`、`guideDirection`、`guideRatio`。
- 符号优先级保持原设计：真实 `signedOverlap`、相对速度、portal/route 手性、最后才是稳定 identity parity。
- 真实几何符号连续稳定三个 Barrier evaluation 后才进入 `Locked`。
- `Locked` 在同一接触周期内禁止翻转；只有完整 release hysteresis 才回到 `Unresolved`。
- 世界 Y、当前 triangle winding 和随机数不得决定 Guide 符号。

### Unified Support Response

Oriented 路径只保留一个支撑带主动写入者：

- 输入：`pendingShortening`、Barrier pressure、锁定 Guide、明确 FoldColumn WholeWall pairs、`bandT`。
- 使用原设计的 `slideProfile(t) = 1 - t` 与 `foldProfile(t) = 4t(1-t)` 合成一次 correction。
- PullAnchor、SeamAnchor 不接受 support response；仅 FoldColumn 接受。
- SideA/SideB 使用相反 side sign，延续锁定交叠方向。
- Oriented 路径不得再进入独立 `SutureLineClosureBandSlideJob` 和 `SutureLineCompressionFoldJob`；这两个 legacy writer 仅服务非 Oriented 路径。
- FoldColumn 的 Outer/Inner partner 必须在构建时明确保存；`crossLayerLinks` 只承担厚度约束，不作为运行时柱身份搜索表。

### Post-solve Settlement

结算仍严格遵循原设计：

```text
newExcess = max(0, frameRestShorteningRequest
                   - frameAdmittedNormalClosure)
actualAbsorption = min(pendingShortening + newExcess,
                       actualFoldIncrease + actualSlideIncrease)
pendingShortening = max(0, pendingShortening
                           + newExcess
                           - actualAbsorption)
```

- `frameAdmittedNormalClosure` 来自本帧 committed gap 与 final Barrier 后 gap 的差值。
- fold/slide absorption 只统计 final Barrier 和 thickness 后仍保留的正向形变。
- Barrier 未激活的 Approach 阶段不从孔位距离误差生成 pending；该阶段是否持续推进由 Pull Proposal 自己验收。
- `pendingSettlementFrame` 保证每帧只结算一次。

## 求解顺序

每个 solver iteration 的 Oriented 路径固定为：

1. Material distance。
2. Material bend。
3. Pull Proposal，并立即应用 staged correction。
4. First Oriented Barrier。
5. Guide Selector。
6. Unified Support Response，并立即应用 staged correction。
7. Cross-layer thickness coupling。
8. 其他 final constraints。
9. WholeWall-column-coherent Final Oriented Barrier。

所有 iteration 完成后，每帧只运行一次 Post-solve Settlement。

非 Oriented 路径继续使用现有 legacy Pull、Contact、Slide 和 Fold scheduling，不隐式进入新链路。

## 状态与诊断

必须完整保留或补齐原设计状态：

- 瞬时：`requestedClosure`、`admittedClosure`、`blockedClosure`、`compressionDemand`、`signedOverlap`。
- 跨帧：line lambda、manifold normal lambda、`pendingShortening`、`foldAbsorbed`、`slideAbsorbed`、Guide 状态。
- 帧结算：`frameRestShorteningRequest`、frame start/final gap、`frameAdmittedNormalClosure`、`pendingSettlementFrame`。
- 实际吸收：previous/current fold metric 与 slide metric。

统一日志采用原设计格式：

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

在 Approach 阶段，同一日志必须同时显示 current rest、actual WholeWall portal distance 和 Pull Proposal 状态，使“命令继续缩短但几何停止”稳定归属到 Pull Proposal，而不是等到 Barrier 或 Settled 才暴露。

## 失败处理

- Pull Proposal 连续输出零 correction：报告 `zero-lambda`、`zero-delta`、`immobile-pull-column` 或 `proposal-not-retained` 中最早成立的一项。
- Barrier contract 失败：保持 Oriented 失败状态并报告具体 column/manifold 原因，不继续 LoadTransfer。
- Guide 为 `Unresolved`：不启动 support response；处于 `Provisional` 时只允许原设计规定的弱引导比例，禁止按 Locked 强度响应。
- FoldColumn partner 不完整：拒绝该 Oriented PullLine 创建或使其 contract 失败，不从 `crossLayerLinks` 猜测。
- pending 长期不下降：保持 LoadTransfer/Absorbing，并输出 pressure、role count、link error 和 absorption metric；不强制 Settled。

## 测试与验收

### 数值与纯策略回归

- 使用项目真实量级的面积质量、dt、compliance、stiffness、max correction 和两轮 solver iteration。
- Barrier 未激活且 rest 连续缩短时，line lambda 保持非负，Pull Proposal 持续有界输出，隔离环境中的 WholeWall portal distance 连续下降。
- 同一帧只 warm start 一次；改变 iteration 数不会重复回放旧 lambda。
- `requestedClosure = admittedClosure + blockedClosure`。
- 有效 Oriented PullLine 的显式 portal 顶点带有 `SutureDriven`，其低于通用 sleep 阈值的有限修正仍能跨帧提交；非 portal 顶点的 sleep 行为不变。
- pending 只由每帧唯一 rest request 与 final admitted closure 结算，不读取 `C`、target delta 或 portal distance error。
- role precedence 和显式 WholeWall partner 保证每个主动自由度只有一个写入者。

### 调度与源码契约

- Oriented 调度严格为 Pull、Barrier、Guide、Unified Support、Thickness、FinalBarrier。
- Guide 不在首次 Barrier 前更新。
- Oriented 路径不调度两个 legacy Slide/Fold writer。
- First/Final Barrier 使用同一 column kernel 和 persistent manifold storage。
- 非 Oriented 与绿地路径没有行为变化。

### Unity Play 分层验收

1. Approach：Barrier 仍 Inactive 时，rest 缩短必须伴随 WholeWall portal distance 的持续净下降；若停滞，统一日志给出 Pull Proposal 原因。
2. ContactCandidate：首次接触无 side inversion、无单层穿模，Barrier pressure 变为非零。
3. LoadTransfer：Guide 在本接触周期最多锁定一次，pending 只由 blocked rest shortening 增加。
4. Absorbing：Seam 保持接触，FoldColumn 后方组织产生最终仍保留的折叠/滑移，pending 随实际吸收下降。
5. SettledHold：同时满足 gap、line tension、pending、metric 稳定、Guide Locked 和 WholeWall column integrity；进入后仍保持 line tension 与 Barrier pressure。

参数增大、编译通过、源码 token 存在或日志显示 Active 均不构成行为验收。

## 决策记录

采用“保留显式身份契约，从最早失败的 Pull Proposal 向后恢复原设计”的方案。

Unity phase67 证据进一步确认，当前最早断点位于 Pull Proposal 之后的 Position Commit：提案与同帧修正存在，但被通用 sleep 门丢弃。因此 Task 2 必须先修复局部 `SutureDriven` 提交身份；不重写已证明非零的 Pull Proposal 数值。

拒绝两种替代方案：

- 仅补 line lambda 或调参：无法修复错误时序、双 writer 和缺失的同帧负载传递。
- 整体回退到 coupled load transfer 之前：会丢失已经正确建立的 WholeWall portal 与 Barrier column 身份契约。
