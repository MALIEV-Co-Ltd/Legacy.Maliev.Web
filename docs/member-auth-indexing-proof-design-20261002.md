# Authenticated member indexing proof — Web #446

## Scope and current status

Test-only acceptance follow-up to Web #439/#442. Runtime source code is unchanged. Final integrated Release is zero warnings/errors, focused member/public-verifier/public-metadata tests are **88/88 pass, zero skips**, and the unfiltered integrated full suite is **2923/2923 pass, zero skips**. This is intended equivalence GREEN, not a manufactured product RED. The preceding read-only audit found the member crawl-policy behavior present but no authenticated normal-cookie HTTP proof of both renderers emitting exactly one metadata directive and its response header. Root independent acceptance remains separate.

Source owner: `8447c95f3254b7bf14279a19463fb9ac8bdbc972`, parent `4338bb31562ea3fdfe6fa7a8eb7952d0db961c68`. Its four paths are `Maliev.Web.Tests/AccountIndexingSourceTests.cs`, `Maliev.Web/Areas/Member/Pages/Shared/_LayoutMember.cshtml`, `Maliev.Web/Startup.cs`, and `Maliev.Web/wwwroot/robots.txt`. Source lexical counts are not migrated as a false current-page count; retired PayPal behavior is not restored. This slice adds missing normal authenticated HTTP evidence, not global source disposition or deployment evidence.

Original reviewed base: `381885527b809e72475948a5687d3c430212e778`. After all earlier handles terminated, the owned branch was fast-forwarded, preserving its four-file changes, onto accepted Web #445 main `5d74a867e6764a9d80bd455be94af1d66d60bd09`. Root independently verified exact-main CI `36901579816` SUCCESS and canonical/live equality; no unaccepted runtime was cherry-picked.

## Exact boundary

`AuthenticatedMemberCrawlPolicyHttpTests.cs` uses the actual Web `Program`, registered `CustomerAuthenticationClient`, `AccountSessionManager`, `DistributedAccountSessionStore`, cookie handler and `AccountCookieEvents`. No authentication handler, service client, session store, token claims or database repositories are replaced. Web runs Development, not the Testing memory-cache branch. It uses real Redis 8.4 and a fixture-only certificate protecting its persisted DataProtection key ring.

Normal HTTPS TestServer GET `/Account/Login` obtains its actual antiforgery cookie/form token. POST `/Account/Login?handler=Login` reaches actual Auth `/auth/v1/login`. Auth validates a confirmed customer's owner-seeded password hash and creates its durable refresh session. The genuine Secure/HttpOnly/SameSite=Lax Web cookie is retained by the normal test client. Tests unprotect that **returned** cookie through the registered format only for readback; no cookie is manufactured. They resolve the actual stored session and independently hash its genuine refresh token to locate the unique PG `refresh_sessions` row, checking its ID, active family/expiry/kind and the identity's security stamp. Auth's accepted `RsaAccessTokenIssuer` deliberately includes `sid` only for employees, never customers; the tests assert its absence and do not invent a customer claim. Redis cache type/key existence establishes the non-memory storage lane. Tokens/passwords are never emitted in fixture process output or saved as evidence.

The representative `/Member/Account` has no CustomerService, order, notification or other provider reads in either implementation. Its four positive cases are active/retained × EN/TH, each requiring HTTP200, the localized html language, member content, correct renderer marker, exactly one robots meta `noindex,follow`, one header `noindex, follow`, and no service tokens in HTML. The retained route is selected solely by the existing `BlazorRouting:MemberAccountIndex=false` flag.

Separate controls cover authenticated `/membership` and `/member-account` 404 without member header; both anonymous renderers still challenge to the normal login; corruption of a previously returned cookie cannot render private content; deleting its genuine Redis session rejects the original cookie; real antiforgery logout revokes the Auth session/removes the Web session and restores the challenge. This does not claim all member business workflows or every page's authenticated renderer; existing route/static/robots/sitemap matrices remain separate evidence.

## Private producer preparation

NEW `scripts/prepare-member-auth-indexing-proof.ps1` is invoked in one mandatory CI step adjacent to the unchanged original profile preparation, before browser build/required validation. Missing binaries fail rather than skip or auto-build inside a test.

