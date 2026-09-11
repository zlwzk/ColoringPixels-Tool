#Requires -Version 5.1
<#
    编译《涂色大师：像素梦想家》的独立助手：
        src\PixelAssist\*.cs  +  src\ColoringPixelsTool\Assist*.cs
            ->  artifacts\PixelAssist.exe

    这个助手的核心扫描逻辑与游戏内插件共用同一份源码（AssistRegion / AssistEngine /
    AssistStore / AssistWin32），因此不需要任何游戏程序集，任何机器都能编译。

    后端优先级（-Backend auto 时自动挑选）：
        1. dotnet  —— 已安装 .NET SDK（net48 需要对应的引用程序集）
        2. csc     —— VS 自带 / .NET Framework 自带的 csc.exe，直接引用 GAC 里的程序集

    两者都不可用时，回落到仓库内随包分发的 artifacts\PixelAssist.exe。
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$Output,
    [ValidateSet('auto', 'dotnet', 'csc')][string]$Backend = 'auto',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projDir = Join-Path $repoRoot 'src\PixelAssist'
$csproj = Join-Path $projDir 'PixelAssist.csproj'
$sharedDir = Join-Path $repoRoot 'src\ColoringPixelsTool'
$artifacts = if ([string]::IsNullOrWhiteSpace($Output)) { Join-Path $repoRoot 'artifacts' } else { $Output }
$exeName = 'PixelAssist.exe'

function Step($m) { Write-Host "  >> $m" -ForegroundColor Cyan }
function Ok($m) { Write-Host "  OK $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  !! $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  XX $m" -ForegroundColor Red; throw $m }

if (-not (Test-Path -LiteralPath $csproj)) { Fail "找不到工程文件：$csproj" }
if (-not (Test-Path -LiteralPath $artifacts)) { New-Item -ItemType Directory -Path $artifacts -Force | Out-Null }

if ($Clean) {
    foreach ($d in @((Join-Path $projDir 'bin'), (Join-Path $projDir 'obj'))) {
        if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
    }
    Step '已清理 bin / obj'
}

Write-Host ''
Write-Host ("  工程    ：" + $csproj) -ForegroundColor DarkGray
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

function Find-RoslynCsc {
    $vswhere = Get-VsWhere
    if ($vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\Roslyn\csc.exe' 2>$null
        if ($found) { return ($found | Select-Object -First 1) }
    }

    $guesses = @()
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        $guesses += (Join-Path $root 'Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe')
        $guesses += (Join-Path $root 'Microsoft Visual Studio\17\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe')
        $guesses += (Join-Path $root 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe')
    }
    foreach ($g in $guesses) { if (Test-Path -LiteralPath $g) { return $g } }
    return $null
}

