#Requires -Version 5.1
<#
    编译作弊插件：src\ColoringPixelsCheat\*.cs  ->  artifacts\ColoringPixelsCheat.dll

    插件引用了游戏自己的程序集（Assembly-CSharp.dll / UnityEngine*.dll），
    因此必须在装有《Coloring Pixels》的机器上编译，或用本脚本的 csc 后端直接编译。

    后端优先级（-Backend auto 时自动挑选）：
        1. dotnet  —— 已安装 .NET SDK
        2. msbuild —— Visual Studio / Build Tools 自带的 MSBuild
        3. csc     —— VS 自带的 Roslyn csc.exe，直接编译（无需 SDK）

    如果三种后端都不可用，可以直接使用仓库内随包分发的
    artifacts\ColoringPixelsCheat.dll（CI 无法访问游戏程序集，因此该 DLL 随仓库提交）。
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$Output,
    [ValidateSet('auto', 'dotnet', 'msbuild', 'csc')][string]$Backend = 'auto',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projDir = Join-Path $repoRoot 'src\ColoringPixelsCheat'
$csproj = Join-Path $projDir 'ColoringPixelsCheat.csproj'
$artifacts = if ([string]::IsNullOrWhiteSpace($Output)) { Join-Path $repoRoot 'artifacts' } else { $Output }

function Step($m) { Write-Host "  >> $m" -ForegroundColor Cyan }
function Ok($m) { Write-Host "  OK $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  !! $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  XX $m" -ForegroundColor Red; throw $m }

# ---------------------------------------------------------------- 0. 基本检查

if (-not (Test-Path -LiteralPath $csproj)) { Fail "找不到工程文件：$csproj" }

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    # 默认：本仓库通常位于游戏目录下的 CheatTools\，因此上一级就是游戏目录
    $GameDir = Split-Path -Parent $repoRoot
}
try {
    $GameDir = (Resolve-Path -LiteralPath $GameDir).Path
}
catch {
    Fail "游戏目录不存在：$GameDir"
}

$managed = Join-Path $GameDir 'ColoringPixels_Data\Managed'
$gameAsm = Join-Path $managed 'Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $gameAsm)) {
    Fail @"
找不到游戏程序集：$gameAsm

本插件必须引用游戏自身的程序集才能编译。请：
  * 确认 -GameDir 指向《Coloring Pixels》安装目录（包含 ColoringPixels.exe 的那一层）；
  * 或直接使用仓库中已随包分发的 artifacts\ColoringPixelsCheat.dll（无需编译）。
"@
}

if (-not (Test-Path -LiteralPath $artifacts)) { New-Item -ItemType Directory -Path $artifacts -Force | Out-Null }

if ($Clean) {
    foreach ($d in @((Join-Path $projDir 'bin'), (Join-Path $projDir 'obj'))) {
        if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
    }
    Step '已清理 bin / obj'
}

Write-Host ''
Write-Host ("  游戏目录：" + $GameDir) -ForegroundColor DarkGray
Write-Host ("  配置    ：" + $Configuration) -ForegroundColor DarkGray
Write-Host ''

# ---------------------------------------------------------------- 后端探测

function Get-VsWhere {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe')
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
    return $null
}

function Find-MSBuild {
    $vswhere = Get-VsWhere
    if ($vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
        if ($found) { return ($found | Select-Object -First 1) }
    }

    $guesses = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'MSBuild\Current\Bin\MSBuild.exe')
    )
    foreach ($g in $guesses) { if (Test-Path -LiteralPath $g) { return $g } }
    return $null
}

function Find-RoslynCsc {
    $msbuild = Find-MSBuild
    if ($msbuild) {
        $roslyn = Join-Path (Split-Path -Parent $msbuild) 'Roslyn\csc.exe'
        if (Test-Path -LiteralPath $roslyn) { return $roslyn }
    }

    $vswhere = Get-VsWhere
    if ($vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\Roslyn\csc.exe' 2>$null
        if ($found) { return ($found | Select-Object -First 1) }
    }
    return $null
}

function Test-DotnetSdk {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $false }
    $sdks = & dotnet --list-sdks 2>$null
    return [bool]($sdks -and ($sdks | Where-Object { $_ -match '\d' }))
}

# ---------------------------------------------------------------- 编译后端

function Compile-WithDotnet {
    Step 'dotnet build……'
    $env:GameDir = $GameDir
    & dotnet build $csproj -c $Configuration --nologo -v:m
    return $LASTEXITCODE
}

function Compile-WithMSBuild {
    param([string]$MSBuild)
    Step "MSBuild：$MSBuild"
    $env:GameDir = $GameDir
    & $MSBuild $csproj "/p:Configuration=$Configuration" /nologo /v:m
    return $LASTEXITCODE
}

