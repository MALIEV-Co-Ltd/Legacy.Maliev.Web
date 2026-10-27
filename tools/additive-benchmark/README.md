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

Create a deterministic, anonymous inventory from an owner-consented private corpus:

```powershell
dotnet run --project tools/additive-benchmark/Maliev.AdditiveBenchmark.csproj -- inventory-corpus `
  --root "<owner-consented-restricted-corpus>" `
  --output "<restricted-job>/anonymous-inventory.json" `
  --consent-id "<actual-owner-consent-reference>" `
  --limit 48
```

The inventory hashes supported CAD files, removes exact-byte duplicates, samples equally across four byte-size buckets when possible, and freezes 25% (at least one and at most twelve files) as the release holdout before tuning. It records only anonymous IDs, digests, byte counts, formats, buckets, and evidence states. Source paths, filenames, CAD bytes, geometry labels, and generated slicer artifacts remain outside Git. An inventory entry stays `matched_reference_missing` until its geometry family is reviewed and a fixed-pose Bambu reference is produced from the exact approved profile bundle.

Resolve an installed preset to the full JSON required by Bambu Studio's CLI:

```powershell
dotnet run --project tools/additive-benchmark/Maliev.AdditiveBenchmark.csproj -- resolve-profile `
  --profile "<BambuStudio>/system/BBL/process/0.12mm High Quality @BBL X1C.json" `
  --search-root "<BambuStudio>/system/BBL/process" `
  --output "<restricted-job>/process.json"
```

Pass the generated machine/process/filament files to Bambu Studio with arguments returned by `BambuStudioCli.BuildArguments`. The Windows 02.08.02.61 contract is deliberately strict: search-mode STL references pass `--orient 1 --arrange 1`; fixed-pose references use a prepared 3MF whose object transform is verified against the manifest and do not pass either auto-transform option. The build plate is explicit and `--export-3mf` receives a relative filename under `--outputdir`. `BambuStudioResultParser` rejects exit-code-zero responses that contain no `sliced_plates`, preventing an empty plate from publishing zero time or material.

Manifest v1 remains readable as historical evidence. It cannot certify simulator accuracy because it does not prove that the requested and sliced transforms match. Manifest v2 adds `orientationPolicy`, `inputArtifactSha256`, `profileBundleSha256`, `referenceTransform4x4`, and explicit time/material/support metric definitions. A fixed-pose mismatch is `reference_pose_mismatch`; a blocked or ambiguous support metric remains unavailable rather than becoming zero.

`BambuStudioResultParser.ParseGCodeEvidence` recognizes absolute and relative extrusion, `G92` resets, per-tool coordinates, retractions, linear moves and arc moves. Support evidence is certifying only when all positive extrusion belongs to known feature roles. Corpus selection and reference generation must be driven by an immutable private manifest: record source SHA-256, resolved profile hash, prepared 3MF hash, slicer version, transform and exact command arguments. Keep source CAD, generated 3MF and G-code in restricted storage; Git contains only synthetic fixtures and anonymized hashes.

The versioned `filament-profile-catalog.v1.json` fixture maps every customer-facing FDM material key to an exact resolved profile candidate or an explicit blocked state. `FilamentProfileCatalogValidator` checks complete coverage and prevents automation eligibility unless a profile is exact and operator approved. Vendor or community profiles are discovery inputs, not automatic production authority.

Compatibility: the historical Web report remains report v1, including its constant manifestSchemaVersion value of 1.0. Matched manifest v2 acceptance does not adopt the separate Workflows report-v2 adaptation. Existing v1 golden fixture and 0/1/2 exit semantics remain unchanged. Corpus inventory examples are not consent or permission to read customer files.
