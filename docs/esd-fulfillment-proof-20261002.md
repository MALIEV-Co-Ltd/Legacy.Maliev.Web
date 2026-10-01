# ESD fulfillment transport proof

## Root integration and current candidate validation

Root integrated merged PR447 main `d2165c69afc27f2084a31fe06bf2d5f75373321a`
at `679af5c7d0cd907a0dc45de7b7e710fc7a7ea9d6`. Its actual mandatory private
member-Auth helper built cleanly; fresh solution Release passed zero warnings/errors.
Combined ESD/real-Auth57 focused regressions passed, zero skips. The current
integrated unfiltered suite passed2963, zero failures/skips,12m59s. Complete original
`TestResults/root-esd-integrated-full/integrated-full.trx` was independently parsed;
SHA256 `0879BF9CCF62DDF97AAA9D58358D83ECC55FAD15E1BF24F2CE948C4D7FC031E6`.
Whole formatting, actionlint and whitespace passed. This supersedes the historical
pre-integration2946 result below, which remains separately preserved.

PR449 still requires its fresh exact-head CI and post-main acceptance. PR447's
post-main gate remains pending at this checkpoint; no application publication,
physical calibration, joined persistence or whole-source-owner closure is claimed.

## Scope and provenance

This bounded [Web #448 currency-failure slice](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/448)
is a follow-up to still-open Web #418. Its handed-off baseline is
`5d74a867e6764a9d80bd455be94af1d66d60bd09`, not the later #447 integration.
Protected PR integration/CI must include #447 and its mandatory helper; these
local results do not claim acceptance of current global main. It retains separately:

- Behavior source `7b4703576cf183abc09cf558148b5c8afb97d20c`, parent
  `4198baa6b0e7903f2b9b6e3d5d68f9d2c2b5b0db`.
- Merge source `bed10c7d15e0698e0b75f1329d0f312937f5d77f`, parents
  `4198baa6b0e7903f2b9b6e3d5d68f9d2c2b5b0db` and
  `7b4703576cf183abc09cf558148b5c8afb97d20c`. Its tree equals the behavior commit.

The source ESD/catalog/profile slice is already merged through Web PR420 and
Catalog PR30. This follow-up does not downgrade the current v11 pricing policy
to source v9, transfer historical PC-ESD benchmark approval, rename persisted
materials, enable producer reconciliation, or resolve either complete source SHA.

## Executed boundary and genuine defect

The new tests invoke actual `CustomerOrderCatalogClient`,
`InstantQuotationFulfillmentClient` and `CustomerOrderSubmissionTransport`.
Only the external HTTP handler/token boundary is controlled. Country, customer,
authentication and notification dependencies throw if unexpectedly invoked.

Six success cases cover PA612-ESD and ABS-ESD with Standard, Quality and Strength.
Recording HTTP sees actual serialized order JSON, service bearer headers,
idempotency keys, catalog routes, owned-order readback and encoded file-link URL.
Assertions preserve resolved material IDs, FDM, Black, As printed, THB, quantity,
commercial fields, v11 policy/Thai-English notes and safe payment/cancellation
defaults. A historical PC-ESD decoy must never substitute for an ESD offer.

Twenty-eight negative cases cover both materials with missing material/color/
finish/currency, denied material/options/currency, HTTP502, network failure and
non-caller-cancelled timeout. All must fail without any order/status/file writes.
Missing THB remains an ordinary mapping rejection with an available dependency.
Denied currency retains the typed client's combined unavailable/unauthorized
availability contract; this slice does not add an authorization flag to its DTO.

Initial compiled regression: 30 cases, 28 passed and two failed. HTTP502 currency
responses were correctly classified unavailable by the typed client, but the
fulfillment client combined this branch with missing values and returned generic
mapping failure with `ServiceAvailable=true`. The unchanged affected tests plus
new cases reproduced 84 passes/two failures. These are genuine behavior failures,
not source-string assertions or missing-type compilation failures.

Root approved a narrow runtime repair: check currency availability before the
existing missing-value mapping branch, log `order-currency/dependency_unavailable`
and return unavailable without writes. No prices, policy, DTO, auth or API changed.
Expanded direct regression then passed 34/34; affected nonbrowser suite passed
90/90. Six additional cases exercise the actual fulfillment coordinator with
the same actual clients/controlled HTTP: repeat HTTP502/network currency outages
preserve an existing order ID, created customer, request/journey/transaction and
checkpoint; they return Partial/DependencyUnavailable without compensation or
checkpoint/order writes. Lost-write-fence controls return Conflict without any
HTTP. All 40 focused cases passed after the final zero-warning/error build.
Final affected nonbrowser regression passed 96/96, zero failures/skips; evidence
is `TestResults/esd418-affected96/affected96.trx`.

## Validation commands and evidence

Run from this isolated worktree, with private exact-CI dependency clones:
ServiceDefaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`, CompatibilityContracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. No sibling output is used.

```powershell
$env:MalievWorkspaceRoot = Join-Path $PWD '.dependencies'
$env:GITHUB_ACTIONS = 'false'
dotnet build Legacy.Maliev.Web.slnx --configuration Release --no-restore
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~EsdFulfillmentContractTests
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~EsdFulfillmentContractTests|FullyQualifiedName~CustomerOrderCatalogClientTests|FullyQualifiedName~CustomerOrderSubmissionTransportTests|FullyQualifiedName~InstantQuotationFulfillmentMappingTests|FullyQualifiedName~InstantQuotationFulfillmentCoordinatorTests|FullyQualifiedName~EsdMaterialProfileTests|FullyQualifiedName~FdmRuntimeProfileCatalogTests|FullyQualifiedName~FilamentProfileCatalogTests'
dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore
./scripts/prepare-profile-producer-boundary.ps1
./scripts/prepare-scb-payment-boundary.ps1
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release --no-build --no-restore --logger 'trx;LogFileName=full.trx' --results-directory TestResults/esd418-full
dotnet list Legacy.Maliev.Web.slnx package --vulnerable --include-transitive --no-restore
Get-Content -LiteralPath @('Legacy.Maliev.Web.Infrastructure/InstantQuotationFulfillmentClient.cs','Legacy.Maliev.Web.Tests/EsdFulfillmentContractTests.cs','docs/esd-fulfillment-proof-20261002.md') -Raw | gitleaks stdin --redact=100 --no-banner --exit-code 1
git diff --check
```

Baseline Release: zero warnings/errors; existing focus: 24 passed. Initial new
test authoring produced four compile/analyzer errors, corrected solely in the
new test before behavioral runs. The final runtime build passed zero warnings/
errors. Preserved ignored evidence: `TestResults/esd418-baseline/baseline.trx`,
`esd418-focus/focus.trx`, `esd418-affected/affected.trx`,
`esd418-focus-green/green.trx`, `esd418-affected-green/affected-green.trx`.
Final expanded focused proof: `esd418-focus-green40/green40.trx`.
Unfiltered full: 2,946 passed, zero failures/skips, 12m54s, exit zero. Original
`TestResults/esd418-full/full.trx` was completely parsed through streaming
`XmlReader`; total/executed/passed are 2946 and failed/error/timeout/aborted/
notExecuted are zero. An earlier PowerShell `[xml]` string conversion failed
diagnostically; no results were rewritten. The bounded streaming parse completed
successfully without printing log bodies. Worktree-owned testhost/dotnet/node/
Chrome handles were zero after terminal completion and the browser window was
released to root.

Both unchanged producer preparation scripts passed with private exact-pinned
Customer/Auth/Accounting and seed builds at zero warnings/errors. Whole-solution
format verification passed. All four Web projects' transitive NuGet vulnerability
audit found no vulnerable packages. Gitleaks scanned the complete three owned text
files without leaks; whitespace checks passed. No new package or browser install.

## What this does not prove

This is controlled HTTP transport proof, not deployed Catalog/PostgreSQL,
joined Aspire, normal cookie authorization, real worker activation, actual
physical calibration or live inventory evidence. Synthetic physical pricing is
only input to the fulfillment transport test; it proves no physics result.
No browser was used for the new tests. Historical PC-ESD cross-service persisted
order/quotation readback and coordinated reconciliation/cache-drain activation
remain separate gates. Existing ESD service-page browser tests alone do not
prove the full upload/build/physical-price/persisted-order journey.

Owned deliverable: one new test, this new document and the approved minimal
`InstantQuotationFulfillmentClient.cs` branch. No existing tests, workflow,
helper, schema, package, generated asset, source repository or ledger changed.
No commit, push, deployment, persistent data operation or benchmark approval.
