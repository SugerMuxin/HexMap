# 特征编辑（Terrain Features）

> Catlike Hex Map Part 9 的工程化落地（含用户自定义扩展）。

## 目标
- 支持在地图编辑模式下实时绘制**四类特征**：城市（Urban）/ 农田（Farm）/ 植被（Plant）/ 怪物巢穴（MonsterLair 放置点），每类 0~3 级
- 支持**密集/稀疏**两种放置密度模式，运行时可切换
- 特征等级随地图**存档（header v2）/ 读档还原**

## 当前状态
- ✅ 四类特征等级编辑（Features Editor 面板：Urban / Fram / Plant / **Lair** 开关 + 滑条）
- ✅ 密度模式切换开关（Dense Toggle：稀疏=仅格心 1 候选位，密集=格心+6 方向共 7 候选位）
- ✅ 存档格式升级 v2（新增 farm/plant 两个字节），v1 旧档兼容读（缺省按 0）
- ✅ 读档后 chunk 全量 Refresh，特征按 seed 哈希重建，位置与旋转稳定
- ✅ Delete 按钮修复（原误绑 Action，点删除会触发存/读档）
- ✅ **特征碰撞体剥离（重要修复）**：特征 prefab 自带 Collider（田块 MeshCollider、树木 MeshCollider、巢穴 BoxCollider）会被 `Physics.Raycast` 取最近命中，导致**在邻近格生成特征 / 点同等级格像"没反应"**；`AddFeature` 放置后默认 `enabled=false` 全部子碰撞体（`keepColliders=false` 可关）
- ✅ **Farm 语义规范化**：滑块等级 = 样式直选（1/2/3 → SoilCell1/2/3，不再哈希轮换）；等级>0 必放；固定朝向；尺寸由 `farmSize`(x/z 目标 scale，默认 10) + `farmThickness`(y 倍率，默认 2) 控制；按 renderer 底边贴地吸附
- ✅ **怪物巢穴（MonsterLair）放置点**：等级 1..n → `monsterLairPrefabs`；仅格心、必放、固定朝向、按 `lairSize`(默认 10) 归一化宽度、底边贴地；优先级 巢穴 > 农田 > 城市/植被
- ✅ **存档格式升级 v3**（每格新增 monsterLair 字节，11B/格），兼容 v0/v1/v2 旧档（按剩余字节自适应：7B/8B/10B/11B）
- ✅ 巢穴 prefab `MonsterLair` 已换成真实模型（洞穴 53 网格 + `chuansongmen` 传送门粒子）；实测实例 = **12.00×5.89×10.50** 世界单位（`lairSize`=12 → **1.20 格宽**，略大于一格；`lairHeight`=1.5 把 y 再抬 1.5 倍 → 顶高 5.89 ≈ **3 倍 Ellen 身高(1.92)**，避免怪物比巢穴还高）、底边**贴地误差 0.000**、格心水平偏移 0.186、每格恒 1 个
- ✅ **Features Editor 面板 Lair 行已落盘**（2026-09-11 重装并保存场景）：`Lair` 开关 → `HexMapEditor.SetApplyLair`、`LairSlider`(0~3 整档) → `SetLairLevel`
- ⏳ 特征 prefab：urban/farm/plant 三数组各 3 级由用户在 Inspector 配置（`HexGridChunk.prefab → Features/HexFeatureManager`），当前沿用用户已配置内容

