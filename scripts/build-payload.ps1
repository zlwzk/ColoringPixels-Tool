#Requires -Version 5.1
<#
    组装「离线部署包」并打包成 build\payload.zip。

    payload 内容（相对游戏目录）：
        winhttp.dll                              doorstop 代理
        doorstop_config.ini                      doorstop 配置
        .doorstop_version                        doorstop 版本标记
        BepInEx\core\*.dll                       BepInEx 运行时
        BepInEx\plugins\ColoringPixelsTool.dll  作弊插件
        PixelAssist\PixelAssist.exe             独立助手（《涂色大师：像素梦想家》）

    安装器按顶层目录区分归属：
        * 根目录下的文件       -> Colors Pixels（注入式）
        * PixelAssist\ 下的文件 -> 涂色大师：像素梦想家（独立助手）

    installer\payload\ 下的同名文件会覆盖 vendor 中的文件（用于放我们的定制配置）。
#>
[CmdletBinding()]
param(
    [string]$Output,
    [string]$ModDll,
    [string]$AssistExe,
    [string]$GameDir,
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $repoRoot 'build' }

function Step($m) { Write-Host "  >> $m" -ForegroundColor Cyan }
function Ok($m) { Write-Host "  OK $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  !! $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  XX $m" -ForegroundColor Red; throw $m }

function Copy-Tree {
    param([string]$Source, [string]$Destination, [string]$Filter = '*')

    Get-ChildItem -LiteralPath $Source -Filter $Filter -Force | ForEach-Object {
        $target = Join-Path $Destination $_.Name
        if ($_.PSIsContainer) {
            if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
            Copy-Tree -Source $_.FullName -Destination $target
        }
        else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

function New-ZipFromDirectory {
    param([string]$Source, [string]$ZipPath)

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue

    $source = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\', '/')
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }

    $fs = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $files = Get-ChildItem -LiteralPath $source -Recurse -File -Force
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
                $entry = $zip.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
                $out = $entry.Open()
                try {
                    $in = [System.IO.File]::OpenRead($file.FullName)
                    try { $in.CopyTo($out) } finally { $in.Dispose() }
                }
                finally { $out.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $fs.Dispose() }
}

# ---------------------------------------------------------------- 1. vendor 检查

$vendor = Join-Path $repoRoot 'vendor\bepinex-x86'
$proxy = Join-Path $vendor 'winhttp.dll'
if (-not (Test-Path -LiteralPath $proxy)) {
    Fail "缺少 $proxy。请确认仓库中的 vendor\bepinex-x86 完整。"
}

$preloader = Join-Path $vendor 'BepInEx\core\BepInEx.Preloader.dll'
if (-not (Test-Path -LiteralPath $preloader)) {
    Fail "缺少 $preloader。请确认仓库中的 vendor\bepinex-x86 完整。"
}

# ---------------------------------------------------------------- 2. 找到插件 DLL

function Resolve-ModDll {
    param([string]$Explicit, [string]$Game)

    $candidates = New-Object System.Collections.ArrayList

    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        [void]$candidates.Add((Resolve-Path -LiteralPath $Explicit).Path)
    }

    [void]$candidates.Add((Join-Path $repoRoot 'src\ColoringPixelsTool\bin\Release\ColoringPixelsTool.dll'))
    [void]$candidates.Add((Join-Path $repoRoot 'artifacts\ColoringPixelsTool.dll'))

    if (-not [string]::IsNullOrWhiteSpace($Game)) {
        [void]$candidates.Add((Join-Path $Game 'BepInEx\plugins\ColoringPixelsTool.dll'))
    }

    $best = $null
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) {
            if ($null -eq $best -or (Get-Item -LiteralPath $c).LastWriteTimeUtc -gt (Get-Item -LiteralPath $best).LastWriteTimeUtc) {
                $best = $c
            }
        }
    }

    return $best
}

