# Web startup/security migration source bundle — 2026-10-06

This unvalidated source bundle adapts five original commits to the current independent Web host. The original four-commit cohort covers 13 historical changed-path entries (nine distinct paths); private observation adds four paths, for 17 entries. Source coverage is not hosted acceptance, deployment readiness, or whole migration-ledger closure.

Integrated base: `d1577dfa29cd292b293089e201ba97f053b1534a` (initial source review used historical `dc3b629bdb28e8e124bb99db064fb8227be6a9bb`). Original checkpoint: `135e526d0dab85c415b3afdcefd7b70fe2c82e2f`. Original Git objects were inspected read-only; private configuration values and credential files are not copied.

| Original commit | Complete changed paths | Current disposition |
| --- | --- | --- |
| `550eaeaac33f5e4fb016a48fc79459bc536a8f79` | `Maliev.Web/Maliev.Web.xml`; `Maliev.Web/ServiceExtensions.cs`; `Maliev.Web/Startup.cs`; `Maliev.Web/tests/VerifyDevelopmentDataProtection.ps1` | Distinct Development discriminator. Encrypted Redis/certificate persistence replaces filesystem/SQL storage. Actual Program restart and cross-environment rejection proofs replace the source-text probe; generated XML has no runtime contract to transplant. |
| `a40ae59b9a9cc4182ed397eb02f18f861180c51f` | `Maliev.Web.Tests/ConfigurationSecurityTests.cs`; `Maliev.Web/Startup.cs`; `Maliev.Web/appsettings.Development.json`; `Maliev.Web/appsettings.json` | Platform TLS validation retained; architecture checks reject bypass callbacks. Both checked-in configurations remain byte-identical; focused checks cover credential fields and connection strings. |
| `79fa2e5c55a384b543a2aee11ab917119f84182b` | `Maliev.Web.Tests/RecaptchaCredentialProviderTests.cs`; `Maliev.Web/Maliev.Web.xml`; `Maliev.Web/RecaptchaSettingsValidator.cs`; `Maliev.Web/Startup.cs` | Required identifiers, legacy KeyID fallback, optional absolute readable CredentialsPath, and actual Program startup regressions. Native Google SDK builder consumes the path lazily. Generated XML is not copied. |
| `34b401e11b3aff96e5c5db2071fb5581048e7a6a` | `Maliev.Web/Startup.cs` only | Current Razor Pages/Blazor static SSR uses compiled pages. Neither environment enables runtime compilation. Development/Production HTTP tests exercise English/Thai compiled Knowledge routes. The original Development runtime-edit facility is an architectural disposition; it is not reproduced or claimed as a hot-reload proof. No deployment/probe change belongs to this commit. |
| `2f60d6b077860132db0aa97a5e0508c1d0407bdd` | `Maliev.Web.Tests/ProductionObservabilityTests.cs`; `Maliev.Web/ObservabilityDiagnosticMiddleware.cs`; `tools/diagnostics/ProductionObservabilityMiddleware.cs`; `tools/diagnostics/tests/ProductionDiagnosticsTests.cs` | Adopt the reviewed shared private-observation implementation and register it in actual Program. HTTP consumer proofs cover original connection admission, synthetic logging, throttling and exact registered health response identity. The historical Startup registration is checkpoint context; it was not a changed path in this commit. |

The nine inspected target inputs are `Legacy.Maliev.Web/Program.cs`, both appsettings files, Infrastructure `RecaptchaEnterpriseOptionsValidator.cs` and `RecaptchaEnterpriseVerifier.cs`, tests `RecaptchaConfigurationStartupTests.cs`, `RecaptchaEnterpriseVerifierTests.cs` and `AppSettingsCredentialBoundaryTests.cs`, and `docs/recaptcha-configuration-acceptance-20260930.md`. Additional glue is limited to current service registration, hosted focused evidence, the owned persistence proof, and this document.

## Data protection and compatibility

Production retains `Legacy.Maliev.Web`; Development uses `Legacy.Maliev.Web.Development`. Both retain required Redis and certificate encryption. Testing retains memory/ephemeral storage. No filesystem fallback, database dependency, shared Production purpose in Development, or unchecked certificate is added.

