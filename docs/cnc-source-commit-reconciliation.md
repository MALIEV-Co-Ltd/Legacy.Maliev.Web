# CNC source commit reconciliation

Authoritative source checkpoint: `bbba5046b41c0b1c7fed949d29b093b555b94b9b`.

Legacy verification checkpoint: `4ebcea553aa753d81667a47e3c036004bb0c2e57`.

This document closes the commit-by-commit register from source baseline
`4486f0e964e508e5eb7b43a59eeaec46cc052c67` through the authoritative source
checkpoint. A checked register entry means the commit was inspected and its
final observable behavior is covered by one of the classifications below. It
does not mean source implementation details that conflict with the .NET 10,
PostgreSQL, or deployment boundaries were copied verbatim.

## Runtime parity

- The 22 CNC planner modules have the same Git blob identities as the source
  checkpoint. `CncPlannerSourceBlobParityTests` freezes those identities.
- The public CNC Razor workspace, localization, responsive behavior,
  accessibility behavior, model viewer, workers, SpaceMouse support, and
  same-origin asset graph were migrated in PR #216.
- Upload admission, receipts, claims, authenticated profiles, signed links,
  notification coordination, persistence, submission, and lead analytics were
  migrated through PRs #205-#214. The Legacy page model adapts the source
  contracts to the protected .NET 10 BFF boundary.
- Pricing, manufacturing evidence, feature detection, setup planning, tooling,
  stock, finishing, validation, revision identity, and finalization behavior are
  represented by the final source planner blobs rather than replaying obsolete
  intermediate implementations.

## Verification parity

- All 44 committed source JavaScript regression files are present. Three files
  change only their repository-root prefix; no assertion is removed or skipped.
- The merged checkpoint passed 137 browser-module tests, 418 CNC-engine tests,
  43 page-dependent source tests, and 2,002 .NET tests.
- ASP.NET Core host tests prove the public route and its seven required
  same-origin assets are delivered. Boundary suites cover upload admission,
  receipt state, profile/session behavior, persistence, notifications,
  submission, analytics, and availability gating.
- Protected-main CI validated the exact PR #216 head and the merged main SHA.

## Deliberate adaptations and exclusions

- Source ASP.NET Core 8 page-model, startup, cookie, and middleware changes are
  implemented through the Legacy .NET 10 host and BFF conventions.
- SQL Server runtime behavior is not carried into Legacy. PostgreSQL is the only
  Legacy runtime database provider.
- Gulp and source deployment-image mechanics are represented by the locked
  `build-assets.mjs`, npm lockfile, Docker/runtime contracts, and GitHub Actions
  used by Legacy. This reconciliation does not authorize or perform deployment.
- Generated XML documentation and source-only design, planning, critique, and
  validation-report artifacts are classified as non-runtime evidence. Their
  required behavior and test assertions are represented by the implementation
  and regression suites above.
- Intermediate commits superseded within the source history are reconciled to
  the final checkpoint. The register remains commit-complete so those changes
  cannot disappear from audit history.

The authoritative per-commit disposition is
[`source-commit-register-through-5ac7d04.md`](source-commit-register-through-5ac7d04.md).

## Issue #221 evidence extension

The source was inspected by committed Git object only. The planning-only parent
`ad2774760862371b4ea442d02669ad9ecd4913f5` is recorded in the commit register
as non-runtime evidence: its source deployment directions are intentionally not
portable to Legacy Web.

| Source commit | Classification | Complete Legacy target lineage |
| --- | --- | --- |
| `ad2774760862371b4ea442d02669ad9ecd4913f5` | Non-runtime plan | None |
| `bf079667bba7d9bc01a9688b1f7f5a5be5748a92` | Migrated | `5068239880271e1a17c1d2707cf728c019d4adc2`; `691d87c56c979613c4cd12b43a0d5e2e40994beb` |
| `cbbe1c3a482d0825cecb732bb1044c88bb20358c` | Migrated | `6438973a98a3a76f724df70f4b5c8b44541c9e3b` |
| `a20a65116b7d99c773cc84c7eeab418af85ea956` through `4f6dc90cac6c95b63d834a81cc00a7994893e5cd` | Migrated | `cdac43d` |
| `6222e5e287ca19546be1a0c3fc1f243c3fcdadd2` | Migrated | `7457bf4` |
| `da8dbfa41a0f5b444ee0375ebe0b646b11b63039` | Migrated with .NET 10 additive-workflow adaptation | `f9331a7` |
| `5feed65c1c84ec180829ec132dc9771ff8127c84` through `209bfc62ff128566a3e73f0a3cce4ce8ef43979b` | Migrated | `7457bf4` |
| `a2f3e817d9061e943955f3e371c58f41a176c664` through `bbba5046b41c0b1c7fed949d29b093b555b94b9b` | Migrated | `f9331a7` |

- `bf079667bba7d9bc01a9688b1f7f5a5be5748a92` maps first to implementation
  commit `5068239880271e1a17c1d2707cf728c019d4adc2`, then to the Legacy
  README-path correction commit `691d87c56c979613c4cd12b43a0d5e2e40994beb`.
  The six catalog assets are kept outside runtime delivery, preserve all
  provenance/dimension/coverage checks, and retain every
  `machiningAuthorized: false` guard.
- `cbbe1c3a482d0825cecb732bb1044c88bb20358c` maps to Legacy target commit
  `6438973a98a3a76f724df70f4b5c8b44541c9e3b`. The two source worker blobs are
  exact matches; only `manufacturing_summary` omits legacy diagnostics, while
  normal callers retain them. The source STEP/STP validation import requests
  absolute millimeter tessellation and display tessellation remains unchanged.

Issue #221 evidence is validated by the source-history register contract,
`scripts/verify-complete-source-history-parity.ps1 -ManifestOnly`, the catalog
validator and regression suite, focused summary/import contracts, Release build,
`dotnet format --verify-no-changes`, package vulnerability audits, and staged
gitleaks. The source-history script deliberately remains frozen at the prior
complete cross-repository boundary; this register closes only the explicitly
scoped three-commit CNC extension and does not claim classification of the
unrelated intervening source history.

## Issues #222 and #223 evidence extension

The native-CAD series preserves exact source assets for the OCCT browser runtime,
native document and topology adapters, bounded repair kernel, native region
interpretation, warning lineage, semantic ownership, and dispatch contracts.
Repository-root references in source JavaScript tests are the only textual test
adaptation; their assertions remain unchanged. The source Playwright fixture is
not copied because Legacy Web validates browser modules through its existing
current-build browser lane rather than the source ASP.NET Core 8 staging host.

The retry commit's CNC upload, thumbnail, pricing-failure ownership, stale-attempt,
and watchdog changes are ported to the Legacy CNC page and shared model-viewer
runtime. Additive upload/retry behavior remains owned by the .NET 10
`InstantQuotationWorkflow` and `upload-controller.mjs`; the obsolete source Razor
implementation is not reintroduced. The migrated watchdog regression contains all
25 assertions and passes against the Legacy path adaptation.

Validation covers exact source blobs before repository-path adaptation, 96 focused
passing JavaScript assertions with six explicit reviewed-fixture skips, the full
browser/CNC suite, deterministic asset generation, source-blob contracts, Release
build, affected .NET tests, formatting, package audit, and secret scanning.
