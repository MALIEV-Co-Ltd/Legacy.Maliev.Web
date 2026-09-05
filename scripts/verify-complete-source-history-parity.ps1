[CmdletBinding()]
param(
    [Parameter()]
    [string] $SourceRoot = 'R:\maliev-web',
    [Parameter()]
    [switch] $ManifestOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$historicalHead = '48e628cf7803264bd0b09bfa7a55b15b47e192dd'
$expectedHead = '4486f0e964e508e5eb7b43a59eeaec46cc052c67'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'docs\complete-source-history-parity-through-8049024.md'
$deltaLedgerPath = Join-Path $repositoryRoot 'docs\source-parity-through-8049024.md'
$dailyManifestPath = Join-Path $repositoryRoot 'docs\source-parity-delta-through-4486f0e.json'

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
            throw "$Name mismatch at index $($index): source=$($Expected[$index]), manifest=$($Actual[$index])."
        }
    }
}

function Get-Sha256Text {
    param([Parameter(Mandatory)] [string] $Value)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
                $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value))) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Add-CanonicalValue {
    param(
        [Parameter(Mandatory)] [Text.StringBuilder] $Builder,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Value
    )
    [void] $Builder.Append($Value.Length).Append(':').Append($Value).Append("`n")
}

function Add-CanonicalArray {
    param(
        [Parameter(Mandatory)] [Text.StringBuilder] $Builder,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Values
    )
    Add-CanonicalValue -Builder $Builder -Value ([string] $Values.Count)
    foreach ($value in $Values) {
        Add-CanonicalValue -Builder $Builder -Value ([string] $value)
    }
}

$dailyManifest = Get-Content -LiteralPath $dailyManifestPath -Raw | ConvertFrom-Json
$dailyEntries = @($dailyManifest.entries)
$dailyManifestCommits = @($dailyEntries | ForEach-Object { [string] $_.sourceCommit })
$allowedOwners = @($dailyManifest.allowedOwners)
$allowedClassifications = @($dailyManifest.allowedClassifications)
$semanticStream = New-Object Text.StringBuilder
Add-CanonicalValue -Builder $semanticStream -Value 'source-parity-semantic-stream-v1'
Add-CanonicalValue -Builder $semanticStream -Value ([string] $dailyManifest.schemaVersion)
Add-CanonicalValue -Builder $semanticStream -Value ([string] $dailyManifest.historicalCheckpoint)
Add-CanonicalValue -Builder $semanticStream -Value ([string] $dailyManifest.sourceHead)
Add-CanonicalValue -Builder $semanticStream -Value ([string] [int] $dailyManifest.commitCount)
Add-CanonicalArray -Builder $semanticStream -Values $allowedOwners
Add-CanonicalArray -Builder $semanticStream -Values $allowedClassifications

if ($dailyManifest.schemaVersion -ne '1.1' -or
    $dailyManifest.historicalCheckpoint -ne $historicalHead -or
    $dailyManifest.sourceHead -ne $expectedHead -or
    [int] $dailyManifest.commitCount -ne 159 -or
    $dailyEntries.Count -ne 159) {
    throw 'The daily source-parity manifest boundary is invalid.'
}

for ($index = 0; $index -lt $dailyEntries.Count; $index++) {
    $entry = $dailyEntries[$index]
    $classification = [string] $entry.classification
    $owners = @($entry.owners)
    $targetEvidence = @($entry.targetEvidence)
    $retirements = @($entry.retirements)
    $exclusions = @($entry.exclusions)

    if ([int] $entry.sequence -ne ($index + 1)) {
        throw "Daily source-parity sequence is not contiguous at index $index."
    }
    if ([string] $entry.sourceCommit -notmatch '^[0-9a-f]{40}$' -or
        $classification -notin $allowedClassifications -or
        $classification -match '(?i)gap|pending|unclassified' -or
        [string]::IsNullOrWhiteSpace([string] $entry.validationEvidence) -or
        [string]::IsNullOrWhiteSpace([string] $entry.classificationRationale)) {
        throw "Daily source-parity entry '$($entry.sourceCommit)' is not fully classified."
    }
    foreach ($owner in $owners) {
        if ([string] $owner -notin $allowedOwners -or [string] $owner -notmatch '^Legacy\.Maliev\.') {
            throw "Daily source-parity entry '$($entry.sourceCommit)' has an invalid owner '$owner'."
        }
    }
    if ($targetEvidence.Count -ne $owners.Count) {
        throw "Daily source-parity entry '$($entry.sourceCommit)' does not bind every owner to target evidence."
    }
    foreach ($target in $targetEvidence) {
        if ([string] $target.repository -notin $owners -or [string] $target.commit -notmatch '^[0-9a-f]{40}$') {
            throw "Daily source-parity entry '$($entry.sourceCommit)' has invalid target evidence."
        }
    }
    if ($owners.Count -eq 0 -and $retirements.Count -eq 0 -and $exclusions.Count -eq 0) {
        throw "Daily source-parity entry '$($entry.sourceCommit)' has no owner, retirement, or exclusion."
    }

    Add-CanonicalValue -Builder $semanticStream -Value ([string] [int] $entry.sequence)
    Add-CanonicalValue -Builder $semanticStream -Value ([string] $entry.sourceCommit)
    Add-CanonicalValue -Builder $semanticStream -Value ([string] $entry.subject)
    Add-CanonicalValue -Builder $semanticStream -Value $classification
    Add-CanonicalArray -Builder $semanticStream -Values $owners
    Add-CanonicalArray -Builder $semanticStream -Values $retirements
    Add-CanonicalArray -Builder $semanticStream -Values $exclusions
    Add-CanonicalArray -Builder $semanticStream -Values @($entry.sourceAreas)
    Add-CanonicalValue -Builder $semanticStream -Value ([string] $targetEvidence.Count)
    foreach ($target in $targetEvidence) {
        Add-CanonicalValue -Builder $semanticStream -Value ([string] $target.repository)
        Add-CanonicalValue -Builder $semanticStream -Value ([string] $target.commit)
    }
    Add-CanonicalValue -Builder $semanticStream -Value ([string] $entry.validationEvidence)
    Add-CanonicalValue -Builder $semanticStream -Value ([string] $entry.classificationRationale)
}

