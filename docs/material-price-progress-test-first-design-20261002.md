# Material comparison continuity — Web #450, parent #380

## Current stage and scope

Bounded [Web #450](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/450), parent #380, in `web-material-price-progress-20261002`, branch `codex/material-price-progress-test-first-20261002`. Original accepted base `5d74a867e6764a9d80bd455be94af1d66d60bd09` was fast-forwarded first to accepted #447 main `d2165c69afc27f2084a31fe06bf2d5f75373321a`, then protected #449 merge `18adfd9b881a6b6d5756a1fdcb0dbdbda3ac79cb`, preserving exactly four owned files without conflict. Root independently reproduced initial 3RED/2GREEN and approved only one-line published-view invalidation at the start of coordinator `PersistAndPriceAsync`. This line is now implemented; no resources, UI/progress transport, project, workflow, authority, arithmetic or schema changes. Root subsequently reviewed and approved precisely one old test's failure assertion adaptation plus its successful-retry quote assertion below. Parent #380 and source `c4dd00b323a350f06a27afbce03dcbbcc56e16fb` stay pending for per-material presentation/incremental progress. Root independent acceptance and protected CI remain required; #449 post-main CI was pending at this handoff, not claimed accepted here.

Fresh final combined private Release: **zero warnings/errors**; combined focus **51/51 PASS** (#449's 40 plus #450's 11), combined affected suite **186/186 PASS**, and unfiltered full with raw coverage **2973/2973 PASS, zero failures/errors/skips, 13m12s**. Whole solution format, five vulnerability audits and four scoped secret scans pass. Browser window was released after the full terminal result. Earlier baseline49, expanded5RED/5GREEN, old145PASS/1policy failure and all pre-integration evidence remain below as historical chronology, not current status. The NEW regressions are actual coordinator/state proof, not authenticated HTTP, real uploaded-file or manufacturing calibration proof; running the unchanged full suite does not turn their synthetic geometry into manufacturing evidence.

## Exact retained source scope

Source `c4dd00b323a350f06a27afbce03dcbbcc56e16fb`, parent `7aa95d1111528d3d875d8001a43676118974c1d0`, changes:

- `Maliev.Web/Pages/InstantQuotation/3D-Printing.cshtml`: selected material moves first in the serial physical-comparison schedule, plus loading layout.
- `Maliev.Web/wwwroot/src/app/js/model-viewer/model-viewer.js`: per-material loading ellipsis becomes an aria-hidden spinner with localized loading-price label; unavailable stays distinct from numeric zero.
- `Maliev.Web.Tests/InstantQuotationPricingBrowserTests.cs`: independent browser scenarios for selected-first scheduling/loading price presentation.

Both runtime behaviors survive in committed latest source `bed10c7d15e0698e0b75f1329d0f312937f5d77f`; ancestry was verified. MigrationTracking's committed `migration/source-commit-resolutions.json` retains this individual Web-owner record pending under #380. No source SHA disposition changes belong to this agent.

The target already analyzes selected FDM materials first in `InstantQuotationAuthoritativePricingService.cs:40–98`. Do not re-port that behavior. It then evaluates bounded comparisons before returning one final quote. Selected-first scheduling and incremental per-material publication are different contracts; this first slice does not invent a progress transport or change physical authority.

## Historical reached defect and exact consumer

`InstantQuotationWorkflowCoordinator.UpdateConfigurationCoreAsync` changes the live part configuration then awaits `PersistAndPriceAsync` while holding its state gate. `PersistAndPriceAsync:790–849` clears stored quote authorization/physical receipts and saves the changed protected request before authoritative pricing. But it does not clear the previously published `OrderQuote` until a new result returns. `Parts:96–115` maps that old quote back onto the changed part by PartId.

`InstantQuotationWorkflow.razor:319–336` directly renders `part.Quote.MaterialPrices`, with numeric values and no per-material pending guard. The component's `Parts` accessor uses this exact coordinator view. Thus a changed part with the previous quote exposes old numeric material prices during the reached delay; cancellation or an exception keeps them exposed afterward. The aggregate status spinner at `:390–400` does not remove these prices. No source-text test is used to establish the defect: the tests assert actual returned live state, changed independently persisted configuration, reached delayed pricing and retained old quote.

The protected store already removes the old authorization before the pricing delay. The first RED verifies that fact independently; it does **not** claim that an old signed ticket can submit the changed configuration. State-display continuity and protected authorization are separate boundaries. For a refused or canceled first persistence attempt, the stored prior request/authorization remains unchanged, while the now-mutated live view must not expose its old quote. The new early-boundary cases separately verify that distinction and that pricing has not started.

## Test boundaries and controls

NEW `MaterialComparisonRepricingContinuityTests.cs` uses the actual coordinator, actual `DistributedInstantQuotationSessionStore`, DataProtection and actual `AdditiveQuoteTicketService`, plus actual `InstantQuotationPricingService` for provisional resin quotes. The storage backing is deliberately memory `IDistributedCache`, not Redis; these tests assert no cross-replica/CAS guarantees. Geometry/prior-upload lineage is explicitly synthetic. Controlled `IInstantQuotationAuthoritativePricingService` gates latency/failure only and calls the real kernel; this is not the actual admitted-STL analysis service. Unreceipted FDM comparisons remain unavailable, never fabricated physical proof. The upload client throws if called, since this is restoration/repricing, not new upload acceptance.

Each setup restores the existing protected session through the real coordinator and verifies a real protected quote authorization was stored. No handmade auth claims/cookies, reflection, internal-renderer API, BL0006 suppression, browser process, package or project change is introduced.

Final reached outcomes:

| Test | Executed result / independently observed behavior |
|---|---|
| MaterialChange_DelayedAuthoritativeQuoteRemovesPreviousMaterialPricesImmediately | RED: live/persisted selection is K/Gray while live Quote is still M68/White/quantity1; stored authorization is already null |
| QuantityChange_CallerCancellationCannotLeavePreviousComparisonPricesVisible | RED after actual OCE carrying caller token: live quantity2 still exposes quantity1 quote |
| FailedRepricing_CannotLeavePreviousComparisonPricesVisible | RED after original IOException identity propagates: live quantity2 still exposes quantity1 quote |
| UnavailableAuthoritativeResult_RemovesComparisonPricesRatherThanInventingZero | GREEN: returned unavailable clears quote and estimate rather than producing zero |
| SerializedNewerConfiguration_WinsAfterOlderDelayedCompletion | GREEN: newer request cannot enter pricing while older is gated; after both settle live/persisted/final quote quantity3 wins |

The final two controls do not prove arbitrary cross-session/replica stale-result protection. Existing protected-session/ticket tests remain untouched.

## Historical test-first diagnostics and commands

Initial new compilation had four fixture interface-signature errors, corrected to the actual upload-client contract before test evidence. First executable run was 3 failures/2 passes: **only two genuine RED**, while the material-switch case used unsupported K/White and never reached pricing. That artifact remains `TestResults/material-progress-initial/material-progress-initial.trx`.

Adding actual protected ticket/store readback initially exposed invalid synthetic FDM comparison receipts from the older reusable synthetic helper; all five setup failures remain `TestResults/material-progress-red/material-progress-red.trx`, not product RED. The new test now uses the actual provisional resin kernel directly, preserving unavailable FDM comparisons and avoiding invented receipts. No runtime or old helper was changed.

Final terminal evidence: `TestResults/material-progress-authority-red/material-progress-authority-red.trx`, **3 actual assertion RED / 2 controls GREEN, no errors/skips** after fresh Release0W0E. Baseline artifact: `TestResults/material-progress-baseline/material-progress-baseline.trx`, 49PASS. Full suite is deliberately not run/claimed green while the new regressions are RED; root requested freeze for design review before repair, and separately owns the browser window.

Final RED TRX SHA256: `710031E32B46683237CB2BC57421D058A84F8D6B5F23D59F5D5AB00207AD04ED`. Scoped solution format verification for the NEW test and both new-file redacted gitleaks scans passed; no suppression or package change. `git diff --check` passed. Only the two NEW owned files are untracked; no tracked runtime diff. All owned build/test/static handles are terminal and the candidate is frozen for root review, not accepted GREEN.

### Authorized repair chronology, superseding initial test-only freeze

After root independent initial RED reproduction, two early-boundary tests were added before runtime: false first Put and caller cancellation while that Put is reached. Both fail at stale published Quote after independently observing no pricing, stored prior quantity1 plus retained prior authorization, live changed quantity2 and actual caller token when canceled. Three invalid-material/color/quantity controls pass and preserve valid quote identity before mutation. `TestResults/material-progress-expanded-red/material-progress-expanded-red.trx`: **10 total,5RED5GREEN0skip**, fresh Release0W0E. No forced zero-price or scanner/provider success proof.

The entire runtime repair is `OrderQuote = null;` before the first awaited persistence/pricing boundary in `InstantQuotationWorkflowCoordinator.PersistAndPriceAsync`. Stored request/authorization/CAS, final quote publication, ticket issuance, revision increment and original exceptions/cancellation are unchanged. `TestResults/material-progress-green/material-progress-green.trx`: **10/10 PASS0skip** after fresh Release0W0E.

`TestResults/material-progress-affected/material-progress-affected.trx`: **146 total,145PASS1FAIL0skip**. Exact old failure is `InstantQuotationWorkflowUploadTests.RejectedQuantityRepriceNeverShowsUnverifiedBulkSavings:648`: `afterFailure.Quote!.Quantity` dereferences a quote that the approved policy now invalidates. Root read the entire old method and approved ONLY replacing that stale-quote assertion with null and adding exact quote quantity10 after successful retry. Original configuration quantity10, pending edit, no unverified bulk savings, exception and retry settlement assertions are unchanged. This deliberate view-policy evolution is not hidden as unrelated flakiness or blanket old-test weakening.

Final terminal artifacts: `TestResults/material-progress-policy-green/material-progress-policy-green.trx`, **11/11PASS0skip**, SHA256 `1AD35916944528E7238987E1B96B5FDEEF4AC08E163E33534AD423D519EFA9BD`; `TestResults/material-progress-affected-green/material-progress-affected-green.trx`, **146/146PASS0skip**, SHA256 `67BB81E4F88B9BBA946539EADFFCC2A56A987ED327F87C7E95CE522DA03F54BA`. Expanded 5RED/5GREEN artifact SHA256 `89AC0A8FB14455854C84B42EBA2F593F162FEC2ADC1E0B8E6F2BF671E3D0C8ED`. Final whole solution format verification, all five private `--no-restore` audits (zero vulnerabilities), four redacted owned-file gitleaks scans (zero findings) and diff check passed, no suppressions. All owned handles are terminal. Four-file candidate is frozen for root independent integration/review, not a full-suite or parent380 completion claim.

Actual model/upload replacement was not added here: the restored-session fixture deliberately throws on every upload/remove/finalize operation. Inventing successful provider responses to extend scope would not establish real upload/physical lineage. Existing workflow upload/restore/removal tests are in the relevant suite; future real-upload browser proof remains separately required.

```powershell
$env:MalievWorkspaceRoot='B:/maliev-legacy/.worktrees/web-material-price-progress-20261002/.dependencies'
$env:UseLocalMalievDependencies='true'
$env:GITHUB_ACTIONS='false'
dotnet build Legacy.Maliev.Web.slnx -c Release --nologo
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~RepricingActivityTests|FullyQualifiedName~InstantQuotationQuantityEditStateTests|FullyQualifiedName~InstantQuotationWorkflowUploadTests' --logger 'trx;LogFileName=material-progress-baseline.trx' --results-directory TestResults/material-progress-baseline
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~MaterialComparisonRepricingContinuityTests --logger 'trx;LogFileName=material-progress-authority-red.trx' --results-directory TestResults/material-progress-authority-red
```

On the current repaired candidate use fresh artifact names rather than overwrite the preserved REDs:

```powershell
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~MaterialComparisonRepricingContinuityTests|FullyQualifiedName~RejectedQuantityRepriceNeverShowsUnverifiedBulkSavings' --logger 'trx;LogFileName=material-progress-policy-green.trx' --results-directory TestResults/material-progress-policy-green
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~MaterialComparisonRepricingContinuityTests|FullyQualifiedName~InstantQuotationWorkflowUploadTests|FullyQualifiedName~RepricingActivityTests|FullyQualifiedName~InstantQuotationQuantityEditStateTests|FullyQualifiedName~InstantQuotationSubmissionTests|FullyQualifiedName~AdditiveQuoteTicketServiceTests|FullyQualifiedName~ProtectedGeometryProfileContinuityTests' --logger 'trx;LogFileName=material-progress-affected-green.trx' --results-directory TestResults/material-progress-affected-green
dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore
```

Own clean detached dependency clones: Defaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Both cloned independently with `--no-hardlinks`; all outputs use the absolute private root. No shared sibling outputs are consumed.

## Implemented child repair and remaining parent acceptance

1. Implemented approved child450: invalidate the published view before any changed request can await pricing; on cancel/error keep it unavailable, retaining caller cancellation/original exception behavior and existing protected-store/ticket fences. The one coordinator line avoids old quote/new configuration mixtures across existing consumers; invalid configuration rejection remains before mutation and retains the valid quote.
2. Render pending versus terminal unavailable material states without old numeric values, using actual repricing lifecycle. This may require a narrow presentation-state model and EN/TH resource entries in the workflow, but is not authorization for those changes yet. Distinguish whole-request pending from selected-first incremental completion; do not copy spinner markup as proof of scheduling.
3. For incremental per-material progress, first design an internal revision-bound progress channel from the actual authoritative service. Ephemeral progress must never issue a ticket or authorize submission, and must not replace final complete quoted/ticketed publication. Profile/upload/owner/configuration changes invalidate it. Missing evidence stays unavailable. No browser-derived money authority or geometry fallback.
4. After review, add actual rendered event/HtmlRenderer or existing Playwright harness proof. Later root-authorized exclusive browser window: genuine admitted upload with delayed selected/comparison pricing, EN/TH, 320/375px, material/model/quantity changes, cancellation, late old completions, unavailable profile and keyboard/touch behavior. No such browser acceptance is claimed by this stage.
5. This child has now safely integrated #447/#449 and executed fresh build, focused/affected/full tests, raw coverage, deterministic assets and statics as recorded below. Root independent review and protected-head/exact-main CI remain required. Future parent presentation work requires its own bounded validation; no deployment/provider/persistent writes. Broad #300/#309/#275 and source-owner closure remain separately evidence-bound.

Skills used: MALIEV testing standards, test-driven-development (including writing-good-tests), using-git-worktrees and verification-before-completion. Root created the fallback worktree because the native creator was bound to the read-only original. Exactly one approved coordinator runtime line, one narrowly reviewed old test method and two NEW files belong to this candidate. No commit, push, GitHub/ledger mutation, persistent data/provider action or deployment was made. The existing browser-inclusive full suite was run in the root-assigned exclusive local window; no new browser test or UI feature belongs to this slice.

## Final combined validation — 2026-10-02

All prerequisite outputs were built inside this worktree by the unchanged reviewed `prepare-profile-producer-boundary.ps1`, `prepare-scb-payment-boundary.ps1` and accepted `prepare-member-auth-indexing-proof.ps1`; every producer/seed build reported zero warnings/errors. No sibling outputs were consumed. Producer pins remain Customer `dc090542c02d675f54c3be59e33654dc24ecca9e`, profile Auth `82c8d63dd08677a7f8ccd107c05dd6c9badbfd79`, SCB Accounting `ae0826156b06c34476e95de8c53dfccfcf5a5972`, and member Auth `51afbbd6e2829382a3431338abedccf339de33b1`, with their scripts' immutable Defaults/Contracts pins unchanged.

`npm run ci` completed: 176/176 module tests, then geometry/native suite 565 PASS / 10 existing SKIP / 0 FAIL. These ten pre-existing private-corpus controls reference `CNC_NATIVE_INTERPRETATION_FIXTURES`, unavailable private saved thread evidence or original CAD mounts; no skip/exclusion/override was added. Generated `wwwroot/dist` had no drift. This is not new CNC acceptance.

The earlier #447-base unfiltered full was 2933/2933 PASS, zero skips, 14m34s: `TestResults/material-progress-integrated-full/material-progress-integrated-full.trx`, SHA256 `FDA9EEB91FE1CAEBFF68344435FBDA68B7613FC8DAD96F9985A6AC25821C1BED`. It is retained bounded-base evidence, superseded for final candidate acceptance by the combined #449-base run:

| Final artifact | Actual result | SHA256 |
|---|---|---|
| `TestResults/material-progress-combined-focus/material-progress-combined-focus.trx` | 51 PASS, 0 FAIL/SKIP | `3B70D508DA9CC5419C850449C71A96FB6AE301F5B000C8402F6710A0E3782D7C` |
| `TestResults/material-progress-combined-affected/material-progress-combined-affected.trx` | 186 PASS, 0 FAIL/SKIP | `2B6580950FFCAD9A6F7CC36669FC771E102DD0C58307C551B9DA0A3DDA4C2E6B` |
| `TestResults/material-progress-combined-full/material-progress-combined-full.trx` | 2973 PASS, 0 FAIL/SKIP | `B58BD5379E5B51BAD3EAC89E4EB48BA64A7062CEBD48ADFED576005F8CDB64C6` |
| `TestResults/material-progress-combined-full/b9a6edc6-b12c-4f90-a8f6-2e076ce41ed2/coverage.cobertura.xml` | unexcluded raw coverage | `8DC4BA1CC9761B6AC506CD5FC2BC02D518709698EF80E76631902208B4E70D42` |

Raw line coverage: Web **87.95%**, Application **81.74%**, Infrastructure **85.41%**, AdditiveBenchmark **73.13%**, shared Defaults **11.66%**, Contracts **0%**. No filter, threshold or denominator adjustment. Benchmark/shared-library residual quality gates are not waived by this child.

Reproduction uses the preceding private `MalievWorkspaceRoot`, `UseLocalMalievDependencies=true`, `GITHUB_ACTIONS=false`, and these non-secret absolute process paths:

```powershell
$root='B:/maliev-legacy/.worktrees/web-material-price-progress-20261002'
$env:MALIEV_PROFILE_PRODUCER_DLL="$root/.dependencies/profile-producer/Legacy.Maliev.CustomerService.Api/bin/Release/net10.0/Legacy.Maliev.CustomerService.Api.dll"
$env:MALIEV_PROFILE_SEED_DLL="$root/tools/profile-contract-seed/bin/Release/net10.0/Maliev.ProfileContractSeed.dll"
$env:MALIEV_PROFILE_AUTH_DLL="$root/.dependencies/profile-auth/Legacy.Maliev.AuthService.Api/bin/Release/net10.0/Legacy.Maliev.AuthService.Api.dll"
$env:MALIEV_PROFILE_AUTH_SEED_DLL="$root/tools/profile-auth-seed/bin/Release/net10.0/Maliev.ProfileAuthSeed.dll"
$env:MALIEV_SCB_ACCOUNTING_DLL="$root/.dependencies/scb-payment-boundary/producer/Legacy.Maliev.AccountingService.Api/bin/Release/net10.0/Legacy.Maliev.AccountingService.Api.dll"
$env:MALIEV_MEMBER_AUTH_DLL="$root/.dependencies/member-auth-indexing/auth/Legacy.Maliev.AuthService.Api/bin/Release/net10.0/Legacy.Maliev.AuthService.Api.dll"
$env:MALIEV_MEMBER_AUTH_SEED_DLL="$root/.dependencies/member-auth-indexing/seed-51afbbd-v3/bin/Release/net10.0/Seed.dll"
dotnet build Legacy.Maliev.Web.slnx -c Release --nologo
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~EsdFulfillmentContractTests|FullyQualifiedName~MaterialComparisonRepricingContinuityTests|FullyQualifiedName~RejectedQuantityRepriceNeverShowsUnverifiedBulkSavings' --logger 'trx;LogFileName=material-progress-combined-focus.trx' --results-directory TestResults/material-progress-combined-focus
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~MaterialComparisonRepricingContinuityTests|FullyQualifiedName~InstantQuotationWorkflowUploadTests|FullyQualifiedName~RepricingActivityTests|FullyQualifiedName~InstantQuotationQuantityEditStateTests|FullyQualifiedName~InstantQuotationSubmissionTests|FullyQualifiedName~AdditiveQuoteTicketServiceTests|FullyQualifiedName~ProtectedGeometryProfileContinuityTests|FullyQualifiedName~EsdFulfillmentContractTests' --logger 'trx;LogFileName=material-progress-combined-affected.trx' --results-directory TestResults/material-progress-combined-affected
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --collect:'XPlat Code Coverage' --logger 'trx;LogFileName=material-progress-combined-full.trx' --results-directory TestResults/material-progress-combined-full
```

Final statics, sequentially after full: `dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore` PASS; `dotnet list <project> package --vulnerable --include-transitive --no-restore` for Web/Application/Infrastructure/Tests/AdditiveBenchmark reports zero vulnerable packages; `gitleaks dir <owned-file> --redact --no-banner` for all four files reports zero findings; `git diff --check` PASS. Actionlint/script AST are not applicable to this owned diff because no workflow/script was edited. Final doc was independently rescanned after this evidence-only update. All build/test/static handles are terminal at freeze; root owns independent review/integration/commit/protected CI.

Root independently read all four files and the coordinator/presentation boundary,
built the integrated Release solution with zero warnings/errors, and executed all
186 affected tests with zero failures/skips. Root parsed original full-suite TRX
counters and matched the 2,973-pass SHA-256 above, and read the unexcluded raw
coverage package rates. Root whole-solution formatting and diff checks passed.
Parent PR449 post-main CI36917032908 subsequently succeeded at exact main18adfd9;
publisher36917033227 skipped publication. This child still requires its own
protected exact-head and post-merge acceptance; parent380 remains open.
