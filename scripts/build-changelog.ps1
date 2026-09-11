#Requires -Version 5.1
<#
    从 RELEASE_NOTES.md 生成「更新公告」源码，让三处公告始终同步：

        RELEASE_NOTES.md  ──┬──> src\ColoringPixelsTool\Changelog.cs   游戏内 F1 面板公告
                            └──> installer\ReleaseNotes.cs            安装完成后的弹窗公告

    RELEASE_NOTES.md 是**唯一来源**，它同时作为 GitHub Release 正文（CI 读取）。
    版本号取自仓库根目录的 VERSION 文件（与 Plugin.Version 保持一致）。

    之所以要生成，是因为公告此前散落在三处手写：版本一升，面板与安装器弹窗就漏改，
    于是玩家看到的还是上一版的公告。现在只要改 RELEASE_NOTES.md，其余两处由本脚本产出，
    publish.ps1 / build-release.ps1 会在编译前自动调用，不会再脱节。

    用法：
        .\scripts\build-changelog.ps1
            生成两个源码文件（内容未变时不改写，避免无意义的 git 变更）。

        .\scripts\build-changelog.ps1 -Check
            只校验已提交的源码是否与 RELEASE_NOTES.md 同步，不写文件；
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
$pluginOut = Join-Path $repoRoot 'src\ColoringPixelsTool\Changelog.cs'
$installerOut = Join-Path $repoRoot 'installer\ReleaseNotes.cs'

function Step($m) { Write-Host "  >> $m" -ForegroundColor Cyan }
function Ok($m) { Write-Host "  OK $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  !! $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  XX $m" -ForegroundColor Red; throw $m }

# ---------------------------------------------------------------- 0. 输入

if (-not (Test-Path -LiteralPath $notesFile)) { Fail "缺少公告源文件：$notesFile" }
if (-not (Test-Path -LiteralPath $versionFile)) { Fail "缺少版本号文件：$versionFile" }

$version = ([System.IO.File]::ReadAllText($versionFile)).Trim().TrimStart('v', 'V')
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "VERSION 内容无法识别：$version" }

$rawNotes = [System.IO.File]::ReadAllText($notesFile)
# 统一换行，便于逐行处理
$rawNotes = $rawNotes -replace "`r`n", "`n" -replace "`r", "`n"
$lines = @($rawNotes -split "`n")

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

# 折叠连续空行，并去掉首尾空行
$bodyLines = New-Object System.Collections.ArrayList
$prevBlank = $true
foreach ($l in $converted) {
    $isBlank = [string]::IsNullOrWhiteSpace($l)
    if ($isBlank) {
        if ($prevBlank) { continue }
        [void]$bodyLines.Add('')
        $prevBlank = $true
    }
    else {
        [void]$bodyLines.Add($l)
        $prevBlank = $false
    }
}
while ($bodyLines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($bodyLines[$bodyLines.Count - 1])) {
    $bodyLines.RemoveAt($bodyLines.Count - 1)
}

if ($bodyLines.Count -eq 0) { Fail "RELEASE_NOTES.md 转换后为空，请检查内容" }

# RELEASE_NOTES.md 里通常会写明版本号，若与 VERSION 对不上，多半是忘改公告了。
if ($rawNotes -notmatch [regex]::Escape($version)) {
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

$pluginBody = New-BodyLiteral -BodyLines $bodyLines.ToArray() -NewLineToken '\n'
$installerBody = New-BodyLiteral -BodyLines $bodyLines.ToArray() -NewLineToken '\r\n'

$pluginCs = @"
// <auto-generated>
//   由 scripts\build-changelog.ps1 从仓库根目录的 RELEASE_NOTES.md 生成，请勿手动修改。
//   要调整公告，请编辑 RELEASE_NOTES.md（它同时是 GitHub Release 正文），再重新运行该脚本。
// </auto-generated>
namespace ColoringPixelsTool
{
    /// <summary>软件更新公告文本（来源：RELEASE_NOTES.md / VERSION）。</summary>
    internal static class Changelog
    {
        public const string CurrentVersion = "$version";

        public const string Body =
$pluginBody
    }
}
"@

$installerCs = @"
// <auto-generated>
//   由 scripts\build-changelog.ps1 从仓库根目录的 RELEASE_NOTES.md 生成，请勿手动修改。
//   要调整公告，请编辑 RELEASE_NOTES.md（它同时是 GitHub Release 正文），再重新运行该脚本。
// </auto-generated>
namespace ColoringPixelsTool.Installer
{
    /// <summary>安装完成后弹窗的更新公告文本（来源：RELEASE_NOTES.md / VERSION）。</summary>
    internal static class ReleaseNotes
    {
        public const string Version = "$version";

        public const string Body =
$installerBody
    }
}
"@

# 统一 CRLF
$pluginCs = ($pluginCs -replace "`r`n", "`n") -replace "`n", "`r`n"
$installerCs = ($installerCs -replace "`r`n", "`n") -replace "`n", "`r`n"

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

Step ("同步更新公告（版本 " + $version + "）")
$dirty = $false
if (Sync-One -Path $pluginOut -Text $pluginCs -Label '游戏内面板公告') { $dirty = $true }
if (Sync-One -Path $installerOut -Text $installerCs -Label '安装器弹窗公告') { $dirty = $true }

if ($Check -and $dirty) {
    Fail '公告源码与 RELEASE_NOTES.md 不同步，请运行 scripts\build-changelog.ps1 后重新提交'
}

if (-not $dirty) { Ok '两处公告均与 RELEASE_NOTES.md 保持一致' }
