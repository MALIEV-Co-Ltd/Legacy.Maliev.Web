# Web #454 — display-only authoritative material progress

Current stage: approved bounded backend runtime implemented; focused21 and affected313
pass. Full/browser integration remains HELD for root review. Historical test-first
sections below retain the original RED evidence, not current implementation status.
Child https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/454 belongs to parent #380.
Base 18adfd9b881a6b6d5756a1fdcb0dbdbda3ac79cb. No commits, publication, deployment,
provider access, browser runs, or persistent data changes. Root owns integration.

## Source and authority boundaries

Source c4dd00b323a350f06a27afbce03dcbbcc56e16fb (parent
7aa95d1111528d3d875d8001a43676118974c1d0) supplies selected-first/spinner behavior.
Incremental loading-or-ready rendering is separately introduced by
744d3c5225e7cbf7a88af5c1f4fdb47442e2c325 (parent
36f0a4cbc18358548260201dcfc6c7f919d3faf5). Latest frozen source
bed10c7d15e0698e0b75f1329d0f312937f5d77f retains those behaviors. These records
remain pending; this slice does not claim whole-source equivalence.

Actual producer: registered InstantQuotationAuthoritativePricingService selects and
validates current owner/session/part/upload/configuration evidence before running
comparison analysis. Actual pricing consumer: InstantQuotationPricingService
QuoteWithPhysical → QuotePart → private MaterialPrice(part,candidate,geometry,build,physical).
The current service returns only after comparisons finish. #450 fixes stale final
OrderQuote publication, not incremental display; its coordinator files are excluded here.

New contract contains PartId, MaterialKey, Pending/Completed/Unavailable, nullable
UnitPrice only. No physical receipt, ticket, total, readiness, or authorization flags.
Completed requires a finite positive amount from the existing kernel; Pending and
Unavailable carry null. The default interface overload delegates unchanged four-argument
QuoteAsync and intentionally ignores the observer until reviewed runtime implementation.
Existing doubles and callers therefore compile without behavior changes. The actual
service now explicitly overrides this overload; only the default interface fallback
ignores observers. The four-argument implementation and its analysis helpers remain
text-identical to base HEAD (direct readback comparison performed).

## Actual test boundary and anchors

NEW InstantQuotationMaterialPricingProgressTests uses TestingWebApplicationFactory
and actual registrations: protected session store, bound analyzer, admitted mesh parser,
embedded profiles, authoritative pricing service, pricing kernel. Only the physical
input reader is replaced with controlled bytes/faults. It asserts exact session/owner/
part on every read. The synthetic clean-upload metadata is a controlled admission
precondition, NOT FileService scan/admission certification. Testing memory cache is
not distributed-CAS proof. No fake physical simulation or expected-money helper.

The independent binary STL is a closed 8×8×8 mm box: 512 mm³, 384 mm², 12 triangles,
bounding 0.512 cm³; selected ABS/Black/Standard, quantity1. Its actual cost is guarded
in (47.5,50], so margin50% gives manufacturing base in (95,100], rounding to100.
Process minimum300 gives explicit surcharge200; reserve30; setup0.25×156.25=39.0625;
packaging20; no delivery. Pre-fee389.0625, /0.97, unrounded VAT28.0766752577,
gross rounded5=430. Commercial subtotal=430−VAT−200=201.9233247423, currency201.92;
unit ceil10=210. Completed display and final line have literal210 anchors. Final
overload/ordinary overload quotes must serialize identically (including receipts),
and stored session timestamp/authorization must remain unchanged.

Read2 is gated: actual selected analysis has completed and first comparison is
waiting. Pending and Completed selected frames must already be visible. Comparison
content failure and the existing three-second analysis budget must publish null
Unavailable without revoking the usable selected final quote. Actual caller abort
after read2 propagates OperationCanceledException, not timeout, and cannot publish
completed comparison money. These are normal service-boundary tests, not UI proof.

## Initially proposed implementation (historical design; now authorized)

1. Keep four-argument path delegating to one shared implementation with no observer;
   add concrete compatible observer overload. No duplicate analysis or arithmetic.
2. Extract a narrowly internal selected/candidate display projection from the exact
   existing MaterialPrice kernel plus shared QuotePart geometry/config validation.
   Return only nullable unit price; never export its receipt. Do not call full-order
   QuoteWithPhysical for every event (quadratic recomputation/allocations).
3. Emit Pending before each attempted candidate, selected Completed immediately after
   verified selected evidence and kernel projection, comparison Completed/Unavailable
   after its analysis; close outstanding candidates when comparison analysis budget ends.
   Final quote still comes from the unchanged single QuoteWithPhysical call.
