# CNC submission admission and engineering-review record

Source checkpoint: `5ac7d045c51194edd9e64d8564f1b726b001be34`.
Owning source: `Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml.cs`.
Tracking: Web #195 under #188. This does not complete either issue or enable
the CNC SubmitRequest endpoint.

## Source mappings

- `CncSubmissionAdmission.Snapshot.cs`: source
  `ValidateCncSnapshotForPersistence` (1742), exact property-set checks, snapshot
  shape/evidence/line-item helpers, raw decimal minor-unit reconciliation, and
  snapshot/item DTOs (2767 onward).
- `CncSubmissionAdmission.Items.cs`: source `ValidateSubmittedOrderItems` (1505)
  and `IsCncRequestWithinBudgets` (1610). Existing protected bindings perform
  source-equivalent form/session/item/role/name/path/expiry/nonce validation.
  The caller must separately validate its protected form token and then
  atomically claim all receipts; this helper does neither storage operation.
- `CncSubmission.cs`: posted contact/address/tax/item data and source address-line,
  shipping fallback, and Thai tax-branch formatting.
- `CncSubmissionAdmission.Review.cs`: readable `BuildCncReviewSummary` (2368) and
  message-composition section of `CreateQuotationRequestAsync` (2254), without
  its HTTP request, identity, persistence, or notification responsibilities.

The reviewer receives contact details, billing/shipping lines, every item's
readable requirements/pricing/version summary, canonical untrusted snapshot,
free-text description, and the nonbinding engineering-review disclaimer.
This is plain-text persistence composition; HTML email escaping remains a
separate required port. Original names and Thai text are preserved.

Required caller order: aggregate budget check, then protected form validation,
then item/receipt/snapshot validation with an empty ModelState, then verify
ModelState success, then compose the record and enforce its final byte limit.
The composer is not an independently safe admission API for arbitrary DTOs:
it consumes validated snapshot projections. Claim/persistence sequencing remains
the future coordinator's responsibility. Pure tests here do not establish any
HTTP handler's zero-write behavior or complete coordinator parity.

## Validation contract

Preserved limits: 20 items; 65,536 UTF-8 bytes per snapshot; 40,960 aggregate
snapshot bytes; 262,144 generated record bytes; depth 24; 50 review reasons of
80 characters; bounded setup/tool-family counts and optional manufacturing
evidence; exact required/optional JSON properties; finite nonnegative values;
exact THB/preliminary/client-generated markers; UTC timestamp; supported
requirements and owned model/drawing receipt binding; quantity correspondence.
Drawing-defined threads or tolerance require the item's own PDF receipt.

Newtonsoft.Json 13.0.4 is explicitly pinned. It was already transitively resolved
by Web. Keeping the source parser preserves duplicate rejection, opt-in wire
names, canonical output, and existing coercion behavior. In particular source
`ToObject` integer coercion is retained: a fractional setup count can round to
an integer. Snapshots remain untrusted evidence; the server does not accept or
recompute manufacturing geometry or binding prices.

The System.Text.Json raw money pass runs before Newtonsoft. It rejects exponent
or sub-satang lexical amounts, caps values at 9,007,199,254,740,990 minor units,
uses checked accumulation, and permits at most one minor-unit discrepancy.

One bounded source defect is corrected: a non-object `lineItems` entry previously
threw `InvalidOperationException` in the raw money pass before shape validation.
An explicit object-kind guard now rejects null, scalar, string, and array entries.
Four regression cases failed before this guard and pass afterward.
Behavioral tests also reject exponent and fractional minor-unit tokens before
normalization, overflowing cumulative amounts and above-ceiling values, while
accepting the exact money ceiling. These replace the source test's brittle
source-text ordering assertion with observed parser behavior.

## Tests and boundaries

`CncSubmissionSnapshotTests` extracts the source's pure snapshot cases and fixtures
from `CncQuotationSubmissionContractTests`, replacing reflection with direct
helper calls. It preserves source assertions for optional manufacturing evidence,
lexical money limits, exact keys, multibyte limits, malformed JSON, duplicate
properties, bounded codes, and shape-valid tampered prices remaining untrusted.
Additional tests cover malformed line entries, nested limits/nulls, depth and
timestamp offsets.

`CncSubmissionAdmissionTests` covers model extensions, requirement policy,
quantity mismatch, drawing ownership, cross-item/session/form/role/name/path
receipts, duplicate nonce, expiry, same-name independent uploads, item/UTF-8
budgets, complete readable record, and exact generated-message byte limit.
All fixtures are synthetic. No source PageModel, identity/database fake, or
pretend submission endpoint is introduced.

The initial
source-test red baseline was 6 failed/10 passed against a rejecting stub; after
port and adversarial guard, all 61 focused tests pass. Release builds have zero
warnings/errors and use
`MalievWorkspaceRoot=B:/maliev-legacy`, `BuildProjectReferences=false`, and serial
project ordering. The full coverage run passes 1,791 tests with zero failures or
skips. The initially absent coverage collector was resolved by explicitly adding
approved `coverlet.collector` 6.0.4 with `PrivateAssets=all` to the test project.

Coverage from the full run, before mechanical whitespace formatting:

| Scope | Covered lines | Total lines | Line coverage |
| --- | ---: | ---: | ---: |
| New submission files combined | 490 | 501 | 97.80% |
| Admission items | 95 | 95 | 100% |
| Review composer | 59 | 60 | 98.33% |
| Snapshot admission and DTOs | 290 | 299 | 96.99% |
| Contact submission DTO | 46 | 47 | 97.87% |
| Web-owned projects combined | 17,392 | 20,626 | 84.32% |

Individual Web-owned package line rates: Application 89.50%, Web 84.65%,
Infrastructure 80.62%. This does not claim coverage for Auth or shared-library
suites; shared dependencies are not assessed by this Web test run.

Solution format verification passes after scoped whitespace formatting of new
files. The four-project transitive package vulnerability audit reports no known
vulnerabilities against the current NuGet source. Gitleaks reports no leaks;
`git diff --check` passes. HTTP, browser and production-data checks are not
applicable to this unregistered helper slice and remain explicit integration
gates, not claimed passing tests.

Remaining: authenticated profile reload/merge/completion, atomic receipt-claim
coordinator, request creation and ambiguity gates, sequential model/drawing
finalization, signed links, notifications, TempData/analytics, HTTP binding,
browser behavior, distributed receipt storage, and production-derived data
reconciliation. No deployment, source mutation, or database/storage writes occur
in this slice.
