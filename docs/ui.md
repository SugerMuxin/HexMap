# UI 系统（ui）

> 方案文档 + 实施计划。技术栈**已定：uGUI + 表驱动 UI 框架**（2026-09-11 用户确认）。
> 定位：为 HexMap 提供统一的 UI 管理层；首期落地「联机大厅 UI」与「背包（种子）UI」，
> 后续 PvZ 战斗 UI（选卡 / 阳光 / 波次 / 胜负）与更多界面直接复用本框架。

## 目标

- 一套完整的 UI 管理系统：面板注册（表驱动）、层级、栈、互斥、模态遮罩、Esc / 热键、输入仲裁
- 与游戏解耦：通用层 `Assets/UISystem/` 零游戏类型依赖；游戏侧 `Assets/Scripts/UI/` 经桥接器接入
  （沿用 AudioSystem / UnderwaterEffect 的「通用模块 + 游戏侧桥接」范式）
- 数据表驱动：CSV 源 → Editor 导入 → 生成/更新资产；**新增种子 / 界面 / 文案 = 改表 + 导入，不动代码**
- 联机 UI：玩家名（本地缓存）→ 主机开房（可选地图）/ 加入；局域网自动发现服务器列表；手输 IP 兜底
- 背包 UI：按 **B** 弹出，展示所有可种植种子（植物卡），点击即选卡进入种植模式
- **不改动现有可用系统**：现有 4 个 uGUI 菜单保持原样；`PlantingSystem` / `NetGameManager` 只做**新增**公开 API

## 当前状态（2026-09-11）

- 技术栈已确认：**uGUI**（FGUI 已评估并放弃：本机 `FairyGUI-Editor 2022.1.0p1` 无专业版授权，实测
  `-batchmode` 探针报 `This feature is only avaliable in professional FairyGUI-Editor` → **命令行发布不可用**，
  每轮 UI 改动都要人工 Publish，自动化闭环成本高）
- 工程现状（实测）：
  - uGUI 已就位：场景根 `UI` 下有 4 个 Canvas（`Hex Map Editor` / `New Map Menu` / `SaveLoadMenu` /
    `Features Editor`，均 ScreenSpaceOverlay 1920×1080 + CanvasScaler）；`EventSystem` + `StandaloneInputModule`；
    `ProjectSettings.activeInputHandler = 2 (Both)`（旧 Input 可用，与现有脚本一致）
  - 另有 WorldSpace `HexGridCanvas`（格子标签），本方案不碰
  - Mirror 联机层已落地：`NetRoot` = `NetGameManager` + `KcpTransport` + `NetworkManagerHUD`（**尚无 NetworkDiscovery**）
  - 种植层已完备：`PlantingSystem` 提供 UI 所需全部接口与事件（阳光 / 选卡 / 冷却 / 校验 / 占用）
  - 输入仲裁已有两处先例：`HexControlMode.netClientReadOnly`（联机客户端只读）、`plantingActive`（种植模式占左键）
- 实施进度：**M1~M6 全部落地并验证（断言 178 条全过）+ 双端联调已通过（HexMap 房主 / HexMap2 客户端）**

## 实施进度

### M1 通用 UI 骨架 ✅（2026-09-11）

产物：

| 类别 | 文件 |
|------|------|
| 通用层 | `Assets/UISystem/`：`UIPanelLayer.cs` / `UIPanelTable.cs`（面板表 SO）/ `UIPanel.cs`（基类）/ `UIManager.cs`（打开关闭·栈·层级容器·互斥组·模态遮罩·Esc 与热键·阻塞广播）/ `UIFontProvider.cs`（中文 OS 字体）/ `Samples/UIDemoPanel.cs`（自检面板） |
| 游戏侧 | `Assets/Scripts/UI/UICharacterInputGate.cs`（订阅 `UIManager.BlockingStateChanged` → 置 `HexControlMode.uiModalActive` + 每帧冻结本地角色输入 + 释放鼠标 + 锁 `HexMapCamera`） |
| 既有文件（各 1 行级） | `HexControlMode.cs` 新增 `uiModalActive`；`HexMapEditor.cs` / `HeroController.cs` / `PlantingInput.cs` 各 +1 个让出条件 |
| 装配工具 | `Assets/Editor/UISetupTool.cs`：菜单 `Tools/UI/一键装配 UI 框架 (UIRoot)`（幂等）、`Tools/UI/移除场景 UIRoot` |
| 装配产物 | 面板表 `Assets/Resources/Configs/Tables/UIPanelTable.asset`；自检面板 `Assets/Resources/UI/Panels/UIDemoPanel.prefab`；场景对象 `[UIRoot]`（Canvas 1920×1080 sortingOrder 100 + `UIManager` + `UICharacterInputGate` + 子物体 `Panels` / `Modal Mask` / `Popups`） |

验收：

