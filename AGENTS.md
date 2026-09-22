# Repository guide

## What this repository ships

`src/KeelMatrix.MetricBudget` is the single shipping library. It observes `System.Diagnostics.Metrics`
measurements through `MeterListener` and reports **observed cardinality** for the workload a test ran, against the
budgets that test declared. It targets `net8.0` and `netstandard2.0` and is the only packable project.

The repository must never claim more than that: a report describes the exercised workload, not the maximum
cardinality production can produce, and it is not an observability-cost estimate.

## Navigation

- `src/KeelMatrix.MetricBudget` - public API, session lifecycle, accounting, diagnostics, assertions, telemetry.
  - `Internal/TagIdentity.cs` - the deterministic series identity rule; `docs/series-identity.md` documents it and
    must keep matching the code.
  - `Internal/MetricBudgetState.cs` and `Internal/InstrumentAccount.cs` - the single writer path for every counter,
    flag, and map.
  - `Internal/ReportBuilder.cs` - the outcome ladder; nothing-verified must never become a pass.
  - `Internal/Telemetry.cs` - the closed field allowlist and the shared telemetry sink.
- `tests/KeelMatrix.MetricBudget.Tests` - behavior, edge, concurrency, safety, telemetry, ASP.NET Core integration,
  and resource tests. Exercises both target frameworks.
- `tests/KeelMatrix.MetricBudget.PackageConsumer` - isolated smoke test that consumes the built `.nupkg` from a
  local package source and references no project.
- `samples/KeelMatrix.MetricBudget.Sample` - runnable sample.
- `docs/` - series identity, observed-vs-production, safety bounds, troubleshooting, privacy and telemetry, and
  the Phase 0 probe evidence.
- `src/MetricBudget.Probe.*`, `tests/MetricBudget.Probe.*` - Phase 0 feasibility probe and downlevel host. Keep
  them non-shipping development evidence; do not lift their types into the product API and do not change their
  recorded evidence.

## Commands

```text
dotnet restore KeelMatrix.MetricBudget.sln
dotnet build KeelMatrix.MetricBudget.sln -c Release
dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release
dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release --framework net472
dotnet pack src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj -c Release -o artifacts/packages
dotnet run --project tests/KeelMatrix.MetricBudget.PackageConsumer -c Release
dotnet run --project samples/KeelMatrix.MetricBudget.Sample -c Release
dotnet format KeelMatrix.MetricBudget.sln --verify-no-changes
dotnet list KeelMatrix.MetricBudget.sln package --vulnerable --include-transitive
```

`Directory.Build.props` treats Release warnings as errors, so a Release build is the cheapest way to catch
analyzer, nullability, and documentation regressions. The `net472` test target is not optional: it is the only
honest way to exercise the `netstandard2.0` asset.

`KeelMatrix.MetricBudget.sln` holds the library, tests, and probe projects. The sample and package-consumer projects
are deliberately outside it because they restore the built package from `artifacts/packages`; pack before running
either package-backed consumer. A plain solution build therefore never depends on the local package feed.
`MetricBudget.Probe.sln` remains the solution the Phase 0 evidence commands use.

## Invariants

- **Observed cardinality only.** Diagnostics say "observed series" or "observed cardinality". Never imply static
  proof of production maximums, and never present a default budget as universally safe.
- **Privacy.** Reports, diagnostics, and assertion messages contain application-supplied meter and instrument
  identifiers, tag keys, and counts, but never tag values, metric values, or workload samples. Review those
  identifiers before sharing output outside its intended audience. Bounded accounting retains only fixed-size digests
  of series and tag values. Hashing can still create ordinary transient managed strings and byte buffers; reusable
  scratch is cleared after each digest, but the package does not promise secure erasure from process memory.
- **Bounded accounting.** Series and per-tag distinct-value accounting stay inside explicit safety bounds. Filling a
  bound is not itself incomplete; when an additional observation or state entry is rejected, affected counts become
  documented lower bounds, and an untracked observation is never matched to an existing series. Conflicts and proven
  budget breaches have higher outcome precedence than incompleteness.
- **One writer path.** Measurement callbacks, session bookkeeping, and summaries all serialize on one lock, and a
  summary is a single consistent snapshot. An impossible account fails loudly instead of passing.
- **Explicit containment.** A session calls `DisableMeasurementEvents` for every instrument it enabled, because
  `MeterListener.Dispose` does not stop delivery. Never present disposal as the containment mechanism.
- **Honest isolation.** Instrument publication is process-global; delivery is scoped to the instruments a session
  enabled. Do not promise isolation the platform cannot give.
- **Deterministic identity.** Tag order never changes identity, duplicate keys are a sorted multiset, and the CLR
  type of a value is part of identity.
- **Telemetry.** Only the allowlisted aggregate fields may ever be attached, activation means a completed
  verification that observed at least one selected instrument, and telemetry failure must never change a result.
  Tests, local development, the sample, and the package-consumer smoke test run with
  `KEELMATRIX_NO_TELEMETRY=1`; `tests.runsettings`, the two program entry points, `docs/DEV.md`, and the package gate
  enforce the setting. The local `keelmatrix.telemetry.json` override is ignored and untracked; root `.env` files are
  also not tracked.
  Telemetry must not contain meter names, instrument names, tag keys, tag values, metric values, URLs, application
  or repository names, budget text, exception messages, stack traces, or file paths.
- **Packaging.** The package ships only the library assembly, XML docs, README, icon, license, symbols, and
  SourceLink. No probe, test, sample, generated report, or local-only file may be packed.

## Scope

Do not add features the specification does not require: no raw tag-value exposure, no JSON report export, no
vendor-specific types, no production-cost estimation, or hosted components. The tag-triggered release workflow and
version/changelog validator are repository release infrastructure; they do not add product behavior. Package identity,
version, target frameworks, dependencies, and metadata are defined by the product specification; change them only with
an approved specification change.
