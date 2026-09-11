欢迎来到 **Coloring Pixels Tool V2.2.3**！

这一版没有加功能，专门把「更新公告」这条线理顺：以后不会再有「安装包已经更新、面板公告却还停在旧版本」的情况。

## 🐛 本次修复

### 1. 面板与安装器的更新公告会自动跟版本走

公告此前散在三处、各写各的：

- GitHub Release 正文（`RELEASE_NOTES.md`）；
- 游戏内 <kbd>F1</kbd> 面板里的「更新公告」；
- 安装器安装完成后的弹窗。

于是版本一升，后两处经常忘了改——v2.2.2 就是如此：安装包是新的，公告却还停在 2.2.1。

现在改为**单一来源**：`RELEASE_NOTES.md` 是唯一出处，新增脚本 `scripts/build-changelog.ps1`
会在编译前把它自动转换成两个源码文件：

- `src/ColoringPixelsTool/Changelog.cs` —— 游戏内面板公告；
- `installer/ReleaseNotes.cs` —— 安装器弹窗公告。

`publish.ps1` 与 CI 所用的 `build-release.ps1` 都会先执行这一步。
发版时如果公告里没写到新版本号，脚本会直接报错提醒，从源头堵住「忘了改公告」。

### 2. 公告排版适配面板与弹窗

转换时会把 Markdown 的标题、加粗、列表、表格与 <kbd>按键</kbd> 标记统一转成纯文本，
面板与弹窗里显示的是干净正文，不会再冒出 `**`、`##`、`<kbd>` 这类符号。

## 📖 文档补充

`docs/BUILDING.md` 增加「更新公告」一节，说明公告的唯一来源与生成方式。

## 💡 小提示

- Coloring Pixels：按 <kbd>F1</kbd> 打开 / 关闭面板，<kbd>F7</kbd> 开始人工辅助。
- 涂色大师：像素梦想家：先启动游戏，再运行 `PixelAssist\PixelAssist.exe`。
- 想改公告内容：编辑仓库根目录的 `RELEASE_NOTES.md` 即可，面板与安装器会自动同步。

## 📦 安装包

| 文件 | 说明 |
|---|---|
| `ColoringPixelsTool-Setup.exe` | 一键安装 / 卸载 / 更新 |
| `ColoringPixelsTool.dll` | 《Coloring Pixels》的 BepInEx 插件 |
| `PixelAssist\PixelAssist.exe` | 《涂色大师：像素梦想家》的独立屏幕扫描助手 |
