# Material pricing coordinator lifetime

## Current implemented scope (2026-10-02)

The coordinator-only lifetime repair is implemented on `031244c096cceb4b9842f6812d0a762c62253b3d`, with private Defaults `6ea131df4bcf8d213d7d121cb8c865697bee7420` and Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. The accepted backend observer implementation is unchanged. The coordinator still uses its four-argument pricing overload. No progress snapshot, observer activation, Razor/resource or financial change is included.

Scope: [Web #457](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/457), the reached disposal defect from the frozen #454 consumer probe, not observer activation or parent #380 completion. Only the coordinator and new lifetime tests belong to this repair. The existing two consumer files remain frozen and their observer-selection requirements remain RED. Source `c4dd00b323a350f06a27afbce03dcbbcc56e16fb` and `744d3c5225e7cbf7a88af5c1f4fdb47442e2c325` remain pending; this correctness slice does not resolve their full owner behavior.

The actual coordinator restores and persists using the registered protected session store, actual ticket issuer and resin kernel. Controlled pricing gates exercise cooperative cancellation, ignored cancellation, late success/fault, queued configuration and restore. Synthetic historical geometry is not actual FileService admission, physical manufacturing, Redis distribution or authentication proof.

Implemented repair: capture a stable lifetime token before disposing its source; link each pricing attempt and queued configuration wait to caller plus coordinator lifetime. Await pricing with cancellable wait and observe eventual dependency faults without any continuation that publishes authority. Check cancellation after protected awaits and before ticket issuance/persistence and final quote publication. Caller cancellation preserves the original token. Disposal does not wait on the state gate held by pricing. A queued operation does not mutate after observed disposal. Invalid configuration still rejects before quote invalidation.

Restore shares the pricing boundary. Upload already owns lifetime-linked cancellation and its late-result cleanup must stay intact. Remove needs a local post-provider lifetime fence before state mutation/pricing; cancellation is not proof that an already submitted provider/storage effect was undone. No new external retry, compensation, arithmetic, ticket version, DTO, session CAS/TTL, observer, Razor or resource behavior is authorized.

An ignored-cancellation dependency may finish in the background; the repair stops the producer wait and prevents its result from reaching ticket/publication code, not forcibly terminates that dependency or rolls back arbitrary side effects. Existing protected-store CAS/readback remains authoritative. A cancellation racing an already submitted protected write is not claimed to roll that write back.

These token checkpoints are not an atomic cancellation transaction. Ticket issuance is synchronous, with a cancellation check after the preceding protected read and another before the protected authorization write. A synchronous callback could cancel during issuance; that check rejects the following write, but cannot undo callback effects. Cancellation can race the check-to-write interval or an executing store write; already committed authorization is not retroactively removed. The final check and assignment likewise do not constitute a renderer cross-thread synchronization guarantee. A future display consumer still needs renderer dispatch and generation fencing. No such guarantee or new lock around arbitrary provider callbacks is introduced here.

Validation follows build first, new focused RED, minimal runtime, fresh build/focus/affected coordinator suites and scoped format/secrets/diff. Full/browser is explicitly held by root's exclusive window; no commit/push.

## Executed chronology

- Initial Release: zero warnings/errors. New lifetime focus `TestResults/material-pricing-lifetime-red/lifetime-red.trx`: 9 executed, 6 assertion failures, 3 passing controls, zero errors/skips. Failures reached active/restore pricing, ignored-cancellation wait (caller and disposal), late successful publication and queued configuration after disposal. Late dependency failure, cooperative caller token identity and invalid-configuration preservation were existing green controls. Cleanup always releases controlled background gates and waits for their exit.
- Runtime Release: zero warnings/errors. `TestResults/material-pricing-lifetime-green/lifetime-green.trx`: 9/9 passing, zero skips. `TestResults/material-pricing-lifetime-affected/lifetime-affected.trx`: 156/156 passing, zero skips, including existing workflow upload/pricing/interop and comparison continuity tests with their original assertions untouched.
- Frozen consumer diagnostic `TestResults/material-pricing-lifetime-held-consumer/held-consumer.trx`: 9 executed, 7 passing and 2 failures (configuration/restore observer-route selection), zero errors/skips. This is intentionally held feature evidence, not an unfiltered green suite or a completed #380/#454 display feature. Neither frozen consumer file changed.
- Final post-format Release: zero warnings/errors; `TestResults/material-pricing-lifetime-final-focus/lifetime-final-focus.trx` 9/9 passing and `TestResults/material-pricing-lifetime-final-affected/lifetime-final-affected.trx` 156/156 passing, zero errors/skips. Scoped formatter and whole-solution `--verify-no-changes` both exited zero. `git diff --check` passed. Gitleaks scanned each of the three owned files with redaction, no suppressions and zero findings.

All commands run from `B:/maliev-legacy/.worktrees/web-esd-fulfillment-proof-20261002` with `MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-esd-fulfillment-proof-20261002/.dependencies` and `UseLocalMalievDependencies=true`:

```powershell
dotnet build Legacy.Maliev.Web.slnx -c Release --nologo
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~MaterialPricingLifetimeTests' --logger 'trx;LogFileName=lifetime-green.trx' --results-directory TestResults/material-pricing-lifetime-green
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~MaterialPricingLifetimeTests|FullyQualifiedName~InstantQuotationWorkflowUploadTests|FullyQualifiedName~InstantQuotationWorkflowPricingTests|FullyQualifiedName~MaterialComparisonRepricingContinuityTests|FullyQualifiedName~InstantQuotationWorkflowInteropTests' --logger 'trx;LogFileName=lifetime-affected.trx' --results-directory TestResults/material-pricing-lifetime-affected
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~MaterialProgressCoordinatorContractTests' --logger 'trx;LogFileName=held-consumer.trx' --results-directory TestResults/material-pricing-lifetime-held-consumer
```

Static commands (same private environment):

```powershell
dotnet format Legacy.Maliev.Web.slnx --no-restore --include Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflowCoordinator.cs Legacy.Maliev.Web.Tests/MaterialPricingLifetimeTests.cs
dotnet format Legacy.Maliev.Web.slnx --no-restore --verify-no-changes
git diff --check
gitleaks dir Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflowCoordinator.cs --redact --no-banner
gitleaks dir Legacy.Maliev.Web.Tests/MaterialPricingLifetimeTests.cs --redact --no-banner
gitleaks dir docs/material-pricing-lifetime-design-20261002.md --redact --no-banner
```

Root will independently build/focus this workspace and create a logical commit containing only the coordinator, `MaterialPricingLifetimeTests.cs`, and this document; the frozen consumer test/design pair remains untracked, not weakened or committed. Root will cherry-pick the bounded commit into its integrated candidate and run unfiltered full verification there. The affected test selection above is a positive class list, not an exclusion/skip of an old committed test. No unexcluded coverage/full/browser result is claimed for this isolated lifetime slice.

Root independently reviewed the complete coordinator diff, all nine new cases and
this document. Fresh Release warnings-as-errors passed zero warnings/errors.
The first root positive class selection matched only the nine new cases; it is
focused evidence, not affected-suite acceptance. Corrected positive selection
using the exact existing class names above executed156 PASS, zero failures/skips,
in `TestResults/root-material-lifetime-complete-affected/affected.trx`.
Whole-solution verify-only formatting, three owned-content redacted secret scans
and diff checks passed. Only these three files are eligible for the local logical
commit; the two frozen feature-RED files remain unstaged. Full integration and
protected-head/post-main CI are subsequent gates, not inferred from these results.
