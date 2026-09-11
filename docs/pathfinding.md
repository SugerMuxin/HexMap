# 六边形寻路（FEAT-001）

> 方案文档：`docs/fix/pvz3d/fix-plan.md`（任务卡 FEAT-001）
> 教程来源：Catlike Coding HexMap Part 15/16/17/19（本地中文翻译：`F:\A-Ruosa\PersonalNotes\Teach\Cat\HexMap\`）
> 定位：**地图侧通用能力层**——不绑定任何玩法。PvZ 出怪（FEAT-002）、未来的 AI 怪物、乃至让角色走 A* 路径，都复用同一套 API。

---

## 1. 目标

在已完成的六边形地图（`HexGrid` / `HexCell` / `HexCoordinates`）上补上"**从 A 格走到 B 格**"的基础能力：

1. 六边形距离（cube 坐标距离）；
2. A* 最短路径搜索（带移动成本、可绕障碍、可扩展通行规则）；
3. 沿路径的实时平滑移动（贝塞尔曲线 + 朝向 + 逐格事件）；
4. 场景里的可视化与手动验证入口，以及一键装配工具。

为 FEAT-002（出怪 / 僵尸单位）、FEAT-004（战斗：僵尸走到植物格）与后续 AI 提供 `FindPath` + `Travel` 两个原语。

---

## 2. 当前状态

**已完成（2026-09-11）**：编译零错误零警告；EditMode 断言 **80/80 通过**；场景 `SampleScene` 已由一键装配工具装配好 `[Pathfinding]`（待用户 Ctrl+S 保存）。

| 能力 | 实现 | 状态 |
|------|------|------|
| 六边形距离 | `HexCoordinates.DistanceTo` | ✅ |
| 优先级队列 | `HexCellPriorityQueue`（分桶 + 同优先级链表） | ✅ |
| A* 搜索 | `HexGrid.FindPath` / `GetPath` / `ClearPath` | ✅ |
| **高度规则（两种模式）** | `pathMode`：**仅同高度**（默认，不攀爬/不跳跃）/ 允许坡道（落差 1；悬崖仍阻断） | ✅ |
| 移动成本 | 平地 5 / 坡道 10（仅 AllowSlope 模式）/ 悬崖阻断 | ✅ |
| 通行规则 | `walkableTerrain` + 水下 / 悬崖开关 + **可注册过滤器**（AI/玩法挂钩） | ✅ |
| 全场距离场 | `HexGrid.ComputeDistanceField`（Dijkstra 式，AI 流场） | ✅ |
| 沿路径移动 | `HexUnit`（实时曲线移动 + 朝向 + 事件） | ✅ |
| 场景演示 / 手动验证 | `PathfindingDemo`（自动走一趟 + 中键点地面 + 路径线） | ✅ |
| 一键装配 | `Editor/PathfindingSetupTool.cs`（6 个菜单） | ✅ |
| 自动验证 | `tools/pathfinding_editmode_assertions.exec.cs`（80 条） | ✅ |

---

## 3. 设计要点

### 3.1 分层

| 文件 | 职责 | 依赖 |
|------|------|------|
| `Scripts/HexCoordinates.cs`（+`DistanceTo`） | 纯坐标数学：两格之间的 hex 步数 | 无 |
| `Scripts/HexCell.cs`（+ 搜索临时数据区） | 每格的搜索状态（`Distance` / `PathFrom` / `SearchHeuristic` / `NextWithSamePriority`）+ `GetNeighborSafe` | 无 |
| `Scripts/HexCellPriorityQueue.cs` | A* 开放集：按优先级分桶 + 同优先级链表，摊销近 O(1)、零额外分配 | `HexCell` |
| `Scripts/HexGrid.cs`（+ Pathfinding 区） | 通行性判定、A* 搜索、路径回溯、距离场、最近可通行格、世界坐标反查 | 上面三者 |
| `Scripts/Bezier.cs` | 二次贝塞尔取点 / 切线（纯静态，可跨项目复用） | 无 |
| `Scripts/HexUnit.cs` | 寻路的**执行端**：沿路径实时平滑移动、逐格事件、到达事件 | `HexGrid` + `Bezier` |
| `Scripts/PathfindingDemo.cs` | 场景装配件：起终点解析、路径线、中键手动寻路 | `HexGrid` + `HexUnit` |
| `Editor/PathfindingSetupTool.cs` | 一键建占位资产 + 场景接线 | Editor 专用 |

对现有文件的改动**只有追加**：`HexCoordinates` +1 方法、`HexCell` +1 方法 +1 个搜索数据区、`HexGrid` +1 个 region（另 `GetCell(HexCoordinates)` 补了 1 行 `cells == null` 空保护、`CreateMap`/`Load` 末尾各 +1 行 `ClearPath()`）。**没有改** `HeroController` / `HexMapEditor` / 特征 / 联机 / 种植的任何逻辑。

### 3.2 高度规则：两种寻路模式（**默认只能走相同高度**）

地图是分层的：`HexCell.Elevation` 是整数层级（世界里每级 = `HexMetrics.elevationStep` = 5 单位，级与级之间还有梯田过渡）。
相邻两格的关系由高度差决定：

| 高度差 | 边类型 | 意义 |
|--------|--------|------|
| 0 | `Flat` | 平地（同一层） |
| 1 | `Slope` | 坡道（上一级/下一级） |
| ≥ 2 | `Cliff` | 悬崖（要跨过去 = **跳跃**） |

`HexGrid.pathMode` 提供**两种模式**，运行时/编辑器里都能切：

| 模式 | 规则 | 适用 |
|------|------|------|
| **`SameElevationOnly`（默认）** | **只有高度相同的格之间可以走**；任何高度差（坡道、悬崖，向上或向下）一律不可通行 → **不支持攀爬，也不支持跳跃**。单位被限制在同一个海拔层（连通的高原/平地）里活动。 | 地面怪物（AI 怪物默认行为） |
| `AllowSlope` | 平地可走（成本 5）+ **坡道可走**（落差 1，成本 10）；落差 ≥ 2 的悬崖在 `blockCliffs`（默认开）下仍不可通行。 | 会爬坡的单位；想放开"跳跃"需再显式关掉 `blockCliffs`（按需求默认不支持） |

```csharp
grid.pathMode = HexPathMode.SameElevationOnly;   // 只走同高度（默认）
grid.pathMode = HexPathMode.AllowSlope;          // 允许坡道
// 运行时（Play）按 H 键切换；编辑器菜单：Tools/寻路系统/切换为「仅同高度」寻路 / 「允许坡道」寻路
```

单位可以**自带模式**（地面怪 vs 飞行怪各走各的）：
`HexUnit.overrideGridPathMode = true` + `HexUnit.pathMode`；不勾则跟随地图 `HexGrid.pathMode`（`EffectivePathMode()`）。
按模式寻路的 API 都不改地图配置：`FindPath(from, to, mode)` / `FindPathList(from, to, mode)` / `ComputeDistanceField(origin, mode)`。

**走不通时会说清原因**：`HexGrid.DescribePathFailure(from, to)` 返回中文原因（如"起点与终点不在同一高度（0 vs 1）——当前为「仅同高度」寻路，不支持攀爬/跳跃"）；
`HexUnit` 把原因写进 `noPathReason`，`PathfindingDemo` 则打印出来。

**兜底取格也按层**：`FindNearestWalkable(nearTo)` 在严格模式下只在 `nearTo` 同高度的格里找；
要精确指定层用 `FindNearestWalkableAtElevation(nearTo, elevation)`（AI 常用：目标格被占/在别的海拔 → 改去"同一层里最近的可走格"）。
演示件的终点解析（东边缘 / 随机 / 坐标）在严格模式下也只在单位当前高度层里挑，避免"给一个根本走不到的终点"。

### 3.2b 地形 / 水下 / 过滤器规则（与高度规则并行）

判定分两层，都在 `HexGrid` 上：

- **格级** `IsWalkable(cell)`：非空 → 地形可通行（`walkableTerrain[terrainTypeIndex]`）→ 非水下（`blockUnderwater`）→ 通过**所有注册的过滤器**。
- **边级** `TryGetMoveCost(from, neighbor, dir, mode, out cost)`：目标格可通行 + **高度规则（§3.2）** + 移动成本。

```csharp
// 玩法层想加规则（如"被单位占住的格不可走"）只需注册一个过滤器，不必改 HexGrid：
grid.AddWalkabilityFilter(cell => !myOccupiedSet.Contains(cell));
```

**默认 = 全部陆地可通行**（沙/草/泥/石/雪，水下与悬崖不可通行）——寻路是通用能力，不该内建某一玩法的地形偏好。
fix-plan §2.3.1 写的 PvZ 简化（**仅草/泥可行走**）改成了**预设**，两种规则都保留、随时可切：

```csharp
grid.ApplyPvZWalkablePreset();      // {false,true,true,false,false} 仅草/泥
grid.ApplyDefaultWalkablePreset();  // {true,true,true,true,true} 全陆地
// 也可菜单：Tools/寻路系统/应用 PvZ 通行预设（仅草/泥可行走） / 恢复默认通行预设（全部陆地）
```

> ⚠️ **坑**：新建地图（`HexGrid.CreateMap`）的每格 `terrainTypeIndex` 默认是 **0（沙）**。切到"仅草/泥"预设后，
> 未刷过地形的空地图会**一格都不可通行**（`FindPath` 直接失败、演示单位原地不动）——先用地形编辑器刷出草(1)/泥(2)，
> 或载入带草/泥的 `.map`。默认预设（全陆地）没有这个问题。
>
> ⚠️ **另一个坑**：地形默认 `Elevation = 0`（整张图同一层），所以"仅同高度"在未雕刻高度的地图上等于"哪里都能去"；
> 一旦你刷了高度（出现高原/坡道/悬崖），怪物就**只在它出生那一层活动**了——这正是本模式想要的行为，
> 但如果发现"怪物站在起点不动"，先用 `DescribePathFailure` 看是不是终点在别的海拔上。

### 3.3 成本、启发式与最优性

- 成本 = 每步移动成本累加（平地 5、坡道 10、悬崖阻断）。
- 启发式 = `coordinates.DistanceTo(目标) × 生效权重`；`EffectiveHeuristicWeight()` 会把权重 **clamp 到 ≤ 最小移动成本**（默认 5），因此启发式始终可采纳（admissible）→ **保证最短路径**，同时比 Dijkstra 少展开大量格子。
  （断言 9.1/9.2：`heuristicWeight = 9999` 时生效值仍为 5，路径成本与 Dijkstra 完全一致。）

### 3.4 实时化改编（相对教程）

| 教程（回合制） | 本项目（实时） |
|----------------|----------------|
| `speed` 参数 = 每回合移动预算，跨回合时"丢失移动" | 删除回合概念，`FindPath` 只累加移动成本 |
| 协程逐帧推进搜索（可视化用） | **同步搜索**一次算完（单帧内出结果），怪物拿到 `List<HexCell>` 直接开走 |
| 逻辑位置立即到达目的地，动画纯表演 | `HexUnit.location` **随行进逐格更新**，并广播 `EnteredCell`（僵尸啃植物 / 踏入基地 / 触发陷阱都要靠它） |
| 曲线插值包含 Y，会穿插地形 | 新增 `followTerrainHeight`（默认开）：每帧把 Y 对齐脚下格顶面，跨海拔不沉不浮 |
| 教程里坡道/悬崖只是成本差异 | 新增 `HexPathMode` 两种模式，默认**只走相同高度**（不支持攀爬/跳跃） |

**EditMode 语义**：编辑器非运行态下协程不推进，所以 `HexUnit.Travel()` 会**直接落到终点并广播 `TravelStarted` / `TravelFinished`**（`EnteredCell` 不触发）——这让断言脚本不进 Play 就能验证"寻路 → 移动 → 到达"整条链路。

### 3.5 序列化 / 存档安全（验收项）

搜索数据全部是 **`HexCell` 的私有字段 + 公开属性**：Unity 只序列化公开字段，所以它们既不进 `.map`、也不进场景/prefab；`HexCell.Save/Load` 一个字段都没动。断言 12.2/12.3 用 `MemoryStream` 实测：搜索前后单格存档字节均为 **11 字节且完全一致**。

### 3.6 与既有系统的关系

- **不改** `HeroController`（仍是点击直线移动）——项目约定"新功能用独立新组件，改旧系统要先确认"。角色若以后要改走路径，只需在 `HeroController` 里换用 `hexGrid.FindPath` + `HexUnit` 的移动（本次未做）。
- **不占用鼠标左键**：演示用手动入口是**鼠标中键**（左键已被角色点击 / 地图编辑 / 种植占用），因此**不需要**给 `HexControlMode` 加新的仲裁位。
- **占位单位无 Collider**（同 `HexFeatureManager.keepColliders` 的既有坑），不会抢走编辑/点击的 `Physics.Raycast` 最近命中。
- 视觉特征系统、`MonsterLairLevel` 语义（出怪点）保持不变，只在需要时被读取。

---

## 4. 接口依赖（对外 API）

### `HexCoordinates`
| 成员 | 用途 |
|------|------|
| `int DistanceTo(HexCoordinates other)` | 两格直线 hex 距离（忽略障碍） |

### `HexCell`
`int Distance`（累计移动成本，不可达 = `int.MaxValue`）、`int SearchHeuristic`、`int SearchPriority`（只读，= Distance + Heuristic）、`HexCell PathFrom`、`HexCell NextWithSamePriority`、`void ResetSearchData()`、`HexCell GetNeighborSafe(HexDirection)`（越界/未初始化返回 null 不抛异常）。
> 这些搜索值**只在一次 `FindPath`/`ComputeDistanceField` 之后有意义**。

### `HexCellPriorityQueue`
`void Enqueue(HexCell)`、`HexCell Dequeue()`、`void Change(HexCell, int oldPriority)`、`bool Contains(HexCell)`（O(n)，调试用）、`void Clear()`、`int Count`。

### `Bezier`
`Vector3 GetPoint(a,b,c,t)`、`GetPointClamped(...)`、`GetDerivative(a,b,c,t)`（切线，用于朝向）。

### `HexGrid`
| 成员 | 用途 |
|------|------|
| `bool FindPath(from, to)` / `FindPath(from, to, HexPathMode mode)` | A*；起点允许不可通行（单位可能被困），**终点必须可通行**；无路返回 false。带 mode 的版本不改地图配置 |
| `HexPathMode pathMode` | **寻路模式（默认 `SameElevationOnly` = 只走相同高度）**；`AllowSlope` = 允许坡道 |
| `static bool IsSameElevation(a, b)` | 两格是否同一高度层 |
| `string DescribePathFailure(from, to)` | 无路时的中文原因（高度不同 / 终点不可通行 / 被隔断） |
| `List<HexCell> GetPath()` / `GetPath(List<HexCell> reuse)` | 取"起点→终点"格列表（含两端）；无路径返回 null / 空表 |
| `List<HexCell> FindPathList(from, to)` | 便捷版：无路返回 null |
| `void ClearPath()` | 清空路径 + 所有格的搜索数据（`CreateMap` / `Load` 自动调用） |
| `int ComputeDistanceField(origin)` | 全场距离场（流场），返回可达格数；成本写进各格 `Distance` |
| `bool IsWalkable(cell)` / `bool TryGetMoveCost(from, neighbor, dir, out cost)` | 格级 / 边级通行判定（UI 高亮、AI 选点可复用） |
| `void AddWalkabilityFilter(WalkabilityFilter)` / `RemoveWalkabilityFilter` / `ClearWalkabilityFilters` | 玩法层挂钩（`delegate bool WalkabilityFilter(HexCell)`） |
| `HexCell FindNearestWalkable(nearTo, maxHexDistance = -1)` | 兜底取格；**严格模式下只在 `nearTo` 同高度的格里找** |
| `HexCell FindNearestWalkableAtElevation(nearTo, elevation, maxHexDistance = -1)` | 指定高度层的最近可走格（AI：目标格被占/在别的海拔 → 换同层最近格） |
| `int ComputeDistanceField(origin)` / `ComputeDistanceField(origin, mode)` | 距离场（流场），按模式算 |
| `int GetHexDistance(from, to)` | 直线距离快捷访问 |
| `HexCell GetCellAtWorld(worldPos)` / `GetCellByOffset(x,z)` | 世界坐标 / offset 坐标 → 格（越界返回 null，不抛异常、不打日志） |
| `HexCell[] Cells` / `int CellCountX` / `CellCountZ` | 供 AI / 工具遍历（出怪点扫描等） |
| `bool HasPath` / `HexCell PathFrom` / `PathTo` / `int LastPathCost` | 上一次搜索的结果信息 |
| `int EffectiveHeuristicWeight()` / `ApplyPvZWalkablePreset()` / `ApplyDefaultWalkablePreset()` / `static bool[] PvZWalkableTerrain` | 调参与通行预设 |

### `HexUnit`（挂在地图单位 / 未来僵尸 prefab 上）
| 成员 | 用途 |
|------|------|
| `bool TravelTo(HexCell target)` / `TravelTo(int offsetX, int offsetZ)` | 内部寻路后出发；无路返回 false（单位原地不动） |
| `void Travel(List<HexCell> path)` | 直接沿给定路径（`HexGrid.GetPath()` 的结果）移动 |
| `void StopTravel(bool snapToNearestCell = true)` | 停止（可选落回当前格） |
| `void WarpTo(HexCell)` / `void SnapTo(HexCell)` / `HexCell CurrentCell()` / `HexCell GetCellAt(Vector3)` | 落位与查询 |
| `event Action<HexUnit,HexCell> TravelStarted / EnteredCell / TravelFinished` | AI 触发点：出发 / 进入某格（啃植物用这个）/ 到达终点 |
| `IList<HexCell> Path` / `int PathLength` / `int RemainingCells` / `location` / `destination` / `isTraveling` | 状态 |
| `HexPathMode EffectivePathMode()` | 本单位生效模式：`overrideGridPathMode` 打开用自带 `pathMode`，否则跟随 `HexGrid.pathMode` |
| `string noPathReason` | 上次 `TravelTo` 失败的中文原因（成功后清空） |
| 参数 | `travelSpeed`（格/秒，默认 4）、`overrideGridPathMode`、`pathMode`、`faceTravelDirection`、`turnSpeed`、`facingOffset`、`yOffset`、`followTerrainHeight`、`showPathGizmo` |

### `PathfindingDemo`（场景装配件，可随时禁用/删除）
`bool BeginJourney(bool forceRepath = false)`、`bool MoveUnitTo(HexCell)`、`HexCell ResolveStartCell()` / `ResolveTargetCell()`、`HexCell RandomWalkableCell()` / `FindLairCell()`、`void UpdatePathLine()`。
`HexPathMode TogglePathMode()` / `void SetPathMode(HexPathMode)`、`int CurrentUnitElevation()`。
起终点模式：`PathStartMode {MonsterLair, WestEdge, OffsetCoordinates}` / `PathTargetMode {EastEdge, OffsetCoordinates, RandomWalkable}`
（严格模式下终点只在单位当前高度层里挑）。

### 编辑器菜单（`PathfindingSetupTool`）
`一键装配` / `一键装配并保存场景` / `清理场景中的寻路单位` /
`切换为「仅同高度」寻路（默认）` / `切换为「允许坡道」寻路` / `应用 PvZ 通行预设` / `恢复默认通行预设` / `检查寻路装配状态`。

---

## 5. 装配方式

```
菜单 Tools/寻路系统/一键装配 (PvZ 寻路)              # 建资产 + 装配场景（场景标记为脏，需自己 Ctrl+S）
菜单 Tools/寻路系统/一键装配并保存场景
菜单 Tools/寻路系统/清理场景中的寻路单位              # 清 [Pathfinding]/Units Container 下的实例
菜单 Tools/寻路系统/切换为「仅同高度」寻路（默认）      # 改场景 HexGrid.pathMode
菜单 Tools/寻路系统/切换为「允许坡道」寻路
菜单 Tools/寻路系统/应用 PvZ 通行预设（仅草/泥可行走）  # 改场景 HexGrid.walkableTerrain
菜单 Tools/寻路系统/恢复默认通行预设（全部陆地）
菜单 Tools/寻路系统/检查寻路装配状态                  # 打印接线与通行规则现状
```

产物：

| 资产 | 路径 |
|------|------|
| 单位材质 | `Assets/Resources/Materials/Units/{Unit_Body,Unit_Head}.mat` |
| 占位寻路单位 | `Assets/Resources/Prefabs/Units/PathAgent.prefab`（胶囊 + 球，无 Collider，挂 `HexUnit`，speed 4） |
| 场景对象 | `[Pathfinding]`（`PathfindingDemo` + 子物体 `Units Container`（内含一个 `PathAgent` 实例）） |

Play 中的操作（`PathfindingDemo` 面板可改）：

| 操作 | 效果 |
|------|------|
| 自动 | Play 后单位从起点（默认**怪物巢穴格** `MonsterLairLevel > 0`，没有巢穴则西边缘）走到终点（默认东边缘中间格），然后每 1 秒换一个随机可通行格继续（`loopTravel` 可关） |
| **鼠标中键**点地面 | 让单位寻路前往该格（格不可通行时自动改去最近可通行格） |
| `P` | 重算到当前终点 |
| `O` | 换一个随机可通行终点 |
| `G` | 显示 / 隐藏路径线（LineRenderer，运行时生成，不入场景） |
| `H` | **切换寻路模式**（仅同高度 ↔ 允许坡道），并按新规则重算终点与路径 |

换模型：把 `PathAgent.prefab` 换成自己的怪物 prefab（补挂 `HexUnit` 即可），或把 `PathfindingDemo.unit` 指向场景里你自己的单位——接线不用动。

---

## 6. 验证方式

### 6.1 EditMode 断言（已跑通 **106/106**；2026-09-11 修正高度规则后重跑）

```bash
unity-cli.exe exec --project H:/UnityProjects/CatLike/HexMap --ignore-version-mismatch \
  < tools/pathfinding_editmode_assertions.exec.cs
