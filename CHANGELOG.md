# 更新日志

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
版本号唯一来源是仓库根目录的 [`VERSION`](VERSION) 文件。

## [2.3.1] - 2026-09-11

### 文档

- `docs/CHRONICLE.md` 时间线补齐 2.2.2 ~ 2.3.0 共 11 个版本的关键词记录（此前停在 2.2.1）。
- 新增「第九章 · 那些『看起来在工作』的功能」：记录速度预设被每帧覆盖、经验值保底导致实际快 30 倍、
  扫描速度受「一帧只推进一个采样点」限制、助手「预览」页构建函数未被调用这几类
  「界面正常、按钮能点、功能不动」的问题，以及由此得出的「显式状态」与「唯一真源」两条结论。
- 「接下来想做的事」中「格子校准少依赖人工框选」已在 2.2.5 / 2.3.0 实现，予以标记。

### 说明

- 本版不含程序代码改动，插件 / 安装器 / 助手的功能与 2.3.0 一致（仅版本号随之更新）。

## [2.3.0] - 2026-09-11

### 修复

- **助手「预览」页从未被创建出来**：`AssistForm` 的构造函数漏调用 `BuildPreviewPage()`，
  点开「预览」页签会因 `_pagePreview` 为 null 触发空引用；同时 `_previewFlip` 恒为 null，
  点「开始」自动绘图时读 `_previewFlip.Checked` 也会崩。现已补上页面构建并对翻转开关判空。

### 新增

- **助手首页 · 关卡卡片与计时器**：显示当前关卡的册号 / 图号、尺寸、已涂 / 总数与进度条；
  新增独立的会话计时器（开始 / 暂停 / 继续 / 重置）。
- **助手自动绘图 · 涂色速度预设**：慢 / 中 / 快三档一键写入 `AutoSettings` 的手速、笔触长度、
  停笔概率；手动改动任意一项自动切回「自定义」（`_applyingSpeedPreset` 防回环）。
- **助手人工辅助 · 按存档推算格子**（`AutoFitGridFromSave`）：用当前关卡的宽高与框选区域尺寸
  反推扫描行数与采样步长，并写入 `AssistSettings.CellWidth / CellHeight`，替代手动框选单格。
- **助手人工辅助 · 区域微调**：四角坐标实时显示；四条边新增弯边滑条（±50% → `AssistRegion.Bend`），
  拖动画面把手会同步回滑条（`UpdateRegionUi` / `OnBendChanged`）。
- **助手人工辅助 · 快速模板**：通用 / 精细小图 / 大图极速三套参数（`ApplyTemplate`）。
- **助手设置页 · 公告与帮助**：启动时自动显示遮罩的开关（持久化到 `Assist.ui`）、快捷键一览、
  「更新公告」与「功能总览」弹窗、一键打开 GitHub 反馈（只带版本号与系统版本，不含路径 / 用户名）。
- `AssistStore` 新增 `LoadValue` / `SaveValue`：轻量 `key=value` 界面偏好存取。

### 变更

- `scripts\build-changelog.ps1` 新增第五处生成：`src\PixelAssist\AssistAnnounce.cs`
  （`Version` / `Changelog` / `Features`），助手的公告与功能总览和插件、安装器共用同一份 Markdown；
  `New-SourceFile` 支持自定义 body 成员名与附加 body（`-BodyMemberName` / `-ExtraBodyName`）。
  校验提示由「四处」改为「五处」。
- `scripts\build-assist.ps1` 的源文件收集依赖 `src\PixelAssist\*.cs` 通配，自动包含新文件。

### 文档

- `FEATURES.md` 补充《涂色大师：像素梦想家》助手的完整功能清单与快捷键。
- 本文件补回 2.2.12 的条目（该版只改了 `RELEASE_NOTES.md` / `VERSION`，漏了这里）。

## [2.2.12] - 2026-09-11

### 新增

- **《涂色大师：像素梦想家》独立助手补全**（`artifacts\PixelAssist.exe`），与 Coloring Pixels 插件功能对齐：
  - 多页签面板（`AssistForm.cs`）：首页、自动绘图、人工辅助、预览、等级、设置；
  - **自动绘图**（`AutoPainter.cs`）：读取 `PcsSave` 解码出的每格目标色，按颜色分组自动点击 / 拖拽填涂；
    调色板自动识别（`PaletteMap`）、只涂当前颜色、拟人参数（手速 / 笔触长度 / 停笔概率 / 手滑概率）；
  - **图案预览**（`LevelPreviewBox.cs`）：直接读存档目标色拼图，不截图；已涂真彩、未涂暗底稿，
    滚轮锚点缩放、拖拽平移、适应窗口；
  - **遮罩 HUD**（`OverlayForm.cs`）：自动绘图时高亮当前格、显示当前颜色与已涂 / 剩余进度。
