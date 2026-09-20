# Additive benchmark harness

This tool validates the versioned additive-manufacturing benchmark manifest, resolves Bambu Studio preset inheritance, parses structured slicer results, and writes deterministic evidence. It does not tune pricing or certify a production profile.

```powershell
dotnet run --project tools/additive-benchmark/Maliev.AdditiveBenchmark.csproj -- `
  --manifest Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/manifest.v1.json `
  --schema Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/manifest.v1.schema.json `
  --report Legacy.Maliev.Web.Tests/TestAssets/AdditiveBenchmark/report.v1.json
```

Exit codes:

- `0`: manifest is valid and every required benchmark is ready.
- `1`: schema or manifest is invalid.
- `2`: manifest is valid but reference evidence or required coverage is blocked.

Exit code `2` is intentional for the checked-in scaffold. A blocked case is never counted as a pass. Unknown values remain JSON `null`; they must not be replaced with zero or reconstructed from screenshots. Customer CAD stays outside Git until consent is recorded, and is referenced by immutable URI plus SHA-256 when available.

Resolve an installed preset to the full JSON required by Bambu Studio's CLI:

```powershell
dotnet run --project tools/additive-benchmark/Maliev.AdditiveBenchmark.csproj -- resolve-profile `
  --profile "<BambuStudio>/system/BBL/process/0.12mm High Quality @BBL X1C.json" `
  --search-root "<BambuStudio>/system/BBL/process" `
  --output "<restricted-job>/process.json"
```

Pass the generated machine/process/filament files to Bambu Studio with arguments returned by `BambuStudioCli.BuildArguments`. The Windows 02.08.02.61 contract is deliberately strict: `--orient 1` needs an explicit value, STL slicing uses `--slice 0`, the build plate is explicit, and `--export-3mf` receives a relative filename under `--outputdir`. `BambuStudioResultParser` rejects exit-code-zero responses that contain no `sliced_plates`, preventing an empty plate from publishing zero time or material.

The versioned `filament-profile-catalog.v1.json` fixture maps every customer-facing FDM material key to an exact resolved profile candidate or an explicit blocked state. `FilamentProfileCatalogValidator` checks complete coverage and prevents automation eligibility unless a profile is exact and operator approved. Vendor or community profiles are discovery inputs, not automatic production authority.
