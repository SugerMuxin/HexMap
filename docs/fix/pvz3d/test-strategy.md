# 验证测试策略初稿

> 关联问题: ISSUE-PVZ3D
> 生成时间: 2026-09-10
> 文档性质: 初稿（用于后续生成详细测试文档）
> 说明: 本需求为 Unity 游戏功能开发，测试以「编辑器 Play 模式目检 + 数据断言 + 关键纯函数单元测试」为主，UI/寻路/战斗的行为验证依赖运行中的 Unity 场景。

---

## 一、测试范围

### 1.1 受影响模块

| 模块 | 修改类型 | 测试优先级 |
|------|----------|------------|
| HexCoordinates.DistanceTo | 新增方法（纯函数） | 高 |
| HexGrid.FindPath / IsWalkable | 新增方法 | 高 |
| HexCellPriorityQueue | 新文件（数据结构） | 高 |
| ZombieUnit / WaveManager | 新文件（出怪） | 高 |
| PlantUnit / PlantingSystem | 新文件（种植） | 高 |
| CombatSystem（Health/Damageable/Projectile/TargetScanner） | 新文件（通用战斗） | 高 |
| PlantShooter / ZombieAttacker / CombatManager | 新文件（战斗桥接） | 高 |
| FGUI（PvZUI 包 + View/Prefab + 桥接） | 新文件（UI） | 中 |
| HexCell 搜索字段 | 追加非序列化字段 | 中（验证不进存档） |

### 1.2 功能点清单

- [ ] 寻路：从起点到终点的六边形路径正确、绕过不可行走格
- [ ] 出怪：按波次定时生成僵尸，僵尸沿路径移动，抵达终点/清波触发胜负
- [ ] 种植：草/泥可种，沙/石/雪/水下/巢穴/已占用不可种；阳光扣除与冷却生效
- [ ] 战斗：植物索敌最近僵尸并发射投射物；僵尸啃食植物；双方死亡移除；胜负判定
- [ ] UI：选卡→种植模式→点击种植闭环；阳光/冷却/波次/胜负正确刷新

---

## 二、测试类型

### 2.1 单元测试（纯函数/数据结构，可用 Unity Test Framework）

**目标**: 验证寻路核心算法与通用战斗组件逻辑，无需运行场景。

| 测试项 | 测试内容 | 对应 FEAT 任务 | 优先级 |
|--------|----------|----------------|--------|
| UT-001 | `HexCoordinates.DistanceTo`：原点/对角线/邻居格距离断言 | FEAT-001 | 高 |
| UT-002 | `HexCellPriorityQueue`：Enqueue/Dequeue/Change/Contains 顺序与最值 | FEAT-001 | 高 |
| UT-003 | `HexGrid.FindPath`：无障碍直线、有障碍绕行、无路径返回空 | FEAT-001 | 高 |
| UT-004 | `IsWalkable`：草/泥=true，沙/石/雪/水/占用=false | FEAT-001 | 高 |
| UT-005 | `Health.Damage`：扣血、归零触发死亡事件、不回负 | FEAT-004 | 高 |
| UT-006 | `TargetScanner`：射程内返回最近目标、空场景返回 null | FEAT-004 | 高 |
| UT-007 | `PlantingSystem.CanPlant`：各地形类型/占用/巢穴/水下组合断言 | FEAT-003 | 高 |

**边界条件**:
- [ ] DistanceTo 同格返回 0；跨地图对角返回预期最大值
- [ ] FindPath 起点==终点、起点或终点不可行走、地图边界（null 邻居）
- [ ] Health 单次伤害超过剩余血量、连续多次伤害
- [ ] 优先队列 Change 一个不在队中的元素

### 2.2 集成测试（场景内组件协作）

**目标**: 验证跨模块交互，需运行 Unity 场景（Play 模式或 EditMode 初始化）。

| 测试项 | 测试场景 | 涉及模块 |
|--------|----------|----------|
| IT-001 | 僵尸出生 → FindPath → 沿路径移动到终点 | WaveManager + ZombieUnit + HexGrid |
| IT-002 | 种植植物 → 植物索敌射程内僵尸 → 投射物命中扣血 | PlantingSystem + PlantShooter + TargetScanner + Projectile + Health |
| IT-003 | 僵尸抵达植物格 → 定时啃食 → 植物归零移除 | ZombieAttacker + Health + CombatManager |
| IT-004 | 清空波次 → 胜利事件；僵尸到终点 → 失败事件 | WaveManager + CombatManager |
| IT-005 | FGUI 选卡 → 进入种植模式 → 点击格种植 | FGUI 桥接 + PlantingSystem + 输入仲裁 |

### 2.3 回归测试

**目标**: 确保新增功能不破坏现有系统。

**回归范围**:
- 核心功能: 地图生成/编辑、地形纹理显示、特征编辑（urban/farm/plant/monsterLair）、存档读档（`.map` 11B/格格式不变）、联机地图下发。
- 关联功能: 角色点击移动（`HeroController`）、近战攻击动画（`EllenAttack`）、音频桥接。
- 关键回归点: `HexCell.Save/Load` 字节数不变（寻路字段未序列化进存档）；种植/僵尸单位不干扰 `HexMapEditor` 点击编辑与 `HeroController` 移动射线。

