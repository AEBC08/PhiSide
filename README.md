# PhiSide

用 Godot 4 + C# 实现的 Phigros 谱面演出 / 音游模拟器。

## 环境要求

| 项目 | 版本 |
| --- | --- |
| Godot | **4.7.2 (.NET / Mono 版)** — 必须是 mono 版，否则无法编译 C# |
| .NET SDK | 10.0 或更高 |
| 目标平台 | 桌面（GL Compatibility 渲染后端）；Android 导出工程已生成 |

## 运行

1. 用 Godot 4.7.2 mono 版打开本目录（`project.godot`）。
2. 首次打开时等待 C# 工程编译与资源导入完成。
3. 按 <kbd>F5</kbd> 运行。

主场景是 `Bin/Scenes/PhigrosPlay/PhigrosPlay.tscn`（见 `project.godot` 的 `run/main_scene`）。

命令行构建 C#：

```powershell
dotnet build
```

## 目录结构

```
PhiSide/
├─ Bin/                              游戏源码（注意：不是构建产物目录，见下方说明）
│  ├─ Scenes/                        Godot 场景
│  │  ├─ Home/                       主页（占位，尚未接入场景流程）
│  │  ├─ Launch/                     启动页（占位，尚未接入场景流程）
│  │  └─ PhigrosPlay/                游戏主场景：PhigrosPlay.tscn + Objects.tscn（判定线/音符模板）
│  ├─ Scripts/                       C# 源码
│  │  ├─ AssetsLoader/               音频文件加载
│  │  ├─ ChartLoader/                谱面 JSON 解析（含 v1/v3 格式兼容）
│  │  ├─ Library/                    通用数据结构
│  │  ├─ PhigrosPlay/                判定线与音符运行时逻辑
│  │  └─ UI/                         界面脚本（FPS、安全区、加载动画）
│  └─ Shaders/                       着色器
├─ Assets/                           美术与音频资源
│  ├─ Fonts/                         字体
│  ├─ Images/                        通用图片
│  ├─ ResourcePacks/Defaults/Phigros/ 默认皮肤：音符贴图、打击动画帧、判定音效
│  └─ Test/                          开发调试用的谱面与音频（体积大，未纳入版本控制）
├─ android/                          Godot 生成的 Android 导出工程
├─ PhiSide.csproj / PhiSide.sln      C# 工程
└─ project.godot                     Godot 项目配置
```

### ⚠️ 关于 `Bin/` 目录名

源码目录叫 `Bin/`，而 `bin/` 在 .NET 项目里是**编译输出目录**的约定名。
Windows 文件系统不区分大小写，所以 **不要往 `.gitignore` 里添加 `bin/` 或 `[Bb]in/` 规则**——那会连带忽略掉整个源码目录。当前 `.gitignore` 已刻意排除该规则。

建议后续把 `Bin/` 改名为 `Src/`（需同步更新场景中的 `res://Bin/...` 引用），即可彻底消除这个隐患。

## 谱面格式

谱面解析遵循 Phigros 官方格式（`formatVersion` 1 与 3）。字段语义与计算公式参考：

- 谱面格式说明：<https://docs.lchzh.net/learning/phigros>
- 相关计算：<https://docs.lchzh.net/learning/phigros/calc>
- 实测数据：<https://docs.lchzh.net/learning/phigros/metrics>

实现中已按官方定义固定的关键量（改动前请先核对文档）：

- 单位长度：`1 X = 0.05625 W = 108 px`，`1 Y = 0.6 H = 648 px`（1920×1080）
- 单位时间：`1 T = 1.875 / BPM` 秒（128 分音符）
- 普通音符垂直距离：`Y(t) = speed × (floorPosition − P_J(t))`
- **Hold 头部**垂直距离 `= floorPosition − P_J(t)`，**不带** `speed` 因子（官方规定 Hold 头速度倍率恒为 1）
- 事件列表规范化：速度事件首项 `startTime = 0`，其它事件首项 `startTime` 为极小值，相邻事件首尾相接，末项 `endTime` 为极大值

## 已知问题 / 待办

- [ ] 谱面与音乐路径目前**写死**在 `PhigrosPlay.cs`（`res://Assets/Test/1.json`）与 `PhigrosPlay.tscn`（`Assets/Test/1.wav`、`1.png`），只能跑固定测试谱面。
- [ ] 谱面 `offset` 字段已解析但**未被应用**，带偏移的谱面时序会整体错位。
- [ ] 判定线事件表为空或谱面字段缺失时的防护尚不完整。
- [ ] 变速谱面在一帧内跨越多个速度事件时，`floorPosition` 只累加一步，可能产生位置漂移。
- [ ] 分数 / 连击 / 进度条 UI 仅有占位控件，未接入判定与计分逻辑；暂停菜单未实现。
- [ ] `Home`、`Launch` 场景尚未接入场景流程，且 `Home.tscn` 引用的 FMOD 节点已随插件移除。

## 许可

本项目以 **GPL-3.0** 授权，详见 [LICENSE](LICENSE)。
