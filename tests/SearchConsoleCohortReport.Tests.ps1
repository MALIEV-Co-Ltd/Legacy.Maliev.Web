[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptPath = Join-Path $PSScriptRoot 'SummarizeSearchConsole.ps1'
$fixturePath = Join-Path $PSScriptRoot 'fixtures/search-console-queries.csv'
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw 'Missing Search Console cohort reporter: tests/SummarizeSearchConsole.ps1'
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('maliev-search-cohort-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$passed = 0

function Assert-Equal($Expected, $Actual, [string] $Label) {
    if ($Expected -ne $Actual) { throw "$Label expected [$Expected], observed [$Actual]." }
}
function Invoke-Case([string] $Name, [scriptblock] $Body) {
    & $Body
    $script:passed++
    Write-Host "PASS $Name"
}
function Read-Report([string] $InputFile) {
    $output = Join-Path $testRoot ('reports/' + [Guid]::NewGuid().ToString('N') + '.json')
    & $scriptPath -InputPath $InputFile -OutputPath $output
    return Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
}
function Assert-Rejected([string] $InputFile, [string] $ExpectedMessage) {
    $output = Join-Path $testRoot 'rejected.json'
    $observed = $null
    try { & $scriptPath -InputPath $InputFile -OutputPath $output }
    catch { $observed = $_.Exception.Message }
    Assert-Equal $ExpectedMessage $observed 'Rejection category'
    Assert-Equal $false (Test-Path -LiteralPath $output) 'Rejected export has no report'
}

try {
    Invoke-Case 'Source six-row totals, recomputed CTR and weighted position' {
        $before = [DateTime]::UtcNow
        $report = Read-Report $fixturePath
        $after = [DateTime]::UtcNow
        Assert-Equal 6 $report.sourceRowCount 'Source row count'
        Assert-Equal 2 $report.cohorts.threeDPrinting.queryCount 'Printing count'
        Assert-Equal 269 $report.cohorts.threeDPrinting.impressions 'Printing impressions'
        Assert-Equal 0 $report.cohorts.threeDPrinting.clicks 'Printing clicks'
        Assert-Equal 0 $report.cohorts.threeDPrinting.ctrPercent 'Printing CTR'
        # (141 * 23.5 + 128 * 30.7) / 269 = 26.926..., rounded to 26.93.
        Assert-Equal 26.93 $report.cohorts.threeDPrinting.averagePosition 'Weighted printing position'
        Assert-Equal 1 $report.cohorts.cnc.queryCount 'CNC count'
        Assert-Equal 1 $report.cohorts.cnc.clicks 'CNC clicks'
        Assert-Equal 14.29 $report.cohorts.cnc.ctrPercent 'CTR recomputed, not source14.3'
        Assert-Equal 57 $report.cohorts.scanning.impressions 'Scanning impressions'
        Assert-Equal 126 $report.cohorts.customManufacturing.impressions 'Custom impressions'
        Assert-Equal 1 $report.unmatched.queryCount 'Unmatched count'
        Assert-Equal 'รับขึ้นรูปพลาสติก 3d' $report.unmatched.queries[0].query 'Unmatched query'
        Assert-Equal 461 ($report.cohorts.threeDPrinting.impressions + $report.cohorts.cnc.impressions + $report.cohorts.scanning.impressions + $report.cohorts.customManufacturing.impressions + $report.unmatched.impressions) 'All impressions conserved'
        Assert-Equal 2 ($report.cohorts.threeDPrinting.clicks + $report.cohorts.cnc.clicks + $report.cohorts.scanning.clicks + $report.cohorts.customManufacturing.clicks + $report.unmatched.clicks) 'All clicks conserved'
        Assert-Equal (Resolve-Path -LiteralPath $fixturePath).Path $report.sourcePath 'Source path'
        $generated = if ($report.generatedAtUtc -is [DateTime]) { $report.generatedAtUtc }
        else { [DateTime]::Parse($report.generatedAtUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind) }
        if ($generated.Kind -ne [DateTimeKind]::Utc -or $generated -lt $before -or $generated -gt $after) { throw 'Report timestamp is not bounded UTC generation time.' }
    }
    foreach ($missing in @('Top queries', 'Clicks', 'Impressions', 'CTR', 'Position')) {
        Invoke-Case "Missing required column $missing" {
            $row = [ordered]@{ 'Top queries' = 'รับ cnc'; Clicks = '1'; Impressions = '7'; CTR = '14.3%'; Position = '11.4' }
            $row.Remove($missing)
            $inputFile = Join-Path $testRoot 'missing.csv'
            [pscustomobject]$row | Export-Csv -LiteralPath $inputFile -NoTypeInformation -Encoding utf8
            Assert-Rejected $inputFile "Missing required Search Console column: $missing"
        }
    }
    Invoke-Case 'Empty export rejected' {
        $inputFile = Join-Path $testRoot 'empty.csv'
        'Top queries,Clicks,Impressions,CTR,Position' | Set-Content -LiteralPath $inputFile -Encoding utf8
        Assert-Rejected $inputFile 'Search Console export contains no query rows.'
    }
    Invoke-Case 'Zero impressions produce zero CTR and position' {
        $inputFile = Join-Path $testRoot 'zero.csv'
        @('Top queries,Clicks,Impressions,CTR,Position', 'รับพิมพ์ 3d,0,0,99%,35.5') | Set-Content -LiteralPath $inputFile -Encoding utf8
        $report = Read-Report $inputFile
        Assert-Equal 1 $report.cohorts.threeDPrinting.queryCount 'Zero-impression count'
        Assert-Equal 0 $report.cohorts.threeDPrinting.ctrPercent 'Zero-impression CTR'
        Assert-Equal 0 $report.cohorts.threeDPrinting.averagePosition 'Zero-impression position'
        Assert-Equal 0 $report.unmatched.averagePosition 'Empty unmatched position'
    }
    Invoke-Case 'Dot-decimal positions are invariant under de-DE culture' {
        $previous = [Globalization.CultureInfo]::CurrentCulture
        try {
            [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('de-DE')
            $report = Read-Report $fixturePath
            Assert-Equal 26.93 $report.cohorts.threeDPrinting.averagePosition 'Invariant weighted position'
            Assert-Equal 11.4 $report.cohorts.cnc.averagePosition 'Invariant CNC position'
        }
        finally { [Globalization.CultureInfo]::CurrentCulture = $previous }
    }
    Invoke-Case 'Overlapping queries belong only to first ordered cohort' {
        $inputFile = Join-Path $testRoot 'overlap.csv'
        @('Top queries,Clicks,Impressions,CTR,Position',
          'รับผลิตชิ้นงานตามแบบ รับสแกน 3d รับ cnc รับพิมพ์ 3d,1,2,0%,10',
          'รับสแกน 3d รับ cnc รับพิมพ์ 3d,2,5,0%,20',
          'รับ CNC รับพิมพ์ 3d,3,7,0%,30',
          'รับพิมพ์ 3d,4,11,0%,40') | Set-Content -LiteralPath $inputFile -Encoding utf8
        $report = Read-Report $inputFile
        Assert-Equal 4 $report.sourceRowCount 'Overlap source rows'
        Assert-Equal 2 $report.cohorts.customManufacturing.impressions 'Custom precedes scanning'
        Assert-Equal 5 $report.cohorts.scanning.impressions 'Scanning precedes CNC'
        Assert-Equal 7 $report.cohorts.cnc.impressions 'CNC precedes printing'
        Assert-Equal 11 $report.cohorts.threeDPrinting.impressions 'Printing last'
        Assert-Equal 0 $report.unmatched.queryCount 'No duplicate unmatched rows'
        foreach ($name in @('customManufacturing', 'scanning', 'cnc', 'threeDPrinting')) { Assert-Equal 1 $report.cohorts.$name.queryCount "$name exclusive count" }
    }
    Write-Host "Search Console cohort report: $passed passed, 0 failed, 0 skipped."
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notmatch '^maliev-search-cohort-[0-9a-f]{32}$') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
