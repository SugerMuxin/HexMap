# HexMap 项目文档

Catlike HexMap 教程实现（Unity 2022.3，自定义 SRP）。

## 当前进度
- 地图生成：基础网格 / 坐标 / 颜色 / 编辑 → 海拔与阶梯 → 不规则化 → 河流 → 水域 ✅
- 地形纹理（Part 14）：splat 顶点色 + Texture2DArray（沙/草/泥/石/雪）✅
- 特征编辑（Part 9）：城市/农田/植被/**怪物巢穴**四级特征绘制 + 密集/稀疏密度切换 + 存读档还原（header v3）✅（见 features-editing.md）
- 河口（Estuaries）：未处理（Readme 注明留待以后）
- 角色控制：点击直线移动 ✅ + 第三人称跟随相机 ✅（无跳跃/无攻击，见 charactercontrol.md）
- 六边形寻路（FEAT-001）：距离 / A* + 移动成本 / 沿路径平滑移动 / 场景演示 + 一键装配 ✅（见 pathfinding.md）
- **AI 怪物系统（FEAT-002）**：怪物 prefab（Monsters 目录）+ 怪物洞穴出怪 + **数量/频率由场景配置表配置**（新增 SceneConfigTable）+ 找最近玩家 / **被更近的玩家攻击则换锁** / 沿寻路追击 + 玩家攻击桥接 ✅（见 monsters.md）
- PvZ 3D 玩法层：**种植系统 ✅（FEAT-003）+ 寻路系统 ✅（FEAT-001）+ 出怪与 AI 怪物 ✅（FEAT-002）**，战斗 / UI 待做（见 pvz3d.md、monsters.md、pathfinding.md、docs/fix/pvz3d/fix-plan.md）
- UI 系统：**M1~M5 全部落地 + 双端联调已通过**（断言 151 条全过；HexMap 房主 + HexMap2 客户端实测连通、位置/玩家名同步，见 ui.md）

## 文档索引
| 文档 | 内容 |
|---|---|
| [map-generation.md](map-generation.md) | 地图生成现状、关键类、扩展点 |
| [charactercontrol.md](charactercontrol.md) | 角色移动 / 跳跃 / 攻击 / 游览（规划中） |
| [pathfinding.md](pathfinding.md) | 六边形寻路（FEAT-001）：距离 / A* / 移动成本 / 沿路径移动 / 一键装配 |
| [monsters.md](monsters.md) | AI 怪物系统（FEAT-002）：怪物洞穴出怪 / 场景配置表（数量·频率·波次）/ 锁敌与换锁 / 追击 / 玩家攻击桥接 |
| [multiplayer.md](multiplayer.md) | 局域网多人（Mirror 房主制）：方案与实施里程碑 |
| [audio.md](audio.md) | 音频系统：BGM / 场景音效 / 角色脚步（A1 已装配，素材待补） |
| [terrain-textures.md](terrain-textures.md) | 地形纹理：splat 顶点色 + 纹理数组（Part 14） |
| [features-editing.md](features-editing.md) | 特征编辑：Urban/Farm/Plant/MonsterLair 绘制、密度切换、存读档（Part 9） |
| [pvz3d.md](pvz3d.md) | PvZ 3D 玩法层：种植（FEAT-003 已落地）/ 出怪 / 战斗 / FGUI（规划中） |
| [ui.md](ui.md) | UI 系统：uGUI 框架 + 表驱动配置（联机大厅 / 背包 / 后续 PvZ UI，规划中） |

## 路线图
1. [进行中] 局域网多人化（Mirror）：方案已建档，阶段 0 装包/连通待实施（见 multiplayer.md）
2. [待定] 角色控制：跳跃 / 攻击 / 寻路（见 charactercontrol.md）
3. [✅] 地图纹理美化（Part 14 纹理数组落地，见 terrain-textures.md；后续可换贴图/调平铺密度）
4. [待定] 资源管理系统（资源增多后）
5. [进行中] 音频系统：BGM / 环境音 / 脚步与水域音效（A1 框架已装配，素材待补，见 audio.md）
6. [进行中] PvZ 3D 玩法层：FEAT-001 寻路 + FEAT-003 种植 + **FEAT-002 出怪与 AI 怪物**已落地；后续 战斗(004) / UI(005)（见 pvz3d.md、monsters.md、pathfinding.md）
7. [进行中] UI 系统：uGUI 框架 + 表驱动配置（联机大厅 / 背包，见 ui.md）
8. [✅] 六边形寻路（FEAT-001）：A* + 移动成本 + 沿路径移动 + 场景演示（见 pathfinding.md）
9. [持续] 对已有功能的调整优化

## 变更记录
- 2026-09-11：**AI 怪物系统落地（FEAT-002 出怪 + 锁敌 AI + 场景配置表）**——新增怪物 prefab 目录语义（`Resources/Prefabs/Monsters/`）、`MonsterConfig/MonsterTable/SceneConfigTable`（三张表，CSV↔资产管线复用 `Tools/UI/数据表/`，新增「怪物表 / 场景配置表」导入导出）、`MonsterUnit`（锁最近玩家 / **被更近的玩家攻击才换锁** / 沿六边形路径追击 / 目标死亡重选）、`MonsterSpawner`（数量·频率·存活上限·波次·首只延迟，出怪点=怪物洞穴 `MonsterLairLevel>0`，无巢穴回退西边缘）、`PlayerTarget`+`PlayerTargetRegistry`（玩家目标抽象，支持多玩家）、`PlayerMeleeAttackBridge`（订阅 EllenAttack 命中窗口 → 扣怪血 + 拉仇恨，不改 EllenAttack）、通用层 `CombatSystem.DamageInfo` + `Health.Damage(DamageInfo)/DamagedBy`、`Editor/MonsterSetupTool` 四个菜单；编译零错误零警告，EditMode 断言 **105/105**（见 monsters.md）
- 2026-09-11：**修复**特征编辑器 Lair 行丢失 + 怪物巢穴又小又悬空——① Features Editor 的 `Lair`/`LairSlider` 只改了内存未落盘，编辑器重载即消失 → 重装并保存场景（装配工具改为按当前 Game 视图自适应面板高度、控件可 Undo）；② `HexFeatureManager.TryGetMeshBounds` 忽略粒子渲染器，修掉 `MonsterLair.prefab` 传送门粒子把 FitToWidth/SnapToGround 带偏的坑（巢穴实测 12.00×5.89×10.50 / 贴地 0.000，城市树/农田零回归，见 features-editing.md）；③ 巢穴尺寸调大调高：新增 `lairHeight` 高度倍率、`lairSize` 6→12（1.2 格宽、顶高 ≈3 倍角色身高，免得怪物比巢穴还高）
- 2026-09-11：**六边形寻路落地（FEAT-001）**——`HexCoordinates.DistanceTo`、`HexCellPriorityQueue`（分桶 + 同优先级链表）、`HexGrid` 寻路 region（A* / 通行规则与过滤器 / 距离场 / 最近可通行格 / 世界坐标反查）、`Bezier`、`HexUnit`（实时沿曲线移动 + 逐格与到达事件）、`PathfindingDemo`（中键点地面寻路 / 路径线 / P·O·G 热键）、`Editor/PathfindingSetupTool` 六个菜单；EditMode 断言 **80/80**，`.map` 存档格式未变（见 pathfinding.md）
- 2026-09-11：修复「Play 后点 UI 任何位置都会关闭 UI、无法创建房间」——全屏模态遮罩层级错误（改为动态贴到最上层模态面板下方）；新增 ui_m6_ui_click 射线层断言，全套 178/178（见 ui.md）
- 2026-09-11：**UI 系统双端联调通过**（HexMap 房主 + HexMap2 客户端：局域网发现/点击连接/玩家名与位置同步均验证）；修复 HUD 隐藏导致 NetRoot 被停用的 bug（见 ui.md）
- 2026-09-11：修复背包空数据锁死 bug（InventorySystem 自愈重载 + 面板兜底轮询）；断言一键脚本加编译屏障；全套断言 151/151（见 ui.md）
- 2026-09-11：**UI 系统 M5 收尾**——一键装配总入口（框架+背包+大厅）、`检查 UI 装配状态` 菜单、`tools/run_ui_assertions.sh` 一键跑 5 套 EditMode 断言（39+22+35+40+15=151）、回归检查清单（见 ui.md）
- 2026-09-11：**UI 系统 M4 落地**——联机大厅（F2）：玩家名本地缓存、房主选地图开房（.map 与存档菜单同源）、局域网房间列表（房主名/人数/地图）、IP 直连、状态与超时提示；联机层新增 NetLobbyDiscovery + NetPlayerName；EditMode 断言 40/40（见 ui.md）
- 2026-09-11：**UI 系统 M2+M3 落地**——CSV 数据表管线（`Tools/UI/数据表/` 导入导出，种子表按 id 稳定 upsert 不动 GUID）+ 背包 UI（B 键开关、种子卡片、阳光/冷却实时刷新、点击选卡进入种植模式）；EditMode 断言 22/22 + 35/35（见 ui.md）
- 2026-09-11：**UI 系统 M1 落地**——通用 UI 框架 `Assets/UISystem/`（UIManager 面板栈/层级/互斥/模态遮罩/热键 + UIPanelTable 表驱动 + 中文 OS 字体）+ 游戏侧 `UICharacterInputGate`（模态时冻结角色输入）+ 装配工具 `Tools/UI/一键装配 UI 框架`；编译零错误，EditMode 断言 39/39（见 ui.md）
- 2026-09-11：**UI 系统立项**——技术栈定 uGUI + 表驱动框架（评估 FGUI 后取舍：本机编辑器无专业版授权、命令行发布不可用）；建档 docs/ui.md（分层 / 数据表 / 接触面 / M1~M5 里程碑）
- 2026-09-11：**PvZ 3D 种植系统落地（FEAT-003）**——通用 `CombatSystem.Health` + 游戏侧 `PlantUnit/PlantingSystem/PlantConfig/PlantingInput/PlantShooter` + 一键装配工具；阳光账本 / 卡片冷却 / 三层种植校验（草泥可种，沙石雪·水下·巢穴·已占格拒绝）/ 植物落位与占用表 / 输入仲裁位 `HexControlMode.plantingActive`；EditMode 断言 40/40 通过（见 pvz3d.md）
- 2026-09-10：新增**怪物巢穴（MonsterLair）放置点**——地形特征第四类（Level 0~3），存档升级 v3 并自适应兼容 v0/v1/v2 与联机下发；Features Editor 新增 Lair 开关/滑条，`Tools/地形特征/装配怪物巢穴` 一键装配（见 features-editing.md）
- 2026-09-09：特征编辑完善（Part 9）——Farm/Plant 绘制与存档（header v2 兼容 v1）、密集/稀疏密度开关、特征 UI 绑定修复、Delete 按钮修复（见 features-editing.md）
- 2026-09-09：地形纹理落地——HexMesh uv2 类型通道 + HexGridChunk splat 顶点色 + TerrainTextured shader + Texture2DArray（沙草泥石雪）；terrainTypeIndex 语义定稿，存档格式不变（见 terrain-textures.md）
- 2026-09-07：音频系统 A1 落地——通用模块 + 游戏侧桥接 + 一键装配（SampleScene），素材目录 Resources/Audio 待用户补充（见 audio.md）
- 2026-09-07：音频系统立项——方案建档 docs/audio.md（通用 AudioSystem 模块 + 桥接分层）
- 2026-09-07：局域网多人化立项——方案确认（Mirror 房主制），建档 docs/multiplayer.md
- 2026-09-03：角色控制进入开发——点击移动 + 第三人称跟随相机落地
- 2026-09-02：建立文档骨架
