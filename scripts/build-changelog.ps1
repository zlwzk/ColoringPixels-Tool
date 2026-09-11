#Requires -Version 5.1
<#
    把两份 Markdown 转成「公告」源码，保证各处看到的文字始终是同一份：

        RELEASE_NOTES.md ──┬──> src\ColoringPixelsTool\Changelog.cs     游戏内 F1 面板的「更新公告」
                           ├──> installer\ReleaseNotes.cs              安装完成弹窗的「更新公告」
                           ├──> src\PixelAssist\AssistAnnounce.cs       助手「设置 → 更新公告」
                           └──> (同时是 GitHub Release 正文，CI 直接读取本文件)

        FEATURES.md ───────┬──> src\ColoringPixelsTool\FeatureGuide.cs  游戏内 F1 面板的「功能总览」（首次启动）
                           ├──> installer\FeatureGuide.cs              安装器首次安装时弹的「功能总览」
                           └──> src\PixelAssist\AssistAnnounce.cs       助手「设置 → 功能总览」

    助手（PixelAssist）把「更新公告 + 功能总览 + 版本号」合并生成到一个文件：
        RELEASE_NOTES.md + FEATURES.md + VERSION ──> src\PixelAssist\AssistAnnounce.cs

    两者的分工：
      * RELEASE_NOTES.md 是**每次发版都变**的更新说明，老用户升级后看到的就是它；
      * FEATURES.md 是**几乎不变**的全功能清单，只在用户第一次接触这个工具时展示一次
        （安装器首次安装 / 游戏内首次启动），之后的更新不再重复糊一遍全部功能。

    之所以要生成，是因为公告此前散落在多处手写：版本一升，面板与安装器弹窗就漏改，
    于是玩家看到的还是上一版的公告。现在只要改 Markdown，其余由本脚本产出，
    publish.ps1 / build-release.ps1 会在编译前自动调用，不会再脱节。

    用法：
        .\scripts\build-changelog.ps1
            生成五个源码文件（内容未变时不改写，避免无意义的 git 变更）。

        .\scripts\build-changelog.ps1 -Check
            只校验已提交的源码是否与 Markdown 同步，不写文件；
            不同步时以退出码 1 结束（供 CI / 发版前置检查使用）。
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $repoRoot 'VERSION'
$notesFile = Join-Path $repoRoot 'RELEASE_NOTES.md'
$featuresFile = Join-Path $repoRoot 'FEATURES.md'
$pluginNotesOut = Join-Path $repoRoot 'src\ColoringPixelsTool\Changelog.cs'
$installerNotesOut = Join-Path $repoRoot 'installer\ReleaseNotes.cs'
$pluginGuideOut = Join-Path $repoRoot 'src\ColoringPixelsTool\FeatureGuide.cs'
$installerGuideOut = Join-Path $repoRoot 'installer\FeatureGuide.cs'
$assistAnnounceOut = Join-Path $repoRoot 'src\PixelAssist\AssistAnnounce.cs'

function Step($m) { Write-Host "  >> $m" -ForegroundColor Cyan }
function Ok($m) { Write-Host "  OK $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  !! $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  XX $m" -ForegroundColor Red; throw $m }

# ---------------------------------------------------------------- 0. 输入

if (-not (Test-Path -LiteralPath $notesFile)) { Fail "缺少公告源文件：$notesFile" }
if (-not (Test-Path -LiteralPath $featuresFile)) { Fail "缺少功能总览源文件：$featuresFile" }
if (-not (Test-Path -LiteralPath $versionFile)) { Fail "缺少版本号文件：$versionFile" }

$version = ([System.IO.File]::ReadAllText($versionFile)).Trim().TrimStart('v', 'V')
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "VERSION 内容无法识别：$version" }

# ---------------------------------------------------------------- 1. Markdown -> 纯文本

# 去掉 Markdown 标记，转成适合 Unity IMGUI / WinForms TextBox 显示的纯文本。
function Convert-Inline {
    param([string]$Text)

    $t = $Text
    $t = $t -replace '\*\*([^*]+)\*\*', '$1'          # 粗体
    $t = $t -replace '\*([^*]+)\*', '$1'              # 斜体
    $t = $t -replace '__([^_]+)__', '$1'
    $t = $t -replace '`([^`]+)`', '$1'                # 行内代码
    $t = $t -replace '<kbd>([^<]*)</kbd>', '$1'       # 按键
    $t = $t -replace '<[^>]+>', ''                    # 其它 HTML
    $t = [regex]::Replace($t, '\[([^\]]+)\]\(([^)]+)\)', '$1')  # 链接只留文字
    return $t.TrimEnd()
}

