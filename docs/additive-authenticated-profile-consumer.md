# Authenticated additive quotation profile completion

Bounded Web issue [415](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/415), parent [148](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/148), migration coordination [Workflows 234](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Workflows/issues/234).

## Source behavior and implementation boundary

Committed source objects only were inspected at source head `4198baa6`. The candidate cohort is:

| Source commit | Source behavior | Target evidence |
| --- | --- | --- |
| `c1ae969aef50a17ff7b80636fa6680cbce8df662` | `InstantQuotationAuthenticatedProfile.cs`: profile projection, populated-value locks and missing-only merge | Application profile model and projection regressions |
| `7e2f00948db4bef52e465b80018ef1cbfc42e260` | `3D-Printing.AuthenticatedProfile.cs` and page: authenticated GET projection and fresh authoritative POST merge | Owner-only typed client, preparation service and actual HTTP journey |
| `e863a3f1c156bcfa664c41d08e0405c10b12553d` | Profile creation/fill-missing, contact/company/tax/billing/shipping and authenticated fulfillment ordering | Producer completion contract, frozen Web operation and durable quotation-first submission |
| `371aa2fb4db1e2a52e9a81db687832806b1a8fb0` | Source regression: failed authenticated resolution remains editable before quotation writes | Missing capability/invalid owner/current identity tests, controlled pre-durable retry |
| `b2f5e0c1514d7ce886b8f4d9dae059b5ab359b86` | Persist quotation reference before profile completion; partial profile failure must not duplicate quotation | Actual producer readiness failure after durable quote, exact frozen-operation replay and count=1 |

The producer owns all persistent customer graph changes; Web has no new Data/provider reference. Architecture deliberately improves source security: no email identity lookup or posted identity authority, no staff permission substitution, and shared related records are copied transactionally rather than mutated across owners. Source branch codes of 1–5 ASCII digits are padded to five before posting. Existing populated raw tax identifiers or Thai suffixes remain authoritative.

## Exact producer and consumer contracts

