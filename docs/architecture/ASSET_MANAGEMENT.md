# 资产管理规范

## 1. 目标

资产目录表达业务所有权和生命周期，不能继续把运行时模型、材质和测试场景混放。FBX 是只读源资产；细分、焊点、切口重建和 PBD 形变只作用于运行时 Mesh 副本。

当前第一批目录为：

```text
Assets/PBDLearn/Content/Vascular/
├─ Models/
├─ Materials/
├─ Prefabs/        # 后续 checkpoint 建立
├─ Textures/       # 有真实纹理时建立
└─ ImportPresets/  # Importer 契约验证后建立
```

`TestScenes` 只保存场景、场景专用测试脚本和测试数据，不拥有正式模型或跨场景材质。

## 2. 当前血管资产登记

| 资产 | GUID | 当前契约 | 状态 |
|---|---|---|---|
| `Models/外壁.fbx` | `dc11a520d2428494388b26c22435250f` | 双层血管 Outer 源 Mesh；Read/Write 开启 | `SingLayer` 正在使用 |
| `Models/内壁.fbx` | `13ddb7cbbe5e592469b102ff4a5b3980` | 双层血管 Inner 源 Mesh；Read/Write 开启 | `SingLayer` 正在使用 |
| `Models/EndCap.fbx` | `19f829d36c665f54baa2a35653d0b2c9` | 端盖候选源 Mesh；Read/Write 关闭 | 未发现跟踪资源引用，确认前不删除 |
| `Materials/外层.mat` | `742432309c537418c9df00fad2c73614` | Outer 展示材质 | `SingLayer` 与 Cloth 测试场景正在使用 |
| `Materials/内层.mat` | `4d3d99ad8984b11478165da07feeae04` | Inner 展示材质；当前也作为 middle-wall override | `SingLayer` 正在使用 |
| `Materials/中层.mat` | `58c02e80cffa545438780f65bc3ee24b` | 中层候选材质 | 未发现跟踪资源引用，确认前不删除 |

本阶段保留现有中文文件名，避免把路径迁移、显示命名和 FBX 重导入风险混在一个 checkpoint。命名标准化必须单独验证 Model Prefab 的子资源 fileID 和 Scene Override。

## 3. FBX 导入契约

外壁和内壁由 CPU 读取顶点、法线和三角索引，必须保持：

- `Read/Write Enabled = true`；
- `Global Scale = 1`；
- `Mesh Compression = Off`；
- `Generate Colliders = false`；
- 当前 `Weld Vertices = true` 在建立拓扑回归前不改；
- 法线、切线和材料导入策略在同一批双层模型中保持一致。

静态血管模型最终应关闭不需要的 Animation、Camera、Light 和 BlendShape 导入，但这会改变 Importer 配置，必须在独立 checkpoint 中验证顶点数、三角数、Outer/Inner 对齐和绿地切口七层日志后再实施。

## 4. 运行时 Mesh 生命周期

`ClothSimulationDriverDoubleLayer` 必须继续以 `cloneMeshAtRuntime = true` 运行：

```text
FBX Mesh（只读源）
  -> Instantiate 工作副本
  -> 可选细分/焊点
  -> 切口拓扑重建
  -> PBD 逐帧形变
  -> Renderer 写回
```

运行时生成的切口 sidewall、marker 和 middle-wall Mesh 只存在于内存，不保存回 FBX，也不作为新的 `.asset` 自动提交。

编辑态预览不得调用 Apply 路径改写导入 Mesh。需要保存派生网格时，必须使用明确的 Editor 导出工具、独立文件名和来源记录。

## 5. Prefab 边界

正式 Scene 最终应引用普通 Unity Prefab，而不是直接在 FBX Model Prefab 实例上堆业务组件：

```text
DoubleLayerVessel.prefab
├─ OuterWall  -> 外壁.fbx Mesh + 外层.mat
├─ InnerWall  -> 内壁.fbx Mesh + 内层.mat
└─ Simulation -> Driver / Renderer / Config bindings
```

包装 Prefab 的建立和 `SingLayer` 替换属于 Scene 行为迁移，必须单独 checkpoint；在用户现有 Scene 修改未确认前不执行。

## 6. DCC 源文件和大二进制

- Blender、原始 OBJ、工程 ZIP 和未优化导出文件不放入 Unity `Assets`。
- DCC 源文件放在仓库外，或放入独立 `ArtSource` 并使用 Git LFS。
- `Assets` 只保存经过确认、会被 Unity 使用的 FBX、材质和纹理。
- 当前三个 FBX 文件很小，继续使用普通 Git 即可；新增较大二进制前先定义 LFS 阈值和团队安装要求。
- FBX、图片和音频保持 binary 属性，禁止文本行尾归一化。

## 7. 移动、替换和删除纪律

- 所有 Unity 资产必须连同 `.meta` 移动，GUID 不得变化。
- 更新 FBX 内容时保留原 `.meta`，并核对子资源 fileID、顶点/三角计数和 Scene Override。
- 不在同一 checkpoint 中同时移动、重命名、改 Importer 和替换 Scene 引用。
- 未引用扫描只能证明“跟踪的 YAML/代码未引用”，不能直接授权删除资产。
- 每个资源阶段必须记录 Scene 文件哈希、GUID 引用、Importer 关键字段、编译结果和未执行的 Unity 手工验证。
# Hybrid vessel asset package

The hybrid branched-vessel model is a reusable runtime-content package rather
than a test-scene-owned asset. Its managed layout is:

```text
Assets/PBDLearn/Content/Vascular/
|-- Models/       # Read-only imported FBX sources
|-- Generated/    # Deterministically rebuilt Unity Mesh assets
|-- Materials/    # Shared vessel, tissue, and support materials
|-- Textures/     # Tissue albedo inputs
`-- Prefabs/      # Scene-ready visual/model assemblies
```

`HybridVesselAssetBuilder` must preserve existing asset GUIDs when rebuilding.
`SimulationOuter` and `SimulationInner` are the only PBD inputs. Transition,
branch, tissue, and support children are presentation-only and must never be
added to the PBD particle/constraint set.
