# AI 怪物系统（怪物洞穴出怪 + 锁敌追击）

> 方案来源：`docs/fix/pvz3d/fix-plan.md`（任务卡 **FEAT-002 出怪系统**）+ 本次需求追加「AI 锁敌」与「场景配置表」
> 依赖的通用能力：`docs/pathfinding.md`（FEAT-001 六边形寻路：`HexGrid.FindPath` + `HexUnit.Travel`）
> 定位：**玩法层**——复用寻路与通用战斗层，不修改 HexGrid / HexCell / HeroController / EllenAttack / HexMapEditor 任何逻辑。

---

## 1. 目标

1. 怪物 prefab 放在 `Assets/Resources/Prefabs/Monsters/`，从**怪物洞穴**（`HexCell.MonsterLairLevel > 0` 的格）诞生；
2. 诞生**数量与频率**由**场景配置表**决定（原项目没有这张表 → 本任务新增）；
3. 怪物**寻找离它最近的玩家**并沿六边形路径追击；
4. **有距离它更近的玩家攻击它时，转换锁定目标**。

---

## 2. 当前状态

**已完成（2026-09-11）**：编译零错误零警告；EditMode 断言 **105/105 通过**（含就绪闸门 9 条 + 隐藏/禁用语义 3 条）；
场景 `SampleScene` 已由一键装配工具装配好 `[Monsters]` 与玩家侧组件（**待用户 Ctrl+S 保存**）。

| 能力 | 实现 | 状态 |
|------|------|------|
| 怪物单位 / AI 控制器 | `MonsterUnit`（组合 `HexUnit` + `Health`） | ✅ |
| 出生锁最近玩家 | `PlayerTargetRegistry.FindNearest` | ✅ |
| 被更近的玩家攻击 → 换锁 | `MonsterUnit.NotifyAttacked`（受击事件自动触发） | ✅ |
| 目标死亡 / 消失 → 重选最近 | `MonsterUnit.Tick` 目标有效性检查 | ✅ |
| 沿六边形路径追击（目标换格即刻重算） | `MonsterUnit.Chase` → `HexUnit.TravelTo` | ✅ |
| 刷怪：数量 / 频率 / 存活上限 / 波次 / 首只延迟 | `MonsterSpawner` | ✅ |
| 出怪点：巢穴轮流 / 巢穴随机 / 东西边缘 / 指定坐标 | `MonsterSpawner.ResolveSpawnCell` | ✅ |
| 玩家目标抽象（支持多玩家 / 联机远端化身） | `PlayerTarget` + `PlayerTargetRegistry` | ✅ |
| 玩家攻击 → 怪物掉血 + 拉仇恨 | `PlayerMeleeAttackBridge`（订阅 EllenAttack 命中窗口） | ✅ |
| 怪物攻击玩家（可选，默认关） | `MonsterConfig.attackPlayers` | ✅ |
| **场景配置表** + **怪物表**（CSV ↔ 资产管线） | `SceneConfigTable` / `MonsterTable` + `TableImporter` 两条新菜单 | ✅ |
| 一键装配 / 清理 / 状态检查 | `Editor/MonsterSetupTool.cs`（4 个菜单） | ✅ |
| 自动验证 | `tools/monsters_editmode_assertions.exec.cs`（93 条） | ✅ |

---

## 3. 设计要点

### 3.1 分层（沿用 AudioSystem / UnderwaterEffect / 种植系统的「通用模块 + 游戏侧」范式）

| 层 | 文件 | 依赖 |
|----|------|------|
| 通用战斗层 | `Assets/CombatSystem/DamageInfo.cs`（新增）、`Health.cs`（+`Damage(DamageInfo)` 重载与 `DamagedBy` 事件） | 零游戏依赖（grep 不到 `HexGrid/HexCell/Monster`） |
| 游戏侧 AI | `Assets/Scripts/Monsters/MonsterUnit.cs` | 只读 `HexGrid`（寻路）、`Health`、`PlayerTarget*` |
| 游戏侧目标抽象 | `PlayerTarget.cs` / `PlayerTargetRegistry.cs` | 无（静态注册表，`RuntimeInitializeOnLoadMethod` 复位） |
| 游戏侧刷怪 | `MonsterSpawner.cs` | 只读 `HexGrid.Cells / MonsterLairLevel / GetCellByOffset` |
| 游戏侧攻击桥接 | `PlayerMeleeAttackBridge.cs` | 订阅 `EllenAttack` 事件（**不改 EllenAttack**） |
| 配置 | `MonsterConfig` / `MonsterTable` / `SceneConfigTable`（SO） | CSV 由 `Editor/TableImporter.cs` 导入 |
| 装配 | `Assets/Editor/MonsterSetupTool.cs` | Editor 专用 |

