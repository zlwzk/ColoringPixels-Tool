# 更新日志

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
版本号唯一来源是仓库根目录的 [`VERSION`](VERSION) 文件。

## [2.2.5] - 2026-09-11

### 新增

- **一键识别画布与格子**（`AssistAutoDetect.cs` 新增）：用户缩放好画面后点一下按钮，
  自动贴合整张画布、取画布行数为扫描行数、按格子宽度的 1/2 设定采样步长。
  做法是读游戏内部的画布几何（正交相机 + Tilemap 的仿射变换，采样 (0,0)/(1,0)/(0,1)
  三个格子中心反推），不是截图识别，因此对缩放 / 平移 / 画布尺寸完全免疫。
  - 画布未完整显示、格子小于 2px、未进入关卡等情况都会返回可读的原因，而不是乱给一个区域。
  - 入口：「人工辅助 → 扫描」「人工辅助 → 区域」两页顶部的「识别画布与格子」，不占热键。
  - 结果（画布格数 / 每格像素）持久化到 `Assist.settings` 的 `cellw` / `cellh`，
    并在面板与覆盖层信息牌上显示；手动 <kbd>F12</kbd> 校准也会记录格子大小。
- **绘图可视框美化**（`AssistOverlay.cs`）：柔光玻璃填充 + 呼吸外发光 + 紫青渐变霓虹描边 +
  沿边框跑动的光点 + 真实格子网格预览（每 5 条加亮）+ 圆角角点手柄（悬停放大、带柔光）+
  区域信息牌（画布尺寸 / 格子数 / 每格像素 / 运行状态点）+ 当前扫描行呼吸高亮。
  手动框选的选框也换成圆角描边 + 四角刻度 + 尺寸胶囊。
- 覆盖层在「还没有识别区域」时显示一条引导条，说明下一步该做什么。

### 隐私

