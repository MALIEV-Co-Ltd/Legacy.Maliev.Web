# Protected geometry profile continuity — Web #423

## Goal and authority

Preserve caller-supplied sampled geometry through protected session snapshots,
storage and restoration, and bind quote tickets to those exact inputs. Parent
Web #275 remains open. This prerequisite changes no price calculation, material
or manufacturer profile, upload policy, worker, public DTO or route.

The immutable geometry is derived from a validated browser claim and matched
upload content hash. Retaining and hashing its sampled profiles establishes
change continuity; it does not prove server reconstruction, manufacturing
authority, resin calibration, or accuracy of the browser's geometry calculation.

Original approved RED/runtime base: `4483e3eb768e8ee59a4a2cf6ba9e2f452e0f3ed0`.
Current green protected-main base: `c56d9cb9b4b2c351aab4893cda2ee89364ef3721`.
Owned worktree: `B:\maliev-legacy\.worktrees\web-protected-profile-continuity-20261001`.
Branch: `codex/protected-geometry-profile-continuity-20261001`.
After #422 exact-main CI `36753999030` succeeded, root authorized the serialized
rebase. The four owned files were stashed explicitly, rebased and restored without
conflicts; fresh Release/focused/full/static gates follow before integration.

## Individual committed-source mappings

Source checkpoint: `bed10c7d15e0698e0b75f1329d0f312937f5d77f`, read only in
`B:\maliev-legacy\.artifacts\source-commit-mirror-20260930.git`.

| Full SHA | Bounded mapping |
| --- | --- |
| `047a7b673c397c388d974b030d55e8eee5b88d69` | Introduces `UnsupportedAreaProfileMm2` in geometry, handlers and worker output. |
| `a3c5c4a53907e03ac202620cc051393c20fc28e3` | Later consumes unsupported-area samples for resin support; monetary/algorithm changes are excluded here. |
| `a15177b36ecb8a729e5eae61238ca9a073d10184` | Introduces the protected geometry-digest ticket field. |
| `ad393a5c5c74174a2a8f8989d0bf03263c20e42d` | Target #258 port introduces the sampled-profile application contract. |

Introduction was inspected with committed-object `git log -S`; source ancestry
checks against the checkpoint returned success for all three source commits.
These are individual field/continuity mappings, not whole-SHA dispositions.

## Concrete defect and path

`InstantQuotationGeometryClaim` accepts unsupported cross-sectional area in mm²
at uniformly sampled heights. It is not a second array of height coordinates.
Normal claims require 64 samples; above 250,000 facets they require 24. A present
unsupported profile must have that count and contain finite nonnegative values.
Absent evidence is not an explicit all-zero profile.

The application immutable geometry already retains that profile, and pricing
already forwards it. `InstantQuotationSessionStore.cs` omits it from all four
paths: validation's reconstructed claim, `ToPersistedPart`, `ToPart`, and
`ClonePart`. The coordinator actually executes `PutAsync` then `GetAsync` before
pricing and again before issuing authorization. Thus an application-only
constructor test does not cover the production loss.

`AdditiveQuoteTicketService.CreateGeometryDigest` includes scalar geometry but
none of the area, perimeter or unsupported sample arrays. Replaying a ticket
against changed samples can currently succeed when scalars and money are equal.

## Compatibility decisions

- Keep stored-session envelope `Version = 1`, cache prefix and protector purpose.
  Add an optional field only to the private persisted geometry record.
- Old absent/null/empty unsupported profiles normalize to the existing unavailable
  empty domain list. Never fabricate 64 zero values.
- Preserve explicit valid all-zero profiles and their sample count.
- Pass the present profile into validation before accepting a stored payload.
  Malformed protected payloads follow the existing rejection/removal behavior.
- Do not eagerly rewrite valid old sessions on reads. Their next ordinary,
  owner-bound expected-revision `Put` persists the additive private field.
- Preserve session/submission IDs, `CreatedAt` and absolute three-hour expiry.
  `UpdatedAt` retains the existing monotonic successful-write rule. Stale writes
  still lose; wrong owners cannot disclose or mutate the session.
- New line schema and protection purpose become `additive-line-quote.v4` and
  `Maliev.Web.AdditiveLineQuote.v4`. Old v3 lines require re-pricing; never accept
  their incomplete digest under the new continuity semantics.
- Keep order v2, upload v1, commercial policy and physical-profile versions
  unchanged. Existing order authorization paired with old v3 lines fails closed;
  a newly issued order must bind its newly issued exact v4 line-ticket set.
- No session v2 is introduced: existing strict-v1 readers would remove it.
  An old writer can nevertheless lose the optional profile. Mixed-writer rollout
  remains a root-owned release consideration, not solved by this patch.

