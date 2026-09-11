# 更新日志

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
版本号唯一来源是仓库根目录的 [`VERSION`](VERSION) 文件。

## [2.2.0] - 2026-09-11

### 新增

- **全端 UI 美化与动效升级**：把 magicui / Aceternity 的设计语言移植到 GDI+ / IMGUI，零第三方依赖
  - 插件端（Coloring Pixels 游戏内 F1 面板）：
    - 新增 `UiFx.cs` 动效库，含 Shimmer、Glare、ShineBorder、Spotlight、Aurora Background、Dot Grid、Grain、Border Beam、Pulse、CountText、Confetti 彩纸、缓动等
    - 标题栏 Spotlight 跟随鼠标，窗口背景加 Aurora + 点阵 + 噪点
    - 图标带 Shimmer 与光晕，Tab 改为滑动指示块 + ShineBorder
    - 内容区 Blur Fade 入场，按钮按下回弹 + 背光 + 渐变 + 指示条
    - 进度条渐变 + Shimmer + 前沿呼吸光点
    - 完成图片时撒彩纸，首页 `HeroBand` + 滚动百分比
  - 安装器端：
    - 新增 `installer/Fx.cs` 动效层
    - 主窗口背景加 Aurora，卡片顶部加品牌渐隐光条
    - `NeonButton` 自绘：渐变底 + Shimmer + Glare + 悬停背光 + 按下回弹
    - `ProgressBarEx` 数值平滑、Shimmer、前沿 Blob 呼吸 / 完成光晕
    - 安装完成后检测到游戏主窗口出现 2 秒自动关闭安装器（60 秒超时）
  - 独立助手端（PixelAssist）：
    - 新增 `AssistFx.cs` / `AssistControls.cs`，复刻同一套无依赖动效
    - 助手窗口背景加 Aurora + Dot Grid + Grain
    - 状态卡片圆角玻璃质感 + 顶部品牌光条
    - 霓虹按钮、状态呼吸点、霓虹进度条（渐变 + Shimmer + 前沿光点）
    - 全屏覆盖层 HUD 改为圆角玻璃卡片 + ShineBorder + 呼吸状态点 + 霓虹进度条 + 当前行脉冲高亮

### 修复

- 修复 v2.2.0 插件源码遗留的编译错误：`CheatPanel.Extras.cs` 补齐未定义成员、`HeartGuard.cs` 替换 Harmony 过时 API、`PaintTimer.cs` 清理未定义字段
- 修复安装器 `MainForm.cs` 被截断导致的编译错误，并把自动关闭相关 `Timer` 限定为 `System.Windows.Forms.Timer` 避免二义性

## [2.1.0] - 2026-09-11

### 新增

- **新增第二款游戏支持：《涂色大师：像素梦想家》**
  - 安装器现在会同时识别两款游戏的目录与配置文件，互不影响：任意一款没识别到，另一款照常安装
    - 《Coloring Pixels》：Steam AppId `897330`，校验 `ColoringPixels.exe` + `ColoringPixels_Data\Managed\Assembly-CSharp.dll`
    - 《涂色大师：像素梦想家》：Steam AppId `3071670`，校验 `PixelCrossStitch.exe` + `GameAssembly.dll`（IL2CPP）
  - 界面顶部新增游戏切换标签，两款游戏各自记忆目录、安装状态与版本，可分别安装 / 卸载
  - 该游戏是 IL2CPP 构建，无法注入 BepInEx 托管插件，因此改为随包分发**独立助手 `PixelAssist.exe`**
    - 全局热键：<kbd>F7</kbd> 框选画布、<kbd>F11</kbd> 框选一个格子自动校准行数/步长、<kbd>F6</kbd> 开始/暂停、<kbd>F8</kbd> 急停、<kbd>F9</kbd> 试扫当前行、<kbd>F10</kbd> 停止并重新整屏扫描、<kbd>F12</kbd> 显示/隐藏范围框
    - 全屏点击穿透覆盖层实时显示扫描区域、当前扫描线与进度 HUD
    - 参数与区域保存在 `%APPDATA%\PixelAssist`，升级安装包不会丢失
  - 命令行新增 `--game=cp|pcs` 与 `--all`（对所有检测到的游戏各安装一次）