function Compile-WithCsc {
    param([string]$Csc)

    Step "Roslyn csc：$Csc"

    # 需要引用的程序集：BepInEx 核心 + 游戏程序集
    $core = Join-Path $repoRoot 'vendor\bepinex-x86\BepInEx\core'
    $refs = New-Object System.Collections.ArrayList

    foreach ($r in @('BepInEx.dll', '0Harmony.dll', 'BepInEx.Harmony.dll')) {
        $p = Join-Path $core $r
        if (Test-Path -LiteralPath $p) { [void]$refs.Add($p) }
    }

    foreach ($r in @(
            'netstandard.dll',
            'Assembly-CSharp.dll',
            'UnityEngine.dll',
            'UnityEngine.CoreModule.dll',
            'UnityEngine.IMGUIModule.dll',
            'UnityEngine.InputModule.dll',
            'UnityEngine.UIModule.dll',
            'UnityEngine.UI.dll',
            'UnityEngine.TilemapModule.dll',
            'UnityEngine.GridModule.dll',
            'UnityEngine.TextRenderingModule.dll',
            'UnityEngine.ImageConversionModule.dll'
        )) {
        $p = Join-Path $managed $r
        if (Test-Path -LiteralPath $p) { [void]$refs.Add($p) }
        else { Warn "缺少引用（可能不影响编译）：$p" }
    }

    $sources = @(Get-ChildItem -LiteralPath $projDir -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
    if ($sources.Count -eq 0) { Fail "在 $projDir 下没有找到任何 .cs 源文件" }

    $tmpOut = Join-Path $artifacts 'ColoringPixelsCheat.csc-tmp.dll'
    if (Test-Path -LiteralPath $tmpOut) { Remove-Item -LiteralPath $tmpOut -Force }

    $parts = New-Object System.Collections.ArrayList
    foreach ($a in @('/nologo', '/target:library', '/platform:anycpu', '/optimize+',
                     '/langversion:latest', '/warn:4', '/codepage:65001', '/utf8output')) {
        [void]$parts.Add($a)
    }
    [void]$parts.Add(('/out:"{0}"' -f $tmpOut))
    foreach ($r in $refs) { [void]$parts.Add(('/r:"{0}"' -f $r)) }
    foreach ($s in $sources) { [void]$parts.Add(('"{0}"' -f $s)) }

    $proc = Start-Process -FilePath $Csc -ArgumentList ($parts -join ' ') -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) { return $proc.ExitCode }

    $finalDll = Join-Path $artifacts 'ColoringPixelsCheat.dll'
    Copy-Item -LiteralPath $tmpOut -Destination $finalDll -Force
    Remove-Item -LiteralPath $tmpOut -Force
    return 0
}

# ---------------------------------------------------------------- 选择并执行

$done = $false
$code = 1

if ($Backend -eq 'auto' -or $Backend -eq 'dotnet') {
    if (Test-DotnetSdk) {
        $code = Compile-WithDotnet
        $done = $true
    }
    elseif ($Backend -eq 'dotnet') {
        Fail '找不到 .NET SDK（dotnet --list-sdks 为空）'
    }
    else {
        Warn '未检测到 .NET SDK，尝试其它后端'
    }
}

if (-not $done -and ($Backend -eq 'auto' -or $Backend -eq 'msbuild')) {
    $msbuild = Find-MSBuild
    if ($msbuild) {
        $code = Compile-WithMSBuild -MSBuild $msbuild
        $done = $true
        # SDK 风格的工程仍然需要 SDK；失败时继续尝试 csc
        if ($code -ne 0 -and $Backend -eq 'auto') {
            Warn 'MSBuild 编译失败（SDK 风格工程可能缺少 .NET SDK），改用 csc 后端'
            $done = $false
        }
    }
    elseif ($Backend -eq 'msbuild') {
        Fail '找不到 MSBuild.exe，请安装 Visual Studio 或 Build Tools'
    }
    else {
        Warn '未找到 MSBuild，尝试 csc 后端'
    }
}

if (-not $done -and ($Backend -eq 'auto' -or $Backend -eq 'csc')) {
    $csc = Find-RoslynCsc
    if ($csc) {
        $code = Compile-WithCsc -Csc $csc
        $done = $true
    }
    elseif ($Backend -eq 'csc') {
        Fail '找不到 Roslyn csc.exe，请安装 Visual Studio 或 Build Tools'
    }
}

if (-not $done) {
    Fail @'
没有可用的编译后端。请任选其一：
  * 安装 .NET SDK：https://dotnet.microsoft.com/download
  * 安装 Visual Studio / Build Tools（勾选「.NET 桌面开发」）
  * 或者直接使用仓库中已随包分发的 artifacts\ColoringPixelsCheat.dll
'@
}

if ($code -ne 0) { Fail "编译失败（退出码 $code）" }

# ---------------------------------------------------------------- 归集产物

$finalDll = Join-Path $artifacts 'ColoringPixelsCheat.dll'

if (-not (Test-Path -LiteralPath $finalDll)) {
    # dotnet / msbuild 默认输出到 bin\<配置>\
    $built = Join-Path $projDir "bin\$Configuration\ColoringPixelsCheat.dll"
    if (-not (Test-Path -LiteralPath $built)) { Fail "编译未产出 DLL：$built" }
    Copy-Item -LiteralPath $built -Destination $finalDll -Force
}

$info = Get-Item -LiteralPath $finalDll
Ok ("插件已生成：{0} ({1:N0} KB)" -f $info.FullName, ($info.Length / 1KB))

$sha = (Get-FileHash -LiteralPath $finalDll -Algorithm SHA256).Hash
Write-Host ("     SHA256: {0}" -f $sha) -ForegroundColor DarkGray
