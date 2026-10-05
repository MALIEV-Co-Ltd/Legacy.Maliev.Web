#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'Test-WebFxRawCoverage.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('web-fx-gate-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$checks = 0
function Assert-GateOutcome([string]$Name, [hashtable]$Report, [bool]$ShouldPass, [decimal]$Floor = 80) {
    $directory = Join-Path $root $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    if ($null -ne $Report) {
        $Report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $directory 'coverage.json') -Encoding utf8
    }
    $passed = $true
    try { & $gate -ResultsRoot $directory -MinimumLinePercent $Floor | Out-Null }
    catch { $passed = $false }
    if ($passed -ne $ShouldPass) { throw "Coverage gate control failed: $Name" }
    $script:checks++
}
function New-Module([int]$Covered = 4, [int]$Total = 5) {
    $lines = @{}
    for ($line = 1; $line -le $Total; $line++) { $lines["$line"] = [int]($line -le $Covered) }
    return @{ 'generated.g.cs' = @{ 'Generated.Type' = @{ 'Generated.Method()' = @{ Lines = $lines } } } }
}
function New-Report {
    return @{
        'Legacy.Maliev.Web.dll' = (New-Module)
        'Legacy.Maliev.Web.Application.dll' = (New-Module)
        'Legacy.Maliev.Web.Infrastructure.dll' = (New-Module)
    }
}
try {
    Assert-GateOutcome 'exact-floor' (New-Report) $true
    Assert-GateOutcome 'missing-report' $null $false
    $missing = New-Report
    $missing.Remove('Legacy.Maliev.Web.Infrastructure.dll')
    Assert-GateOutcome 'missing-assembly' $missing $false
    $empty = New-Report
    $empty['Legacy.Maliev.Web.dll'] = @{}
    Assert-GateOutcome 'empty-assembly' $empty $false
    $uncovered = New-Report
    $uncovered['Legacy.Maliev.Web.dll'] = New-Module 0 5
    Assert-GateOutcome 'uncovered-assembly' $uncovered $false
    $below = New-Report
    $below['Legacy.Maliev.Web.Infrastructure.dll'] = New-Module 79 100
    Assert-GateOutcome 'below-floor' $below $false
    Assert-GateOutcome 'floor-cannot-be-lowered' (New-Report) $false 79
    $generated = New-Report
    $generated['Legacy.Maliev.Web.dll']['uncovered-generated.g.cs'] = @{ 'Generated.Other' = @{ 'Generated.Other()' = @{ Lines = @{ '1' = 0; '2' = 0 } } } }
    Assert-GateOutcome 'generated-lines-remain-in-denominator' $generated $false
    $copies = Join-Path $root 'identical-copies'
    New-Item -ItemType Directory -Path (Join-Path $copies 'first'), (Join-Path $copies 'second') | Out-Null
    $copySource = Join-Path $root 'exact-floor/coverage.json'
    Copy-Item -LiteralPath $copySource -Destination (Join-Path $copies 'first/coverage.json')
    Copy-Item -LiteralPath $copySource -Destination (Join-Path $copies 'second/coverage.json')
    & $gate -ResultsRoot $copies | Out-Null
    $checks++
    (New-Report) | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $copies 'second/coverage.json') -Encoding utf8
    Add-Content -LiteralPath (Join-Path $copies 'second/coverage.json') -Value ' ' -Encoding utf8
    $conflictRejected = $false
    try { & $gate -ResultsRoot $copies | Out-Null }
    catch { $conflictRejected = $true }
    if (!$conflictRejected) { throw 'Conflicting reports were accepted.' }
    $checks++
    Write-Output "Raw Web coverage gate controls: $checks passed."
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('web-fx-gate-')) {
        throw 'Refused cleanup outside the owned temporary fixture.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
