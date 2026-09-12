# CNC source commit reconciliation

Authoritative source checkpoint: `cbbe1c3a482d0825cecb732bb1044c88bb20358c`.

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

- `bf079667bba7d9bc01a9688b1f7f5a5be5748a92` maps to Legacy target commit
  `691d87c56c979613c4cd12b43a0d5e2e40994beb`. The six catalog assets are kept
  outside runtime delivery, preserve all provenance/dimension/coverage checks,
  and retain every `machiningAuthorized: false` guard.
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