# 把一份 Markdown 转成「纯文本行」数组：折叠连续空行、去掉首尾空行。
function Convert-Markdown {
    param([string]$Path)

    $raw = [System.IO.File]::ReadAllText($Path)
    # 统一换行，便于逐行处理
    $raw = $raw -replace "`r`n", "`n" -replace "`r", "`n"
    $lines = @($raw -split "`n")

    $converted = New-Object System.Collections.ArrayList

    foreach ($line in $lines) {
        $line = $line.TrimEnd()

        # YAML / 水平分割线
        if ($line -match '^\s*-{3,}\s*$') { continue }

        # 标题：## xxx -> xxx
        if ($line -match '^\s*#{1,6}\s*(.+?)\s*$') {
            [void]$converted.Add('')
            [void]$converted.Add((Convert-Inline $Matches[1]))
            continue
        }

        # 引用块
        if ($line -match '^\s*>\s?(.*)$') {
            [void]$converted.Add(('  ' + (Convert-Inline $Matches[1])))
            continue
        }

        # 表格
        if ($line -match '^\s*\|') {
            $cells = @($line.Trim().Trim('|') -split '\|' | ForEach-Object { (Convert-Inline $_).Trim() })
            # 分隔行 |---|---|
            if ($cells.Count -gt 0 -and (@($cells | Where-Object { $_ -notmatch '^:?-{2,}:?$' }).Count -eq 0)) { continue }
            $text = (@($cells | Where-Object { $_ -ne '' }) -join '  ')
            [void]$converted.Add(('· ' + $text))
            continue
        }

        # 无序列表：保留缩进，项目符号换成 ·
        if ($line -match '^(\s*)[-*+]\s+(.*)$') {
            $indent = $Matches[1]
            [void]$converted.Add(($indent + '· ' + (Convert-Inline $Matches[2])))
            continue
        }

        # 有序列表：保留原编号
        if ($line -match '^(\s*)(\d+)\.\s+(.*)$') {
            [void]$converted.Add(($Matches[1] + $Matches[2] + '. ' + (Convert-Inline $Matches[3])))
            continue
        }

        # 普通段落 / 空行
        [void]$converted.Add((Convert-Inline $line))
    }

    $body = New-Object System.Collections.ArrayList
    $prevBlank = $true
    foreach ($l in $converted) {
        $isBlank = [string]::IsNullOrWhiteSpace($l)
        if ($isBlank) {
            if ($prevBlank) { continue }
            [void]$body.Add('')
            $prevBlank = $true
        }
        else {
            [void]$body.Add($l)
            $prevBlank = $false
        }
    }
    while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[$body.Count - 1])) {
        $body.RemoveAt($body.Count - 1)
    }

    if ($body.Count -eq 0) { Fail ("转换后为空，请检查内容：" + $Path) }
    return $body
}

# 函数返回集合时 PowerShell 会把它摊平成数组（甚至单个字符串），这里统一收回成字符串数组。
$noteLines = @(Convert-Markdown -Path $notesFile)
$featureLines = @(Convert-Markdown -Path $featuresFile)

# RELEASE_NOTES.md 里通常会写明版本号，若与 VERSION 对不上，多半是忘改公告了。
if ([System.IO.File]::ReadAllText($notesFile) -notmatch [regex]::Escape($version)) {
    Warn ("RELEASE_NOTES.md 中未出现当前版本号 " + $version + "，请确认公告已更新到最新版本")
}

# ---------------------------------------------------------------- 2. 生成 C# 源码

function Escape-CsString {
    param([string]$Text)
    $t = $Text -replace '\\', '\\'
    $t = $t -replace '"', '\"'
    return $t
}

# 生成形如 "第一行\n" + "第二行" 的字符串字面量（逐行拼接，避免超长单行）。
function New-BodyLiteral {
    param([string[]]$BodyLines, [string]$NewLineToken)

    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $BodyLines.Count; $i++) {
        $escaped = Escape-CsString $BodyLines[$i]
        if ($i -lt $BodyLines.Count - 1) {
            [void]$sb.Append('"' + $escaped + $NewLineToken + '" +')
        }
        else {
            [void]$sb.Append('"' + $escaped + '";')
        }
        if ($i -lt $BodyLines.Count - 1) { [void]$sb.Append("`r`n") }
    }
    return $sb.ToString()
}

