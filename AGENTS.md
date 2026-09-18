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
dotnet format --verify-no-changes
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
- **Privacy.** Reports, diagnostics, assertion messages, and telemetry contain tag keys and counts, never tag
  values, metric values, or workload samples. Only fixed-size digests of series and tag values are retained.
- **Bounded accounting.** Series and per-tag distinct-value accounting stay inside explicit safety bounds. A
  bounded run is reported as incomplete, counts become documented lower bounds, and an untracked observation is
  never matched to an existing series.
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
  Tests and local development run with telemetry opted out: `tests.runsettings` covers the test host and the
  committed `keelmatrix.telemetry.json` at the repository root covers every other local run path, including the
  sample and the package-consumer smoke test. That file is deliberately tracked; the root `.env` files are not.
- **Packaging.** The package ships only the library assembly, XML docs, README, icon, license, symbols, and
  SourceLink. No probe, test, sample, generated report, or local-only file may be packed.

## Scope

Do not add features the specification does not require: no raw tag-value exposure, no JSON report export, no
vendor-specific types, no production-cost estimation, or hosted components. The tag-triggered release workflow and
version/changelog validator are repository release infrastructure; they do not add product behavior. Package identity,
version, target frameworks, dependencies, and metadata are defined by the product specification; change them only with
an approved specification change.
