#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$HttpUri = 'http://www.maliev.com/',

    [string]$BaseUri = 'https://www.maliev.com',

    [string]$OutputPath = '',

    [scriptblock]$Request
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ProductionSeoVerifier.psm1') -Force

$usesDefaultRequest = $null -eq $Request
if ($null -eq $Request) {
    $Request = {
        param(
            [string]$Uri,
            [bool]$FollowRedirects
        )

        return Invoke-ProductionSeoHttpRequest `
            -Uri $Uri `
            -FollowRedirects $FollowRedirects
    }
}

$LocalizedRequest = if ($usesDefaultRequest) {
    {
        param(
            [string]$Uri,
            [bool]$FollowRedirects,
            [hashtable]$Headers
        )

        return Invoke-ProductionSeoHttpRequest `
            -Uri $Uri `
            -FollowRedirects $FollowRedirects `
            -Headers $Headers
    }
}
else {
    $null
}

$result = Invoke-ProductionSeoVerification `
    -HttpUri $HttpUri `
    -BaseUri $BaseUri `
    -ExpectedPaths @(Get-ExpectedPublicSearchPaths) `
    -Request $Request `
    -LocalizedRequest $LocalizedRequest

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $parent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
}

Write-Output $result

if (-not $result.Passed) {
    $failedChecks = @($result.Checks | Where-Object { -not $_.Passed } | ForEach-Object Name)
    throw "Production SEO verification failed: $($failedChecks -join ', ')"
}
