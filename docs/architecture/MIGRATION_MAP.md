# 现有脚本到目标模块的迁移映射

本表是架构盘点，不授权立即移动文件。迁移顺序以行为不变、契约先行和小 checkpoint 为原则；“目标模块”表示最终职责归属。

## 当前迁移状态

- 已建立 `Assets/PBDLearn/Runtime/Procedures/VascularSuturing` 作为第一个真实运行时模块目录。
- `AutoSutureLifecyclePolicy.cs` 已连同原 `.meta` 迁入该目录；`PBDLearn` 命名空间、公开 API 和调用方保持不变。
- 已建立 `Assets/PBDLearn/Runtime/Simulation/Geometry`，并将 AABB 树、轴、适配接口和 TriangleObject 基础类型连同原 `.meta` 迁入；源码和全局命名空间保持不变。
- 已建立 `Assets/PBDLearn/Runtime/Presentation/Mesh`，并将单层/双层 Mesh 写回组件连同原 `.meta` 迁入；Scene/Prefab 组件 GUID 保持不变。
- 已建立 `Assets/PBDLearn/Runtime/Presentation/Diagnostics`，并将 `ClothDebugGizmos.cs` 连同原 `.meta` 迁入；类型、序列化字段、场景组件 GUID 和现有只读/同步行为保持不变，职责拆分尚未开始。
- 已建立 `Assets/PBDLearn/Editor/Presentation/Diagnostics`，并将 `ClothDebugGizmosEditor.cs` 连同原 `.meta` 迁入；自定义 Inspector、Scene 拾取工具和脚本 GUID 保持不变，Driver 适配边界仍待后续提取。
- `ColliderStructs.cs` 暂不迁入 Contact：该文件当前混合 Job 数据、`ICollider` 适配接口和 Unity `Rigidbody` 引用，必须先确定拆分契约，不能用目录移动掩盖反向依赖。
- 现有 `AutoSutureLifecycleTests.cs` 暂留 `Assets/PBDLearn/Editor`。它还覆盖 RuntimeController、Simulator 和 Gizmo；在测试 `asmdef` 建立前不改变其 Editor 编译归属。
- 其余表项仍是目标映射，不表示已经完成物理迁移。

## 1. 核心仿真与切口

| 当前文件 | 当前主要职责 | 目标模块 | 首个安全提取点 |
|---|---|---|---|
| `Cloth/Incision/DoubleLayerIncisionGenerator.cs` | 双层绿地切口几何、拓扑身份、分区、诊断和验收 | `Simulation.Incision` | 先提取只读的阶段结果/诊断 DTO；不先拆算法流程 |
| `Cloth/ClothSimulationDriverDoubleLayer.cs` | 双层场景装配、切口提交、网格、缝合序列、调试数据 | `UnityDrivers`，业务部分到 `Procedures.VascularSuturing` | 先提取不可变请求/结果接口，保留原 MonoBehaviour 作为 facade |
| `Cloth/ClothSimulator.cs` | 粒子/约束求解、针接触、portal、拉线与接触状态 | `Simulation.SoftBody` + `Simulation.Contact` | 先分离接触/portal 结果快照和策略输入，不移动 Native 状态 |
| `Cloth/ClothJobs.cs` | 约束数据、接触数据及多类 Burst Job | `Simulation.SoftBody` + `Simulation.Contact` | 按数据契约和 Job 家族拆文件；保持调度顺序与 Native 所有权 |
| `Cloth/ClothSetting.cs` | 多类仿真、接触、穿刺、拉线参数 | `Simulation.Configuration`，流程参数到 Procedure Config | 先生成参数所有权清单，再拆 ScriptableObject/序列化配置 |
| `Cloth/ClothMeshModifier.cs` | 网格撕裂/拓扑修改 | `Simulation.SoftBody.Topology` | 为输入/结果增加身份契约和回归测试 |

## 2. 自动缝合与器械控制

| 当前文件 | 当前主要职责 | 目标模块 | 首个安全提取点 |
|---|---|---|---|
| `Runtime/Procedures/VascularSuturing/AutoSutureLifecyclePolicy.cs` | 自动缝合生命周期判定；已完成首批物理归位 | `Procedures.VascularSuturing` | 保持现有 API；下一步将反射型测试逐步改为直接契约测试 |
| `AutoSuture/AutoSutureRuntimeController.cs` | 计划、流程状态、场景查找、鼠标拖拽、UI 与日志 | Procedure + `UnityDrivers` + `Presentation` | 先提取计划/状态转移；鼠标拖拽留在 Desktop Debug Adapter |
| `Colliders/NeedleSceneControlRig.cs` | 针场景控制、自动请求、控制面板数据 | `UnityDrivers`，请求模型到 Procedure | 先提取针动作请求 DTO 和执行接口 |
| `Colliders/NeedleManualDriver.cs` | 手动输入并驱动针 | `UnityDrivers.Input` | 保留为 Desktop Debug Input Adapter，不让 KeyCode 进入 Procedure |

## 3. 几何、碰撞与 Unity 代理

