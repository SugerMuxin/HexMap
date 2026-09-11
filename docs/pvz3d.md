# PvZ 3D（植物大战僵尸玩法层）

> 方案文档：`docs/fix/pvz3d/fix-plan.md`（任务拆分 FEAT-001 ~ FEAT-005）
> 测试策略：`docs/fix/pvz3d/test-strategy.md`
> 通用能力层：六边形寻路（FEAT-001）已落地 → `docs/pathfinding.md`（`HexGrid.FindPath` + `HexUnit.Travel`，出怪/战斗直接复用）。
> 定位：在已完成的 HexMap（地形 + 特征 + 角色 + 联机 + 音频）之上叠加一层"实时玩法层"，
> 与现有"地形可视化层"解耦——新增独立组件，不改动 HexGrid / HexCell 既有语义。

## 0. 子系统状态

| 任务 | 内容 | 状态 |
|------|------|------|
| FEAT-001 | 六边形寻路（DistanceTo + A* + 沿路径移动） | ✅ 已落地（2026-09-11，见 **pathfinding.md**） |
| FEAT-002 | **出怪（波次 + 僵尸单位）** + AI 锁敌 | ✅ 已落地（2026-09-11，见 **monsters.md**：怪物洞穴出怪 / 场景配置表 / 找最近玩家 / 被更近者攻击换锁） |
| FEAT-003 | **种植（可玩植物 + 阳光/冷却/地块校验）** | ✅ 已落地（2026-09-11） |
| FEAT-004 | 战斗（植物射击 ↔ 僵尸啃食 + 胜负判定） | ⬜ 未开始（已留接线点；**玩家→怪物**的伤害与仇恨已由 FEAT-002 打通） |
| FEAT-005 | UI 界面（选卡 / 阳光 / 波次 / 胜负）— 技术栈已改 **uGUI + 表驱动**（原 FGUI 因本机无专业版授权放弃，见 ui.md / README） | 🚧 M1 框架已落地；面板内容临时按键入口可用 |

---

## 0.5 出怪与 AI 怪物（FEAT-002）

见 **`docs/monsters.md`**（权威）。要点：

- **怪物 prefab 放在 `Assets/Resources/Prefabs/Monsters/`**，一键装配复用你已有的 prefab（只补挂 `MonsterUnit`）。
- **从怪物洞穴诞生**：出怪点 = `HexCell.MonsterLairLevel > 0` 的格（无巢穴回退地图西边缘）。
- **场景配置表（新增）**：`Assets/Resources/Configs/Tables/SceneConfigTable.csv` →
  `SceneConfigTable.asset`，一行 = 一条刷怪规则（场景名 / 怪物 id / count 数量 / interval 频率 /
  maxAlive 上限 / startDelay / waves 波次 / waveInterval / waitForClear / spawnPoint）。
  导入菜单：`Tools/UI/数据表/导入 场景配置表 CSV`。
- **锁敌**：`MonsterUnit` 出生即锁**离它最近的玩家**（`PlayerTarget` + `PlayerTargetRegistry`，天然支持多玩家）；
  **有更近的玩家攻击它时转换锁定目标**（受击事件自动触发 `NotifyAttacked`，或由 `PlayerMeleeAttackBridge` 显式调用）；
  目标死亡/消失则重选最近。追击复用 `HexUnit`（A* + 沿曲线移动）。
- **玩家攻击**：`PlayerMeleeAttackBridge` 订阅 `EllenAttack` 的命中窗口事件做范围判定（**未改 EllenAttack**），
  命中 → `Health.Damage(DamageInfo{source=玩家})` → 拉仇恨。
- 装配：菜单 `Tools/怪物系统/一键装配 (AI 怪物)`；断言：`tools/monsters_editmode_assertions.exec.cs`（105/105）。

---

## 1. 种植系统（FEAT-003）

### 1.1 目标

在草 / 泥地形格上种植"可玩植物"：消耗阳光、按卡冷却、地块合法性校验、植物落地为单位
（有血量、有攻击数值），为 FEAT-004 战斗与 FEAT-005 选卡 UI 提供数据与事件接口。

### 1.2 当前状态

已完成并验证（编译零错误零警告 + 40 条 EditMode 断言全过）：

- 阳光账本：初始 50，可选自然产出 25/5s（可关），`AddSun / TrySpendSun / ResetSun`。
- 选卡（种植模式）：`SelectPlant / ClearSelection / IsPlantingMode`，供临时按键入口与 FGUI 共用。
- 三层校验：`CheckCell`（格规则）→ `Validate`（+ config / 冷却 / 阳光）→ `CanPlant`，失败原因用
  `PlantResult` 枚举 + `PlantingSystem.Describe()` 中文文案（UI 直接可显示）。
- 种植：扣阳光 → 进入该卡冷却 → 实例化植物 → 登记格占用 → 广播事件。
- 植物单位：`PlantUnit` 持有通用 `Health`，死亡自动注销占用并销毁；`PlantShooter` 已同步数值、
  等待 FEAT-004 接入索敌与投射物（`shootingEnabled` 默认 false，当前不发射）。
