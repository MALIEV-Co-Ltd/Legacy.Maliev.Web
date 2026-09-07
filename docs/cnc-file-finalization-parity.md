# CNC file finalization transport

Source checkpoint: `5ac7d045c51194edd9e64d8564f1b726b001be34`.
Source ownership: `Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml.cs`
(`MoveAndLinkUploadedFileAsync`, `CreateRequestFileAsync`, and drawing finalization)
and `CNC-Machining.ProfilePersistence.cs` (`SubmittedUploadObjectName`).
Web tracking: issue #195 under #188. This is a transport slice, not a wired
submission coordinator or completed source commit.

## Producer and consumer contract

The server-only `ICncFileFinalizationClient` receives an already persisted request
ID, authoritative session, one submission timestamp, and receipt coordinates.
Its caller must first validate the protected receipt and atomically claim it.
The coordinates must never be bound directly from browser JSON. It rejects
cross-session, transformed, noncanonical, or unsupported role/file coordinates
before any HTTP call. It does not invent additive file IDs or content hashes.

The source upload name is `yyyy-M-d/session/reserved-guid.extension` in
`maliev-instant-quotations`. The destination is
`instant-quotation/yyyy-M-d/session/reserved-guid.extension` in
`maliev-quotation-requests`, using the single submission timestamp's UTC date.
Drawing PDF and model STEP/STP/IGES/IGS preserve their reserved basenames.

1. Named `files` client sends bearer-authenticated PUT `Uploads` with query
   `sourceBucket`, `sourceObjectName`, `destinationBucket`, and
   `destinationObjectName`; no body.
2. Only confirmed HTTP 204 permits bearer-authenticated POST through named
   `quotations` to `quotationrequests/{requestId}/files`, with query `bucket` and
   `objectName`; no body or invented idempotency header.
3. Only HTTP 201 JSON with positive PascalCase `Id`, matching `RequestId`, `Bucket`,
   and `ObjectName`, and matching `Location` confirms the link. Exact relative
   resource paths and same-request-origin absolute URLs are accepted; other
   origins, resources, query strings, fragments, and userinfo are rejected.
   Responses are bounded to 64 KiB. The client never follows `Location`.

Canonical producers inspected: FileService `UploadsController`,
`FileApplicationService.MoveAsync`, storage and upload repository; QuotationService
`QuotationRequestFilesController` and its request-file response contract. Exact
204/201 validation deliberately tightens the source's broad successful-status
test to these canonical producer contracts. A separately executed producer MVC
HTTP regression passes and confirms an absolute `Location`, PascalCase body, and
null omission through real `CreatedAtRoute` serialization. Its isolated test
does not establish producer authentication or persistence; producer integration
and PR validation remain separately owned.

## Uncertainty and retry safety

FileService storage copy/delete and metadata update are not one transaction.
No failed move response proves that the original object is untouched. Missing
credentials, authentication resilience rejection, invalid coordinates, and
pre-send cancellation return `NotSent`. After move send starts, unconfirmed
outcomes return `MoveUnconfirmed` and never link, replay, delete, or compensate.
After confirmed move, any link failure returns terminal `MovedLinkUnconfirmed`
with the destination for server-side recovery. Only exact successful linkage
returns `Linked`. HTTP 401 invalidates the server-side token without replay.
Both named clients retain their unsafe-method retry prohibition.

## Verification and remaining integration

Regression coverage uses synthetic data: exact wire method/query/body and bearer,
session/path/role rejection, UTC offset dates, cancellation and token failures,
ambiguous status codes, malformed/mismatched/oversized/non-JSON responses,
absolute and relative locations, and terminal partial state. Real named-client
resilience tests prove PUT and POST are not automatically replayed.

Validation commands: Release solution build with `MalievWorkspaceRoot=B:/maliev-legacy`
and `BuildProjectReferences=false`; focused `CncFileFinalizationClientTests`; full
Web .NET suite; format verification; transitive dependency vulnerability audit;
gitleaks; and `git diff --check`. Release build: zero warnings/errors. Focused
finalization tests: 44 passed. Full Web suite: 1,774 passed, zero failed/skipped.
Explicit unowned IGES regression: one passed. Solution format verification
passes after a scoped test-only whitespace correction; the four-project
transitive dependency audit reports no known vulnerable packages against the
current NuGet source; gitleaks reports no leaks. `git diff --check` passes.

Pending: submission coordinator wiring, receipt claim/recovery orchestration,
profile fill-only persistence, notifications, browser/end-to-end evidence, and
actual production-derived data reconciliation. No application deployment,
database migration, storage write, or review-data refresh is part of this slice.
