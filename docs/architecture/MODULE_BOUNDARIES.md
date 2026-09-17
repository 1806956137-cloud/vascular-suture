# 目标模块边界

## 1. 当前事实

截至 2026-07-21：

- 项目使用 Unity `2022.3.62f3c1`。
- `Assets/PBDLearn/Scripts` 下有 34 个运行时 C# 文件，均由默认 `Assembly-CSharp` 编译。
- 项目代码没有自有 `asmdef`；PackageCache 内的程序集定义不属于本项目架构。
- 运行时代码同时存在 `PBDLearn`、`Utils` 和全局命名空间。
- `DoubleLayerIncisionGenerator`、`ClothSimulationDriverDoubleLayer`、`ClothSimulator`、`ClothJobs` 和 `AutoSutureRuntimeController` 承载了主要复杂度。

因此下述模块首先是职责边界，不是立即创建的目录或程序集。

## 2. 目标依赖方向

```text
UnityDrivers / Presentation
             |
             v
Procedures.VascularSuturing
             |
             v
Simulation.Incision / Contact / SoftBody / Geometry
             |
             v
Core contracts and value types
```

外部设备或 Unity 物理代理属于 Adapter：它们实现 Core/Simulation 定义的接口，可被上层组合根装配，但 Core 和 Simulation 不反向引用具体 SDK、场景或 UI。

## 3. 模块职责

### Core

拥有稳定、无场景依赖的值类型和接口：材料点/层/表面身份、拓扑句柄、位姿样本、时间和单位、操作结果、只读诊断记录。目标是普通 C#；若必须使用数学类型，依赖限制在 `Unity.Mathematics`，不持有 `GameObject`、`Transform` 或 `MonoBehaviour`。

### Simulation.Geometry

拥有 AABB、三角形、相交测试、空间索引和网格局部数学。它不知道切口工作流、针次顺序或 UI。

### Simulation.SoftBody

拥有粒子状态、约束数据、XPBD/PBD 求解、层间连接、网格写回前的仿真结果。它消费身份和接触约束，不决定临床步骤。

### Simulation.Incision

拥有目标线采样、structured rows、影响域分区、cell boundary graph、接口共形、边界语义和合法 polygon 三角化。七层验收契约在这里形成结构化结果，Unity 驱动只负责提交输入与显示输出。

### Simulation.Contact

拥有通用的针/组织、组织/组织和缝线/组织候选生成、窄相、约束构造、CCD 与接触求解。它不硬编码“当前是第几针”或 UI 状态。

### Procedures.VascularSuturing

拥有血管缝合业务语义：针次计划、进入/穿出顺序、Carrel 分区、外翻目标、拉线时机、完成条件和流程状态。它通过契约向 Simulation 请求物理动作，并读取结构化结果，不直接修改 Native 容器或 Mesh。

### UnityDrivers

拥有场景组合根、Inspector 引用、Unity 生命周期、Mesh/Collider/Transform 适配以及普通 C# 服务的调用。驱动可以协调模块，但不能重新定义材料身份、接触合格条件或切口成功条件。

### Presentation

拥有 Gizmo、Editor、UI、日志格式化和可视反馈。展示层只消费快照，不成为运行时算法的输入真相源。

### Tests

拥有纯算法回归、契约测试和 Unity 场景烟雾测试。测试可跨模块组装，但生产模块不得依赖 Tests。

## 4. 外翻、内膜身份与接触策略

这三类能力跨层协作，但必须只有一个语义所有者。

| 能力 | 语义所有者 | 物理实现 | 禁止放置的位置 |
|---|---|---|---|
| 切口壁外翻 | `Procedures.VascularSuturing` 定义目标、适用阶段和完成条件 | `Simulation.SoftBody`/`Simulation.Contact` 实现约束、力或边界条件 | UI、Gizmo、场景坐标硬编码、自动缝合控制器内的临时顶点推移 |
| 内膜/外膜身份 | `Core` 定义稳定的层与表面身份；拓扑生成阶段赋值并由 Simulation 全程保留 | `Simulation.Incision` 在重建时传播，`Simulation.Contact` 在筛选时消费 | 依据瞬时法线、世界方向、最近点或渲染材质在下游重新猜测 |
| 接触策略 | `Procedures.VascularSuturing` 定义哪些身份在何阶段允许/需要接触 | `Simulation.Contact` 负责候选、窄相、约束和数值求解 | `Presentation`、输入适配器，或 `ClothJobs` 内无法追踪来源的流程分支 |

推荐的边界数据流：

```text
Procedure: Eversion/Contact policy
        -> immutable request with material identities
Simulation: validate identities, build and solve constraints
        -> structured contact/eversion result
Procedure: advance or fail the workflow
Presentation: render the same result snapshot
```

如果内膜身份、外翻目标或允许接触组合存在多种合理解释，必须先由用户确认业务语义，不能由 Simulation 或 Driver 自行选择。

## 5. 引入程序集前置条件

只有同时满足以下条件，才允许在独立 checkpoint 中增加 `asmdef`：

1. 待拆模块的公开输入/输出契约已经存在并有测试。
2. 已扫描并消除目标模块对 Driver、Presentation 和具体 Procedure 的反向引用。
3. Editor 类型与 Runtime 类型能够分离。
4. Scene、Prefab、脚本 GUID 和序列化字段迁移方案明确。
5. 默认程序集与新程序集之间没有循环依赖。
6. Unity 编译和当前基线场景均可验证。

第一批更适合验证的边界是纯 Geometry 或纯生命周期策略；切口生成器、双层驱动和求解器不作为第一批整体搬迁对象。
