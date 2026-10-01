# SCB payment service boundary — TEST/DESIGN candidate

This is the bounded [Web#433 portable payment proof](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/433) under Web#280, not a payment activation or authoritative-account reconciliation. No product runtime changes are authorized. Parent280/source7387, Accounting#22, DataMigration#201 and shared IAM remain separate open data/evidence gates.

## Exact source and current dispositions

- Source `7387d9e88e4f1b1b254c48f5ab0933ec3bbf20d3`: `Maliev.Web/Areas/Member/Pages/Quotations/View.cshtml`, `Maliev.Web/Resources/Areas/Member/Pages/Quotations/View.th.resx`, `Maliev.Web/Pages/Shared/_SupportedPaymentsPartial.cshtml`, source localization tests, and `Maliev.SqlServer/Deployments/2026-09-22-scb-payment-account.sql`.
- Committed source mirror checkpoint `bed10c7d15e0698e0b75f1329d0f312937f5d77f`; no original fetch/write/provider operation.
- Web display migrated by PR287 at `85bbefdd380f278ed949b05433d3055c2a602847`: bank/company localization, unknown Branch/Swift omission, typed service account-number projection and SCB sprite. This does not prove the source account update ran or the current PostgreSQL destination.
- Owned detached Web baseline `e25168e76f829b147bb5ad415ffdbf570e19a574`, equal observed canonical/live main. Untouched Release build passed zero warnings/errors; untouched full suite passed **2706/2706**, zero skips, in 11m51s (`TestResults/scb280-baseline/baseline.trx`).
- Accounting immutable producer `ae0826156b06c34476e95de8c53dfccfcf5a5972`. Its existing PaymentAccountContractTests prove repository/controller nullable wire projection with disposable PG, not normal HTTP IAM authorization.

## Graph and fixture ownership

After explicit root portability review, the candidate owns four paths: new `Legacy.Maliev.Web.Tests/ScbPaymentServiceBoundaryTests.cs`, this document, new `scripts/prepare-scb-payment-boundary.ps1`, and one required preparation step in `.github/workflows/_build-and-test.yml`. No Web project/package/solution references were added. No product runtime or existing-test edits. Existing workflow validators were read completely; none required an assertion change.

Private ignored paths under this worktree:

- `TestResults/.scb280/producer`: clean detached Accounting at the exact SHA above; unmodified API binary built separately.
- `TestResults/.scb280/runtime`: clean Accounting CI Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3` and Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.
- `TestResults/.scb280/results`: owned results. A separate ignored seed harness is authorized if needed, but none has been introduced for the initial before-DB authorization characterization.
- Existing Web `TestResults/.wall-dependencies`: Defaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Existing ignored profile producer proofs remain intact.

Reproduce the private Accounting build (after independently obtaining the exact clean clones):

```powershell
dotnet build TestResults/.scb280/producer/Legacy.Maliev.AccountingService.Api/Legacy.Maliev.AccountingService.Api.csproj -c Release --nologo -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-wall-browser-acceptance-20261001/TestResults/.scb280/runtime
```

Set `MALIEV_SCB_ACCOUNTING_DLL` to that absolute API binary. The fixture starts that unmodified Program in **Production**, disposable PostgreSQL18 and an owned loopback controlled IAM HTTP endpoint. It uses ephemeral RS256 keys, exact fixture issuer/audience, no authentication handler replacement, no IIam DI replacement, no production credentials or provider requests. Tokens and process/provider logs are not emitted. Default local service permission opt-in remains explicitly false.

## Actual registration contract and early characterization

Accounting Program calls `AddLegacyAuthServiceTokenExchange` and `AddJwtAuthentication`; it does not call `AddIAMServiceClient` or `AddAuthServiceIAMClient`. Exact pinned Defaults' legacy exchange registers outbound workload-token services, not `IIamServiceClient`. Its permission handler accepts an optional IAM client; forced-live checks do not fall back to ordinary token permissions. `GET payments/accounts` requires authenticated `legacy.accounting.read` with `RequireLiveCheck=true`.

Consequently merely setting `Services:IAMService:BaseUrl` and `IAM:LivePermissionChecks:Credential` cannot install the missing client. The intended controlled endpoint returns allow, but the initial tests must truthfully characterize whether it is contacted. Anonymous/wrong-signature/wrong-issuer/expired credentials expect401; missing/exact read claims expect403 in this unmodified default-off registration. Default denial is not automatically a security bug. A desired normal live-IAM positive contract needs a separately reviewed producer-owned registration design, not a fake proof host or a Web workaround.

The initial six cases assert HTTP status, no account-number payload disclosure, zero controlled IAM calls, and unchanged public-table count in the actual disposable PG. Table-count equality is a limited shape/no-DDL control, **not** proof of unchanged populated account values or full storage atomicity. No production account values are seeded, copied or logged.

## Executed consumer controls and diagnostic classification

Initial actual-HTTP six cases passed in `TestResults/.scb280/results/early-http/early-http.trx`. After root review, the new fixture was changed to shared `IClassFixture` lifecycle with one owned PG/Production subprocess per class, initialization-failure cleanup and bounded owned loop/process disposal. The actual default-off permission flag is `Features:AllowExactServiceClaimsForLiveCheck`; it is explicitly false. The first six-case run left its default false; the final fixture explicitly sets the exact name.

Ten cases passed in `consumer-denial/consumer-denial.trx`: six normal-HTTP controls; two actual producer403 → real member client → real HTML renderer EN/TH warning/empty bank projection/no destination/no bearer output; and two controlled paid/wrong-owner invoice preconditions that make no account request. The quotation/invoice preconditions are synthetic, not a joined invoice workflow.

Seven additional distinct controlled loopback HTTP cases exercise the real consumer: synthetic non-payable PascalCase bank account with Branch/Swift omitted, localized EN/TH bank/company and null metadata omission;503, malformed JSON, invalid field shape and empty array; caller abort after the actual account request is received and response is held behind a cancellation gate. These positive account responses are **not** unmodified Accounting PostgreSQL/IAM successes. They are explicitly consumer wire/render tests. Existing missing-invoice and ownership test surfaces remain unchanged; a new missing-invoice case is not claimed.

The first17 run retained `controlled-wire/controlled-wire.trx`:16pass/1fixture cleanup error. The caller-cancellation assertion reached, but the controlled listener was closed before its held context was aborted, producing `ObjectDisposedException`. Only new fixture cleanup ordering was corrected. Fresh Release0warnings/0errors followed; `controlled-wire-corrected/controlled-wire-corrected.trx` passed17/17, zero skips. This fixture diagnostic is **not** a genuine product regression RED. No runtime repair is proposed from these results.

## Implemented portability slice

Root approved `scripts/prepare-scb-payment-boundary.ps1` and one required preparation step before the existing Web build. The helper obtains immutable Accounting `ae0826156b06c34476e95de8c53dfccfcf5a5972` and its exact8f4/78e runtime clones under the workspace-confined ignored `.dependencies/scb-payment-boundary` subtree, rejects dirty/wrong-origin/wrong-commit/missing prerequisites, builds only the unmodified Accounting API separately with Release/warnings-as-errors/shared-compilation disabled, and exports its verified binary through `GITHUB_ENV`. Verification-only never substitutes for the mandatory CI build. No Web test/production project references or seed/Data-reference project were introduced.

The helper uses the existing profile-helper HTTPS clone convention. No new token/secret/permission is introduced; clone credentials are not persisted in local helper configuration, and checkout remains detached. Existing workflow checkout steps retain `persist-credentials:false`, all prior validation steps and CI pins remain unchanged. Actual helper execution built the producer and private dependencies at Release0warnings/0errors without starting any database/service. Final tests use its exported binary, not the earlier manually prepared private binary.

Six executable negative preparation controls copy the actual script into unique owned ignored roots: outside directory, missing dependencies, dirty tree, wrong immutable head, wrong origin and missing binary all must terminate nonzero instead of skipping. The required-workflow guard parses the one step's exact shell/run properties and ordering before Web build; forbids conditional/fail-open/override properties and `-VerifyOnly`. No historical validator assertion is waived.

Retained portability setup diagnostics: `portable-focus` and `portable-focus-corrected`27pass/4setupfail; `preparation-diagnostic` and `preparation-cause`2pass/4setupfail. The captured cause was Windows Git `'$GIT_DIR' too big` during a disposable nested clone, not a product or helper authorization failure. The new fixture uses shorter owned roots and `.dependencies/p`. `portable-focus-shortpaths` and the historical first `focus-final`30pass/1setupfail identified the shallow clone's missing parent traversal/object. The wrong-head control now copies the existing parent commit object `0ec928ee470e29777151e8028b3f300a93f5b538` explicitly from the owned local prepared clone into its disposable clone before checkout; no new commit, original modification or provider/network request creates that vector. These diagnostics are not product RED evidence.

## Frozen candidate validation

Final fresh direct test-project Release build passed **0 warnings / 0 errors**, with warnings-as-errors and shared compilation disabled, using the absolute owned Web dependency root. The portable helper separately built the unmodified Accounting API and its exact private dependencies at Release0warnings/0errors; final test runs used that helper's exported binary.

- Combined focused contracts: **31/31 PASS**, zero skips (24 new cases and seven unchanged existing controls), `TestResults/.scb280/results/focus-accepted-candidate/focus-accepted-candidate.trx`.
- Complete affected Web suite: **2730/2730 PASS**, zero skips, 11m13s, `TestResults/.scb280/results/full-final/full-final.trx`. This preserves all2706 baseline cases plus24 new controls.
- Whole solution `dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore`: PASS with `GITHUB_ACTIONS=false`, `UseLocalMalievDependencies=true` and absolute owned `MalievWorkspaceRoot`. An initial invocation incorrectly passed an unsupported `-p:` option and exited before validation; the corrected environment-based invocation is the acceptance check.
- Five unexcluded package audits (Application, Infrastructure, Web, Tests and additive benchmark), each `dotnet list <project> package --vulnerable --include-transitive --no-restore`: no vulnerable packages reported by the current NuGet sources.
- `actionlint`, PowerShell AST parsing, pinned Workflows `Invoke-JwtSigningResourceScan.ps1`, all four candidate-path redacted Gitleaks scans, repository-history redacted Gitleaks (511 commits), and `git diff --check`: PASS. No signing resources, secrets or new suppression markers were added. The pinned validator does not contain the later current-tree credential script; the explicit candidate-path scans cover the new untracked files instead.

The final graph's five private checkouts were re-observed clean at their exact pins. No existing assertion, runtime file, project reference, DTO, package, browser asset or payment destination changed. No schema/data reconciliation or real IAM positive acceptance can be inferred from the GREEN controls. The candidate is ready for independent root validation, not claimed as whole parent280/source-owner acceptance.

No source SQL deployment, PostgreSQL persistent reconciliation, IAM grants, provider/payment execution, CNC expansion, schema/runtime activation, deployment, commit or push is part of this slice. No full Aspire or production workflow acceptance claim.

## Independent root gate

Root direct Release0warnings/0errors and new focused24/24PASS0skip. All five audits, whole-format, actionlint, PowerShell parse, pinned signing scan, four scoped redacted scans and diffcheck passed. Fresh exact private Customer/Auth APIs and both seed helpers built0warnings/0errors before full.

Initial independent full executed2730:2729PASS/oneFAIL/zeroSKIP, retained `TestResults/root-scb433-full/natth_MALIEV-31USFIV_2026-10-01_18_17_23_net10.0.trx`. The unchanged CompactControlReachability browser case timed out in WaitForFunction. A concurrent independent sitemap suite also timed out in an unchanged browser Goto; observed freeRAM2871/32604MB supports an environment-pressure hypothesis, not a proven cause or waived failure. Both runs terminated before serial acceptance started. Root is running the identical candidate serially with an explicit TRX filename; no timeout, assertion, skip or browser-test source changed. No final root full acceptance or commit is claimed at this pending checkpoint.

The serial root run subsequently passed2730/2730, zero failed/skipped,11m1s: `TestResults/root-scb433-serial-full/root-scb433-serial-full.trx`. All2706 old cases and24new cases are preserved. No test/runtime/timeout change occurred between the failed concurrent attempt and this successful serial run. Fresh helper graph builds again had zero warnings/errors. This supersedes the pending root local full gate above; protected-head/post-main CI and merge remain required.
