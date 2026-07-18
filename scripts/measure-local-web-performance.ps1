[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [uri] $Uri,

    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int] $TargetProcessId,

    [ValidateRange(1, 10000)]
    [int] $RequestCount = 500,

    [ValidateRange(0, 1000)]
    [int] $WarmupCount = 100,

    [string] $AssetDirectory = (Join-Path $PSScriptRoot '..\Legacy.Maliev.Web\wwwroot\dist')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Percentile {
    param(
        [Parameter(Mandatory)]
        [double[]] $SortedValues,

        [Parameter(Mandatory)]
        [ValidateRange(0.0, 1.0)]
        [double] $Percentile
    )

    if ($SortedValues.Count -eq 0) {
        return 0.0
    }

    $index = [Math]::Max(0, [Math]::Ceiling($SortedValues.Count * $Percentile) - 1)
    return $SortedValues[$index]
}

$process = Get-Process -Id $TargetProcessId -ErrorAction Stop
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(15)

try {
    foreach ($iteration in 1..$WarmupCount) {
        if ($WarmupCount -eq 0) { break }
        $response = $client.GetAsync($Uri).GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) {
                throw "Warm-up request returned HTTP $([int]$response.StatusCode)."
            }
        }
        finally {
            $response.Dispose()
        }
    }

    $process.Refresh()
    $cpuBefore = $process.TotalProcessorTime.TotalSeconds
    $privateMemoryBefore = $process.PrivateMemorySize64
    $workingSetBefore = $process.WorkingSet64
    $latencies = [System.Collections.Generic.List[double]]::new($RequestCount)
    $runTimer = [System.Diagnostics.Stopwatch]::StartNew()

    foreach ($iteration in 1..$RequestCount) {
        $requestTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $response = $client.GetAsync($Uri).GetAwaiter().GetResult()
        $requestTimer.Stop()
        try {
            if (-not $response.IsSuccessStatusCode) {
                throw "Measured request $iteration returned HTTP $([int]$response.StatusCode)."
            }
            $null = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        }
        finally {
            $response.Dispose()
        }
        $latencies.Add($requestTimer.Elapsed.TotalMilliseconds)
    }

    $runTimer.Stop()
    $process.Refresh()
    $latencyArray = $latencies.ToArray()
    [Array]::Sort($latencyArray)
    $assetBytes = if (Test-Path -LiteralPath $AssetDirectory) {
        (Get-ChildItem -LiteralPath $AssetDirectory -File -Recurse |
            Where-Object Extension -In '.css', '.js' |
            Measure-Object -Property Length -Sum).Sum
    }
    else {
        0
    }

    [ordered]@{
        TimestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Uri = $Uri.AbsoluteUri
        TargetProcessId = $TargetProcessId
        RequestCount = $RequestCount
        WarmupCount = $WarmupCount
        P50LatencyMilliseconds = [Math]::Round((Get-Percentile $latencyArray 0.50), 3)
        P95LatencyMilliseconds = [Math]::Round((Get-Percentile $latencyArray 0.95), 3)
        MaximumLatencyMilliseconds = [Math]::Round($latencyArray[-1], 3)
        ThroughputRequestsPerSecond = [Math]::Round($RequestCount / $runTimer.Elapsed.TotalSeconds, 2)
        CpuSeconds = [Math]::Round($process.TotalProcessorTime.TotalSeconds - $cpuBefore, 3)
        PrivateMemoryMegabytes = [ordered]@{
            Before = [Math]::Round($privateMemoryBefore / 1MB, 2)
            After = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        }
        WorkingSetMegabytes = [ordered]@{
            Before = [Math]::Round($workingSetBefore / 1MB, 2)
            After = [Math]::Round($process.WorkingSet64 / 1MB, 2)
        }
        AssetBytes = [long]$assetBytes
    } | ConvertTo-Json -Depth 4
}
finally {
    $client.Dispose()
}