4. Await callbacks, but enforce deadlines with WaitAsync on an owned linked token.
   Proposed bounds: original selected30s and comparison3s include callbacks within
   those phases; an outer33s absolute deadline bounds terminal cleanup notifications.
   No fresh30s watchdog per frame. Caller cancellation wins. A callback that ignores
   cancellation cannot keep the producer awaiting indefinitely. On callback fault,
   propagate the original exception/no usable final quote; on owned deadline expiry,
   fail closed with no usable final quote, never fabricate completion. A late external
   callback itself cannot be forcibly stopped: tokens/deadlines bound producer waiting,
   not arbitrary observer side effects. Future coordinator must fence every publication
   by captured local generation/sessionUpdatedAt/owner/part/config/fileid/hash and detach
   disposed consumers. Backend callback arguments grant no publication authority.
   Root reviewed and approved this specific deadline policy before implementation.
5. No Razor/resources/coordinator channel in this phase. Future per-material EN/TH
   accessible pending labels/aria-live and real-upload browser controls require separate
   owned rendering lifecycle tests and an exclusive browser window. Current synthetic
   clean-byte browser helpers are not genuine FileService admission proof.

## Executed evidence and truthful classification

Private dependencies: Defaults6ea131df4bcf8d213d7d121cb8c865697bee7420,
Contracts78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7; both clean, outputs under this worktree.
Set MalievWorkspaceRoot to this worktree's absolute .dependencies and
UseLocalMalievDependencies=true. Release build succeeded0warnings/0errors before tests.

- Baseline: filter InstantQuotationBoundPhysicalAnalysisServiceTests OR
  InstantQuotationAdmittedMeshServiceTests OR InstantQuotationWorkflowPricingTests:
  107PASS/0FAIL/0SKIP; TestResults/material-backend-progress-baseline/baseline.trx.
- Initial test compile found two xUnit2031 analyzer errors in NEW Assert.Single usage;
  corrected to predicate overloads. This is not product RED. Fresh build0W/E.
- Initial feature8:6RED/2GREEN/0SKIP; material-backend-progress-red/feature-red.trx.
- Expanded diagnostic9:8FAIL/1PASS. One was a NEW mistaken10 unit anchor; real expected
  commercial sequence above is210. Retained expanded-red.trx; corrected test assumption,
  not product defect or relaxed existing assertion.
- Final focus9:7RED/2GREEN/0ERROR/0SKIP;
  TestResults/material-backend-progress-final-red/final-red.trx. Selected Pending/
  Completed and content-failure/budget cases reach real analysis/kernel and fail for
  absent frames. Await/fault cases show observer never invoked. Noncooperative observer
  cancellation schedule is honestly UNREACHED (run completes before observer entry);
  it must be rerun after implementation and is not current cancellation-safety proof.
  Two green controls preserve actual reached caller abort and byte-for-byte final quote.

The existing four-argument body is unchanged. No old tests modified. No full suite or
browser launched while root owns browser window37567. Full suite/coverage/whole static
gates remain required after runtime approval and accepted-main integration. No claim
that #454/#380 is complete; these are historical RED results. Final runtime focused
results and remaining whole-suite gate are recorded below.

Scoped dotnet format --verify-no-changes --no-restore on the three code files passed;
git diff --check passed. Four individual gitleaks dir --redact scans (the three code
files and this document) found zero leaks; no scanner suppressions added. Final RED
TRX SHA256 B0F6661AC05297F6F2FB04BDF176A53EC88B6CFAD29C6BD8733F0C04BCEB6752.

## Approved runtime and latest evidence — 2026-10-02

Root independently reproduced initial7RED/2GREEN before approving the class body.
Runtime touches only InstantQuotationAuthoritativePricingService and
InstantQuotationPricingService. The latter extracts the exact original validation
into ValidatePart and reuses the original MaterialPrice kernel for nullable UnitPrice.
No arithmetic, profile, policy, receipt/ticket creation, or final order calculation
was rewritten. A finite positive amount alone can produce Completed. Shared final
QuoteWithPhysical still runs exactly once, after selected and comparison phases.

Observer path selects all parts first, then comparisons under the existing3s budget.
Selected callbacks and analysis share30s; outer absolute33s limits terminal unavailable
notifications. Comparison analysis timeout preserves selected quote and closes the
single attempted card using the remaining absolute deadline. Callback deadline failure
returns no usable quote. Caller cancellation propagates. Known pricing failures are
caught only around pricing/analysis calls; observer IOException, ArgumentException,
and InvalidOperationException retain their original identity and are not swallowed.

