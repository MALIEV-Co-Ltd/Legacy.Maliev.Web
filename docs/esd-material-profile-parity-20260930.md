# Current ESD offers and physical profiles

Bounded producer/public-offer slice for Web #418, from main
`148c1a06a4b6b06b6c14a313c9105a8a091f2b90`. Source authority is committed
`7b4703576cf183abc09cf558148b5c8afb97d20c`, reviewed within the isolated source
mirror at `bed10c7d15e0698e0b75f1329d0f312937f5d77f`. Original repositories were
not edited or fetched into. This does not complete the broader #275 economics,
#300 geometry-authority or #309 STEP/batch bundles.

## Behavior and boundaries

- Replace current PC-ESD offers with Fiberon PA612-ESD and eSUN ABS-ESD, including
  source density/cost, physical profile composition, preference overrides and
  black-only material variants. Preserve all unrelated material settings.
- Embed exact source/resolved JSON bytes; the source/resolved paths are binary
  Git attributes to preserve manufacturer-composition SHA-256 across checkouts.
  Their path-specific `cr-at-eol` whitespace attribute treats the preserved CRLF
  terminator as a terminator, without exempting actual trailing spaces or changing
  repository-wide whitespace checks.
  `esd-profile-evidence.v1.json` records exact filament/process composition
  digests. Overrides are bounded, limited to existing reviewed settings, validated
  after merging, and participate in physical profile admission.
- Keep public Thai/English descriptions, procurement caveats and finished-part
  ESD testing requirements. New committed WebP illustrations are not samples,
  test results or proof of stocked/certified material. The old PC-ESD production
  photo remains visibly labeled as a previous sample.
- The historical benchmark approval for PC-ESD is not transferable. Both new
  benchmark decisions explicitly have no approved profile and remain automation
  ineligible pending operator review; all other approvals are unchanged.
- Advance immutable profile/policy identifiers to invalidate stale profiles and
  tickets. The policy identifier is not evidence that the remaining eight-tier,
  per-unit manufacturing-floor and commercial calculator changes are migrated.

## Independent root validation

Release build: zero warnings/errors. Current focused material/profile/pricing/
localization/SSR/endpoint checks: 191 passed, zero failures/skips. Full affected
solution: 2,481 passed, zero failures/skips, 8m25s. Evidence is ignored at
`TestResults/esd-catalog-final-focus/esd-catalog-final-focus.trx` and
`TestResults/esd-catalog-final-full/esd-catalog-final-full.trx`.

Actual Chromium tests first failed four cases because the current offers could
not load details. After source-derived data/assets were migrated, all four pass:
each material on English desktop and Thai mobile, denied optional consent,
manufacturer/density/caveat text, loaded nonzero image dimensions, no details
error, close visibility and focus return. Saved full-page screenshots are ignored
under the test output's `TestResults/esd-material-browser`. These are synthetic
public-page browser checks, not production-derived Aspire acceptance or a full
light/dark/viewport matrix; operating-system color preference alone does not
prove application theme behavior.

The first full run preserved 2,478 passes and exposed exactly three stale
catalog/version expectations. Their corrections preserve negative controls and
explicitly block unreviewed benchmark automation. Exact-pinned Customer/Auth
producer preparation ran before the final full suite; no persistent database
or service was started by preparation. Required protected PR/head/main CI and
remaining browser/economics/Aspire acceptance are still separate gates. No
deployment, cutover, notification or production analytics event was authorized.

Deterministic asset build passed. JavaScript browser-module tests passed 157/157;
the existing CNC/geometry regression suite passed 565 with its 10 existing skips
(no CNC expansion changes). npm production audit found zero vulnerabilities.
Whole-solution formatting and all four projects' transitive NuGet vulnerability
audit passed. Complete changed/new text secret scan found no leaks; binary
illustrations were verified against their committed source objects.