Development cookies generated with the former shared discriminator cease to decrypt and require reauthentication. Production discriminator and persisted-key contract remain unchanged.

The two new hosted-only tests start owned Redis and generate an ephemeral certificate. Each exercises actual Program, verifies encrypted key XML, disposes the first host, reopens the same key ring/certificate, decrypts the same payload, and proves opposite-environment rejection. Public compiled routes do not call Google or downstream producers. Outside GitHub Actions the fixture fails before starting a container; tests are not skipped.

## reCAPTCHA configuration and credential boundaries

`ProjectID` binds case-insensitively to `ProjectId`. Nonblank canonical `SiteKey` wins; a blank/missing canonical key falls back to legacy `KeyID`. Required identifier validation still uses actual `ValidateOnStart` and field-only diagnostics.

Blank/missing `CredentialsPath` leaves native SDK default ADC selection. A configured path must be fully qualified, exist as a file, and be readable. Startup errors mention only the field name, never its value or contents. The lazy native `RecaptchaEnterpriseServiceClientBuilder` receives the path. Startup does not parse credential JSON, invoke Build/BuildAsync, mutate environment variables, acquire Google credentials, or call Google APIs. A readable synthetic noncredential file proves this boundary. Runtime handles unreadable paths; no chmod/root-sensitive hosted unreadability oracle is claimed.

Assessment fields, token/action/score validity, cancellation, and redacted failure logging remain unchanged. The existing 16 verifier cases remain. Embedded service accounts stay unsupported. This bundle supersedes the earlier document's KeyID/CredentialsPath adaptation gap only after hosted acceptance; broader five-SHA cohort, Workload Identity/IAM readiness, and live credential validity remain open.