function Find-FrameworkCsc {
    foreach ($bit in @('Framework64', 'Framework')) {
        $p = Join-Path $env:WINDIR ("Microsoft.NET\$bit\v4.0.30319\csc.exe")
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

function Get-FrameworkDir {
    foreach ($bit in @('Framework64', 'Framework')) {
        $p = Join-Path $env:WINDIR "Microsoft.NET\$bit\v4.0.30319"
        if (Test-Path -LiteralPath (Join-Path $p 'System.Windows.Forms.dll')) { return $p }
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
    & dotnet build $csproj -c $Configuration --nologo -v:m
    return $LASTEXITCODE
}

function Compile-WithCsc {
    param([string]$Csc)

    Step "csc：$Csc"

    $fw = Get-FrameworkDir
    if (-not $fw) { Fail '找不到 .NET Framework 4.x 引用程序集（System.Windows.Forms.dll）' }

    $refs = New-Object System.Collections.ArrayList
    foreach ($r in @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')) {
        $p = Join-Path $fw $r
        if (Test-Path -LiteralPath $p) { [void]$refs.Add($p) }
        else { Fail "缺少引用程序集：$p" }
    }

    $sources = New-Object System.Collections.ArrayList
    foreach ($f in (Get-ChildItem -LiteralPath $projDir -Filter '*.cs' -File | Sort-Object Name)) {
        [void]$sources.Add($f.FullName)
    }
    foreach ($name in @('AssistRegion.cs', 'AssistEngine.cs', 'AssistStore.cs', 'AssistWin32.cs')) {
        $p = Join-Path $sharedDir $name
        if (-not (Test-Path -LiteralPath $p)) { Fail "缺少共享源文件：$p" }
        [void]$sources.Add($p)
    }
    if ($sources.Count -eq 0) { Fail "在 $projDir 下没有找到任何 .cs 源文件" }

    $tmpOut = Join-Path $artifacts ($exeName + '.tmp')
    if (Test-Path -LiteralPath $tmpOut) { Remove-Item -LiteralPath $tmpOut -Force }

    $appConfig = Join-Path $projDir 'app.config'

    $parts = New-Object System.Collections.ArrayList
    foreach ($a in @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
                     '/langversion:5', '/warn:4', '/codepage:65001', '/utf8output')) {
        [void]$parts.Add($a)
    }
    if (Test-Path -LiteralPath $appConfig) {
        [void]$parts.Add(('/appconfig:"{0}"' -f $appConfig))
    }
    [void]$parts.Add(('/out:"{0}"' -f $tmpOut))
    foreach ($r in $refs) { [void]$parts.Add(('/r:"{0}"' -f $r)) }
    foreach ($s in $sources) { [void]$parts.Add(('"{0}"' -f $s)) }

    $proc = Start-Process -FilePath $Csc -ArgumentList ($parts -join ' ') -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) { return $proc.ExitCode }

    Copy-Item -LiteralPath $tmpOut -Destination (Join-Path $artifacts $exeName) -Force
    Remove-Item -LiteralPath $tmpOut -Force
    return 0
}

# ---------------------------------------------------------------- 选择并执行

$done = $false
$code = 1
$useDotnet = $false

if ($Backend -eq 'auto' -or $Backend -eq 'dotnet') {
    if (Test-DotnetSdk) {
        $code = Compile-WithDotnet
        if ($code -eq 0) {
            $done = $true
            $useDotnet = $true
        }
        elseif ($Backend -eq 'dotnet') {
            Fail "dotnet build 失败（退出码 $code）"
        }
        else {
            Warn 'dotnet build 失败（多半缺少 net48 引用程序集），改用 csc 后端'
        }
    }
    elseif ($Backend -eq 'dotnet') {
        Fail '找不到 .NET SDK（dotnet --list-sdks 为空）'
    }
    else {
        Warn '未检测到 .NET SDK，尝试 csc 后端'
    }
}

if (-not $done) {
    $csc = Find-RoslynCsc
    if (-not $csc) { $csc = Find-FrameworkCsc }
    if ($csc) {
        $code = Compile-WithCsc -Csc $csc
        $done = $true
    }
    elseif ($Backend -eq 'csc') {
        Fail '找不到任何 csc.exe（Visual Studio 或 .NET Framework 均不可用）'
    }
}

if (-not $done) {
    Fail @'
没有可用的编译后端。请任选其一：
  * 安装 .NET SDK：https://dotnet.microsoft.com/download
  * 安装 Visual Studio / Build Tools（勾选「.NET 桌面开发」）
  * 或者直接使用仓库中已随包分发的 artifacts\PixelAssist.exe
'@
}

if ($code -ne 0) { Fail "编译失败（退出码 $code）" }

# ---------------------------------------------------------------- 归集产物

$finalExe = Join-Path $artifacts $exeName

if ($useDotnet) {
    $built = Join-Path $projDir "bin\$Configuration\$exeName"
    if (-not (Test-Path -LiteralPath $built)) { Fail "编译未产出 EXE：$built" }
    Copy-Item -LiteralPath $built -Destination $finalExe -Force
}

if (-not (Test-Path -LiteralPath $finalExe)) { Fail "编译未产出 EXE：$finalExe" }

$info = Get-Item -LiteralPath $finalExe
Ok ("独立助手已生成：{0} ({1:N0} KB)" -f $info.FullName, ($info.Length / 1KB))

$sha = (Get-FileHash -LiteralPath $finalExe -Algorithm SHA256).Hash
Write-Host ("     SHA256: {0}" -f $sha) -ForegroundColor DarkGray