- 编译零错误零警告（`unity-cli editor refresh --compile`，console 空）
- EditMode 断言 **39/39 通过**（`tools/ui_m1_assertions.exec.cs`）：表注册、打开/关闭/栈顶/栈深、层级容器归属、同层排序、重复 Open=刷新、Toggle、CloseAll、互斥组、遮罩显隐、阻塞广播、Esc 跳过 `escClose=false` 的栈顶、遮罩点击、中文 OS 字体可用、仲裁位读写
- **待人工确认**：进 Play 目视验证（`F1` 开关自检面板）；场景 `[UIRoot]` 需你 `Ctrl+S` 保存

## 设计要点

### 1. 分层

| 层 | 目录 | 依赖 |
|----|------|------|
| 通用 UI 框架 | `Assets/UISystem/` | 仅 `UnityEngine.UI`，零游戏类型（可跨项目复用） |
| 游戏侧界面 | `Assets/Scripts/UI/` | 桥接 `PlantingSystem` / `NetGameManager` / Mirror / HexGrid |
| 数据表 | `Assets/Resources/Configs/Tables/` + `Assets/Editor/TableImporter.cs` | 表驱动资产生成 |
| 装配工具 | `Assets/Editor/UISetupTool.cs` | 一键建 Canvas / 面板 prefab / 接线（幂等） |

### 2. 通用层（`Assets/UISystem/`）

- `UIManager`（场景单例，挂在 `[UI Root]` 对象）
  - API：`Open(id, payload)` / `Close(id)` / `CloseTop()` / `Toggle(id)` / `IsOpen(id)` / `TopId`
  - 面板栈 + 层级（`Background` / `Normal` / `Popup` / `Overlay`）+ `exclusiveGroup` 互斥 + `modal` 模态遮罩（遮罩点击可选关闭）
  - 事件：`PanelOpened` / `PanelClosed` / `PanelStackChanged`
  - 热键：`Esc` 关闭栈顶；表内 `hotkey` 字段配置（如 `B` 开关背包）
  - 输入仲裁：任一 `blockGameInput` 面板打开 → `HexControlMode.uiModalActive = true`（关闭后复位）
  - 面板实例缓存（懒实例化，`preload` 可预热）
- `UIPanel`（抽象基类）：生命周期 `OnCreate` / `OnShow(payload)` / `OnHide` / `OnRefresh`
- `UIPanelTable`（ScriptableObject 注册表，由 CSV 生成）：`id / prefabPath / layer / modal / exclusiveGroup / escClose / hotkey / blockGameInput / preload`
- `UIEventBus`：面板间解耦通信（弱引用订阅，防泄漏）
- `UIFontProvider`：中文字体统一入口（见 §5）
- 面板 prefab 约定目录：`Assets/Resources/UI/Panels/*.prefab`（表里引用路径）

### 3. 游戏侧界面（`Assets/Scripts/UI/`）

- `UICharacterInputGate`（`[DefaultExecutionOrder(100)]`）：模态面板打开时冻结本地角色输入——
  每帧 `SetMovementInput(Vector3.zero)` + `SetJumpInput(false)` + 把 `controlRotation` 复位到开面板那一刻
  （抵消鼠标视角漂移），并解锁/恢复鼠标指针。**完全新增，不改 `NaughtyCharacter` / `PlayerController`**
- `LobbyPanel`
  - 玩家名输入：`PlayerPrefs["ui.playerName"]` 本地缓存，打开时回填、开房/失焦时保存
  - **主机**：可选「地图」下拉（列 `persistentDataPath/*.map`）→ 先载入所选地图 → `StartHost()`
  - **加入**：局域网服务器列表（`NetLobbyDiscovery` 发现结果：房主名 / 人数 / 地图 / 地址）+ 刷新 + 手输 IP 直连
  - 状态区：连接中 / 失败原因 / 已连接（+ 断开按钮）；就绪后隐藏 Mirror `NetworkManagerHUD`（保留兜底开关）
- `InventoryPanel`（B 键）：种子格（icon / 名称 / 阳光 / 冷却灰化 / 可种地形提示）；
  点击 → `PlantingSystem.SelectPlant(config)` + 自动收起背包；订阅 `SunChanged` / `CooldownChanged` / `SelectionChanged`；
  分类、排序、搜索留扩展位
- `InventorySystem`（游戏侧数据源）：首版 = `SeedTable` 中已解锁项；预留拥有数量 / 解锁存档接口
  （后续加掉落、合成不动 UI）
- `MapFileUtil`：列 / 载 `.map`（与 `SaveLoadMenu` 同格式 `header=3`，复用 `HexGrid.Load`；**不修改** `SaveLoadMenu`）

### 4. 联机桥接（新增，Mirror 侧）

- `NetLobbyDiscovery : NetworkDiscoveryBase<NetLobbyRequest, NetLobbyResponse>`：广播响应携带
  `hostName / playerCount / mapName / port`（比 Mirror 自带 `NetworkDiscovery` 的单一 uri 更适合做列表 UI）
