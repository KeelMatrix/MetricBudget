# KeelMatrix.MetricBudget

Verify observed metric cardinality in .NET tests and CI. Run your normal workload, observe the metric series it
actually emits and how many distinct values each tag carried, and fail when an instrument identity exceeds an explicit
observed-series or per-instrument-identity distinct-value budget - without a collector, exporter, or observability backend.

The package is `KeelMatrix.MetricBudget`.

## Install

```bash
dotnet add package KeelMatrix.MetricBudget --version 0.1.0
```

The package works from a test project or a CI step and needs no collector, exporter, or backend. It targets
`net8.0` and `netstandard2.0`.

## Quick Start

```csharp
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

using MetricBudgetSession session = MetricBudgetSession.Start(
    new MetricBudgetOptions()
        .ForInstrument("My.Service", "http.client.request.duration", budget =>
        {
            budget.MaxObservedSeries = 50;
            budget.Tag("server.address").MaxDistinctValues = 5;
        }));

await ExerciseApplication();

MetricBudgetReport report = session.Complete();
report.AssertWithinBudget();
```

A failing run names the instrument, the breached budget, the observed count, the configured limit, and the
offending tag keys, and never prints tag values.

The [package README](src/KeelMatrix.MetricBudget/README.md) is the complete user guide: budgets, outcomes, examples,
lifecycle, safety bounds, privacy, telemetry, supported targets, and troubleshooting.

## What is verified

- the distinct **observed series** an instrument produced during the exercised workload;
- the distinct **values** each configured tag key produced;
- tag-set identity that is order-independent and deterministic, so the same combination never counts twice;
- explicit outcomes for a budget breach, invalid configuration, an instrument that was never observed, and a
  session that observed no measurements at all.

A report describes the workload you ran. It never claims to prove the maximum cardinality production can produce,
and it is not an observability-cost estimate.

## Repository layout

```text
src/KeelMatrix.MetricBudget/            the shipping library package (net8.0, netstandard2.0)
tests/KeelMatrix.MetricBudget.Tests/     behavior, edge, concurrency, safety, telemetry, integration, resource tests
tests/KeelMatrix.MetricBudget.PackageConsumer/ isolated package-consumer smoke test (consumes the built .nupkg)
samples/KeelMatrix.MetricBudget.Sample/  runnable sample
docs/                                    series identity, observed-vs-production, safety, privacy, troubleshooting
src/MetricBudget.Probe.*, tests/MetricBudget.Probe.*  Phase 0 feasibility probe and its downlevel host
```

The `MetricBudget.Probe.*` projects are development evidence from the feasibility phase. They are not packable,
they are not part of the product API, and nothing in the shipping library depends on them. They also keep their own
`MetricBudget.Probe.sln`, which is the solution their evidence commands use.

`KeelMatrix.MetricBudget.sln` contains the library, its tests, and the probe projects. The sample and package-consumer
projects are intentionally outside that solution: each restores the package from a local feed, so each is run
explicitly after packing rather than as part of a normal solution build.

## Documentation

- [docs/series-identity.md](docs/series-identity.md) - the exact, deterministic series identity rule.
- [docs/observed-vs-production-cardinality.md](docs/observed-vs-production-cardinality.md) - what a passing run
  does and does not tell you.
- [docs/safety-bounds.md](docs/safety-bounds.md) - the bounded-memory contract and incomplete states.
- [docs/troubleshooting.md](docs/troubleshooting.md) - "instrument not observed", selector ambiguity, and other
  recurring problems.
- [docs/privacy-and-telemetry.md](docs/privacy-and-telemetry.md) - what reaches logs, reports, and telemetry.
- [PRIVACY.md](PRIVACY.md) and [SECURITY.md](SECURITY.md) - privacy summary and vulnerability reporting.
- [CONTRIBUTING.md](CONTRIBUTING.md) - contributor setup and repository validation.

## Build and test

```text
dotnet restore KeelMatrix.MetricBudget.sln
dotnet build KeelMatrix.MetricBudget.sln -c Release
dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release
```

The test project targets `net8.0` and `net472`. The `net472` run executes the library's `netstandard2.0` asset
against `System.Diagnostics.DiagnosticSource` 8.0.1 on .NET Framework, which is the only way to exercise that
asset honestly.

Package validation:

```text
dotnet pack src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj -c Release -o artifacts/packages
dotnet run --project tests/KeelMatrix.MetricBudget.PackageConsumer -c Release
dotnet run --project samples/KeelMatrix.MetricBudget.Sample -c Release
```

Both package-backed projects restore `KeelMatrix.MetricBudget` from `artifacts/packages` and reference no project in
this repository. See [docs/DEV.md](docs/DEV.md) for the complete reproducible validation sequence.

## License

MIT. See [LICENSE](LICENSE).