## 设计要点
- **放置优先级**：格心候选位按 `怪物巢穴 → 农田 → 城市/植被` 依次判定，前者命中则不再放后者（巢穴/农田不抽概率，等级>0 必放、样式=数组[等级-1]）
- **放置算法（城市/植被）**：每候选位采样 `HexMetrics.SampleHashGrid(position)`，用独立哈希 a/c 按 `hash < 等级 × 0.25` 判定是否出现（0 级永不出现），同现时哈希更小者胜出（刷新稳定、无抖动）；旋转用哈希 e
- **贴地吸附**：放置后读取实例 `Renderer.bounds.min.y`，把底边平移到该格地表高度 `cell.Position.y`（兼容任意 pivot；无 Renderer 的空 prefab 自动跳过）
- **网格 / 粒子渲染边界分离（2026-09-11 修复）**：`FitToWidth`/`SnapToGround` 原先用 `GetComponentInChildren<Renderer>()`——取的是**层级里第一个** Renderer。`MonsterLair.prefab` 第一个恰好是 `chuansongmen` 的 `ParticleSystemRenderer`（实例化当帧 bounds 为 0 或 ~56 宽）→ 巢穴被缩到 0.32 倍、并用粒子底边(−2.2)整体抬起 2.52（表现为模型仅 3.92 宽且悬空）。新增 `TryGetMeshBounds`（只合并 `MeshRenderer`/`SkinnedMeshRenderer`）：`FitToWidth` 改用网格包围盒；`SnapToGround(instance, y, meshOnly)` **只有巢穴传 true**，城市/农田/植被沿用旧语义以保持现有观感（实测城市树与农田放置结果逐位不变）
- **巢穴尺寸两个旋钮**：`lairSize`（目标宽度，世界单位；10 = 一格直径，当前 12 = 1.2 格）+ `lairHeight`（高度倍率，在宽度归一化后再乘 y；默认 1 = 模型天然比例）。洞穴类模型天然很扁（MonsterLair 高/宽 ≈ 0.33 → 宽 12 时仅 3.9 高），故需要单独加高；贴地按**拉伸后**的网格底边计算，抬高不会离地
- **碰撞体剥离**：`Terrain` 的 HexMesh（`useCollider=1`）是唯一应有的碰撞体——地图编辑（`HexMapEditor`）与点击移动（`HeroController`）都假设"只有地形有 collider"；特征随实例自带的 Collider 会抢命中并让 `hit.point` 落到邻格，故实例化后统一 `Collider.enabled=false`
- **密度开关**：`HexMetrics.denseFeatures` 静态标志；稀疏=仅 `Triangulate(cell)` 格心位置；密集=格心 + 无河方向三角 + 河相邻三角（均自动避让水下/河流，道路逻辑未实现故不判断）
- **序列化兼容**：`urbanPrefabs` 字段名与 `Transform[]` 类型保持不变，用户已配置引用不丢；farm/plant 为同构新增字段
- **存档格式**：每格 = terrain/elevation/water + 河流 4 字节 + urban [+ farm + plant] [+ monsterLair]
  - v0(7B)/v1(8B)/v2(10B)/v3(11B)；`HexGrid.Load` 按**文件剩余字节数自适应**每格格式并把归一化 header 传给 `HexCell.Load`，再按 header 分支读取（旧档缺失字段归 0）
  - 兼容 Net 联机下发：房主 `SendMapTo` 写 header=1 但 `grid.Save` 产 11B/格，客户端靠同一自适应探测正确解析
- **存读档入口**：SaveLoadMenu（header 3）+ HexMapEditor.Save/Load（test.map 固定路径，同为 header 3）

## 接口依赖
| 组件 | 职责 |
|---|---|
| `HexCell.FarmLevel / PlantLevel` | 属性 setter 触发 `RefreshSelfOnly()` 重建所在 chunk |
| `HexFeatureManager` | `PickPrefab(数组, 等级, hash)` + 四类型优先级放置 + `FitToWidth`/`SnapToGround`（`TryGetMeshBounds` 网格包围盒，忽略粒子）；`monsterLairPrefabs`/`lairSize`/`lairHeight` |
| `HexMetrics.HexHash` | a~e 五个独立哈希（出现概率×3、旋转×1，d 预留） |
| `HexMapEditor.SetFarmLevel / SetPlantLevel / SetApplyFarmLevel / SetApplyPlantLevel / SetFeatureDensity` | UI 绑定入口 |
| `HexMapEditor.SetApplyLair / SetLairLevel` | 巢穴 UI 绑定入口（Toggle + Slider） |
| `Editor/HexLairSetupTool` | 菜单 `Tools/地形特征/装配怪物巢穴 (UI+预制体)` 一键装配（幂等） |
| `HexGrid.RefreshAll()` | 密度切换后全图刷新 |

