# 地图生成（map-generation）

## 当前状态
- 已完成 Catlike HexMap 教程至"水域"部分；河口（Estuaries）未处理（暂缓）
- 代码镜像见 `Assets/CodeTxts/Scripts/*.txt`（.cs 加密环境的未加密副本，以 .cs 为准）
- 地图资源：`explore.map`（100×75，见 [地图资产](#地图资产)）

## 关键类
| 类 | 职责 |
|---|---|
| HexGrid | 网格容器（width/height），生成单元格与网格 |
| HexCell | 单元格数据：坐标、颜色、海拔、河流、水域标记等 |
| HexMesh | 网格构建：地形、河流、水域 Mesh |
| HexMetrics | 常量与坐标换算：内外半径、方位角、海拔比例等 |
| HexCoordinates | 立方体 / 偏移坐标转换 |
| HexDirection | 六方向枚举与扩展（邻格、边、角） |
| HexMapEditor | 编辑器交互：涂色、海拔、河流工具 |

## 扩展点（后续功能可能依赖）
- 角色移动：需经 HexGrid / HexCoordinates 取格、读 HexCell 海拔与通行状态
- 纹理美化：HexMesh UV / 材质相关，注意 Unlit 约束

## 验证方式
- Unity 编辑器 Play：地图生成正确、编辑工具可用

## 地图资产
- `explore.map`（100×75=7500 格，大陆式）：四周环海大陆、斜贯雪山山脉、6 个湖泊、
  12 条河流（6 条入海/湖、6 条陆地收尾）。存档路径 =
  `Application.persistentDataPath/explore.map`
  （本机：`C:\Users\xiongfucheng.JOY\AppData\LocalLow\DefaultCompany\HexMap\explore.map`）
- 加载方式：Play 模式 → Load 菜单选择 explore（或代码 `hexGrid.Load`，header=1）
- 生成器：`<项目根>/tools/gen_hexmap.py`（Python 直写 .map 二进制，格式与
  HexCell.Save 一致；`--seed` 换地形、`--out` 指定路径，需再配合编辑器内 Load 重建网格）
- 地形色语义（HexGrid.colors 5 色）：0 黄=沙/旱地、1 绿=草地、3 橙红=岩、
  4 白=雪（2 蓝未使用）；海拔 6+ 为雪线；水为独立 mesh（水位 1，湖/海按连通区分）

## 变更记录
- 2026-09-03：生成大陆式探索地图 explore.map（100×75，含雪山/湖泊/河流），
  验证加载与渲染正常；新增生成器 tools/gen_hexmap.py
