# 地形纹理（Terrain Textures，Catlike Part 14）

> 状态：✅ 已落地（2026-09-09）。把纯色地面升级为 splat 顶点色 + Texture2DArray 的 5 类地形纹理。

## 目标
- 用顶点色做 splat 图（红/绿/蓝 = 每三角形最多三种地形的混合权重）
- 纹理数组（Texture2DArray）存 5 张地形纹理：0 沙 / 1 草 / 2 泥 / 3 石 / 4 雪
- 每三角形通过第 3 套 UV（uv2，Vector3 = 三种纹理索引）选纹理，着色器内三路采样混合

## 当前状态
- [x] `HexMesh`：新增 `useTerrainTypes` / `terrainTypes` 列表 / `AddTriangleTerrainTypes` / `AddQuadTerrainTypes`，`Apply()` 写 `SetUVs(2, …)`
- [x] `HexGridChunk`：全部地形三角化由 `cell.color` 纯色改为 splat 常量色 `color1/2/3` + 逐三角形类型；河流旁/梯田/悬崖角落各分支均已覆盖
- [x] 新 shader `Resources/Shaders/TerrainTextured.shader`（surface Standard + `vertex:vert` + `UNITY_SAMPLE_TEX2DARRAY`；uv = worldPos.xz × 0.03；`_TerrainCount` clamp 防类型越界）
- [x] 新 Editor 工具 `Editor/TextureArrayWizard.cs`：菜单 `Assets/Create/Texture Array` + 批处理入口 `TextureArrayWizard.BuildDefaultTerrainArray`
- [x] 官方 5 张纹理 → `Resources/Textures/Terrain/{sand,grass,mud,stone,snow}.png`（512×512）
- [x] 纹理数组资产 `Resources/Textures/TerrainTextures.asset`（5 层，GUID 5f67b773…）
- [x] `Resources/Materials/cellMat.mat` → `Custom/TerrainTextured` + `_MainTex` = TerrainTextures
- [x] `Resources/Prefabs/HexGridChunk.prefab` 的 Terrain HexMesh 打开 `useTerrainTypes`
- [x] 清理：删除 `HexCell.Color`、`HexMetrics.colors`、`HexGrid.colors`（颜色本就是 terrainTypeIndex 派生的，删后存档格式不变）

## 设计要点
- **terrainTypeIndex 语义定稿**：0=沙 1=草 2=泥 3=石 4=雪（原为“色号”，无固定含义）。存档 `.map` 首字节即类型索引，**格式不变**，旧图直接按新语义着色
- splat 约定：格子中心/河流区=纯红(color1)；边=红↔绿渐变（本格 type↔邻居 type）；角落=红绿蓝三色（bottom/left/right 格）
- 类型索引存 uv2（`Vector3`），uv0 保留给河流/水岸等
- shader 对超出 `_TerrainCount` 的索引 clamp 到末层（防旧地图残留类型变黑）
- 顶点色通道仍是 Color32 精度，混合权重为 8bit —— 与教程一致，够用

## 接口依赖
- 改前依赖：`HexCell.TerrainTypeIndex`（已有）→ 无新增数据字段；`HexMesh` 新增 1 个 bool + 1 个列表
- `HexGroundAudioBridge`：类型已有语义后，如需按材质出脚步音，在场景 Inspector 的 `overrides` 按 `index → GroundKind` 填写（如 3 → Stone）；默认仍归 Grass，行为不变
- 联机/存档：不受影响（chunk 三角化是纯表现层，客户端各自生成）

## 验证方式
- [ ] 编辑器编译零错误（已由 HexMap2 批处理验证）
- [ ] Play 目检：地面显示 5 类纹理；不同类型交界处有渐变过渡；河岸/梯田/悬崖正常
- [ ] 载入 `test.map`（多类型）与 `simple.map`（≈全 0）对比；用地形刷子改类型即时生效
- [ ] 旧图加载：类型超出 4 的格子应显示为雪（clamp），不黑屏

## 变更记录
- 2026-09-09：落地。改动文件：
  - `Scripts/HexMesh.cs`、`Scripts/HexGridChunk.cs`（主改造）
  - `Scripts/HexCell.cs`、`Scripts/HexMetrics.cs`、`Scripts/HexGrid.cs`（清理 colors）
  - `Scripts/Audio/HexGroundAudioBridge.cs`（注释更新，无行为变化）
  - 新增 `Resources/Shaders/TerrainTextured.shader`、`Editor/TextureArrayWizard.cs`、`Editor/TerrainTexBatch.cs`（一次性批处理装配工具，可留作重建）
  - 资源：`Resources/Textures/Terrain/*.png` ×5、`TerrainTextures.asset`、`cellMat.mat`、`HexGridChunk.prefab`
- 已知可调项：shader 中 uv 缩放 0.03（纹理平铺密度）、`_TerrainCount` 默认 5
