# Instant Quotation Production-Parity Design

## Outcome

Restore `/InstantQuotation/3D-Printing` as the upload-first, multi-part quotation journey covered by Legacy.Maliev.Web issues #148-#152. The public document remains static SSR for metadata, navigation, consent, and first paint. A single scoped Interactive Server component owns upload progress, viewer/configuration state, review, customer details, and submission.

## Boundaries

- Legacy.Maliev.Web owns the browser workflow, advisory preview/DFM, server-side quotation session, deterministic price calculation, review, customer validation, CSRF, idempotency, quotation request creation, consent-safe analytics, and FileService client integration.
- Legacy.Maliev.FileService issue #7 owns streaming upload, authoritative geometry, temporary-object ownership, storage lifecycle, finalization, GCS credentials, and its OpenAPI wire contract. Web must fail closed when that service contract is unavailable.
- Issue #153 owns golden fixtures, production/Aspire browser regression automation, and release evidence. Issue #154 owns Aspire identity/startup. This branch does not edit either lane or use port 5088.
- Browser geometry and DFM are advisory only. Price and final submission use server-held geometry returned by FileService for the current protected quotation session.

## Architecture

`InstantQuotationPage.razor` remains the static route owner and renders the localized metadata and shell. `InstantQuotationWorkflow.razor` is the only interactive island. It delegates to a protected `IInstantQuotationSessionStore`, an `IInstantQuotationUploadClient`, an `InstantQuotationPricingService`, and an `InstantQuotationSubmissionService`. Browser JavaScript is limited to file-drop/progress transport and a dynamically imported Three.js/OCCT viewer; it never receives service credentials or storage ownership identifiers suitable for cross-session reuse.

The workflow state is a server-held aggregate keyed by a cryptographically random HttpOnly session cookie. Each part has an opaque part ID, display filename, FileService upload reference, authoritative geometry, advisory viewer metadata/DFM, material, color, quantity, server-calculated line quote, and status. The browser receives only the display state needed to render the current session.

## Data flow

1. Static SSR emits metadata, consent infrastructure, localized empty state, an antiforgery token, and the scoped interactive component marker.
2. Upload is validated in Web and streamed through the typed FileService client using the current quotation-session token. FileService returns an opaque upload reference and authoritative geometry/problem details.
3. The browser dynamically previews supported formats and reports advisory DFM. Server pricing ignores browser totals and calculates from the stored authoritative geometry plus material and quantity.
4. Review renders per-part configuration, line prices, printing subtotal, shipping, VAT, grand total, and deterministic lead-time range from server state.
5. Submission validates antiforgery, customer fields, session ownership, and a stable submission ID. It recomputes totals, creates the quotation request with the existing typed client and idempotency key, asks FileService to finalize/link the session uploads, and uses PRG-style persisted-reference messaging for uncertain partial completion.
6. Analytics goes through `window.malievAnalytics.emit`. Events contain no filename, session ID, customer data, or free-form description. The persisted request reference is the sole `transaction_id` for `request_quote` deduplication.

## Compatibility

- Supported extensions: `.stl`, `.obj`, `.3mf`, `.glb`, `.gltf`, `.stp`, `.step`, `.igs`, `.iges`.
- Material keys, color wire values, quantity range `1-1000`, tiers `1/10/50/100`, THB rounding, process minimums, shipping, 7% VAT, and lead time preserve the captured production rules.
- The legacy `GetEstimate` and `GetOrderTotal` shapes remain temporarily available for rollback compatibility, but the interactive workflow uses typed POST commands backed by server session state.
- Thai and English copy preserve production intent while correcting production accessibility defects: native buttons/inputs, labelled canvas alternative, focus management, live status, reduced motion, and keyboard-accessible controls.

## Failure handling

- Unsupported, malformed, oversized, cancelled, parser-failed, malware-rejected, session-mismatched, and downstream-unavailable uploads remain recoverable without losing other parts.
- Unsupported material/process combinations produce no total.
- Missing or ambiguous service responses fail closed and preserve user-entered data.
- Once a quotation request is persisted, every later failure shows the opaque request reference and forbids duplicate resubmission.
- No unsafe HTTP method receives an automatic retry. Explicit retries reuse stable session, part, and submission identifiers.

## Validation

Use red-green focused xUnit and Node tests for every Web-owned contract. Verify exact JSON casing and problem details, server recomputation against tampered browser values, CSRF, anonymous/member continuity, idempotency, analytics consent/deduplication, Thai/English static SSR, keyboard/mobile semantics, dependency audits, Release build/format, gitleaks, and browser QA on an isolated safe local port. Final Aspire evidence and production deployment remain outside this branch pending #153/#154 and owner review.