- 新增 `PcsSave.cs`（存档解码：整文件每字节 `+0x11` 得明文 JSON；读未完成关卡与每格目标色）、
  `ScreenSampler.cs`（屏幕采样）、`MiniJson.cs`（最小 JSON 解析）、`LevelPreview.cs`（预览控件）。
- **两个游戏共用同一套等级系统**：`UserProfile.cs` 抽成可编入两宿主的共享核心
  （`ProfileLog` / `MathUtil` 解耦 `UnityEngine` 依赖），插件与助手读写同一份
  `%APPDATA%\ColoringPixelsTool\ColoringPixelsTool.Profile.json`；
  读写双向合并取最大（`MergeWithDisk` / `MergeFromJson` / `RefreshExternal`），
  写入用带进程号的临时文件 + 原子替换。
- **「纵向翻转」**：应对存档行序与屏幕方向相反的情况，同时作用于预览与自动绘图落点。

### 修复

- 自动绘图线程回 UI 由阻塞 `Invoke` 改为异步 `BeginInvoke`（`AssistForm.UiPost`），
  消除点「停止」时与 `_worker.Join` 互相等待导致的数秒卡顿。
- `ScreenSampler` 去掉 `unsafe`，改用受管数组拷贝，兼容未开启 `/unsafe` 的 csc 编译。

### 变更

- 助手工程 `PixelAssist.csproj` 通过 `<Compile Include="..\ColoringPixelsTool\UserProfile.cs">`
  链接共享等级核心；`scripts\build-assist.ps1` 的共享源列表同步加入 `UserProfile.cs`。

## [2.2.11] - 2026-09-11

### 修复

- **自动化页签的「涂色速度预设」从来没生效过**：`CheatPanel.Update()` 每帧无条件把「拟人涂色」
  页签的手速 / 笔触长度 / 停笔概率写进 `AutoPainter`，而 `AutoScheduler` 只在 `StartSession`
  时设一次预设值，下一帧就被覆盖。慢 / 中 / 快三档因此形同虚设。
  现在速度分场合：`AutoScheduler` 用 `SpeedOverrideActive` + `SpeedCellsPerSecond /
  SpeedStrokeLength / SpeedPauseChance` 保存本次会话的参数，面板只在会话未运行时才同步
  「拟人」页签的数值。

### 新增

- **自动化的「自定义」速度可以自己设定了**：新增 `AutoCustomSpeed`（手速，5–400 格/秒）、
  `AutoCustomStroke`（笔触长度，1–200）、`AutoCustomPause`（停笔概率，0–1）三项配置，
  预设选「自定义」（0）时使用；不再借用「拟人」页签的值。
- 「自动化」页签选「自定义」时直接显示三个滑块，并有「复制『拟人涂色』页签的参数」一键搬运。
- 自动化运行中的状态卡片新增「涂色速度」一行（`CheatPanel.SpeedSummary`）。
- 「拟人」页签在自动化运行期间给出提示：当前速度由自动化页签决定，改动结束后才生效。
- 慢 / 中 / 快三档下方标注各自的真实数值（`CheatPanel.PresetSpeedText`），选完心里有数。

### 文档

- `FEATURES.md`、`README.md`、`docs/USAGE.md` 补充自动化速度的归属规则、三档预设数值、
  自定义参数与「手速受帧率限制」的说明。

## [2.2.10] - 2026-09-11

### 新增

- **首次使用的「功能总览」公告**：新玩家第一次接触工具时，看到的是完整功能清单而不是
  「这版改了什么」；老用户升级仍然只看当版更新公告。
  - 新增仓库根目录 `FEATURES.md`（幽默风的全部功能说明），由 `build-changelog.ps1` 生成
    `src\ColoringPixelsTool\FeatureGuide.cs`（游戏内面板）与 `installer\FeatureGuide.cs`（安装器弹窗）。
  - 安装器：`MainForm.OnInstallClick` 在安装**前**记下 `PayloadInstaller.IsInstalled()`，
    为 `false`（新玩家）时 `ChangelogDialog(firstInstall: true)` 展示功能总览。
  - 插件：`Plugin.Awake` 用 `string.IsNullOrEmpty(UserProfile.LastVersion)` 判定首次使用，
    置 `CheatPanel.PendingAnnouncementIsFirstRun`；首启弹功能总览，升级弹更新公告。
  - 两种弹窗（游戏内 / 安装器）都加了「换一份看」按钮，可在功能总览与更新公告之间切换；
    公告窗高度与按钮布局随之上调，滚动文本区相应收窄。
  - 「设置」页新增「查看完整功能清单」按钮，分区标题改为「新手指引 / 功能总览」。