- **Coloring Pixels 面板分为「自动完成」与「人工辅助」两大模块**
  - 「自动完成」保留原有全部功能（涂色、拟人、自动化、辅助、解锁、设置、调试）
  - 「人工辅助」按设计文档实现：屏幕扫描辅助区域（四角 + 弯边模型，可逐角拖动微调）、
    扫描行数 / 速度 / 采样步长 / 行间停顿 / 蛇形 / 边缘内缩 / 启动延时 / 按住按键 /
    自动停止时长 / 每 N 行自动换色 / 失败半径 / 人工介入检测 / 越界收敛，
    并提供参数预设的保存 / 载入 / 删除

### 变更

- **面板自适应缩放**：面板整体按屏幕分辨率与用户倍率缩放（新增 `PanelScale` 配置，`0` 为自适应），
  低分辨率下不再出现文字互相覆盖；超宽文本自动省略号截断
- **经验值增长大幅放缓**，并把手动点击次数、手动涂色率纳入经验判定；挂机收益明显低于真人操作
- 每个等级区间新增趣味称号（面板顶部展示，升级时弹 Toast）

### 修复

- 修复面板在非 1080p 分辨率下命中区域与实际控件错位的问题
- 修复安装器只比较文件长度就判定「已是最新」的问题：负载重建后新文件长度恰好与旧文件相同时，
  插件会永远更新不上；现在改为逐字节内容比对，并支持「全部已是最新」时正常报成功而非误报权限错误

## [2.0.0] - 2026-09-10

### 新增

- **用户等级系统**：头像、用户名、经验条常驻面板顶部，按在线时长 / 涂色格数 / 完成图片 / 图片大小累计 XP（不影响任何功能）
- **面板自定义背景图**：在「设置」页填写图片路径即可替换面板背景
- **自动化自动切图**：完成当前图片后自动加载下一张，涂完一本自动翻下一本并跳过已完成关卡
- **游戏设置推荐预设**：在游戏原生设置界面注入「推荐预设」按钮，一键套用推荐配置
- **安装器更新后弹窗公告**（`ChangelogDialog`），GitHub Release 正文改用 `RELEASE_NOTES.md`
- 安装器检查更新增加 `releases/latest` 302 重定向兜底，失败时提示夸克网盘手动更新入口

### 变更

- 面板与安装器 UI 全面重构：深靛蓝底 + 青紫霓虹强调 + 玻璃质感卡片 + 分段式页签动画

## [1.3.1] - 2026-09-10

### 新增

- **游戏设置汉化**：新增「游戏汉化」配置组，可直接把游戏本体设置界面的英文文案翻译成中文
  - 内置常用词典，也可外挂词典文件（放在 `BepInEx/config/`）随时补充，保存后热重载
  - 自动把游戏原生像素字体替换为系统中文字体，中文不再显示成方块
- **推荐预设按钮**：在游戏设置界面注入「推荐预设」按钮，形状、颜色与游戏自带按钮保持一致
  - 预设内容写在 `BepInEx/config/ColoringPixelsTool.Preset.txt`，键名支持存档字段名或界面控件名
  - 点击后一键把音量、灰度、季节特效等设置改为推荐值，并立即刷新界面
- **安装器检查更新**：安装器启动时自动检查 GitHub Release
  - 发现新版本时按钮变为「更新到 vX.Y.Z」，点击即可把最新版安装器下载到桌面并直接运行

### 修复

- 修复鼠标在作弊面板上滚动时游戏画面被同步缩放的问题（滚轮不再穿透到游戏）

## [1.3.0] - 2026-09-10

### 新增

- **首页（Dashboard）**：面板新增「首页」标签，包含：
  - 实时北京时间显示（UTC+8，支持时区回退）
  - 可启停的计时器（HH:MM:SS），并可一键把计时同步为自动化总时长
  - 自动化任务状态看板：已运行、剩余、已完成图片数
- **自动化模块**：在「自动化」标签设定总时长，工具会自动进行拟人涂色
  - 支持「连续涂图」：完成当前图片后尝试打开下一张继续涂
  - 找不到自动切图入口时会暂停并提示手动换图，换图后自动继续
  - 提供慢 / 中 / 快三档速度预设，也可复用「拟人」页签的自定义速度
  - 与首页计时器联动，随时查看剩余时间
- **设置模块**：新增「设置」标签，集中管理
  - 面板不透明度
  - 悬浮 HUD 开关、位置、不透明度
  - 全部热键可视化设置（点击后按任意键即可绑定）
