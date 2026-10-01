# Public service rendered coverage proof — 2026-10-02

Logical acceptance issue: https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/455.

Tests-only acceptance slice. Original diagnostic baseline was `18adfd9b881a6b6d5756a1fdcb0dbdbda3ac79cb` in isolated `web-member-auth-indexing-20261002`; root transferred only these two files to its separate acceptance workspace at the complete452 candidate. Exact-main acceptance remains pending. Runtime, assets, localization, old tests, project files, workflows, migration ledgers and original source objects are not changed by this slice. The original frozen issue453 files and diagnostics remain untouched.

## Independent committed-source provenance

Frozen source checkpoint: `bed10c7d15e0698e0b75f1329d0f312937f5d77f`. Committed objects are read only from `B:/maliev-legacy/.artifacts/source-commit-mirror-20260930.git`.

### Thai printing search entry

Source `6de82fd9760e86c71ddba3085879a63b43faff9f`; parent `54ad353a386dc9476f779f9b3fdf152b93b135c9`; subject `Align Thai 3D printing search entry`; sole owner Web.

Complete paths:

- `Maliev.Web.Tests/SeoBusinessContractTests.cs`
- `Maliev.Web.Tests/ServicePageRedesignSourceTests.cs`
- `Maliev.Web.Tests/ServiceResponsiveImageBrowserTests.cs`
- `Maliev.Web/Pages/Services/3D-Printing.cshtml`
- `Maliev.Web/wwwroot/src/app/css/service-pages.css`

The new source browser assertion checks exact Thai title, description, H1, proof-link fragment and meaningful proof label. Despite its first-screen name and 1280x900 viewport, it does not assert a bounding box or position. This slice preserves those literal contracts through HTTP-rendered elements, not an invented viewport guarantee. The English title/body were subsequently changed by accepted source copy; this slice does not resurrect the old English snippet.

Actual target owners: `Components/Pages/Services/ThreeDimensionalPrintingPage.razor`, `ThreeDimensionalPrintingContent.razor`, retained `Pages/Services/3D-Printing.cshtml`, existing service CSS and generated bundles. Existing `ThreeDimensionalPrintingStaticSsrRouteTests` independently cover exact localized title/description/H1. New tests also check the Thai literals without deriving them from target implementation.

Four source CSS rules were inspected directly against `wwwroot/src/app/css/service-pages.css` and both generated `wwwroot/dist/route-services.css` and `route-services-index.css`:

| Selector | Source rule / generated correspondence |
| --- | --- |
| `.service-hero-proof` | Margin `.85rem 0 0`. |
| `.service-hero-proof-link` | Inline flex, centered alignment, `.55rem` gap, white text, `.9rem` size, weight600, underline, `.2em` underline offset. Source `rgba(255,255,255,.55)` becomes equivalent `#ffffff8c`. |
| `.service-hero-proof-link:hover` | Underline color `currentColor`. |
| `.service-hero-proof-link i, .service-hero-proof-link .svg-inline--fa` | Width/height1rem and color `#9dc2f5`. |

No generation/build task was changed or rerun for CSS. This is inspected correspondence, not browser/computed-style or visual acceptance.

### Confirmed service coverage promises

Source `7b4b2af697207d36a6e7b7784dddefa150193e97`; parent `63e5f99f3cef37b1f005b3399333ede53e560587`; subject `Publish verified service coverage details`; sole owner Web.

Complete paths:

- `Maliev.Web.Tests/LowVolumeInjectionMoldingPageSourceTests.cs`
- `Maliev.Web.Tests/SeoBusinessContractTests.cs`
- `Maliev.Web.Tests/ServicePageRedesignSourceTests.cs`
- `Maliev.Web/Pages/Services/3D-Scanning.cshtml`
- `Maliev.Web/Pages/Services/Low-Volume-Injection-Molding.cshtml`
- `Maliev.Web/Pages/Shared/_ServiceLocationPartial.cshtml`

Independently pinned English/Thai promises: nationwide file acceptance and parcel fulfillment; phone/LINE notice or appointment before visiting Pak Kret; project-reviewed onsite work with distance/scope-based travel; onsite scanning of very large, non-disassemblable or unshippable parts subject to feasibility/access/safety/lighting/space/schedule; purchased PIMM installation at the customer site with outside-metropolitan travel/installation quoted by distance.