对现有文件的改动**只有追加**：
- `CombatSystem/Health.cs`：+`DamagedBy` 事件、+`Damage(DamageInfo)` 重载（`Damage(int)` 行为与返回值完全不变，既有调用方零影响）；
- `Editor/TableImporter.cs`：+2 张表（怪物表 / 场景配置表）的导出与导入菜单，并挂进「全部导出/导入」。

**没有改**：`HeroController`、`EllenAttack`、`HexMapEditor`、`HexGrid`、`HexCell`、`HexUnit`、`PlantingSystem`、特征、联机。

### 3.2 锁敌与换锁规则（需求核心）

`MonsterConfig.lockMode` 两种策略，**都保留**（`enum MonsterLockMode`）：

| 模式 | 语义 |
|------|------|
| `FirstUntilAttacked`（**默认**，= 需求原话） | 出生/失去目标时锁定**当时最近的玩家**；此后不主动改锁，只有①**更近的玩家攻击它**，或②当前目标死亡/不可索敌/被销毁，才重新选目标 |
| `NearestAlways` | 每 `retargetInterval` 秒重算「谁近打谁」（受击换锁规则依旧生效） |

换锁判定（`MonsterUnit.NotifyAttacked(GameObject attacker)`）：

```
attacker 无 PlayerTarget        → 忽略（未知来源不拉仇恨）
attacker == 当前目标            → 不换锁，但立刻重算路径（追得更紧）
当前无目标                      → 直接锁 attacker
switchOnlyWhenCloser = true(默认) → 仅当 attacker 比当前目标更近才换
switchOnlyWhenCloser = false      → 谁打我我就打谁（最后攻击者优先）
```

触发源有两条，**都会落到同一个 `NotifyAttacked`**：
1. **受伤事件**（自动）：`Health.Damage(DamageInfo)` → `DamagedBy` → `MonsterUnit.HandleDamagedBy` → `NotifyAttacked(info.source)`；
2. **显式调用**（无伤害也能拉仇恨）：`PlayerMeleeAttackBridge` 在命中后额外调一次 `monster.NotifyAttacked(attacker)`。

> 为什么需要 (2)：只靠伤害事件的话，「伤害为 0 / 免疫 / 血量组件缺失」的攻击不会拉仇恨；显式调用保证「被攻击就换锁」的语义完整。两条路径幂等（已锁则返回 false）。

### 3.3 追击：复用 FEAT-001，不自己写移动

- 目标格 = `hexGrid.GetCellAtWorld(target.FootWorld)`；**格不可通行时退到 `FindNearestWalkable`**（玩家站在水里/石头上的情形）；
- 重算节流：目标**换格立即重算**；否则每 `repathInterval` 秒一次；到站后仍离得远也立刻重算（防卡住）；
- 到攻击距离（`StopDistance` = `attackRange`）就 `StopTravel` 并面向目标；
- 移动本身、Y 贴地、逐格/到达事件全部由 `HexUnit` 负责（含 EditMode 即时到达语义）。

### 3.4 场景配置表（新增：原项目没有）

一张 `SceneConfigTable` 管所有关卡，行 = 一条刷怪规则，键 = **场景名**（`scene` 为空表示任意场景）：

