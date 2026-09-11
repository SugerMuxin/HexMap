# 音频系统（audio）

> 为 HexMap 添加全 3D 空间化音频：BGM、场景环境音（瀑布/河流等摆放式音源）、角色事件音（脚步声/涉水/入水）。
> 架构沿用 UnderwaterEffect 的成熟模式：**通用音频模块（零游戏依赖）+ 游戏侧桥接 + 一键装配工具**。

## 目标
- 背景音乐：进入场景循环播放，支持淡入淡出与音量记忆
- 场景音效（**摆放式**）：如瀑布——把一个"音效预制体"拖到瀑布处即发声，3D 空间化（走近大声、走远小声）
- 代码触发音效：角色跑动脚步声（按脚下地形材质区分）、湖区涉水/入水水花、跳跃落地；攻击命中（预留接口）
- 全 3D 效果：AudioListener 位于主相机（CameraRig）→ 所有音源相对听者定位
- 约束（沿项目惯例）：
  - 通用模块不引用任何游戏类型（HexGrid/HexCell/NaughtyCharacter 一律不进通用目录）
  - 全部**新增**，现有系统（HexGrid / NaughtyCharacter / Mirror / 存档）零改动
  - 多人（Mirror）下语义正确：环境音各端本地一致；角色音只响本地角色；远端化身静默

## 需求 → 落点映射
| 需求 | 触发方式 | 落点 |
|---|---|---|
| 背景音乐 | 自动（AudioService Start） | BGM 通道（key `bgm/main`） |
| 瀑布/河流/风声 | **场景中放预制体** | `AmbientEmitter.prefab`（3D 循环音源） |
| 角色跑动脚步声 | 代码（轮询移动状态） | `CharacterFootstepAudio` + key `footstep/<材质>` |
| 湖区涉水 / 入水水花 | 代码（脚下格 IsUnderwater） | 同上；`splash/in`、`splash/out` |
| 跳跃落地 | 代码（IsGrounded 上升沿 + 滞空时长） | `land` |
| 攻击命中 | 代码（攻击系统落地后 1 行调用） | `combat/hit`（**预留 key**） |

## 当前状态（2026-09-07）
- ✅ **A1 框架已实施并装配**：代码（通用层 + 游戏侧 + Editor 工具）编译零错误；
  场景 SampleScene 已装配（见「装配记录」）；CodeTxts 已同步
- ✅ **A5 远端脚步已实施**（2026-09-07）：`NetworkFootstepRelay` 事件广播，客户端可听到彼此脚步（见 §7）
- ⏳ **素材待补**：`Assets/Resources/Audio/` 素材目录树已建好，用户后续把音频文件丢进对应子目录即自动生效（SoundBank `autoLoadFolder` 运行时 LoadAll，**补素材零配置**）；素材缺失时系统静默跳过并一次性警告，不影响游戏
- 主相机 `CameraRig`（NaughtyCharacter）已带 AudioListener（场景 active 唯一，装配时已校验）
- 角色 = Ellen（NaughtyCharacter）：`Character.cs` 公开 `IsGrounded` / `Velocity` / `HorizontalVelocity` / `RemoteSuppressed`（Character.cs:72-90）——脚步挂接零改动第三方库
- 地图查询 API：`HexCell.TerrainTypeIndex`（HexCell.cs:16）/ `IsUnderwater`（:309）；.map 存档首字节即 terrainTypeIndex → 联机客户端本地重建的地图同样可查询
- ⚠️ 语义提醒：`TerrainTypeIndex` 目前是"色号"（HexMetrics.colors 哈希调色板）而非固定语义 → 脚步素材分组靠 `HexGroundAudioBridge.overrides` 映射表（Inspector 配置，当前默认全 Grass，见 §4）

## 设计要点

