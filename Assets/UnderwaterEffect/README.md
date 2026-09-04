# UnderwaterEffect（通用水下后处理模块）

完全独立、可复用到任意 Unity 项目（内置管线 / 无 SRP 依赖）的全屏水下视觉效果。

## 文件
| 文件 | 说明 |
|---|---|
| `UnderwaterEffect.cs` | 组件：挂在相机上，判定入水并按深度驱动效果（零游戏逻辑） |
| `Editor/UnderwaterSetupTool.cs` | 菜单 `Tools/水下效果/…`：一键装配 / 重置 / 移除 |
| `../Resources/Shaders/Underwater.shader` | 效果 shader（`Custom/Underwater`），跨项目拷贝需一并携带 |

> `IWaterSurfaceProvider` 接口也定义在 UnderwaterEffect.cs 内，桥接水面系统用。

## 使用（三步）
1. 拷贝 `UnderwaterEffect.cs` + `UnderwaterSetupTool.cs` + `Underwater.shader`（保持 Resources 下）
2. 菜单 `Tools/水下效果/装配到主相机`
3. 提供水面高度，三选一：
   - **最简**：`Surface Y` 填水面世界高度常数
   - 或把水面物体拖到 **Surface Object**（取它世界 Y，水面物体移动/上下浮动也会跟随）
   - 或写一个实现 `IWaterSurfaceProvider` 的组件拖到 **Surface Provider**（任意地形/格子水面）

`Probe Transform` 可设角色：角色/相机任一低于水面即触发，没入越深效果越强；出水自动淡出。

## 效果组成（shader 内）
深度雾（远处沉向深水色）、随距离折射扰动（近处清晰）、世界空间 caustics 光斑
（噪声纹理运行时自生成，贴地、不随镜头漂移）、头顶"透过水面看天空"天窗、
暗角、降饱和压暗。全部参数在组件 Inspector 实时可调。

## 已验证参数默认
水面下方默认效果较强但可辨：雾 0.18 / 焦散 0.6 / 天窗 1.2。每项目建议按水面颜色重新调
`Water Color` / `Deep Color`。