| 列 | 语义 |
|----|------|
| `scene` | 生效场景名（`SceneManager.GetActiveScene().name`；空 = 任意场景） |
| `monster` | 怪物 id（引用怪物表 → `MonsterConfig_{id}.asset`） |
| `count` | **总诞生数量** |
| `interval` | **诞生频率**：每隔多少秒生成一只 |
| `maxAlive` | 同时存活上限（0 = 不限） |
| `startDelay` | 首只延迟（秒） |
| `waves` | 分几波（≤1 = 不分波，连续刷完 count） |
| `waveInterval` | 波与波的间隔（秒） |
| `waitForClear` | `1` = 等本波全清再倒计时下一波；`0` = 固定 interval 到点就下一波（**两种都保留**） |
| `spawnPoint` | `LairRoundRobin` / `LairRandom` / `WestEdge` / `EastEdge` / `OffsetCoordinates` |
| `offsetX / offsetZ` | `OffsetCoordinates` 模式的坐标 |
| `enabled` / `order` | 启用 / 顺序 |

`waitForClear` 有**卡死保护**：`MonsterSpawner.waveStallTimeout`（默认 90s）到点强制进下一波并告警（怪物杀不掉时不会永远卡住）。

### 3.5 出生点解析（怪物洞穴）与**就绪闸门**

1. 遍历 `HexGrid.Cells` 收集 `MonsterLairLevel > 0` 的格（缓存，`InvalidateSpawnCells()` 可失效）；
2. `LairRoundRobin` 按行内游标轮流、`LairRandom` 随机挑；
3. **没有巢穴时回退地图西边缘**，并**打一次告警**（每个配置行一次），提示检查地图/巢穴/`spawnPoint`。

**就绪闸门（避免"怪从错误的地方静默冒出来"）**：地图与巢穴数据可能是**运行时读档 / 后建**才到位的，
所以刷怪前先过 `ReadyToSpawn`：

| 字段 | 默认 | 语义 |
|------|------|------|
| `waitForMapReady` | `true` | `HexGrid` 未建图 / `Cells` 为空时不刷，等建图或读档完成 |
| `mapWaitTimeout` | `30` | 等待地图就绪的超时；超时**只告警一次**且继续等待（不会在空地图上瞎刷） |
| `requireLairs` | `true` | 出生点模式为 `Lair*` 时，巢穴扫到 0 格就先等（不立刻回退西边缘） |
| `lairWaitTimeout` | `15` | 等待巢穴的超时；超时后**放行 → 回退西边缘并告警**（保证不会永久卡住不刷） |

`InvalidateSpawnCells()` 在读档 / 新建地图后调用即可让闸门重新评估（缓存为空的不会被锁死）。

### 3.5.1 出生点可达性（★ 怪物"不追人"的常见原因）

`FindPath` 是**真的 A\* 无路径**时怪物会原地待机 —— 这**不是追踪逻辑坏了**，而是地图把出生点和玩家隔开了。
本项目的默认通行设置是 `blockUnderwater=true` + `blockCliffs=true`（悬崖 = 相邻落差 ≥ 2 不可通行），
而噪声生成的地形很容易出现"整片台地被一圈悬崖/水封死"的情况，于是：

```
玩家（海拔 3 的西北高地）  ←水 + 2 级落差→  怪物巢穴（海拔 1 的东部低地）   ✘ A* 无路径
```

| 字段 | 默认 | 语义 |
|------|------|------|
| `requireReachableSpawn` | `true` | 出生点必须**能走到至少一个玩家**（A* 实际可达，不是直线距离）。开启时优先挑可达的巢穴；全都不可达 → **告警一次**并照常刷（不偷偷换地点，把问题暴露给关卡） |

**注意「场上没有可索敌玩家」时不拦**（还没玩家 / 玩家 `targetable=false`）：`CanReachAnyPlayer` 返回 `true`，
否则会出现"玩家没出生 → 不刷怪 → 死锁"。

**诊断入口**：菜单 `Tools/怪物系统/诊断出生点可达性`（只读，不改场景），输出示例：

```
玩家目标：登记 1 个
   - Ellen  可索敌=True  所在格=(0,0,0)  世界=(0,35,0)
怪物巢穴：2 个（MonsterLairLevel > 0）
   - (18,-18,0) 海拔=1 水下=False   ✘ 走不到玩家（A* 无路径）
HexGrid 通行设置：blockUnderwater=True  blockCliffs=True  walkableTerrain=[True,True,True,True,True]
⚠ 巢穴全都走不到玩家 → 怪物刷出来会原地不动。
```

