# reCAPTCHA configuration candidate — 2026-09-30

Scoped tracking: Web issue #417 (parent #148), covering required public identifiers, provider-log redaction and configuration-only test fixture alignment. Root independent acceptance and any PR remain pending; this does not close the five-SHA provenance cohort or source-owner configuration/workload-identity gates.

Base: exact Web main `dcc9bb8d6a27483a839059d08ec87bcd74f0d46d`; isolated branch `codex/web-recaptcha-preflight-20260930`. No canonical/original/CNC/GitOps/ledger/configuration-secret/deployment changes, commits or pushes are authorized. Post-main CI `36702124535` is independently verified completed/success at this exact SHA; image publication `36702124902` also succeeded. Local validation is complete for the bounded candidate; root independent acceptance remains required.

## Source evidence and explicit boundaries

Original committed Git objects were read without checkout, execution or private-configuration copying:

| Source SHA | Parent(s) | Relevant contract / remaining gate |
| --- | --- | --- |
| `a40ae59b9a9cc4182ed397eb02f18f861180c51f` | `5abf77a1354de36a189e3e5f60056152ba810270` | Embedded credentials and TLS bypass removed; Maps/TLS/security owner evidence is read-only in this lane. |
| `b6c8c6a18f2a782b59f988c24f5e48a30c51cfe0` | `a40ae59b9a9cc4182ed397eb02f18f861180c51f` | `ProjectID`, `KeyID`, optional `CredentialsPath`; mounted-file provider or ADC; configurable Maps key. Legacy path adaptation remains OPEN. |
| `79fa2e5c55a384b543a2aee11ab917119f84182b` | `7d6f46f53cbab853ca9c25e385af067cfff6238a` | Required identifiers and readable absolute compatibility file validated on startup. Candidate restores required target identifier startup checks only. |
| `eb8ed86672bd9afccc6560b547b734d0fcd7363b` | `6da103d234ea312ce3b404fefaed343c172517b1`, `868b5909406cc3759254ee10520e04d4ad5beab0` | Broad wave-0 security merge, not a claim that all services/configurations are migrated by this Web change. |
| `f80b4b05fd8785814c441a7edddbece2aec3c425` | `c26acfa938067b22018ec38327529d4cf9d04b06` | Dedicated reCAPTCHA Workload Identity declaration plus source assessment contracts; deployment identity readiness is not verified here. |

Current target binds only `Recaptcha:ProjectId`, `Recaptcha:SiteKey`, `MinimumScore`. `ProjectID` is case-insensitive equivalent, but source `KeyID` requires explicit mapping to target `SiteKey`. Source `Recaptcha:CredentialsPath` is not consumed by target options/provider; old embedded `Recaptcha:ServiceAccount` is also unsupported. Their unknown keys are currently ignored by normal binding. This is source-inspected behavior, not a newly approved migration policy. No arbitrary rejection/alias/custom credential loader has been added to manufacture complete parity. Historical completion documentation and all five-SHA cohort completion remain subject to root owner review.

The target `GoogleRecaptchaAssessmentClient` retains its unchanged lazy `RecaptchaEnterpriseServiceClient.CreateAsync()` ADC path. Resolved Google.Apis.Auth is 1.72.0; its shipped XML documentation confirms standard `GOOGLE_APPLICATION_CREDENTIALS` points to an ADC credential file. Mounted-file compatibility therefore remains available through that native environment mechanism. No credential file is opened by these tests; no Google credential discovery or API request is invoked; no Google credential/process environment is changed. An explicit owner-approved configuration transition from legacy `CredentialsPath` to native ADC remains OPEN.

## Candidate contract

- A registered `IValidateOptions<RecaptchaEnterpriseOptions>` validator requires nonblank ProjectId and SiteKey and runs through normal `ValidateOnStart`. Diagnostics contain field names only, not configuration values.
- Actual host startup with missing/empty/whitespace identifiers must fail before serving requests; configured startup retains real DI registrations and does not eagerly acquire ADC credentials.
- Assessment validity/action/minimum-score behavior and exact project/site-key/token/action forwarding are preserved; empty inputs still fail closed before the external assessment boundary.
- Provider failures return false and emit a constant warning without attaching the original exception or token/PII payload. Caller-requested cancellation still propagates without logging; noncaller cancellation remains a fail-closed provider failure.

Startup tests invoke the actual Web Program using its existing Testing environment (memory session state), with ephemeral data protection; only identifiers are controlled through per-factory configuration. They do not replace the production reCAPTCHA provider or install an authentication shortcut. Verifier tests replace only the external assessment transport, with synthetic results/exceptions.

## Evidence so far