## Approved minimal runtime ownership after observed RED/root gate

Only `Legacy.Maliev.Web.Infrastructure/InstantQuotationSessionStore.cs` and
`Legacy.Maliev.Web/Services/AdditiveQuoteTicketService.cs` are presently needed.
No public geometry shape, coordinator, calculator, manufacturer profile or policy
edit is required. Existing application immutable-list and claim-validation
contracts are reused.

The geometry digest must encode all three profiles with tags, explicit lengths,
ordered invariant round-trip numeric values and an unambiguous unavailable marker.
An absent/null/empty unsupported profile has one unavailable representation;
an explicit 64-zero profile has a distinct representation. Scalar fields already
in the digest remain included. Encoding boundaries must not allow concatenation
collisions. This digest is a private ticket detail, not a public wire expansion.

## Test design and meaningful breaks

New `ProtectedGeometryProfileContinuityTests.cs` uses the real distributed session
store, real quote-ticket service and real coordinator. Only the byte-cache boundary
is controlled; keys are ephemeral and no persistent key ring or external service
is used. These serialization/continuity unit tests are not Redis integration proof.

1. Create/snapshot 64 and 24 samples with literal values 10 at index 1 and 11 at
   the last index. Caller-array mutation must not change retained immutable values.
   This catches `ClonePart` dropping the profile.
2. Seed a protected payload with a valid optional profile and read exact ordered
   values. This independently catches the private deserializer/restorer loss.
3. Successful `Put` retains the profile, rejects the same stale revision, preserves
   IDs/creation time, advances revision once and keeps the original expiry.
4. Present short, negative and named nonfinite malformed profiles are rejected and
   removed. A JSON string `NaN`/`Infinity` is deliberately malformed for the numeric
   array; unknown-field dropping currently hides that malformed input.
5. Old absent/null/empty fields remain unavailable, do not trigger a rewrite and
   remain owner scoped. Explicit 64 zeros remain explicit, not unavailable.
6. Change area, perimeter or unsupported samples while keeping scalar geometry
   and real resin quote money identical. Unchanged authorization passes; replay
   against each changed profile fails. Resin is merely the existing unchanged-money
   control, not an economics or manufacturing acceptance case.
7. Check fractional sample values 10.25/11.5 under en-US/fr-FR, and distinguish
   unavailable support from an explicit zero array. Numeric boundaries `[1,23]`
   versus `[12,3]` must not collide.
8. Build an actual v3-purpose/schema line with the same ephemeral provider. New
   authorization rejects it and freshly issued lines/order still validate.
9. Run the actual coordinator restoration `Put → Get → pricing → ticket` sequence
   with the real protected store and ticket service. A narrow observed pricing
   boundary delegates to real pricing; assert the profile consumed and the final
   protected session rather than substituting a recording session store.

Reuse existing session owner/expiry/concurrent-CAS and ticket changed-settings,
exact-line-set, FDM receipt replay and protected-money guards. Full submission
regression verification remains required after runtime release. The expanded gate
also verifies the actual new v4 protection purpose/schema and rejects an old v2
order correctly bound to a v3 line; merely mismatching the order's line set would
not prove the migration boundary.

## Execution ledger

Ruling: root initially authorized tests/design only, then reviewed the terminal
RED and approved the two runtime files above, followed by focused/full/static
verification. Root also approved the existing pinned boundary preparation helper
and uniquely owned disposable Testcontainers fixtures. No commit, rebase, shared
outputs or persistent/provider data writes are authorized in this lane. Root later
authorized only the serialized rebase above once the active full run terminated.

- Preflight: clean owned linked worktree; own ignored dependency clones only.
- Defaults: `6ea131df4bcf8d213d7d121cb8c865697bee7420`, clean detached clone.
- Contracts: `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`, clean detached clone.
- Release build: 0 warnings, 0 errors.
- Existing focused baseline: 29 passed, 0 failed, 0 skipped.
- Initial diagnostic: 16 failures, 3 passes; one 24-sample create fixture passed
  the default count 64 and was corrected. The diagnostic TRX is retained, not
  claimed as the reviewed RED gate.
- Reviewed test-only run: 18 intended failures, 3 old-session compatibility
  guards passed, 0 skipped (21 cases). `TestResults/423-red-reviewed.trx`.
- Scoped `dotnet format --verify-no-changes --no-restore --include
  Legacy.Maliev.Web.Tests/ProtectedGeometryProfileContinuityTests.cs`: exit 0.
- Expanded pre-runtime RED: 20 intended failures, 3 compatibility passes, 0 skipped
  (23 cases). `TestResults/423-red-expanded.trx`; the two extra failures were the
  new v4 purpose/schema and correctly bound old-line/order replay checks.
