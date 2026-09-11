# 修复方案文档（植物大战僵尸 3D 功能开发）

> 文档生成时间: 2026-09-10
> 关联问题ID: ISSUE-PVZ3D
> 说明: 本需求为**新增玩法功能**（在 HexMap 基础上制作 PvZ 3D），非缺陷修复。任务卡沿用 fix-doc-generator 的流程模板，任务 ID 使用 `FEAT-` 前缀以区分"功能开发"与"缺陷修复"。

---

## 一、问题诊断（功能缺口分析）

### 1.1 概述

| 属性 | 内容 |
|------|------|
| 问题ID | ISSUE-PVZ3D |
| 发现时间 | 2026-09-10 |
| 影响级别 | P1（大型功能开发，非线上缺陷） |
| 问题状态 | 待开发 |
| 目标 | 在已完成的 HexMap 六边形地图上制作 PvZ 3D：出怪、种植、寻路、战斗、FGUI 五大子系统 |
| 影响模块 | HexCell / HexGrid / HexCoordinates / 新增 CombatSystem / 新增 FGUI 层 |

### 1.2 现状与缺口

**已具备的基础（可直接复用）：**

- 网格：`HexGrid`（20×15 offset → cube 坐标）、`HexCell`（`TerrainTypeIndex` 0-4、`Elevation`、`WaterLevel`、`UrbanLevel/FarmLevel/PlantLevel/MonsterLairLevel`、河流、六邻居数组 `neighbors`）、`HexCoordinates`（cube 坐标 + `FromPosition`）、`HexDirection/Extensions`（`Opposite/Next/Previous`）。
- 地形纹理：`terrainTypeIndex` 语义已定稿 **0 沙 / 1 草 / 2 泥 / 3 石 / 4 雪**（Part14，splat 顶点色 + Texture2DArray）。
- 特征系统（纯视觉）：`HexFeatureManager` 已含 `urbanPrefabs/farmPrefabs/plantPrefabs/monsterLairPrefabs`；其中 **`monsterLair`（怪物巢穴）语义已预留给"僵尸巢穴/出怪点"**，`MonsterLairLevel > 0` 的格即为候选出怪点。
- 地图存档：`HexGrid.Save/Load`（header=3，11B/格，含 monsterLair），`SaveLoadMenu`、联机 `NetGameManager` 下发同格式。
- 角色：`HeroController`（点击移动）、`EllenAttack`（近战 Animator Trigger，无伤害判定）、`EllenNetController`（联机分流）。
- 联机：Mirror 房主制（`NetGameManager`）+ `NetworkAnimator` / `NetworkTransformReliable`。

**缺口（本需求要新增的，当前全部不存在）：**

| 缺口 | 现状证据 | 目标 |
|------|----------|------|
| ① 寻路 | `HeroController` 注释明确"不做寻路，直线插值"；`HexCoordinates` **无 `DistanceTo`**；无 A* / 优先队列 / 路径数据 | 六边形 A* + 移动成本 + 沿路径动画移动 |
| ② 出怪 | 无僵尸/敌人单位；`monsterLair` 仅视觉 prefab，无波次/刷怪逻辑 | 出怪点 + 波次系统 + 僵尸单位 |
| ③ 种植 | `PlantLevel` 是纯视觉装饰（`HexFeatureManager` 概率摆放），无"可玩植物"（血量/攻击/CD）、无种植交互/阳光/冷却/地块校验 | 可玩植物单位 + 种植系统 |
| ④ 战斗 | 无血量/伤害/投射物/索敌；`EllenAttack` 只触发动画事件不发伤害 | 植物射击僵尸 + 僵尸啃食植物 + 胜负判定 |
| ⑤ UI | 现有 UI 全为 uGUI（`HexMapEditor` 滑条 / `SaveLoadMenu` 等）；**FairyGUI 完全未导入**（0 命中） | 引入 FGUI：选卡 / 阳光 / 波次 / 种植模式 / 胜负弹窗 |

### 1.3 根因分析

**直接原因**：HexMap 教程目前仅做到"地形 + 特征 + 角色点击移动"阶段，尚未进入 Part15-19（距离/寻路/单位/移动）与任何战斗玩法，这是教程进度本身决定的，非代码缺陷。