- 装配：菜单 `Tools/种植系统/一键装配 (PvZ 种植)` 一键生成占位材质/prefab/配置并装配场景，
  幂等可重复运行。

### 1.3 设计要点

**分层（沿用 AudioSystem / UnderwaterEffect 的"通用模块 + 游戏侧桥接"范式）**

| 层 | 目录 | 依赖 |
|----|------|------|
| 通用战斗层 | `Assets/CombatSystem/` | 零游戏依赖（当前含 `Health`；FEAT-004 追加 Damageable/Projectile/TargetScanner） |
| 游戏侧种植 | `Assets/Scripts/Combat/` | 读 `HexCell.TerrainTypeIndex / IsUnderwater / MonsterLairLevel`、`HexGrid.GetCell` |
| 装配工具 | `Assets/Editor/PlantingSetupTool.cs` | Editor 专用（建资源 + 场景接线） |

**校验规则（集中可配）**

- 可种植地形 = `plantableTerrain[terrainTypeIndex]`，默认 `{false,true,true,false,false}`
  （0沙 1草 2泥 3石 4雪，语义与 Part14 定稿一致）。
- 其余拒绝条件：水下（`IsUnderwater`）、怪物巢穴格（`MonsterLairLevel > 0`，可用
  `allowPlantOnLair` 放开）、已被植物占用、阳光不足、该卡冷却中、config 无 prefab。
- 判定顺序：**格规则 → 冷却 → 阳光**。冷却先于阳光，UI 置灰时才能正确提示"冷却中"而非"阳光不足"。

**落位与占位**

- 落位 = `cell.transform.position`（格中心顶面，含海拔与垂直扰动）——与 `HeroController.MoveTo`
  完全同口径；`plantYOffset / plantYaw` 可微调。
- 植物实例挂在 `[Planting]/Plants Container` 下，**不**挂进 chunk（chunk 的
  `HexFeatureManager.container` 每次 Triangulate 都会 `Clear` 重建，挂进去会被销毁）。

**碰撞与射线（踩过的坑）**

- 种植物后统一剥掉植物自带的全部 Collider（`stripPlantColliders`，默认开）：地图编辑
  `HexMapEditor` 与角色 `HeroController` 都用 `Physics.Raycast` 取最近命中，植物一旦有碰撞体
  就会"点 A 格种/走到 B 格"（同 `HexFeatureManager.keepColliders` 的既有坑）。占位 prefab
  建的时候也直接不生成 Collider。

**输入仲裁（对现有系统的最小改动）**

- `HexControlMode` 新增静态位 `plantingActive`；`PlantingInput` 每帧按 `IsPlantingMode` 置位。
- `HeroController` 点击移动条件加 `!HexControlMode.plantingActive`；`HexMapEditor.Update`
  的让出条件加 `|| HexControlMode.plantingActive`（各 1 行，行为在种植模式关闭时完全不变）。

**视觉特征系统不受影响**

- `HexCell.PlantLevel` / `HexFeatureManager.plantPrefabs` 仍是纯装饰（概率摆放、随 chunk 重建），
  与可玩植物单位完全解耦——可玩植物的成败、死亡、占用都不写回这两个字段。

**自愈（防御式设计）**

- `PlantingSystem.PruneStaleEntries()`（每帧，可关）清理失效登记：植物实例被外部 `Destroy`、
  或读档重建地图导致 `HexCell` 对象消失时，占用表会留下 Unity 伪 null 引用；后者会连带销毁
  "所在格已消失"的孤立实例。`ClearPlants()` 额外兜底清空容器内任何未登记的残留实例。

### 1.4 接口依赖（对外 API）

`PlantingSystem`（场景单例 `Instance`，事件均为 C# 事件）

| 成员 | 用途 |
|------|------|
| `int Sun` / `AddSun / TrySpendSun / ResetSun` | 阳光账本（FGUI 阳光计数） |
| `SelectPlant(config)` / `ClearSelection()` / `SelectedPlant` / `IsPlantingMode` | 选卡与种植模式（FGUI 选卡栏） |
| `event SunChanged` / `SelectionChanged` / `CooldownChanged` / `PlantPlaced` / `PlantRemoved` | UI 订阅点 |
| `CheckCell(cell)` / `Validate(cell, config)` / `CanPlant(cell, config)` | 校验（UI 高亮可种植格 / 卡片置灰） |
| `TryPlant(cell, config, out result, out unit)` / `Plant` / `TryPlantSelected` | 种植 |
| `GetCooldownRemaining(config)` / `IsCoolingDown(config)` | 冷却显示 |
| `IsOccupied(cell)` / `GetPlant(cell)` / `RemovePlant` / `RemovePlantAt` / `ClearPlants` | 占用表（FEAT-001 的 `IsWalkable`、FEAT-004 战斗均可复用） |
| `GetCellAtWorld(worldPos)` | 世界坐标 → 格（越界/未建图返回 null，不抛异常） |
| `static Describe(PlantResult)` | 失败原因中文文案 |

`PlantConfig`（ScriptableObject，一张卡）：`displayName / description / icon / sunCost / cooldown /
prefab / maxHealth / attackDamage / attackInterval / attackRange / projectileSpeed`。