**处理办法（任选，按代价从低到高）**：

1. **关掉悬崖阻断**：`HexGrid.blockCliffs = false`（允许翻越 2 级落差，成本 10）——噪声地图通常立刻连通。
   本项目实测：同一张图 `blockCliffs=true` → 9 个互不连通区；关掉 → **1 个连通区（187/300 格，其余是水）**。
   代价：单位会"爬"陡壁（Catlike 教程的原始语义就是这样，悬崖只是更贵）。
2. **把怪物巢穴画在玩家可达的陆地上**（Lair 滑条 / 特征编辑器）。
3. **改地形**：削掉 2 级落差、或在断口处填一格水为陆地，做出通路。
4. 若就是不想让怪物追人：把 `requireReachableSpawn` 关掉，明确接受"怪物不追人"。

> 结论：**"怪物不追人"先查可达性，不要先怀疑锁敌逻辑**。锁敌（`target=Ellen`）与追击（A* 路径）是两段，
> 前者好了但后者无路时会表现为"锁了目标却站着不动"。

### 3.5.2 无路可走时的告警（不再静默）

- `MonsterUnit` 走不到目标时打**一条**告警（每个怪物一次，换目标后重置），把成因（blockUnderwater / blockCliffs、
  自身格与目标格的海拔）和处理办法一起写出来 —— 以前这里是静默 `return`，所以"怪物不动"完全没有线索。
- `MonsterSpawner` 在巢穴全部不可达时也打一条（每个配置行一次，`StartSpawning` 时重置）。

### 3.6 玩家目标的「隐藏 / 禁用」语义（两处开关）

本项目惯用「隐藏 / 禁用对象」做隔离（隐藏旧相机、旧角色）。因此**默认不把隐藏当成离场**：

| 字段 | 默认 | 语义 |
|------|------|------|
| `targetable` | `true` | 总开关；关掉 = 怪物忽略该玩家 |
| `requireActive` | `false` | **默认不要求** `active && enabled`：对象被隐藏 / 组件被禁用**仍可被索敌**（否则隐藏 Ellen 会让怪直接待机）。打开 = 严格语义（隐藏 = 不可索敌） |
| `unregisterWhenDisabled` | `false` | 禁用 / 隐藏时**保留**索敌登记；打开才从索敌表注销 |
| `untargetableWhenDead` | `true` | 血量归零退出索敌（不受上面两项影响） |

> 注意：若对象在场景里就是 **inactive 启动**的，Unity 不会跑 `Awake/OnEnable` → 需要外部（联机化身生成、
> UI 显示时）调用 `ManualRegister()`。`ManualRegister/ManualUnregister` 幂等，Play 下也可安全调用。

### 3.7 碰撞体 / 射线（踩过的坑）

- 怪物 prefab **保留 Collider**（玩家攻击判定 `OverlapSphere` 要靠它命中）；
- 攻击判定用「**组件过滤**」（`GetComponentInParent<MonsterUnit>()`）而不是 Layer / Tag —— 不需要改 `TagManager`，也不与 `HexMapEditor` / `HeroController` 的 `Physics.Raycast` 抢语义（怪物身上的射线命中点 XZ 仍落在同一格）；
- 若某个怪物确实要「完全不参与射线」，把 `MonsterConfig.stripColliders` 打开即可（代价：玩家打不到它）。

### 3.8 玩家目标抽象（为什么要新建一层）

项目此前**没有统一的「玩家」概念**：单机是场景里的 `Role/Ellen`，联机远端是 `EllenNet` 化身，且远端化身被禁用了 `Character`。
`PlayerTarget` 把「玩家」从实现里抽出来 —— 挂上它 = 可被索敌；`PlayerTargetRegistry` 是静态注册表（支持 0..N 个目标）：

- `FindNearest(pos, maxRange)` → 最近的可索敌玩家（单机只有一个时行为退化为「永远锁唯一玩家」）；
- `FindByObject(go)` → 由攻击者 GameObject 反查所属玩家（`GetComponentInParent`，子物体也能反查到）；
- `IsTargetable` = 开关 ∧ 激活 ∧ 未死亡（`untargetableWhenDead`）；
- `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` → 进 Play / 域重载自动清空，静态状态不跨运行残留。