- **反馈上传脱敏**（`GitHubFeedback.Sanitize`）：日志尾部与正文里出现的
  `C:\Users\<用户名>\...`、游戏目录、`%APPDATA%`、`%LOCALAPPDATA%`、`%TEMP%`
  以及机器名，提交前统一替换为占位符（issue 是公开的，不该替用户把这些发出去）；
  另加一条正则兜底，任何盘符下的 `\Users\<名字>\` 都会被盖掉。
- **界面上不再出现 Windows 用户名**：`UserProfile.UserDataDirectoryDisplay()` 把面板里的
  等级存档目录显示成 `%APPDATA%\ColoringPixelsTool\`；安装器日志路径显示成
  `%USERPROFILE%\...`（`installer\Log.PrettyPath`）。
- **仓库内不再有开发机绝对路径**：`tools\dump-types.ps1`、`scripts\publish.ps1` 改为自动
  探测游戏目录（显式参数 → `CPT_GAME_DIR` → 仓库上级 → 各盘 Steam 默认位置），
  `README.md` / `docs\USAGE.md` / `docs\BUILDING.md` 的示例统一改成 Steam 默认安装位置。

### 修复

- **窗口化下的坐标偏移**（`AssistWin32.TryGetClientOrigin` + `AssistOverlay`）：区域的存储单位
  统一为桌面坐标（因为最终要移动真实光标），绘制与命中测试再换算回客户区。
  以前窗口化时游戏内坐标与桌面坐标差一个窗口边框 + 标题栏，框选与扫描会整体偏出去。
  全屏 / 无边框时偏移为 (0,0)，行为与之前完全一致；只有客户区尺寸与渲染分辨率吻合时才采纳偏移。
- 「区域」页与文档里「拖每条边的中点可把直边弯成弧线」是**未实现**的功能，文案已改为
  用下面的弯边滑块（`TabAssistRegion`、`docs/USAGE.md`、`README.md`）。

### 文档

- `README.md`、`docs/USAGE.md`、`RELEASE_NOTES.md` 同步一键识别与可视框的说明，并补充两条常见问题。

## [2.2.4] - 2026-09-11

### 修复

- **等级 / 经验值不再随版本更新重置**（`UserProfile.cs`）：
  - 存档从游戏目录 `BepInEx\config\ColoringPixelsTool.Profile.json` 移到漫游目录
    `%APPDATA%\ColoringPixelsTool\ColoringPixelsTool.Profile.json`。
    前者会随「覆盖安装 / 卸载插件 / 卸载时移除 BepInEx / 验证游戏文件完整性 / 重装游戏」一并消失，
    这正是「更新一次版本，等级从头再来」的直接原因。
  - `Save()` 改为**原子写入**（先写 `.tmp` 再替换）：原先的 `File.WriteAllText` 会先截断原文件，
    中途崩溃 / 断电会留下半截 JSON，导致下次加载解析失败、等级回到 1 级。
  - `Load()` 在「正式存档 / `.bak` 备份 / 旧位置镜像」三份里挑进度最多的一份，
    并在来源不是正式位置时自动迁移回写。
  - 疑似损坏的存档重命名为 `.corrupt-<时间戳>` 留存，不再被静默覆盖。
  - 新增 `LevelFromXp()`：等级可由累计 XP 反推还原（只升不降），存档丢失 `level` 字段也不会掉级。
  - 存档 JSON 增加 `schema` / `savedAt` 字段，便于日后排查。
- 「设置 → 用户资料」卡片底部新增一行，显示等级存档所在目录。

### 文档

- `README.md`、`docs/USAGE.md` 增加等级存档位置与自动迁移说明（新增 `USAGE.md` 6.3 小节）。

## [2.2.3] - 2026-09-11

### 修复

- **面板与安装器的「更新公告」现在会自动跟随版本**：此前公告散落在 `RELEASE_NOTES.md`、
  插件 `Changelog.cs`、安装器 `ChangelogDialog.cs` 三处手写，版本一升，后两处常被漏改
  （v2.2.2 就出现了「安装包是新的、面板公告还停在 2.2.1」）。
  现在 `RELEASE_NOTES.md` 是唯一来源，新增 `scripts/build-changelog.ps1` 生成
  `src/ColoringPixelsTool/Changelog.cs` 与 `installer/ReleaseNotes.cs`；
  `publish.ps1` 与 `build-release.ps1`（CI）都会在编译前自动执行，发版时若公告未写到新版本号则直接报错。
- 公告文本由 Markdown 转为纯文本时，统一处理标题、加粗、列表、表格与 `<kbd>` 按键标记，
  面板与弹窗中不再出现 Markdown 符号。

### 文档

- `docs/BUILDING.md` 新增「更新公告」小节，说明公告唯一来源与生成命令。

## [2.2.2] - 2026-09-11

### 修复

- **修复安装器启动失败**：`NeonButton` 在声明 `SupportsTransparentBackColor` 之前就设置了
  `BackColor = Color.Transparent`，部分 .NET Framework / 高 DPI 环境下会抛出
  「控件不支持透明的背景色」而无法启动。现改为先 `SetStyle(...)` 再设置透明背景色。

## [2.2.1] - 2026-09-11

### 修复

- 修正面板里写死的人工辅助热键：按钮与提示文案此前仍写着独立助手的 F6 / F8，与插件实际默认的
  F7（开始）/ F9（停止）不符。现在这些文案统一改为读取「设置 → 快捷键」的实际配置，改键后即时更新
  （涉及「扫描」页按钮、「区域」页说明与新手指引第 3 页）。
- 覆盖层开关未绑定按键时，不再显示「未设置」这种别扭文案，改为提示可在设置里绑定。
- 修正 `README.md` 徽章的仓库地址（`zlwzk/ColoringPixelsTool` → `zlwzk/ColoringPixels-Tool`），
  此前 Release / Build 徽章指向一个不存在的仓库。

### 文档

- `docs/USAGE.md` 全面重写：补齐第二款游戏《涂色大师：像素梦想家》与独立助手 PixelAssist 的用法、
  面板「自动完成 / 人工辅助」两大模块的逐页说明、自动绘图上锁与解锁流程，以及 12 个配置分组的完整键值表。
- `README.md` 同步到当前版本：双游戏简介与组件表、特性清单、F1–F12 快捷键、
  `--game` / `--all` 参数，常见问题新增「三个页面为什么是灰的」「第二款游戏怎么用」。
- `docs/BUILDING.md` 修正 `publish.ps1` 的错误参数说明与写错的仓库地址。

## [2.2.0] - 2026-09-11

### 新增

- **自动绘图解锁风险确认**：第一次解锁「涂色 / 拟人 / 自动化」这三个会写存档的页面时，会先弹出风险说明
  - 确认键带 3 秒冷静期，确认之后才真正解锁；解锁状态长期有效，之后启动游戏不再询问
  - 从「设置 → 自动化与安全」开启同样会走这道确认；关掉开关即可重新上锁
- **新增 [开发心路历程](docs/CHRONICLE.md)**：按时间线记录每个版本的设计动机与取舍，README 底部提供入口
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

[2.2.5]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.2.5
[2.2.4]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.2.4
[2.2.3]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.2.3
[2.2.2]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.2.2
[2.2.1]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.2.1
[2.2.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.2.0
[2.1.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.1.0
[2.0.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.0.0
[1.3.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.3.0
[1.2.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.2.0
[1.1.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.1.0
[1.0.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.0.0
