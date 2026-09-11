#Requires -Version 5.1
<#
    一条命令走完：提交 -> 推送 -> 发版 -> 把安装器同步到桌面。

    发版由 CI 完成（.github/workflows/release.yml 收到 v* 标签后自动构建单文件安装器、
    创建 GitHub Release 并上传 exe 与 SHA256SUMS.txt）。本脚本在推完标签后会等 CI 出包，
    再把 exe 下载到桌面，文件名形如 ColoringPixelsTool-Setup-v1.2.1.exe，
    同时覆盖一份固定名字 ColoringPixelsTool-Setup.exe，方便直接双击调试。

    用法：
        .\scripts\publish.ps1 -Message "fix: 修复高亮闪烁"
            自动提交、推送；当前版本已发过就自动 +1 patch 并发版，最后把 exe 同步到桌面。

        .\scripts\publish.ps1 -Message "feat: 新增 XXX" -Bump minor
            升 minor 版本发版（可选 patch / minor / major）。

        .\scripts\publish.ps1 -Message "feat: 下一个大版本" -Version 2.0.0
            指定版本号发版。

        .\scripts\publish.ps1 -Message "wip: 只提交" -NoRelease
            只提交并推送，不发版、不下载 exe。

        .\scripts\publish.ps1 -Message "..." -NoDesktop
            发版但不往桌面同步 exe。

        .\scripts\publish.ps1 -Message "..." -GameDir "D:\Steam\...\Coloring Pixels"
            指定游戏目录，用于发版前重编译插件 DLL（让面板版本跟随安装包版本）。

        .\scripts\publish.ps1 -Message "..." -SkipMod
            跳过插件重编译，直接用仓库里已提交的 artifacts\ColoringPixelsTool.dll。

        关于版本号的一致性：
         VERSION 与 Plugin.Version 由本脚本同步；而 CI 打包时用的是仓库里已提交的
         artifacts\ColoringPixelsTool.dll（CI 拿不到游戏程序集，无法现场编译）。
         因此发版前必须在本地重编译插件，才能让面板显示的版本与安装包版本一致。
         本脚本会在提交前自动重编译（除非 -SkipMod）。
        #>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, HelpMessage = '提交信息，例如 "fix: 修复高亮闪烁"')]
    [string]$Message,

    [string]$Version,

    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',

    [string]$GameDir,

    [switch]$SkipMod,

    [switch]$NoRelease,

    [switch]$NoPush,

    [switch]$NoDesktop,

    [string]$DesktopDir,

    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $repoRoot 'VERSION'
$pluginFile = Join-Path $repoRoot 'src\ColoringPixelsTool\Plugin.cs'
$release = -not $NoRelease

function Step($m) { Write-Host "`n== $m" -ForegroundColor Magenta }
function Ok($m) { Write-Host "  OK $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  !! $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  XX $m" -ForegroundColor Red; throw $m }

# PowerShell 5.1 会把原生命令写到 stderr 的内容包成 NativeCommandError：既渲染成一片红色
# 报错，在 $ErrorActionPreference='Stop' 下还会直接中断脚本。这里统一在调用期间降级为
# SilentlyContinue，并把 stderr 落盘留档，退出码照常判断，信息也不丢。
function Invoke-Native {
    param([string]$Exe, [string[]]$ArgList)

    $errFile = [System.IO.Path]::GetTempFileName()
    $saved = $ErrorActionPreference
    $code = 0
    $out = ''
    $err = ''
    try {
        $ErrorActionPreference = 'SilentlyContinue'
        $out = (& $Exe @ArgList 2> $errFile | Out-String)
        $code = $LASTEXITCODE
        $err = (Get-Content -LiteralPath $errFile -Raw -ErrorAction SilentlyContinue)
    }
    finally {
        $ErrorActionPreference = $saved
        Remove-Item -LiteralPath $errFile -Force -ErrorAction SilentlyContinue
    }
    return [pscustomobject]@{ Code = $code; Out = $out; Err = $err }
}

function Git-Run {
    param([string[]]$GitArgs, [switch]$Quiet)

    $r = Invoke-Native -Exe 'git' -ArgList (@('-C', $repoRoot) + $GitArgs)
    if (-not $Quiet) {
        foreach ($chunk in @($r.Out, $r.Err)) {
            if (-not [string]::IsNullOrWhiteSpace($chunk)) { Write-Host $chunk.TrimEnd() }
        }
    }
    if ($r.Code -ne 0) { Fail ('git ' + ($GitArgs -join ' ') + " 执行失败（exit " + $r.Code + "）") }
    return $r.Out
}

