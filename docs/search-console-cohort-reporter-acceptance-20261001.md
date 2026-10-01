# Offline Search Console query-cohort reporter

Tracking: Web #429; Workflows retains independent source-ledger ownership. Base `a9185aef9f3ea29502ca5b8f59b10712969f8991`, branch `codex/search-console-cohort-reporter-20261001`. This is an offline tool slice, not Web deployment, analytics publication, live Search Console acceptance or whole migration completion.

## Source and contract

Committed source: `dd9de3053cffd082df603950e8e3be3669d89ccb`, inspected only from isolated bare mirror through checkpoint `bed10c7d15e0698e0b75f1329d0f312937f5d77f`. No later source changes affect the reporter path through that checkpoint.

| Source path | Target path | Git blob parity |
| --- | --- | --- |
| Maliev.Web/tests/SummarizeSearchConsole.ps1 | tests/SummarizeSearchConsole.ps1 | Exact `553af237c62a45928ea2d5487690fde54e7f0ece` |
| Maliev.Web/tests/fixtures/search-console-queries.csv | tests/fixtures/search-console-queries.csv | Exact `494e7c9a4b9ef475cbca21305390ad0074aba8b8` |
| Maliev.Web/tests/SearchConsoleCohortReport.Tests.ps1 | tests/SearchConsoleCohortReport.Tests.ps1 | Explicit portable assertion adaptation, not byte parity |

Input remains CSV with all five English columns: `Top queries`, `Clicks`, `Impressions`, `CTR`, `Position`. Normalization uses invariant lowercase and collapsed whitespace for classification only; output retains trimmed original queries. Exclusive first-match precedence is customManufacturing, scanning, cnc, threeDPrinting; unmatched rows remain separate. Existing CNC classification is retained reporting behavior, not CNC quoting expansion.

Each cohort retains queryCount/clicks/impressions/query rows, recomputed `ctrPercent`, and impression-weighted `averagePosition` rounded to two places; zero impressions return zero CTR/position. Root output retains UTC `generatedAtUtc`, resolved local `sourcePath`, `sourceRowCount`, ordered cohorts and unmatched. Output parent directories are created as in source. Required-column/empty-export rejection and original conversion behavior remain unchanged: this slice adds no new numeric, query, file-size or path validation policy.

`sourcePath` and query rows are potentially sensitive local-report metadata. Do not publish reports or logs containing real exports automatically. The committed six-row historical fixture is the only source dataset used here. There is no Google API client, credential, live export, browser tag, lead outcome join or uploaded report.

## Execution and CI

```powershell
pwsh -NoProfile -File ./tests/SummarizeSearchConsole.ps1 -InputPath <local-export.csv> -OutputPath <local-report.json>
pwsh -NoProfile -File ./tests/SearchConsoleCohortReport.Tests.ps1
```

Tests use built-in PowerShell assertions rather than requiring Pester installation/import on Ubuntu. They execute the actual reporter with committed/synthetic inputs, create a uniquely owned temporary directory, and verify its resolved path before cleanup. Failure throws and terminates nonzero; no skipped or opt-in path exists. The reusable existing validate job unconditionally runs that same script with `shell: pwsh`, before the existing Node/assets/.NET stages. No extra action, network install, credential, CI permission, producer preparation or baseline gate change was introduced.

Current unchanged workflow dependency pins: Defaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`, validator Workflows `d7efac266bc66273bc45eab583618871292ecbd6`. Owned ignored local dependency clones use exact detached commits under `TestResults/.cohort-dependencies`; canonical and other worktree outputs are untouched.

## Test-first evidence

Before porting runtime, `pwsh -NoProfile -File tests/SearchConsoleCohortReport.Tests.ps1` exited1 with exact missing-reporter error. After port, an initial runner timestamp assertion failed because current PowerShell automatically converts JSON dates to DateTime. The test was corrected to handle either decoded DateTime or string, retaining bounded UTC timestamp checks; this was a test-harness diagnostic, not a reporter defect or separate product RED.

Final executable script suite: **10passed/0failed/0skipped**:

- Source six rows: printing2queries/269impressions/0clicks/26.93weighted position; all461impressions/2clicks conserved; CNC CTR14.29 recomputed rather than copying14.3; unmatched query preserved; source path and fresh UTC timestamp checked.
- Each of five required columns independently omitted and rejected without a report.
- Empty export rejected without a report.
- Zero-impression cohort and empty unmatched zero values.
- Dot-decimal positions parsed correctly under de-DE culture, with culture restored.
- Four overlapping queries demonstrate literal first-match precedence, exclusive counts and no unmatched duplication.

PowerShell parser inspected both files: zero errors. `git hash-object` exactly matched both source blobs. Reporter/CSV formatting is source-identical; `git diff --check` passed. No formatter or module is installed for this tool.

Because a CI input changed, fresh exact-pin application solution build was run rather than marking build not applicable:

```powershell
dotnet build Legacy.Maliev.Web.slnx -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\web-search-console-cohort-20261001\TestResults\.cohort-dependencies
# Same private dependency properties set process-locally for subsequent commands:
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~LegacyServiceDefaultsIdentityContractTests|FullyQualifiedName~PublishWorkflowPermissionContractTests' --logger trx --results-directory TestResults/search-console-workflow-contracts
```

Release **0warnings/0errors**; existing workflow/identity contract suites **7passed/0failed/0skipped**, `TestResults/search-console-workflow-contracts/natth_MALIEV-31USFIV_2026-10-01_09_37_53_net10.0.trx`. PyYAML parsed the actual reusable workflow and verified one unconditional exact `pwsh` invocation. Existing reviewed three CI branch callers, action pins, permissions and stages remain unchanged.

Whole-solution `dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore` completed exit0 with the same private properties. Redacted gitleaks scan of all five owned files passed. Signing-resource scanner read in-memory from the exact validator Workflows `d7efac266bc66273bc45eab583618871292ecbd6:scripts/JwtSigningResourceScanner.ps1` found no material in tracked/nonignored inventory. No scanner source, signing key or dependency version was changed. Source reporter/CSV Git blob parity, clean private clone statuses and final `git diff --check` reconfirmed.

The full affected script suite and relevant existing workflow suites were executed. Full application/browser/producer suites, production health/SEO, Lighthouse, deployed Ubuntu CI and live Search Console were not run locally: no application/asset/.NET input changed, and those broad/runtime/external boundaries are outside this offline lane. Root must observe protected CI for the exact integrated SHA before merge; Windows local PowerShell success alone is not a claim of executed Ubuntu CI.

No source ledger resolutions, grants, runtime configuration, service contracts, assets, providers, database/schema, original repositories, canonical files or operational automation were changed. No commit, push, GitHub write, deploy, live analytics or external notification is authorized by this evidence.

## Root integration acceptance

Root independently reviewed all five files and executed the actual portable reporter suite: **10 passed / 0 failed / 0 skipped**. Both exact source Git blobs were independently rechecked. The affected Release test/application graph rebuilt with **0 warnings / 0 errors**; existing workflow/identity contracts passed **7 / 7**, zero skips, at `TestResults/root-cohort-workflow/root-cohort-workflow.trx`. Whole-solution formatting, pinned signing-resource scan and redacted owned-file secret scan passed. Ignored local build/test evidence is retained and excluded from staging. Actual Ubuntu protected CI remains mandatory before merge; no executed Ubuntu, live Search Console or broader browser acceptance is inferred from local results.
