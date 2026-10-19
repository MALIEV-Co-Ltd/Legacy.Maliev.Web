# Knowledge article orientation and defect imagery

## Brief and spatial decision

User invoked the impeccable layout skill for the Geometry and Workholding article and requested warpage/chatter images, a clearer accurate hole-depth image, and a straight micrometer in the inspection photograph. This is a Read-mode refinement inside the existing MALIEV design system.

Assessment: the old contents auto-fit grid creates three columns at the supplied desktop width; five long Thai links wrap unevenly and obscure reading order. Geometry's nav had only an accessible label, without a visible heading. The existing left rail already provides global and article navigation, so no additional sticky sidebar or overlay is needed.

The shared article contents now uses one column at all sizes, full-width links with at least44px targets, restrained separators, a visible heading, and grey hover/focus. Natural DOM/tab order is retained. Geometry's five short labels retain the exact existing anchor IDs; section headings and factual prose remain. Four existing TOCs that lacked visible labels gained their existing localized 'On this page' text. No new breakpoint, container width, font, dependency or navigation behavior is introduced. Other articles inherit the same linear contents layout.

## Images and engineering meaning

- Built-in image_gen generated three selected photographs. Prompts and source paths are in 2026-10-16-knowledge-reading-images.json. Outputs are the full1536x1024 and768x512 WebP variants for dimensional-inspection, warpage and chatter. Root inspected each generated output. The replacement micrometer has one straight anvil/spindle/barrel axis; the plate bows freely above a reference surface; the machining wall has repeated periodic surface bands.
- New issue figures sit directly after the warpage and chatter section headings, before their explanatory paragraphs. They have localized alternative text and captions. The bow is explicitly exaggerated, with no claimed cause or measured flatness. The chatter example does not establish a unique cause or roughness value. These are illustrative images, not customer work or inspection records.
- The pending corrected hole-depth SVG retains conventional section hatching, centre lines, external dimensions and simplified internal-thread envelopes, with clearly named BLIND THREAD, THROUGH HOLE and COUNTERBORE panels. The old screenshot's decorative teeth are absent. A rasterized technical image is not substituted for deterministic engineering geometry; final visual review is required.
- Prior uncommitted visual accuracy corrections remain preserved and included in the validation scope. No previous full-suite pass is reused as evidence for these changed files.

Primary references rechecked: Sandvik milling vibration (https://www.sandvik.coromant.com/en-us/knowledge/milling/vibration), NPL callipers/micrometers GPG40 (https://www.npl.co.uk/resources/gpgs/callipers-micrometers), and ISO6410-1 drawing-convention scope (https://www.iso.org/standard/12750.html). They support the explanatory content, not a claim that a generated photograph is a validated measurement or a complete production setup.

## Static checks completed

Impeccable context loaded once for the named route; PRODUCT.md and DESIGN.md retain authority. Independent source assessment and the pre-/post-edit layout detector were run; detect returned[] both times. This does not establish rendered usability. All existing TOCs have visible strong/h2 headings. Eight affected EN/TH resource sets have unique keys and complete matching Localizer keys. All19 SVGs parse, with no previously identified UTF-8 mojibake. Six new/replacement WebP variants decode at their expected sizes. git diff --check passes.

## Runtime validation and ownership

NOT RUN: fresh Release build, focused regression/browser checks, scoped C#format, full affected Web suite, and actual desktop/mobile visual review. The ten-minute resource-admission wait expired at3,016,988KiB available versus the unchanged4,194,304KiB requirement. No build/test/format worker started; only the finite admission queue existed and it exited. Previous-turn build/test results do not validate these new source changes.

Required continuation when admission permits: run D:/codex-temp/2026-10-14/task/knowledge-reading-layout/verify.ps1, then full.ps1; inspect the resulting desktop/mobile EN/TH captures and the latest hole/anodizing diagrams in the actual browser. Fix any failures, rerun affected checks, then make coherent commits. The six new cases are uncompiled/unexecuted. The CSS layout has source/detector evidence only, not a verified browser result. No commit created; no deployment. Preview remains stopped. New KnowledgeReadingLayoutBrowserTests has6cases: EN/TH×1440/375 geometry orientation, actual keyboard/hash destinations and image decoding; EN/TH micrometer hero decoding. Existing page coverage checks all guides, culture, actions, section links and image decode. No API/auth/database/pricing contracts changed. No push or deployment.

A finite serialized verification queue uses the unchanged4GiB admission guard,2logicalprocessors and2GiBmanagedheap limit. Exact process identity/start/expiry lives in D:/codex-temp/2026-10-14/task/knowledge-reading-layout/resources.json. Prior task-owned preview is stopped; other tasks and user applications are preserved. Final cleanup verified that the current admission queue exited and no task-created processes remain. No task-owned browser tabs are currently open. Generated source images remain in the standard generation location and project WebP copies are saved.


## Authorized production release follow-up (2026-10-17)

Resource admission later succeeded. Fresh Release build passed with zero warnings/errors; the corrected focused suite passed 65 tests; the full Web suite passed 1,208 tests with zero failures/skips. Scoped formatting passed for both touched browser-test files. JavaScript tests passed 48/48 and frontend tests 78/78. Security scanner tests passed 26/26 with zero high-confidence current-tree findings. Final native desktop/mobile English/Thai captures were reviewed. Fresh loopback specifications requests returned HTTP 200 in both languages with literal count placeholders. All temporary native workers exited; the review tab was closed and the temporary preview was stopped before packaging.

The image test initially assumed responsive naturalWidth equals the source pixel width; it was corrected to check successful decoding, the intended responsive source and 3:2 aspect ratio. File-level checks still confirm actual source dimensions. Final browser review of the anodizing asset corrected missing endpoint arrowheads by separating its dimension paths; that static-only correction was XML-parsed and visually rechecked. A focused all-guide image decode rerun is recorded in the release verification note. Production deployment is now explicitly authorized; exact publication/rollout receipts are retained outside Git under D:/codex-temp/2026-10-14/task/knowledge-production-release.
