# Crawlable public email fallback migration — Web440

Source `7e3847e326363d6b14a0800b5141d3d0599f9714`, parent
`937b5314821259f9d86059dfbc8de49e07c91d32`, changes public contact/footer,
career, NDA and quotation email links to `/contact#contact-us` with known
`data-contact-email` recipients. Its shared contact script enhances only five
public business addresses to mailto. Current migrated Contact already emits
the marker, but the consent-safe script ignores it; other active links remain
direct mailto. Source and current runtime were read before writing tests.

This workspace reuses completed, detached payment-proof outputs under a new
exclusive branch at protected main `c0e63e2c51865433703ec551228b806443b93ba3`.
Existing private proof outputs were preserved; no runtime was edited during that
initial TEST/DESIGN stage. Subsequent authorized repair evidence follows below.

Initial tests exercise actual local HTTP-rendered documents in Chromium with
JavaScript disabled, checking crawlable href and exact public recipient marker.
Separate executable-script tests obtain the analytics script from the actual
HTTP response, execute that unmodified script in Chromium with real DOM Elements,
and substitute only registration, timer, location-assignment and analytics sinks.
They prove classification, navigation intent, fixed PII-free event shape and
no raw dataLayer write; they are not trusted-click, real mail client, complete
consent-queue, hosted tracking or provider acceptance. Nothing sends an email.
The known-recipient expectations are literals derived from committed source.

Next: build first, prove genuine missing behavior and preserve controls before
the smallest runtime repair. Extend tests for modified/download clicks, recipient
normalization, denied/unavailable analytics and existing target behavior. Existing
consent-gated event names/shape must remain, not source raw dataLayer pushes.
Update only explicitly superseded direct-mailto assertions with stronger fallback
checks; keep all other assertions. Final focused/full/static/security gates and
protected PR/main CI remain required. Broader source owners, metadata/copy and
hosted SEO stay open. No deployment, original edit or persistent data/schema write.

## Observed RED and bounded repair

The initial private dependency-root typo failed the build before tests (two errors);
the corrected owned `TestResults/.wall-dependencies` produced Release zero
warnings/errors. Initial15 cases:10 genuine failures and5 passing controls, zero
skips (`TestResults/email440-initial-red/initial-red.trx`). Four actual no-JavaScript
NDA/footer links retained direct mailto, and six known-marker script cases did not
enhance navigation. Contact fallback and unknown-marker controls already passed.
Ten additional actual HTTP renderer cases all failed the fallback assertions
(`TestResults/email440-renderer-red/renderer-red.trx`), covering EN/TH Career,
quotation guidance and retained Contact Razor rendering. They reached actual200
documents; synthetic Country/Career DTOs isolate those downstream read boundaries.

The repair changes only seven public component/Razor paths: the shared analytics
script recognizes exactly five fixed business recipients and reuses existing
consent-safe four-field emission/navigation; public footer, NDA, Career, quotation
guidance, retained Contact and shared contact partial retain crawlable fallback
links plus explicit recipient markers. Unknown/prototype/query-bearing recipients
are not enhanced. Private member links, auth, cookies, canonical policy, GTM loading,
providers, service contracts and persistence are unchanged.

Four old direct-mailto literal assertions in three existing test files are explicitly
superseded by this source change; their replacements assert both fallback href and
recipient marker together. Every other assertion remains unchanged. A subsequent
browser run produced six strict-key failures caused by Playwright's JSON object
transport adding reference metadata (`$id`), not rendered analytics emission. The
harness now reads browser `JSON.stringify` as a string and parses it independently;
the exact four-key assertion and recipient exclusion are unchanged. Failed TRX is
retained at `TestResults/email440-browser-green/browser-green.trx` despite its
historical directory label; it is not green evidence.

Extended focused36 cases passed with zero skips after Release zero warnings/errors
(`TestResults/email440-extended-green/extended-green.trx`). New controls exercise
recipient case/whitespace normalization, modified/middle/already-prevented/download
clicks, and absent/declined analytics sinks without blocking mailto navigation.
These controlled sinks do not prove real consent storage/queue or a mail client.
Existing channel/consent contracts remain covered by the affected suite.

Root preserved all owned changes while fast-forwarding the branch to accepted
main `33eff14e8e1aa88c7833507add00f7032cb96cf2`. Exact private profile/SCB helper
preparation and integrated Web Release build all finished zero warnings/errors;
integrated focus68 passed without skips, including32 accepted culture-cookie cases.
Full affected suite is running serially; final static/security and protected PR/main
CI remain pending. This narrow email slice does not close source
`7e3847e326363d6b14a0800b5141d3d0599f9714` in full: its other metadata/copy,
generated-documentation and route changes require separate explicit dispositions.

Integrated full suite terminal exit0:2798executed/2798PASS/0failed/0skipped,
duration12m19s. TRX `TestResults/email440-integrated-full/integrated-full.trx`,
SHA256 `C267A22573481A188F294E283E921E7038F5B47D0AE48272BA7D5ED0C191FF78`.
This is2762 unchanged accepted-base cases plus36 new email boundary/control cases.
Full npm audit found0vulnerabilities. Subsequent format verification caught one
newline-only initializer formatting issue in the new HTTP fixture; only that
whitespace was corrected, with no assertion or runtime behavior change. The
original passing full evidence is retained; final static checks and exact-head CI
validate the candidate before any merge. No provider email was sent.

Final whole-solution format verification, four transitive .NET dependency audits,
full npm audit, actionlint, CI-pinned JWT signing-resource scanner, thirteen scoped
redacted secret scans and diff checks passed. Scanner returns false for detected
material (none); its trusted wrapper semantics were independently read. After the
newline-only fixture correction, fresh Release again produced zero warnings/errors
and affected nonbrowser HTTP/analytics focus33 passed without skips
(`TestResults/email440-final-http/final-http.trx`). Browser tests are not rerun
concurrently with the separately owned account-header full suite. The full passing
TRX above predates only that test whitespace/doc change, not any runtime or
assertion change. Exact-head CI remains the final candidate gate. Proof outputs are
intentionally retained, confirmed ignored and excluded from staging.

## Current-main integration checkpoint

The clean candidate was rebased onto sitemap main
`834f089771cc2f7693b688bda6f80b1cb1fa8b94` without conflicts. No email runtime or
assertion changed. Root rebuilt the integrated solution in Release: zero warnings
and errors. The combined nonbrowser email/channel/sitemap HTTP focus passed
45 tests, zero failures/skips (`TestResults/email440-sitemap-rebase-http/root-rebase-http.trx`).
The earlier 2798-case full proof above remains bound to its stated base; it is
not relabeled as current-main evidence. Replacement exact-head CI must validate
the complete integrated suite before merge. Local browser execution is serialized
behind the independent account-header worktree; no timeout or skip was changed.
