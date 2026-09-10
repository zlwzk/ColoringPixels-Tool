#Requires -Version 5.1
<#
    一键构建发布包：

        1) build-mod.ps1        编译插件   -> artifacts\ColoringPixelsCheat.dll
        2) build-payload.ps1    组装部署包 -> build\payload.zip
        3) build-installer.ps1  编译安装器 -> dist\ColoringPixelsCheat-Setup-v<版本>.exe
        4) 生成校验和           -> dist\SHA256SUMS.txt

    版本号唯一来源是仓库根目录的 VERSION 文件。
    用法：
        .\scripts\build-release.ps1
        .\scripts\build-release.ps1 -SkipMod          # 不重新编译插件，直接用 artifacts 里的 DLL
        .\scripts\build-release.ps1 -GameDir "D:\..." # 指定游戏目录
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$GameDir,
    [switch]$SkipMod,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repoRoot 'dist'
$build = Join-Path $repoRoot 'build'

function Step($m) { Write-Host "`n== $m" -ForegroundColor Magenta }
function Ok($m) { Write-Host "  OK $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  !! $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  XX $m" -ForegroundColor Red; throw $m }

function Resolve-Version {
    param([string]$Explicit)
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { return $Explicit.Trim().TrimStart('v', 'V') }
    $vf = Join-Path $repoRoot 'VERSION'
    if (Test-Path -LiteralPath $vf) { return (Get-Content -LiteralPath $vf -Raw).Trim().TrimStart('v', 'V') }
    return '1.0.0'
}

$version = Resolve-Version -Explicit $Version

Write-Host ''
Write-Host ('  Coloring Pixels Cheat Suite  发布构建  v' + $version) -ForegroundColor White
Write-Host ('  仓库根目录：' + $repoRoot) -ForegroundColor DarkGray

# ---------------------------------------------------------------- 清理

if ($Clean) {
    Step '清理 build / dist'
    foreach ($d in @($build, $dist)) {
        if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
    Ok '已清理'
}

# ---------------------------------------------------------------- 1. 插件

if (-not $SkipMod) {
    Step '1/4  编译插件'
    $modArgs = @{ Configuration = 'Release' }
    if (-not [string]::IsNullOrWhiteSpace($GameDir)) { $modArgs['GameDir'] = $GameDir }
    & (Join-Path $PSScriptRoot 'build-mod.ps1') @modArgs
    Ok '插件编译完成'
}
else {
    Step '1/4  跳过插件编译（-SkipMod）'
    $artifact = Join-Path $repoRoot 'artifacts\ColoringPixelsCheat.dll'
    if (-not (Test-Path -LiteralPath $artifact)) { Fail "缺少已编译插件：$artifact（去掉 -SkipMod 重新编译）" }
    Ok ('使用现有插件：' + $artifact)
}

# ---------------------------------------------------------------- 2. 部署包

Step '2/4  组装部署包（BepInEx + 插件）'
$payloadArgs = @{ Output = $build }
if (-not [string]::IsNullOrWhiteSpace($GameDir)) { $payloadArgs['GameDir'] = $GameDir }
& (Join-Path $PSScriptRoot 'build-payload.ps1') @payloadArgs
Ok '部署包就绪'

# ---------------------------------------------------------------- 3. 安装器

Step '3/4  编译单文件安装器'
if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist -Force | Out-Null }
& (Join-Path $PSScriptRoot 'build-installer.ps1') -Version $version -Output $dist `
    -PayloadZip (Join-Path $build 'payload.zip')
Ok '安装器就绪'

# ---------------------------------------------------------------- 4. 校验和

Step '4/4  生成校验和'
$exe = Join-Path $dist ("ColoringPixelsCheat-Setup-v{0}.exe" -f $version)
if (-not (Test-Path -LiteralPath $exe)) { Fail "找不到安装器：$exe" }

$sumsFile = Join-Path $dist 'SHA256SUMS.txt'
$exeName = Split-Path -Leaf $exe
$exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()

$lines = New-Object System.Collections.ArrayList
[void]$lines.Add("# Coloring Pixels Cheat Suite v$version - SHA256 checksums")
[void]$lines.Add("# Generated (UTC): " + (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))
[void]$lines.Add("$exeHash  $exeName")

# payload.zip 也一并记录，便于核对
$payloadZip = Join-Path $build 'payload.zip'
if (Test-Path -LiteralPath $payloadZip) {
    $zipHash = (Get-FileHash -LiteralPath $payloadZip -Algorithm SHA256).Hash.ToLowerInvariant()
    [void]$lines.Add("$zipHash  payload.zip")
}

[System.IO.File]::WriteAllLines($sumsFile, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Ok ("校验和：$sumsFile")

# ---------------------------------------------------------------- 汇总

$exeInfo = Get-Item -LiteralPath $exe

Write-Host ''
Write-Host '  ==================================================' -ForegroundColor DarkGray
Write-Host ('  发布产物：' + $exeInfo.FullName) -ForegroundColor White
Write-Host ("  大小    ：{0:N0} KB" -f ($exeInfo.Length / 1KB)) -ForegroundColor DarkGray
Write-Host ("  SHA256  ：{0}" -f $exeHash) -ForegroundColor DarkGray
Write-Host '  ==================================================' -ForegroundColor DarkGray
Write-Host ''
Write-Host '  下一步：把 dist\ 里的 exe 与 SHA256SUMS.txt 上传到 GitHub Release。' -ForegroundColor DarkGray
Write-Host ''