CustomerService pin `dc090542c02d675f54c3be59e33654dc24ecca9e`, [PR34](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CustomerService/pull/34), [exact-main CI](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CustomerService/actions/runs/36683675585). Its runtime pins ServiceDefaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3` and CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.

AuthService pin `82c8d63dd08677a7f8ccd107c05dd6c9badbfd79`, [exact-main CI](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AuthService/actions/runs/36508758560). Its runtime ServiceDefaults is `5c5f9479313710fa576f83d3b396442997a2fcf4`, with the same Contracts pin. All producer binaries have separate output directories from Web and each other.

- `GET customers/{id}/instant-quotation-profile-completion`: validated owner customer bearer JWT only. PascalCase customer graph, strong quoted 64-hex graph ETag. Web requires exactly one `X-Quotation-Profile-Completion-Contract: 2` header before creating an authenticated quotation.
- After a same-owner successful GET, missing contact fields may use `GET auth/v1/customer-self-service/identity` with the same owner access token. Exact wire fields are lowercase `customerId`, `email`, `mobile`; mobile comes from genuine current `MobileNumber`, never fabricated login-cookie metadata. Cross-owner/malformed/unavailable projection fails closed. Populated customer values always win.
- When customer email is blank, current Auth email must be present, valid and exactly match the unique bounded string `email` metadata in the already producer-validated server access JWT. This comparison is not token authentication or owner resolution. Null/blank current Auth email or missing/malformed/array/duplicate/different token email requires reauthentication before quote persistence. Nonblank stored customer email remains authoritative even when current Auth email is absent. Auth refresh revocation alone cannot revoke an already issued access JWT.
- `POST customers/{id}/instant-quotation-profile-completion`: same owner JWT, original `If-Match` graph ETag and stable UUID `Idempotency-Key`. PascalCase body: nullable `FirstName`, `LastName`, `Telephone`, `Mobile`, `Company`, raw 13-digit `TaxNumber`, `Billing`, `Shipping`, `ShipToBillingAddress`, optional `TaxBranch`, `TaxBranchCode`. No posted `Email`, customer ID, identity ID or service token. Address fields follow `CustomerAddressInput` (`Building`, `AddressLine1`, `AddressLine2`, `City`, `State`, `PostalCode`, integer `CountryId`).
- Branch designation is `head-office` (no branch code) or `branch` with exact five ASCII digits. Producer stores `<13> (สำนักงานใหญ่)` or `<13> (สาขาที่ <5>)`; old callers omitting both optional fields retain raw-13 behavior.
- Successful POST receipt remains `{CustomerId, CompletionId, Changed}`. `CompletionId` is a nonzero independent producer ID, **not** an echo of the caller key. POST does not promise the GET capability header. Replay returns the original receipt; changed payload conflicts, stale graph preconditions fail, and missing/drifted journal readiness fails 503 without mutation.

## Durable ordering and failure semantics

Web rereads the trusted graph before merging submitted form values. Populated contact/company/billing/shipping values are locked in the form and also preserved by the server; DOM tampering cannot override them. Existing distinct shipping remains distinct, and locked selects/checkbox values use controlled hidden fields for normal form submission.

The display-only locked-field set is a concrete ordinal `HashSet<string>` because InteractiveServer component parameters cross a System.Text.Json boundary. JSON roundtrip and the unchanged guest interactive upload/browser regressions cover hydration; the authoritative server profile continues to expose read-only locks. Draft/TempData initial state is established before the first dependency await, so a delayed trusted profile read cannot initialize the child coordinator with an unrelated empty state.

Quotation creation and its reference precede producer completion. The owner-bound encrypted Redis checkpoint freezes the original customer details, description, producer body, key and ETag before completion. Profile failure yields a terminal partial result with the durable quotation reference; retry uses this exact operation without refreshing the graph or creating another quotation. Profile completion is recorded before files/fulfillment proceed. A known durable reference is retained even when writing that initial checkpoint conflicts; unknown transport outcomes are never presented as an editable safe retry.

The SSR parent establishes controlled retry/submitted workflow state before dependency awaits, so the interactive child cannot initialize from Empty while the actual HTTP profile is loading. This does not replace dirty user state with later untrusted form metadata.

## Reproducible disposable validation and rollout guard

`scripts/prepare-profile-producer-boundary.ps1` checks out exact pinned services/runtimes under this worktree's ignored `.dependencies`, refuses dirty checkouts, builds standalone binaries and exports the four `MALIEV_PROFILE_*_DLL` variables. Run it in the same PowerShell session before the .NET suite. CI runs the same script before its suite. The script itself starts no service and performs no DDL.

`InstantQuotationProfileProducerBoundaryTests` runs the real separately built CustomerService and AuthService Programs under normal RS256 validation (not an auth bypass), fresh loopback PostgreSQL/Redis containers, guarded standalone fixture seed processes and Web HTTP/browser journeys. Seed tools accept only the fixture's exact `profile_contract_<GUID>` or `profile_identity_<GUID>` loopback database and never reference persistent infrastructure. Browser requests outside the disposable Web origin are blocked, and optional cookies are rejected.

The Web fixture projects a test-only owner cookie/session carrying those real service access tokens. Quotation creation and file-finalization dependencies are deterministic existing-contract stand-ins: the count-one and immutable reference assertions verify consumer ordering/retry, not a new live quotation-provider persistence, login flow, physical-pricing or fulfillment acceptance claim.

Local final-candidate validation on 2026-09-30:

- `dotnet build Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-restore`: zero warnings/errors.
- Focused profile/preparation/client/submission/store/endpoint/review/unchanged guest-upload browser filter: 210/210 passed, no skips. The nullable-email and durable-checkpoint exception regressions were each observed failing before their repair.
- `dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore`: 2,442/2,442 passed, no skips, with all four pinned boundary DLL variables set.
- `npm run ci`: deterministic assets and 157 browser-module tests passed; unchanged CNC engine suite 565 passed with 10 pre-existing skips. No CNC implementation was modified.
- Web and both standalone seed project format checks, workflow `actionlint`, PowerShell parser, diff whitespace and Gitleaks checks passed. NuGet transitive and npm audits reported no vulnerable dependencies.
- Fresh separate `.dependencies/fresh` preparation exercised all six initial pinned checkouts and four Release builds without reusing an existing clone; each build had zero warnings/errors. Existing dirty checkouts fail the preservation guard.

Independent root acceptance on the final candidate (2026-09-30):

- Exact-pinned producer preparation and the Web Release build passed with zero warnings/errors.
- Root's combined `InstantQuotationAuthenticated`, `InstantQuotationProfile`, `InstantQuotationSubmission`, `InstantQuotationReviewCustomer` and `WallThicknessRealUploadBrowser` filter passed 158/158 with no skips. This is a different filter from the writer's 210-test run above.
- Root's complete Web suite passed 2,442/2,442, zero failures/skips, in 9m6s. The four pinned boundary DLL variables were present. Durable TRX files are retained outside the repository under `.artifacts/web-profile-root-20260930`.
- Root verified solution formatting, and separately verified both seed projects with their exact `ProfileAuthRoot`/`ProfileProducerRoot` and runtime paths. Checks without those seed properties are not acceptance evidence.
- Workflow actionlint, PowerShell syntax, whitespace, changed-source Gitleaks (529,063 bytes), and transitive NuGet vulnerability checks passed. Build outputs and dependency checkouts remain ignored and are not commit inputs.

Protected PR/exact-head and post-merge main CI remain separate gates; local evidence does not substitute for them.

`InstantQuotation:AuthenticatedProfileCompletion:Enabled` defaults to **false**. No deployment, persistent journal migration, role grant or activation is performed by this Web slice. Enabling the consumer requires separately approved schema/runtime readiness and existing guarded persistent acceptance gates. The producer returns 503 until the additive journal structure and needed role grants are verified. Its replay journal intentionally has no customer FK (history survives lifecycle changes); receipt hashes are pseudonymous, not anonymous.

Local disposable proof and CI do not establish persistent Aspire/production parity. The five source ledger entries remain root-owned and must not be marked resolved from a manifest target, a mock-only result or this document alone. CNC, physical pricing, general parent148 work, deployments, notifications and shared databases are excluded.