### 1. 总体架构：通用模块 + 桥接（UnderwaterEffect 同款）
```
Assets/AudioSystem/            ← 通用层：零游戏类型引用，可整目录拷到别的项目
  AudioService.cs              全局音频门面（BGM / 一次性 SFX / 音量 / AudioSource 池）
  SoundBank.cs                 ScriptableObject：key → clip 组（手动拖 或 autoLoadFolder 自动加载）
  AmbientAudioSource.cs        摆放式 3D 环境音源组件（按听者距离淡入淡出）
  IGroundKindProvider.cs       脚下材质分类枚举 + 查询接口（通用，跨项目）
  Editor/AudioSetupTool.cs     菜单 Tools/音频系统/一键装配
Assets/Scripts/Audio/          ← 游戏侧触发与桥接（引用 NaughtyCharacter / HexGrid，与 HexWaterBridge 同思路）
  CharacterFootstepAudio.cs    挂本地角色：读 Character 状态 → 问桥 → 播 SoundBank
  HexGroundAudioBridge.cs      实现 IGroundKindProvider：HexGrid 格 → 地形/水面 → GroundKind
Assets/Resources/Audio/        ← 全部音频资产（用户放素材 + AudioBank 配置）
  AudioBank.asset              SoundBank 实例（装配时自动创建，运行时 Resources.Load("Audio/AudioBank")）
  BGM/                         放 BGM 素材（→ bgm/main）
  SFX/Footsteps/…  SFX/Splash/…  SFX/Ambient/…  按 key 分目录放素材（见 §8）
Assets/Resources/Prefabs/
  AmbientEmitter.prefab        场景音源预制体（AudioSource + AmbientAudioSource，3D 预设开箱即用）
```
依赖方向：`CharacterFootstepAudio → HexGroundAudioBridge（实现 IGroundKindProvider）→ HexGrid`；
播放一律 `AudioService.PlaySfx*(key, …)`。通用层不认识 HexGrid；游戏侧不认识 AudioSource 细节。

### 2. 通用模块（Assets/AudioSystem/）

**AudioService**（MonoBehaviour 单例，DontDestroyOnLoad，场景根对象 `[AudioSystem]`）
- BGM 通道：`PlayBgm(key)` / `StopBgm()`，循环播放 + 淡入淡出（bgmFadeSeconds）
- SFX 池：16 个常驻 AudioSource（各自独立子物体，3D 定位互不干扰），轮转复用：
  ```csharp
  PlaySfxAtPoint(string key, Vector3 position);   // 3D 定位一次性（脚步/水花/命中）
  PlaySfxFollow(string key, Transform follow);    // 3D 跟随目标直到播完
  PlaySfx2D(string key);                          // 无空间化（UI）
  ```
- 音量：`SetMasterVolume / SetSfxVolume / SetBgmVolume`（PlayerPrefs 记忆，即时生效）
- 容错：bank 缺 key / 无 clip → 静默 + 每 key 一次性警告（提示补素材目录）；绝不抛错影响游戏

**SoundBank**（ScriptableObject）——素材以"语义 key"组织，代码不直接引用 clip：
```csharp
SoundEntry { string key;
             AudioClip[] clips;            // 手动拖（可选）
             string autoLoadFolder;        // Resources 目录（如 Audio/SFX/Footsteps/Grass）——丢文件即生效
             float volume; pitchMin; pitchMax;
             bool spatial; float minDistance; float maxDistance; }
```
同 key 多 clip 随机挑（防脚步机械重复）；pitch 随机 ±；3D 衰减 Logarithmic。

**AmbientAudioSource**（摆放式环境音）
- 拖进组件：bank + ambientKey、triggerRadius、淡入淡出秒数
- 进入 triggerRadius → 淡入循环播放（随机起始相位，多实例不同相）；离开 → 淡出停止（不空转）
- 音量实时跟随 AudioService 的 sfx/master
- 配套 prefab `AmbientEmitter`：**场景中复制摆多个**（瀑布口一个、溪流一个），位置即声源

### 3. 瀑布等场景音效（摆放式）用法
1. 把 `Assets/Resources/Prefabs/AmbientEmitter.prefab` 拖到场景瀑布/溪流处
2. Inspector：bank 选 `AudioBank`、key 选 `ambient/waterfall`（或 river）、triggerRadius 按声源大小调
3. 完成——3D 定位自动生效（听者 = CameraRig 的 AudioListener）。多人下各端场景相同 → 声音一致，零网络
（当前场景已放了一个示例摆件 `AmbientEmitter (示例-河流)` 在 HexGrid 中心上空，可拖到真实瀑布处改名使用）

