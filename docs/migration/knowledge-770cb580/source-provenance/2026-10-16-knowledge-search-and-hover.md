# Knowledge Center search and directory feedback

## Intended behavior

Directory links highlight the entire image-and-copy item in light grey, fading in and out over 180 ms. Keyboard focus gets the same background and a visible outline. The existing global reduced-motion rule shortens the transition. No viewport-specific override or new breakpoint is introduced.

A compact, labelled search field sits below the Knowledge Center sidebar header. It searches the localized article body and headings, returns section links with short excerpts, preserves culture, and supports technical English aliases alongside Thai content. A minimum of two characters starts search after a short debounce. Eight results are shown with the full matching count and a refinement hint when more exist. Clearing restores the existing grouped navigation.

## Implementation and boundaries

- `knowledge-search.js` derives the index from the actual 18 non-directory guides rather than a separately maintained keyword list. Content outside sections is included in the article-level entry; section IDs or heading IDs supply deep-link targets.
- Public, same-origin article HTML is fetched only after search is used, with up to three concurrent requests and a 30-second index-load timeout. Successfully loaded content is cached only for the current page lifetime. Failed loads are reported and can be retried; they are not presented as complete empty results.
- HTML is parsed into inert template content. Scripts and article images must not execute/load during indexing. Results are built with textContent, not user-generated HTML. Query text stays in the browser and is not sent to an external search service or added to article requests.
- NFKC normalization, case-insensitive AND matching, weighted heading/title/alias ranking and bounded excerpts support Thai text, material codes and numerical terms.
- Existing mobile drawer keyboard handling now includes the search input. Same-page section results close the modal drawer and focus the target, including guides whose published anchors are on headings. Standard Tab/Enter navigation is retained.
- Search controls remain hidden until enhanced, leaving ordinary topic navigation available without JavaScript. Labels, hints, counts, loading, empty and retry states have Thai resources.
- Localized count templates use their plain string Value in encoded Razor attributes, so server HTML formatting does not consume placeholders intended for browser substitution.
- No API, authentication, database, pricing, route inventory, public-article content or deployment configuration changed. No new dependencies.

## Validation

The first browser attempt exposed a server HtmlFormattableString FormatException in deferred count templates. After capturing the response exception, the run was stopped; its partial result was 42 passed and 8 failed, with remaining cases aborted. Exact task-owned workers were cleaned up and the failure logs retained outside the repository. The templates were corrected before rebuilding.

Regression coverage includes pure ranking/normalization behavior, localized body search, mixed-language technical queries, heading-anchor navigation, mobile focus, lazy requests, avoiding article-photo downloads while indexing, clearing and network-failure retry. Browser captures include full-row hover and desktop/mobile search states.

Final validation passed:

- `dotnet build Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false /nodeReuse:false -warnaserror`: zero warnings and errors.
- Focused `dotnet test ... -c Release --no-build --filter` covering KnowledgeCenterBrowserTests, KnowledgeSearchBrowserTests, KnowledgePageMigrationSourceTests, ApplicationAreaShellSourceTests, TechnicalSeoTests, SeoBusinessContractTests and ApplicationPageMigrationSourceTests: 57 passed, zero failed or skipped.
- `dotnet test Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-build --blame-hang-timeout 5m --blame-hang-dump-type none`: 1,200 passed, zero failed or skipped; 10 minutes 4 seconds.
- `dotnet format whitespace Maliev.Web.Tests/Maliev.Web.Tests.csproj --no-restore --include Maliev.Web.Tests/KnowledgeSearchBrowserTests.cs --verify-no-changes`: passed.
- `node --test --test-concurrency=1 Maliev.Web.Tests/JavaScript/*.test.cjs`: 48 passed. Corresponding Frontend suite: 78 passed.
- Thai resource XML parsed with 47 unique keys; `git diff --check` passed. Build outputs are ignored; no generated outputs or unrelated files are staged.
- Desktop English/Thai and mobile English/Thai browser captures reviewed. A final in-app browser check found the Thai milling example from `feeds 3819.7`, preserving `culture=th#milling`. Normal-motion computed style is `background-color 0.18s`; the global reduced-motion override remains active when requested.

All 14 observed full-suite worker processes exited. The temporary in-app review tab was closed and its absence verified. The local preview runs on loopback port 5187 under a finite lease ending at 22:37 Bangkok on 2026-10-16; its exact process identity and expiry are recorded in the resource ledger.

Commands and ownership receipts are retained under `D:/codex-temp/2026-10-14/task/knowledge-search`. Heavy native work is serialized behind the unchanged 4 GiB admission guard with two logical processors, a 2 GiB managed heap cap and finite leases. The previous task-owned preview was stopped for rebuilding. Unrelated files and user applications are preserved. No push or deployment is authorized for this change.
