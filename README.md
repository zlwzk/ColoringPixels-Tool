<div align="center">

# 涂色大师 · Tool

**《Coloring Pixels》非官方作弊 / 辅助工具 —— 一键安装，开箱即用**

带图形化安装器的 BepInEx 插件：自动定位游戏目录、部署运行时、启动游戏，进入关卡后按 <kbd>F1</kbd> 打开面板。

[![Release](https://img.shields.io/github/v/release/zlwzk/ColoringPixelsTool?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC&color=5682ff)](https://github.com/zlwzk/ColoringPixels-Tool/releases)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x86-0078d4.svg)](#系统要求)
[![Build](https://img.shields.io/github/actions/workflow/status/zlwzk/ColoringPixelsTool/build.yml?label=build)](https://github.com/zlwzk/ColoringPixels-Tool/actions)

</div>

---

## 简介

本项目是《Coloring Pixels》（Steam AppID **897330**）的辅助工具，由两部分组成：

| 组件 | 说明 |
| --- | --- |
| **安装器** `ColoringPixelsTool-Setup.exe` | 单文件、免安装、可视化。自动检测游戏目录 → 部署 BepInEx + 插件 → 启动游戏。 |
| **插件** `ColoringPixelsTool.dll` | 游戏内作弊面板，提供涂色、拟人涂色、内容解锁、悬浮 HUD 等能力。 |

插件基于 **BepInEx 5（x86）** 与 **Harmony** 运行，不改动游戏本体文件（除注入所需的 `winhttp.dll` / `BepInEx/`）。

## 特性

- **一键自动化**：安装器自动完成「找目录 → 部署 → 启动」，无需手动解压、复制文件。
- **智能目录检测**：运行中的进程 → Steam 库（含 `libraryfolders.vdf`）→ 常见路径 → 全盘深度扫描，并读取 PE 头校验 32 位主程序。
- **安全可逆**：安装时自动备份被覆盖的文件，卸载时一键还原；也可连同 BepInEx 本体一起移除。
- **涂色辅助**：一键涂完本关 / 当前颜色、清空画布、立即保存、重载关卡。
- **拟人涂色**：按颜色逐个处理，就近分块 + 蛇形扫行 + 随机停顿 + 手速抖动，可选手滑，模拟真实玩家节奏。
- **人工辅助高亮**：把当前选中颜色在画布上的待涂格子高亮出来，方便肉眼快速定位；高亮颜色、样式、透明度均可自选。
- **内容解锁**：解锁全部 DLC、免费提示、显示隐藏书籍、标记书籍完成、解锁 Steam 成就。
- **悬浮 HUD**：实时显示进度、剩余格子、剩余颜色明细，位置/透明度可调。
- **字段调试**：直接编辑存档字段与关卡运行时字段。
- **命令行模式**：`--silent`、`--detect-only`、`--uninstall` 等，便于脚本化部署。

## 快速开始

### 系统要求

- Windows 10 / 11（x64 或 x86 系统均可）
- 已安装 Steam 版《Coloring Pixels》（**32 位版本**；本插件不支持 64 位主程序）
- 无需安装 .NET 运行时（插件随游戏自带的 Mono 运行；安装器使用系统自带的 .NET Framework）

### 一键安装

1. 前往 [**Releases**](https://github.com/zlwzk/ColoringPixels-Tool/releases) 下载最新版 `ColoringPixelsTool-Setup-vX.Y.Z.exe`。
2. 关闭正在运行的《Coloring Pixels》。
3. 双击运行安装器：
   - 若有安全软件拦截，请选择「允许」/「仍要运行」。
   - 若游戏装在 `Program Files` 等受保护目录，安装器会提示以管理员身份重启。
4. 点击「**一键安装**」，等待进度条走完。
5. 安装器会**自动启动游戏**；进入任意关卡后按 <kbd>F1</kbd> 打开作弊面板。

> 首次启动游戏时 BepInEx 会初始化，可能比平时稍慢，属正常现象。

### 卸载

- 重新运行安装器，点击「**卸载**」；
- 或在「卸载」弹窗中勾选「同时移除 BepInEx 本体」以彻底清理。

卸载只会移除本插件与（可选的）BepInEx 本体，不会触碰游戏存档。

## 使用说明

### 快捷键

| 按键 | 功能 | 备注 |
| --- | --- | --- |
| <kbd>F1</kbd> | 打开 / 关闭面板 | 可在配置文件修改 |
| <kbd>F2</kbd> | 一键涂完当前关卡 | |
| <kbd>F3</kbd> | 开始 / 停止拟人涂色 | |
| <kbd>F4</kbd> | 清空当前画布 | |
| <kbd>F5</kbd> | 立即保存当前关卡 | |
| <kbd>F6</kbd> | 开关画布颜色高亮 | |

### 面板页面

- **涂色**：一键涂完、涂完当前颜色、清空、保存、重载；查看画布尺寸 / 颜色数量 / 进度；点击调色板色块可直接涂完该颜色。
- **拟人**：启动 / 停止自动涂色；调节手速、笔触长度、就近分块、停笔概率、手滑概率；可设置「先涂大面积颜色」「同步高亮调色板」「只涂当前颜色」「结束后自动保存」。
- **辅助**：高亮当前选中颜色在画布上的待涂格子；可自选高亮颜色（预设色块 / R·G·B 滑条）、样式（填充 / 描边 / 四角框）、不透明度，可只高亮未涂格子、可呼吸闪烁。
- **解锁**：解锁全部 DLC、免费提示（普通 / 重提示）、显示隐藏书籍、标记书籍完成、解锁 Steam 成就。
- **显示**：HUD 开关、颜色明细、不透明度、屏幕坐标；查看已完成像素 / 点击次数 / 存档累计 / 关卡状态。
- **字段**：编辑 `CrossLevelStorage` 存档字段与 `ClickTest` 运行时字段（进阶用途，请谨慎修改）。

### 配置文件

首次运行游戏后，插件会把默认配置写入：

```
<游戏目录>\BepInEx\config\coloringpixels.cheatsuite.cfg
```

可直接编辑该文件修改热键、参数与默认值（面板内的改动也会实时写回）。

## 命令行参数

安装器同时支持无人值守模式，便于脚本批量部署：

```text
ColoringPixelsTool-Setup.exe [选项]

  --dir=<路径>        指定游戏目录（跳过自动检测）
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
.\ColoringPixelsTool-Setup.exe --silent --dir="D:\Steam\steamapps\common\Coloring Pixels" --no-launch

# 只检测游戏目录
.\ColoringPixelsTool-Setup.exe --detect-only
```

## 常见问题

<details>
<summary><b>游戏里按 F1 没有反应？</b></summary>

- 确认已**进入关卡**（主菜单下面板逻辑不生效）。
- 检查游戏目录下是否存在 `winhttp.dll`、`BepInEx\core\BepInEx.Preloader.dll`、`BepInEx\plugins\ColoringPixelsTool.dll`。
- 查看 `BepInEx\LogOutput.log`，搜索 `Coloring Pixels Tool` 是否成功加载。

</details>

<details>
<summary><b>安装器提示「目录不可用」或找不到游戏？</b></summary>

- 请选择包含 `ColoringPixels.exe` 的那一层目录。
- 或使用 `--deep` 参数进行全盘深度扫描。
- 若游戏为 64 位主程序，本插件不支持。

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
├─ docs/                        使用与构建文档
├─ VERSION                      版本号（唯一来源）
└─ Directory.Build.props        全局 MSBuild 属性
```

## 免责声明

- 本项目仅供**学习、交流与单机娱乐**使用，请勿用于任何商业或非法用途。
- 请在下载后 **24 小时内**自行删除相关内容；因使用本工具产生的一切后果由使用者自负。
- 本项目与《Coloring Pixels》的开发商、发行商及 Steam 无关，游戏相关的全部权利归其各自所有者所有。
- 请支持正版游戏。

## 许可证

本项目基于 [MIT License](LICENSE) 开源。

若本工具基于或包含第三方作品，请一并遵守其原始授权与声明；发布前请确认你拥有相应的分发权利。