### 4. 角色脚步声与湖区音效（代码触发）
**地面材质分类**：`GroundKind { Grass, Stone, Sand, Water, Default }`（AudioSystem 通用枚举）
- `HexGroundAudioBridge`（挂 HexGrid）：`worldPos → HexGrid 本地坐标 → GetCell`；越界 → defaultKind；
  水下格 → Water（优先于一切）；`terrainTypeIndex` 命中 Inspector 的 overrides 表 → 对应 kind；否则 defaultKind
- 当前 terrainTypeIndex 无语义 → defaultKind=Grass，全部先走 `footstep/grass`；将来在 bridge 的
  overrides 填 `index → kind` 即可细分（如 3 → Stone）

**脚步触发**（`CharacterFootstepAudio`，挂**本地活角色**）
- 每帧读 `Character.IsGrounded` + `HorizontalVelocity`（不动 NaughtyCharacter 一行代码）
- 移动距离累计 ≥ stepDistance（0.7m）触发一步，步频上限 minStepInterval（0.28s≈3.5 步/s）；
  speed < 0.5 不响；每步 `PlaySfxAtPoint("footstep/<kind>", 角色位置)`（同 key 多 clip 随机）
- 入水/出水：脚下 kind Water ↔ 非 Water 跳变 → `splash/in` / `splash/out`（0.8s 防抖）
- 落地：滞空 ≥ airTimeToLand(0.35s) 后落地 → `land`
- **多人纪律**：`character.RemoteSuppressed` 或组件被禁用（EllenNetController 对远端做的事）→ 直接 return，
  远端化身天然静默

### 5. BGM
- AudioService Awake 建 2D BGM 源；Start 时 `autoPlayBgm` → `PlayBgm("bgm/main")` 循环淡入
- 素材放 `Resources/Audio/BGM/`（任意文件名）；音量三档 PlayerPrefs 记忆
- 音量滑块 UI：后置（见待确认 ④），当前 Inspector 调 / 代码 Set*

### 6. 攻击命中（预留）
- 攻击系统未实现。`combat/hit` key 与目录 `Resources/Audio/SFX/Combat/` 已就位，
  届时攻击判定处加一行 `AudioService.Instance.PlaySfxAtPoint("combat/hit", hitPoint)` 即可
- 多人（房主权威）流程：房主判定 → `[ClientRpc]` 广播命中点 → 各端本地 PlaySfxAtPoint → 3D 定位正确

### 7. 多人（Mirror）语义
| 声音 | 处理 | 说明 |
|---|---|---|
| BGM / 瀑布 / 河流 | 各端本地播放 | 世界一致 → 声音一致，**零网络** |
| 本地角色脚步/水花 | 本地组件触发 | RemoteSuppressed 纪律防远端化身发声 |
| **远端角色脚步/水花/落地** | **事件广播（A5 已实现）** | 见下 |
| 攻击命中 | 房主判定 + Rpc 各端播 | 随攻击系统一起做 |

**A5 远端脚步/水花/落地（已实现）**——`NetworkFootstepRelay`（Assets/Scripts/Net/，挂 EllenNet 根，NetworkBehaviour）：
- `CharacterFootstepAudio` 增加 C# 事件 `StepSfxTriggered(key, position)`（本组件不依赖 Mirror，事件解耦）
- **拥有者端**（isOwned，本地活角色）：`OnStartAuthority` 订阅事件 → 每步 `[Command] CmdBroadcastStep(key, pos)` 上报服务器（host 模式本地直通）；
- 服务器 `[ClientRpc] RpcPlayStep` 广播所有客户端；
- 各端收到：**非拥有者端**在本地该角色位置 `PlaySfxAtPoint`（3D 定位正确）；**拥有者端跳过**（本地已播，防双响）。
- 单机（非网络 spawn / 无 relay 的 Ellen）零影响；素材缺失端由 AudioService 容错静默。
- ⚠️ 坑：本工程 Mirror 版本 **无 `hasAuthority` 属性**（编译报 CS0103），判断"本端拥有"用 `isOwned`。
- 装配：菜单 `Tools/多人联机/给 EllenNet 补装脚步网络转发`（幂等；`一键装配 EllenNet + NetRoot` 新建流程已内置）。