| 当前文件 | 目标模块 | 迁移备注 |
|---|---|---|
| `Runtime/Simulation/Geometry/AABB.cs` | `Simulation.Geometry` | 已完成物理归位；当前无命名空间，后续需先补测试再迁移命名空间 |
| `Runtime/Simulation/Geometry/AABBNode.cs` | `Simulation.Geometry` | 已与树和 adaptor 一起完成物理归位 |
| `Runtime/Simulation/Geometry/AABBTree.cs` | `Simulation.Geometry` | 已完成物理归位；不依赖流程语义 |
| `Runtime/Simulation/Geometry/AABBTreeUtil.cs` | `Simulation.Geometry` | 已完成物理归位；当前无命名空间 |
| `Runtime/Simulation/Geometry/Axis.cs` | `Simulation.Geometry` | 依赖扫描确认其当前只服务 AABB 树拆分轴 |
| `Runtime/Simulation/Geometry/IAABBNodeAdaptor.cs` | `Simulation.Geometry` | 已完成物理归位；保持空间索引边界接口不变 |
| `Colliders/ParticleAABBNodeAdaptor.cs` | `Simulation.SoftBody` 对 Geometry 的适配 | 不能让 Geometry 反向依赖 ClothSimulator |
| `Runtime/Simulation/Geometry/TriangleObject.cs` | `Simulation.Geometry` | 已完成物理归位；当前无命名空间 |
| `Runtime/Simulation/Geometry/TriangleObjectAABBNodeAdaptor.cs` | `Simulation.Geometry` | 已与 TriangleObject 同批迁移 |
| `Colliders/ColliderAABBUtil.cs` | `Simulation.Geometry` | 通用碰撞体包围盒计算 |
| `Colliders/ColliderStructs.cs` | `Simulation.Contact` contracts | 将 Unity 代理描述与求解数据分开 |
| `Colliders/UnityColliderProxy.cs` | `UnityDrivers.Adapters` | 实现 Simulation 接触描述接口 |
| `Colliders/UnityBoxColliderProxy.cs` | `UnityDrivers.Adapters` | 保留 MonoBehaviour 和序列化引用 |
| `Colliders/UnityCapsuleColliderProxy.cs` | `UnityDrivers.Adapters` | 针特有语义最终不应写入通用基类 |
| `Colliders/UnitySphereColliderProxy.cs` | `UnityDrivers.Adapters` | 保留 MonoBehaviour 和序列化引用 |

## 4. 渲染、调试与网格生成

| 当前文件 | 目标模块 | 迁移备注 |
|---|---|---|
| `Runtime/Presentation/Diagnostics/ClothDebugGizmos.cs` | `Presentation.Diagnostics` | 已完成物理归位；后续只读取结构化快照，不得生成算法成功条件 |
| `Runtime/Presentation/Mesh/ClothRenderer.cs` | `Presentation.Mesh` | 已完成物理归位；单层 Mesh 写回 API 与组件 GUID 不变 |
| `Runtime/Presentation/Mesh/ClothRendererSlice.cs` | `Presentation.Mesh` | 已完成物理归位；双层 slice Mesh 写回 API 与组件 GUID 不变 |
| `Cloth/ClothSimulationDriver.cs` | `UnityDrivers` | 单层组合根，后续复用统一仿真服务接口 |
| `Vessel/ProceduralVesselMesh.cs` | `Simulation.Geometry.Authoring` + Unity Adapter | 纯生成算法与 MeshFilter 写回应分开 |
| `Vessel/VesselMeshSubdivider.cs` | `Simulation.Geometry.Topology` + Unity Adapter | 先为细分结果增加纯算法测试 |

## 5. 工具类

| 当前文件 | 目标模块 | 迁移备注 |
|---|---|---|
| `Utils/IntersectUtil.cs` | `Simulation.Geometry` | 已在 `PBDLearn`；按算法测试迁移，不保留笼统 Utils 所有权 |
| `Utils/Vector3Helper.cs` | `Simulation.Geometry` 或 `Core.Math` | 依据是否依赖 UnityEngine.Vector3 决定最终位置 |
| `Utils/Extension.cs` | 使用方所属模块 | 避免形成跨层通用扩展集合；扩展应靠近唯一使用者 |

## 6. Editor 与场景测试

| 当前文件 | 目标模块 | 迁移备注 |
|---|---|---|
| `Editor/Presentation/Diagnostics/ClothDebugGizmosEditor.cs` | `Presentation.Editor` | 已完成物理归位；未来只引用 Presentation runtime API |
| `Editor/ClothSimulationDriverDoubleLayerEditor.cs` | `UnityDrivers.Editor` | 保留自定义 Inspector，不承载运行时算法 |
| `Editor/NeedleSceneControlRigEditor.cs` | `UnityDrivers.Editor` | Editor 搜索行为不应复制到 Runtime |
| `Editor/AutoSutureLifecycleTests.cs` | `Tests.EditMode.Procedures` | 未来可成为 Procedure asmdef 的首批契约测试 |
| `TestScenes/ClothTest/ClothSimulateTest.cs` | `Tests.PlayMode.Simulation` | 当前无命名空间；与场景资源一同迁移 |
| `TestScenes/IntersectTest/IntersectionTestScene.cs` | `Tests.PlayMode.Geometry` | 当前无命名空间；算法断言优先提取到 EditMode |

## 7. 风险排序与建议阶段

1. **低风险：契约和测试。** 提取 `AutoSutureLifecyclePolicy` 相关纯状态测试，或为 Geometry 增加纯算法测试；不移动 Unity 资源。
2. **中低风险：展示快照。** 让 Gizmo/UI 消费只读结果，减少对 Driver 内部集合的直接访问。
3. **中风险：Procedure 请求/结果。** 从自动缝合控制器和针 Rig 提取计划、动作请求与状态转移。
4. **中高风险：接触策略边界。** 将 Procedure 的允许接触策略与 Simulation 的数值求解分开，保持当前运行结果。
5. **高风险：切口与求解器拆分。** 只有绿地切口分层验收和双层同步均稳定后，才拆 `DoubleLayerIncisionGenerator`、`ClothSimulator` 或 Job 调度。
6. **最后引入 `asmdef`。** 先从无反向依赖的纯模块开始；每个程序集单独 checkpoint 和 Unity 编译验证。

任何阶段如果需要改变真实切口形态、支撑域宽度、共形接口、Outer/Inner 同步、外翻定义或内膜接触语义，必须先暂停并让用户确认。
