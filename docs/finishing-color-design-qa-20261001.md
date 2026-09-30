# Portable finishing-colour QA and initialization repair

Tracks Web #424 and Workflows #225. Source commit:
`04c9bb0d80ddc9d8be97af632c71f1348307c332`.
Base: protected Web `c56d9cb9b4b2c351aab4893cda2ee89364ef3721`.

## Source decisions, not stale proof

The committed source `design-qa.md` records an approved matcher composition:
technical controls, ranked open HLC references, one selected-reference band and
one quotation action. Licensed Pantone cards are intentionally not reproduced;
the official finder is an external handoff, and any entered Pantone code is
customer supplied. Both English and Thai must distinguish screen estimates from
physical spray-out/reference approval. No digital preview is production colour
certification.

Its local generated-image/screenshot paths and historical "passed" conclusion
are historical context only, not live evidence links. Original 20-result expansion
and typography observations are also historical: current source-derived ranking
uses ten lightness-ordered references, and current site tokens are not determined
by the old conceptual board. No licensed data or obsolete UI is restored simply
to reproduce an old screenshot.

## Current independent failure and minimal repair

Normal rendered Chromium on a loopback Kestrel test host reported Three revision
186, available WebGL2 and the preview API, but no `is-pbr-ready` stage. The deployed
bundle's matcher initialization preceded its namespace assignment (indices
842419 and 851736 respectively). Static ES-module dependencies execute before the
entry's top-level assignment, so the matcher tried to create a preview while
`window.MalievFinishingThree` was still absent. This is an initialization defect,
not a missing GPU or a need to upgrade packages.

The two genuine desktop/mobile reduced-motion preview cases failed while four
other new matcher cases passed. Initial harness failures were corrected separately:
native keyboard activation replaces an attempted click through the styled radio's
covering label; Playwright locator assertions replace an eval-string wait blocked
by the existing CSP. CSP, runtime expectations and browser assertions were not
relaxed. Those setup failures are not evidence of a production defect.

`finishing-three-runtime.js` is now the first entry dependency and publishes the
unchanged pinned Three namespace before preview/matcher side-effect modules.
Only the finishing entry and its deterministic bundle change. No pricing,
quotation contract, colour algorithm, consent, analytics, dependency, provider or
deployment configuration changes.

## Reproducible acceptance

Browser plugin not available; use this repository's pinned .NET Playwright tests
and existing `PublicContactBrowserFixture`. Each browser context is fresh and
rejects optional consent. Host identity is loopback, with the real compiled SSR
application/assets in the Testing environment; only the country API is controlled.
This is not authenticated production-derived Aspire or production browser proof.

Flow: `/services/finishing-and-color?culture=en|th` → HEX/keyboard reference and
sheen selection → customer Pantone reference → actual `/quotation` prefilled form.

- English/Thai at 390 and 1360 CSS pixels, height 912; reduced motion tested in
  English mobile and Thai desktop.
- Ten real ranked HLC rows and one selected state; ArrowRight, Home, End and
  native Space radio activation update selected/focused controls.
- Colour `#00FF00`, selected HLC, customer Pantone and localized gloss survive
  the actual quotation-page handoff.
- Physical-reference caveat, open atlas attribution and official finder link
  remain visible; primary action fits the viewport without document overflow.
- Invalid HEX and non-image upload preserve the previous selection and expose
  the exact localized validity/status messages.
- Genuine PBR readiness is required. With reduced motion, actual pointer movement
  schedules zero preview interaction animation frames through the normal browser
  runtime. The observer is scoped to actual preview pointer listeners; unrelated
  page navigation/scroll animation frames are not preview-motion evidence.
- New flow cases capture page errors and matcher screenshots; no errors are
  allowed. These are interaction/layout checks, not colour calibration or a
  pixel-equivalence claim against an unavailable conceptual image.

Baseline: zero-warning/error Release build and nine existing focused tests passed.
Repair gate: zero-warning/error Release build and 15 focused tests passed, zero
skips, in 22 seconds (`TestResults/matcher-qa-runtime-focus`). Full affected suite:
2,522 passed, zero failures/skips, in 12m51s (`TestResults/matcher-qa-full`). A later
focused check caught an invalid global-RAF observer: it also counted the page's
independent TOC/scroll callbacks. That failed report remains in
`TestResults/matcher-final-focus`; it is not silently discarded or treated as a
preview runtime defect. The observer now wraps real preview-stage pointer handlers
and asserts an actual pointer event occurred, while preserving native callbacks.
Final scoped-motion focus: nine cases passed, zero skips; final full affected
suite: 2,522 passed, zero failures/skips, 9m03s
(`TestResults/matcher-scoped-motion-full`). Earlier 15-case focus included related
checks; the final exact two-class filter has six new and three retained cases.
Complete
solution formatting passed after a whitespace-only initializer adjustment; the
initial formatting failure and wrong-working-directory diagnostic are not passing
evidence. Existing asset CI passed: 157 browser modules and 565 geometry tests,
with ten existing optional CAD/evidence skips, zero failures. These skips do not
prove CNC acceptance. Protected PR and exact-main gates remain pending; do not
close Web #424 or Workflows #225 before those complete.

Screenshots are ignored evidence beneath the owned test output's
`TestResults/finishing-color-design-qa/matcher-{en|th}-{390|1360}.png`, not committed
customer images or stale source paths. Root directly inspected all four current
English/Thai mobile/desktop screenshots: actual shaded foreground, localized
controls/reference caveat and quotation action are visible. Locator screenshots
also capture the page's sticky navigation overlay; they are not a clean conceptual
board or pixel-equivalence proof. No application is deployed and no production
database, GTM tag or traffic is changed. Separate Workflows #229 telemetry/release
gates remain open.

## Root composed integration on protected preview main

The colour and protected-session continuity slices retain separate logical
commits in one Web regression PR. Root composed them with preview PR425, ran
Release with zero warnings/errors, focused53/53 and full2566/2566 with zero
skips (10m52). Whole solution format and transitive NuGet audit passed. Asset CI
passed176 browser modules and565 geometry tests with10 existing opt-in skips.
Npm audit found zero vulnerabilities; scoped secret scanning found no leaks.

Rebasing onto merged preview main `8f12747a88ec32b26bbc5254e51f7270a1de10d0`
skipped the already-applied preview patch and preserved the exact tested code
tree `d3aefe1612ea7e5f696317fe5a3de6cbebeeb368`. Its exact-main CI36766083437
passed, and publication was explicitly skipped. No code changed during rebase.
The first root asset hash command used a nonexistent `.mjs` path; its apparent
null-equality result is excluded. The corrected command rebuilt the actual
`route-service-finishing.js` and verified unchanged SHA256
`EC3C8ADE24FCE6E9F32B9EE29B207FB287AB9DD76B16EAD0BF64220742EF81EC`.

This document-only acceptance update does not alter the validated runtime tree.
The combined PR's required head and post-merge main checks remain necessary.
Parent275, telemetry229, whole-source resolution and production-derived Aspire
acceptance are not closed by these bounded fixes.