### 2.4 场景测试（模拟真实游玩）

| 场景 | 操作步骤 | 预期结果 |
|------|----------|----------|
| 场景1: 正常通关 | 种植一排植物 → 等待波次 → 植物消灭所有僵尸 | 波次提示递增，全清后弹"胜利" |
| 场景2: 防守失败 | 不种或少种植物 → 僵尸走到终点 | 弹"失败" |
| 场景3: 非法种植 | 在石/雪/水下/已占用格点击种植 | 不响应或提示不可种植，阳光不扣除 |
| 场景4: 阳光不足 | 阳光为 0 时选卡种植 | 无法种植，卡片置灰/提示 |
| 场景5: 冷却中 | 连续快速种植同种植物 | 冷却期内无法再次种植 |

---

## 三、验收标准

### 3.1 必须通过
- [ ] 寻路在无障碍/有障碍地图上均返回正确路径，不可行走格被跳过
- [ ] 僵尸按波次从出怪点生成并沿路径移动
- [ ] 草/泥格可种植，其它地形不可种植
- [ ] 植物能攻击僵尸、僵尸能啃食植物、双方死亡正确移除
- [ ] 胜负判定正确触发
- [ ] FGUI 选卡→种植→战斗→胜负闭环可用
- [ ] `.map` 存档字节格式不变（回归），现有功能无回退

### 3.2 建议通过
- [ ] 集成测试场景全部通过（IT-001~005）
- [ ] 边界条件覆盖（UT 边界项）
- [ ] 大量僵尸同屏（如 30+）时无明显卡顿/异常
- [ ] CombatSystem 模块内 grep 无 `HexGrid/HexCell` 引用

### 3.3 验证点清单

| 编号 | 验证点 | 验证方法 | 通过标准 |
|------|--------|----------|----------|
| V-001 | DistanceTo 正确性 | 单元测试断言已知坐标对 | 距离与手工计算一致 |
| V-002 | FindPath 绕障 | 编辑器目检路径高亮 / 单元断言路径序列 | 绕过障碍且为最短 |
| V-003 | 波次出怪 | Play 模式目检 + 日志 | 每波数量/间隔符合配置 |
| V-004 | 种植合法性 | 点击各类地形 | 仅草/泥成功 |
| V-005 | 战斗伤害 | 观察血量/日志 | 投射物命中扣血、归零移除 |
| V-006 | 存档兼容 | 存→读旧档 / 联机下发 | 字节数不变、字段正确 |
| V-007 | 射线互不干扰 | 编辑模式点格 + 角色移动 | 仍能正确选格/移动 |

---

## 四、测试建议（后续详细测试文档扩展方向）

1. **测试数据准备**: 预制多张测试地图（纯草地图、含障碍地图、多巢穴多终点地图、大波次地图）；预制僵尸/植物参数 SO 覆盖不同数值档。
2. **自动化测试**: 寻路纯函数（DistanceTo/优先队列/FindPath/IsWalkable）与 CombatSystem 通用组件（Health/TargetScanner）用 Unity Test Framework 自动化；可结合 `run-python-agent-tests` / `run-agent-test-unityplayer` 技能做黑盒 Play 模式脚本化测试。
3. **性能测试**: 同屏 N 僵尸（10/30/50）帧率、FindPath 在 20×15 与更大地图的耗时、频繁刷怪的内存增长（对象池必要性评估）。
4. **联机测试**: 若后续要求联机同步战斗（当前阶段未纳入），需评估植物/僵尸/阳光的网络权威与同步方案，另行立项。
5. **边界与异常**: 空地图、全障碍地图、无出怪点、终点不可达等退化场景的降级行为。

### 注意事项
- **Play 模式验证前先征得用户同意**（用户偏好：不擅自进入 Play / 保存场景）。建议先做不依赖 Play 的单元测试与 EditMode 断言，场景目检时再问用户。
- 本机多编辑器实例（HexMap/HexMap2 同物理 Assets）测试时注意两端同步与单一实例操作。
- 战斗单位注意 Layer 隔离，避免与 `HexMapEditor`/`HeroController` 的 Physics.Raycast 冲突。

---

## 五、参考信息

**相关代码文件**:
- `Assets/Scripts/HexCoordinates.cs`、`HexCell.cs`、`HexGrid.cs`（寻路基础，FEAT-001 改动点）
- `Assets/Scripts/HexFeatureManager.cs`、`HexMapEditor.cs`（特征/编辑，回归参照）
- `Assets/AudioSystem/`、`Assets/Scripts/Audio/`（通用模块+桥接范式参照）
- 新增: `Assets/Scripts/HexCellPriorityQueue.cs`、`Bezier.cs`、`Assets/CombatSystem/`、`Assets/Scripts/Combat/`、`Assets/Scripts/UI/`

**相关文档**:
- 修复方案文档: `fix-plan.md`（同目录）
- `Assets/docs/README.md`（项目进度总览）
- `Assets/docs/features-editing.md`（MonsterLair 语义）
- catlike 翻译: `F:\A-Ruosa\PersonalNotes\Teach\Cat\HexMap\15-距离、16-寻路、17-受限移动、19-动画移动`