**深层原因**：PvZ 玩法依赖一套"**单位 + 寻路 + 战斗数值 + UI**"的实时玩法层，与现有"地形可视化"层是两类系统，需要：
1. 补上寻路基础能力（HexCoordinates 距离 + A*）。
2. 新建"可玩单位"（植物/僵尸），与现有"视觉特征"（PlantLevel/MonsterLairLevel）**解耦**，避免污染已有可用系统。
3. 战斗模块遵循项目既定的"**通用组件 + 游戏侧桥接**"范式（同 AudioSystem / UnderwaterEffect），做到跨项目复用、模块内不引用 HexGrid/HexCell。

**影响范围**：
- 新增文件：约 15-20 个 .cs（寻路 4 个、出怪 3-4 个、种植 3-4 个、战斗 6-8 个、FGUI 若干）。
- 修改文件：`HexCoordinates.cs`（+`DistanceTo`）、`HexCell.cs`（+寻路临时字段）、`HexGrid.cs`（+`FindPath/Search`）、`HexGridChunk.cs`（如需地形 collider 供寻路可达性，视情况）。
- **不动**：`HeroController`、`EllenAttack`、`HexMapEditor`、特征/联机现有逻辑。

### 1.4 复现/触发场景（需求用例）

1. 加载/新建一张地图，含草/泥可种植区 + 一个 `monsterLair` 出怪点 + 一个终点（基地/房）。
2. 玩家在 FGUI 选卡后点击可种植格 → 种植植物。
3. 波次开始 → 僵尸从出怪点出生，**沿六边形路径寻路**向终点移动。
4. 植物自动索敌射程内僵尸 → 投射物命中扣血；僵尸到达植物格 → 啃食植物。
5. 僵尸到达终点 → 判负；清完所有波次 → 判胜。

---

## 二、修复方案（功能实现方案）

### 2.1 方案对比

#### 2.1.1 寻路方案

| 维度 | 方案A（推荐）移植 catlike A* | 方案B Unity NavMesh | 方案C 简化 BFS |
|------|------------------------------|---------------------|----------------|
| 描述 | 移植 HexMap Part15/16/17/19 的 DistanceTo + A* + 优先队列 + 沿路径动画 | 烘焙 NavMesh 后 NavMeshAgent | 仅广度优先、无成本、无优化 |
| 优点 | 教程有完整方案+中文翻译，天然贴合六边形；支持地形/特征移动成本、障碍、可选路径 | 省事，自带避障 | 实现最简单 |
| 缺点 | 需把"回合制速度"适配为"实时移动" | 与六边形格/地形类型语义脱节，难做"仅草/泥可行走"、"绕过植物" | 无移动成本、无启发式、路径质量差 |
| 风险 | 低 | 中 | 低 |
| 工作量 | 中 | 中 | 小 |

**选择：方案A**。理由：项目就是 Catlike HexMap 教程实现，Part15-19 是官方给出的、与现有 `HexCell/HexGrid/HexCoordinates` 结构完全匹配的续篇；且用户明确要求"地形与寻路优先参考 catlike hexmap 教程"。适配点：PvZ 是实时制，把教程的"每回合移动预算(speed)"改为"僵尸每帧连续移动"，路径一次性算好沿路走。

#### 2.1.2 种植"可玩植物"与现有视觉 PlantLevel 的关系

| 维度 | 方案A（推荐）新建可玩植物单位 | 方案B 扩展现有 PlantLevel |
|------|-------------------------------|---------------------------|
| 描述 | 新建 `PlantUnit`（血量/攻击/CD/射程）+ `PlantingSystem`，与视觉 PlantLevel 完全解耦 | 给 `HexFeatureManager` 的植物加战斗逻辑 |
| 优点 | 不污染现有特征系统；植物是独立 GameObject 便于战斗/网络/动画 | 复用已有摆放 |
| 缺点 | 需新种植校验层 | 视觉与战斗耦合，违反用户"不改现有系统、模块解耦"约定 |
| 风险 | 低 | 高 |
| 工作量 | 中 | 中 |

**选择：方案A**。理由：用户已明确偏好"新功能用独立新组件/新脚本，不改现有可用系统"；现有 `PlantLevel/MonsterLairLevel` 保持纯视觉语义不动。

#### 2.1.3 战斗模块组织

