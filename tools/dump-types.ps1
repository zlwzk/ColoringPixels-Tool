#Requires -Version 5.1
# Dev-only helper: dump types/members from Assembly-CSharp.dll via Mono.Cecil.
[CmdletBinding()]
param(
    [string]$Assembly = 'D:\Steam\steamapps\common\Coloring Pixels\ColoringPixels_Data\Managed\Assembly-CSharp.dll',
    [string]$Cecil = 'D:\Steam\steamapps\common\Coloring Pixels\BepInEx\core\Mono.Cecil.dll',
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
