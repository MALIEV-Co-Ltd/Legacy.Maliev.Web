#requires -Version 7.2
param(
    [Parameter(Mandatory)][string]$ResultsRoot,
    [ValidateRange(80, 100)][decimal]$MinimumLinePercent = 80
)

$ErrorActionPreference = 'Stop'
$reports = @(Get-ChildItem -LiteralPath $ResultsRoot -Filter coverage.json -Recurse -File)
if ($reports.Count -ne 1) {
    throw "Expected one raw coverage.json report, found $($reports.Count)."
}
$coverage = Get-Content -Raw -LiteralPath $reports[0].FullName | ConvertFrom-Json -AsHashtable
$required = @('Legacy.Maliev.Web.dll', 'Legacy.Maliev.Web.Application.dll', 'Legacy.Maliev.Web.Infrastructure.dll')
$summaries = @()
foreach ($assembly in $required) {
    if (!$coverage.Contains($assembly)) {
        throw "Missing raw production assembly: $assembly"
    }
    [long]$total = 0
    [long]$covered = 0
    foreach ($document in $coverage[$assembly].Values) {
        foreach ($type in $document.Values) {
            foreach ($method in $type.Values) {
                foreach ($hits in $method['Lines'].Values) {
                    $total++
                    if ([long]$hits -gt 0) { $covered++ }
                }
            }
        }
    }
    if ($total -eq 0) { throw "No raw sequence points for $assembly" }
    $percent = [decimal]100 * $covered / $total
    $summaries += [ordered]@{ Assembly = $assembly; Covered = $covered; Total = $total; Percent = $percent }
}
$summaries | ConvertTo-Json | Write-Output
$failures = @($summaries | Where-Object { $_.Percent -lt $MinimumLinePercent })
if ($failures.Count -gt 0) {
    throw "Raw generated-inclusive coverage below $MinimumLinePercent percent: $($failures.Assembly -join ', ')"
}
