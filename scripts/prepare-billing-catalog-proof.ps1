[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$boundary = [IO.Path]::GetFullPath((Join-Path $root '.dependencies/billing-catalog'))
function Checked([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}
function Checkout([string] $Repository, [string] $Directory, [string] $Sha) {
    $path = [IO.Path]::GetFullPath((Join-Path $boundary $Directory))
    if (!$path.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Disposable dependency boundary rejected.' }
    if (!(Test-Path -LiteralPath (Join-Path $path '.git'))) {
        Checked git @('-c', 'core.longpaths=true', 'clone', '--no-checkout', "https://github.com/MALIEV-Co-Ltd/$Repository.git", $path)
        Checked git @('-C', $path, 'fetch', '--depth=1', 'origin', $Sha)
        Checked git @('-c', 'core.longpaths=true', '-C', $path, 'checkout', '--detach', $Sha)
    }
    if ((& git -C $path status --porcelain) -or (& git -C $path rev-parse HEAD) -ne $Sha) { throw 'Refusing foreign or dirty pinned dependency.' }
    return $path
}
$catalog = Checkout 'Legacy.Maliev.CatalogService' 'catalog' '3f426723743570a6c20d2c014499445abb0774e1'
$runtime = Join-Path $boundary 'runtime'
$null = Checkout 'Legacy.Maliev.ServiceDefaults' 'runtime/Legacy.Maliev.ServiceDefaults' '7edcd961024868513fd5f373cab3dcb261197f77'
$null = Checkout 'Legacy.Maliev.CompatibilityContracts' 'runtime/Legacy.Maliev.CompatibilityContracts' '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'
Checked dotnet @('build', (Join-Path $catalog 'Legacy.Maliev.CatalogService.Api/Legacy.Maliev.CatalogService.Api.csproj'), '-c', 'Release', '--nologo', '-p:GITHUB_ACTIONS=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$runtime")
$binary = Join-Path $catalog 'Legacy.Maliev.CatalogService.Api/bin/Release/net10.0/Legacy.Maliev.CatalogService.Api.dll'
if (!(Test-Path -LiteralPath $binary)) { throw 'Missing pinned Catalog binary.' }
[Environment]::SetEnvironmentVariable('MALIEV_BILLING_CATALOG_DLL', $binary, 'Process')
if ($env:GITHUB_ENV) { Add-Content -LiteralPath $env:GITHUB_ENV -Value "MALIEV_BILLING_CATALOG_DLL=$binary" }
Write-Host 'Pinned Catalog Program and licensed geography ready; no database or service started.'