- `NetPlayerName : NetworkBehaviour`（挂 `EllenNet`）：`SyncVar<string> displayName`，owner 在
  `OnStartLocalPlayer` 经 `[Command]` 上报
- `NetGameManager` **新增**公开 API（不改既有闸门流程）：`SetPlayerName / StartHostWithMap / JoinByAddress /
  StopNet`，只读状态 `IsHosting / IsConnected / PlayerCount`，事件 `SessionStateChanged`
- 列表刷新：`StartDiscovery()` → `OnServerFound` 按 `serverId` 去重累积 → 面板刷新

### 5. 数据表（配置中心）

- 源文件：`Assets/Resources/Configs/Tables/*.csv`（UTF-8，逗号分隔，首行表头 `字段名:类型`）
- 导入：菜单 `Tools/UI/导入数据表`（`Assets/Editor/TableImporter.cs`）
  - `SeedTable.csv` → 逐行 upsert `Assets/Resources/Configs/Plants/PlantConfig_{id}.asset`
    （**按 id 稳定命名；已存在资产只覆盖表管字段，GUID 与既有引用保持不变**）→ 汇总 `SeedTable.asset`
  - `UIPanelTable.csv` → `UIPanelTable.asset`（面板注册表）
  - `UITextTable.csv` → `UITextTable.asset`（key / 中文 / 预留 en 列，便于以后多语言）
- `SeedTable.csv` 首版列：
  `id, name, desc, icon, prefab, sunCost, cooldown, maxHealth, damage, attackInterval, attackRange, projectileSpeed, terrain, unlock, order`
  - `terrain` 用 `草|泥` 或 `1|2` 表达（转 `plantableTerrain` 掩码；terrainTypeIndex 语义 0沙 1草 2泥 3石 4雪）
  - `unlock` 首版恒 `1`（背包显示全部），为后续解锁 / 掉落留位
- **中文字体**：首版用 legacy `Text` + 运行时 OS 字体（`UIFontProvider` 走
  `Font.CreateDynamicFontFromOSFont("Microsoft YaHei", …)`）——Windows 开发机零资产即可显示中文，
  且与现有菜单同用 legacy Text；后续要发布其它平台或追求字形时再切 TMP + 内置字体资产（列入待办）

### 6. 与现有系统的接触面（尽量小）

| 文件 | 改动 | 说明 |
|------|------|------|
| `HexControlMode.cs` | +`public static bool uiModalActive` | 仅新增字段 |
| `HeroController.cs` / `HexMapEditor.cs` / `PlantingInput.cs` | 让出条件各 +1 个 `\|\| HexControlMode.uiModalActive` | 无模态面板时行为完全不变 |
| `NetGameManager.cs` | 新增公开 API + 会话事件 | 既有地图闸门 / spawn 流程不动 |
| `EllenNet.prefab` | 挂 `NetPlayerName` | 装配工具或手动 |
| `NetRoot` | 挂 `NetLobbyDiscovery`（+ 可选隐藏 HUD） | 装配工具做 |
| 现有 4 个菜单 | **不改** | 保持可用 |

### M2 数据表管线 ✅（2026-09-11）

产物：

| 类别 | 文件 / 路径 |
|------|------|
| 通用层 | `Assets/UISystem/UITextTable.cs`（文案表 SO + `UIText.Get` 静态入口，缺 key 回退并只警告一次） |
| 游戏侧 | `Assets/Scripts/UI/SeedTable.cs`（种子表 SO：id → PlantConfig + terrain 展示元数据 + unlocked + order；`GetUnlockedConfigs()` 可直接灌给 `PlantingSystem.availablePlants`） |
| Editor | `Assets/Editor/TableCsv.cs`（极简 CSV 读写：引号/逗号/换行转义、类型解析）、`Assets/Editor/TableImporter.cs`（导入/导出菜单） |
| CSV 源（可编辑） | `Assets/Resources/Configs/Tables/{SeedTable,UIPanelTable,UITextTable}.csv`（UTF-8 BOM，Excel 友好） |
| 资产（导入产物） | `.../Tables/{SeedTable,UIPanelTable,UITextTable}.asset` + `.../Plants/PlantConfig_{id}.asset` |

菜单：`Tools/UI/数据表/` → `全部导出 CSV（从资产）` / `全部导入 CSV（到资产）` / 各表单独导入导出。

关键保证：

- **种子导入按 id 稳定 upsert**：已存在的 `PlantConfig_*.asset` 只覆盖"表管字段"（名称 / 描述 / 图标 / prefab / 数值），
  **文件名与 GUID 不变** → 场景里已配好的 `availablePlants`、`[Planting]` 引用不会断（实测导入前后 GUID 一致）。
- 面板导入按 id upsert，**CSV 之外的既有条目保留不删**；文案表为全量替换。
- 首版 `SeedTable.csv` 由现有资产反向导出生成基线（射手 / 坚果墙），再导入回写，形成"改表→导入"闭环。
- 文案表首版 30 条 key（背包 / 联机大厅 / 通用按钮），后续 UI 一律走 `UIText.Get("key", "兜底")`。

