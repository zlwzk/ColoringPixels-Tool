# 贡献指南

感谢你对本项目的关注！下面是参与开发与提交代码的一些约定。

## 提交 Issue

- 使用仓库自带的 Issue 模板（Bug 反馈 / 功能建议）。
- Bug 反馈请附上：系统版本、游戏版本、插件版本、`BepInEx/LogOutput.log` 相关片段、复现步骤。
- 安装器相关问题请附上 `%TEMP%\ColoringPixelsTool-Setup.log`。

## 提交 Pull Request

1. Fork 本仓库并从 `main` 拉出特性分支，例如 `feat/xxx`、`fix/xxx`。
2. 保持改动聚焦，一次 PR 只解决一件事。
3. 遵循现有代码风格：
   - 使用 C# 编写插件，兼容 BepInEx 5 + Unity 5.6 ~ 2019 的 Mono；
   - 安装器源码受系统自带 `csc.exe` 限制，**必须保持 C# 5 语法**（不要用字符串插值、表达式体成员、`?.` 等）；
   - 所有 `.cs` / `.ps1` 文件保存为 **UTF-8 with BOM**（避免中文在 PowerShell 5.1 与 csc 下乱码）。
4. 如果修改了插件源码，请重新编译并更新 `artifacts/ColoringPixelsTool.dll`：
   ```powershell
   .\scripts\build-mod.ps1
   ```
5. 本地完整验证构建：
   ```powershell
   .\scripts\build-release.ps1
   ```
6. 在 PR 描述中说明改动内容、测试方式与影响范围。

## 版本发布

版本号维护在根目录 [`VERSION`](VERSION) 中（`MAJOR.MINOR.PATCH`）。发布流程：

1. 更新 `VERSION` 与 `CHANGELOG.md`；
2. 提交并推送；
3. 打标签 `vX.Y.Z` 并推送：
   ```bash
   git tag v1.0.1
   git push origin v1.0.1
   ```
4. GitHub Actions 会自动构建安装器并创建 Release，产物包含
   `ColoringPixelsTool-Setup-vX.Y.Z.exe` 与 `SHA256SUMS.txt`。

## 注意事项

- 请勿提交游戏本体文件（`ColoringPixels.exe`、`ColoringPixels_Data/` 下的任何文件）。
- 请勿提交 `build/`、`dist/`、`installer/obj/`、`src/**/bin/`、`src/**/obj/` 等构建产物。
- `vendor/bepinex-x86/` 与 `artifacts/ColoringPixelsTool.dll` 需要保留，CI 依赖它们。
