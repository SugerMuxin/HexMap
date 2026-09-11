# 多人局域网（multiplayer）

> 将当前单机 HexMap 改造为**局域网多人共存/联机**游戏。框架选定：**Mirror**（免费开源，房主制，最适合纯 LAN）。
> 本文档为方案文档，实施按文末「分阶段里程碑」推进；动手前先经用户确认。

## 目标
- 局域网内多玩家同场：房主开房，他人输 IP（或自动发现）加入
- 所有客户端看到**同一张地图**；各自操控自己的角色（Ellen），互见对方移动
- 网络功能全部以**新增**方式落地：现有单机系统（HexGrid/角色控制/相机/编辑/存档）零改动、仍可单机游玩
- 为后续玩法（攻击判定、动画同步等）预留扩展点

## 当前状态（2026-09-07）
- 方案已确认（框架 = Mirror，房主制）
- **阶段 0.5 代码已完成（待编译 + 场景装配）**：
  - 新增 `Scripts/Net/NetGameManager.cs`（NetworkManager 子类：地图闸门 RequestMap→下发 .map 字节→客户端加载完发 ClientReady→AddPlayerForConnection 延迟 spawn；会话开始隐藏场景单机角色/结束恢复；纯客户端只读 + 隐藏菜单）
  - 新增 `Scripts/Net/EllenNetController.cs`（远端 !isLocalPlayer 禁用 Character + CharacterController）
  - 改动 `HexControlMode.cs`（+`netClientReadOnly` 静态标志）、`HexMapEditor.cs`（Update 仲裁加该标志，1 行）
  - 新增 `Editor/HexNetSetupTool.cs`（菜单 **Tools/多人联机/一键装配 EllenNet + NetRoot（SampleScene）**：以 Ellen.prefab 建 variant + NetworkIdentity/NetworkTransformReliable(World/C2S)/EllenNetController，建 NetRoot=NetGameManager+KcpTransport+NetworkManagerHUD 并保存场景；另有清理菜单）
  - 装配前确认的 Ellen 驱动链：Ellen.prefab 根挂 Character(Controller=PlayerController.asset SO)+CharacterAnimator+CharacterController；场景级 PlayerInput.prefab 提供输入、CameraRig(PlayerCamera) 跟随——均场景单例 → 每端只留"自己的活角色"，相机/输入天然归位（无需额外绑定代码）
- 现状核对结论：
  - 世界：`HexGrid` seed 确定性生成 + `Save/Load` 二进制 .map（persistentDataPath）→ 客户端可本地重建同一世界，网格无需网络化
  - 角色：Ellen（NaughtyCharacter + CharacterController）+ CameraRig 主相机；旧 mushrole/HeroController 体系已 inactive（docs/charactercontrol.md 滞后于场景，实施前需口头确认 Ellen 当前的输入驱动方式：WASD 还是点击移动）
  - 工程：Unity 2022.3，已装 Input System / uGUI；Packages 无任何网络库

## 设计要点

### 1. 总体架构：房主制（Host）
- 房主 = 服务器 + 玩家一体（`NetworkManager.StartHost`）；客户端 `StartClient` 输 IP 直连
- 局域网发现：Mirror 的 `NetworkDiscovery` 组件（UDP 广播），后期可免输 IP
- 权威模型：
  - 角色位置 = **client authority**（各自驱动自己的角色，局域网低延迟足够；NetworkTransform 上报）
  - 以后攻击/竞技判定 = 升级为**房主权威**：`[Command]`（客户端→房主）判定 + `[ClientRpc]` 广播结果——Mirror 原生支持，无需换框架

### 2. 世界一致性（网格不进网络层）
利用现成 seed/.map 机制，客户端本地重建世界：
1. 客户端加入后 `[Command] RequestMap()`；
2. 房主把当前地图**统一序列化为 .map 字节流**（复用 `HexGrid.Save(BinaryWriter)` → MemoryStream，几十~几百 KB，LAN 无压力）`[TargetRpc] SendMap(byte[])` 发给该客户端；
   - 纯 seed 生成的地图后期可优化为只传 seed+尺寸，首版统一走字节流最简单
3. 客户端把字节写入临时 .map 文件 → 复用现成 `HexGrid.Load` 加载 → 世界一致（零新增反序列化代码）。

约束：
- 客户端**禁用地图编辑**：`HexMapEditor` 的 Update 增加联机远端检查（扩展 `HexControlMode` 的仲裁思路，新增 netMode 判断），客户端隐藏/禁用 NewMapMenu / SaveLoadMenu 入口（自制联机菜单时一并处理）
- 只有房主（编辑模式下）可改地图

### 3. 角色同步（核心，全部新增）
- 新建玩家 prefab **`EllenNet`**（从现 Ellen 派生）：`NetworkIdentity` + `NetworkTransform`（clientAuthority）+ `EllenNetController`
- 挂到 `NetworkManager.playerPrefab`：每连接自动 spawn 一个
- **本地玩家（hasAuthority）**：NaughtyCharacter 原样运行（输入 + CharacterController 驱动）
- **远端玩家**：`EllenNetController` 在非 authority 端禁用 CharacterController 与输入组件（避免双写抖动——NetworkTransform×CharacterController 的经典坑，社区成熟规避法），NetworkTransform 插值显示；动画先 Idle，动画同步放阶段 3
- 若现行输入是「点击移动」类：点击射线只在本地 owned 实例响应，天然互不干扰