验收：EditMode 断言 **22/22 通过**（`tools/ui_m2_assertions.exec.cs`，含幂等重导入、GUID 保持、字段不丢、缺 key 回退）。

### M3 背包（种子）UI ✅（2026-09-11）

产物：

| 类别 | 文件 |
|------|------|
| 游戏侧 | `Assets/Scripts/UI/InventorySystem.cs`（数据源：种子表 → 排序 / 解锁 / 阳光 / 冷却 / 选卡转发，预留数量与掉落扩展点）、`InventoryPanel.cs`（面板：网格卡片 + 阳光 + 冷却 + 点击选卡 + 自动收起 + 订阅 PlantingSystem 事件）、`SeedCardView.cs`（卡片：图标 / 名称 / 阳光 / 地形 / 冷却填充遮罩 / 选中高亮 / 点击上报） |
| 装配工具 | `Assets/Editor/UIBagSetupTool.cs`：菜单 `Tools/UI/一键装配 背包 UI (bag)`、`Tools/UI/一键装配 全部 UI（框架 + 背包）` |
| 产物资产 | 卡片 `Assets/Resources/UI/Panels/SeedCardView.prefab`、面板 `.../InventoryPanel.prefab`、表项 `bag`（Normal 层 / 模态 / 阻塞输入 / **热键 B** / Esc 可关）、场景对象 `[Inventory]`（InventorySystem） |

行为：按 **B** 开关背包；卡片显示名称 / 阳光消耗（不够时变红）/ 可种植地形；点击卡片 → `PlantingSystem.SelectPlant()` 进入种植模式并自动收起面板（可在 `[Inventory]` 上关掉）；冷却中的卡片显示垂直填充遮罩与剩余秒数；阳光 / 冷却 / 选卡变化实时刷新（订阅 `SunChanged / CooldownChanged / SelectionChanged / PlantPlaced`）。

验收：EditMode 断言 **35/35 通过**（`tools/ui_m3_assertions.exec.cs`）：控件接线、表项配置、卡片数量与文本、点击选卡 → 进入种植模式 → 面板自动收起、重复打开复用实例、遮罩与阻塞状态收发、清理后状态复位。

### M4 联机大厅 UI ✅（EditMode 断言 40/40 + 双端 Play 联调通过）

**双端联调实测结果**（2026-09-11，HexMap 作房主 + HexMap2 作客户端，两编辑器同机）：

| 步骤 | 结果 |
|------|------|
| 房主开房 | `state=Hosting`，状态「你是房主 · 10.80.4.87:7777 · 玩家 1」，大厅面板自动收起 |
| 客户端「刷新」局域网 | 发现 1 个房间：**房主甲 / 人数 1/100 / 地图 20×15 / 地址 10.80.4.87 / 版本 1.0**，面板列表渲染出 1 行 |
| 点击列表行连接 | 地址自动填入地址框 → `state=Connecting` → `Connected`「已连接 · 10.80.4.87」，大厅自动收起 |
| 房主侧 | 连接数 **2**，状态「你是房主 · … · 玩家 2」 |
| 玩家名同步 | 房主端看到远端玩家名为 **客户端乙**（`[Command]`→`[SyncVar]` 生效） |
| 位置同步 | 两端看到的两个角色位置完全一致（客户端乙 82.3,0.2,52.5 / 房主甲 52.0,-0.2,30.0） |
| HUD 接管 | 联机后 Mirror 自带 HUD 被**禁用组件**（NetRoot 仍 active） |
| 停 Play 后 | 两端场景不脏、NetRoot 活跃、HUD 恢复、无残留玩家实体/网络实体 |

> 「移动同步」未单独验证：直接写 `transform.position` 会被角色自身的 CharacterController 覆盖（位置根本不变化），要验证移动得走 WASD 输入 —— 那属于既有角色系统，不在本次 UI 范围。

产物：

| 类别 | 文件 |
|------|------|
| 联机层（游戏侧） | `Assets/Scripts/Net/NetLobbyDiscovery.cs`（继承 Mirror `NetworkDiscoveryBase<NetLobbyRequest, NetLobbyResponse>`：广播**房主名 / 地图 / 人数 / 上限 / 版本**）、`Assets/Scripts/Net/NetPlayerName.cs`（玩家名：PlayerPrefs 本地缓存 → `[Command]` 上报 → `[SyncVar]` 广播；`Sanitize` 限 16 字去控制字符） |
| 桥接层 | `Assets/Scripts/UI/NetLobbyBridge.cs`（大厅 ↔ Mirror 唯一入口：状态机 Offline/Hosting/Connecting/Connected、发现列表去重、开房（可先载图）、加入、12 秒超时、断开、自动收起大厅、接管 NetworkManagerHUD 显隐）、`Assets/Scripts/UI/MapFileUtil.cs`（`.map` 列表 / 载入 / 另存，与 `SaveLoadMenu` **同格式同目录**，载图后清旧植物） |
| UI 层 | `Assets/Scripts/UI/LobbyPanel.cs`（玩家名 / 地图下拉 / 创建主机 / 局域网列表 / IP 直连 / 状态 / 断开）、`Assets/Scripts/UI/LobbyServerRow.cs`（一行房间：房主·人数·地图·地址，点击即连） |
| 装配工具 | `Assets/Editor/UILobbySetupTool.cs`：菜单 `Tools/UI/一键装配 联机大厅 UI (lobby)` |
| 资产 / 场景 | 面板 `Assets/Resources/UI/Panels/LobbyPanel.prefab`、行 `.../LobbyServerRow.prefab`、表项 `lobby`（Normal / 模态 / **热键 F2** / 阻塞输入 / Esc）、场景 `NetRoot` 补 `NetLobbyDiscovery` + `NetLobbyBridge`、`EllenNet.prefab` 补 `NetPlayerName` |

