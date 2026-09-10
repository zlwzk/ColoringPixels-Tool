# 更新日志

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
版本号唯一来源是仓库根目录的 [`VERSION`](VERSION) 文件。

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

[1.2.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.2.0
[1.1.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.1.0
[1.0.0]: https://github.com/zlwzk/ColoringPixels-Tool/releases/tag/v1.0.0