- **全新布局与视觉**：
  - 8 个标签按功能分区：首页、涂色、拟人、自动化、辅助、解锁、设置、调试
  - 卡片化分区、统一留白、圆角与强调色，整体更现代
  - 修复滚动后面板内容与鼠标命中区域错位的 bug
- **安装器优化**：安装选项复选框改为自绘 CheckBox，勾选状态在深色背景下清晰可见

### 修复

- 修复面板滚动后鼠标命中区域偏移的问题
- 修复安装器复选框勾选状态几乎不可见、看起来「勾选不了」的问题

## [1.2.0] - 2026-09-10

### 变更

- **项目更名为 Coloring Pixels Tool**：仓库、程序集、安装器与文档统一改用新名称
  - 插件程序集为 `ColoringPixelsTool.dll`，安装器为 `ColoringPixelsTool-Setup-v<版本>.exe`
  - 安装 / 卸载时自动清理旧版遗留的 `ColoringPixelsCheat.dll`，避免同一 GUID 的插件被重复加载
  - BepInEx 插件 GUID 与配置文件仍为 `coloringpixels.cheatsuite`，已有设置不会丢失
- **发布流程**：提交、推送、打标签发版以及把安装器同步到桌面，现在由同一条命令完成

## [1.1.0] - 2026-09-10

### 新增

- **人工辅助 · 画布颜色高亮**：把「当前选中颜色」在画布上对应的格子高亮出来，方便肉眼快速定位
  - 高亮颜色可自选：内置 8 种预设色块，也可用 R / G / B 滑条任意调色
  - 三种高亮样式：填充 / 描边 / 四角框
  - 可只高亮未涂格子、可开启呼吸闪烁、可调节不透明度
  - 新增快捷键 <kbd>F6</kbd> 一键开关画布高亮
  - 面板新增「辅助」页，配置新增 `4-人工辅助` 分组

## [1.0.0] - 2026-09-10

首个正式发布版本。

### 新增

- **一键安装器**（`ColoringPixelsTool-Setup.exe`，单文件、免安装）
  - 自动检测游戏目录：运行中的进程 → Steam 库 → 常见位置 → 全盘深度扫描
  - 校验是否为《Coloring Pixels》目录，并读取 PE 头判断主程序位数
  - 自动部署 BepInEx 5（x86）+ 作弊插件，支持覆盖 / 备份 / 幂等重装
  - 部署完成后自动启动游戏
  - 一键卸载（可选连同 BepInEx 本体一起移除、可选还原备份）
  - 无边框深色界面，支持高 DPI
  - 命令行模式：`--silent` / `--detect-only` / `--uninstall` / `--dir=` 等
- **作弊插件**（`ColoringPixelsTool.dll`）
  - 涂色页：一键涂完本关、涂完当前颜色、清空画布、立即保存、重载本关
  - 拟人涂色引擎：按颜色逐个处理、就近分块 + 蛇形扫行、随机停顿、手速抖动、可选手滑
  - 解锁页：解锁全部 DLC、免费提示（普通 / 重提示）、显示隐藏书籍、标记书籍完成、解锁 Steam 成就
  - 显示页：可拖动悬浮 HUD（进度、剩余格子、剩余颜色明细）
  - 字段页：直接编辑 `CrossLevelStorage` 存档字段与 `ClickTest` 运行时字段
  - 全局快捷键：F1 面板、F2 一键涂完、F3 拟人涂色、F4 清空、F5 保存
  - 配置写入 `BepInEx/config/coloringpixels.cheatsuite.cfg`

### 构建

- 新增 `scripts/build-mod.ps1`：编译插件，自动在 `dotnet` / `MSBuild` / `Roslyn csc` 三种后端间择优
- 新增 `scripts/build-payload.ps1`：组装 BepInEx + 插件的离线部署包
- 新增 `scripts/build-installer.ps1`：使用系统自带 `csc.exe` 编译单文件安装器（无需 .NET SDK）
- 新增 `scripts/build-release.ps1`：一键完成「编译插件 → 组装部署包 → 编译安装器 → 生成 SHA256」
- 新增 `scripts/make-icon.ps1`：纯 `System.Drawing` 生成多尺寸应用图标
- 版本号单点维护：根目录 `VERSION` 被 `Directory.Build.props` 与全部构建脚本读取
- 通过 GitHub Actions 在 `windows-latest` 上自动构建并发布 Release

[2.1.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.1.0
[2.0.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.0.0
[1.3.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.3.0
[1.2.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.2.0
[1.1.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.1.0
[1.0.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.0.0