### 变更

- `scripts/build-changelog.ps1` 重构：把 Markdown → 纯文本的转换抽成 `Convert-Markdown`，
  一次生成四处源码（游戏内 / 安装器 × 更新公告 / 功能总览）；
  新增「内容未变则不写文件」的行为，避免无意义的 git 变更；`-Check` 语义保持不变。

### 文档

- `docs/BUILDING.md` 第 3.1 节改为说明「两个来源、四处同步」，并补充「第一次」的判定方式。
- `README.md`、`docs/USAGE.md` 补充功能总览的触发时机与入口。

## [2.2.9] - 2026-09-11

### 新增

- **两条独立经验线**（`UserProfile` / `XpTrack`）：等级系统由单条经验条拆成互不相干的两条，各升各的级。
  - **自动绘图**：一键涂完本关、自动挂机、自动切图完成一张图。称号是一套全新的机械画风
    （插头还没插稳 / 手抖的填色脚本 / … / 永动机涂色装置 / 不需要人类的画家 / 传说·合法外挂）。
  - **人工辅助**：手动点击与拖涂、扫描引擎替你涂掉的部分、在线时长、手工涂完一张图。称号沿用原来那套。
  - 两条线各自升级、各自弹提示（同帧都升级则合成一条）；升级曲线与每档等级边界完全一致，**称号表零重叠**。
  - 成果分流：`RecordAutoPaint` → 自动轨；新增 `RecordAssistPaint` → 人工轨；`RecordManualClick`、
    `Tick`、手工完成 → 人工轨；一键 / 挂机 / 自动切图完成 → 自动轨。
- **人工辅助 · 「预览」页**（`CanvasPreview` / `CheatPanel.Preview.cs`）：画出当前关卡整幅图案，
  可直接读游戏网格数据（不截图，不受窗口 / 缩放 / 遮挡影响）。已涂为真彩、未涂为压暗底稿（可关闭），
  支持滚轮以光标为中心缩放、左键拖拽平移、适应窗口 / 1:1，顶部显示书名关号、画布格数、已涂格数与百分比。

### 变更

- 面板顶部资料区改为两行经验条（人工辅助 = Accent2，自动绘图 = Accent），各自显示等级、称号、百分比与进度。
- 「设置」页资料卡加高，同时展示两条线的称号 / 本级经验 / 进度，以及人工 / 自动各自的完成张数与扫描引擎格数。
- 「人工辅助」页签顺序调整为：扫描 / 区域 / 参数 / 预设 / **预览** / 助手 / 设置 / 调试。
- 存档 `schema` 升到 3：新增 `levelManual` / `xpManual` / `levelAuto` / `xpAuto` / `assistPixels` /
  `imagesCompletedManual` / `imagesCompletedAuto`，并继续写旧字段 `level` / `xp`（= 人工轨）以兼容降级。

### 修复

- **拆轨后的存档读取**（`UserProfile.Parse`）：老存档只有 `level` / `xp`，现在会正确回退到人工轨，
  自动轨从 1 级 / 0 XP 起；旧的 `imagesCompleted` 也会回落到 `imagesCompletedManual`。
- **人工辅助页签索引**：`DrawContent` 的手动模块 `switch` 与新的 8 个页签对齐（此前遗漏「预览」导致后续页签错位）。

### 文档

- `README.md`、`docs/USAGE.md` 更新双经验线与「预览」页说明。

## [2.2.8] - 2026-09-11

### 修复

- **等级涨得太快**（`UserProfile`）：自动涂色是**一格一格**调用 `RecordPixels(1)` 的，而原实现
  `AddXp(Mathf.Max(1, count / 30))` 对每次调用都保底 1 XP —— 于是 1 格 = 1 XP，比设计值快 30 倍，
  自动挂机每分钟几千格就是几千经验。改为**余数累计**（`_autoPixelCarry`）：每 150 格 +1 XP，
  不够一档的余数留到下次，单次上限 200 XP。

### 变更

- **整体调慢经验获取**：
  - 在线时长：每 30 秒 +1 XP（旧：每 6 秒）
  - 自动涂色：每 150 格 +1 XP（旧：实际每格 1 XP）
  - 手动涂色：每 25 格 +1 XP（旧：每 12 格）
  - 涂色率奖励：`格数/20`、上限 12（旧：`格数/4`、上限 25）
  - 手速奖励：+1 XP（旧：+2）
  - 完成图片：`25 + 像素数/150`（旧：`40 + 像素数/40`）
  - 刷新最大完成图：`差值/150`（旧：`差值/40`）