[Native builder API](https://docs.cloud.google.com/dotnet/docs/reference/Google.Cloud.RecaptchaEnterprise.V1/latest/Google.Cloud.RecaptchaEnterprise.V1.RecaptchaEnterpriseServiceClientBuilder). Runtime package remains Google.Cloud.RecaptchaEnterprise.V1 2.20.0.

## Shared private observation adoption

Web selects the accepted ServiceDefaults producer `3c790ba6414b2a539f24aabb6948549ffd81a86b` consistently in normal CI, Docker, the joined Web graph and identity checks. The broad producer change from `6ea131df4bcf8d213d7d121cb8c865697bee7420` also contains authentication and redaction work; adoption requires fresh complete Web and joined validation. CompatibilityContracts, joined Auth/Catalog and dedicated historical producer graph pins remain unchanged. No shared producer source is edited.

Actual Program registers `AddPrivateRequestObservation("web")`; existing standard middleware applies the shared implementation before forwarded-header rewriting. The private GET `/internal/diagnostics/observability` probe requires the original connection peer to be loopback and exactly one GUID-N nonce. Valid probes return a synthetic 500 with uncached host identity and diagnostic nonce, emit the producer's synthetic/framework/critical signals, and avoid a duplicate Web incident. Denied requests and throttled 429 responses expose no host identity. The host-local throttle resets after one minute.

Only registered GET `/web/liveness` and `/web/readiness` receive completed-response process identity. `/web/aspire-liveness`, business routes, unsupported methods and retired root health paths do not gain that marker. Seven actual Program health cases prove host stability, independent host identities and these exclusions. TestServer peers are explicitly fixture-owned; these tests do not claim a real Kestrel network witness. The original diagnostic tools are represented by private HTTP consumer regressions rather than restored executable deployment probes.

Nineteen private consumer cases preserve actual Program while controlling only the original connection peer, clock, logger capture, health-check callback and retained Razor page filter. They cover denied admission, IPv4/IPv6 synthetic signals, singleton throttling/reset, independent host admission, late-header restoration and ordinary thrown/handled Error reexecution. The thrown Razor control rejects every extra `HandledOperationFailure`; returned-500 controls with and without a forged incident header still require one response-classification Error and no Web incident event. Captured state, formatted messages, scopes and exception types exclude sentinel query/cookie/auth data. Framework event 1 also receives the original exception separately; this capture is not a native logging-sink sanitization proof of that exception payload.

## Validation contract

Focused hosted group predicts 80 cases: the original 45 startup cases, seven health identity cases, 19 private observation cases, five dependency identity cases, two shared diagnostics cases and two incident cases. Current material/signed-link main `d1577dfa29cd292b293089e201ba97f053b1534a` has 3,361 declared cases; this bundle predicts 3,405 with 44 new rows. Combined focused groups predict 283 cases. Both retained material54 and signed-link35 focused groups and all corresponding full-suite rows remain required. These are source counts; no expanded count has executed.

Acceptance requires exact-head hosted build with zero warnings/errors, focused and relevant full suites with zero failures/skips, fresh joined validation, formatting/analyzers/audit, generated-inclusive raw coverage per assembly, individual retained TRX rows, secrets scan, and independent actual-source review. Pricing negative controls remain unchanged. No local SDK/build/test/container/browser/provider execution is authorized; source guards cannot replace hosted checks.

No deployment, runtime configuration application, IAM change, live API request, database migration, CNC feature expansion, Docker context publication, or whole source-ledger closure is included.

The original selected `7edcd961` completed-response observer could classify a thrown Razor request again after Web incident handling. Accepted producer `3c790ba` skips that response classification only when the framework-owned `IExceptionHandlerFeature.Error` exists; an incident header alone is insufficient. Producer fresh main run37396490198 passed711 full/76 focused and generated-inclusive raw3453/4293, with isolated produced-package consumer passing. That producer evidence does not accept the Web consumer or joined graph. The stronger actual Program controls, complete Web suite and joined65 cases must execute against this exact pin.


## Protected-main navigation observation correction

Fresh signed protected-main run `37398894770` on `d1577dfa29cd292b293089e201ba97f053b1534a` executed 3361 full rows: 3360 passed and one English/375 px Knowledge keyboard row timed out waiting for Load. All 203 independent focused rows, including signed35 and material54, passed; all nine build summaries had zero warnings/errors and raw generated-inclusive coverage passed. Formatting and vulnerability audit were skipped after the full-suite failure, so this main is not accepted. Pricing negative controls remain independently retained. No unchanged rerun is authorized.

The existing four-row browser regression now arms the exact destination/Load/30-second observer before Enter and records capped public-route, focus, load and navigation diagnostics if it fails. This reuses the exact earlier prepared navigation correction (`f10f4d5e4bd1f145d59bb99f18e199ae0e8d4b3a` test blob). It removes an observer-sequencing gap; the failed interleaving is not proven from the old run because it did not retain these diagnostics. There is no timeout inflation, assertion skip, route or UI runtime change. Diagnostic JSON is retained with the existing PNG artifact.

Startup and this causal observation correction will use one normal protected PR workflow, as coordinator-directed. Forecast counts remain 3405 full, 283 combined focused including 80 startup, and ten TRX files. Joined native actor/provider/state remains65. All are predictions until exact-head hosted proof. This candidate can replace the failed-main publication dependency only through coordinator review and actual successful required gates; no production deployment or whole source closure follows.


## Supported Google credential API corrective slice

PR492 source f568693 failed native joined37402265309 and actual image37402265301 at build with CS0618: the installed GAX CredentialsPath builder property is obsolete. Zero joined/image runtime rows executed; no unchanged rerun. The supported API now uses an explicit GoogleCredential loaded through CredentialFactory.FromFile<ServiceAccountCredential>().ToGoogleCredential(). Explicit mounted files are restricted to service-account JSON; other credential types fail closed at lazy assessment construction. Ambient ADC/workload identity remains unchanged when the optional path is absent. Startup still validates only absolute/readable path and does not parse JSON, acquire credentials or contact Google. No live key, environment or IAM change.

The retained startup proof covers blank paths and actual Program startup with a readable noncredential file plus an offline loader seam. Three added native SDK rows construct a generated synthetic service-account credential without token acquisition and reject authorized_user/external_account mounted documents. Required forecasts are now83startup,3408full/286focused across10TRX, plus65joined rows and actual packaged-image proof. These are predictions until the corrective exact head is executed.

Official API reference: https://docs.cloud.google.com/dotnet/docs/reference/Google.Apis/latest/Google.Apis.Auth.OAuth2.CredentialFactory