玩家流程：

1. **房主**：F2 开大厅 → 填玩家名 → 选地图（列表来自 `persistentDataPath/*.map`，首项"（当前地图）"= 不换图）→ 创建主机 → 界面显示本机 IP；`NetLobbyDiscovery` 开始 UDP 广播（47777）。
2. **加入者**：F2 → 填玩家名 → 点"刷新"→ 列表出现房间（房主名 / 人数 / 地图）→ 点该行即连；也可在"主机地址"直接填 IP 再点"连接"（同机测试用 `localhost`）。
3. 连接成功后大厅自动收起（露出游戏），Mirror 自带 HUD 自动隐藏；断开时恢复。

设计要点：

- **UI 完全不碰 Mirror**：面板只认 `NetLobbyBridge`，桥接器再转发 `NetGameManager` —— 换网络库或改直连方式都不动 UI。
- 玩家名缓存 key 由 `NetPlayerName.PrefsKey` 统一管理，UI 只调 `SetLocalName`。
- 地图列表与存档菜单同源（`persistentDataPath/*.map`），"菜单里存的地图"和"大厅里能选的地图"是同一批。
- Mirror 的 `NetworkClient` 静态事件在 `Shutdown` 时会被清空 → 状态检测一律**轮询** `NetworkServer.active` / `NetworkClient.isConnected`，并自带 12 秒连接超时。
- 局域网发现的响应结构体是 struct（`NetworkDiscoveryBase` 用 `info == null` 做装箱判断，struct 永远非 null）→ **无法用返回 null 抑制响应**，故"已在游玩"只体现在 `inGame` 字段上。

验收：EditMode 断言 **40/40**（`tools/ui_m4_assertions.exec.cs`）：prefab 与表项、NetRoot / EllenNet 接线、`Sanitize` 边界、PlayerPrefs 读写与恢复、地图工具、面板文案（全部走文案表）/ 下拉填充 / 按钮可用性 / 空列表 / 刷新不抛异常 / 遮罩与阻塞复位。

### M5 收尾（一键装配 / 检查 / 断言集 / 回归清单）✅

**菜单（`Tools/UI/`）**

| 菜单 | 作用 |
|------|------|
| `一键装配 UI 框架 (UIRoot)` | 1️⃣ 场景 UI 根：Canvas + UIManager + 输入门禁 + 容器 + 遮罩 + 面板表 |
| `一键装配 框架 + 背包（不含联机）` | 1️⃣+2️⃣ |
| `一键装配 联机大厅 UI (lobby)` | 3️⃣ 大厅面板/表项 + `NetRoot` / `EllenNet` 接线（需先有 NetRoot） |
| `一键装配 全部 UI（框架 + 背包 + 大厅）` | 1️⃣+2️⃣+3️⃣（没有 NetRoot 时自动跳过联机部分并提示） |
| `检查 UI 装配状态` | 打印场景对象 / 面板表每一项 / 表资产自检结果 |
| `数据表/全部导出 CSV（从资产）` `数据表/全部导入 CSV（到资产）` | 与 CSV 源表互转 |
| `移除场景 UIRoot` | 只删场景根对象，面板表与 prefab 保留 |

**断言集**（`tools/`，一键跑：`bash tools/run_ui_assertions.sh [工程路径]`）

| 脚本 | 覆盖 | 结果 |
|------|------|------|
| `ui_m1_assertions.exec.cs` | 框架：表注册 / 开·关·栈 / 互斥组 / 遮罩 / 阻塞广播 / Esc / 热键 / 中文字体 | 39/39 |
| `ui_m2_assertions.exec.cs` | 数据表：导入幂等 / GUID 不变 / 字段不丢 / 缺 key 回退 | 22/22 |
| `ui_m3_assertions.exec.cs` | 背包：控件接线 / 卡片 / 点卡片 → 进入种植模式 / 自动收起 | 35/35 |
| `ui_m4_assertions.exec.cs` | 联机大厅：接线 / `Sanitize` / PlayerPrefs / 地图工具 / 面板文案与按钮态 | 40/40 |
| `ui_m5_regression.exec.cs` | 装配完整性 + 旧系统接缝（HexGrid / HexMapEditor / PlantingSystem / HexMapCamera / HUD）+ 25 个文案 key | 15/15 |