Exact private pins:

| Graph | Pin |
|---|---|
| Actual Auth producer | `51afbbd6e2829382a3431338abedccf339de33b1` |
| Auth ServiceDefaults | `5c5f9479313710fa576f83d3b396442997a2fcf4` |
| CompatibilityContracts | `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7` |
| Web ServiceDefaults | `6ea131df4bcf8d213d7d121cb8c865697bee7420` |

The helper uses its own `.dependencies/member-auth-indexing` clones/outputs; foreign heads/dirty clones fail closed. It generates a test-only seed utility under a versioned private directory, refusing an existing different file. No Web runtime project references Auth EF. It calls the exact owner's `MigrateAsync` for CustomerIdentity, EmployeeIdentity and RefreshSessions, never copied DDL/EnsureCreated/history. The fixture creates three distinct runUUID database names exclusively from its started PG18 Testcontainer connection, passes its container identity/mapped loopback port, requires pooling/multiplexing off and exact names, and validates **all** targets before migration. There is no ambient connection input. Synthetic confirmed customer `DatabaseID=1` uses the owner's row type and normal PasswordHasher, random credential/security/concurrency stamps. No sessions are fabricated: actual login creates them.

Only the fixture's disposable endpoints are started/migrated. No provider email, IAM grant, real customer data, cloud, persistent database, deployment or source writes occur. Process stdout/stderr are drained but suppressed; subprocess startup/seed/termination are bounded. Setup failure tears down owned children and containers. Auth and seed child environments are cleared after copying only the reviewed OS/runtime essentials, then receive only their explicit disposable dictionary; `LEGACY_DEPLOY_ENABLED=false` is forced last. No ambient connection/provider/cloud/token/telemetry settings propagate.

The final v3 generated utility adds an executable environment probe before any context/migration. The new control injects hostile connection/JWT/provider/cloud/telemetry/deployment variables into the fresh child-owned inherited dictionary, **not** the global environment shared with other tests. That actual child enumerates every received variable against an independent literal essentials/probe allowlist and checks deployment is disabled. Its zero exit proves the settings cannot reach that executable; the normal real Auth login cases also pass under the same sanitizer. This is a test infrastructure isolation proof, not provider readiness or a production configuration change. Existing v1/v2 helpers remain untouched ignored diagnostic artifacts.

## Validation chronology (not product RED)

Initial seed build revealed an MSB3277 EF Relational 10.0.4/10.0.12 conflict. The generated v1 helper is retained ignored as diagnostic; v2 explicitly references EF Relational10.0.12 and refuses overwrite. Auth and corrected helper Release builds each passed zero warnings/errors. Initial Web compilation exposed three xUnit analyzer errors and a nullable fixture host assignment; those fixture-only diagnostics were corrected before any test claim. Fresh Web Release subsequently passed zero warnings/errors.

Initial `member-initial-focus/member-focus.trx`: 12 total, 1 pass / 11 setup failures from missing unused nonTesting Recaptcha options. `member-configured-focus/member-focus.trx`: 12 total, 3 pass / 9 fixture assertion failures because RedisCacheImpl is a legitimate RedisCache subclass. `member-authority-focus/member-focus.trx`: 16 total, 7 pass / 9 incorrect customer-sid expectation failures. These are fixture diagnostics, not product REDs. The four helper adversarial controls already passed in that last run, proving changed database/host/pooling/port rejects all endpoints before any public relation is created.

Fresh Release after the corrections: zero warnings/errors. `TestResults/member-customer-lineage-focus/member-focus.trx` and `member-final-focus/member-final-focus.trx`: **16/16 pass, zero skips** (four actual authenticated renderer/culture cases, seven route/auth/session controls, one mandatory CI structure control, four helper authority controls). No runtime repair was needed.

