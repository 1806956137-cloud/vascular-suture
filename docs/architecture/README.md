# 血管缝合工程架构基线

本目录记录当前工程从原型结构向可维护结构迁移时必须遵守的边界。它是迁移目标，不表示这些程序集和目录已经存在。

当前阶段只建立规范，不改变 Unity 场景、Prefab、脚本 GUID、命名空间、程序集或仿真行为。现有代码继续编入 Unity 默认程序集；只有依赖方向经过验证后，才允许另立 checkpoint 引入 `asmdef`。

## 文档入口

- [CODING_STANDARDS.md](./CODING_STANDARDS.md)：新增代码和增量修改的编码、Unity、测试与提交规则。
- [MODULE_BOUNDARIES.md](./MODULE_BOUNDARIES.md)：目标模块、依赖方向，以及外翻、内膜身份和接触策略的所有权。
- [MIGRATION_MAP.md](./MIGRATION_MAP.md)：当前脚本到目标模块的事实映射和建议提取顺序。
- [ASSET_MANAGEMENT.md](./ASSET_MANAGEMENT.md)：模型、材质、Prefab、动态 Mesh 和二进制资产的所有权与迁移规则。

## 本阶段不做的事项

- 不批量格式化旧代码。
- 不移动或重命名 Unity 资源。
- 不统一旧命名空间。
- 不创建业务 `asmdef`。
- 不改绿地切口、接触、穿刺、拉线或自动缝合算法。
- 不复制参考项目的 XR、Haply、串口或包配置。

任何后续结构修改都必须遵守根目录 [AGENTS.md](../../AGENTS.md) 的分层验收契约和 Git checkpoint 工作流。
