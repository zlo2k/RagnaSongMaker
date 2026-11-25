$ErrorActionPreference = 'Stop'

$root = 'F:\work\unity\ragnaGod'
$csc = 'C:\Program Files\dotnet\sdk\8.0.424\Roslyn\bincore\csc.dll'
$ns = 'F:\unity\hub\6000.5.0f1\Editor\Data\NetStandard'
$managed = 'F:\unity\hub\6000.5.0f1\Editor\Data\Managed'
$ueng = Join-Path $managed 'UnityEngine'
$sa = Join-Path $root 'Library\ScriptAssemblies'

$refFiles = @()
$refFiles += Get-ChildItem (Join-Path $ns 'ref\2.1.0') -Filter *.dll -File
$refFiles += Get-ChildItem (Join-Path $ns 'compat\2.1.0\shims\netstandard') -Filter *.dll -File
$refFiles += Get-ChildItem (Join-Path $ns 'compat\2.1.0\shims\netfx') -Filter *.dll -File
$refFiles += Get-ChildItem $ueng -Filter *.dll -File
$refFiles += Get-Item (Join-Path $managed 'Newtonsoft.Json.dll') -ErrorAction SilentlyContinue
$refFiles += Get-Item (Join-Path $managed 'UnityEditor.dll') -ErrorAction SilentlyContinue
$refFiles += Get-ChildItem $sa -Filter *.dll -File | Where-Object { $_.Name -notlike 'RagnarockEditor*' }

$seen = @{}
$refs = New-Object System.Collections.Generic.List[string]
foreach ($f in $refFiles) {
    if ($null -eq $f) { continue }
    $key = $f.Name.ToLowerInvariant()
    if ($seen.ContainsKey($key)) { continue }
    $seen[$key] = $true
    $refs.Add('-r:"' + $f.FullName + '"')
}

function Invoke-Csc([string]$rspPath, [string]$outPath, [string[]]$sources) {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('-nologo')
    $lines.Add('-target:library')
    $lines.Add('-langversion:9')
    $lines.Add('-nostdlib+')
    $lines.Add('-nowarn:0618,0067,0169,0649,0414,0219')
    $lines.Add('-out:"' + $outPath + '"')
    foreach ($r in $refs) { $lines.Add($r) }
    foreach ($r in $script:extraRefs) { $lines.Add($r) }
    foreach ($s in $sources) { $lines.Add('"' + $s + '"') }
    Set-Content -Path $rspPath -Value $lines -Encoding UTF8
    return (& dotnet $csc ('@' + $rspPath) 2>&1)
}

$runtimeDir = Join-Path $root 'Assets\RagnarockEditor\Scripts'
$sources = @(Get-ChildItem $runtimeDir -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\Editor\\' -and $_.FullName -notmatch '\\EditorTools\\' } |
    ForEach-Object { $_.FullName })

Write-Host "runtime sources: $($sources.Count), refs: $($refs.Count)"

$script:extraRefs = @()
$outRuntime = Join-Path $env:TEMP 'ragna_runtime_check.dll'
$output = Invoke-Csc (Join-Path $env:TEMP 'ragna_runtime.rsp') $outRuntime $sources
$runtimeErrors = @($output | Where-Object { $_ -match ': error CS' } | Sort-Object -Unique)
$runtimeErrors | Out-String | Write-Host
Write-Host "RUNTIME errors: $($runtimeErrors.Count)"

# ---- editor pass ----
$script:extraRefs = @('-r:"' + $outRuntime + '"')
$editorSources = @()
$editorSources += Get-ChildItem (Join-Path $root 'Assets\RagnarockEditor\Scripts\EditorTools') -Filter *.cs -File | ForEach-Object { $_.FullName }
$editorSources += Get-ChildItem (Join-Path $root 'Assets\RagnarockEditor\Scripts\Editor') -Filter *.cs -File | ForEach-Object { $_.FullName }
$editorSources += Get-ChildItem (Join-Path $root 'Assets\Editor') -Filter *.cs -File | ForEach-Object { $_.FullName }

$outEditor = Join-Path $env:TEMP 'ragna_editor_check.dll'
$output2 = Invoke-Csc (Join-Path $env:TEMP 'ragna_editor.rsp') $outEditor $editorSources
$editorErrors = @($output2 | Where-Object { $_ -match ': error CS' } | Sort-Object -Unique)
$editorErrors | Out-String | Write-Host
Write-Host "EDITOR errors: $($editorErrors.Count)"