# 生成一个「只有常量」的静态类源码文件。
function New-SourceFile {
    param(
        [string]$Namespace,
        [string]$ClassName,
        [string]$SourceFile,
        [string]$Summary,
        [string]$BodyLiteral,
        [string]$VersionMemberName,
        [string]$BodyMemberName,
        [string]$ExtraBodyName,
        [string]$ExtraBodyLiteral
    )

    if ([string]::IsNullOrEmpty($BodyMemberName)) { $BodyMemberName = 'Body' }

    $head = @"
// <auto-generated>
//   由 scripts\build-changelog.ps1 从仓库根目录的 $SourceFile 生成，请勿手动修改。
//   要调整内容，请编辑 $SourceFile，再重新运行该脚本。
// </auto-generated>
namespace $Namespace
{
    /// <summary>$Summary</summary>
    internal static class $ClassName
    {
"@

    if (-not [string]::IsNullOrEmpty($VersionMemberName)) {
        $head = $head + "`r`n        public const string $VersionMemberName = `"$version`";`r`n"
    }

    $tail = "        public const string $BodyMemberName =`r`n$BodyLiteral`r`n"
    if (-not [string]::IsNullOrEmpty($ExtraBodyName)) {
        $tail = $tail + "`r`n        public const string $ExtraBodyName =`r`n$ExtraBodyLiteral`r`n"
    }
    $tail = $tail + "    }`r`n}`r`n"

    return (($head + "`r`n" + $tail) -replace "`r`n", "`n") -replace "`n", "`r`n"
}

$pluginNotesCs = New-SourceFile -Namespace 'ColoringPixelsTool' -ClassName 'Changelog' `
    -SourceFile 'RELEASE_NOTES.md' -Summary '软件更新公告文本（来源：RELEASE_NOTES.md / VERSION）。' `
    -BodyLiteral (New-BodyLiteral -BodyLines ([string[]]$noteLines) -NewLineToken '\n') `
    -VersionMemberName 'CurrentVersion'

$installerNotesCs = New-SourceFile -Namespace 'ColoringPixelsTool.Installer' -ClassName 'ReleaseNotes' `
    -SourceFile 'RELEASE_NOTES.md' -Summary '安装完成后弹窗的更新公告文本（来源：RELEASE_NOTES.md / VERSION）。' `
    -BodyLiteral (New-BodyLiteral -BodyLines ([string[]]$noteLines) -NewLineToken '\r\n') `
    -VersionMemberName 'Version'

$pluginGuideCs = New-SourceFile -Namespace 'ColoringPixelsTool' -ClassName 'FeatureGuide' `
    -SourceFile 'FEATURES.md' -Summary '全功能总览文本（来源：FEATURES.md）。首次使用时展示一次，之后只看更新公告。' `
    -BodyLiteral (New-BodyLiteral -BodyLines ([string[]]$featureLines) -NewLineToken '\n') `
    -VersionMemberName ''

$installerGuideCs = New-SourceFile -Namespace 'ColoringPixelsTool.Installer' -ClassName 'FeatureGuide' `
    -SourceFile 'FEATURES.md' -Summary '首次安装时弹窗的全功能总览文本（来源：FEATURES.md）。' `
    -BodyLiteral (New-BodyLiteral -BodyLines ([string[]]$featureLines) -NewLineToken '\r\n') `
    -VersionMemberName ''

$assistAnnounceCs = New-SourceFile -Namespace 'PixelAssist' -ClassName 'AssistAnnounce' `
    -SourceFile 'RELEASE_NOTES.md、FEATURES.md' -Summary '助手面板的更新公告与功能总览文本（来源：RELEASE_NOTES.md、FEATURES.md / VERSION）。' `
    -BodyLiteral (New-BodyLiteral -BodyLines ([string[]]$noteLines) -NewLineToken '\r\n') `
    -BodyMemberName 'Changelog' `
    -VersionMemberName 'Version' `
    -ExtraBodyName 'Features' `
    -ExtraBodyLiteral (New-BodyLiteral -BodyLines ([string[]]$featureLines) -NewLineToken '\r\n')

$utf8Bom = New-Object System.Text.UTF8Encoding($true)

# ---------------------------------------------------------------- 3. 写入 / 校验

function Sync-One {
    param([string]$Path, [string]$Text, [string]$Label)

    $existing = $null
    if (Test-Path -LiteralPath $Path) {
        $existing = [System.IO.File]::ReadAllText($Path)
    }

    if ($existing -eq $Text) {
        Ok ($Label + ' 已是最新：' + (Split-Path -Leaf $Path))
        return $false
    }

    if ($Check) {
        Warn ($Label + ' 未同步：' + (Split-Path -Leaf $Path))
        return $true
    }

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Text, $utf8Bom)
    Ok ($Label + ' 已更新：' + (Split-Path -Leaf $Path))
    return $true
}

Step ("同步公告与功能总览（版本 " + $version + "）")
$dirty = $false
if (Sync-One -Path $pluginNotesOut -Text $pluginNotesCs -Label '游戏内·更新公告') { $dirty = $true }
if (Sync-One -Path $installerNotesOut -Text $installerNotesCs -Label '安装器·更新公告') { $dirty = $true }
if (Sync-One -Path $pluginGuideOut -Text $pluginGuideCs -Label '游戏内·功能总览') { $dirty = $true }
if (Sync-One -Path $installerGuideOut -Text $installerGuideCs -Label '安装器·功能总览') { $dirty = $true }
if (Sync-One -Path $assistAnnounceOut -Text $assistAnnounceCs -Label '助手·公告与总览') { $dirty = $true }

if ($Check -and $dirty) {
    Fail '公告源码与 Markdown 源文件不同步，请运行 scripts\build-changelog.ps1 后重新提交'
}

if (-not $dirty) { Ok '五处公告源码均与 Markdown 源文件保持一致' }
