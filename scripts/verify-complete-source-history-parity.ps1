[CmdletBinding()]
param(
    [Parameter()]
    [string] $SourceRoot = 'R:\maliev-web'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$historicalHead = '48e628cf7803264bd0b09bfa7a55b15b47e192dd'
$expectedHead = '7b4b2af697207d36a6e7b7784dddefa150193e97'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'docs\complete-source-history-parity-through-8049024.md'
$deltaLedgerPath = Join-Path $repositoryRoot 'docs\source-parity-through-8049024.md'
$dailyManifestPath = Join-Path $repositoryRoot 'docs\source-parity-delta-through-7b4b2af.json'

if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot '.git'))) {
    throw "The read-only source repository was not found at '$SourceRoot'."
}

$sourceHead = (& git -C $SourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceHead -ne $expectedHead) {
    throw "Source HEAD is '$sourceHead'; expected '$expectedHead'. Refresh the audit before merging."
}

$sourceWebCommits = @(& git -C $SourceRoot log --reverse --format='%h' $historicalHead -- Maliev.Web Maliev.Web.Tests)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate the source Web history.'
}

$manifestCommits = @(
    Select-String -LiteralPath $manifestPath -Pattern '^\| `([0-9a-f]{7})` \|' |
        ForEach-Object { $_.Matches[0].Groups[1].Value }
)

$sourceDeltaCommits = @(& git -C $SourceRoot log --reverse --format='%h' dcc088f..$historicalHead)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate the post-publication source history.'
}

$deltaLedgerCommits = @(
    Select-String -LiteralPath $deltaLedgerPath -Pattern '^\| `([0-9a-f]{7})` \|' |
        ForEach-Object { $_.Matches[0].Groups[1].Value }
)

function Assert-ExactSequence {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string[]] $Expected,
        [Parameter(Mandatory)] [string[]] $Actual
    )

    if ($Expected.Count -ne $Actual.Count) {
        throw "$Name count mismatch: source=$($Expected.Count), manifest=$($Actual.Count)."
    }

    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ($Expected[$index] -ne $Actual[$index]) {
            throw "$Name mismatch at index ${index}: source=$($Expected[$index]), manifest=$($Actual[$index])."
        }
    }
}

Assert-ExactSequence -Name 'Complete Web history' -Expected $sourceWebCommits -Actual $manifestCommits
Assert-ExactSequence -Name 'Post-publication history' -Expected $sourceDeltaCommits -Actual $deltaLedgerCommits

$dailySourceCommits = @(& git -C $SourceRoot rev-list --reverse "$historicalHead..$expectedHead")
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate the daily source-parity history.'
}

$dailyManifest = Get-Content -LiteralPath $dailyManifestPath -Raw | ConvertFrom-Json
$dailyEntries = @($dailyManifest.entries)
$dailyManifestCommits = @($dailyEntries | ForEach-Object { $_.sourceCommit })
Assert-ExactSequence -Name 'Daily source-parity history' -Expected $dailySourceCommits -Actual $dailyManifestCommits

if ($dailyManifest.schemaVersion -ne '1.0' -or
    $dailyManifest.historicalCheckpoint -ne $historicalHead -or
    $dailyManifest.sourceHead -ne $expectedHead -or
    [int] $dailyManifest.commitCount -ne $dailySourceCommits.Count) {
    throw 'The daily source-parity manifest boundary is invalid.'
}

for ($index = 0; $index -lt $dailyEntries.Count; $index++) {
    $entry = $dailyEntries[$index]
    if ([int] $entry.sequence -ne ($index + 1)) {
        throw "Daily source-parity sequence is not contiguous at index $index."
    }

    if ([string]::IsNullOrWhiteSpace([string] $entry.disposition) -or
        [string] $entry.disposition -match '(?i)gap|pending|unclassified' -or
        @($entry.owners).Count -eq 0 -or
        @($entry.owners | Where-Object { [string] $_ -match '^unmapped:' }).Count -gt 0 -or
        [string]::IsNullOrWhiteSpace([string] $entry.evidence)) {
        throw "Daily source-parity entry '$($entry.sourceCommit)' is not fully classified."
    }
}

$dailySequence = ($dailyManifestCommits -join "`n") + "`n"
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $dailySequenceSha256 = ([BitConverter]::ToString(
            $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($dailySequence))) -replace '-', '').ToLowerInvariant()
}
finally {
    $sha256.Dispose()
}
if ($dailySequenceSha256 -ne $dailyManifest.sequenceSha256) {
    throw "Daily source-parity sequence digest mismatch: actual=$dailySequenceSha256, manifest=$($dailyManifest.sequenceSha256)."
}

[pscustomobject]@{
    SourceHead = $sourceHead
    WebCommitCount = $sourceWebCommits.Count
    PostPublicationCommitCount = $sourceDeltaCommits.Count
    DailyParityCommitCount = $dailySourceCommits.Count
    DailyParitySequenceSha256 = $dailySequenceSha256
    CompleteManifest = 'exact'
    DeltaLedger = 'exact'
    DailyManifest = 'exact'
    SourceWritePerformedByAudit = $false
} | ConvertTo-Json