```

覆盖：

| 段 | 内容 |
|----|------|
| 1 | `DistanceTo`：自身 0、已知坐标对、对称性、三角不等式 |
| 2 | 优先级队列：入队/出队顺序、`Change` 升降优先级、`Count`、`Contains`、`Clear` |
| 3 | A* 平地：路径格数 / 成本 / 首尾 / **每步相邻** / `Distance` 一致 / 重复搜索无残留 / 同格 / `ClearPath` |
| 4 | 绕障：石地在 PvZ 预设下不可走 → 路径绕行且成本 = 30；预设可恢复 |
| 5 | 水下格被跳过；关掉 `blockUnderwater` 后可走 |
| 6 | **两种模式 × 高度（18 条）**：默认 = 仅同高度；落差 1 严格模式无路且原因提示"被高度差阻断"、`AllowSlope` 下成本 35 且路径真的上下坡；落差 2（=跳跃）在 AllowSlope 下仍阻断（原因"被悬崖阻断…需要跳跃"，`IsReachableIgnoringCliffs` 反证）、只有显式关掉 `blockCliffs` 才放行；严格模式绕过单个高格但到不了它；两种距离场可达格数 23 vs 24；`FindNearestWalkable` / `FindNearestWalkableAtElevation` 的取格层数 |
| 7 | 整列不可通行 → 不可达 |
| 8 | 通行过滤器：注册后绕行、移除后恢复直线、`ClearWalkabilityFilters` |
| 9 | 启发式权重 clamp 到 5；大权重与 Dijkstra 成本一致（**仍最优**） |
| 10 | 距离场：可达格数 24、成本正确、无终点时不置 `HasPath` |
| 11 | 最近可通行格 |
| 12 | **存档安全**：搜索字段非公开字段、`HexCell.Save` = 11 字节/格、搜索前后字节完全一致 |
| 13 | 防御式 API：未建图 / 越界 / null 一律安全返回 |
| 14 | `HexUnit`：落位、`TravelTo`、到达、事件计数、路径长度、不可通行目标返回 false、`StopTravel`、手动路径；**单位级移动方式**：默认跟随地图、严格模式登不上高格且 `noPathReason` 有值、`overrideGridPathMode + AllowSlope` 能登上去、override 时不受地图模式影响 |
| 15 | `PathfindingDemo`：巢穴起点解析、边缘/坐标/随机终点、`BeginJourney` 走通、null 安全、路径线不抛异常 |
| 16 | 演示件模式切换：`CurrentUnitElevation`、严格模式随机终点只在同层、`TogglePathMode` 来回切 |

脚本要点（本机 exec 环境的坑，同种植断言）：exec 是**方法体**（不能 `using`，全限定名）；EditMode 下 `Awake/OnEnable/OnDestroy` 都不跑 → 夹具自带 `HexGridChunk` + `uiRect` + 反射补 `neighbors`/`cells`；回调用 **lambda 计数器**（避免"局部函数不能省略默认参数"）；`finally` 里 `DestroyImmediate` 全部临时对象并还原 `HexMetrics.noiseSource`。

### 6.2 手动目检（**先与用户确认再进 Play**；2026-09-11 已按此流程跑通一次）

> 已跑通的 Play 探针结果（13/13，HexMap 实例真实地图）：默认严格模式 → 落差 1 走不过去并给出中文原因 →
> 切 `AllowSlope` 可上下坡且单位跟着地图模式走 → 落差 2 仍被"悬崖"阻断 → 严格模式绕开单个高格到达终点 →
> `TogglePathMode` 来回切 → 严格模式随机终点只在同层；Console 零错误。
> 探针已存成可重跑脚本：`tools/pathfinding_play_height_probe.exec.cs`（13 条断言，含 P0~P13）。
> 跑法：让 HexMap 进 Play → `unity-cli.exe exec --project H:/UnityProjects/CatLike/HexMap --ignore-version-mismatch < tools/pathfinding_play_height_probe.exec.cs`。
> 它临时抬高若干格的 `Elevation`（只改内存、不落盘，末尾复原），所以**不必手刷地形**就能验证高度规则。

1. 在装配过的场景里 Ctrl+S 保存；
2. Play → 单位应自动从巢穴（或西边缘）沿路径走到东边缘，路径线可见、翻坡不沉不浮；
3. 中键点地面 → 单位寻路过去；点水面/悬崖/不可通行格 → Console 打印"该格不可通行，改用最近可通行格"；
4. `G` 切换路径线、`O` 换随机终点、`P` 重算；
5. 想要"仅草/泥可行走"：跑菜单「应用 PvZ 通行预设」，再 Play 观察绕行。
6. **验证高度规则**：用地图编辑器把一小片格子抬高 1~2 级（F12 开编辑面板 → Elevation），
   在严格模式（默认）下点那一片 → Console 打印「不在同一高度…不支持攀爬/跳跃」；
   按 `H` 切到"允许坡道" → 落差 1 的片能走上去、落差 2 的仍走不过去。

### 6.3 回归点

- 角色点击移动 / 地图编辑 / 种植 / 联机 / 特征显示行为不变（寻路不注册任何鼠标左键、不改这三个系统的代码）；
- `.map` 存档字节格式未变（断言 12.2/12.3 实测 11B/格）；
- 场景新增对象只有 `[Pathfinding]`（可整根删除即回滚）。

---

## 7. 已知边界与后续接线

- **`EnteredCell` 逐格事件只能在 Play 验证**（EditMode 协程不推进，`Travel` 直接落终点）。FEAT-004「僵尸到达植物格 → 啃食」就用它。
- **曲线移动是纯视觉 + 逻辑逐格**：`location` 在跨过格边界时更新，`transform.position` 走贝塞尔曲线；`followTerrainHeight` 兜住 Y，避免跨海拔穿插。
- **多单位性能**：`ComputeDistanceField` 一张距离场可给多个单位共用（流场寻路），比每单位各跑一次 A* 便宜。
- **高度语义**：`Elevation` 是整数层，世界高度 = 层 × 5 单位 + 垂直扰动 + 梯田过渡；"同一高度"只看 `Elevation` 相等。
- **默认模式是"仅同高度"**：地图若被刷出多个海拔层，怪物就只在出生层活动（这是需求要的行为）。
  需要跨层的单位（飞行 / 会爬坡）请给它 `overrideGridPathMode = true` + `AllowSlope`，或把地图切到 `AllowSlope`。
- **未做**：道路加成（本工程 `HexCell.HasRoads` 恒为 false）、战争迷雾/视野、联机路径同步、单位互相阻挡（用 `AddWalkabilityFilter` 由玩法层注册即可）、把 `HeroController` 改成走路径（属旧系统改动，需用户点头）。
- **FEAT-002 接法建议**：`ZombieUnit` 继承 `HexUnit`（或挂同物体），出生点用 `HexGrid.Cells` 扫 `MonsterLairLevel > 0`（`PathfindingDemo.FindLairCell()` 是现成范例），终点 = 基地格，然后 `TravelTo(base)`；波次与数值另做 `WaveManager` / `ZombieConfig`。
- 待用户在编辑器里 **Ctrl+S 保存场景**（装配工具刻意不自动保存）。

---

## 8. 变更记录

- 2026-09-11（修正）：**寻路高度规则改为两种模式，默认「仅同高度」**。原实现把"坡道可通行"当默认（落差 1 成本 10），
  与需求不符——现在 `HexGrid.pathMode`：`SameElevationOnly`（默认，相邻格高度不同一律不可通行 = 不支持攀爬/跳跃）
  / `AllowSlope`（落差 1 可走，落差 ≥2 仍阻断）。新增 `HexGrid.IsSameElevation` / `DescribePathFailure`（中文原因）/
  `FindNearestWalkableAtElevation`、`FindPath·FindPathList·ComputeDistanceField·FindNearestWalkable·TryGetMoveCost`
  的模式重载；`HexUnit` 新增 `overrideGridPathMode` / `pathMode` / `EffectivePathMode()` / `noPathReason`；
  `PathfindingDemo` 新增 `TogglePathMode` / `SetPathMode`（Play 中 `H` 键）且终点解析在严格模式下只在同层挑；
  编辑器菜单新增两个模式切换。断言脚本相应改写（高度段重写 + 新增单位级/演示件模式段）。
  **验证**：EditMode 断言 106/106 全绿；真实地图 Play 探针 13/13；Console 零错误。
  另修 `PathfindingSetupTool` 的一个 bug：原来用 `GameObject.Find` 找根对象（**找不到 inactive 对象**），
  把 `[Pathfinding]` 关掉后再点「一键装配」会重复创建一个根对象（场景里出现两个 `[Pathfinding]`）；
  现改为扫根对象（含 inactive）、自动删除重复根对象，复用时强制 `SetActive(true)`。

- 2026-09-11：FEAT-001 寻路系统落地。新增 `Scripts/{Bezier,HexCellPriorityQueue,HexUnit,PathfindingDemo}.cs`、`Editor/PathfindingSetupTool.cs`；`HexCoordinates` 加 `DistanceTo`，`HexCell` 加搜索临时数据区 + `GetNeighborSafe`，`HexGrid` 加寻路 region（通行判定 / A* / 距离场 / 路径回溯 / 最近可通行格 / 世界坐标反查）并在 `CreateMap`、`Load` 末尾清空搜索态。编译零错误零警告；EditMode 断言 **80/80** 通过；场景 `SampleScene` 装配 `[Pathfinding]`（一键装配菜单，待用户保存）。相对 fix-plan 的唯一偏差：通行规则默认"全部陆地"、PvZ 的"仅草/泥"改为可切换预设（理由见 §3.2）。