function Gh-Run {
    param([string[]]$GhArgs)

    $r = Invoke-Native -Exe $script:ghPath -ArgList $GhArgs
    foreach ($chunk in @($r.Out, $r.Err)) {
        if (-not [string]::IsNullOrWhiteSpace($chunk)) { Write-Host $chunk.TrimEnd() }
    }
    if ($r.Code -ne 0) { Fail ('gh ' + ($GhArgs -join ' ') + " 执行失败（exit " + $r.Code + "）") }
    return $r.Out
}

function Resolve-Gh {
    $cmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @("$env:ProgramFiles\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe")) {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    Fail '未找到 GitHub CLI（gh）。请先安装：winget install GitHub.cli'
}

# 保持文件原有编码：有 BOM 的写回 BOM，没有的写回无 BOM。
function Save-PreservingBom {
    param([string]$Path, [string]$Text)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($hasBom)))
}

function Get-CurrentVersion {
    if (-not (Test-Path -LiteralPath $versionFile)) { Fail "缺少 VERSION 文件：$versionFile" }
    return ([System.IO.File]::ReadAllText($versionFile)).Trim().TrimStart('v', 'V')
}

# 同步 VERSION 与插件源码里的 Plugin.Version，避免两处版本号不一致。
function Set-ProjectVersion {
    param([string]$NewVersion)

    Save-PreservingBom -Path $versionFile -Text ($NewVersion + "`r`n")

    if (-not (Test-Path -LiteralPath $pluginFile)) {
        Warn "未找到插件源码，跳过 Plugin.Version 同步：$pluginFile"
        return
    }

    $src = [System.IO.File]::ReadAllText($pluginFile)
    $patched = [regex]::Replace($src, '(public const string Version = ")[^"]*(";)', ('${1}' + $NewVersion + '${2}'))
    if ($patched -eq $src) { Warn 'Plugin.cs 中未匹配到 Version 常量，请手动确认' }
    else { Save-PreservingBom -Path $pluginFile -Text $patched }
}

# 找到游戏目录（用于编译插件）。按优先级依次尝试：
# 显式参数 > 环境变量 CPT_GAME_DIR > 仓库的上一级 > 几个常见 Steam 安装位置。
function Resolve-GameDir {
    param([string]$Explicit)

    $candidates = New-Object System.Collections.ArrayList
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { [void]$candidates.Add($Explicit) }
    if (-not [string]::IsNullOrWhiteSpace($env:CPT_GAME_DIR)) { [void]$candidates.Add($env:CPT_GAME_DIR) }
    [void]$candidates.Add((Split-Path -Parent $repoRoot))
    foreach ($c in @(
            'D:\Steam\steamapps\common\Coloring Pixels',
            'C:\Program Files (x86)\Steam\steamapps\common\Coloring Pixels',
            'C:\Program Files\Steam\steamapps\common\Coloring Pixels'
        )) {
        [void]$candidates.Add($c)
    }

    foreach ($c in $candidates) {
        if ([string]::IsNullOrWhiteSpace($c)) { continue }
        $path = $null
        try { $path = (Resolve-Path -LiteralPath $c).Path } catch { continue }
        if (Test-Path -LiteralPath (Join-Path $path 'ColoringPixels_Data\Managed\Assembly-CSharp.dll')) {
            return $path
        }
    }
    return $null
}

# 校验 DLL 里是否真的带上了目标版本号。
# Plugin.Version 是编译期常量，会以 UTF-16 字符串的形式存进程序集元数据，
# 因此可以直接在字节里找它的 UTF-16 编码，确认面板将显示的版本。
function Test-DllVersion {
    param([string]$Dll, [string]$Version)

    try {
        $bytes = [System.IO.File]::ReadAllBytes($Dll)

        # 元数据堆通常 4 字节对齐，但为了稳妥，两种对齐都试一遍。
        if ([System.Text.Encoding]::Unicode.GetString($bytes).Contains($Version)) { return $true }
        if ($bytes.Length -gt 1 -and
            [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1).Contains($Version)) {
            return $true
        }
        return $false
    }
    catch {
        return $false
    }
}

# 重新编译插件，让 artifacts\ColoringPixelsTool.dll 里的 Plugin.Version 与 VERSION 保持一致。
# 这一步很关键：CI 打包用的是仓库里已提交的 DLL，不在本地重编译的话，
# 面板上显示的版本就会停在旧版本，和安装包对不上。
function Invoke-ModBuild {
    param([string]$Game, [string]$Version)

    if (-not (Test-Path -LiteralPath $pluginFile)) {
        Warn "未找到插件源码，跳过插件重编译：$pluginFile"
        return
    }

    $buildMod = Join-Path $PSScriptRoot 'build-mod.ps1'
    if (-not (Test-Path -LiteralPath $buildMod)) {
        Warn "未找到编译脚本，跳过插件重编译：$buildMod"
        return
    }

    # build-mod.ps1 自己会在失败时 throw，这里直接透传它的输出即可。
    & $buildMod -GameDir $Game -Configuration Release

    $dll = Join-Path $repoRoot 'artifacts\ColoringPixelsTool.dll'
    if (-not (Test-Path -LiteralPath $dll)) { Fail "插件编译后未找到产物：$dll" }
    Ok ('插件已重新编译：' + $dll)

    if (Test-DllVersion -Dll $dll -Version $Version) {
        Ok ('已确认面板版本：v' + $Version + '（与安装包一致）')
    }
    else {
        Warn ('未能在插件 DLL 中找到版本号 v' + $Version + '，面板显示的版本可能与安装包不一致')
    }
}

