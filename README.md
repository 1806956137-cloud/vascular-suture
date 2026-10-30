# 基于 XPBD 的血管端对端缝合仿真

使用 **Unity 2022.3** 构建的血管缝合仿真工程，集成双层血管变形、针具接触与穿刺、孔道跟随、缝线牵拉，以及手动操作和六针自动缝合两种流程。

主场景将双层血管沿环周切开、分离，通过针具依次穿过两个断端并牵拉缝线，完成端对端吻合的仿真流程。

![仿真工作台主页](docs/images/home.png)

## 功能与流程

| 模块 | 当前功能 |
| --- | --- |
| 主页与双模式 | Play 后进入主页，分别选择手动或自动流程 |
| 双层血管 | 内外壁共同求解，通过跨层连接维持壁厚与层间关系 |
| 手动操针 | 移动、推进、转向、滚转；针具锁定、速度调节与精细操作 |
| 工具提示 | 十字双向箭头、针轴箭头、转向弧线、滚转环形箭头 |
| 穿刺与孔道 | 记录双层穿刺，维护当前孔道、历史孔道和针体离壁状态 |
| 缝线收拢 | 配对实际孔道，通过拉线约束逐步收拢局部组织 |
| 自动六针 | 三针定位、三针补充，串联穿刺、退出、转移和拉线 |
| 观察与记录 | 旋转、平移、缩放、聚焦、总览、暂停和截图 |

### 手动缝合

进入手动模式后默认选中并锁定针具，通过工具按钮或数字键选择操作，在视图内左键拖动操针。六针参考孔位以青绿圈贴合血管表面显示，随组织变形实时更新。

**流程按实际穿刺事件推进**：独立的完整双层穿刺在针体退出后计数，同一事件记录一次。每累计两次穿刺后开放拉线，收拢达到推进条件后进入下一针。操作者自行安排两侧穿刺位置和进出方向，参考孔位用于观察和定位。

![手动缝合与工具提示](docs/images/manual-tools.png)

### 自动六针缝合

自动模式先预览六针计划，点击“开始六针缝合”后执行：

1. 选择三个相隔约 120° 的定位针位。
2. 每针依次穿过近端、远端血管壁，退出针体后拉线。
3. 在三个定位针之间各增加一针，完成三针补充缝合。
4. 在组织变形中更新材料锚点，规划针具转移路线，检查穿刺、离壁与拉线状态。

**针位预览 → 移动到进针位置 → 对准针具 → 穿透血管壁 → 确认针体退出 → 转移到另一侧 → 拉线收拢 → 下一针**

![自动六针流程结束画面](docs/images/automatic-complete.png)

**演示视频：待补充。**

<!-- 自动缝合视频上传后，将视频链接放在此处。 -->

## 快速开始

### 环境与启动

- Unity **2022.3.62f3c1**，桌面键鼠操作。
- Unity Package Manager 根据 [manifest.json](Packages/manifest.json) 安装依赖，包括 Collections、Mathematics、uGUI 和 Test Framework。

1. 克隆仓库，在 Unity Hub 中将仓库根目录添加为项目。
2. 打开项目，等待资源导入和脚本编译。
3. 打开主场景：[`Assets/TestScenes/Vessel_PBDTest/SingLayer.unity`](Assets/TestScenes/Vessel_PBDTest/SingLayer.unity)。
4. 点击 Play，在主页选择手动或自动模式。

主页等待选择时保持仿真暂停。自动模式可先预览针位，再点击开始。返回主页或重置本轮会重新加载场景，恢复初始针具、组织与孔道状态。

### 键鼠操作

| 操作 | 按钮或输入 |
| --- | --- |
| 移动 | **1**；左键拖动，在视平面移动；**Q / E** 调整远近 |
| 推进 / 回退 | **2**；沿针尖在屏幕上的朝向拖动推进，反向拖动回退 |
| 转向 | **3**；左键拖动调整针尖朝向 |
| 滚转 | **4**；左键左右拖动，绕针轴旋转 |
| 选中与锁定 | 手动模式默认锁定；同一按钮可取消选中或重新锁定 |
| 聚焦针具 | “聚焦针具”按钮 |
| 操针速度 | 滑条 **0.2×–3.0×**，默认 **1.0×** |
| 精细操作 | 按住 **Shift**，或使用精细模式开关 |
| 相机 | 右键旋转、中键平移、滚轮缩放 |
| 观察范围 | **F** 聚焦参考针位，**G** 查看整体，**R** 恢复跟随 |
| 暂停 / 继续 | **空格**或界面按钮 |
| 帮助与菜单 | “操作帮助”按钮；**Esc** 打开或关闭暂停菜单 |
| 拉线 | 完成两次穿刺后点击“拉线” |
| 截图 | **F12**；仿真视图保存至工程根目录 `PatentScreenshots/` |

