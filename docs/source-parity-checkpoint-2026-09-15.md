# Source parity checkpoint through `7c416cc`

## Boundary

This checkpoint inspects committed Git objects from the read-only source
repository `R:\maliev-web` and records the complete delta after the previous
`5ac7d045c51194edd9e64d8564f1b726b001be34` checkpoint. The source `origin/main`
head is `7c416cc8cfd27ef7440e7c046529011630276807` and the ordered delta contains
19 commits. Uncommitted source work is excluded. The machine-readable manifest
is [source-parity-delta-through-7c416cc.json](source-parity-delta-through-7c416cc.json).

All portable behavior is represented by protected-main Legacy commits. A source
deployment script, SQL Server deployment script, or source-only test host is not
copied into the .NET 10 repositories; the target uses its PostgreSQL service
boundary, GitOps workflow, deterministic asset pipeline, and target-native tests.

## Commit dispositions

| Source commit | Disposition | Target evidence |
| --- | --- | --- |
| `14a7f42608526b1773643e8050a2ec9c775e6723` | Migrated thumbnail and active-part behavior | Web `3913dc07d8af3120a6fd6832559ab71acea4b154` (PR #257) |
| `362308b605ff94878f684258ade46c67ae0b08ee` | Migrated qualification contract and PostgreSQL persistence | QuotationService `fce640dc2d232c46c357b8cc3bad82ec5fc0d359` (PR #44) |
| `e78ab85594e688aed223f54ef31c7b6df399a735` | Migrated employee qualification workflow | Intranet `a94c9cb472ce0c5fce323ebbff1a9c03705f2bd8` (PR #173) |
| `99462c12c33fc62da281fc90fc61a8ee458d0d7a` | Migrated receipt privacy and persistence hardening | QuotationService `fce640dc2d232c46c357b8cc3bad82ec5fc0d359` (PR #44) |
| `d957038a208b2cbdf1604010a6d82f1d74f1a06c` | Classified source-only design document | No runtime artifact; pricing implementation follows |
| `2128395c31a1ebfe94f9d0ced846bdc861fe62f4` | Classified source-only implementation plan | No runtime artifact; source deployment instructions excluded |
| `05c42bcf7529e9d606803c271490baed59546bc1` | Migrated physical FDM build-profile pricing | Web `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` (PR #258) |
| `047a7b673c397c388d974b030d55e8eee5b88d69` | Migrated layer-overlap support estimation | Web `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` (PR #258) |
| `813f8ffdcd6b2cb1ed567f5264807133ddd482b7` | Migrated occupied-plate resin pricing | Web `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` (PR #258) |
| `a15177b36ecb8a729e5eae61238ca9a073d10184` | Migrated signed server-owned additive totals | Web `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` (PR #258) |
| `a22cc927ef943e9a9f4ab9dbdd6337befdd06eab` | Migrated fail-closed FX and destination/shipping rules | Web `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` (PR #258) |
| `60d3677264e046fd73028ff3ccb75097d153bedb` | Translated verification into target-native tests | Web `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` (PR #258) |
| `941cd2757246527108eb0dddce7582d1a47ea442` | Migrated additive/CNC analysis isolation | Web `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` (PR #258) |
| `2d126d1d55a3240e40678136e6aa621c7f10ed47` | Merge-only; both parents are classified | No independent runtime patch |
| `cc5b3864b8752f5e5e421f01a68d8925c940021a` | Migrated resin process review disclosure | Web `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` (PR #258) |
| `9e9c7c5edfd2574ef721623951e3f50de1272a6a` | Merge-only; resin parent is classified | No independent runtime patch |
| `d15a44524698e315bc712139ef4dc2e8e35f3013` | Migrated country-outage rendering, pending CNC skeleton state, and asset path correction | Web `1a31a06b48dee6aa1ab7ecf2d81e122b3c62c3b0` (PR #262) |
| `3ce9936b6e39f41c000e542f005a18645f445a8a` | Migrated API-owned quotation attribution producer/consumer contract | Web `17626967d0eb93142e1cf517038c01af4e1381b6` (PR #260); QuotationService `89a6d653302561fedc5756cf0843458971d50a7b` (PR #45) |
| `7c416cc8cfd27ef7440e7c046529011630276807` | Migrated self-hosted Outfit typography and generated assets | Web `1a31a06b48dee6aa1ab7ecf2d81e122b3c62c3b0` (PR #262) |

## Validation evidence

- Web Outfit migration: Release build **0 warnings, 0 errors**; full
  `Legacy.Maliev.Web.Tests` **2,028 passed, 0 failed**; focused font tests
  **2/2**; browser modules **538 passed, 0 failed, 9 skipped**; format and
  asset checks clean.
- Web attribution migration: Release build **0/0**; full suite
  **2,028/2,028**; focused attribution **412/412**; format clean.
- Web additive pricing migration: Release build **0/0**; full suite
  **2,026 passed**; focused **127 passed**; browser modules **137 passed**;
  CNC engine **538 passed, 9 skipped**; format and asset checks clean.
- QuotationService attribution migration: Release build **0/0**; focused
  **30 passed**; full suite **185 passed, 0 failed, 0 skipped**; format clean.
- All 21 Legacy canonical `main` branches are aligned with `origin/main` and
  their latest required CI validation is green. Intranet has only preserved
  untracked review artifacts; no tracked code is dirty.

## Explicit exclusions and gates

- No Legacy application deployment, traffic change, GKE mutation, Secret
  Manager write, PostgreSQL write, or production cutover was performed.
- Production SQL Server remains authoritative. The exact-25 data refresh and
  Aspire production-derived data gate remain deferred by owner direction.
- Source-only deploy scripts, SQL Server migrations, and incompatible .NET 8
  test-host artifacts remain excluded; their target-native equivalents are
  listed in the manifest and existing historical parity ledgers.

## Tracking

Issue [#263](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/263)
tracks this checkpoint and is closed only after the protected-main PR and
post-merge verification. The daily migration automation should use this
source head as its next comparison boundary and classify only newer committed
source changes.

## Classified source commits

The two design/plan commits and two merge commits above have no independent
runtime artifact. They remain in the manifest with explicit non-runtime or
merge-only dispositions so no source SHA is silently dropped.
