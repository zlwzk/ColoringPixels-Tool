#Requires -Version 5.1
<#
    发布到 GitHub：同步版本号 -> 提交 -> 推送 -> （可选）打标签触发 CI 自动发布。

    CI 侧（.github/workflows/release.yml）在收到 v* 标签后会自动：
        1) 使用仓库内的 artifacts\ColoringPixelsCheat.dll 组装部署包
        2) 编译单文件安装器
        3) 创建 GitHub Release 并上传 exe 与 SHA256SUMS.txt

    因此本脚本只负责「版本号 + 提交 + 推送 + 打标签」，exe 由 CI 产出，
    不需要在本机安装游戏或完整编译。

    用法：
        .\scripts\publish.ps1 -Message "fix: 修复高亮闪烁"
        .\scripts\publish.ps1 -Message "feat: 新增 XXX" -Version 1.2.0 -Release
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, HelpMessage = '提交信息，例如 "feat: 新增 XXX"')]
    [string]$Message,

    [string]$Version,

    [switch]$Release,

    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $repoRoot 'VERSION'
$pluginFile = Join-Path $repoRoot 'src\ColoringPixelsCheat\Plugin.cs'

function Step($m) { Write-Host "`n== $m" -ForegroundColor Magenta }
function Ok($m) { Write-Host "  OK $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  !! $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  XX $m" -ForegroundColor Red; throw $m }

# git 会把进度写到 stderr，配合 $ErrorActionPreference='Stop' 会被当成终止性错误，
# 所以这里临时降级为 Continue，只按退出码判断成功与否。
function Git-OrFail {
    param([string[]]$GitArgs)

    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $text = (& git -C $repoRoot @GitArgs 2>&1 | Out-String) } finally { $ErrorActionPreference = $saved }
    $code = $LASTEXITCODE

    if (-not [string]::IsNullOrWhiteSpace($text)) { Write-Host $text.Trim() }
    if ($code -ne 0) { Fail ('git ' + ($GitArgs -join ' ') + ' 执行失败') }
}

# 保持文件原有编码：有 BOM 的写回 BOM，没有的写回无 BOM。
function Save-PreservingBom {
    param([string]$Path, [string]$Text)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($hasBom)))
}

# ---------------------------------------------------------------- 0. 前置检查

Step '0/5  检查仓库'
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) { Fail "不是 git 仓库：$repoRoot" }
if (-not (Test-Path -LiteralPath $versionFile)) { Fail "缺少 VERSION 文件：$versionFile" }
if (-not (Test-Path -LiteralPath $pluginFile)) { Fail "缺少插件源码：$pluginFile" }

$branch = (& git -C $repoRoot rev-parse --abbrev-ref HEAD | Out-String).Trim()
Ok ("分支：" + $branch)

# ---------------------------------------------------------------- 1. 版本号

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    Step '1/5  写入版本号'
    $v = $Version.Trim().TrimStart('v', 'V')
    if ($v -notmatch '^\d+\.\d+\.\d+$') { Fail "版本号格式应为 MAJOR.MINOR.PATCH，收到：$Version" }

    Save-PreservingBom -Path $versionFile -Text ($v + "`r`n")

    $src = [System.IO.File]::ReadAllText($pluginFile)
    $patched = [regex]::Replace($src, '(public const string Version = ")[^"]*(";)', ('${1}' + $v + '${2}'))
    if ($patched -eq $src) {
        Warn 'Plugin.cs 中未匹配到 Version 常量，请手动确认'
    }
    else {
        Save-PreservingBom -Path $pluginFile -Text $patched
    }
    Ok ('VERSION 与 Plugin.Version 已更新为 ' + $v)
}
else {
    $v = ([System.IO.File]::ReadAllText($versionFile)).Trim().TrimStart('v', 'V')
    Ok ('沿用当前版本：v' + $v)
}

# ---------------------------------------------------------------- 2. 提交

Step '2/5  提交改动'
Git-OrFail @('add', '-A')

$staged = @(& git -C $repoRoot diff --cached --name-only)
if ($staged.Count -eq 0) {
    Warn '没有需要提交的改动'
}
else {
    $msgFile = Join-Path $env:TEMP ('cpc-commit-' + [Guid]::NewGuid().ToString('N') + '.txt')
    [System.IO.File]::WriteAllText($msgFile, $Message, (New-Object System.Text.UTF8Encoding($false)))
    try { Git-OrFail @('commit', '-F', $msgFile) }
    finally { Remove-Item -LiteralPath $msgFile -Force -ErrorAction SilentlyContinue }
    Ok ('已提交 ' + $staged.Count + ' 个文件')
}

# ---------------------------------------------------------------- 3. 推送

if ($NoPush) {
    Warn '-NoPush：跳过推送'
}
else {
    Step '3/5  推送到 origin'
    Git-OrFail @('push', 'origin', $branch)
    Ok '已推送'
}

# ---------------------------------------------------------------- 4. 打标签

if (-not $Release) {
    Write-Host "`n  未指定 -Release，流程结束。" -ForegroundColor DarkGray
    return
}

Step '4/5  创建并推送标签'
$tag = 'v' + $v
if (@(& git -C $repoRoot tag --list $tag).Count -gt 0) {
    Fail ('标签 ' + $tag + ' 已存在。请用 -Version 提升版本号后再发布')
}

Git-OrFail @('tag', '-a', $tag, '-m', ('Coloring Pixels Cheat Suite ' + $tag))
if ($NoPush) { Warn '-NoPush：仅创建本地标签，未推送' }
else {
    Git-OrFail @('push', 'origin', $tag)
    Ok ('标签已推送：' + $tag)
}

# ---------------------------------------------------------------- 5. 汇总

Step '5/5  CI 开始构建发布包'
$remote = (& git -C $repoRoot remote get-url origin | Out-String).Trim()
$web = $remote -replace '\.git$', ''

Write-Host ''
Write-Host ('  远端   ：' + $web) -ForegroundColor DarkGray
Write-Host ('  标签   ：' + $tag) -ForegroundColor DarkGray
Write-Host '  CI 完成后安装器会自动出现在 Releases 页面：' -ForegroundColor DarkGray
Write-Host ('      ' + $web + '/releases') -ForegroundColor Cyan
Write-Host ''
