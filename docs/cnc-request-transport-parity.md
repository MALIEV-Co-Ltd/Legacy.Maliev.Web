# CNC engineering-review request transport

Source checkpoint: `5ac7d045c51194edd9e64d8564f1b726b001be34`,
`Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml.cs` request-creation path.
Journey attribution producer source: `7ebe7e4` (tracked separately in
`Legacy.Maliev.QuotationService` issue #35). Web tracking: issue #195 under #188.

This slice provides the typed BFF client, not the completed CNC submission page.
It preserves PascalCase contact/review fields, `JourneyId`, nullable `Done`, UTC
creation time, server-side bearer authentication, and POST `quotationrequests/`.
It does not create orders, invoices, or payment instructions. It does not add an
idempotency header to the source CNC request contract.

Before POST, missing credentials, cancellation, and authentication resilience
rejection return `NotSent`. Once POST begins, errors or unverifiable responses
return `Unknown`: callers must not release receipts for an automatic retry.
Only HTTP 201 with a positive request ID, matching journey, and null or omitted
`Done` returns `Created`. The bounded response accommodates the source's 256 KiB
review record including JSON escaping. The existing general quotation client
accepts the producer's optional JourneyId without changing its own POST contract.

Regression coverage includes exact method/path/payload, no idempotency header,
missing identity, pre-cancellation, authentication circuit rejection before send,
post-send cancellation, unsuccessful status codes, malformed/incorrect response
fields, and large escaped review records. All examples use synthetic data.

Remaining integration gates: merge the PostgreSQL JourneyId producer, connect the
CNC submission coordinator, preserve profile fill-only semantics, move/link files,
deliver source-equivalent notifications, and verify browser/receipt finalization
contracts. This slice is not a deployment or full-feature parity approval.