- Approved runtime repair: optional profile retention/validation across all four
  session paths; tagged, length-delimited invariant-R three-profile digest; only
  line schema/protection purpose advances to v4.
- Post-repair Release build: 0 warnings, 0 errors.
- Focused session/ticket/coordinator/upload/submission suite: 146 passed, 0 failed,
  0 skipped. `TestResults/423-focus-green.trx`.
- Private producer, authentication and both seed-tool Release builds: each
  0 warnings, 0 errors; the preparation helper started/migrated no service/database.
- Browser asset CI: 157 module tests passed without skips; existing engine suite
  565 passed, 0 failed, 10 skipped because optional private CAD/saved-evidence
  fixtures are unavailable. CNC is excluded; these skips are not acceptance proof.
  Asset generation produced no tracked changes.
- Scoped `gitleaks stdin --redact` on both runtime diffs, new tests and this design:
  no leaks found.
- Original-base full suite: 2504 passed, 0 failed, 0 skipped, 8m57s;
  `TestResults/423-full.trx`. All four required private boundary binaries were set.
- Rebased exact-main Release build: 0 warnings, 0 errors; private dependency clones
  remained clean at their exact pins. Rebased focus: 147 passed, 0 failed, 0 skipped;
  `TestResults/423-main-focus.trx`.
- First rebased full run: 2538 passed, 1 failed, 0 skipped, 12m01s;
  `TestResults/423-main-full.trx`. Existing
  `SelectingUploadedPartsShowsOnlyTheirOwnPrintTime` timed out at line 222 waiting
  for exactly two uploaded-part rows, before selected print-time assertions.
- Isolated unchanged browser diagnostic: that same test passed in 7 seconds;
  `TestResults/423-main-browser-diagnostic.trx`. The failure is not consistently
  reproduced; timing/contention is a hypothesis, not an established root cause.
  No UI, worker, runtime, test or timeout change was made in response.
- Whole-solution `dotnet format Legacy.Maliev.Web.slnx --no-restore
  --verify-no-changes`: exit 0 with the owned absolute workspace props in the
  environment. `git diff --check`: exit 0. Scoped redacted gitleaks: no leaks.
- Unchanged rebased full retry: 2539 passed, 0 failed, 0 skipped, 9m35s;
  `TestResults/423-main-full-retry.trx`. The failed full and isolated diagnostic
  remain preserved; no root-cause claim or timeout/source relaxation is made.
  Root owns independent integration acceptance. No commit or push has been created.

Commands use `--configuration Release` and absolute
`-p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-protected-profile-continuity-20261001/.dependencies`
with `-p:UseLocalMalievDependencies=true` for builds. Tests run `--no-build
--no-restore`; TRX files stay in this worktree's ignored `TestResults` directory.

Exact fresh-main commands (from the owned worktree):

```powershell
dotnet build Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-protected-profile-continuity-20261001/.dependencies -p:UseLocalMalievDependencies=true -v:minimal
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~ProtectedGeometryProfileContinuityTests|FullyQualifiedName~InstantQuotationSessionStoreTests|FullyQualifiedName~AdditiveQuoteTicketServiceTests|FullyQualifiedName~InstantQuotationWorkflowUploadTests|FullyQualifiedName~InstantQuotationSubmissionTests" --logger "trx;LogFileName=423-main-focus.trx" --results-directory TestResults -v:minimal
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release --no-build --no-restore --logger "trx;LogFileName=423-main-full.trx" --results-directory TestResults -v:minimal
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~InstantQuotationWallThicknessRealUploadBrowserTests.SelectingUploadedPartsShowsOnlyTheirOwnPrintTime" --logger "trx;LogFileName=423-main-browser-diagnostic.trx" --results-directory TestResults -v:normal
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release --no-build --no-restore --logger "trx;LogFileName=423-main-full-retry.trx" --results-directory TestResults -v:minimal
```

Full tests set `MALIEV_PROFILE_PRODUCER_DLL`, `MALIEV_PROFILE_SEED_DLL`,
`MALIEV_PROFILE_AUTH_DLL` and `MALIEV_PROFILE_AUTH_SEED_DLL` to this worktree's
own reviewed Release binaries. The existing preparation script verifies their
pins and confines dependency clones to this worktree. Synthetic PostgreSQL/Redis
Testcontainers fixtures are disposable and are not production/data-parity proof.

Excluded: resin 10% browser/15% server authority decision, monetary/profile changes,
browser/admitted geometry improvements, #300/#309, CNC/STEP, UI/JS, metadata,
GitHub mutation, persistent/cloud data, deployment, commit/push and other worktrees.