> **EditMode 注意**：编辑器非运行态下 `AddComponent` **不触发** `OnEnable` → 断言脚本 / 手工装配需显式调 `ManualRegister()`（幂等）。

### 3.9 玩家攻击链路（本项目原本没有伤害判定）

`EllenAttack` 只播动画 + 广播命中窗口事件，**没有任何伤害判定**。`PlayerMeleeAttackBridge` 是**新增独立组件**（不改 EllenAttack）：

```
EllenAttack.MeleeAttackWindowStart（AnimationEvent）
   → Physics.OverlapSphereNonAlloc（半径 radius，可选扇形 minDot）
   → Health.Damage(DamageInfo.From(damage, 玩家, 命中点, "player-melee"))
   → MonsterUnit.NotifyAttacked(玩家)   ← 更近的玩家攻击 → 换锁
```

`useHitWindowEvent=false` 时改用 `AttackTriggered`（按键瞬间判定）；两种都保留。
没有 `EllenAttack` 也能用：`bridge.PerformAttack()` 是公开方法。

---

## 4. 接口依赖（对外 API）

### 通用层 `CombatSystem`
| 成员 | 用途 |
|------|------|
| `struct DamageInfo { int amount; GameObject source; Vector3 point; string kind; }` | 伤害 + 来源 + 命中点 |
| `DamageInfo.From(amount, source, point, kind)` / `Simple(amount)` / `HasSource` | 构造与判定 |
| `Health.Damage(DamageInfo)` | 带来源受伤（返回实际扣除量） |
| `event Action<Health, DamageInfo, int, int> DamagedBy` | （自身, 伤害信息, 实际扣除, 剩余）→ AI 仇恨 / 伤害归属 |

### `MonsterUnit`（挂怪物 prefab）
| 成员 | 用途 |
|------|------|
| `Initialize(config, spawnCell, spawner)` | 注入配置 / 落位 / 初始化血量与移速 / 出生即锁最近玩家 |
| `Tick(float now)` | 单次 AI 决策（`Update` 在 Play 每帧调用；断言脚本手动驱动） |
| `AcquireNearestTarget()` / `SetTarget(t, force)` / `ClearTarget()` | 目标控制 |
| `NotifyAttacked(GameObject)` | **被攻击**：按「更近者优先」换锁，返回是否换锁 |
| `ResolveTargetCell(t)` / `Chase(cell, now)` / `Attack(t)` | 追击与攻击（可单独调用） |
| `currentTarget` / `Cell` / `IsAlive` / `IsMoving` / `DistanceToTarget` | 只读状态 |
| `event TargetChanged(unit, prev, now)` / `Died(unit)` | 目标变化 / 死亡 |
| `sourceRowIndex` | 由哪条刷怪行产生（刷怪器分组统计用） |

### `PlayerTarget` / `PlayerTargetRegistry`
`PlayerTarget`：`playerId` / `targetable` / `aimPoint` / `aimHeight` / `health` / `untargetableWhenDead` /
`IsTargetable` / `AimWorld` / `FootWorld` / `ManualRegister()` / `ManualUnregister()`

`PlayerTargetRegistry`：`Register` / `Unregister` / `Clear` / `Prune` / `Count` / `CountValid` /
`All` / `FindNearest(pos, maxRange)` / `FindByObject(go)` / `GetTargets(list)`

