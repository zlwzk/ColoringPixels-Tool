<div align="center">

# 涂色大师 · Tool

**两款涂色游戏的非官方辅助工具 —— 一键安装，开箱即用**

一个安装器同时支持《Coloring Pixels》与《涂色大师：像素梦想家》：自动定位游戏目录、部署文件、启动游戏。
前者是带图形面板的 BepInEx 插件（进入关卡后按 <kbd>F1</kbd> 打开）；后者的游戏本体是 IL2CPP，无法注入，改用独立助手 `PixelAssist.exe`（启动助手后按 <kbd>F7</kbd> 框选画布、<kbd>F11</kbd> 校准格子、<kbd>F6</kbd> 开始）。

[![Release](https://img.shields.io/github/v/release/zlwzk/ColoringPixels-Tool?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC&color=5682ff)](https://github.com/zlwzk/ColoringPixels-Tool/releases)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x86%20%2F%20x64-0078d4.svg)](#系统要求)
[![Build](https://img.shields.io/github/actions/workflow/status/zlwzk/ColoringPixels-Tool/build.yml?label=build)](https://github.com/zlwzk/ColoringPixels-Tool/actions)

</div>

---

## 简介

支持两款游戏，共用一个安装器：

| 游戏 | Steam AppID | 接入方式 |
| --- | --- | --- |
| 《Coloring Pixels》 | **897330** | BepInEx 插件 + 游戏内面板（<kbd>F1</kbd>） |
| 《涂色大师：像素梦想家》 | **3071670** | 独立助手 `PixelAssist.exe`（IL2CPP，无法注入插件） |

工具由三部分组成：

| 组件 | 说明 |
| --- | --- |
| **安装器** `ColoringPixelsTool-Setup.exe` | 单文件、免安装、可视化。顶部可切换两款游戏，各自记忆目录与状态，互不影响。自动检测游戏目录 → 部署文件 → 启动游戏，并支持检查更新。 |
| **插件** `ColoringPixelsTool.dll` | 《Coloring Pixels》游戏内面板：分「自动完成」「人工辅助」两大模块，提供涂色、拟人涂色、定时连图、内容解锁、悬浮 HUD 等能力。 |
| **独立助手** `PixelAssist.exe` | 《涂色大师：像素梦想家》的屏幕扫描助手：框选画布后逐行匀速扫过，每一格仍由游戏自己判定。 |

插件基于 **BepInEx 5（x86）** 与 **Harmony** 运行，不改动游戏本体文件（除注入所需的 `winhttp.dll` / `BepInEx/`）。
独立助手是纯外部程序，通过热键控制鼠标，不向游戏写入任何内容。

## 特性

- **一键自动化**：安装器自动完成「找目录 → 部署 → 启动」，无需手动解压、复制文件。
- **双游戏支持**：一个安装包同时处理两款涂色游戏；一款没识别到不影响另一款，两者各自记忆目录与安装状态。
- **智能目录检测**：运行中的进程 → Steam 库（含 `libraryfolders.vdf`）→ 常见路径 → 全盘深度扫描，并读取 PE 头校验位数。
- **安全可逆**：安装时自动备份被覆盖的文件，卸载时一键还原；也可连同 BepInEx 本体一起移除。
- **默认上锁**：会写存档的「涂色 / 拟人 / 自动化」三页默认上锁，第一次解锁要确认风险提示（确定键有 3 秒冷静期），随时可在设置里重新上锁。
- **自动完成**：一键涂完本关 / 当前颜色、清空画布、立即保存、重载关卡；拟人涂色（就近分块 + 蛇形扫行 + 随机停顿 + 手速抖动，可选手滑）；定时自动化可连涂多张图。
- **人工辅助**：不代替你点，而是「屏幕扫描 + 模拟鼠标」逐行匀速扫过你框选的区域，每一格仍由游戏自己判定是否涂对，进度与统计和手涂一致；插件内和独立助手共用同一套引擎。
- **一键识别画布**：缩放到想要的画面后点一下，就按游戏内部的画布几何自动贴合扫描区域、算出行数与格子大小，不用手动框选对格子；可视框带柔光填充、贴合格子网格与实时信息牌（默认关闭，不挡视线，按 F12 或点面板按钮随时开关）。
- **画布颜色高亮**：把当前选中颜色在画布上的待涂格子高亮出来，方便肉眼快速定位；高亮颜色、样式、透明度均可自选。
- **内容解锁**：解锁全部 DLC、免费提示、显示隐藏书籍、标记书籍完成、解锁 Steam 成就。
- **悬浮 HUD**：实时显示进度、剩余格子、剩余颜色明细与本图用时，位置/透明度可调。
- **体验增强**：游戏界面汉化、语音换色、等级与称号、经验与统计、游戏内「推荐预设」按钮、新手指引、爱心重置二次确认。等级系统有**两条互不相干的经验线**——「人工辅助」（手动点击拖涂、扫描引擎、在线时长）与「自动绘图」（一键涂完、挂机、自动切图），各自升级、各有各的称号，只有对应模块的成果才增长对应那条。经验节奏刻意放得很慢（在线挂机 1 XP / 30 秒、自动涂色 1 XP / 150 格），越往后每级越久，等级只升不降。
- **界面自适配**：面板按分辨率自动缩放，低分辨率下也不会文字重叠。
- **字段调试**：直接编辑存档字段与关卡运行时字段。
- **命令行模式**：`--silent`、`--detect-only`、`--game=pcs`、`--all`、`--uninstall` 等，便于脚本化部署。

## 快速开始

### 系统要求

- Windows 10 / 11（x64 或 x86 系统均可）
- 已安装 Steam 版的任一款（或两款）受支持游戏
  - 《Coloring Pixels》需 **32 位版本**：本插件不支持 64 位主程序
  - 《涂色大师：像素梦想家》为 64 位 IL2CPP，走独立助手，无位数限制
- 无需安装 .NET 运行时（插件随游戏自带的 Mono 运行；安装器使用系统自带的 .NET Framework）

### 一键安装

1. 前往 [**Releases**](https://github.com/zlwzk/ColoringPixels-Tool/releases) 下载最新版 `ColoringPixelsTool-Setup-vX.Y.Z.exe`。
2. 关闭正在运行的游戏。
3. 双击运行安装器：
   - 若有安全软件拦截，请选择「允许」/「仍要运行」。
   - 若游戏装在 `Program Files` 等受保护目录，安装器会提示以管理员身份重启。
4. 在顶部标签选择要安装的游戏（默认《Coloring Pixels》），确认游戏目录后点击「**一键安装**」，等待进度条走完。
5. 安装器会**自动启动游戏**：
   - 《Coloring Pixels》：进入任意关卡后按 <kbd>F1</kbd> 打开面板。
   - 《涂色大师：像素梦想家》：启动游戏后运行 `PixelAssist\PixelAssist.exe`，按 <kbd>F7</kbd> 框选画布 → <kbd>F11</kbd> 校准格子 → <kbd>F6</kbd> 开始。

> 首次启动游戏时 BepInEx 会初始化，可能比平时稍慢，属正常现象。
> 两款游戏的目录与状态相互独立：只装了其中一款时，另一款显示未检测到即可，不影响使用。

### 卸载

- 重新运行安装器，点击「**卸载**」；
- 或在「卸载」弹窗中勾选「同时移除 BepInEx 本体」以彻底清理（仅《Coloring Pixels》）；
- 《涂色大师：像素梦想家》的卸载只会删除游戏目录下的 `PixelAssist\`，**不会**动 `%APPDATA%\PixelAssist` 里的区域与参数预设。

卸载只会移除工具文件与（可选的）BepInEx 本体，不会触碰游戏存档。

## 使用说明

### 快捷键

《Coloring Pixels》插件（全部可在「设置 → 快捷键」里改）：

| 按键 | 功能 | 备注 |
| --- | --- | --- |
| <kbd>F1</kbd> | 打开 / 关闭面板 | |
| <kbd>F2</kbd> | 一键涂完当前关卡 | 需先解锁自动绘图 |
| <kbd>F3</kbd> | 开始 / 停止拟人涂色 | 需先解锁自动绘图 |
| <kbd>F4</kbd> | 清空当前画布 | |
| <kbd>F5</kbd> | 立即保存当前关卡 | |
| <kbd>F6</kbd> | 开关画布颜色高亮 | |
| <kbd>F7</kbd> | 人工辅助：开始 / 暂停 / 继续 | |
| <kbd>F8</kbd> | 人工辅助：框选扫描区域 | |
| <kbd>F9</kbd> | 人工辅助：立即停止 | |
| <kbd>F10</kbd> | 人工辅助：只扫当前这一行 | |
| <kbd>F11</kbd> | 人工辅助：停止并从头重新整扫 | |
| <kbd>F12</kbd> | 人工辅助：格子校准 | 框选一个格子推算行数与采样步长 |
| <kbd>Q</kbd> | 长按 = 一直按住鼠标左键 | 涂色时不用一直压着鼠标 |

独立助手 `PixelAssist.exe`（《涂色大师：像素梦想家》）：

| 按键 | 功能 |
| --- | --- |
| <kbd>F6</kbd> | 开始 / 暂停 / 继续 |
| <kbd>F7</kbd> | 框选画布区域 |
| <kbd>F8</kbd> | 急停 |
| <kbd>F9</kbd> | 试扫当前这一行 |
| <kbd>F10</kbd> | 停止并从头重新整屏扫描 |
| <kbd>F11</kbd> | 格子校准 |
| <kbd>F12</kbd> | 显示 / 隐藏遮罩覆盖层 |

### 面板页面

面板顶部有两个模块，切换后各自有独立页签：

**自动完成**（替代手涂）

- **首页**：关卡进度、计时器、自动化状态与快捷操作。
- **涂色**：一键涂完本关 / 当前颜色、清空画布、立即保存、重载关卡；点调色板色块可直接涂完该颜色。
- **拟人**：调节手速、笔触长度、就近分块、停笔概率、手滑概率，以及「先涂大面积颜色」「同步高亮调色板」「只涂当前颜色」「结束后自动保存」。
- **自动化**：设定总时长、换图间隔、连续涂图与速度预设，挂机连涂多张图。
- **辅助**：画布颜色高亮（预设色块 / R·G·B、填充·描边·四角框、不透明度、只高亮未涂格子、呼吸闪烁）。
- **解锁**：解锁全部 DLC、免费提示（普通 / 重提示）、显示隐藏书籍、标记书籍完成、解锁 Steam 成就。
- **设置**：用户资料与背景、面板外观、悬浮 HUD、快捷键、游戏汉化、推荐预设、自动化与安全、语音交互、Bug 反馈、新手指引。
- **调试**：编辑 `CrossLevelStorage` 存档字段与 `ClickTest` 运行时字段（进阶用途，请谨慎修改）。

> 「涂色 / 拟人 / 自动化」三页会写入存档，默认上锁。第一次点「解锁自动绘图」会弹出风险提示，确认键有 3 秒冷静期；确认后长期有效，也可在「设置 → 自动化与安全」里重新上锁。

**人工辅助**（自己涂，但不用一直按住左键）

- **扫描**：状态与进度、开始 / 暂停、试扫本行、重新整扫、格子校准、**识别画布与格子**、区域概览。
- **区域**：**识别画布与格子**（一键贴合整张画布并算出行数与格子大小）；四角坐标可拖动成平行四边形 / 梯形，弯边用滑块微调。
- **参数**：扫描行数、鼠标速度、采样步长、行间停顿、边缘内缩、形状（蛇形往返）、安全与自动化（开始倒计时、干预判定半径、自动停止）、自动换色。
- **预设**：参数预设的保存 / 加载 / 删除，内置「通用 / 精细小图 / 大图极速」快速模板。
- **预览**：整幅图案预览（读游戏网格数据，不截图）：已涂为真彩、未涂为可关闭的暗色底稿，滚轮以光标为中心缩放、左键拖动平移，可放大细看。
- **助手**：引擎状态、使用说明，以及打开独立助手 `%APPDATA%\PixelAssist` 配置目录的入口。

### 配置文件

《Coloring Pixels》插件首次运行后会把默认配置写入：

```
<游戏目录>\BepInEx\config\coloringpixels.cheatsuite.cfg
```

可直接编辑该文件修改热键、参数与默认值（面板内的改动也会实时写回）。主要分组：`0-通用`、`1-解锁`、`2-显示`、`3-拟人涂色`、`4-人工辅助`、`5-自动化`、`6-面板`、`7-游戏汉化`、`8-推荐预设`、`9-快捷键`、`A-功能开关`、`B-反馈`。

独立助手的配置与预设保存在：

```
%APPDATA%\PixelAssist\        （Assist.region / Assist.settings / Presets\*.txt）
```

插件内的人工辅助区域与参数则存在 `BepInEx\config\ColoringPixelsTool.Assist\`，两者互不影响。

**等级与用户资料存档**（等级、经验值、各项统计）刻意存在漫游目录，不随游戏目录一起被清理：

```
%APPDATA%\ColoringPixelsTool\ColoringPixelsTool.Profile.json
```

`BepInEx\config` 会随「覆盖安装 / 卸载插件 / 验证游戏文件完整性 / 重装游戏」一起消失，
所以等级存档不放在那里，更新版本不会重置进度。升级后会自动迁移旧位置的存档，并在旧位置留一份镜像备份。

## 命令行参数

安装器同时支持无人值守模式，便于脚本批量部署：

```text
ColoringPixelsTool-Setup.exe [选项]

  --dir=<路径>        指定游戏目录（跳过自动检测）
  --game=<cp|pcs>     指定目标游戏，默认 cp（cp = Coloring Pixels，pcs = 像素梦想家）
  --all               对所有检测到的游戏各安装一次
  --silent            静默安装，不显示界面
  --detect-only       只检测游戏目录并退出
  --uninstall         卸载本插件
  --remove-bepinex    卸载时一并移除 BepInEx 本体
  --no-restore        卸载时不还原备份
  --no-launch         安装完成后不启动游戏
  --no-backup         不备份被覆盖的文件
  --force             强制覆盖所有文件
  --deep              深度扫描所有磁盘查找游戏
  --log=<文件>        指定日志文件路径
  -h, --help          显示帮助
```

退出码：`0` 成功，`1` 执行失败，`2` 找不到 / 无效的游戏目录，`3` 游戏正在运行。

示例：

```powershell
# 静默安装到指定目录，不启动游戏
.\ColoringPixelsTool-Setup.exe --silent --dir="C:\Program Files (x86)\Steam\steamapps\common\Coloring Pixels" --no-launch

# 给第二款游戏安装独立助手
.\ColoringPixelsTool-Setup.exe --game=pcs --silent --no-launch

# 两款游戏各装一次
.\ColoringPixelsTool-Setup.exe --all --silent --no-launch

# 只检测游戏目录（两款都检测）
.\ColoringPixelsTool-Setup.exe --detect-only --all
```

## 常见问题

<details>
<summary><b>游戏里按 F1 没有反应？</b></summary>

- 确认已**进入关卡**（主菜单下面板逻辑不生效）。
- 检查游戏目录下是否存在 `winhttp.dll`、`BepInEx\core\BepInEx.Preloader.dll`、`BepInEx\plugins\ColoringPixelsTool.dll`。
- 查看 `BepInEx\LogOutput.log`，搜索 `Coloring Pixels Tool` 是否成功加载。

</details>

<details>
<summary><b>「涂色 / 拟人 / 自动化」三个页面点不进去、提示已上锁？</b></summary>

这三页会把结果写进当前关卡存档，因此默认上锁。切到「解锁」页点「解锁自动绘图」，
在风险提示里确认一次（确定键有 3 秒冷静期）即可永久生效；想关掉回到「设置 → 自动化与安全」重新上锁。

</details>

<details>
<summary><b>《涂色大师：像素梦想家》该怎么用？</b></summary>

这款游戏是 IL2CPP，无法注入插件，安装器改为部署一个独立的 `PixelAssist.exe`：

1. 先启动游戏并进入关卡；
2. 运行游戏目录下的 `PixelAssist\PixelAssist.exe`；
3. 按 <kbd>F7</kbd> 框选整个画布区域，按 <kbd>F11</kbd> 框选其中一个格子做校准，再按 <kbd>F6</kbd> 开始；
4. <kbd>F8</kbd> 随时急停，<kbd>F12</kbd> 开关遮罩覆盖层。

助手只是模拟鼠标逐行扫过，是否涂对仍由游戏自己判定，所以进度和统计与你手涂一致。

</details>

<details>
<summary><b>安装器提示「目录不可用」或找不到游戏？</b></summary>

- 请选择包含游戏主程序（`ColoringPixels.exe` 或 `PixelCrossStitch.exe`）的那一层目录。
- 或使用 `--deep` 参数进行全盘深度扫描。
- 《Coloring Pixels》的 64 位主程序不受支持；《涂色大师：像素梦想家》无此限制。

</details>

<details>
<summary><b>安装 / 卸载失败、提示没有权限？</b></summary>

游戏若安装在 `Program Files` 等受保护目录，请以管理员身份重新运行安装器（安装器在遇到权限问题时也会主动询问）。

</details>

<details>
<summary><b>会影响其它 Mod 或游戏存档吗？</b></summary>

- 只卸载本插件时，其它基于 BepInEx 的 Mod 不受影响；BepInEx 本体仅在勾选对应选项时才移除。
- 卸载不会删除游戏存档，但安装时被覆盖的文件会在卸载时按选项还原。

</details>

<details>
<summary><b>会误封账号吗？</b></summary>

本插件仅在本地修改单机游戏的运行时数据，不注入网络流量、不修改游戏文件。但**任何第三方工具都存在风险**，请自行评估后使用。

</details>

## 从源码构建

详细步骤见 [docs/BUILDING.md](docs/BUILDING.md)。最简流程：

```powershell
# 一键构建：编译插件 -> 组装部署包 -> 编译安装器 -> 生成 SHA256
.\scripts\build-release.ps1

# 产物：dist\ColoringPixelsTool-Setup-v<版本>.exe 与 dist\SHA256SUMS.txt
```

> 编译插件需要引用游戏自身的 `Assembly-CSharp.dll`，因此本机需安装《Coloring Pixels》，
> 或直接使用仓库内已随包分发的 `artifacts\ColoringPixelsTool.dll`（`build-release.ps1 -SkipMod`）。

## 目录结构

```
CheatTools/
├─ src/ColoringPixelsTool/     插件源码（C#，BepInEx + Harmony）
│  ├─ Assist*.cs                人工辅助引擎（区域模型 / 屏幕扫描 / 模拟鼠标），插件与助手共用
│  └─ AssistAutoDetect.cs       一键识别画布区域与格子大小（读游戏内部画布几何，仅插件）
├─ src/PixelAssist/            独立助手源码（C# 5 / WinForms，供 IL2CPP 游戏使用）
├─ installer/                   安装器源码（C# 5 / WinForms，单文件）
│  └─ payload/                  部署包覆盖层（定制 doorstop_config.ini 等）
├─ scripts/                     构建脚本（PowerShell）
│  ├─ build-mod.ps1             编译插件
│  ├─ build-payload.ps1         组装 BepInEx + 插件的部署包
│  ├─ build-installer.ps1       编译单文件安装器
│  ├─ build-release.ps1         一键发布
│  ├─ publish.ps1               提交 / 推送 / 打标签发布
│  └─ make-icon.ps1             生成应用图标
├─ vendor/bepinex-x86/          随仓库分发的 BepInEx 5 x86 运行时
├─ artifacts/                   已编译的插件 DLL（随仓库提交，供 CI / 无游戏环境使用）
├─ assets/icon.ico              安装器图标
├─ docs/                        使用 / 构建文档，以及开发心路历程
├─ VERSION                      版本号（唯一来源）
└─ Directory.Build.props        全局 MSBuild 属性
```

> 想知道这个工具是怎么一步步长出来的：**[开发心路历程](docs/CHRONICLE.md)**。

## 免责声明

- 本项目仅供**学习、交流与单机娱乐**使用，请勿用于任何商业或非法用途。
- 请在下载后 **24 小时内**自行删除相关内容；因使用本工具产生的一切后果由使用者自负。
- 本项目与《Coloring Pixels》《涂色大师：像素梦想家》的开发商、发行商及 Steam 无关，游戏相关的全部权利归其各自所有者所有。
- 请支持正版游戏。

## 许可证

本项目基于 [MIT License](LICENSE) 开源。

若本工具基于或包含第三方作品，请一并遵守其原始授权与声明；发布前请确认你拥有相应的分发权利。