### 8. 素材目录约定（Resources/Audio）
素材按 key 的子目录组织，**把音频文件丢进目录即自动生效**（无需改配置/拖引用）：
| 目录 | 对应 key | 说明 |
|---|---|---|
| `BGM/` | bgm/main | 背景音乐（循环） |
| `SFX/Footsteps/Grass/` | footstep/grass | 草地脚步（Default 地形也走这里） |
| `SFX/Footsteps/Stone/` | footstep/stone | 石头脚步 |
| `SFX/Footsteps/Sand/` | footstep/sand | 沙地脚步 |
| `SFX/Footsteps/Water/` | footstep/water | 涉水脚步 |
| `SFX/Splash/In/` | splash/in | 入水水花 |
| `SFX/Splash/Out/` | splash/out | 出水水花 |
| `SFX/Land/` | land | 跳跃/跌落落地 |
| `SFX/Ambient/Waterfall/` | ambient/waterfall | 瀑布环境音（3D 大范围） |
| `SFX/Ambient/River/` | ambient/river | 河流环境音（3D） |
| `SFX/Combat/` | combat/hit | 攻击命中（预留） |

命名建议小写下划线（footstep_grass_01.ogg），同目录多个文件自动随机轮换。导入设置：短音效关 Loop、
Load Type=Decompress On Load；BGM 开 Loop。首版验证可用 Mirror Examples 内置 Kenney/OpenGameArt
免费素材临时充数（Assets/Mirror/Examples/_Common/KenneyAssets/kenney_rpg-audio/Audio/footstep06.ogg 等）。

## 装配记录（2026-09-07 已执行）
菜单 `Tools/音频系统/一键装配（SampleScene）`（幂等，可重复执行）做了什么：
1. 建素材目录树 `Assets/Resources/Audio/`（BGM + SFX 分类子目录）
2. 创建 `Assets/Resources/Audio/AudioBank.asset`：11 条默认 key（缺失才补，保留用户改动）
3. 场景建根对象 `[AudioSystem]`（AudioService + bank 引用；先清旧实例）
4. HexGrid 挂 `HexGroundAudioBridge`
5. 场景活角色 Ellen 挂 `CharacterFootstepAudio`（bridge/character 引用已设）；EllenNet.prefab（存在时）同步加组件
6. 生成 `Assets/Resources/Prefabs/AmbientEmitter.prefab`；场景放示例摆件 `AmbientEmitter (示例-河流)`
7. 校验 active AudioListener 唯一（CameraRig/Main Camera）；保存场景
清理菜单：`Tools/音频系统/清理装配`（只删场景对象/组件/示例，保留 bank/prefab/素材目录）

## 文件清单（全部新增，现有系统零改动）
| 文件 | 位置 | 说明 |
|---|---|---|
| `AudioService.cs` | Assets/AudioSystem/ | 全局门面：BGM/SFX/池/音量 |
| `SoundBank.cs` | Assets/AudioSystem/ | SO：key → clip 组 + autoLoadFolder 自动加载 |
| `AmbientAudioSource.cs` | Assets/AudioSystem/ | 摆放式环境音源 |
| `IGroundKindProvider.cs` | Assets/AudioSystem/ | GroundKind 枚举 + 地面查询接口 |
| `AudioSetupTool.cs` | Assets/AudioSystem/Editor/ | 菜单一键装配 / 清理 |
| `CharacterFootstepAudio.cs` | Assets/Scripts/Audio/ | 本地角色脚步/水花/落地触发（含 `StepSfxTriggered` 事件） |
| `HexGroundAudioBridge.cs` | Assets/Scripts/Audio/ | HexGrid → GroundKind 适配（overrides 映射表） |
| `NetworkFootstepRelay.cs` | Assets/Scripts/Net/ | 联机脚步事件广播（[Command]→[ClientRpc]，A5） |
| `HexNetSetupTool.cs` | Assets/Editor/ | 改动：EllenNet 装配加 NetworkFootstepRelay + 补装菜单 |
| `AudioBank.asset` | Assets/Resources/Audio/ | SoundBank 实例（11 条默认 key） |
| `AmbientEmitter.prefab` | Assets/Resources/Prefabs/ | 场景音源预制体 |

