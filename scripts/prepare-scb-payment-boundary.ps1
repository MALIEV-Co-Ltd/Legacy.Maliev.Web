[CmdletBinding()]
param(
    [string] $DependencyDirectory = '.dependencies/scb-payment-boundary',
    [switch] $VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$boundary = [IO.Path]::GetFullPath((Join-Path $root '.dependencies'))
$dependencies = [IO.Path]::GetFullPath((Join-Path $root $DependencyDirectory))
if (!$dependencies.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'SCB test dependencies must remain inside this workspace .dependencies directory.'
}

function Invoke-Checked([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed while preparing the disposable SCB boundary." }
}

function Pinned-Checkout([string] $Repository, [string] $RelativePath, [string] $Sha) {
    $path = [IO.Path]::GetFullPath((Join-Path $dependencies $RelativePath))
    if (!$path.StartsWith($dependencies + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'SCB test checkout escaped its owned dependency directory.'
    }
    $origin = "https://github.com/MALIEV-Co-Ltd/$Repository.git"
    if (!(Test-Path -LiteralPath (Join-Path $path '.git'))) {
        if ($VerifyOnly) { throw 'Exact-pinned SCB producer dependencies are missing; preparation is required.' }
        if (Test-Path -LiteralPath $path) { throw 'Refusing to overwrite an existing non-repository dependency path.' }
        Invoke-Checked git @('-c', 'core.longpaths=true', 'clone', '--no-checkout', $origin, $path)
        Invoke-Checked git @('-C', $path, 'config', 'credential.helper', '')
        Invoke-Checked git @('-C', $path, 'fetch', '--depth=1', 'origin', $Sha)
        Invoke-Checked git @('-c', 'core.longpaths=true', '-C', $path, 'checkout', '--detach', $Sha)
    }
    $status = & git -c core.longpaths=true -C $path status --porcelain
    if ($LASTEXITCODE -ne 0 -or $status) { throw 'Refusing dirty SCB test dependencies.' }
    $remote = & git -C $path remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $remote -ne $origin) { throw 'SCB test dependency origin does not match its immutable repository.' }
    $head = & git -C $path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $Sha) { throw 'SCB test dependency does not match its immutable commit.' }
    return $path
}

$producer = Pinned-Checkout 'Legacy.Maliev.AccountingService' 'producer' 'ae0826156b06c34476e95de8c53dfccfcf5a5972'
$null = Pinned-Checkout 'Legacy.Maliev.ServiceDefaults' 'runtime/Legacy.Maliev.ServiceDefaults' '8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3'
$null = Pinned-Checkout 'Legacy.Maliev.CompatibilityContracts' 'runtime/Legacy.Maliev.CompatibilityContracts' '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'
$runtime = Join-Path $dependencies 'runtime'
if (!$VerifyOnly) {
    Invoke-Checked dotnet @('build', (Join-Path $producer 'Legacy.Maliev.AccountingService.Api/Legacy.Maliev.AccountingService.Api.csproj'),
        '-c', 'Release', '--nologo', '-p:GITHUB_ACTIONS=false', '-p:TreatWarningsAsErrors=true', '-p:UseSharedCompilation=false',
        '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$runtime")
}
$binary = Join-Path $producer 'Legacy.Maliev.AccountingService.Api/bin/Release/net10.0/Legacy.Maliev.AccountingService.Api.dll'
if (!(Test-Path -LiteralPath $binary -PathType Leaf)) { throw 'The exact-pinned SCB Accounting binary is missing; no tests may skip this prerequisite.' }
[Environment]::SetEnvironmentVariable('MALIEV_SCB_ACCOUNTING_DLL', $binary, 'Process')
if ($env:GITHUB_ENV) { Add-Content -LiteralPath $env:GITHUB_ENV -Value "MALIEV_SCB_ACCOUNTING_DLL=$binary" }
Write-Host 'Exact-pinned SCB boundary binary is ready. No service, migration or database was started.'
