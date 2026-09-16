# Repository guide

## Purpose

This repository is a Phase 0 feasibility probe for the proposed MetricBudget test-time metric-cardinality
verifier. It measures `System.Diagnostics.Metrics` and `MeterListener` behavior and produces raw evidence. It is
not a shipping package and defines no product API.

## Layout

- `src/MetricBudget.Probe.Core` — candidate series identity rule (`ProbeTagCanonicalizer`), bounded observed-series
  accounting (`ProbeSeriesTracker`), and the `net8.0` `MeterListener` session (`MeterObservationSession`).
- `src/MetricBudget.Probe.NetStandard` — the same identity rule and tracker compiled for `netstandard2.0` against
  `System.Diagnostics.DiagnosticSource`, plus `NetStandardObservationSession` for parity evidence.
- `tests/MetricBudget.Probe.Runner` — the only executable entry point. `Probes/` holds one file per acceptance
  item: instrument coverage, instrument lifecycle, tag identity, parallel isolation, cardinality scale, and
  target-framework parity. `ProbeReport.cs` holds the shared measurement and verdict helpers.

## Commands

Full probe from the repository root:

```text
dotnet run --project tests/MetricBudget.Probe.Runner/MetricBudget.Probe.Runner.csproj -c Release
```

Single section while iterating (faster than the full run):

```text
dotnet run --project tests/MetricBudget.Probe.Runner/MetricBudget.Probe.Runner.csproj -c Release --no-build --probe=tags
```

Release build of everything:

```text
dotnet build MetricBudget.Probe.sln -c Release
```

`Directory.Build.props` treats Release warnings as errors, so a Release build is the cheapest way to catch
analyzer and nullability regressions.

## Invariants

- Every project stays non-packable; the repository produces no package.
- The probe observes only BCL metrics APIs. Do not add a telemetry, exporter, or vendor dependency.
- Tag data is copied out of the measurement callback. Never retain or store the callback span.
- Bounded accounting must stay honest: when a safety cap is reached, produce an explicit incomplete/bounded
  state and never report an untracked series as an existing one.
- Sessions disable every instrument they enabled before disposal; `MeterListener.Dispose` is not sufficient for
  containment on .NET 8.
- Local diagnostics in the probe may include tag keys and counts, never raw tag values harvested from a session.
- Keep the runner deterministic apart from timing and memory numbers, which are reported as measurements.

## Validation

Restore, build Release, then run the runner from the repository root. The runner is the only full-corpus command.
Keep generated output (`bin/`, `obj/`) out of source control.

## Scope

Do not add a shipping package, public product API, CLI, workflow, analyzer, or package metadata here. If a probe
finding contradicts the product specification, record the finding instead of changing the probe to fit the
specification.
