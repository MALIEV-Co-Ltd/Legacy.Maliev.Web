[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$boundary = [IO.Path]::GetFullPath((Join-Path $root '.dependencies/member-auth-indexing'))
function Checked([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}
function Checkout([string] $Repository, [string] $Directory, [string] $Sha) {
    $path = [IO.Path]::GetFullPath((Join-Path $boundary $Directory))
    if (!$path.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Private dependency boundary rejected.' }
    if (!(Test-Path -LiteralPath (Join-Path $path '.git'))) {
        Checked git @('-c', 'core.longpaths=true', 'clone', '--no-checkout', "https://github.com/MALIEV-Co-Ltd/$Repository.git", $path)
        Checked git @('-C', $path, 'fetch', '--depth=1', 'origin', $Sha)
        Checked git @('-c', 'core.longpaths=true', '-C', $path, 'checkout', '--detach', $Sha)
    }
    if ((& git -C $path status --porcelain) -or (& git -C $path rev-parse HEAD) -ne $Sha) { throw 'Refusing foreign or dirty private dependency.' }
    return $path
}
$auth = Checkout 'Legacy.Maliev.AuthService' 'auth' '51afbbd6e2829382a3431338abedccf339de33b1'
$runtime = Join-Path $boundary 'runtime'
$null = Checkout 'Legacy.Maliev.ServiceDefaults' 'runtime/Legacy.Maliev.ServiceDefaults' '5c5f9479313710fa576f83d3b396442997a2fcf4'
$null = Checkout 'Legacy.Maliev.CompatibilityContracts' 'runtime/Legacy.Maliev.CompatibilityContracts' '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'
$seed = Join-Path $boundary 'seed-51afbbd-v3'
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup><ProjectReference Include="../auth/Legacy.Maliev.AuthService.Infrastructure/Legacy.Maliev.AuthService.Infrastructure.csproj" /></ItemGroup>
  <ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" /></ItemGroup>
</Project>
'@
$program = @'
using Legacy.Maliev.AuthService.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

if (Environment.GetEnvironmentVariable("MALIEV_MEMBER_ENV_PROBE") == "1")
{
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
        "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)",
        "MALIEV_MEMBER_ENV_PROBE", "LEGACY_DEPLOY_ENABLED"
    };
    foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
        if (!allowed.Contains((string)variable.Key)) throw new InvalidOperationException("Child environment isolation rejected.");
    if (Environment.GetEnvironmentVariable("LEGACY_DEPLOY_ENABLED") != "false") throw new InvalidOperationException("Child deployment isolation rejected.");
    return;
}

static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing disposable fixture authority.");
var run = Required("MALIEV_MEMBER_RUN");
if (!Guid.TryParseExact(run, "N", out _) || Required("MALIEV_MEMBER_CONTAINER").Length < 12) throw new InvalidOperationException("Invalid disposable fixture identity.");
var host = Required("MALIEV_MEMBER_HOST");
if (host != "127.0.0.1" && host != "localhost" && host != "::1") throw new InvalidOperationException("Nonloopback target rejected.");
if (!int.TryParse(Required("MALIEV_MEMBER_PORT"), out var port) || port <= 1024 || port > 65535) throw new InvalidOperationException("Mapped port rejected.");
string Connection(string kind)
{
    var value = Required("MALIEV_MEMBER_" + kind.ToUpperInvariant() + "_CONNECTION");
    var parsed = new NpgsqlConnectionStringBuilder(value);
    if (parsed.Host != host || parsed.Port != port || parsed.Database != $"member_{kind}_{run}" || parsed.Pooling || parsed.Multiplexing || parsed.Username != "postgres") throw new InvalidOperationException("Disposable database authority rejected.");
    return value;
}
// Validate ALL endpoints before any owner migration can execute.
var customerConnection = Connection("customer");
var employeeConnection = Connection("employee");
var sessionsConnection = Connection("sessions");
await using var customer = new CustomerIdentityDbContext(new DbContextOptionsBuilder<CustomerIdentityDbContext>().UseNpgsql(customerConnection).Options);
await using var employee = new EmployeeIdentityDbContext(new DbContextOptionsBuilder<EmployeeIdentityDbContext>().UseNpgsql(employeeConnection).Options);
await using var sessions = new RefreshSessionDbContext(new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(sessionsConnection).Options);
await customer.Database.MigrateAsync();
await employee.Database.MigrateAsync();
await sessions.Database.MigrateAsync();
var row = new LegacyIdentityRow
{
    Id = "member-crawl-customer", DatabaseID = 1,
    UserName = "member-crawl@example.test", NormalizedUserName = "MEMBER-CRAWL@EXAMPLE.TEST",
    Email = "member-crawl@example.test", NormalizedEmail = "MEMBER-CRAWL@EXAMPLE.TEST", EmailConfirmed = true,
    SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"),
};
row.PasswordHash = new PasswordHasher<LegacyIdentityRow>().HashPassword(row, Required("MALIEV_MEMBER_PASSWORD"));
customer.Users.Add(row);
await customer.SaveChangesAsync();
'@
New-Item -ItemType Directory -Path $seed -Force | Out-Null
foreach ($file in @(@('Seed.csproj', $project), @('Program.cs', $program))) {
    $path = [IO.Path]::GetFullPath((Join-Path $seed $file[0]))
    if (!$path.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Generated helper boundary rejected.' }
    if (Test-Path -LiteralPath $path) {
        if ([IO.File]::ReadAllText($path) -ne $file[1]) { throw 'Refusing to overwrite different generated helper.' }
    } else { [IO.File]::WriteAllText($path, $file[1], [Text.UTF8Encoding]::new($false)) }
}
Checked dotnet @('build', (Join-Path $auth 'Legacy.Maliev.AuthService.Api/Legacy.Maliev.AuthService.Api.csproj'), '-c', 'Release', '--nologo', '-p:GITHUB_ACTIONS=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$runtime")
Checked dotnet @('build', (Join-Path $seed 'Seed.csproj'), '-c', 'Release', '--nologo', '-p:GITHUB_ACTIONS=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$runtime")
$outputs = @{
    MALIEV_MEMBER_AUTH_DLL = Join-Path $auth 'Legacy.Maliev.AuthService.Api/bin/Release/net10.0/Legacy.Maliev.AuthService.Api.dll'
    MALIEV_MEMBER_AUTH_SEED_DLL = Join-Path $seed 'bin/Release/net10.0/Seed.dll'
}
foreach ($name in $outputs.Keys) {
    if (!(Test-Path -LiteralPath $outputs[$name])) { throw 'Missing private producer binary.' }
    [Environment]::SetEnvironmentVariable($name, $outputs[$name], 'Process')
    if ($env:GITHUB_ENV) { Add-Content -LiteralPath $env:GITHUB_ENV -Value "$name=$($outputs[$name])" }
}
Write-Host 'Private pinned member authority binaries ready; no database or service started.'