## 验证方式
1. Play → Features Editor 面板勾选类型 + 拖滑条刷地图：格上出现对应特征（等级越高越密）
2. 勾 **Dense** 开关：特征密度变为 7 候选位/格；取消回到仅格心
3. Save Map（任意名）→ Load Map：三类特征等级还原一致
4. 旧格式（v0/v1/v2）存档仍可读，缺失特征默认 0 级
5. 勾 **Lair** + 滑条 1/2/3 刷地图：格心出现对应巢穴（大小 = `lairSize`×`lairHeight`，当前 12 / 1.5 → 1.2 格宽、5.89 高、贴地、朝向固定）；滑条 0 清除；**每格恒 1 个**，同一格重复点击不会增加
6. 点击已放特征的地块应仍命中地形（特征无碰撞体），不会跑到邻近格生成

## 变更记录
- 2026-09-11：**巢穴尺寸调大调高**——新增 `lairHeight`（高度倍率，默认 1）并把 `lairSize` 6→12：实测 12.00×5.89×10.50（1.2 格宽、顶高 ≈3 倍 Ellen 身高 1.92），贴地仍 0.000；Play 内用临时相机 + Ellen 站位出图核对（见 README 同日记）
- 2026-09-11：**修复「Lair 行消失」+「巢穴又小又悬空」**——① Features Editor 的 `Lair`/`LairSlider` 只存在于场景内存（装配工具仅 `MarkSceneDirty` 未落盘），编辑器重载后整行丢失 → 重跑 `Tools/地形特征/装配怪物巢穴 (UI+预制体)` + 保存场景（工具已强化：面板高度按当前 Game 视图自适应、不再写死 1080；新建控件登记 Undo）
- 2026-09-11：`HexFeatureManager` 新增 `TryGetMeshBounds`（忽略粒子渲染器）修掉 FitToWidth/SnapToGround 被 `MonsterLair.prefab` 传送门粒子带偏的坑；Play 实测巢穴 6.00×1.96×5.25 / 贴地 0.000 / 格心 0.186，城市树与农田放置结果**零回归**
- 2026-09-10：**修复特征点击错位/重复**——特征 prefab 自带 Collider 抢了 `Physics.Raycast` 的最近命中，`hit.point` 落到邻近格，表现为"点一次旁边多长一个巢穴（2~3 个）/点同等级格没反应"；`AddFeature` 现默认剥离实例全部 Collider（新增 `keepColliders` 开关），同时修好点击移动的落格
- 2026-09-10：新增**怪物巢穴（MonsterLair）放置点**——`HexCell.MonsterLairLevel` + `HexFeatureManager.monsterLairPrefabs/lairSize`（格心、必放、贴地、按宽度归一化）+ Features Editor 新增 Lair 开关/滑条 + 存档 v3（11B/格，自适应兼容 v0/v1/v2 与 Net 下发）+ `Editor/HexLairSetupTool` 一键装配
- 2026-09-10：Farm 语义修正——等级即样式直选（替代原哈希随机轮换）、`farmSize`/`farmThickness` 替代旧 `farmCoverage` 自适应（旧字段序列化值导致田块仅 6.62 的现象随之消失）
- 2026-09-09：Part 9 特征编辑完善——补齐 Farm/Plant 编辑与存档（header v2 兼容 v1）、新增密集/稀疏密度开关、特征 UI 重绑（Plant/Fram Toggle/Slider 原误绑地形索引/空绑）、修复 Delete 按钮、导出 CodeTxts