| `ui_m6_ui_click` | 27 | **UI 点击可达性**（模态遮罩层级 / 谁先接到射线 / 控件不被遮罩盖住） |

合计 **178 条断言全过**。

> `ui_m6_ui_click` 的意义：EditMode 下 `Graphic` / `EventSystem` 的 `OnEnable` 不执行，
> 真正跑 `GraphicRaycaster` 拿不到命中，所以改用**层次展开法**复现 uGUI 的画家算法 ——
> 把画布按"父先子后、兄弟按序"深度优先展开，列表中位置越靠后 = 渲染越晚 = 越先接到射线。
> 于是"点击到底被谁接住"在 EditMode 里也能被精确断言。

### 踩坑与修复记录

| 现象 | 根因 | 修复 |
|------|------|------|
| 背包永远显示"背包里还没有种子"（种子表里明明有 2 条） | `InventorySystem` 用 `loaded=true` 把**空结果**永久缓存：脚本重编译 / 资产重导入的窗口期里 `SeedTable` 引用会短暂失效，读到空后再也不重载 | 改为**自愈式**：只要当前是空的就按 `reloadRetryInterval`（0.5s）节流重试；`loaded` 仅在读到非空时置位。另在 `InventoryPanel.Update()` 每秒核对一次条目数与卡片数，不一致就重建（表稍后可读时自动补上） |
| 一键脚本里后几个套件报"场景对象缺失"/无输出，单独跑却全过 | M2 套件执行 CSV 导入 → 触发资产重导入 + 脚本重编译；下一个套件在**编译/域重载期间**执行，`Find`/`FindObjectOfType` 会返回 null、exec 也可能空返回 | `run_ui_assertions.sh` 在每个套件前插入**编译屏障**：`unity-cli editor refresh --compile`（阻塞到编译结束）+ 1 秒间隔 |
| 断言中断会在场景里残留 `UITestRoot*` 对象（可能被误存进场景） | 断言中途异常时 `DestroyImmediate` 没执行 | 每个断言脚本开头统一清理 `UITestRoot*` / `UITestDiag*` 遗留对象 |
| **★ Play 后点 UI 任何位置都会把 UI 关掉，根本没法创建房间** | 模态遮罩是**全屏 Image + Button**，靠 sibling 顺序决定谁先接到射线；而它被静态固定在 `Panels` 之后（`Panels(0) → Modal Mask(1) → Popups(2)`），于是遮罩盖住了 `Panels` 里的所有面板（`bag`/`lobby` 都是 Normal 层模态面板，都进 `Panels`）→ 点击面板任意位置（含"创建主机"按钮）都先命中遮罩，被当作"点了遮罩"→ `CloseTop` 立刻关掉面板 | `UIManager.RefreshMask()` 改为**每次打开/关闭模态面板时把遮罩挪到"最上层模态面板"正下方**（同一容器内、sibling 差 1）：既挡住它下面的面板与游戏世界，又不挡面板自己。`CreateMask()` 的静态停放位置也改为面板容器**之下**（安全默认值），`UISetupTool` 同步（`Modal Mask(0) → Panels(1) → Popups(2)`） |
| 点遮罩关掉的不是最上层那个面板 | `CloseTop()` 按【打开顺序】取最后一个，而遮罩与排序按【层 → order → 打开序】定位 —— 面板表里 `order` 不同时两者不一致（实测 `bag` 后开但 `lobby` order 更高 → 视觉最上层是 `lobby`，点遮罩却关了 `bag`） | `RefreshMask()` 记录 `maskOwner` = 遮罩当前所贴的面板；`OnMaskClicked()` 直接关 `maskOwner`（不在则不退化为 `CloseTop` 的兜底）。**教训：同一个"栈顶"在一个框架里只能有一种定义** |
| **联机后 NetRoot 被整个停用**：客户端永远卡在「连接中…」、局域网发现失效 | `NetLobbyBridge.ApplyMirrorHudVisibility` 隐藏的是 HUD 所在的 **GameObject**，而 Mirror 的 `NetworkManagerHUD` 恰好与 `NetworkManager` / `Transport` / 本桥接器**挂在同一个物体**（NetRoot）上 → 一隐藏就把整个联机根节点停掉，`Update()` 不再轮询状态 | 改为只 **禁用 HUD 组件**（`mirrorHud.enabled = !IsOnline`），字段类型从 `GameObject` 改为 `NetworkManagerHUD`，装配工具同步存组件引用。**教训：需要"隐藏某个 UI"时，先确认它是否与逻辑组件同物体，优先禁用组件而非隐藏物体** |
| 双实例（HexMap + HexMap2）共享 `Assets` 时，一端保存场景会让**另一端弹模态框**并卡死 | 一端 `SaveScene` 后，另一实例检测到外部修改，弹出原生模态框「The open scene(s) have been modified externally」（`#32770`，按钮 Reload / Ignore），**主线程被阻塞** → 该实例 `unity-cli` 全部超时（表现为 `not responding`、`cannot reach Unity health endpoint`），且该实例的程序集不会重编译（用旧程序集跑会报"字段类型不匹配"之类编译错） | 联调流程必须**串行**：一端保存后，另一端先处理模态框（点 Reload）→ 再 `editor refresh --compile` → 才开始 Play；期间不要再次保存场景。诊断用 Win32 `EnumWindows` 找 `#32770` 窗口；恢复用 `SendMessageTimeout(btn, BM_CLICK)` 点 Reload（无需抢占前台） |

