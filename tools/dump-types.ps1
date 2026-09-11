#Requires -Version 5.1
# Dev-only helper: dump types/members from Assembly-CSharp.dll via Mono.Cecil.
#
# Paths are auto-detected on purpose: a hard-coded developer machine path is
# both useless for anyone else and a privacy leak in a public repo.
[CmdletBinding()]
param(
    [string]$GameDir = '',
    [string]$Assembly = '',
    [string]$Cecil = '',
    [string]$Filter = '',
    [string]$Type = '',
    [switch]$Members,
    [switch]$Fields,
    [switch]$Strings,
    [switch]$IL,
    [switch]$AllRefs,
    [string]$Search = '',
    [string]$Method = ''
)

$ErrorActionPreference = 'Stop'

# Resolve the game folder: explicit -GameDir > CPT_GAME_DIR > repo parent >
# Steam default locations on every ready drive.
function Resolve-GameDir {
    param([string]$Explicit)

    $candidates = New-Object System.Collections.ArrayList
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { [void]$candidates.Add($Explicit) }
    if (-not [string]::IsNullOrWhiteSpace($env:CPT_GAME_DIR)) { [void]$candidates.Add($env:CPT_GAME_DIR) }
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        [void]$candidates.Add((Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
    }

    $rel = 'Steam\steamapps\common\Coloring Pixels'
    [void]$candidates.Add(('C:\Program Files (x86)\' + $rel))
    [void]$candidates.Add(('C:\Program Files\' + $rel))
    try {
        foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
            if ($drive.IsReady) { [void]$candidates.Add((Join-Path $drive.Name $rel)) }
        }
    }
    catch {
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

if ([string]::IsNullOrWhiteSpace($Assembly) -or [string]::IsNullOrWhiteSpace($Cecil)) {
    $game = Resolve-GameDir $GameDir
    if (-not $game) {
        throw ('Game folder not found. Pass -GameDir "<path to Coloring Pixels>" ' +
            'or set the CPT_GAME_DIR environment variable.')
    }
    if ([string]::IsNullOrWhiteSpace($Assembly)) {
        $Assembly = Join-Path $game 'ColoringPixels_Data\Managed\Assembly-CSharp.dll'
    }
    if ([string]::IsNullOrWhiteSpace($Cecil)) {
        $Cecil = Join-Path $game 'BepInEx\core\Mono.Cecil.dll'
    }
}

Add-Type -Path $Cecil
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Assembly)
$mod = $asm.MainModule

function Get-AllTypes($types) {
    foreach ($t in $types) {
        $t
        if ($t.NestedTypes.Count -gt 0) { Get-AllTypes $t.NestedTypes }
    }
}

$all = @(Get-AllTypes $mod.Types)

if ($Type) {
    $targets = @($all | Where-Object { $_.FullName -eq $Type -or $_.Name -eq $Type })
    if ($targets.Count -eq 0) { Write-Output "TYPE NOT FOUND: $Type"; return }
    $t = $targets[0]
    Write-Output ("TYPE: " + $t.FullName)
    Write-Output ("BASE: " + $t.BaseType)
    Write-Output ("ATTRS: " + $t.Attributes)
    if ($Members) {
        Write-Output '--- FIELDS ---'
        foreach ($f in $t.Fields) {
            $at = ($f.CustomAttributes | ForEach-Object { $_.AttributeType.Name }) -join ','
            Write-Output ("  {0} {1}  [{2}]" -f $f.FieldType.Name, $f.Name, $at)
        }
        Write-Output '--- PROPERTIES ---'
        foreach ($p in $t.Properties) { Write-Output ("  {0} {1}" -f $p.PropertyType.Name, $p.Name) }
        Write-Output '--- METHODS ---'
        foreach ($m in $t.Methods) {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
            Write-Output ("  {0} {1}({2})" -f $m.ReturnType.Name, $m.Name, $ps)
        }
    }
    if ($Strings) {
        Write-Output '--- STRING LITERALS ---'
        foreach ($m in $t.Methods) {
            if (-not $m.HasBody) { continue }
            foreach ($ins in $m.Body.Instructions) {
                if ($ins.OpCode.Name -eq 'ldstr') { Write-Output ("  [" + $m.Name + "] " + $ins.Operand) }
            }
        }
    }
    if ($IL) {
        Write-Output '--- IL ---'
        foreach ($m in $t.Methods) {
            if (-not $m.HasBody) { continue }
            Write-Output ("  == " + $m.Name)
            foreach ($ins in $m.Body.Instructions) {
                Write-Output ("     {0,-12} {1}" -f $ins.OpCode.Name, $ins.Operand)
            }
        }
    }
    if ($AllRefs) {
        Write-Output '--- TYPE REFS ---'
        foreach ($m in $t.Methods) {
            if (-not $m.HasBody) { continue }
            foreach ($ins in $m.Body.Instructions) {
                if ($ins.OpCode.Name -like 'call*' -or $ins.OpCode.Name -like 'newobj*') {
                    Write-Output ("  [" + $m.Name + "] " + $ins.Operand)
                }
            }
        }
    }
    return
}

if ($Method) {
    $parts = $Method.Split('.')
    $typeName = $parts[0]
    $methodName = if ($parts.Count -gt 1) { $parts[1] } else { '' }
    $targets = @($all | Where-Object { $_.FullName -eq $typeName -or $_.Name -eq $typeName })
    if ($targets.Count -eq 0) { Write-Output "TYPE NOT FOUND: $typeName"; return }
    $tt = $targets[0]
    foreach ($mm in $tt.Methods) {
        if ($methodName -and $mm.Name -ne $methodName) { continue }
        Write-Output ("== " + $tt.FullName + "::" + $mm.Name)
        if (-not $mm.HasBody) { Write-Output '   (no body)'; continue }
        foreach ($ii in $mm.Body.Instructions) {
            Write-Output ("   {0,-14} {1}" -f $ii.OpCode.Name, $ii.Operand)
        }
    }
    return
}

if ($Search) {
    foreach ($t in $all) {
        foreach ($m in $t.Methods) {
            if (-not $m.HasBody) { continue }
            foreach ($ins in $m.Body.Instructions) {
                if ($ins.Operand -eq $null) { continue }
                $op = $ins.Operand.ToString()
                if ($op -match $Search) {
                    Write-Output ("{0} :: {1}  ->  {2} {3}" -f $t.FullName, $m.Name, $ins.OpCode.Name, $op)
                }
            }
        }
    }
    return
}

if ($Fields) {
    foreach ($t in $all) {
        if ($Filter -and $t.FullName -notmatch $Filter) { continue }
        if ($t.Fields.Count -eq 0) { continue }
        Write-Output ("== " + $t.FullName)
        foreach ($f in $t.Fields) { Write-Output ("   field {0} {1}" -f $f.FieldType.Name, $f.Name) }
    }
    return
}

foreach ($t in $all) {
    if ($Filter -and $t.FullName -notmatch $Filter) { continue }
    Write-Output ("{0,-70} {1}" -f $t.FullName, $t.Attributes)
}
Write-Output ("TOTAL TYPES: " + $all.Count)
