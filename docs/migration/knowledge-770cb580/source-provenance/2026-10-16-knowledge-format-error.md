# Knowledge specifications FormatException investigation

User screenshot: /knowledges/specifications displays System.FormatException from HtmlFormattableString.WriteTo with an out-of-range format index.

## Evidence

The same stack was captured during the original search implementation in D:/codex-temp/2026-10-14/task/knowledge-search/render-error.html. It was caused by rendering localized search-count templates containing {0}/{1} through IHtmlLocalizer without server formatting arguments. These are client-side templates: knowledge-search.js substitutes them later. Razor must receive the encoded plain string via Localizer[key].Value.

Current committed source already contains the fix in adbed46b: data-search-count uses Localizer["Matches: {0}"].Value, and data-search-limited uses Localizer["Showing {0} of {1} matches. Narrow your search to see more."].Value. Both English and Thai templates retain their intended placeholders. No other positional placeholders were found in the specifications overview page, its Thai resource, or the examined shared layouts/partials. The specifications route was covered by successful prior browser checks after this fix; those prior results do not validate the newer pending visual/layout changes.

Port5187 had no listening process at the beginning of this investigation. Therefore the displayed exception was an earlier response, not an error reproduced against a currently running process. Its exact serving build cannot be identified from the screenshot alone. No new production code defect is established beyond the earlier fixed placeholder bug.

## Current changes

Added a Razor comment explaining why .Value is required, and two direct page-render regression cases to KnowledgeSearchBrowserTests.cs: English/Thai specifications overview must return200, render its article heading, and expose the exact literal localized data-search-count/data-search-limited templates. Existing search tests additionally exercise client-side substitution. Other pending source/asset changes are preserved.

## Validation and blocker

Source/readback and git diff --check passed. New tests are NOT COMPILED OR RUN; fresh build, focused tests, full Web suite and scoped C#format remain unrun because resource admission is blocked. A guarded two-minute attempt to restart the latest built Release preview ended without spawning a server: free RAM remained below the unchanged4GiB requirement. The admission queue exited, no task-owned process remains, and port5187 is still unserved. No commit, push or deployment for this investigation.

Required follow-up when admission permits: build Release with warnings as errors, run KnowledgeSearchBrowserTests (including Specifications_RendersLiteralSearchCountTemplates) in both languages and the relevant/full Web suite; run scoped format on the changed test; restore the finite preview and verify GET /knowledges/specifications for default/en/th cultures. Then review and commit the coherent validated slice. Do not claim the screenshot's error is resolved live until a fresh200 response is inspected.


## Authorized production release follow-up (2026-10-17)

Resource admission later succeeded. Fresh Release build passed with zero warnings/errors; the corrected focused suite passed 65 tests; the full Web suite passed 1,208 tests with zero failures/skips. Scoped formatting passed for both touched browser-test files. JavaScript tests passed 48/48 and frontend tests 78/78. Security scanner tests passed 26/26 with zero high-confidence current-tree findings. Final native desktop/mobile English/Thai captures were reviewed. Fresh loopback specifications requests returned HTTP 200 in both languages with literal count placeholders. All temporary native workers exited; the review tab was closed and the temporary preview was stopped before packaging.

The image test initially assumed responsive naturalWidth equals the source pixel width; it was corrected to check successful decoding, the intended responsive source and 3:2 aspect ratio. File-level checks still confirm actual source dimensions. Final browser review of the anodizing asset corrected missing endpoint arrowheads by separating its dimension paths; that static-only correction was XML-parsed and visually rechecked. A focused all-guide image decode rerun is recorded in the release verification note. Production deployment is now explicitly authorized; exact publication/rollout receipts are retained outside Git under D:/codex-temp/2026-10-14/task/knowledge-production-release.