## 验证方式
- 已实测：编译零错误；装配后场景对象/组件/资产齐全（AudioService/HexGroundAudioBridge/CharacterFootstepAudio on Ellen/AmbientEmitter 示例/AudioBank 11 key/AudioListener 唯一）
- Play 验证清单（素材到位后，需用户 Play 实测）：
  - 跑动 → 脚步声节奏随速度、音高随机不机械 ✅；停步即静
  - 跑进湖区 → splash/in + 涉水脚步；跑出 → splash/out（无素材时静默不报错）
  - 瀑布 emitter：走近淡入、走远淡出、3D 衰减
  - BGM 循环淡入；AudioService 音量 PlayerPrefs 记忆
  - 双开联机（HexMap2 做 Client）：本地角色有声、远端化身无声、无 AudioListener 冲突告警

## 里程碑
| 阶段 | 内容 | 状态 |
|---|---|---|
| A1 | 框架 + 装配 + 素材目录约定（本档） | ✅ 已完成（2026-09-07） |
| A2 | 脚步地形细分：bridge.overrides 填 terrainType→材质（需地形语义定稿） | ⏳ 待素材/语义 |
| A3 | BGM 音量 uGUI 滑块（与 SaveLoadMenu 同风格） | ⏳ 待确认 |
| A4 | 攻击命中音（随攻击系统） | ⏳ 待定 |
| A5 | 远端角色脚步/事件音网络化 | ✅ 已完成（2026-09-07，NetworkFootstepRelay） |

## 接口依赖
- NaughtyCharacter：`Character.IsGrounded` / `HorizontalVelocity` / `VerticalVelocity` / `RemoteSuppressed`（只读，不改）
- HexGrid/HexCell：`GetCell` / `HexCoordinates.FromPosition` / `TerrainTypeIndex` / `IsUnderwater`（只读）
- CameraRig：现成 AudioListener（不加第二个）
- 复用模式：UnderwaterEffect（通用模块化）/ HexWaterBridge（桥接写法）/ HexNetSetupTool（Editor 装配写法）
- Mirror（多人时）：EllenNetController 已禁用远端 Character → 脚步组件随 `RemoteSuppressed` 静默，零改动

## 待确认事项
1. ~~素材来源~~ → 用户提供，放 `Assets/Resources/Audio/` 对应子目录（本档已按此实施）
2. **地形脚步分组**：terrainType 色号 → 材质映射（当前全 Grass）。若各色有语义（草地/泥/岩），
   在 HexGrid 上 HexGroundAudioBridge 的 overrides 表填 index→kind，或告知我做映射
3. 湖中音效语义：当前按"涉水脚步 + 进出水 splash"实现；深水游泳环境声待游泳玩法
4. BGM/音量 uGUI 滑块：是否需要（当前 PlayerPrefs + 代码 API）
5. ~~一键装配目标角色~~ → 已确认：场景活角色 Ellen + EllenNet prefab

## 变更记录
- 2026-09-07：A5 远端脚步落地——`CharacterFootstepAudio` 加 `StepSfxTriggered` 事件；新增 `NetworkFootstepRelay`（NetworkBehaviour，[Command]→[ClientRpc] 广播脚步/水花/落地，isOwned 防双响）；HexNetSetupTool 装配与补装菜单；两端（HexMap/HexMap2）EllenNet.prefab 已装配、CodeTxts 已同步、24 项检查通过
- 2026-09-07：桥接器容错加固——`HexGrid.GetCell` 只按 cellCountX/Z 查界、不校验 cells 数组（网格未生成时抛 IndexOutOfRange），`HexGroundAudioBridge.GetGroundKind` 已包 try/catch 保证永不抛异常（EditMode 验证 169 点位 0 异常，优雅降级 defaultKind）
- 2026-09-07：A1 实施完成——通用层 4 组件 + 游戏侧桥接/脚步触发 + Editor 一键装配；SampleScene 已装配；
  素材目录按用户指定改为 `Resources/Audio`（BGM/SFX 分类 + AudioBank.asset 同目录）；CodeTxts 已同步；
  素材缺失时静默容错 + 一次性提示，后续丢文件即生效
- 2026-09-07：建档（方案：通用 AudioSystem 模块 + 桥接分层）
