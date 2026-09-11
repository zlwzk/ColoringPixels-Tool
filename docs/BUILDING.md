# 构建与发布指南

本文档面向开发者，说明如何从源码构建插件、部署包与安装器，以及如何发版。

- [1. 前置条件](#1-前置条件)
- [2. 获取源码](#2-获取源码)
- [3. 版本号](#3-版本号)
- [4. 编译插件](#4-编译插件)
- [5. 一键构建发布包](#5-一键构建发布包)
- [6. 分步构建](#6-分步构建)
- [7. 产物说明](#7-产物说明)
- [8. 持续集成与发版](#8-持续集成与发版)
- [9. 自定义（仓库信息 / 图标）](#9-自定义仓库信息--图标)
- [10. 构建问题排查](#10-构建问题排查)

---

## 1. 前置条件

| 依赖 | 说明 |
| --- | --- |
| Windows 10 / 11 | 脚本使用 PowerShell 5.1 与 Windows 自带工具 |
| PowerShell 5.1 | 系统自带，无需额外安装 |
| 编译插件 | **必须**满足其一：.NET SDK / Visual Studio / Build Tools；或直接使用仓库内的 `artifacts\ColoringPixelsTool.dll` |
| 编译安装器 | 无需 .NET SDK，使用系统自带的 `csc.exe`（.NET Framework 4.x） |
| 《Coloring Pixels》 | **编译插件时必需**：插件引用游戏的 `Assembly-CSharp.dll` |

> 插件工程目标框架为 `netstandard2.0`，运行在游戏自带的 Mono 上。
> 安装器工程为 `net48` WinForms，但源码限制在 **C# 5** 语法，以便用系统自带 `csc.exe` 编译，因此最终用户**无需安装 .NET 运行时**。

## 2. 获取源码

```powershell
git clone https://github.com/zlwzk/ColoringPixels-Tool.git
cd ColoringPixelsTool
```

仓库结构见 [README · 目录结构](../README.md#目录结构)。

## 3. 版本号

版本号**唯一来源**是仓库根目录的 [`VERSION`](../VERSION) 文件（格式 `MAJOR.MINOR.PATCH`）：

- `Directory.Build.props` 读取它并写入程序集版本；
- `build-installer.ps1` / `build-release.ps1` 读取它并用于产物文件名。
- 插件源码中的 `Plugin.Version` 常量由 `publish.ps1` 自动同步；手动发版时才需要自己改。

## 4. 编译插件

```powershell
# 默认：自动查找游戏目录（仓库上一级）
.\scripts\build-mod.ps1

# 指定游戏目录
.\scripts\build-mod.ps1 -GameDir "D:\Steam\steamapps\common\Coloring Pixels"

# 强制使用某个后端
.\scripts\build-mod.ps1 -Backend csc      # csc | msbuild | dotnet | auto
```

脚本会按 `dotnet` → `MSBuild` → `Roslyn csc` 的顺序自动选择可用后端，产物输出到
`artifacts\ColoringPixelsTool.dll`。

> **为什么仓库里要提交一份编译好的 DLL？**
> 编译插件必须引用游戏自身的 `Assembly-CSharp.dll`，而 CI 与大多数协作者都没有游戏程序集。
> 因此仓库保留 `artifacts\ColoringPixelsTool.dll`，让 `build-release.ps1 -SkipMod` 与 CI 无需游戏即可出包。

## 5. 一键构建发布包

```powershell
# 完整构建：编译插件 -> 组装部署包 -> 编译安装器 -> 生成 SHA256
.\scripts\build-release.ps1

# 无游戏 / CI 场景：跳过插件编译，直接使用 artifacts 里的 DLL
.\scripts\build-release.ps1 -SkipMod

# 先清空 build / dist
.\scripts\build-release.ps1 -Clean
```

## 6. 分步构建

```powershell
# ① 仅编译插件
.\scripts\build-mod.ps1

# ② 仅组装部署包（BepInEx + 插件），产出 build\payload.zip
.\scripts\build-payload.ps1

# ③ 仅编译安装器（内嵌 payload.zip），产出 dist\ColoringPixelsTool-Setup-v<版本>.exe
.\scripts\build-installer.ps1

# ④ 仅生成图标（一般无需重复执行）
.\scripts\make-icon.ps1
```

各脚本参数：

| 脚本 | 主要参数 |
| --- | --- |
| `build-mod.ps1` | `-GameDir` `-Configuration` `-Output` `-Backend` `-Clean` |
| `build-payload.ps1` | `-Output` `-ModDll` `-GameDir` `-SkipZip` |
| `build-installer.ps1` | `-Version` `-Output` `-PayloadZip` |
| `build-release.ps1` | `-Version` `-GameDir` `-SkipMod` `-Clean` |

## 7. 产物说明

```
build\payload\                 部署包展开内容（winhttp.dll / doorstop_config.ini / BepInEx\...）
build\payload.zip              压缩后的部署包（被安装器内嵌）
dist\ColoringPixelsTool-Setup-v<版本>.exe   最终单文件安装器
dist\SHA256SUMS.txt            安装器与部署包的 SHA256 校验和
```

安装器是一个**自包含单文件**：内嵌 `payload.zip`，运行时解压部署，不依赖 .NET 运行时。

## 8. 持续集成与发版

- [`.github/workflows/build.yml`](../.github/workflows/build.yml)：每次 push / PR 触发，验证 `build-release.ps1 -SkipMod` 可正常出包，并上传构建产物。
- [`.github/workflows/release.yml`](../.github/workflows/release.yml)：推送 `v*` 标签时触发，自动构建并创建 GitHub Release，附带安装器与 `SHA256SUMS.txt`。

发版推荐用一条命令完成：

```powershell
# 提交 + 推送 + 自动升版本 + 打标签 + 等 CI + 把安装器同步到桌面
.\scripts\publish.ps1 -Message "feat: ..." -GameDir "D:\Steam\steamapps\common\Coloring Pixels"

# 只提交推送，不发版
.\scripts\publish.ps1 -Message "docs: ..." -NoRelease
```

脚本会自动同步 `VERSION` 与 `Plugin.Version`、重编译插件（保证面板版本与安装包一致）、提交推送、
打 `v*` 标签触发 CI，最后把安装器下载到桌面固定名 `ColoringPixelsTool-Setup.exe`。
当前版本已发过时会自动 +1 patch（也可用 `-Bump` / `-Version` 指定），所以发版前请先把
`CHANGELOG.md` 与 `RELEASE_NOTES.md`（CI 用它作为 Release 正文）写好。

手动发版：改 `VERSION` / `Plugin.Version` / `CHANGELOG.md` → 提交推送 →
`git tag vX.Y.Z` 与 `git push origin vX.Y.Z` → 等 Actions 完成后在 Releases 页面确认产物。

## 9. 自定义（仓库信息 / 图标）

- **仓库地址**：`Directory.Build.props` 的 `RepositoryUrl`、`installer/AppInfo.cs` 的 `DefaultRepositoryUrl`、
  `README.md` 徽章与 `CHANGELOG.md` 的版本链接中均已写入本仓库地址 `zlwzk/ColoringPixels-Tool`；
  若 fork 本项目，请把这几处一并改成你自己的仓库路径。
- **图标**：编辑 `scripts/make-icon.ps1` 的配色 / 图形后重新生成 `assets/icon.ico`。
- **安装器文案 / 配色**：见 `installer/Theme.cs` 与 `installer/MainForm.cs`（**保持 C# 5 语法**）。
- **插件名称 / 版本 / 快捷键**：见 `src/ColoringPixelsTool/Plugin.cs`。

## 10. 构建问题排查

| 现象 | 处理 |
| --- | --- |
| `找不到游戏程序集：...Assembly-CSharp.dll` | 用 `-GameDir` 指定游戏目录，或改用 `-SkipMod` 使用仓库内 DLL |
| `找不到 .NET SDK` / MSBuild 失败 | 脚本会自动回退到 csc；也可安装 .NET SDK 或 VS Build Tools |
| `找不到 Roslyn csc.exe` | 安装 Visual Studio / Build Tools，或使用 `-SkipMod` |
| 中文在脚本输出里乱码 | 确认 `.ps1` / `.cs` 保存为 **UTF-8 with BOM** |
| 安装器编译报 C# 语法错误 | 安装器源码必须兼容 C# 5（不能用插值字符串 / `?.` / 表达式体成员） |
| `payload.zip` 缺失 | 先运行 `build-payload.ps1`，或直接跑 `build-release.ps1` |