- **升级曲线加陡**（`XpForLevel`）：`200n + 60n²` → `150n + 60n² + n³`，前期基本不变，
  后期约为原来的 1.2~2.6 倍（Lv.30 ≈ 79k、Lv.50 ≈ 269k、Lv.100 ≈ 1.57M）。
  综合下来升级速度约为上一版的 1/4 ~ 1/5。
- **老存档不掉级**：等级仍是只升不降；曲线变陡导致累计 XP 低于本级门槛时，
  `XpIntoLevel` 夹到 0（否则面板会显示负数），进度条从本级起点重新爬。

### 文档

- `README.md`、`docs/USAGE.md` 同步新的经验规则与「只升不降」说明。

## [2.2.7] - 2026-09-11

### 修复

- **安装器启动游戏后不会自动关闭**（`MainForm`）：
  - 自动关闭用的 `System.Windows.Forms.Timer` 是在后台线程上创建并启动的（安装流程从
    `RunTask` 的后台线程调用 `Launch`），而 WinForms 计时器只在其**创建线程的消息循环**
    里派发 `Tick` —— 后台线程没有消息泵，所以永远不触发。现在统一切回 UI 线程再起表。
  - 安装完成后的更新公告是模态弹窗，会把主窗口的 `Close()` 压在模态循环里；自动关闭时
    先收掉公告，再把关闭请求排进消息队列，等模态循环退干净后再关自己。
  - `MarkGameRunning()` 原本在后台线程直接改按钮文案，会触发跨线程校验（被 `try` 吞掉
    后按钮一直显示旧文字），改为回 UI 线程执行。

### 变更

- **绘图可视框默认关闭**（`AssistOverlay._showOverlay` / `AssistForm._overlayVisible`）：
  覆盖层是盖在游戏画面上的特效层，不涂的时候挡视线。默认 `false`；主动框选、格子校准、
  一键识别画布时仍会自动打开；独立助手的「显示 / 隐藏遮罩」按钮文案随状态刷新
  （此前固定写着「隐藏遮罩」）。

### 文档

- `README.md`、`docs/USAGE.md` 补充可视框默认关闭的说明与开关方式。

## [2.2.6] - 2026-09-11

### 修复

- **人工辅助误报「人工介入」**（`AssistEngine`）：旧判定拿「下一个扫描点」当基准比距离，
  于是「刚点开始（鼠标还停在按钮上）」「每扫完一行（下一行起点差一整行）」「换色/暂停后
  点继续（鼠标停在继续按钮上）」这三种用户根本没碰鼠标的情况都会立刻暂停。
  现在改为以「引擎上一帧命令光标去的位置」为基准 —— SendInput 是异步入队的，
  基准必须是命令位置而不是 `GetCursorPos` 的读数；偏差超过 `FailRadius` 才算人工干预。
  开始 / 继续 / 单行测试 / 换色后会把光标当前位置重新认作基准，并且阈值自动
  留出一帧的行程（`max(FailRadius, 本帧预算 × 1.6)`），高速或掉帧时同样不误报。
- **扫描速度与设置不符**：旧实现一帧只推进一个采样点，步长 4px、60fps 时实际最快
  240px/s，面板上的「鼠标速度」调到 1400/12000 都没有效果。现在按像素预算在一帧内
  连续推进多个点，速度等于设置值（游戏自身对拖动轨迹有插值，不会漏格子）。
- **多画出的线**：从鼠标当前位置滑向画布起点的那段路以前一路按住左键，会沿路画一道；
  现在到达第一个扫描点才按住左键。换行时也先松手（非蛇形时下一行起点横跨整张画布，
  按着左键过去就是一条横线），到达新行第一个点自动重新按住。
- **安装器重复启动游戏**（`MainForm.Launch` / `PayloadInstaller.LaunchGame`）：
  Unity 游戏不拦多开，「安装后自动启动」再顺手点「启动游戏」就会冒出好几个游戏画面。
  现在启动前先查进程，已在运行则不再启动、只把窗口切到前台；「启动游戏」按钮变成
  「游戏运行中」；独立助手同样去重；安装/更新进行中点击「启动游戏」不再生效。

### 变更

- 覆盖层的「当前行高亮」在换行停顿与换色等待期间也会指向即将扫描的新行（此前晚一行）。

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

[2.3.1]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.3.1
[2.3.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.3.0
[2.2.12]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v2.2.12
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