### 4. 相机与 UI
- **相机无需新绑定代码**（优于初版设想，撤销 NetCameraRigBinder）：NaughtyCharacter 架构中 `PlayerInputComponent`/`PlayerCamera` 均为**场景级单例**，由"活角色"的 `Controller`（SO）驱动。只要保证每客户端只有一个活角色（自己的），相机天然只跟随本地角色、输入天然只喂本地角色。
- 联机菜单：阶段 0 先用 Mirror 自带 `NetworkManagerHUD`（Host/Client + IP 输入）；后期换自制 uGUI 面板（与 SaveLoadMenu 风格一致），加「开始联机/连接/自动发现」入口

### 5. 场景/脚本清单（全部新增，单机系统零改动）
| 新增 | 类型 | 说明 |
|---|---|---|
| `NetRoot`（SampleScene 根对象） | 场景对象 | NetworkManager + NetGameManager + NetworkDiscovery（阶段 0 附 HUD，后期挂自制菜单） |
| `EllenNet` prefab | 预制体 | **Ellen.prefab 的 Prefab Variant**：根上加 NetworkIdentity + NetworkTransformReliable（coordinateSpace=World / syncDirection=ClientToServer / 开插值+旋转）+ EllenNetController；注册为 NetworkManager.playerPrefab |
| `Scripts/Net/NetGameManager.cs` | 新增 | 地图闸门 RequestMap→SendMap(byte[])→MapReady→AddPlayerForConnection；出生点分配；地图只读标志 |
| `Scripts/Net/EllenNetController.cs` | 新增 | 远端（!isLocalPlayer）：Character.enabled=false + CharacterController.enabled=false；本地全默认。一行禁用阻断移动/重力/相机驱动 |
| `Scripts/Net/NetMenuUI.cs` | 新增（后期） | 自制 uGUI 联机菜单（发现/直连） |
| `HexControlMode.cs` | 改动 | 加 static 标志 `netClientReadOnly`（联机客户端跳过地图编辑，单机默认 false） |
| `HexMapEditor.cs` | 改动 | Update 开头仲裁加该标志（1 行）；NewMap/SaveLoad 菜单在客户端由 NetGameManager SetActive(false) |

### 6. 分阶段里程碑
| 阶段 | 内容 | 验证方式 |
|---|---|---|
| 0 | 装 Mirror（Asset Store 免费「Mirror Networking」，当前稳定版兼容 Unity 2022.3 LTS；**实施前先确认 Ellen 输入驱动方式**）+ 新场景双端连通 | Editor 作 Host + Standalone Build 作 Client（同机或两台局域网机器）。⚠️ 同机双开 Editor 共用 Library 有风险，勿用 |
| 1 | 世界一致：RequestMap/TargetRpc 传 .map 字节 | 两端看到同一张图（含河流/海拔/手工编辑） |
| 2 | 角色共存：spawn + NetworkTransform + 相机跟随分流 + 输入 owner 分流 | 双端各自移动，互见对方平滑移动 |
| 3 | 玩法扩展：动画同步、断线处理、攻击判定（房主权威）、自制联机菜单 | 双端联机实操 |

## 接口依赖
- **Mirror 包**（Asset Store，免费）：NetworkManager / NetworkBehaviour / [Command]/[TargetRpc]/[ClientRpc] / NetworkTransform / NetworkDiscovery
- 现有复用：`HexGrid.Save/Load(BinaryReader, header)`、`HexGrid.NewMap(seed)`、`HexControlMode`（输入仲裁扩展）、NaughtyCharacter（Ellen/CharacterController）、CameraRig、uGUI
- 不动：HexGrid/HexCell/HexMesh 生成逻辑、HeroController/HeroFollowCamera（旧体系）、HexMapCamera、HexMapEditor（仅加远端检查）、存档 UI（仅客户端隐藏入口）

## 验证方式
- 阶段 0：Editor(Host) 开房 + Build(exe)(Client) 直连成功，双方进入同一场景
- 阶段 1：房主加载一张带河流/海拔的 .map，客户端连接后看到完全相同的地图
- 阶段 2：双端各驱动角色移动，对方画面平滑跟随；相机各自跟随本机角色；单机（不开房）游玩一切照旧
- 回归：单机点击移动/相机/编辑/存档功能不受影响（网络代码全部新增、默认不激活）

## 变更记录
- 2026-09-07：阶段 0.5 代码完成（NetGameManager 地图闸门 + EllenNetController 远端禁用 + HexControlMode/HexMapEditor 只读 + Editor 一键装配工具）；待 Unity 编译 → 菜单装配 → 双端连通验证
- 2026-09-07：建档（方案：Mirror 房主制局域网；待实施，下一步 = 阶段 0）