Actual target owners: `Components/Shared/ServiceLocation.razor`, `ThreeDimensionalScanningContent.razor`, `ThreeDimensionalScanningPage.razor`, `LowVolumeInjectionMoldingContent.razor`, `LowVolumeInjectionMoldingPage.razor` and existing `PublicServiceStructuredData` display component/model.

The source adds the installation question and answer to both visible and structured PIMM FAQ, increasing each to seven entries. The tests parse raw JSON-LD, require owned schema.org context, the precise installation question/answer, the independently expected Service name/URL/provider and Thailand area. These prove emitted documents, not actual travel or installation execution.

Compatibility distinction: source7b4 changes scanning's visible onsite answer, not its concise structured FAQ. Both source intents are retained; no requirement is invented that scanning JSON-LD repeat the new visible travel sentence. Nationwide and appointment paragraphs likewise are not fabricated FAQPage entries. Later accepted shipping/pickup copy may add detail while preserving the original literal promises.

## New test boundary

Only `Legacy.Maliev.Web.Tests/PublicServiceCoverageRenderedContractTests.cs` is added. It uses the unchanged `TestingWebApplicationFactory`, actual routing/localization/renderers, and HTTPS test-client GETs. No new endpoint, fake page, authentication principal, permission, external service substitution, persistence provider or browser fixture is introduced.

32 cases:

- Twelve positive cases: English/Thai proof anchor and unique rendered gallery target; source promises across printing, scanning and injection; scanning visible/concise structured FAQ; PIMM visible/raw structured FAQ parity. Supported printing/scanning active and retained owners are exercised inside each case; PIMM has only its actual primary Blazor owner.
- Two explicit characterization cases: disabling the only injection route owner yields404, without inventing a retained Razor fallback.
- Eighteen negative controls: independently corrupt fetched documents' proof anchor, target or label; remove coverage/appointment/travel promises; change onsite answer; independently change PIMM visible or raw structured installation answers. Original fetched documents must pass first and every mutation must actually change its intended field.

Visible-text corruptions decode individual text nodes, never an entire raw JSON-LD document. Structured installation controls alter only raw script JSON, verify unchanged visible promise and changed parsed structured answer, and require the actual equality assertion to fail. The opposite controls prove unchanged structured answer and changed visible answer. These are test-oracle sensitivity controls, not claims of mutations in deployed production.

## Diagnostics and validation

Root independently reviewed the complete source/target mapping and both files.
Fresh solution Release completed with zero warnings/errors and new32 focused tests
passed. The first unfiltered attempt was deliberately stopped after missing process-local
pinned producer paths; it is not acceptance evidence. Root rebuilt all reviewed pinned
profile/member-auth/SCB helper services in the SAME PowerShell process, each with zero
warnings/errors, then rebuilt Web before rerunning the unfiltered suite. Final
`TestResults/root-service-coverage-prepared-full/full.trx` passed3091/3091 with
zero failures/errors/skips/timeouts/aborts. Independently parsed SHA256:
`DA86E80F98842701FF35BE08A55A742D40BB8663606F9F1E62CC2E5AAA934623`.
This preserves the normal authenticated producer and disposable PostgreSQL/Redis gates;
no test was skipped or replaced to repair the prerequisite failure. Protected-head,
exact-main and per-source ledger acceptance remain separate pending gates.

Baseline Release build: zero warnings/errors. First new-file build had ten xUnit2031 analyzer errors; fixed only new assertions to use the predicate overload. An initial PowerShell logger argument was incorrectly escaped; corrected quotation before execution. These are development diagnostics, not runtime defects.

Preserved `TestResults/service-coverage-20261002/service-coverage-original.trx`: 30 executed, 20 passed, ten failed, zero skipped. Four failures came from this new fixture's nonexistent retained PIMM assumption. Six came from Thai HTML-corruption helpers not changing ASP.NET's hexadecimal-encoded text. None establishes a production RED. The normal primary PIMM HTTP cases passed. Corrected focused run:32 passed, zero failed/skipped, preserved as `service-coverage-corrected.trx`. Stronger final raw-schema negative assertions also passed32/32, preserved as `service-coverage-final.trx`. The bounded affected HTTP regression set passed106/106 with zero skips, preserved as `service-coverage-affected-http.trx`.