$dailySequence = ($dailyManifestCommits -join "`n") + "`n"
$dailySequenceSha256 = Get-Sha256Text -Value $dailySequence
if ($dailySequenceSha256 -ne $dailyManifest.sequenceSha256) {
    throw "Daily source-parity sequence digest mismatch: actual=$dailySequenceSha256, manifest=$($dailyManifest.sequenceSha256)."
}
$semanticSha256 = Get-Sha256Text -Value $semanticStream.ToString()
if ($semanticSha256 -ne $dailyManifest.semanticSha256) {
    throw "Daily source-parity semantic digest mismatch: actual=$semanticSha256, manifest=$($dailyManifest.semanticSha256)."
}

if ($ManifestOnly) {
    [pscustomobject]@{
        SourceHead = $expectedHead
        DailyParityCommitCount = $dailyEntries.Count
        DailyParitySequenceSha256 = $dailySequenceSha256
        DailyParitySemanticSha256 = $semanticSha256
        DailyManifest = 'exact'
        SourceComparison = 'not-run-private-source'
        SourceWritePerformedByAudit = $false
    } | ConvertTo-Json
    return
}

if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot '.git'))) {
    throw "The read-only source repository was not found at '$SourceRoot'."
}
$sourceHead = (& git -C $SourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceHead -ne $expectedHead) {
    throw "Source HEAD is '$sourceHead'; expected '$expectedHead'. Refresh the audit before merging."
}
$sourceWebCommits = @(& git -C $SourceRoot log --reverse --format='%h' $historicalHead -- Maliev.Web Maliev.Web.Tests)
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate the source Web history.' }
$manifestCommits = @(
    Select-String -LiteralPath $manifestPath -Pattern '^\| `([0-9a-f]{7})` \|' |
        ForEach-Object { $_.Matches[0].Groups[1].Value }
)
$sourceDeltaCommits = @(& git -C $SourceRoot log --reverse --format='%h' dcc088f..$historicalHead)
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate the post-publication source history.' }
$deltaLedgerCommits = @(
    Select-String -LiteralPath $deltaLedgerPath -Pattern '^\| `([0-9a-f]{7})` \|' |
        ForEach-Object { $_.Matches[0].Groups[1].Value }
)
$dailySourceCommits = @(& git -C $SourceRoot rev-list --reverse "$historicalHead..$expectedHead")
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate the daily source-parity history.' }

Assert-ExactSequence -Name 'Complete Web history' -Expected $sourceWebCommits -Actual $manifestCommits
Assert-ExactSequence -Name 'Post-publication history' -Expected $sourceDeltaCommits -Actual $deltaLedgerCommits
Assert-ExactSequence -Name 'Daily source-parity history' -Expected $dailySourceCommits -Actual $dailyManifestCommits

[pscustomobject]@{
    SourceHead = $sourceHead
    WebCommitCount = $sourceWebCommits.Count
    PostPublicationCommitCount = $sourceDeltaCommits.Count
    DailyParityCommitCount = $dailySourceCommits.Count
    DailyParitySequenceSha256 = $dailySequenceSha256
    DailyParitySemanticSha256 = $semanticSha256
    CompleteManifest = 'exact'
    DeltaLedger = 'exact'
    DailyManifest = 'exact'
    SourceComparison = 'exact-read-only'
    SourceWritePerformedByAudit = $false
} | ConvertTo-Json
