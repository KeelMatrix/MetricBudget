# KeelMatrix.MetricBudget

This repository contains a bounded feasibility probe for the proposed MetricBudget test-time metric-cardinality
verifier. It is **not** a shipping package: there is no public product API, no package metadata, and no publishable
artifact yet.

The probe observes real `System.Diagnostics.Metrics` behavior through `MeterListener` and records raw, reproducible
numbers for the questions that decide whether the product can be built as specified:

1. Instrument coverage for `Counter`, `UpDownCounter`, `Histogram`, `ObservableCounter`,
   `ObservableUpDownCounter`, and `ObservableGauge` across every supported measurement type, plus the concrete
   failure mode for unsupported types.
2. Deterministic, order-independent, culture-independent series identity, including null, empty, duplicate, and
   oversized tag values.
3. Whether tag data must be copied out of the measurement callback, with measured buffer aliasing.
4. What a process-global `MeterListener` actually observes when several tests run in parallel, and what disposal
   really stops.
5. Bounded canonicalization cost and explicit safety-cap behavior at one million distinct series.
6. Whether `netstandard2.0` is viable, including the exact `System.Diagnostics.DiagnosticSource` version, the
   dependency closure it adds, and the public API gap against the `net8.0` framework.

## Running the probe

The whole probe runs with one command from the repository root:

```text
dotnet run --project tests/MetricBudget.Probe.Runner/MetricBudget.Probe.Runner.csproj -c Release
```

Add `--probe=<name>` to run a single section: `coverage`, `lifecycle`, `tags`, `isolation`, `scale`, `parity`.

The runner prints the environment, one line of raw numbers per observation, and a `PASS` / `NARROW` / `FAIL`
verdict per acceptance item, then a summary block. Verdicts are feasibility evidence, not release readiness.

## What the probe has established

- All six instrument kinds deliver measurements for `byte`, `short`, `int`, `long`, `float`, `double`, and
  `decimal`, and the callback type delivered always matches the instrument's measurement type.
- Unsupported measurement types fail loudly: `Create*` throws `InvalidOperationException` listing the supported
  types, so there is no silently unusable instrument.
- `net8.0` exposes no public `Measure` or `RecordMeasurement` member on `Instrument` or `Instrument<T>`.
  A listener can only observe measurements through `SetMeasurementEventCallback<T>`.
- Observable callbacks run once per `RecordObservableInstruments()` call, never at listener start, and the tags
  they attach arrive on the measurement callback.
- The measurement callback receives a `ReadOnlySpan<KeyValuePair<string, object?>>` that aliases caller-owned
  memory, and the backing buffer of a `TagList` is reused across calls. Tag data must be copied out during the
  callback; it cannot be retained.
- `MeterListener.Dispose()` does **not** stop measurement delivery for instruments that listener already enabled
  on .NET 8.0.31: after disposal the callbacks keep firing and `Instrument.Enabled` stays `true`. Explicit
  `DisableMeasurementEvents` per instrument is required for containment, so the probe session disables every
  instrument it enabled before disposal.
- Instrument publication is process-global while delivery is scoped: an unselected listener observes other
  parallel tests' measurements, a listener that selects its own instruments does not.
- Canonicalization stays linear and bounded: one ordinal sort of the delivered entries and one key string per
  series, with per-series memory in the low hundreds of bytes and a stable `sha256` descriptor replacing
  oversized tag values.
- The series safety cap produces an explicit bounded-state result, never a silent undercount: at a cap of
  250,000 with one million generated combinations, the tracker reported 750,000 untracked observations, kept
  the tracked set at the cap, and never matched an untracked key to an existing series.
- `netstandard2.0` assets of `System.Diagnostics.DiagnosticSource` expose the same public metrics surface as the
  `net8.0` framework from version 8.0.0 onward, at the cost of two direct package dependencies
  (`System.Memory`, `System.Runtime.CompilerServices.Unsafe`) and five supporting packages.

## Series identity rule

```text
seriesKey  = entries joined by U+001F, entries sorted with StringComparer.Ordinal
entry      = {keyLength}:{key} U+001E {descriptorLength}:{descriptor}
descriptor = {CLR type full name}:{value formatted with CultureInfo.InvariantCulture}
null value = descriptor "null"
null key   = reserved token @null-key
oversized  = {type}#chars={count}#sha256={hex} when the descriptor exceeds the configured bound
```

Duplicate keys are retained, so a tag set is a sorted multiset rather than a set. The CLR type is part of
identity, so `int 1` and `string "1"` are different series even though a type-blind formatter would merge them.

## Layout

- `src/MetricBudget.Probe.Core` — candidate identity rule, bounded tracker, and the `net8.0` observation session.
- `src/MetricBudget.Probe.NetStandard` — the same identity rule and tracker compiled for `netstandard2.0`
  against `System.Diagnostics.DiagnosticSource`, plus the parity session.
- `tests/MetricBudget.Probe.Runner` — the single executable probe entry point.

All projects are non-packable. `IsPackable` is `false` for every project, and the repository intentionally
contains no workflow configuration.