### 回归检查清单（Play 时人工确认）

1. **不开面板**：WASD + 鼠标 + 空格跳跃正常；相机点击地面才转向；`HexMapEditor` 刷地形/特征正常；种植（选卡 + 点地面）正常；存档菜单保存/载入正常。
2. **按 B 开背包**：角色立即停止响应移动/跳跃、鼠标指针出现；卡片显示名称/阳光/地形，阳光不足变红；点卡片 → 进入种植模式、背包自动收起 → 点地面种下 → 背包里该卡显示冷却遮罩；Esc 或点遮罩关闭背包，角色恢复操作。
3. **按 F2 开大厅**：玩家名可编辑并记住（重启工程仍在）；房主选地图 → 创建主机 → 显示本机 IP；加入者刷新 → 列表出现房间 → 点击连接 → 大厅自动收起、HUD 隐藏；断开后 HUD 与单机玩法恢复。
4. **联机**：双端能看到对方移动/攻击/脚步；断线或房主退出时客户端回到单机（不卡死、无残留玩家）。
5. **UI 与玩法互不干扰**：面板开着时点 UI 不会同时在地图上刷地形/种植（遮罩 + `uiModalActive` 门禁）。

## 里程碑（每步可独立验证）

| M | 内容 | 验收 |
|---|------|------|
| M1 ✅ | 通用 UI 骨架：`UISystem` 全套 + `HexControlMode.uiModalActive` + `UICharacterInputGate` + 一个自检面板 | ✅ 编译干净 + EditMode 断言 **39/39**（Play 目视待用户确认） |
| M2 ✅ | 表管线：CSV + `TableImporter` + 三张表 + `PlantConfig` 增量更新 | ✅ EditMode 断言 **22/22**（GUID 不变、字段不丢、重导入幂等） |
| M3 ✅ | 背包 UI：`InventorySystem` + `InventoryPanel` + `SeedCardView`（B 键、种子格、阳光 / 冷却、点击选卡） | ✅ EditMode 断言 **35/35**；Play 目视待用户确认 |
| M4 ✅ | 联机大厅 UI：`NetLobbyBridge` + `NetLobbyDiscovery` + `NetPlayerName` + `LobbyPanel`（玩家名缓存 / 选地图开房 / LAN 列表 / IP 直连） | ✅ EditMode 断言 **40/40**；⏳ 双端 Play 联调待做 |
| M5 ✅ | 收尾：一键装配总入口 / 检查菜单 / 断言集 + 一键脚本 / 回归清单 | ✅ 断言 **151/151**（39+22+35+40+15）；⏳ 人工 Play 目视待用户 |
| M6 ✅ | **UI 点击可达性修复**：模态遮罩改为动态贴到最上层模态面板下方 + 点遮罩精确关闭所贴面板 | ✅ 断言 **27/27**（新增 `ui_m6_ui_click`，层次展开法验证射线顺序）；全套 **178/178** |

## 接口依赖

- 通用层：仅 `UnityEngine.UI`（legacy `Text` / `Button` / `InputField` / `ScrollRect` / `Slider`）
- 游戏侧：`PlantingSystem` / `PlantConfig`（已有）、`NetGameManager`（新增 API）、Mirror
  （`NetworkDiscoveryBase` / `NetworkBehaviour` / `SyncVar`）、`HexGrid.Load` + `.map` 格式、`NaughtyCharacter.Character`
- 不引入：FairyGUI、DOTween 等第三方

## 验证方式

1. **EditMode 断言**（`unity-cli exec`，方法体、全限定名）：面板注册 / 栈 / 互斥 / 热键 / 仲裁位、
   表导入结果、背包选卡 → 种植链路、`MapFileUtil` 列表与载入
2. **视觉**：`unity-cli screenshot --view game`（需进入 Play，**先与用户确认**）
3. **联机**：双实例（HexMap 作 Host + HexMap2 作 Client，同机）已验证 ✅ —— 局域网发现 / 点击列表连接 / 玩家名同步 / 位置同步 / HUD 接管 / 停 Play 复位全部通过
4. **回归**：不开面板时角色移动 / 相机 / 地图编辑 / 存档 / 种植行为不变

