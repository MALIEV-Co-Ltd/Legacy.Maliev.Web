[CmdletBinding()]
param([string] $DependencyDirectory = '.dependencies')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dependencyBoundary = [IO.Path]::GetFullPath((Join-Path $root '.dependencies'))
$dependencies = [IO.Path]::GetFullPath((Join-Path $root $DependencyDirectory))
if ($dependencies -ne $dependencyBoundary -and !$dependencies.StartsWith($dependencyBoundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Disposable dependency root must remain inside this workspace .dependencies directory.'
}

function Invoke-Checked([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

function Pinned-Checkout([string] $Repository, [string] $Directory, [string] $Sha) {
    $path = [IO.Path]::GetFullPath((Join-Path $dependencies $Directory))
    if (!$path.StartsWith([IO.Path]::GetFullPath($dependencies) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Pinned test checkout must remain inside this workspace dependency directory.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $path '.git'))) {
        Invoke-Checked git @('-c', 'core.longpaths=true', 'clone', '--no-checkout', "https://github.com/MALIEV-Co-Ltd/$Repository.git", $path)
        # A no-checkout clone intentionally has an empty worktree. Materialize the
        # exact pin before checking cleanliness; only existing dirty clones are preserved.
        Invoke-Checked git @('-C', $path, 'fetch', '--depth=1', 'origin', $Sha)
        Invoke-Checked git @('-c', 'core.longpaths=true', '-C', $path, 'checkout', '--detach', $Sha)
    }
    $status = & git -C $path status --porcelain
    if ($LASTEXITCODE -ne 0 -or $status) { throw "Refusing to modify dirty test dependency $Directory." }
    $head = & git -C $path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $Sha) {
        Invoke-Checked git @('-C', $path, 'fetch', '--depth=1', 'origin', $Sha)
        Invoke-Checked git @('-c', 'core.longpaths=true', '-C', $path, 'checkout', '--detach', $Sha)
    }
    if ((& git -C $path rev-parse HEAD) -ne $Sha) { throw "Unexpected pinned test dependency $Directory." }
    return $path
}

$customer = Pinned-Checkout 'Legacy.Maliev.CustomerService' 'profile-producer' '68df44fcc416ae712d01f32ad5b1a3a969ac6ba2'
$auth = Pinned-Checkout 'Legacy.Maliev.AuthService' 'profile-auth' '82c8d63dd08677a7f8ccd107c05dd6c9badbfd79'
$null = Pinned-Checkout 'Legacy.Maliev.ServiceDefaults' 'profile-producer-runtime/Legacy.Maliev.ServiceDefaults' '086760fa0aae976a799dbcda1960d5c0981248cb'
$null = Pinned-Checkout 'Legacy.Maliev.CompatibilityContracts' 'profile-producer-runtime/Legacy.Maliev.CompatibilityContracts' '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'
$null = Pinned-Checkout 'Legacy.Maliev.ServiceDefaults' 'profile-auth-runtime/Legacy.Maliev.ServiceDefaults' '5c5f9479313710fa576f83d3b396442997a2fcf4'
$null = Pinned-Checkout 'Legacy.Maliev.CompatibilityContracts' 'profile-auth-runtime/Legacy.Maliev.CompatibilityContracts' '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'

# Distinct producer outputs preserve each service's exact-CI dependency runtime.
# These isolated historical producers condition their local project references on
# GITHUB_ACTIONS. Select that dependency graph per build without changing the
# hosted job environment or bypassing any Web validation step.
$customerRuntime = Join-Path $dependencies 'profile-producer-runtime'
$authRuntime = Join-Path $dependencies 'profile-auth-runtime'
Invoke-Checked dotnet @('build', (Join-Path $customer 'Legacy.Maliev.CustomerService.Api/Legacy.Maliev.CustomerService.Api.csproj'), '-c', 'Release', '--nologo', '-p:GITHUB_ACTIONS=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$customerRuntime")
Invoke-Checked dotnet @('build', (Join-Path $auth 'Legacy.Maliev.AuthService.Api/Legacy.Maliev.AuthService.Api.csproj'), '-c', 'Release', '--nologo', '-p:GITHUB_ACTIONS=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$authRuntime")
Invoke-Checked dotnet @('build', (Join-Path $root 'tools/profile-contract-seed/ProfileContractSeed.csproj'), '-c', 'Release', '--nologo', '-p:GITHUB_ACTIONS=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$customerRuntime", "-p:ProfileProducerRoot=$customer")
Invoke-Checked dotnet @('build', (Join-Path $root 'tools/profile-auth-seed/ProfileAuthSeed.csproj'), '-c', 'Release', '--nologo', '-p:GITHUB_ACTIONS=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$authRuntime", "-p:ProfileAuthRoot=$auth")

Invoke-Checked dotnet @('build', (Join-Path $root 'tools/cnc-lease-probe/CncLeaseProbe.csproj'), '-c', 'Release', '--nologo', '-warnaserror')

$outputs = @{
    MALIEV_CNC_LEASE_PROBE_DLL = Join-Path $root 'tools/cnc-lease-probe/bin/Release/net10.0/CncLeaseProbe.dll'
    MALIEV_PROFILE_PRODUCER_DLL = Join-Path $customer 'Legacy.Maliev.CustomerService.Api/bin/Release/net10.0/Legacy.Maliev.CustomerService.Api.dll'
    MALIEV_PROFILE_SEED_DLL = Join-Path $root 'tools/profile-contract-seed/bin/Release/net10.0/Maliev.ProfileContractSeed.dll'
    MALIEV_PROFILE_AUTH_DLL = Join-Path $auth 'Legacy.Maliev.AuthService.Api/bin/Release/net10.0/Legacy.Maliev.AuthService.Api.dll'
    MALIEV_PROFILE_AUTH_SEED_DLL = Join-Path $root 'tools/profile-auth-seed/bin/Release/net10.0/Maliev.ProfileAuthSeed.dll'
}
foreach ($name in $outputs.Keys) {
    if (!(Test-Path -LiteralPath $outputs[$name])) { throw "Missing boundary binary $name." }
    [Environment]::SetEnvironmentVariable($name, $outputs[$name], 'Process')
    if ($env:GITHUB_ENV) { Add-Content -LiteralPath $env:GITHUB_ENV -Value "$name=$($outputs[$name])" }
}
Write-Host 'Pinned disposable boundary binaries are ready. No database or service was started or migrated.'