function Step-Version {
    param([string]$Current, [string]$Kind)

    $parts = $Current.Split('.')
    if ($parts.Count -ne 3) { Fail "无法解析当前版本号：$Current" }
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]

    switch ($Kind) {
        'major' { $major++; $minor = 0; $patch = 0 }
        'minor' { $minor++; $patch = 0 }
        default { $patch++ }
    }
    return ("" + $major + "." + $minor + "." + $patch)
}

function Test-TagExists {
    param([string]$Tag)

    if ((Invoke-Native -Exe 'git' -ArgList @('-C', $repoRoot, 'tag', '--list', $Tag)).Out.Trim().Length -gt 0) {
        return $true
    }
    $remote = (Invoke-Native -Exe 'git' -ArgList @('-C', $repoRoot, 'ls-remote', '--tags', 'origin', $Tag)).Out.Trim()
    return ($remote.Length -gt 0)
}

# ---------------------------------------------------------------- 0. 环境检查

Step '0/6  检查环境'
$ghPath = Resolve-Gh
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) { Fail "不是 git 仓库：$repoRoot" }
if ($release -and $NoPush) { Fail '-NoRelease 与 -NoPush 不能同时缺省：要发版必须能推送' }

$branch = (Git-Run @('rev-parse', '--abbrev-ref', 'HEAD') -Quiet).Trim()
$remote = (Git-Run @('remote', 'get-url', 'origin') -Quiet).Trim()
if ([string]::IsNullOrWhiteSpace($remote)) { Fail '未配置远端 origin' }
$slug = $remote -replace '^.*github\.com[:/]', '' -replace '\.git$', ''
$web = 'https://github.com/' + $slug
Ok ("分支：" + $branch)
Ok ("远端：" + $slug)

# ---------------------------------------------------------------- 1. 版本号

Step '1/6  确定版本号并重编译插件'
$current = Get-CurrentVersion

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $v = $Version.Trim().TrimStart('v', 'V')
    if ($v -notmatch '^\d+\.\d+\.\d+$') { Fail "版本号格式应为 MAJOR.MINOR.PATCH，收到：$Version" }
    Set-ProjectVersion -NewVersion $v
    Ok ('指定版本号 v' + $v + '，已同步 VERSION 与 Plugin.Version')
}
elseif ($release -and (Test-TagExists ('v' + $current))) {
    $v = Step-Version -Current $current -Kind $Bump
    Set-ProjectVersion -NewVersion $v
    Ok ('v' + $current + ' 已发布过，按 ' + $Bump + ' 自动升到 v' + $v)
}
else {
    $v = $current
    Ok ('使用当前版本 v' + $v)
}

# 重编译插件，确保 DLL 里的 Plugin.Version 与 VERSION 一致（面板版本跟随安装包版本）。
if ($SkipMod) {
    Warn '-SkipMod：跳过插件重编译，直接使用仓库里的 artifacts\ColoringPixelsTool.dll'
}
else {
    $game = Resolve-GameDir -Explicit $GameDir
    if ($null -eq $game) {
        Warn @'
未找到游戏目录，跳过插件重编译。
  这样一来，安装包里的插件仍是上一次编译的版本，面板显示的版本可能落后于安装包。
  如需保证一致，请用 -GameDir "…\Coloring Pixels" 指定游戏目录，或按下面方式重编译：
      .\scripts\build-mod.ps1 -Backend csc -GameDir "D:\Steam\steamapps\common\Coloring Pixels"
'@
    }
    else {
        Ok ('游戏目录：' + $game)
        Invoke-ModBuild -Game $game -Version $v
    }
}

# ---------------------------------------------------------------- 2. 提交

Step '2/6  提交改动'
Git-Run @('add', '-A') -Quiet