Callback invocation is isolated via Task.Run with start/token precheck plus WaitAsync
on the owned phase/absolute token. Fault continuations only observe eventual exceptions;
they cannot resume analysis or emit another frame. A synchronous blocked callback may
remain on a background worker until its consumer releases it. There is no claim of
thread termination, rollback of arbitrary callback side effects, or authentication
authority. Future UI consumers must InvokeAsync on their own renderer and enforce the
captured generation fence. Tests always release their gates and drain background exit.

Optional TimeProvider on the internal service constructor preserves existing callers
and default System clock. NEW fixture registers a one-shot manual provider only for
deadline controls. It advances actual30s/3s/33s cancellation budgets without long sleeps;
normal registered analyzer/kernel/store are retained.

Evidence chronology:

- Initial runtime9:8PASS/1FAIL in material-backend-progress-initial-runtime/initial-runtime.trx.
  The sole failure assumed the unordered catalog's first comparison was PLA; actual
  attempted FDM was TPU. Root approved NEW test correction: exactly one nonselected
  Pending/FDM/read2; matching exactly one Unavailable/null; no completed comparison;
  selected literal210 retained. No runtime ordering policy added.
- Expanded runtime18PASS, then genuine new2RED in
  material-backend-progress-blocker-case-red/blocker-case-red.trx: a synchronous
  callback blocked producer beyond its owned deadline; lowercase abs selection was
  republished as a comparison. Both reached their controlled boundaries. Root approved
  async invocation for the former; canonical resolved selected-key exclusion repairs
  the latter. No old test or four-argument policy changed.
- Fresh Release0warnings/0errors; final new21PASS/0FAIL/0ERROR/0SKIP at
  TestResults/material-backend-progress-literal-runtime/literal-runtime.trx.
  Tests now reach caller abort, selected30s async/sync blockers, eventual fault,
  absolute33s terminal-cleanup bound, selected failure, original callback categories,
  unsupported/collision, multipart selected-first, and resin. Selected ABS and ABS as
  comparison under selected resin both independently assert literal210 plus exact
  final-card equality. JSON assertion permits only the four display fields; all
  noncompleted amounts are null and completed amounts finite/positive. Exact final
  quote serialization, physical receipt behavior, and unchanged protected session
  state are retained. Resin physical receipt remains null.
- Final affected313PASS/0FAIL/0ERROR/0SKIP at
  TestResults/material-backend-progress-final-affected/final-affected.trx. Filter covers
  BoundPhysicalAnalysisService, AdmittedMeshService, WorkflowPricing, Pricing,
  AdditiveQuoteTicketService, ResinServerSupportEvidenceParity, and
  TechnicalFilamentMinimumPricing test classes (no browser).

Five-file candidate: two authorized existing runtime files, NEW display contract,
NEW test class, NEW design document. No old tests, Razor, resources, coordinator,
workflow, public API, or persistent authority changed. No full/browser run, deployment,
producer activation, or source-owner disposition. Parent380 remains open. Final
whole-suite/raw coverage and accepted-main integration are pending root review.

Final scoped format verification (four code files) and git diff --check passed. Five
redacted gitleaks file scans found0 leaks with no suppression. Final21 TRX SHA256:
D948EEDA36A16A5290FF5F836CD2B4FC854109FE09F63ED27C00E364BDC16D94.
Final313 TRX SHA256:
FE1DC635D903DCA649034F5B85F2E929458EBEEAE40FF15337CF5D0D4C219888.

Reproduce with own .dependencies root, UseLocalMalievDependencies=true:
`dotnet build Legacy.Maliev.Web.slnx -c Release --nologo`, then
`dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --filter FullyQualifiedName~InstantQuotationMaterialPricingProgressTests`.
No foreign graphs or private producer fixtures are needed for this nonbrowser focus.

## Root independent review checkpoint

Root independently read both complete runtime diffs, the display contract, all21 new
tests and this design. Fresh Release build completed with zero warnings/errors;
`root-material-progress-final-focus/focus.trx` passed21/21 and
`root-material-progress-final-affected/affected.trx` passed313/313, both zero skips.
Whole-solution verify-only formatting passed with the owned dependency environment.
Transitive NuGet vulnerability audit reported none; five owned-content redacted
secret scans and whitespace checks passed. Ignored private dependencies and reviewed
proof outputs are preserved, never staged. This is an expanded-feature local commit
checkpoint only: integrated full-suite, protected-head and exact-main acceptance
remain pending. It does not close parent380, sourcec4dd/744 or frontend/admission proof.