$modDll = Resolve-ModDll -Explicit $ModDll -Game $GameDir
if ($null -eq $modDll) {
    Fail "找不到 ColoringPixelsTool.dll。请先运行 scripts\build-mod.ps1，或把已编译的 DLL 放到 artifacts\ 下。"
}
Ok "使用插件 DLL：$modDll"

# ---------------------------------------------------------------- 3. 组装

$staging = Join-Path $Output 'payload'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

Step "复制 BepInEx x86 运行时……"
Copy-Tree -Source $vendor -Destination $staging

$pluginsDir = Join-Path $staging 'BepInEx\plugins'
if (-not (Test-Path -LiteralPath $pluginsDir)) { New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null }

Step "放置插件 $([System.IO.Path]::GetFileName($modDll))……"
Copy-Item -LiteralPath $modDll -Destination (Join-Path $pluginsDir 'ColoringPixelsTool.dll') -Force

# 覆盖层（我们的定制配置等）
$overlay = Join-Path $repoRoot 'installer\payload'
if (Test-Path -LiteralPath $overlay) {
    Step "应用 installer\payload 覆盖层……"
    Copy-Tree -Source $overlay -Destination $staging
}

# ---------------------------------------------------------------- 3.5 独立助手

function Resolve-AssistExe {
    param([string]$Explicit)

    $candidates = New-Object System.Collections.ArrayList
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        [void]$candidates.Add((Resolve-Path -LiteralPath $Explicit).Path)
    }
    [void]$candidates.Add((Join-Path $repoRoot 'src\PixelAssist\bin\Release\PixelAssist.exe'))
    [void]$candidates.Add((Join-Path $repoRoot 'artifacts\PixelAssist.exe'))

    $best = $null
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) {
            if ($null -eq $best -or (Get-Item -LiteralPath $c).LastWriteTimeUtc -gt (Get-Item -LiteralPath $best).LastWriteTimeUtc) {
                $best = $c
            }
        }
    }
    return $best
}

$assist = Resolve-AssistExe -Explicit $AssistExe
if ($null -eq $assist) {
    Warn '找不到 PixelAssist.exe——《涂色大师：像素梦想家》将无法安装（Coloring Pixels 不受影响）。'
    Warn '请先运行 scripts\build-assist.ps1，或把已编译的 EXE 放到 artifacts\ 下。'
}
else {
    $assistDir = Join-Path $staging 'PixelAssist'
    if (-not (Test-Path -LiteralPath $assistDir)) { New-Item -ItemType Directory -Path $assistDir -Force | Out-Null }
    Step "放置独立助手 $([System.IO.Path]::GetFileName($assist))……"
    Copy-Item -LiteralPath $assist -Destination (Join-Path $assistDir 'PixelAssist.exe') -Force
    Ok "独立助手：$assist"
}

# 清理 vendor 里不需要随包分发的东西
foreach ($junk in @('changelog.txt', 'README.md', 'LICENSE', 'doorstop_config.ini.bak')) {
    $p = Join-Path $staging $junk
    if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
}

# ---------------------------------------------------------------- 4. 打包

$files = Get-ChildItem -LiteralPath $staging -Recurse -File -Force
$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum

Write-Host ''
Write-Host ("  部署包内容（{0} 个文件，{1:N1} KB）：" -f $files.Count, ($totalBytes / 1KB)) -ForegroundColor DarkGray
foreach ($f in $files) {
    Write-Host ("    " + $f.FullName.Substring($staging.Length + 1)) -ForegroundColor DarkGray
}
Write-Host ''

Ok "部署包已生成：$staging"

if ($SkipZip) { return }

$zipPath = Join-Path $Output 'payload.zip'
New-ZipFromDirectory -Source $staging -ZipPath $zipPath
$zipInfo = Get-Item -LiteralPath $zipPath
Ok ("payload.zip 已生成：{0} ({1:N0} KB)" -f $zipInfo.FullName, ($zipInfo.Length / 1KB))