## 风险与待办

- 中文字体：首版 OS 动态字体（Windows 开发机）；发布期需换内置字体资产（TMP 或 OTF 打包）
- `NetworkManagerHUD` 与自制大厅并存期：先并存，面板就绪后隐藏 HUD（Inspector 开关保留）
- `docs/pvz3d.md` 的 FEAT-005 原写「FGUI」→ 需同步改为「uGUI（本框架）」，避免文档矛盾（M5 一并做）
- 联机验证需出包 / 双端，属人工确认项

## 变更记录

- 2026-09-11：**修复"Play 后点 UI 任何位置都会关闭 UI"（无法创建房间）** —— 全屏模态遮罩被静态固定在面板容器之上，导致所有点击都被遮罩接住并触发"点外部关闭"。改为 `RefreshMask()` 动态把遮罩放到**最上层模态面板正下方**（同容器）；同时修掉"点遮罩关掉的面板不是最上层那个"（`CloseTop` 用打开序、遮罩用视觉层级，两者不一致）→ 新增 `maskOwner` 精确关闭；`UISetupTool` 静态默认顺序改为 `Modal Mask(0) → Panels(1) → Popups(2)`；新增 `tools/ui_m6_ui_click.exec.cs`（27 条，层次展开法复现 uGUI 画家算法，EditMode 也能断言"谁先接到射线"）；全套断言 **178/178**
- 2026-09-11：**双端 Play 联调通过**并修复联调发现的 bug —— `NetLobbyBridge.ApplyMirrorHudVisibility` 改为只禁用 HUD 组件（原先隐藏 NetRoot 物体导致联机根节点被停用、客户端永远卡在"连接中"、局域网发现失效）；记录双实例共享 Assets 时的「场景被外部修改」模态框卡死坑与串行联调流程；`UILobbySetupTool` 同步存组件引用
- 2026-09-11：**修复背包空数据锁死 bug**——`InventorySystem` 改为自愈式重载（节流重试）+ `InventoryPanel` 每秒兜底核对卡片数；`run_ui_assertions.sh` 加编译屏障 + 断言脚本加遗留对象清理；全套断言 **151/151**
- 2026-09-11：**M5 收尾**——`Tools/UI/一键装配 全部 UI（框架+背包+大厅）` 总入口、`检查 UI 装配状态` 菜单、`tools/run_ui_assertions.sh` 一键跑 5 套断言（合计 151 条）、回归检查清单；`LobbyPanel.RefreshFromBridge()` 公开刷新入口（测试用，避免 SendMessage）
- 2026-09-11：**M4 联机大厅 UI 落地**——`NetLobbyDiscovery`（自定义局域网发现，广播房主名/地图/人数）、`NetPlayerName`（玩家名缓存 + SyncVar 同步）、`NetLobbyBridge`（大厅 ↔ Mirror 桥，含连接超时与 HUD 接管）、`MapFileUtil`（.map 列表/载入，与存档菜单同格式）、`LobbyPanel` + `LobbyServerRow`、`Editor/UILobbySetupTool.cs`（一键装配面板/表项/NetRoot/EllenNet）；EditMode 断言 40/40
- 2026-09-11：**M3 背包 UI 落地**——`InventorySystem`（种子表数据源 + 阳光/冷却 + 选卡转发，预留掉落/数量扩展点）、`InventoryPanel`（B 键开关 / 网格卡片 / 阳光与冷却实时刷新 / 点击选卡后自动收起）、`SeedCardView`（卡片显示 + 点击上报）、`Editor/UIBagSetupTool.cs`（一键装配卡片与面板 prefab + 注册 bag 表项 + 场景 `[Inventory]`）；EditMode 断言 35/35
- 2026-09-11：**M2 数据表管线落地**——`TableCsv`（CSV 读写）+ `TableImporter`（导入/导出菜单）+ `SeedTable` / `UITextTable` 资产类型；SeedTable.csv / UIPanelTable.csv / UITextTable.csv 三张源表 + 导入产出的 .asset；种子导入按 id 稳定 upsert（GUID 与场景引用不变）；EditMode 断言 22/22
- 2026-09-11：**M1 通用 UI 骨架落地**——新增通用层 `Assets/UISystem/`（UIManager/UIPanel/UIPanelTable/UIFontProvider + 自检面板）、游戏侧 `UICharacterInputGate`、装配工具 `Editor/UISetupTool.cs`；`HexControlMode` 新增 `uiModalActive`，`HexMapEditor`/`HeroController`/`PlantingInput` 各加 1 个让出条件；编译零错误零警告，EditMode 断言 39/39 通过；场景 `[UIRoot]` 待用户 Ctrl+S
- 2026-09-11：建档。技术栈定 **uGUI + 表驱动**（已评估 FGUI：本机编辑器无专业版授权、命令行发布不可用）；
  确定分层 / 接触面 / 数据表方案 / M1~M5 里程碑；待用户确认后开工
