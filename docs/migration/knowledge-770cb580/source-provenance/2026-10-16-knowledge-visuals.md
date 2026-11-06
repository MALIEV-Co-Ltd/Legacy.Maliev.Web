# Knowledge Center image expansion

## Scope and provenance

The 19 English/Thai Knowledge Center pages each have a distinct process photograph generated with the built-in `image_gen` workflow. The index reuses each article's smaller image as its navigation thumbnail. Photos are illustrative teaching scenes, not customer-portfolio evidence or documentary proof of a facility. Thai people, plain black staff clothing, correct readable MALIEV wordmarks where appropriate, and unmodified third-party equipment branding follow the owner instructions. No bottle-inspection bracket images are reused.

The full prompt set, optimized paths, dimensions and SHA-256 hashes are in `2026-10-16-knowledge-photo-prompts.json`. Original PNG outputs remain in the built-in generated-images directory; the repository stores WebP derivatives for delivery. Encoding changes format and size only; no photo content was edited outside image generation.

Sixteen new native SVG diagrams explain geometry, cutting calculations, material condition, creep, manufacturing routes, heat-treatment allowances, thermal measurement, printing and reverse engineering. The three existing SVG guides remain and now use the same accessible viewer. Diagram captions and placement metadata are in `2026-10-16-knowledge-diagram-captions.json`. Numerical examples retain article assumptions; qualitative curves and exaggerated geometry are identified explicitly. No machining limits, material properties or accuracy guarantees are inferred from illustrations.

## Presentation

Photos and diagrams fill the article width. Photo subjects are protected within a 16:9 responsive crop. Full-size and 768px images use srcset; directory thumbnails and diagrams load lazily, while the lead photo is prioritized. Width/height attributes reserve space. Each generated photo has localized alt text and a caption. Thumbnail alt text is empty because the adjacent link text names the destination.

SVG diagrams retain a readable minimum width in a labelled, keyboard-focusable horizontal viewport. A localized full-size link is provided. The existing 72rem shell breakpoint and navigation behavior remain unchanged. No new JavaScript or external image/font dependencies are introduced.

## Validation

- All 38 WebP files decode at the expected dimensions; each lead image is below 400 KB.
- The 19 lead photographs total approximately 2.5 MB; smaller variants support mobile and directory use.
- XML parsing, SVG viewBoxes, unique IDs, restricted content and approximate text bounds passed for all new diagrams.
- Every image reference exists; all 19 article photographs are distinct. Complete nonempty Thai resources cover the new alt text, captions and full-size links.
- Source/browser regression coverage checks article imagery, diagram viewports, directory thumbnails and image decode in English/Thai on desktop/mobile.
- Release build: zero warnings and zero errors.
- Focused source, SEO, shared-shell and browser checks: 49 passed, zero failures or skips.
- Scoped formatting: `dotnet format whitespace --verify-no-changes --no-restore` passed for the two changed C# test files.
- Rendered contact-sheet review covers all 16 new SVGs. Desktop and mobile Thai index screenshots show full-width photographs, contained thumbnails and readable navigation. Final regenerated screenshots confirm the refined feed-result units, cantilever deflection and heat-treatment allowance diagram without a misleading diameter dimension.
- Full affected Web test suite: 1192 passed, zero failures or skips, 8m57s.
- JavaScript unit suite: 38 passed. Frontend suite: 78 passed.
- Staged whitespace checks passed for each coherent commit. Asset provenance hashes match all 19 optimized photographs.
- All 15 observed processes belonging to the full test run exited; no test server or browser workers remain.
- Local preview HTTP checks passed for Thai index, English feeds guide, WebP thumbnail and SVG. The loopback preview has a separate finite 45-minute lease.

Resource policy remains unchanged: heavy local validation requires at least 4 GiB free physical RAM. Image generation ran remotely; serial WebP encoding completed in 12.36 seconds and exited. Subagents authored SVG source only and created no helper processes. The previous local preview expired and its process and watcher are absent. A new preview serves this worktree on loopback port 5187 until 21:34 Bangkok time on 2026-10-16. No push or deployment is authorized for this visual expansion.

## Commands and evidence

```powershell
dotnet build Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false /nodeReuse:false -warnaserror
dotnet test Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~KnowledgeCenterBrowserTests|FullyQualifiedName~KnowledgePageMigrationSourceTests|FullyQualifiedName~ApplicationAreaShellSourceTests|FullyQualifiedName~TechnicalSeoTests|FullyQualifiedName~SeoBusinessContractTests|FullyQualifiedName~ApplicationPageMigrationSourceTests'
dotnet format whitespace Maliev.Web.Tests/Maliev.Web.Tests.csproj --no-restore --include Maliev.Web.Tests/KnowledgeCenterBrowserTests.cs Maliev.Web.Tests/KnowledgePageMigrationSourceTests.cs --verify-no-changes --verbosity minimal
dotnet test Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-build --blame-hang-timeout 5m --blame-hang-dump-type none
node --test --test-concurrency=1 Maliev.Web.Tests/JavaScript/*.test.cjs
node --test --test-concurrency=1 Maliev.Web.Tests/Frontend/*.test.cjs
```

Logs, TRX reports, the resource ledger and screenshots are retained outside the repository under `D:/codex-temp/2026-10-14/task/knowledge-visuals`. Screenshots include the English/Thai index and feeds guide at 375px and 1440px, plus each vector image and a complete diagram contact sheet.