## 仿真方法

### 1. 粒子状态与位置预测

将网格顶点表示为粒子，保存位置 $\mathbf{x}_i$、速度 $\mathbf{v}_i$、质量 $m_i$ 和逆质量 $w_i$。质量为零的粒子在自由预测中保持位置。其余粒子的预测包含重力、沿表面法线的外场分量及阻尼：

$$
\mathbf{f}_i^\perp=(\mathbf{f}\cdot\mathbf{n}_i)\mathbf{n}_i,
$$

$$
\mathbf{v}_i^*=\left(\mathbf{v}_i^n+\Delta t\,\mathbf{g}+\frac{\Delta t}{m_i}\mathbf{f}_i^\perp\right)\max\left(0,1-\frac{d\,\Delta t}{m_i}\right),
\qquad \widetilde{\mathbf{x}}_i=\mathbf{x}_i^n+\Delta t\,\mathbf{v}_i^*.
$$

其中 $d$ 为阻尼参数，$\mathbf{n}_i$ 为表面法线。迭代求解约束后，根据最终位置更新速度：

$$
\mathbf{v}_i^{n+1}=\frac{\mathbf{x}_i^{n+1}-\mathbf{x}_i^n}{\Delta t}.
$$

对应实现：[ClothJobs.cs](Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs) 中的 `PositionEstimateJob`，以及 [ClothSimulator.cs](Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs) 的仿真步调度。

### 2. XPBD 约束更新

对约束 $C(\mathbf{x})=0$，使用柔度 $c$ 和拉格朗日乘子 $\lambda$ 计算修正：

$$
\widetilde{\alpha}=\frac{c}{\Delta t^2},\qquad
\Delta\lambda=\frac{-C(\mathbf{x})-\widetilde{\alpha}\lambda}{\sum_i w_i\|\nabla_{\mathbf{x}_i}C\|^2+\widetilde{\alpha}},
$$

$$
\Delta\mathbf{x}_i=w_i\nabla_{\mathbf{x}_i}C\,\Delta\lambda,\qquad
\lambda\leftarrow\lambda+\Delta\lambda.
$$

柔度控制约束的软硬程度。距离、弯曲和跨层连接分别保存对应乘子，局部刚度系数进一步调整有效柔度。

| 约束 | 几何量与作用 |
| --- | --- |
| 面内距离 | $C_d=\lVert\mathbf{x}_i-\mathbf{x}_j\rVert-\ell_{ij}^0$，维持网格边长 |
| 弯曲 | $C_b=\theta-\theta_0$，控制相邻三角面的二面角 |
| 跨层厚度 | $C_h=\lVert\mathbf{x}_o-\mathbf{x}_i\rVert-h_0$，维持对应内外壁粒子的距离 |
| 层间分离与防翻转 | 限制层间穿越，对局部有向体积施加约束 |
| 固定与接触 | 处理端部固定、针具接触及缝合区域局部接触 |

厚度约束中，令 $\mathbf{n}=(\mathbf{x}_o-\mathbf{x}_i)/\lVert\mathbf{x}_o-\mathbf{x}_i\rVert$，两端的修正为：

$$
\Delta\mathbf{x}_o=w_o\Delta\lambda\,\mathbf{n},\qquad
\Delta\mathbf{x}_i=-w_i\Delta\lambda\,\mathbf{n}.
$$

对应实现：`XPBDDistanceConstraintJob`、`XPBDBendConstraintJob`、`XPBDCrossLayerDistanceJob`，见 [ClothJobs.cs](Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs)。

### 3. 材料锚点与实时针位

参考针位和自动计划通过血管边界的材料身份绑定到组织。边上的锚点保存顶点身份 $a,b$ 与插值参数 $s$：

$$
\mathbf{p}(s,t)=(1-s)\mathbf{x}_a(t)+s\mathbf{x}_b(t),\qquad 0\le s\le1.
$$

