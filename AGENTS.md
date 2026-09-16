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
- `tests/MetricBudget.Probe.Net472Host` — the bounded downlevel host. It runs the netstandard2.0-compiled session
  on .NET Framework against the package's netstandard2.0 `System.Diagnostics.DiagnosticSource` asset, because a
  net8.0 process always binds that session to the framework assembly.

## Commands

Full probe from the repository root:

```text
dotnet run --project tests/MetricBudget.Probe.Runner/MetricBudget.Probe.Runner.csproj -c Release
```

Single section while iterating (faster than the full run):

```text
dotnet run --project tests/MetricBudget.Probe.Runner/MetricBudget.Probe.Runner.csproj -c Release --no-build --probe=tags
```

Downlevel host for the netstandard2.0 decision (after a Release build):

```text
tests/MetricBudget.Probe.Net472Host/bin/Release/net472/MetricBudget.Probe.Net472Host.exe
```

Release build of everything:

```text
dotnet build MetricBudget.Probe.sln -c Release
```

`Directory.Build.props` treats Release warnings as errors, so a Release build is the cheapest way to catch
analyzer and nullability regressions.

## Invariants

- Every project stays non-packable; the repository produces no package.
- The session types (`MeterObservationSession`, `NetStandardObservationSession`) and every other probe type are
  probe-only. They are public so the runner and the downlevel host can drive them, and nothing here may be lifted
  into a product surface as-is.
- The probe observes only BCL metrics APIs. Do not add a telemetry, exporter, or vendor dependency.
- Tag data is copied out of the measurement callback. Never retain or store the callback span.
- Bounded accounting must stay honest: when a safety cap is reached, produce an explicit incomplete/bounded
  state and never report an untracked series as an existing one.
- All accounting is shared with measurement threads. The tracker's series map, per-tag value maps, counters, and
  flags are written under one writer lock, session summaries are single consistent snapshots, and a summary must
  never report more tracked series than observed measurements.
- The runner never lets an exception escape. A probe that throws becomes a named failed gate, and any failed gate
  makes the runner exit non-zero. A finding that records measured platform behavior (for example that
  `MeterListener.Dispose` does not stop delivery on .NET 8) is an observation, not a gate, so its `FAIL` never
  changes the exit code.
- The printed tag identity rule and the code must agree. A null key is the bare `null` marker in the key field; no
  field is described as length-prefixed unless it is.
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