### `MonsterSpawner`（挂 `[Monsters]`）
| 成员 | 用途 |
|------|------|
| `StartSpawning()` / `StopSpawning(clear)` / `Tick(now)` / `BuildRows()` | 启动 / 停止 / 驱动 / 重载配置行 |
| `SpawnOne()` / `SpawnOneForRow(i)` / `SpawnAtCell(i, cell)` | 手动刷怪（调试 / 断言） |
| `isRunning` / `spawnedCount` / `AliveCount` / `TotalToSpawn` / `RemainingToSpawn` / `RowCount` / `FinishedRowCount` / `AliveMonsters` | 状态（UI 数据源） |
| `GetRowEntry(i)` / `GetRowSpawnedTotal(i)` / `GetRowWaveIndex(i)` / `IsRowDone(i)` | 行级查询 |
| `GetLairCells()` / `GetEdgeCells(west)` / `ResolveSpawnCellForRow(i)` / `InvalidateSpawnCells()` | 出生点 |
| `ClearMonsters(destroy)` / `Prune()` / `NotifyMonsterDied(unit)` / `NotifyMonsterRemoved(unit)` | 清理与回执 |
| `event Spawned(unit)` / `MonsterRemoved(unit)` / `WaveStarted(wave, total)` / `AllSpawned()` / `Cleared()` | 给 UI / 胜负判定订阅 |
| 参数 | `sceneNameOverride` / `autoStart` / `waveStallTimeout` / `logSpawns` / `pruneStaleEachFrame` |

### `PlayerMeleeAttackBridge`（挂玩家）
`attack` / `owner` / `damage` / `radius` / `minDot` / `centerHeight` / `damageKind` / `useHitWindowEvent`；
`PerformAttack()` → 命中数；`event HitMonster(桥, 怪物, 实际伤害)` / `AttackPerformed(桥, 命中数)`；`attackCount` / `hitCount`。

### `MonsterConfig`（SO，一行 = 一种怪物）
`displayName / description / icon / prefab / maxHealth / moveSpeed / turnSpeed / yOffset /
lockMode / detectRange / retargetInterval / switchOnlyWhenCloser / switchOnlyWithinDetectRange /
repathInterval / arriveDistance / attackRange / attackPlayers / attackDamage / attackInterval /
stripColliders / corpseLifetime`；`IsValid` / `DisplayName` / `StopDistance`。

### `SceneConfigTable` / `MonsterTable`（SO）
`SceneConfigTable`：`entries[]` / `Find(id)` / `GetOrCreate(id)` / `GetForScene(scene)` / `GetUsableForScene(scene)` /
`Count` / `static Load()` / `static ActiveSceneName()`。
`MonsterSceneConfigEntry`：上述 CSV 列的字段版 + `IsUsable` / `MatchesScene(name)` / `PerWaveCount`。

`MonsterTable`：`monsters[]`（`id` / `config` / `unlocked` / `order`）/ `Find` / `FindConfig` / `OrderedEntries` /
`GetUnlockedConfigs` / `static Load()` / `static LoadConfigById(id)`。

---

## 5. 装配方式

```
菜单 Tools/怪物系统/一键装配 (AI 怪物)             # 建资产 + 装配场景（场景标记为脏，需自己 Ctrl+S）
菜单 Tools/怪物系统/一键装配并保存场景
菜单 Tools/怪物系统/清理场景中的怪物实例
菜单 Tools/怪物系统/检查怪物系统装配状态
```

产物：

| 资产 | 路径 |
|------|------|
| 怪物 prefab | `Assets/Resources/Prefabs/Monsters/Zombie1.prefab`（**复用你已有的 prefab**，只补挂 `MonsterUnit`，模型/碰撞体原样保留；缺则建 `MonsterPlaceholder.prefab`） |
| 怪物配置 | `Assets/Resources/Configs/Monsters/MonsterConfig_Zombie.asset` |
| 怪物表 | `Assets/Resources/Configs/Tables/MonsterTable.asset` + `.csv` |
| 场景配置表 | `Assets/Resources/Configs/Tables/SceneConfigTable.asset` + `.csv` |
| 场景对象 | `[Monsters]`（`MonsterSpawner` + 子物体 `Monsters Container`） |
| 玩家侧 | `Role/Ellen` 上新增 `PlayerTarget` + `PlayerMeleeAttackBridge`（+ `Health` 100，供「怪物攻击玩家」开关；不需要可移除） |

**改数量/频率**：编辑 `Assets/Resources/Configs/Tables/SceneConfigTable.csv`（Excel 可直接打开，UTF-8 BOM）
→ 菜单 `Tools/UI/数据表/导入 场景配置表 CSV`（或「全部导入」）→ Play。
**改怪物数值**：编辑 `MonsterTable.csv` → `Tools/UI/数据表/导入 怪物表 CSV`。

