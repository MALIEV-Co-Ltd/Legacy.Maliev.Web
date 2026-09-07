# CNC source commit reconciliation

Authoritative source checkpoint: `5ac7d045c51194edd9e64d8564f1b726b001be34`.

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
