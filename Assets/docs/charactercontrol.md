# 角色控制（charactercontrol）
    角色的prefab为Resources/Prefabs/Hero.prefab
## 目标
在地图生成的基础上实现角色：移动、跳跃、攻击，支持地图游览。

## 当前状态（2026-09-03）
- ✅ 角色最小移动：鼠标左键点击地图任意格 → 角色直线移动到该格中心（无寻路，直线插值）
- ✅ 点击转向：角色只在点击地面时转向点击点（faceMouse 默认关，鼠标悬停画面不转）
- ✅ 第三人称跟随相机：相机在角色正后方，随点击转向平滑转到新方向；滚轮缩放
- ✅ 第一人称/第三人称切换：按 V 切换；第一人称相机在角色眼睛高度、朝向与角色一致
- ⏳ 跳跃：未做（设计见下，待确认后实现）
- ⏳ 攻击：未做（待定）
- 输入仲裁：角色控制激活时地图编辑自动让出鼠标（HexControlMode.heroActive）

## 新增/改动文件
| 文件 | 类型 | 说明 |
|---|---|---|
| `Scripts/HeroController.cs` | 新增 | 点击移动 + 面向鼠标（挂到 mushrole） |
| `Scripts/HeroFollowCamera.cs` | 新增 | 跟随相机：第三人称(角色后方)/第一人称(眼睛) 切换，挂到 Hero Camera |
| `Scripts/HexControlMode.cs` | 新增 | 角色控制 ↔ 地图编辑 输入仲裁静态开关 |
| `Scripts/HexMapEditor.cs` | 改动 | Update 开头加 `HexControlMode.heroActive` 检查，角色激活时跳过编辑 |
| `Scripts/HexMapCamera.cs` | 改动 | Locked/ValidatePosition 加 null 保护（相机被隐藏后 Save/Load 菜单不再 NRE） |
| `Editor/HeroSetupTool.cs` | 新增 | 菜单 `Tools/角色控制/装配角色与跟随相机` 一键装配 |

## 设计要点
### 移动（当前实现）
- 交互：鼠标左键点击（`EventSystem` 不拦截时）→ `Physics.Raycast` 命中地形 collider → `HexGrid.GetCell(HexCoordinates.FromPosition(...))` 取格 → 直线移动到格中心顶面
- **不做寻路**：起点到终点直线插值（含 Y），中途可能穿坡——寻路留给后续自行扩展
- 移动中可再次点击更换目标（从当前点直线前往新目标）
- 到达判定 `arriveDistance=0.15`；速度 `speed=14`（格直径约 17.3，一格约 1.2s）
- 格中心顶面 = `cell.transform.position`（含海拔与 ±1.5 垂直扰动）
- Start 时 `SnapToGround()`：把角色 y 吸附到所在格顶面

### 面向鼠标 / 点击转向（当前实现）
- **点击驱动**：左键点击地面时，角色平滑转向点击的地面点（FaceTo 持续转向至到位）并直线移动过去
- **鼠标悬停不实时转向**（faceMouse 默认 false）——避免第三人称下画面随鼠标持续转动
- 第一人称下（firstPersonView，由 HeroFollowCamera 切换时自动设置）角色实时面向鼠标=视角控制
- `faceMouse` 手动开启可恢复"悬停即转向"（一般不需要）
- `facingOffset`：模型正脸相对 transform.forward 的补偿角（若脸朝向反了设 180）
- `SnapFacing(worldPoint)`：立即转向（测试/复位用）
- 转向只转 Y 轴（水平），不俯仰；turnSpeed=360°/s

### 第三人称跟随相机（当前实现）
- 相机水平角 = 角色朝向（`followTargetYaw`），即**相机始终在角色视野方向的正后方**
- 角色只在点击地面时转向 → 相机只在点击后平滑绕到新方向一次，**悬停鼠标画面不持续转动**
- `yawOffset` 可绕到侧后方（0=正后）；pitch=35~40 俯视；`SmoothDamp` 平滑跟随
- 滚轮缩放 distance（12~90）；LookAt 角色上方 lookHeight
- 旧 HexMapCamera 的 RTS 控制逻辑完全未动