`PlantUnit`（游戏侧）：`config / cell / health`、`IsAlive / CellTopWorld / CenterWorld`、
`Initialize(config, cell, system)`。

`CombatSystem.Health`（通用）：`MaxHealth / CurrentHealth / IsDead / Normalized`、
`SetMax / Damage（返回实际扣除）/ Heal / Kill / ResetHealth`、`event Changed / Damaged / Died / Revived`。

### 1.5 装配方式

```
菜单 Tools/种植系统/一键装配 (PvZ 种植)          # 建资产 + 装配场景（场景标记为脏，需自己 Ctrl+S）
菜单 Tools/种植系统/一键装配并保存场景
菜单 Tools/种植系统/清理场景中的植物实例          # 清掉 [Planting]/Plants Container 下的残留
```

产物：

| 资产 | 路径 |
|------|------|
| 植物材质 | `Assets/Resources/Materials/Plants/{Plant_Leaf,Plant_Stem,Plant_Body}.mat` |
| 占位植物 prefab | `Assets/Resources/Prefabs/Plants/{PlantShooter,PlantWall}.prefab` |
| 植物配置 | `Assets/Resources/Configs/Plants/{PlantConfig_Shooter,PlantConfig_Wall}.asset` |
| 场景对象 | `[Planting]`（`PlantingSystem` + `PlantingInput` + 子物体 `Plants Container`） |

占位 prefab 只用于跑通链路，换模型只需把 `PlantConfig.prefab` 换成自己的 prefab（脚本会按需
补挂 `PlantUnit`/`Health`、剥碰撞体、同步 `PlantShooter` 数值），接线不用动。

### 1.6 验证方式

1. **EditMode 断言（已跑通 40/40）**——不依赖 Play，直接对运行时 API 下断言：

   ```bash
   # 项目根 tools/ 下已存放脚本；用 unity-cli 执行
   unity-cli.exe exec --project H:/UnityProjects/CatLike/HexMap --ignore-version-mismatch < tools/planting_editmode_assertions.exec.cs
   ```

   覆盖：草/泥可种 + 沙/石/雪/水下/巢穴/占用/越界拒绝、阳光不足、冷却中（且不扣阳光）、
   落位 = 格中心顶面、Health 按 config 初始化、PlantShooter 数值同步、植物无碰撞体、
   死亡自动注销 + 销毁、外部销毁与地图重建后的 prune 自愈、ClearPlants、仲裁位可用。

   脚本要点（本机 exec 环境的坑）：exec 是**方法体**（不能写 using / class，全限定名）；
   EditMode 下 `Instantiate/AddComponent` 不触发 `Awake`，故测试夹具自带
   `HexGridChunk` 与 `uiRect`，并用反射补 `HexCell.neighbors`，靠 `ResetSun()` 显式给阳光。

2. **手动验证（建议先与用户确认再进 Play）**：装配 → Ctrl+S 保存场景 → Play →
   按 `1`/`2` 选卡（进入种植模式，右/上角无 UI，看 Console 的阳光日志）→ 左键点草/泥格种植；
   `右键`/`Esc` 取消；点石/雪/水下/已占格应打印失败原因。

3. **回归点**：种植模式关闭时角色点击移动与地图编辑行为不变；`.map` 存档字节格式未改
   （本任务不碰 `HexCell.Save/Load`）；地形特征显示不变。

### 1.7 已知边界与后续接线

- **FEAT-004**：`PlantShooter`（`shootingEnabled` 目前 false）+ `Assets/CombatSystem`
  追加 Damageable/Projectile/TargetScanner；`CombatSystem` 目录内必须 grep 不到 `HexGrid/HexCell`。
- **FEAT-005**：FGUI 选卡栏读 `availablePlants`、阳光读 `SunChanged`、卡片置灰读 `CooldownChanged`；
  接好后可关掉 `PlantingInput.numberKeySelect` 或整个组件（`plantingActive` 会在 OnDisable 复位）。
  另：`PlantingSystem.logSunChanges` 是临时日志开关。
- **地图重建**：新地图 / 读档后请调用 `ClearPlants()`（或依赖每帧 prune 自愈）；本任务未把它
  挂进 `NewMapMenu` / `HexMapEditor.Load`（那属于现有系统的改动，留待用户确认）。
- **自然阳光**：默认开（25 / 5s），是"没有 UI 时也能连续试种植"的临时设定，可在 `[Planting]`
  上改成 0 关闭——正式数值由后续玩法设计确定。
- 联机同步、僵尸与阳光的服务器权威未纳入本任务。

### 1.8 变更记录

- 2026-09-11：FEAT-003 种植系统落地。新增 `Assets/CombatSystem/Health.cs`（通用生命值）、
  `Assets/Scripts/Combat/{PlantConfig,PlantUnit,PlantingSystem,PlantingInput,PlantShooter}.cs`、
  `Assets/Editor/PlantingSetupTool.cs`；`HexControlMode` 增加 `plantingActive` 仲裁位，
  `HeroController` / `HexMapEditor` 各加 1 行让出条件。编译零错误零警告；EditMode 断言 40/40 通过。