The first formatting invocation incorrectly supplied an MSBuild property argument, which `dotnet format` does not support; corrected to an invocation-local `MalievWorkspaceRoot` environment variable. The next check reported two whitespace positions in this new file's client options (despite returning exit0); these were corrected in this file only before the final gates. A guessed generated CSS filename was absent; actual inspected generated bundles are the two route CSS files named above. No generated file was added or changed.

Commands (worktree root; no shared sibling outputs):

```powershell
dotnet build Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-restore -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\web-member-auth-indexing-20261002\.dependencies --verbosity minimal
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\web-member-auth-indexing-20261002\.dependencies --filter FullyQualifiedName~PublicServiceCoverageRenderedContractTests --logger 'trx;LogFileName=service-coverage-final.trx' --results-directory TestResults/service-coverage-20261002 --verbosity minimal
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\web-member-auth-indexing-20261002\.dependencies --filter 'FullyQualifiedName~PublicServiceCoverageRenderedContractTests|FullyQualifiedName~ThreeDimensionalPrintingStaticSsrRouteTests|FullyQualifiedName~ThreeDimensionalScanningStaticSsrRouteTests|FullyQualifiedName~LowVolumeInjectionMoldingParityTests|FullyQualifiedName~PublicServiceStructuredDataMigrationTests|FullyQualifiedName~PublicServiceFaqStructuredDataParityTests' --logger 'trx;LogFileName=service-coverage-affected-http.trx' --results-directory TestResults/service-coverage-20261002 --verbosity minimal
# Invocation-local MalievWorkspaceRoot set to the same absolute private path:
dotnet format Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --verify-no-changes --no-restore --include Legacy.Maliev.Web.Tests/PublicServiceCoverageRenderedContractTests.cs --verbosity minimal
# Scoped new-file content scan only:
Get-Content Legacy.Maliev.Web.Tests/PublicServiceCoverageRenderedContractTests.cs,docs/public-service-coverage-rendered-proof-design-20261002.md -Raw | gitleaks stdin --no-banner
```

No runtime defect or runtime repair is asserted. Missing acceptance coverage was the purpose of this slice; genuine behavior gaps, if later found, require separate root approval. Whole source-owner closure requires root review, integration and exact accepted-main evidence; no ledger or issue closure follows automatically.

Final freeze gates: Release build zero warnings/errors; post-whitespace focused `service-coverage-frozen.trx`32/32 passed, zero failed/skipped; bounded affected HTTP106/106 passed, zero failed/skipped; scoped formatting verification exit0 with no diagnostics; scoped new-file gitleaks scan no leaks. These are the bounded gates, not a completed unfiltered suite or a coverage percentage claim.

Evidence SHA256:

- Original diagnostic `service-coverage-original.trx`: `658637D28CE8040CF818A9E2CC86D893C6691E0FA3EE8323308CFCCF508177D0`.
- Post-whitespace focused `service-coverage-frozen.trx`: `D4CB4652F71F83786017E0AAC55BE8E34E9EB85BDC4A06A3B86B69C7453C14EB`.
- Affected HTTP `service-coverage-affected-http.trx`: `2C8AD8FC7FFEDC029C29B693FFE0470AAF3C00A6C93F2BEA8E48B26A01497881`.

Issue453 frozen test SHA256 remains `61AC83DA9C3D889858F0BC784784C0C6297F1AFF7859D204535FE1D6D6E2E3FB`; its design note remains `BE16742EC1BBB835F168DC0EC610F18ABB0011CCADF2EFA0932ADD2FA07DDBE4`.

Deliberate exclusions: Chromium/viewport/computed styles/screenshots, unfiltered full suite while the other acceptance lane owns it, joined Aspire/normal cookie-auth/Redis/PostgreSQL/provider acceptance, model upload/pricing/quotationPOST/order writes, SEO rankings/CTR benchmarks, Ads/provider writes, physical calibration, CNC expansion and cost/infra/deployment authority. Full suite and coverage remain integration gates, not inferred from focused tests.