$staged = @((Git-Run @('diff', '--cached', '--name-only') -Quiet) -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
if ($staged.Count -eq 0) {
    Warn '没有需要提交的改动'
}
else {
    $msgFile = Join-Path $env:TEMP ('cpt-commit-' + [Guid]::NewGuid().ToString('N') + '.txt')
    [System.IO.File]::WriteAllText($msgFile, $Message, (New-Object System.Text.UTF8Encoding($false)))
    try { Git-Run @('commit', '-F', $msgFile) | Out-Null }
    finally { Remove-Item -LiteralPath $msgFile -Force -ErrorAction SilentlyContinue }
    Ok ('已提交 ' + $staged.Count + ' 个文件')
}

# ---------------------------------------------------------------- 3. 推送

if ($NoPush) {
    Warn '-NoPush：跳过推送'
}
else {
    Step '3/6  推送到 origin'
    Git-Run @('push', 'origin', $branch) | Out-Null
    Ok ('已推送 ' + $branch)
}

# ---------------------------------------------------------------- 4. 打标签

if (-not $release) {
    Step '4/6  发版'
    Warn '-NoRelease：跳过发版与桌面同步，流程结束'
    return
}

Step '4/6  打标签并推送（触发 CI 发版）'
$tag = 'v' + $v
if (Test-TagExists $tag) { Fail ('标签 ' + $tag + ' 已存在，无法重复发版') }

Git-Run @('tag', '-a', $tag, '-m', ('Coloring Pixels Tool ' + $tag)) | Out-Null
if ($NoPush) { Warn '-NoPush：标签只建在本地' }
else {
    Git-Run @('push', 'origin', $tag) | Out-Null
    Ok ('标签已推送：' + $tag)
}

# ---------------------------------------------------------------- 5. 等 CI

Step '5/6  等待 CI 构建 Release'
Write-Host ('  CI 运行状态：' + $web + '/actions') -ForegroundColor DarkGray

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$ready = $false
$started = Get-Date
while ((Get-Date) -lt $deadline) {
    $probe = Invoke-Native -Exe $ghPath -ArgList @('release', 'view', $tag, '-R', $slug, '--json', 'assets')
    if ($probe.Code -eq 0) { $ready = $true; break }

    $waited = [int]((Get-Date) - $started).TotalSeconds
    Write-Host ("  等待中…… " + $waited + "s") -ForegroundColor DarkGray
    Start-Sleep -Seconds 10
}

if (-not $ready) {
    Fail ('等待 CI 超时（' + $TimeoutSeconds + ' 秒）。请打开 ' + $web + '/actions 确认构建状态')
}
Ok 'Release 已生成'

# ---------------------------------------------------------------- 6. 同步到桌面

if ($NoDesktop) {
    Step '6/6  同步到桌面'
    Warn '-NoDesktop：跳过桌面同步'
}
else {
    Step '6/6  同步 exe 到桌面'

    if ([string]::IsNullOrWhiteSpace($DesktopDir)) { $desktop = [Environment]::GetFolderPath('Desktop') }
    else { $desktop = $DesktopDir }

    if (-not (Test-Path -LiteralPath $desktop)) { Fail ("桌面目录不存在：" + $desktop) }

    # 桌面上只放一个固定名字的安装器，每次发版覆盖，既方便双击调试也不会越攒越多。
    $desktopExe = Join-Path $desktop 'ColoringPixelsTool-Setup.exe'
    Gh-Run @('release', 'download', $tag, '-R', $slug, '--pattern', '*.exe', '--output', $desktopExe, '--clobber') | Out-Null

    if (-not (Test-Path -LiteralPath $desktopExe)) { Fail ('桌面安装器未生成：' + $desktopExe) }

    # 清掉早期发版留在桌面上的带版本号安装器（含更名前的 ColoringPixelsCheat 命名）
    Get-ChildItem -LiteralPath $desktop -File -Filter '*-Setup-v*.exe' -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
            Write-Host ('  已清理旧版安装器：' + $_.Name) -ForegroundColor DarkGray
        }

    $desktopInfo = Get-Item -LiteralPath $desktopExe
    Ok ('桌面安装器：' + $desktopInfo.FullName)
    Ok ('大小 ' + $desktopInfo.Length + ' 字节，SHA256 ' + (Get-FileHash -LiteralPath $desktopExe -Algorithm SHA256).Hash.ToLower())
}

# ---------------------------------------------------------------- 汇总

Write-Host ''
Write-Host '  ==================================================' -ForegroundColor DarkGray
Write-Host ('  版本    ：' + $tag) -ForegroundColor Cyan
Write-Host ('  仓库    ：' + $web) -ForegroundColor DarkGray
Write-Host ('  Release ：' + $web + '/releases/tag/' + $tag) -ForegroundColor Cyan
if (-not $NoDesktop) {
    Write-Host ('  桌面    ：' + (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ColoringPixelsTool-Setup.exe')) -ForegroundColor Cyan
}
Write-Host '  ==================================================' -ForegroundColor DarkGray
Write-Host ''