---

## 6. 验证方式

### 6.1 EditMode 断言（已跑通 105/105）

```bash
unity-cli.exe exec --project H:/UnityProjects/CatLike/HexMap --ignore-version-mismatch \
  < tools/monsters_editmode_assertions.exec.cs
```

覆盖：资源与 prefab 接线、表语义（场景匹配 / 排序 / 波次拆分 / 可用性 / 稳定 upsert）、
玩家注册表（最近查询 / 范围 / 死亡退出 / 攻击者反查）、
AI（出生锁最近 / **更近者攻击换锁** / **更远者攻击不换锁** / `switchOnlyWhenCloser=false` 谁打谁 / 未知来源忽略 /
目标死亡重选 / 索敌范围为空则待机 / 追击落格 / 目标格不可通行退到最近可通行格）、
怪物攻击玩家（开关 / 扣血 / 攻击间隔 / 关掉不扣血）、
刷怪器（首只延迟 / 频率 / 数量上限 / 存活上限 / 死亡腾位 / 清理 / 全清）、波次（分波 / `waitForClear` 等清场 / 波事件 / AllSpawned / Cleared）、
出生点（巢穴轮询 / 指定坐标 / 无巢穴回退西边缘 / 缓存失效）、
攻击桥接（范围命中 / 扣血 / 拉仇恨 / 范围外不命中 / 尸体不死）、
`.map` 存档字节数（11B/格）未变。

### 6.2 手动验证（建议先与用户确认再进 Play）

1. 装配 → **Ctrl+S 保存场景**；
2. 让地图上有草/泥等**可通行地形** + 至少一个**怪物巢穴**（菜单 `Tools/地形特征/装配怪物巢穴` 已有 Lair 开关/滑条，画 Lair Level ≥ 1 的格）；
3. Play → 怪物按 `SceneConfigTable.csv` 的数量/频率从巢穴诞生 → 沿六边形路径追向玩家；
4. 左键攻击怪物（走 `EllenAttack` 的命中窗口）→ 怪物掉血；多人联机时用另一个玩家靠近攻击 → 怪物**改锁更近的那个**。

---

## 7. 已知边界与后续接线

- **寻路可达性**：怪物走 `HexGrid.IsWalkable`（默认**全部陆地可通行**）。如果之前手动切过「仅草/泥可行走」预设，
  怪物也只能走草/泥 —— 用菜单 `Tools/寻路系统/恢复默认通行预设` 或 `检查怪物系统装配状态` 的告警确认。
- **怪物之间不互相避让**：没有局部避障（会重叠）。需要时给寻路注册 `AddWalkabilityFilter`（用一个共享的「被怪物占住」集合）。
- **攻击不作用于植物**：怪物只打玩家；「僵尸啃植物」属于 FEAT-004（战斗系统），接线点是 `PlantingSystem.IsOccupied` + `HexUnit.EnteredCell`。
- **胜负判定未做**：刷怪器只广播 `AllSpawned` / `Cleared` 事件，真正的胜/负 UI 与规则属 FEAT-004 / FEAT-005。
- **联机未同步**：`MonsterSpawner` 是房主本地生成 + 本地 AI；Mirror 同步（生成 / 位置 / 目标）未纳入本任务。
  目标抽象层已经为它留好位置（远端化身挂 `PlayerTarget` 即可被索敌）。
- **玩家死亡无处理**：玩家 `Health` 归零后 `PlayerTarget` 自动退出索敌（怪物待机），没有复活 / 失败流程。
- **`MonsterConfig` 挂了但未接线**：`prefab` 为空的行会被 `BuildRows` 跳过并打警告（不静默失败）。

---

## 8. 变更记录

