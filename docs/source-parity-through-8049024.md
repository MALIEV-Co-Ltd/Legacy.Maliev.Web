# Maliev.Web source parity through `48e628c`

## Audit boundary

- Read-only source: `R:\maliev-web`, `main`, `48e628cf7803264bd0b09bfa7a55b15b47e192dd`.
- Migrated target: `Legacy.Maliev.Web` on .NET 10 and Blazor static SSR, with interactive islands only where required.
- Audited range: `dcc088f..48e628c` (92 commits).
- Complete Web/Web-test history: 311 commits, independently inventoried in
  `docs/complete-source-history-parity-through-8049024.md` and verified against
  the read-only source by `scripts/verify-complete-source-history-parity.ps1`.
- Production deployment is outside this audit. The source repository was not modified.
- Local-only Claude settings and Impeccable critique artifacts are intentionally excluded.
- Source IBM Plex work is superseded by the owner-approved `Inter, "Noto Sans Thai", sans-serif` contract.

Status legend: **Migrated** means equivalent behavior is implemented and covered in Legacy Web; **Excluded** means repository tooling or an explicitly superseded choice; **Gate** means the Web implementation is present but release evidence belongs to GitOps or another Legacy service.

## Commit ledger

| Source | Status | Legacy implementation or disposition |
| --- | --- | --- |
| `2cee60b` | Excluded | Image-generation documentation snapshot; no Web runtime behavior. |
| `8505810` | Excluded | Agent configuration snapshot and validator. |
| `933d605` | Excluded | Vendored agent YAML generator. |
| `621771a` | Excluded | Global protocol snapshots. |
| `793d027` | Excluded | Local Claude permissions/settings. |
| `baa0509` | Excluded | Vendored skills tooling. |
| `80af7ba` | Excluded | Agent documentation tooling. |
| `a3e58c6` | Excluded | Hatch-pet tooling. |
| `cc1deea` | Migrated | Per-part unit pricing, explicit minimum-order surcharge and review selection in `e722450`/`24c5106`; pricing and review tests. |
| `3c0bdc3` | Migrated | Review pricing clarity, status and diagnostics in `e722450`/`24c5106`. |
| `e7a9521` | Migrated | Responsive quotation layout/navigation and summary in `e722450`/`24c5106`/`065479d`. |
| `87f86bf` | Migrated | Culture persistence through the scoped interactive route; interactive-route tests. |
| `3c967db` | Migrated | Main-thread advisory parsing/lifecycle adapted to the current IQ browser modules and interop/viewer tests. |
| `de4f9a9` | Migrated | Metrics and summary placement/wrapping in the IQ responsive implementation. |
| `279660a` | Migrated | Pinned deterministic 3MF loader resolves cross-component references; exact Node regression tests. |
| `238ce8a` | Migrated | Responsive viewer, CSP and route-module isolation; responsive and interop tests. |
| `1fbb19b` | Migrated | Configurator pricing-summary alignment. |
| `edf451e` | Migrated | Viewer lighting and shading adaptation; viewer tests. |
| `d88d279` | Migrated | Paired material-detail state and active sticky TOC in `b9e2078`; responsive and JS tests. |
| `03a7736` | Migrated | Flat shading, responsive layout and accessibility; viewer/accessibility/responsive tests. |
| `f701209` | Migrated | Authoritative-estimate status and copy. |
| `4948e98` | Migrated | Foldable material list and visible part numbering. |
| `4b416b9` | Migrated | Thai rendering/localization and viewer sizing. |
| `09f04df` | Migrated | Upload-first dropzone and parts beside preview. |
| `8f693a8` | Migrated | Viewer fills its assigned column. |
| `d161f80` | Migrated | Metrics card and summary no longer stretch incorrectly. |
| `7f15700` | Excluded | Impeccable Live source tooling. |
| `6040ed5` | Excluded | Impeccable Live configuration/layout slots. |
| `0dac145` | Migrated | Measurements grouped with their part. |
| `4062184` | Migrated | Thai/English legal links and resources. |
| `f506d03` | Migrated | Material settings height and viewer behavior. |
| `172d08d` | Migrated | Exact nine standalone quotation formats, full MIME picker allowlist and 200 MB admission limit; workflow/upload and per-format browser-module tests. |
| `369b749` | Migrated | Safari STL MIME/UTI identifiers (`application/sla`, `application/vnd.ms-pki.stl`, `model/x.stl-binary`, `model/x.stl-ascii`) are retained in the picker contract. |
| `bb74b3e` | Migrated | iPhone/iPad and Mac-touch detection preserves the application allowlist in `data-accepted-types`, removes Safari's unsupported picker filter, and still rejects unsupported extensions before preview/upload; Node regression tests cover iOS, iPadOS and desktop behavior. |
| `33ce2c0` | Migrated | Summary viewport containment in `065479d`. |
| `da854df` | Migrated | Missing browser content type accepted and normalized to octet-stream for validated member uploads. |
| `ee2bb59` | Migrated | Opaque incident ID, response header, structured logging and Thai UI in `122b338`; incident tests. |
| `a76d1c6` | Migrated | Exact BuildPreference enum, factors, persistence, UI and Thai resources. |
| `297e8ab` | Migrated | Compact per-part settings and status. |
| `f9523e3` | Migrated | Authoritative deterministic repricing and revision behavior. |
| `03e307f` | Migrated | Degenerate cap-slice guard, 5% edge trim and 24 samples in `26ede99`; geometry tests. |
| `21ba323` | Migrated | Material-selection scroll affordance and exact catalog. |
| `92122f4` | Migrated | Structured customer quotation detail and review. |
| `54d91d6` | Migrated | Tablet configurator layout. |
| `c26acfa` | Migrated | Legacy design-system adaptation for Instant Quotation. |
| `f80b4b0` | Gate | ADC-based, fail-closed reCAPTCHA implementation and form tests are present; KSA/GSA Workload Identity must be proven in GitOps before release. |
| `0d80edc` | Migrated | Customer/order fields, validation, review and submission. |
| `e1c871c` | Migrated | Automatic material price comparison and pricing tests. |
| `f3a853d` | Migrated | Persistence-before-finalization, idempotent upload linking, replay/retry and partial-finalization tests. |
| `e09e3b0` | Gate | Source deploy script is not copied; equivalent GitOps service-account routing remains a release gate. |
| `d7e0296` | Migrated | Typed timeouts and fail-closed resilience replace the UI workaround. |
| `beecf31` | Migrated | Thin-part DFM and degenerate-boundary corrections. |
| `ada8404` | Migrated | Web-side customer/order workflow is present; the source Intranet portion remains in the Legacy Intranet lane. |
| `344c32a` | Migrated | Exact catalog validation and fail-closed fallback. |
| `b220158` | Migrated | Opaque transactional credential callbacks in `4283bfa`/`3546120`; Auth main `764e29e` (PR #66). |
| `1c611bb` | Gate | Web material/color contract is frozen in tests; downstream Legacy material compatibility is a separate service gate. |
| `8888ce5` | Excluded | Impeccable tooling/settings/critique only. |
| `bea4a42` | Migrated | Expanded service pages, finishing route, SEO, breadcrumbs, location and pricing in `19b42e1`/`7e604c1`/`b9e2078`. |
| `1529bc5` | Migrated | Navigation/footer finishing entry and hierarchy. |
| `d46bf5e` | Excluded | Critique snapshot. |
| `1498292` | Migrated | Every TOC target is reachable; tolerance guidance and sticky-TOC tests. |
| `5e2030b` | Gate | Customer callback is migrated; employee callback belongs to Legacy Intranet/Auth validation. |
| `beba894` | Migrated | Secure resend action, no-store/no-referrer and localized recovery. |
| `e62d177` | Migrated | Shared motion system with reduced-motion fail-safe in `29353ba`; source and Node tests. |
| `b16aa08` | Migrated | Dependency/build-asset refresh; current audit evidence is npm audit with zero vulnerabilities. |
| `04c9bb0` | Migrated | HLC matcher, preview, quotation prefill and browser tests. |
| `e25c833` | Migrated | First-login required actions and set-initial-password flow in `3546120`; Auth main `764e29e` (PR #66). |
| `81909e6` | Migrated | Responsive chapter navigation and sticky TOC. |
| `2fbee81` | Migrated | Finishing selection guidance. |
| `7055e4e` | Migrated | Matcher diagnostics route only through consent-gated `malievAnalytics`; analytics tests. |
| `4d16491` | Excluded | Critique snapshots. |
| `a00a44e` | Excluded | IBM Plex choice superseded by the owner-approved Inter/Noto Sans Thai stack and font contract tests. |
| `866fa2f` | Migrated | Matcher guidance and preview polish. |
| `7987661` | Migrated | Consultation hover contrast and shared responsive UI contract. |
| `94581dd` | Migrated | Color fidelity/PBR interaction and matcher core tests. |
| `f6af750` | Migrated | Mesh Splitter link and finishing/color parity tests. |
| `ad8cbb7` | Migrated | Robots permits general and Google AI crawlers while keeping private routes disallowed; SEO tests. |
| `ab2e481` | Migrated | Localized account/inquiry forms, guarded score-based reCAPTCHA, credential leak prevention and password/email workflows. |
| `370fe20` | Migrated | Runtime motion/responsive regression contracts and geometry tests; IBM-font expectation and Impeccable artifacts are excluded as described above. |
| `d913fcd` | Migrated | CNC pricing cards now expose black oxide, standard anodizing, and hard/custom anodizing planning estimates without naming the outsourced supplier. |
| `8049024` | Migrated | Standard anodizing now uses the final bilingual THB 2,500 starting estimate with part-size and processing-complexity qualification. |
| `5f11642` | Migrated | Mobile LCP/search discoverability: hero fetch priority, localized metadata, schema, route catalog, robots, and reduced-motion behavior. |
| `dbcf5d8` | Migrated | Thai service/home SEO intent now consistently describes custom part manufacturing. |
| `24d001a` | Validation translated | Quotation pricing browser assertions are represented by the Legacy interactive and browser-module contracts. |
| `acce138` | Validation translated | Comprehensive SEO release checks are represented by Legacy SSR metadata, schema, robots, route, and crawler contracts. |
| `0880eb1` | Migrated | Review-only browser preliminary quotation preview with A4 print/PDF actions and complete part/order details. |
| `e8eaf82` | Migrated | Preview uses the canonical MALIEV logo data URI. |
| `00a7c41` | Migrated | Preview action is rendered only in the order review island. |
| `25a1e12` | Migrated | Print, download-as-PDF, and close actions remain separate and localized. |
| `57844bb` | Migrated | Preview reports authoritative print time per part rather than multiplying by quantity. |
| `8cbc28f` | Migrated | Preview thumbnails use the enlarged 156px A4 layout and safe crop fallback. |
| `48e628c` | Validation translated | Customer-continuation coverage is represented by the Legacy review/customer route and source parity tests. |

## Aggregate evidence at audit completion

- Release build: zero warnings and zero errors.
- Non-HTTP .NET partition: 1,013 passed, 0 failed.
- HTTP surface partitions: 113 + 42 + 107 = 262 passed, 0 failed.
- Browser modules: 101 passed, 0 failed.
- npm audit: zero vulnerabilities.
- Formatting: no changes required.

## Latest delta validation (2026-08-07)

- Source verification is exact: 311 Web/Web-test commits, 92 commits after
  `dcc088f`, and no writes to the read-only source repository.
- Release build: 0 warnings, 0 errors. `dotnet format --verify-no-changes` and
  `git diff --check` are clean.
- Latest SEO, localization, route, and preliminary-quotation regression slice:
  **28 passed, 0 failed, 0 skipped**.
- Instant Quotation partition: **430 passed, 0 failed, 0 skipped**.
- Public/member route partition: **378 passed, 0 failed, 0 skipped**.
- Runtime integration: **7 passed**; Redis and submission-cache integration:
  **5 passed**; targeted WebSurface chunks all passed.
- Browser modules: **101 passed, 0 failed, 0 skipped**; `npm audit` reports
  **0 vulnerabilities**.
- A resource-safe class-group run completed **1,322 passed, 0 failed, 0
  skipped** across 16 fresh testhosts, covering the discovered .NET test
  cases without the monolithic host's memory growth.

The single-process full assembly run remains a release gate: the testhost
exceeded the available resource budget (over 10 GB) before emitting a summary
and was terminated without an assertion failure. The same tests pass in
isolated partitions, but the resource-safe whole-assembly command still needs
to be completed before production release. No Aspire, GKE, PostgreSQL,
Secret Manager, or production application was changed or deployed by this
parity slice.

## Remaining release gates

1. AuthService main `764e29e` is landed; retain its 409 required-action and opaque challenge contracts during Aspire review.
2. Prove Legacy Web KSA/GSA Workload Identity and ADC-backed reCAPTCHA configuration through GitOps.
3. Validate employee identity callbacks in Legacy Intranet/Auth and material-catalog compatibility in their own Legacy service lanes.
4. Run the integrated Aspire review gate. Do not deploy Legacy applications to production until the owner explicitly approves it.