| 维度 | 方案A（推荐）通用战斗模块+桥接 | 方案B 逻辑直接写在植物/僵尸脚本 |
|------|-------------------------------|--------------------------------|
| 描述 | `Assets/CombatSystem`（Health/Damageable/Projectile/TargetScanner 通用组件，零游戏依赖）+ `Assets/Scripts/Combat`（PlantShooter/ZombieAttacker 桥接） | 血量伤害直接写进每个单位 |
| 优点 | 跨项目复用；与 AudioSystem/UnderwaterEffect 同范式，用户已认可 | 文件少 |
| 缺点 | 多一层桥接 | 每单位重复实现、难扩展、违反用户"表现层模块独立可复用"要求 |
| 风险 | 低 | 中 |
| 工作量 | 中 | 小 |

**选择：方案A**。理由：用户曾明确要求"表现层效果/战斗模块必须通用组件 + 小接口 + 游戏侧桥接器，模块目录内连注释都 grep 干净（不引用 HexGrid/HexCell）"。

#### 2.1.4 UI 方案

用户已指定 **FGUI**，不做方案对比。采用 `fgui-editor`（建源 XML/包）→ `fgui-runtime`（codegen C#）→ `ui-creator`（一键建 View/Prefab）链路；需先导入 FairyGUI-Unity SDK。

### 2.2 推荐方案汇总

1. 寻路：移植 catlike A*（Part15/16/17/19），实时化适配。
2. 种植：新建 `PlantUnit` + `PlantingSystem`，与视觉特征解耦。
3. 战斗：通用 `CombatSystem` 模块 + 游戏侧桥接。
4. 出怪：复用 `MonsterLairLevel > 0` 格作为出怪点 + `WaveManager` 波次系统。
5. UI：FGUI 全链路。

### 2.3 技术实现要点

#### 2.3.1 寻路（catlike Part15/16/17/19 移植）

- `HexCoordinates.DistanceTo(HexCoordinates other)`：`(|Δx| + |Δy| + |Δz|) / 2`（cube 坐标距离，Part15 §3.2）。
- `HexCell` 增加非序列化的搜索临时字段：`PathFrom`、`SearchHeuristic`、`SearchPriority`、`NextWithSamePriority`（Part16）。
- 新文件 `HexCellPriorityQueue.cs`：二叉堆，含 `Enqueue/Dequeue/Change/Contains`（Part16 §4）。
- `HexGrid.FindPath(from, to)` + `Search`：A*，开放集用优先队列，启发式 = `DistanceTo(目标)`；移动成本参考 Part15 §5（跳过 `IsUnderwater` 格；edgeType Flat=5 / Slope/Cliff=10；可再加特征成本）。PvZ 简化：**仅草/泥可行走**（障碍=石/雪/水/已占用格，通过一个 `IsWalkable(HexCell)` 判定集中管理）。
- 僵尸沿路径移动：新文件 `ZombieUnit.cs`，`Travel(List<HexCell> path)` + 协程逐格移动，用 `Bezier.GetPoint`（新文件 `Bezier.cs`，Part19 §2.2）+ `LookAt` 朝向（Part19 §3.2）。**实时化**：删除回合制 speed 预算，改为 `travelSpeed` 常量/字段连续移动。

