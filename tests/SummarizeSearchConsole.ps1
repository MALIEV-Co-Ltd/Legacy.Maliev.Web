[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

$rows = @(Import-Csv -LiteralPath $InputPath)
if ($rows.Count -eq 0) {
    throw 'Search Console export contains no query rows.'
}

$requiredColumns = @('Top queries', 'Clicks', 'Impressions', 'CTR', 'Position')
$availableColumns = @($rows[0].PSObject.Properties.Name)
foreach ($column in $requiredColumns) {
    if ($column -notin $availableColumns) {
        throw "Missing required Search Console column: $column"
    }
}

$cohortPatterns = [ordered]@{
    customManufacturing = 'รับ\s*(?:ผลิตชิ้นงานตามแบบ|ผลิตชิ้นส่วนตามแบบ)'
    scanning              = 'รับ.*(?:สแกน|scan).*(?:3\s*d|3\s*มิติ|สามมิติ)'
    cnc                   = 'รับ.*(?:cnc|ซีเอ็นซี|กลึง)'
    threeDPrinting        = 'รับ.*(?:(?:ปริ้น|พิมพ์|print).*3\s*d|3\s*d.*(?:ปริ้น|พิมพ์|print))'
}

function New-CohortAccumulator {
    return [ordered]@{
        queryCount       = 0
        clicks           = 0
        impressions      = 0
        weightedPosition = 0.0
        queries          = @()
    }
}

$cohorts = [ordered]@{}
foreach ($name in $cohortPatterns.Keys) {
    $cohorts[$name] = New-CohortAccumulator
}
$unmatched = New-CohortAccumulator

foreach ($row in $rows) {
    $query = ([string]$row.'Top queries').Trim()
    $normalizedQuery = ($query.ToLowerInvariant() -replace '\s+', ' ').Trim()
    $clicks = [int]$row.Clicks
    $impressions = [int]$row.Impressions
    $position = [double]::Parse([string]$row.Position, [Globalization.CultureInfo]::InvariantCulture)

    $target = $null
    foreach ($name in $cohortPatterns.Keys) {
        if ($normalizedQuery -match $cohortPatterns[$name]) {
            $target = $cohorts[$name]
            break
        }
    }

    if ($null -eq $target) {
        $target = $unmatched
    }

    $target.queryCount++
    $target.clicks += $clicks
    $target.impressions += $impressions
    $target.weightedPosition += $position * $impressions
    $target.queries += [ordered]@{
        query       = $query
        clicks      = $clicks
        impressions = $impressions
        position    = $position
    }
}

function Complete-Cohort([System.Collections.IDictionary] $cohort) {
    $ctr = if ($cohort.impressions -gt 0) {
        [Math]::Round(($cohort.clicks / $cohort.impressions) * 100, 2)
    }
    else {
        0.0
    }

    $averagePosition = if ($cohort.impressions -gt 0) {
        [Math]::Round($cohort.weightedPosition / $cohort.impressions, 2)
    }
    else {
        0.0
    }

    return [ordered]@{
        queryCount      = $cohort.queryCount
        clicks          = $cohort.clicks
        impressions     = $cohort.impressions
        ctrPercent      = $ctr
        averagePosition = $averagePosition
        queries         = $cohort.queries
    }
}

$completedCohorts = [ordered]@{}
foreach ($name in $cohorts.Keys) {
    $completedCohorts[$name] = Complete-Cohort $cohorts[$name]
}

$report = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    sourcePath     = (Resolve-Path -LiteralPath $InputPath).Path
    sourceRowCount = $rows.Count
    cohorts        = $completedCohorts
    unmatched      = Complete-Cohort $unmatched
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