Unfiltered `TestResults/member-full/member-full.trx`: **2854/2854 pass, zero skips**, 12m28s, SHA256 `03BF740B2E71A19AACD7D6C8BDD2B83BC0D54E566541B57EDFF3496F12632807`. Raw coverage: `TestResults/member-full/e1780aaf-fc59-458d-991e-b8b48226f388/coverage.cobertura.xml`, SHA256 `3C810ACEC2C79E145649D220278CBEDC87B244DC06E6BE7D908806CA1896541A`. Unexcluded line rates: Web87.95%, Application81.64%, Infrastructure83.16%; AdditiveBenchmark73.13%, ServiceDefaults11.66%, CompatibilityContracts0%. No denominator/exclusion/threshold was changed, and the residual assemblies are not represented as an all-project80% pass.

After that full, root required fixed-message assertions so even a privacy or cookie assertion failure cannot print ephemeral credentials/cookie/private HTML, preserving the same predicates. Fresh Release0W0E and `member-redacted-focus/member-redacted-focus.trx` **16/16 pass**. Root then required the child environment isolation/control above: actual Auth and v3 utility builds0W0E, fresh Web Release0W0E and `member-isolated-focus/member-isolated-focus.trx` **17/17 pass, zero skips**. The preceding 2854 full is explicitly **pre-refinement/hardening and pre-integration evidence**.

Final integrated candidate on accepted `5d74a867e6764a9d80bd455be94af1d66d60bd09`: fresh private Release **0 warnings/0 errors**; `TestResults/member-integrated-focus/member-integrated-focus.trx` **88/88 pass, zero skips** (member17 plus public-document verifier/public-metadata controls); `TestResults/member-integrated-full/member-integrated-full.trx` **2923/2923 pass, zero skips**, 12m32s, SHA256 `64790A739DBA761FE64E44D2FFD68033D06909564666E5B13122BD0D1107B7A3`. Raw coverage `TestResults/member-integrated-full/2ebbe0e9-9b93-4876-872a-c5745f30e5bf/coverage.cobertura.xml`, SHA256 `5DB8F3580E7EB40000CFDBD0D7B608769E19F55777FD1EAFFDE6CC6A403D5C22`: Web87.96%, Application81.69%, Infrastructure83.16%, AdditiveBenchmark73.13%, ServiceDefaults11.68%, CompatibilityContracts0%. These are unexcluded actual line rates; no all-assembly80% claim, filter/denominator waiver or synthetic data parity claim. The global local-browser window was released after full termination.

All five unchanged historical boundary builds (Customer/Auth/two seed/SCBAccounting) passed0W0E under owned private graphs. `npm run ci` passed: browser modules176/176 (zero skips), unchanged numerical engine575 total565 pass10 preexisting skips0 failures. These JS skips remain disclosed, not reclassified as .NET full skips or waived. Committed assets are unchanged. Solution formatting, workflow actionlint, PowerShell AST, five package vulnerability audits and scoped secret scans pass; no new scanner suppression.

One stopped audit diagnostic accidentally omitted private MSBuild environment and restored ignored shared `.worktrees/Legacy.Maliev.ServiceDefaults`/CompatibilityContracts obj metadata. Root was notified immediately; no shared files were reverted or treated as proof. Read-only status subsequently confirmed both source trees tracked-clean. Corrective Release restored the owned explicit private graph0W0E, and all five audits were repeated with `--no-restore`/explicit private environment and reported zero vulnerabilities. This mistake is not concealed as isolated validation.

Existing profile helper/pins/tests, runtime, schema, permissions and all public URLs remain untouched. Four owned files only: this new document, the new test/fixture class, the new preparation script, and one three-line mandatory CI step. No commit/push/ledger disposition belongs to this agent; root independently reviews and integrates the frozen candidate. Skills used: authentication-systems, MALIEV testing standards and verification-before-completion; the mocked-auth shortcut was deliberately not used because this acceptance requires actual authority.

Final integrated static lane terminated exit0: whole solution format verification, actionlint on the changed workflow, new helper AST with zero errors, five private `--no-restore` vulnerability audits with zero findings, four scoped redacted gitleaks scans with zero findings/no suppression, `git diff --check`, and unchanged committed dist assets. All owned build/test/static handles are terminal. Root receives the frozen four-file candidate and private outputs for independent validation; no commit was created.

## Reproduction (owned workspace only)