> 📚 **技术来源**:
> - [Catlike Coding Hex Map Part 15 - Distances](https://catlikecoding.com/unity/tutorials/hex-map/part-15/)
> - [Catlike Coding Hex Map Part 16 - Pathfinding](https://catlikecoding.com/unity/tutorials/hex-map/part-16/)
> - [Catlike Coding Hex Map Part 17 - Limited Movement](https://catlikecoding.com/unity/tutorials/hex-map/part-17/)
> - [Catlike Coding Hex Map Part 19 - Animating Movement](https://catlikecoding.com/unity/tutorials/hex-map/part-19/)
> - 本地中文翻译：`F:\A-Ruosa\PersonalNotes\Teach\Cat\HexMap\15-距离 / 16-寻路 / 17-受限移动 / 19-动画移动`

#### 2.3.2 出怪（波次 + 僵尸单位）

- 出怪点：遍历 `HexGrid.cells`，收集 `MonsterLairLevel > 0` 的格作为 spawn 点（与已预置的"怪物巢穴"语义对齐）；无巢穴时回退到地图边缘格。
- `WaveManager.cs`：波次表（每波僵尸数量/类型/间隔），定时刷怪，记录剩余僵尸数，全部清完触发胜利事件。
- `ZombieConfig`（ScriptableObject）：血量/移速/伤害/啃食间隔/寻路终点引用。
- 终点（基地/房）：本阶段用一个可配置的"终点格"（如地图对侧边缘格或指定 `specialIndex` 预留），僵尸寻路目标 = 终点格。

#### 2.3.3 种植系统

- `PlantConfig`（ScriptableObject）：阳光消耗/冷却/血量/攻击力/射程/攻速/植物 prefab。
- `PlantingSystem.cs`：阳光计数、冷却管理、种植合法性校验。
- 种植校验（`CanPlant(HexCell)`）：`TerrainTypeIndex ∈ {1 草, 2 泥}` 且 `!IsUnderwater` 且无既有可玩单位占用 且 `MonsterLairLevel == 0`。
- `PlantUnit.cs`：可玩植物单位，持有 Health + 射击器桥接。

#### 2.3.4 战斗（通用模块 + 桥接）

- 通用模块 `Assets/CombatSystem`（**零游戏依赖，注释/引用 grep 干净**）：
  - `Health.cs`（当前/上限、受伤、死亡事件）
  - `Damageable.cs` / `Damage.cs`（伤害类型）
  - `Projectile.cs`（直线飞行、命中回调、生命周期）
  - `TargetScanner.cs`（球形/范围索敌，返回最近目标）
- 游戏侧 `Assets/Scripts/Combat`：
  - `PlantShooter.cs`（植物桥接：定时扫描射程内僵尸 → 生成 Projectile）
  - `ZombieAttacker.cs`（僵尸桥接：到达植物格 → 定时啃食）
  - `CombatManager.cs`（胜负判定、单位注册/注销）

#### 2.3.5 FGUI

- 引入 FairyGUI-Unity SDK（当前未集成）。
- 用 `fgui-editor` 建包 `PvZUI`：选卡栏（植物卡片列表）、阳光计数、波次提示、种植模式开关、胜利/失败弹窗。
- 用 `fgui-runtime` codegen 生成 C#，`ui-creator` 建 View/Prefab。
- 桥接：FGUI View 订阅 `PlantingSystem`/`WaveManager`/`CombatManager` 的事件，避免 UI 直接依赖 HexGrid/HexCell。

### 2.4 风险评估与回滚

**影响范围**：
- 直接：新增寻路字段到 `HexCell/HexCoordinates/HexGrid`（追加式，不破坏现有字段语义）。
- 间接：`HexCell` 新增搜索临时字段需确保**不进存档**（`Save/Load` 不读写它们），避免破坏 `.map` 11B/格格式与联机下发。

**副作用分析**：
- 寻路字段若误序列化进 `.map`，会破坏存档/联机兼容 → 用非序列化 `[System.NonSerialized]` + 显式不写入 `Save/Load`。
- 战斗单位射线/碰撞可能干扰 `HexMapEditor` 点击编辑与 `HeroController` 移动（同现有"特征关碰撞体"坑）→ 植物/僵尸放在独立 Layer，或关闭其对编辑射线的碰撞。
- 多实例（HexMap/HexMap2 同物理 Assets）改 .cs 需同步两端 + CodeTxts 导出。

**回滚方案**：
- 所有新增文件为独立新文件，删除即回滚；对 `HexCell/HexCoordinates/HexGrid` 的追加改动为独立方法/字段，逐段可 `git diff` 回退。
- 每个 FIX 任务单独 commit，保证可单独回滚。

---

## 三、Agent 任务拆分

### 3.1 批次规划

| Batch | 阶段 | 执行方式 | 任务 |
|-------|------|----------|------|
| 1 | 寻路基础 | 串行 | FEAT-001 |
| 2 | 出怪 + 种植 | 并行(2) | FEAT-002、FEAT-003 |
| 3 | 战斗 | 串行（依赖 002+003） | FEAT-004 |
| 4 | FGUI | 串行（依赖 002/003 数据接口） | FEAT-005 |

### 3.2 任务依赖关系图

```
Batch 1: FEAT-001 寻路（DistanceTo + A* + 沿路径移动）
              │
   ┌──────────┴──────────┐
   ▼                     ▼
Batch 2: FEAT-002 出怪   FEAT-003 种植   （并行，最多2）
   │                     │
   └──────────┬──────────┘
              ▼
Batch 3: FEAT-004 战斗（植物↔僵尸）
              │
              ▼
Batch 4: FEAT-005 FGUI（依赖 002 波次数据 + 003 阳光/冷却数据）
```

### 3.3 Agent 任务卡

#### Agent 任务: FEAT-001 六边形寻路（DistanceTo + A* + 沿路径移动）

**基本信息**:
- 任务ID: FEAT-001
- 问题级别: 一般（功能开发，基础依赖）
- 影响模块: HexCoordinates / HexCell / HexGrid + 新增 HexCellPriorityQueue / ZombieUnit(移动部分) / Bezier
- 依赖任务: 无
- 预计风险: 低

**问题描述**:
当前无任何寻路能力：`HexCoordinates` 无距离函数，`HexGrid` 无路径搜索，`HeroController` 采用直线插值移动。需为僵尸等实时单位提供"沿六边形路径移动"能力，并支持移动成本（跳过水下/石/雪等不可行走格）。

**修复方案**:
1. `HexCoordinates` 增加 `DistanceTo(HexCoordinates other)`（cube 坐标曼哈顿距离 / 2）。
2. `HexCell` 增加非序列化搜索字段 `PathFrom / SearchHeuristic / SearchPriority / NextWithSamePriority`（**不写入 Save/Load**）。
3. 新增 `HexCellPriorityQueue.cs`（二叉堆）。
4. `HexGrid` 增加 `FindPath(HexCell from, HexCell to)` 与 A* 搜索；集中实现 `IsWalkable(HexCell)`（默认草/泥可行走，水/石/雪/已占用不可）。
5. 新增 `Bezier.cs`（`GetPoint/GetDerivative`）与 `ZombieUnit`（或先做通用 `HexUnit` 移动基类）的 `Travel(List<HexCell>)` 协程，实时逐格移动 + 朝向。

**输入**:
- 待改代码: `Assets/Scripts/HexCoordinates.cs`、`HexCell.cs`、`HexGrid.cs`
- 参考: `F:\A-Ruosa\PersonalNotes\Teach\Cat\HexMap\15-距离、16-寻路、17-受限移动、19-动画移动`

**输出**:
- 新增: `Assets/Scripts/HexCellPriorityQueue.cs`、`Bezier.cs`、`Assets/Scripts/HexUnit.cs`（或 `ZombieUnit.cs` 移动基类）
- 修改: `HexCoordinates.cs`、`HexCell.cs`、`HexGrid.cs`
- 修复说明: 更新 `Assets/docs/README.md` 路线图与新增 `docs/pathfinding.md`

**执行步骤**:
1. 备份/记录改动前 `HexCoordinates/HexCell/HexGrid` 三文件状态。
2. 实现 DistanceTo → 编译通过。
3. 实现 HexCellPriorityQueue + A* FindPath。
4. 实现沿路径移动（实时，无回合制 speed 预算）。
5. 镜像 CodeTxts（只导改动文件），更新文档。

**验收标准（不含测试）**:
- [ ] `HexCoordinates.DistanceTo` 返回正确 hex 距离（对已知坐标对验证）
- [ ] `HexGrid.FindPath` 在无障碍/有障碍地图上返回正确路径序列
- [ ] 不可行走格（水/石/雪）被正确跳过
- [ ] 可编译通过，无新增编译告警
- [ ] 搜索字段不进 `.map` 存档（Save/Load 字节格式不变）
- [ ] 代码审查通过

> 测试相关验证见 `test-strategy.md`，本文档不含测试内容。

---

#### Agent 任务: FEAT-002 出怪系统（波次 + 僵尸单位）

**基本信息**:
- 任务ID: FEAT-002
- 问题级别: 一般（功能开发）
- 影响模块: 新增 WaveManager / ZombieUnit / ZombieConfig；复用 HexGrid.monsterLair 语义
- 依赖任务: FEAT-001
- 预计风险: 中

**问题描述**:
无僵尸单位、无刷怪/波次逻辑。需在 `MonsterLairLevel > 0` 的格（或地图边缘）作为出怪点，按波次定时生成僵尸，僵尸出生后沿寻路向终点移动。

**修复方案**:
1. `ZombieConfig`（ScriptableObject）：血量/移速/伤害/啃食间隔/zombie prefab。
2. `ZombieUnit.cs`：持有 `Health` 桥接 + 寻路移动（调用 FEAT-001 的 Travel），记录终点格。
3. `WaveManager.cs`：波次表、刷怪定时器、剩余僵尸计数、全清广播胜利事件、失败（僵尸抵达终点）广播。
4. 出怪点解析：`HexGrid` 提供 `GetSpawnCells()`（返回 `MonsterLairLevel > 0` 的格集合）；终点用可配置格（本阶段用地图对侧边缘或指定格）。
5. 僵尸 prefab 用低模占位（可先用项目现有低模/占位 cube，`model-unit-creator` 技能可生成）。

**输入**:
- 依赖: FEAT-001 的 `FindPath` / `Travel`
- 待改: 无（全部新增）；复用 `HexGrid` / `HexCell.MonsterLairLevel`

**输出**:
- 新增: `Assets/Scripts/Combat/WaveManager.cs`、`Assets/Scripts/Combat/ZombieUnit.cs`、`ZombieConfig`（SO，`Assets/Resources/`）
- 修复说明: `docs/pvz3d.md` 出怪章节

**执行步骤**:
1. 建 ZombieConfig SO 与占位僵尸 prefab。
2. 实现 ZombieUnit（寻路移动 + 终点判定）。
3. 实现 WaveManager（波次 + 刷怪 + 胜负事件）。
4. 装配一个测试地图（一个巢穴 + 一个终点）供目检。
5. 镜像 CodeTxts、更新文档。

**验收标准（不含测试）**:
- [ ] 僵尸从出怪点按波次定时生成
- [ ] 僵尸沿寻路路径向终点连续移动（不穿坡/不进水）
- [ ] 僵尸抵达终点触发失败事件；全清触发胜利事件
- [ ] 可编译通过，无新增编译告警
- [ ] 代码审查通过

> 测试相关验证见 `test-strategy.md`。

---

#### Agent 任务: FEAT-003 种植系统（可玩植物 + 阳光/冷却/地块校验）

**基本信息**:
- 任务ID: FEAT-003
- 问题级别: 一般（功能开发）
- 影响模块: 新增 PlantUnit / PlantingSystem / PlantConfig；复用 HexCell.TerrainTypeIndex
- 依赖任务: 无（弱依赖 FEAT-001 的 `IsWalkable`，可并行）
- 预计风险: 中

**问题描述**:
现有 `PlantLevel` 为纯视觉装饰，无"可玩植物"（有血量/攻击/CD 的单位）、无种植交互、无阳光/冷却/地块合法性校验。需在特定类型 HexCell（草/泥）上实现种植。

**修复方案**:
1. `PlantConfig`（ScriptableObject）：阳光消耗/冷却/血量/攻击力/射程/攻速/植物 prefab。
2. `PlantUnit.cs`：可玩植物单位，持有 `Health` + 射击器桥接（射击逻辑由 FEAT-004 补全，本任务先占位 `PlantShooter` 空实现）。
3. `PlantingSystem.cs`：阳光计数（`AddSun/SpendSun`）、冷却管理、`CanPlant(HexCell)` 校验（`TerrainTypeIndex ∈ {1,2}` 且非水下、无占用、无巢穴）、`Plant(cell, config)` 实例化。
4. 种植交互入口：本任务先提供可编程 API + 一个临时鼠标点击入口（正式 UI 由 FEAT-005 接 FGUI）。

**输入**:
- 待改: 无（全部新增）；复用 `HexGrid.GetCell` / `HexCell.TerrainTypeIndex`
- 参考: 现有 `HexFeatureManager` 的"特征关碰撞体"坑（种植单位需放独立 Layer）

**输出**:
- 新增: `Assets/Scripts/Combat/PlantingSystem.cs`、`PlantUnit.cs`、`PlantConfig`（SO）、占位植物 prefab
- 修复说明: `docs/pvz3d.md` 种植章节

**执行步骤**:
1. 建 PlantConfig SO 与占位植物 prefab。
2. 实现 PlantingSystem（阳光/冷却/校验/实例化）。
3. 实现 PlantUnit（持有 Health + 占位射击器）。
4. 处理种植单位碰撞层，避免干扰地图编辑/角色移动射线。
5. 镜像 CodeTxts、更新文档。

**验收标准（不含测试）**:
- [ ] `CanPlant` 在草/泥格返回 true，在沙/石/雪/水下/巢穴/已占用格返回 false
- [ ] 种植消耗阳光并进入冷却，阳光不足/冷却中无法种植
- [ ] 种植后植物正确落位于格中心顶面
- [ ] 可编译通过，无新增编译告警
- [ ] 代码审查通过

> 测试相关验证见 `test-strategy.md`。

---

#### Agent 任务: FEAT-004 战斗系统（植物射击 ↔ 僵尸啃食 + 胜负）

**基本信息**:
- 任务ID: FEAT-004
- 问题级别: 一般（功能开发）
- 影响模块: 新增 CombatSystem 通用模块 + Combat 桥接
- 依赖任务: FEAT-002、FEAT-003
- 预计风险: 中

**问题描述**:
无血量/伤害/投射物/索敌，植物与僵尸之间无战斗交互。需实现：植物自动索敌射程内最近僵尸并发射投射物；僵尸到达植物格后定时啃食；双方死亡判定；胜负条件（僵尸到终点败/清波胜）。

**修复方案**:
1. 通用模块 `Assets/CombatSystem`（**零游戏依赖**）：`Health.cs`、`Damageable.cs`、`Damage.cs`、`Projectile.cs`、`TargetScanner.cs`。
2. 游戏侧 `Assets/Scripts/Combat`：
   - `PlantShooter.cs`（植物桥接：定时扫射程内僵尸 → 生成 Projectile）
   - `ZombieAttacker.cs`（僵尸桥接：到达植物格 → 定时啃食扣植物血）
   - `CombatManager.cs`（单位注册/注销、胜负判定，对接 WaveManager 事件）
3. 投射物命中 → 目标 `Health.Damage()`；血量归零 → 死亡事件 → 单位销毁/移除。
4. 模块内注释/引用 grep 干净，不引用 `HexGrid/HexCell`（仅通过接口/事件桥接）。

**输入**:
- 依赖: FEAT-002 的 ZombieUnit/WaveManager、FEAT-003 的 PlantUnit
- 范式参考: `Assets/AudioSystem`（通用模块）+ `Assets/Scripts/Audio`（桥接）

**输出**:
- 新增: `Assets/CombatSystem/*`（通用，5 文件）、`Assets/Scripts/Combat/PlantShooter.cs`、`ZombieAttacker.cs`、`CombatManager.cs`
- 修复说明: `docs/pvz3d.md` 战斗章节

**执行步骤**:
1. 建 CombatSystem 通用模块（Health/Damageable/Damage/Projectile/TargetScanner）。
2. 实现 PlantShooter + ZombieAttacker 桥接。
3. 实现 CombatManager 胜负判定（对接 WaveManager 胜负事件）。
4. 处理死亡清理（对象池或 Destroy）。
5. 镜像 CodeTxts、更新文档。

**验收标准（不含测试）**:
- [ ] 植物对射程内最近僵尸发射投射物，命中扣血
- [ ] 僵尸到达植物格后定时啃食，植物血量归零后移除
- [ ] 血量归零单位正确销毁，无空引用异常
- [ ] CombatSystem 模块内 grep 无 `HexGrid/HexCell` 引用
- [ ] 可编译通过，无新增编译告警
- [ ] 代码审查通过

> 测试相关验证见 `test-strategy.md`。

---

#### Agent 任务: FEAT-005 FGUI 界面（SDK 引入 + 选卡/阳光/波次/胜负）

**基本信息**:
- 任务ID: FEAT-005
- 问题级别: 一般（功能开发）
- 影响模块: 新增 FGUI 层（SDK + 包 + View/Prefab + 桥接）
- 依赖任务: FEAT-002（波次数据）、FEAT-003（阳光/冷却数据）
- 预计风险: 中

**问题描述**:
项目当前无任何 FGUI（0 命中），现有 UI 全为 uGUI。需引入 FairyGUI-Unity SDK，用 FGUI 实现：植物选卡栏、阳光计数、波次提示、种植模式切换、胜利/失败弹窗，并桥接到 PlantingSystem/WaveManager/CombatManager 的数据与事件。

**修复方案**:
1. 导入 FairyGUI-Unity SDK（注意：若需官方编辑器 CLI 发布，需专业版授权，见 `fgui-editor` 技能）。
2. 用 `fgui-editor` 建包 `PvZUI`：`PlantCardList`（选卡）、`SunCounter`（阳光）、`WaveBanner`（波次）、`PlantingToggle`（种植模式）、`ResultDialog`（胜负）。
3. 用 `fgui-runtime` codegen 生成 C#，`ui-creator` 建 View/Prefab。
4. 桥接：FGUI View 订阅游戏侧事件（`PlantingSystem` 阳光/冷却变化、`WaveManager` 波次/剩余僵尸、`CombatManager` 胜负），点击选卡 → `PlantingSystem.SelectPlant(config)` 进入种植模式 → 点击格种植。
5. 输入仲裁：种植模式激活时置 `HexControlMode.heroActive`（或新增 `plantingActive` 位），让 `HeroController`/`HexMapEditor` 让出鼠标（与现有 `HexControlMode` 约定一致）。

**输入**:
- 依赖: FEAT-002 WaveManager 事件、FEAT-003 PlantingSystem API
- 技能链: `fgui-editor`（建 XML/包）→ `fgui-runtime`（codegen）→ `ui-creator`（View/Prefab）

**输出**:
- 新增: FGUI 包 `PvZUI` + 生成 View/Designer/Binder + 桥接脚本 `Assets/Scripts/UI/*`
- 修复说明: `docs/pvz3d.md` UI 章节

**执行步骤**:
1. 导入 FairyGUI-Unity SDK 并验证最小运行。
2. 建 FGUI 包与组件。
3. codegen + 建 View/Prefab，桥接游戏事件。
4. 处理种植模式与角色/编辑的输入仲裁。
5. 镜像 CodeTxts、更新文档。

**验收标准（不含测试）**:
- [ ] FGUI SDK 正常初始化，UI 在 Game 视图显示
- [ ] 选卡 → 进入种植模式 → 点击可种植格成功种植（不可种植格不响应/提示）
- [ ] 阳光计数实时刷新，冷却状态正确显示
- [ ] 波次提示与胜负弹窗正确弹出
- [ ] 可编译通过，无新增编译告警
- [ ] 代码审查通过

> 测试相关验证见 `test-strategy.md`。

---

## 四、实施计划

### 4.1 阶段划分

| 阶段 | 任务 | 产出（实现） |
|------|------|--------------|
| 阶段1 | FEAT-001 | 寻路基础（DistanceTo / A* / 沿路径移动） |
| 阶段2 | FEAT-002 + FEAT-003（并行） | 出怪波次 + 僵尸单位；种植系统 + 植物单位 |
| 阶段3 | FEAT-004 | 战斗系统（通用模块 + 桥接 + 胜负） |
| 阶段4 | FEAT-005 | FGUI 界面（SDK + 选卡/阳光/波次/胜负 + 桥接） |

### 4.2 落地里程碑

- M1：寻路可用 —— 一个占位单位能沿六边形路径从 A 走到 B（绕过水/石/雪）。
- M2：僵尸从巢穴波次出生并走到终点。
- M3：可种植植物，植物与僵尸发生战斗，能判定胜负。
- M4：FGUI 完整体验闭环（选卡→种植→战斗→胜负）。

### 4.3 约束与纪律

- 严格遵循 `.hermes.md`：改 `.cs` 后运行 `Tools/代码同步/导出 .cs 为 .txt (CodeTxts)`；每功能补 `docs/<feature>.md`；开工前读 `docs/README.md`。
- 遵循用户偏好：新增功能用独立组件/脚本，不改 `HeroController/EllenAttack/HexMapEditor` 等现有系统；战斗模块通用化、模块内 grep 干净；旧功能保留。
- 每个 FEAT 任务单独 commit，可独立回滚。
- 编辑器内验证前先问用户，不擅自 Play / 保存场景。

> 各阶段对应的测试计划（单元/集成/回归/场景）见 `test-strategy.md`，此处不重复。
