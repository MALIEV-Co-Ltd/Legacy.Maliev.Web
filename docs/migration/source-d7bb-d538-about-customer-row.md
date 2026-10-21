# About customer row: source d7bb4f8 and d5385fd

This proposal carries the final combined behavior of original commits
`d7bb4f884786b4bb6460c5003d1da43c7ce1578f` (parent `1b1b4949407976d0215e374326d51137776d3fa5`)
and `d5385fdd683e443668bcef10e5e1e5286e43e707` (parent `d7bb4f884786b4bb6460c5003d1da43c7ce1578f`).
Issue #727 is the source owner; the existing About implementation is proposed through PR #653.

The first commit replaced Kumwell with ALTEN Korea. Its direct successor explicitly restored Kumwell, retained Lenso and all other original customers in their original order, and appended six entries. The final order therefore has the existing 30 followed by ALTEN Korea, Forth Corporation, Do Day Dream, Thai Agri Foods, Sahamitr Pressure Container and Index Creative Village. The English and Thai introduction describes companies that have chosen MALIEV; it does not claim that every project is complete.

The shared Blazor content retains the source dimensions, adjacent English/Thai captions, lazy asynchronous decorative images, and the existing dark backing for the white SMPC and Index artwork. Existing CSS supplies the desktop and mobile layout; no CSS or JavaScript change is needed. Both the active Blazor SSR route and retained Razor composition consume this content.

## Asset custody

The six unchanged original PNG/JPEG inputs are kept under `tests/about-customer-row-source-inputs/`, outside the public application. Their exact original Git blobs, SHA-256 hashes, sizes and dimensions are closed in `tests/about-customer-row-webp-inputs.json`. The five later source URLs are copied unchanged in `about-customer-row-original-logo-sources.json`. ALTEN's original source is `https://www.alten-korea.com/images/logo_on_m.png`, referenced by the original verification note for `https://www.alten-korea.com/`.

The existing pinned Pillow 13 proof lane must generate the six lossless static WebP followers using the unchanged reviewed `convert_static` function. Runtime references must not be published without those genuine, independently qualified bytes. Qualification checks decoded RGBA and alpha, dimensions, orientation, ICC, normalized EXIF and XMP. Original containers remain available as exact source inputs; this is not a claim that WebP preserves every PNG/JPEG container chunk. The two PNG XMP payloads are preserved by hash, not printed in receipts.

The prior eighteen-source manifest, converter, rejection tests and qualified-asset evidence remain unchanged. This six-source intake is separate and does not rewrite their historical custody.

## Evidence and limits

The original verification note records the author's October 21 customer research, artwork inspection, zero-warning build and 346 focused passes. Those are historical original-repository claims, not validation of Legacy Web. No new authenticated customer research, disclosure permission or customer/order data is introduced here. The source's draft publication review remains applicable; the names and marks do not imply endorsement or external disclosure approval.

Local Legacy build, tests, browser rendering and image conversion are **NOT RUN** under the owner-approved source-only/resource boundary. Exact source-byte and literal-packet checks are not runtime acceptance. Required follow-up is the reviewed finite hosted asset proof, genuine follower qualification and commit, then exact-head Release, focused About/SSR/browser, full-suite, formatting, security, generated-asset and coverage validation under unchanged policies, followed by exact protected-main acceptance. Neither source commit is closed by this proposal alone.

No API, persistence, account, payment, deployment or infrastructure behavior changes.
