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

## Matched-input benchmark evidence

The local helper port includes original source commits `b4e6e871dfb1e046273499c1eb1d2fbccc556237` and the profile-digest facet of `0e7e95216bc4c73495d3e64ae910f88d26f139b9`.

Manifest v1 keeps its existing emitted report-v1 contract. Manifest v2 adds orientation policy, input and profile-bundle digests, observed transform and explicit metric definitions. A fixed-pose mismatch returns `reference_pose_mismatch`; either matched digest must be present. The supplied schema pin must match the manifest version. Recognized v2 input emits report contract 2.0 with `manifestSchemaVersion: "2.0"`; the accompanying report-v2 schema describes invalid cases as well as blocked and ready cases. A valid metadata report does not establish physical accuracy or production approval.

Fixed-pose argument construction requires a prepared 3MF filename and omits automatic orientation and arrangement switches. Search mode keeps the explicit orientation and all-plate argument contract. These helpers construct arguments and parse supplied text; they do not start Bambu Studio, inspect private CAD or generate reference artifacts. The existing accepted pure argument/result helpers and their JSON dependency are reused from Workflows main `a9d0eea56d9192568b5cc91e7075bfd6174adfdd`, with only namespace adaptation for this local assembly.

G-code evidence preserves absolute/relative extrusion, resets, per-tool positions, retractions and arc moves. Missing or ambiguous feature roles leave certification support unavailable. Existing helper limits and finite-number checks remain; support evidence is a syntactic observation rather than physical certification.

Preset resolution exposes `SourceSha256` for the raw leaf bytes separately from `Sha256` for resolved settings. The JSON written by the existing resolve-profile command remains the merged settings object.

The new physical-profile regressions exercise complete synthetic dictionaries against the existing Application parser and role ledger. The historical simulation-result schema records the original physical-field contract; it does not validate every expanded field on the current simulation object. No trusted runtime catalog, eligibility setting in production, pricing path, provider or production configuration is changed by this port. The benchmark fixture ports source0e analytical eligibility and blockers for the eighteen unchanged historical profile records. Fourteen remain eligible after preserving the accepted PC-ESD retirement; substitutions and both later unreviewed ESD decisions stay blocked. Fixture eligibility is not current runtime approval.

Builds and tests must be run before integration. This source-only proposal has not executed a build, test, CLI or vendor operation.