### 第一人称（当前实现）
- 按 **V** 切换；相机移到角色眼睛高度（eyeHeight=9，角色身高约 10.7 世界单位）
- 相机朝向 = 角色朝向（角色面向鼠标 → 第一人称即看向鼠标方向）
- 切换时自动隐藏角色自身网格（`hideSelfInFirstPerson`，避免遮挡视线；selfRenderer 自动查找 target 下 Renderer）
- 切回第三人称恢复显示

### 输入仲裁
- `HeroController.active` 勾选时（默认），`OnEnable` 置 `HexControlMode.heroActive=true`，HexMapEditor 的 Update 直接 return 不编辑
- 取消勾选或禁用角色 → 交还地图编辑

## 场景装配（已做，SampleScene）
- `HexGrid/mushrole`：挂 HeroController（hexGrid=HexGrid，active=on）
- `Hero Camera`（根级）：Camera(tag=MainCamera, FOV60) + AudioListener + HeroFollowCamera(target=mushrole, mode=ThirdPerson, toggleKey=V, pitch=40, followTargetYaw=on, distance=45, eyeHeight=9, lookHeight=8)
- 旧相机：`HexMapCamera/Swivel/Stick/Main Camera`——**使用游览视角前先在场景中隐藏 HexMapCamera 物体**（HexMapCamera 已加 null 保护，隐藏后 Save/Load/NewMap 菜单不再报错）

## 接口 / 依赖
- 依赖：HexGrid / HexCell / HexCoordinates / HexMetrics（取格、读格顶高度）
- HexGrid.GetCell(HexCoordinates) 有边界检查（越界返回 null），点击取格安全
- 待跳跃/寻路使用：HexCell.Elevation / GetNeighbor / IsUnderwater / HasRiver / GetEdgeType

## 验证方式（已实测 Play 模式）
- 角色 SnapFacing(100,0,100) → yaw=45°、forward=(0.71,0,0.71) ✅
- 第三人称相机位置与「角色正后方」理论值误差 < 1e-4 ✅（点积 0.766=cos40 俯仰正确）
- SetMode(FirstPerson) → 相机位置=角色+up×9、rotation=角色 rotation ✅；角色网格隐藏 ✅
- SetMode(ThirdPerson) → 相机回正后方、网格恢复 ✅
- 点击转向：FaceTo(点击点) → 角色平滑转到该方向、相机绕到正后方 ✅
- 悬停稳定：faceMouse=false 时 1.5s 内角色 yaw 完全不变（画面不持续转动）✅
- FP/TP 切换联动 firstPersonView ✅；移动中相机跟随平移但视角方向稳定 ✅
- 编译零错误；CodeTxts 已同步

## 后续规划（待确认后实现）
- 跳跃：Space 触发；朝当前朝向相邻格，`目标.Elevation - 当前.Elevation == 1` 可跳上（elevationStep=5 一级台阶）；= -1 为下坡正常走；悬崖(≥2)/水域/边界跳不上 → 原地小跳反馈；跳跃中锁定移动；抛物线 ~0.5s
- 攻击：待定
- 移动升级：寻路（BFS/地形通行规则：Cliff ≥2 不可走、水域不可走）

## 变更记录
- 2026-09-03：修正为点击驱动——相机朝向角色点击的地面方向，鼠标悬停不再实时转动画面；第一人称仍实时面向鼠标（视角控制）
- 2026-09-03：角色面向鼠标 + 相机跟随角色视野方向 + 第一/第三人称切换（V）
- 2026-09-03：实现角色点击移动 + 第三人称跟随相机 + 输入仲裁（初始版）
- 2026-09-02：建档（骨架）