Use the assigned workspace as cwd and set the actual **absolute** private root before every build/format/audit/test:

```powershell
$env:MalievWorkspaceRoot = 'B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/.dependencies'
$env:UseLocalMalievDependencies = 'true'
$env:GITHUB_ACTIONS = 'false'
./scripts/prepare-member-auth-indexing-proof.ps1
./scripts/prepare-profile-producer-boundary.ps1
./scripts/prepare-scb-payment-boundary.ps1
dotnet build Legacy.Maliev.Web.slnx -c Release --nologo
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~AuthenticatedMemberCrawlPolicyHttpTests --logger 'trx;LogFileName=member-isolated-focus.trx' --results-directory TestResults/member-isolated-focus
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=member-full.trx' --results-directory TestResults/member-full --collect:'XPlat Code Coverage'
dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore
actionlint .github/workflows/_build-and-test.yml
```

Preparation exports seven mandatory non-secret paths, all under this owned workspace: `MALIEV_MEMBER_AUTH_DLL` (`.dependencies/member-auth-indexing/auth/...Api/bin/Release/net10.0/...Api.dll`), `MALIEV_MEMBER_AUTH_SEED_DLL` (`.dependencies/member-auth-indexing/seed-51afbbd-v3/bin/Release/net10.0/Seed.dll`), the unchanged four `MALIEV_PROFILE_*` paths from their existing script, and unchanged `MALIEV_SCB_ACCOUNTING_DLL`. Run `npm run ci` inside `Legacy.Maliev.Web` before the final build/full; existing Chromium is the ordinary browser prerequisite. Five private `dotnet list <project> package --vulnerable --include-transitive --no-restore` audits target Web/Application/Infrastructure/Tests/AdditiveBenchmark. Parse the new PowerShell script with `System.Management.Automation.Language.Parser.ParseFile`; scan each of the four owned files with `gitleaks dir <file> --redact --no-banner`, then `git diff --check` and committed-dist diff. No test auto-build or skip is permitted.

Already-built private outputs for root's independent build/focus (no foreign graph rebuild required):

```powershell
$env:MALIEV_MEMBER_AUTH_DLL = 'B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/.dependencies/member-auth-indexing/auth/Legacy.Maliev.AuthService.Api/bin/Release/net10.0/Legacy.Maliev.AuthService.Api.dll'
$env:MALIEV_MEMBER_AUTH_SEED_DLL = 'B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/.dependencies/member-auth-indexing/seed-51afbbd-v3/bin/Release/net10.0/Seed.dll'
$env:MALIEV_PROFILE_PRODUCER_DLL = 'B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/.dependencies/profile-producer/Legacy.Maliev.CustomerService.Api/bin/Release/net10.0/Legacy.Maliev.CustomerService.Api.dll'
$env:MALIEV_PROFILE_SEED_DLL = 'B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/tools/profile-contract-seed/bin/Release/net10.0/Maliev.ProfileContractSeed.dll'
$env:MALIEV_PROFILE_AUTH_DLL = 'B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/.dependencies/profile-auth/Legacy.Maliev.AuthService.Api/bin/Release/net10.0/Legacy.Maliev.AuthService.Api.dll'
$env:MALIEV_PROFILE_AUTH_SEED_DLL = 'B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/tools/profile-auth-seed/bin/Release/net10.0/Maliev.ProfileAuthSeed.dll'
$env:MALIEV_SCB_ACCOUNTING_DLL = 'B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/.dependencies/scb-payment-boundary/producer/Legacy.Maliev.AccountingService.Api/bin/Release/net10.0/Legacy.Maliev.AccountingService.Api.dll'
dotnet build Legacy.Maliev.Web.slnx -c Release --nologo
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthenticatedMemberCrawlPolicyHttpTests|FullyQualifiedName~PublicDocumentVerifierQuotationHttpTests|FullyQualifiedName~PublicOpenGraphMetadata' --logger 'trx;LogFileName=member-integrated-focus.trx' --results-directory TestResults/member-integrated-focus
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=member-integrated-full.trx' --results-directory TestResults/member-integrated-full --collect:'XPlat Code Coverage'
```
