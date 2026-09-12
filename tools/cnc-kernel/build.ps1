param(
    [Parameter(Mandatory)][string] $Output,
    [string] $Container = 'maliev-cnc-kernel-build'
)
$ErrorActionPreference = 'Stop'
$image = 'emscripten/emsdk@sha256:9d6522879357a363ada61862481cc12c5f772d5e9738b8addf95d38490cdc6ea'
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" } }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$outputPath = (Resolve-Path -LiteralPath $Output).ProviderPath
docker create --name $Container --cpus 2 $image bash -lc 'set -eu; git clone --no-checkout https://github.com/kovacsv/occt-import-js.git /src; git -C /src checkout c2148e54b456b571238d35cac037d304053d64b2; git -C /src config submodule.occt.url https://github.com/Open-Cascade-SAS/OCCT.git; git -C /src submodule update --init --depth 1; test "$(git -C /src/occt rev-parse HEAD)" = d2abb6d844231cb8f29be6894440874a4700e4a5; node /overlay/apply-overlay.cjs /src; emcmake cmake -S /src -B /build -DCMAKE_BUILD_TYPE=Release; cmake --build /build --parallel 2'
Check-Exit
docker cp $PSScriptRoot "${Container}:/overlay"
Check-Exit
docker start -a $Container
Check-Exit
docker cp "${Container}:/build/Release/occt-import-js.js" $outputPath
Check-Exit
docker cp "${Container}:/build/Release/occt-import-js.wasm" $outputPath
Check-Exit
Get-FileHash -Algorithm SHA256 (Join-Path $outputPath 'occt-import-js.js'), (Join-Path $outputPath 'occt-import-js.wasm')
# Container intentionally retained to inspect/rebuild. Never writes wwwroot.
