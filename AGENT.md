# HexMap 项目说明（AGENTS 通用）

## 技术栈
- Unity 2022.3 + 自定义 SRP（CustomRP 只渲染 SRPDefaultUnlit pass）→ **新材质必须用 Unlit shader**

## 代码同步约定（重要）
- 开发环境对 .cs 加密；`Assets/CodeTxts/` 下同名 .txt 是未加密镜像，用于 git 提交
- 用菜单 `Tools/代码同步/` 导出/还原。改动 .cs 后必须导出刷新 CodeTxts

## 文档索引
- `docs/README.md` —— 路线图与索引
- `docs/map-generation.md` —— 地图生成现状
- `docs/charactercontrol.md` —— 角色控制（规划中）

## 开发习惯
- 验证方式：Unity 编辑器内 Play 验证
- 大改造先给方案，确认后再动手
- 功能性开发应该尽可能的与现有功能低耦合，保证复用性。必须耦合的情况下尽可能的使用代理模式或者观察者模式