组织变形后，通过当前粒子位置重新解析锚点，结合进针边距更新计划点。孔位圈进一步投影到当前渲染网格，贴合内壁或外壁表面。针具转移与穿刺阶段使用更新后的几何信息。

对应实现：`TryCaptureSutureMaterialAnchor`、`TryResolveSutureMaterialAnchor` 和 [`SutureSurfaceGuidance`](Assets/PBDLearn/Runtime/Presentation/Mesh/SutureSurfaceGuidance.cs)。

### 4. 缝线牵拉与局部接触

缝线以两侧孔道约束点 $\mathbf{q}_A,\mathbf{q}_B$ 为端点。拉线推进阶段，当前静长按有效收拢速度 $v_{\mathrm{pull}}$ 递减：

$$
L^{n+1}=\max\left(L_{\mathrm{target}},L^n-v_{\mathrm{pull}}\Delta t\right).
$$

接触状态选择当前有效收拢速度，接触锁存时还结合局部接触距离调整静长。对拉紧状态，定义：

$$
C_s=\lVert\mathbf{q}_B-\mathbf{q}_A\rVert-L,\qquad
\mathbf{n}_s=\frac{\mathbf{q}_B-\mathbf{q}_A}{\lVert\mathbf{q}_B-\mathbf{q}_A\rVert}.
$$

当 $C_s>0$ 时，按约束点的有效逆质量 $\omega_A,\omega_B$ 分配收拢修正：

$$
\Delta\mathbf{q}_A=\omega_A\frac{C_s}{\omega_A+\omega_B}\mathbf{n}_s,\qquad
\Delta\mathbf{q}_B=-\omega_B\frac{C_s}{\omega_A+\omega_B}\mathbf{n}_s.
$$

修正通过孔道和局部孔位区域分配到组织粒子，与局部接触约束共同作用。工作流结合穿刺历史、针体离壁、缝线收拢和接触状态决定下一阶段。

对应实现：[ClothSimulator.cs](Assets/PBDLearn/Scripts/Cloth/ClothSimulator.cs) 中的 `UpdateSutureLineConstraintProgressMainThread`，以及 [ClothJobs.cs](Assets/PBDLearn/Scripts/Cloth/ClothJobs.cs) 的缝线求解。

## 工程结构

| 路径 | 职责 |
| --- | --- |
| `Assets/PBDLearn/Scripts/Cloth/` | 求解器、约束任务、双层驱动、切口与穿刺数据 |
| `Assets/PBDLearn/Scripts/AutoSuture/` | 六针计划、针具转移、穿刺与拉线工作流 |
| `Assets/PBDLearn/Scripts/Colliders/` | 针具控制、碰撞代理和针体几何 |
| `Assets/PBDLearn/Runtime/Procedures/VascularSuturing/` | 手动事件推进、会话模式及键鼠输入 |
| `Assets/PBDLearn/Runtime/Presentation/` | 主页、操作面板、孔位和工具提示 |
| `Assets/PBDLearn/Content/Vascular/` | 血管资源、预制体和材质 |
| `Assets/TestScenes/Vessel_PBDTest/` | 主演示场景 |
| `Tools/Tests/` | 几何、路线与自动缝合回归检查 |
| `docs/` | 操作说明、架构与验证记录 |

粒子状态使用 `NativeList` / `NativeArray` 存储，内外壁由同一求解器管理，通过顶点身份及跨层连接建立对应关系。Unity Job System 调度位置预测、约束求解和网格相关计算。

## 开发与验证

Unity 内通过 **PBDLearn → Session → Run guidance checks** 运行手动流程与交互规则检查，覆盖完整穿刺、重复事件、任意孔位事件推进、拉线条件、推进方向和表面标记跟随。

自动缝合几何与路线检查位于 [`Tools/Tests/AutoSutureWorkflowAdvanceRegression`](Tools/Tests/AutoSutureWorkflowAdvanceRegression)。迭代验证范围见 [验证记录](docs/validation/suture-session-2026-10-30.md)，详细操作见 [双模式使用说明](docs/SUTURE_SESSION.md)。

## 后续迭代

- 完善运行时手术刀绘制切口、网格更新和约束重建的连续操作。
- 调整操针灵敏度、工具提示与孔位观察体验。
- 丰富缝合过程记录和局部组织形变的观察方式。