Dependencies are clean no-fetch local clones in ignored `TestResults/.dependencies`: ServiceDefaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`, CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.

Build command: `dotnet build Legacy.Maliev.Web.slnx -c Release --nologo -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-recaptcha-preflight-20260930/TestResults/.dependencies`.

- Baseline build 0 warnings/0 errors; existing reCAPTCHA/configuration tests 7/7, no skips.
- Real-host startup RED: six missing/blank cases each failed because **no exception was thrown**; configured host/verifier/lifecycle/credential tests passed (final pre-repair run 18 passed/6 failed/0 skipped).
- Logging RED: two synthetic provider exception/cancellation cases leaked their fake token/email/phone through the attached exception; caller cancellation passed (1 passed/2 failed/0 skipped).
- Approved runtime candidate Release build: 0 warnings/0 errors; focused startup/verifier/lifecycle/credential checks 27/27, no skips. TRX: `Legacy.Maliev.Web.Tests/TestResults/recaptcha-focused-green.trx`.
- Existing About SSR fixture RED: 1 passed/3 failed; independent FDM submission/Redis fixture RED: 4 passed/37 failed, all host failures due to missing SiteKey. Root approved config-only synthetic identifiers in the shared Testing factory, FDM factory (reuse shared defaults), and Redis integration factory. Caller overrides retain precedence; no Testing validation exemption, authentication/CNC/Redis behavior change or credential environment mutation was introduced. Expanded final focused lane: 72 passed/0 failed/0 skipped; final Release build 0 warnings/0 errors (14.74 seconds).
- Changed-file formatting verification and `git diff --check` pass at this checkpoint.

The unchanged full suite's profile producer fixture preparation uses ignored local no-fetch clones: Customer `dc090542c02d675f54c3be59e33654dc24ecca9e` + Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`; Auth `82c8d63dd08677a7f8ccd107c05dd6c9badbfd79` + Defaults `5c5f9479313710fa576f83d3b396442997a2fcf4`; both Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. All heads/cleanliness were checked before invoking the existing preparation script so it required no fetch. Four noncredential DLL locator variables are exported only inside the owned test-launch child process; no ambient credential environment or hosted environment-file projection is changed. Existing boundary tests use only disposable PostgreSQL/Redis and bounded child service processes.

Final local validation is recorded below; root independent review remains required before acceptance, commit or PR.

First full run is preserved at `TestResults/recaptcha-full/recaptcha-full.trx`: 2,421 passed / 39 failed / 0 skipped, 2,460 total, 11m25s. All failures are the same missing-SiteKey startup prerequisite across the four pre-existing bare-host instant-quotation static SSR/runtime/analytics/interactive test classes. A read-only inventory of all test hosts found no additional real-Program host prerequisites. Root approved only synthetic ProjectId/SiteKey `UseSetting` additions to those four constructors; environment, authentication, runtime and analytics configuration remain unchanged. Final rebuilt Release is 0 warnings/0 errors (5.83 seconds); expanded focused rerun is 126 passed / 0 failed / 0 skipped (39 seconds), recorded in `Legacy.Maliev.Web.Tests/TestResults/recaptcha-final-focus.trx`.

Gitleaks `stdin --redact --no-banner --report-format json` scanned the complete contents of all 13 changed/untracked candidate source/doc files: no leaks (130,075 bytes at that checkpoint). Ignored clones, binaries, coverage, browser captures and TRX outputs are noncommittable; none belong in the candidate diff.

## Final suite and residual gates

### Independent root acceptance

Root reviewed all 13 files and the actual Google provider, validator registration,
startup factories, fixture override precedence and cancellation/logging contract.
The exact-pinned Web Release rebuild passed with zero warnings/errors (30.11s).
All six isolated producer clone heads and cleanliness matched the preparation
script before invocation; its four Release builds passed with zero warnings/errors.
The focused startup/verifier filter passed 23/23 with zero skips, followed by the
complete affected suite: 2,460/2,460, zero failures/skips (12m7s).
Durable root results are ignored `TestResults/root-recaptcha/root-focused.trx`
and `root-full.trx`. These do not establish credential acquisition, deployed
Workload Identity, current-data Aspire parity or complete source-owner resolution.

`dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --collect:'XPlat Code Coverage' --results-directory TestResults/recaptcha-full-final --logger 'trx;LogFileName=recaptcha-full-final.trx'` ran after the identical exact/clean producer preparation in the owned child process: **2,460 passed / 0 failed / 0 skipped**, 9m30s. This includes 139 browser-named results plus existing route/SEO/analytics/auth/health and disposable actual-producer boundary tests; no new live Google integration is claimed.

Coverlet: validator and verifier 100% line/branch; Web Infrastructure 82.62% line, Application 80.66%, Web 87.63%. Raw aggregate is 74.74% (24,543/32,837 lines), including dependency ServiceDefaults 11.66%, Contracts 0%, and benchmark 72.82%; this is not a claim of global 80% coverage. Full TRX and Cobertura are in ignored `TestResults/recaptcha-full-final`.

Final changed-file `dotnet format Legacy.Maliev.Web.slnx --no-restore --verify-no-changes --include <12 changed C# paths>` and `git diff --check` pass. `scripts/verify-complete-source-history-parity.ps1 -ManifestOnly` reports exact 159-entry manifest, source comparison not run, source writes false. It validates existing ledger structure only, not five-SHA migration completion.

`dotnet list Legacy.Maliev.Web.slnx package --vulnerable --include-transitive --no-restore` reports no vulnerable packages for Application, Infrastructure, Tests or Web against NuGet's current metadata. Final evidence readback and redacted changed-file secret scan pass. Container-image build/health, real Google ADC credential acquisition/assessment, mounted-file credential execution, restricted public-ID validity, deployed Workload Identity and original-source tests are deliberately not run; these need owner-approved environment/deployment checks and cannot be inferred from local fixture startup. Required identifiers must be present in target deployment configuration before adoption; source `KeyID`/`CredentialsPath` transition remains an explicit owner gate. No commits or external state changes are made by this lane.
