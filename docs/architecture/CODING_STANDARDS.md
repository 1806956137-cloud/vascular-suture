# 代码规范

## 1. 适用方式

本规范立即约束新增文件和实际修改到的代码。旧代码采用“触碰即改善”的增量策略，不因建立规范而整文件格式化。格式化范围应与行为修改范围一致，避免把算法差异淹没在机械改动中。

根目录 `.editorconfig` 固定 UTF-8、LF、4 空格缩进和 Allman 大括号；`.gitattributes` 固定项目文本的行尾，并为 Unity YAML 资源声明 UnityYAMLMerge。配置只提供编辑器约定，不把样式建议升级为编译错误。

## 2. 命名与文件

- 一个主要类型一个文件，文件名与主要类型名一致。与 Burst Job 紧密绑定的小型数据结构在拆分前可保留在同一文件。
- 类型、方法、属性和枚举使用 `PascalCase`；局部变量、参数和私有字段使用 `camelCase`。
- 接口使用 `I` 前缀；配置类型使用 `Config` 或现有 Unity 兼容的 `Setting` 后缀。
- 布尔值优先使用 `Is`、`Has`、`Can`、`Should`、`Enable` 等能表达判断的问题式名称。
- 有量纲的值在名称中写明单位，例如 `distanceMeters`、`durationSeconds`；网格局部尺度等无固定物理单位的值必须在注释或契约中说明空间。
- 禁止用 `Manager2`、`NewVersion`、`TempFix` 等名称承载正式业务语义。

## 3. 命名空间迁移

现有 `PBDLearn`、`Utils` 和全局命名空间保持不动，避免无关的 Unity 序列化和引用风险。新增类型应按 [MODULE_BOUNDARIES.md](./MODULE_BOUNDARIES.md) 选择目标命名空间，例如：

```text
PBDLearn.Core
PBDLearn.Simulation.Geometry
PBDLearn.Simulation.SoftBody
PBDLearn.Simulation.Incision
PBDLearn.Simulation.Contact
PBDLearn.Procedures.VascularSuturing
PBDLearn.Presentation
PBDLearn.UnityDrivers
```

旧类型只有在独立迁移 checkpoint 中才能改命名空间。迁移前必须扫描代码、Scene 和 Prefab 引用，并保留对应 `.meta` 文件。

## 4. Unity 与序列化

- `MonoBehaviour` 只负责 Unity 生命周期、Inspector 绑定、数据转换、服务调用和结果写回。
- 几何算法、拓扑身份、接触求解、状态机和验收规则优先放入可测试的普通 C# 类型。
- Inspector 引用优先使用 `[SerializeField] private`；不要为方便访问而新增可写 `public` 运行时字段。
- 必填引用在 `Awake`、`OnEnable` 或显式初始化入口校验，并给出一次可定位的错误。
- 不新增 `FindObjectOfType` 作为关键依赖的正常成功路径。调试/Editor 工具可搜索，但运行时依赖应由 Inspector、组合根或显式初始化参数提供。
- 不把直接鼠标或键盘读取扩散到流程逻辑。桌面操作保留为调试输入适配层。
- 改名序列化字段时使用 `FormerlySerializedAs`；移动脚本时连同 `.meta` 移动。
- 不直接编辑已导入 Mesh 资源来伪装运行时成功；运行时拓扑应作用于明确复制的实例。

## 5. 依赖与职责

- 上层可以依赖下层公开契约，下层不得引用具体流程控制器、UI 或场景对象。
- Unity 驱动不得成为几何、接触或业务规则的唯一真相源。
- Procedure 表达“要完成什么”和临床语义；Simulation 表达“物理上如何计算”；Presentation 只展示结果。
- 通用数据契约不能依赖具体血管场景、自动缝合步骤或设备 SDK。
- 当前尚无项目业务 `asmdef`。在文档边界变成真实程序集前，必须先消除反向依赖并建立最小契约测试。

## 6. 绿地切口与拓扑修改纪律

所有切口修改继续以根目录 `AGENTS.md` 的七层契约为准：Shape / Strip、ProjectionConsistency、CellDefinition、CellBoundaryGraph、InterfaceConformality、BoundarySemantics、TriangulationQuality。

- 3D sample 身份、邻接、顺序和 side/corner 语义是真相源；二维 `(s,u)` 只用于结构化中间表示。
- 失败必须归属于最早未证实层，禁止以 fallback 冒充成功路径。
- 修复发生在产生错误语义的上一层，不在 triangulation、boundary 或 quality 阶段补救拓扑身份错误。
- 诊断优先输出 `count + byCategory + preview` 的全局模式，再分析单个失败对象。
- Outer 先通过同层验收，再验证 Inner；不得用展示结果替代双层同步契约。

## 7. 日志与诊断

- 高频 `Update`、求解 Job 和接触循环内禁止无条件逐帧日志。
- 诊断日志必须带稳定类别和阶段，例如 `[Incision][CellBoundaryGraph]`，并能关联 layer、row/cell、materializedId 或 colliderEntityId。
- 成功摘要与失败证据分开；失败日志必须保留最早失败原因。
- 发布或性能测试前必须能关闭详细诊断，不通过放宽验收阈值降低日志量。
- 训练指标和流程完成状态应成为结构化数据，不从 `Debug.Log` 反向解析。

## 8. 性能与 Job

- 不在 `Update`、`FixedUpdate`、Burst Job 或高频接触循环中使用 LINQ、闭包或持续分配临时集合。
- Native 容器的创建、所有权和释放必须成对且可追踪；Job 调度前固定输入快照，完成后再写回 Unity 对象。
- 后台线程和 Job 不访问 Unity 主线程 API。
- 优化必须保留身份、接口和验收日志；性能理由不能作为放宽拓扑契约的依据。

## 9. 测试与完成定义

每个可编译的小阶段至少满足：

1. 修改目标和所属层明确，未混入无关重构。
2. `git diff --stat` 和实际文件列表已审查。
3. `dotnet build Assembly-CSharp.csproj -nologo` 或当时可用的等价 Unity 检查通过。
4. 与目标层有关的回归测试通过；无法自动验证的 Unity 场景行为明确列出。
5. 没有新增 fallback 成功路径、隐式全局依赖或逐帧日志。
6. 按文件显式暂存并建立小型 Git checkpoint；禁止 `git add .`。

场景和算法行为改变时，还必须在 Unity 中验证当前基线场景，并按 `AGENTS.md` 的验收顺序读取日志。