- 2026-09-11：**AI 怪物系统落地（FEAT-002 出怪 + AI 锁敌 + 场景配置表）**。
  - 新增通用层：`Assets/CombatSystem/DamageInfo.cs`；`Health.cs` 追加 `Damage(DamageInfo)` 重载与 `DamagedBy` 事件（旧 API 行为不变）。
  - 新增游戏侧：`Assets/Scripts/Monsters/{PlayerTarget, PlayerTargetRegistry, MonsterConfig, MonsterTable, SceneConfigTable, MonsterUnit, MonsterSpawner, PlayerMeleeAttackBridge}.cs`。
  - 新增 Editor：`Assets/Editor/MonsterSetupTool.cs`（4 个菜单）；`Assets/Editor/TableImporter.cs` 追加「怪物表」「场景配置表」CSV 导出/导入（并入全部导出/导入）。
  - 新增数据：`Resources/Configs/Monsters/MonsterConfig_Zombie.asset`、`Resources/Configs/Tables/{MonsterTable,SceneConfigTable}.{asset,csv}`。
  - 装配：`SampleScene` 新增 `[Monsters]`（MonsterSpawner）；`Role/Ellen` 新增 `PlayerTarget` + `PlayerMeleeAttackBridge` + `Health`（待用户 Ctrl+S）。
  - 验证：编译零错误零警告；EditMode 断言 **105/105**；`.map` 存档 11B/格未变；未改 `HeroController / EllenAttack / HexMapEditor / HexGrid / HexCell / HexUnit` 任何逻辑。
- 2026-09-11 **稳健性补强（Play 实测后）**：
  - **刷怪就绪闸门**：`waitForMapReady / mapWaitTimeout / requireLairs / lairWaitTimeout` —— 地图或巢穴数据是运行时读档到位时不再静默回退西边缘，而是等待（超时才回退并**告警一次**）。触发场景：Play 实测中发现怪全部出现在西边缘、与地图上的 2 个巢穴不符。
  - **`PlayerTarget` 的隐藏/禁用语义开关**：`requireActive`（默认 `false`，隐藏仍可被索敌）+ `unregisterWhenDisabled`（默认 `false`，隐藏不注销登记）。触发场景：Play 实测时 `Role/Ellen` 处于 inactive（本项目惯用的隐藏隔离手段）→ 怪物全部 `target=NONE` 待机。
  - 断言 93 → **105**（新增 11.x 就绪闸门 6 条 + 11.7~11.9 地图就绪 3 条 + 2.10~2.12 隐藏语义 3 条）；`pathfinding` 80/80、`planting` 40/40 回归全绿。

### 8.1 验证方法与一个坑（写给未来的自己）

本项目的"测试"就是 `unity-cli exec` 跑 EditMode 断言套件，**必须在 Unity 非 Play 状态**下跑：

- Play 模式下 `editor refresh --compile` 会被 Unity 拒绝（`rc=1` 但 Console 无错误 → 别误判为编译失败）；
- 断言 fixture 用 `new GameObject + AddComponent<HexGrid>()`，**Play 下会真跑 `Awake → CreateMap → Instantiate(chunkPrefab=null)` 抛异常**，
  且 fixture 的 `HexGrid`（`noiseSource=null`）会把静态 `HexMetrics.noiseSource` 清掉 → 后续 `HexCell.Elevation` 连锁 NRE、
  `Destroy` 变成延迟销毁导致"实例已销毁"类断言全体失败。**这些红不是代码回归**，先确认 `unity-cli status` 是否 `playing`。

- 2026-09-11 **修复「怪物创建后没有跟踪场景中的角色」**：真因是**地图被隔断**（玩家在海拔 3 的西北高地，巢穴在海拔 1 的东部低地，
  中间是水 + 2 级落差；`blockCliffs=true` 下 A* 无路径 → 怪物锁定了目标却只能原地站着，且**全程静默无提示**）。
  新增：`MonsterSpawner.requireReachableSpawn`（默认开，出生点必须 A* 可达玩家；优先挑可达巢穴，全不可达则告警一次）、
  `CanReachAnyPlayer` / `GetReachableLairCells` / `HasTargetablePlayer` / `DiagnoseSpawnReachability`、
  菜单 `Tools/怪物系统/诊断出生点可达性`；`MonsterUnit` 无路径时告警一次（含成因与处理办法，换目标后重置）。
  断言 105 → **117**（13.1~13.12 可达性）。地图连通性实测：`blockCliffs=true` → 9 区；`false` → 1 区（187/300）。
