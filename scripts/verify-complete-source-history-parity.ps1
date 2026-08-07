[CmdletBinding()]
param(
    [Parameter()]
    [string] $SourceRoot = 'R:\maliev-web'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedHead = '48e628cf7803264bd0b09bfa7a55b15b47e192dd'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'docs\complete-source-history-parity-through-8049024.md'
$deltaLedgerPath = Join-Path $repositoryRoot 'docs\source-parity-through-8049024.md'

if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot '.git'))) {
    throw "The read-only source repository was not found at '$SourceRoot'."
}

$sourceHead = (& git -C $SourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceHead -ne $expectedHead) {
    throw "Source HEAD is '$sourceHead'; expected '$expectedHead'. Refresh the audit before merging."
}

$sourceWebCommits = @(& git -C $SourceRoot log --reverse --format='%h' $expectedHead -- Maliev.Web Maliev.Web.Tests)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate the source Web history.'
}

$manifestCommits = @(
    Select-String -LiteralPath $manifestPath -Pattern '^\| `([0-9a-f]{7})` \|' |
        ForEach-Object { $_.Matches[0].Groups[1].Value }
)

$sourceDeltaCommits = @(& git -C $SourceRoot log --reverse --format='%h' dcc088f..$expectedHead)
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

[pscustomobject]@{
    SourceHead = $sourceHead
    WebCommitCount = $sourceWebCommits.Count
    PostPublicationCommitCount = $sourceDeltaCommits.Count
    CompleteManifest = 'exact'
    DeltaLedger = 'exact'
    SourceWritePerformedByAudit = $false
} | ConvertTo-Json
