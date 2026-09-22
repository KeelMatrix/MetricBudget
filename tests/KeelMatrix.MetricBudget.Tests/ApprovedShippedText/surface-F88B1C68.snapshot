# Phase 0 probe evidence

This page is repository development evidence, not package documentation. It records what the feasibility probe
measured before the shipping library was designed. The probe projects
(`src/MetricBudget.Probe.*`, `tests/MetricBudget.Probe.*`) remain in the repository as non-shipping development
evidence: they are not packable, no product type derives from them, and nothing in `src/KeelMatrix.MetricBudget`
depends on them.

The findings below are what the probe measured on its evidence run, and they are the constraints the shipping
library honors. The text after this introduction is the probe's own report, kept as evidence.

---

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
verdict per acceptance item, then a summary block and a final runner verdict. Verdicts are feasibility evidence,
not release readiness.

The `netstandard2.0` decision also has a bounded downlevel host, which runs the netstandard2.0-compiled session
on .NET Framework against the package's own netstandard2.0 `System.Diagnostics.DiagnosticSource` asset:

```text
dotnet build MetricBudget.Probe.sln -c Release
tests/MetricBudget.Probe.Net472Host/bin/Release/net472/MetricBudget.Probe.Net472Host.exe
```

Every finding is either a run gate or an observation. A failed gate makes the runner exit non-zero with a
`GATE[FAIL]` line naming the finding, and a probe that throws is reported as a named failed gate instead of
aborting the run. An observation records measured platform behavior, so a `FAIL` there (for example the
`MeterListener.Dispose` result below) is an expected outcome and never changes the exit code.

## What the probe has established

- All six instrument kinds deliver measurements for `byte`, `short`, `int`, `long`, `float`, `double`, and
  `decimal`, and the callback type delivered always matches the instrument's measurement type.
- The supported measurement class is complete at those seven numeric types because the runtime validates the
  instrument type against one fixed set. Every sampled unsupported type (including `sbyte`, `ushort`, `uint`,
  `ulong`, `nint`, `Half`, `Guid`, `DateOnly`, `BigInteger`, an enum, a struct, and `int?`) fails loudly:
  `Create*` throws `InvalidOperationException` listing the supported types, so there is no silently unusable
  instrument. The unsupported class is a sample, not an exhaustive enumeration.
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
- Concurrent delivery is accounted for exactly. Observed-series accounting is serialized behind one writer lock,
  session summaries are consistent snapshots, and the parallel case asserts a strict contract: each of four
  unselected sessions that is delivered `4 x 5` distinct series must report exactly 20 observed measurements and
  20 tracked series, must never report more tracked series than observations, and must never lose a copied tag
  set. Sessions that select their own instruments observe only their own measurements.
- Canonicalization stays linear and bounded: one ordinal sort of the delivered entries and one key string per
  series, with per-series memory in the low hundreds of bytes and a stable `sha256` descriptor replacing
  oversized tag values. That claim is evidence-gated: raising the series count tenfold must not raise the measured
  per-series time or the per-series allocation past the gate, and the verdict fails when it does.
- The per-instrument-identity series safety cap produces an explicit bounded-state result, never a silent undercount: at a cap of
  250,000 with one million generated combinations, the tracker reported 750,000 untracked observations, kept
  the tracked set at the cap, and never matched an untracked key to an existing series.
- `netstandard2.0` assets of `System.Diagnostics.DiagnosticSource` expose the same public metrics surface as the
  `net8.0` framework from version 8.0.0 onward, at the cost of two direct package dependencies
  (`System.Memory`, `System.Runtime.CompilerServices.Unsafe`) and five supporting packages.
- The `netstandard2.0` implementation executes on a downlevel host, not only inside a `net8.0` process. On .NET
  Framework 4.8, with the package's own `netstandard2.0` asset of `System.Diagnostics.DiagnosticSource` loaded
  from the host's output directory, the netstandard2.0-compiled session delivers counter, histogram,
  upDownCounter and observableCounter measurements, tracks one series per delivered measurement, folds three tag
  orders into one series, runs observable callbacks once per `RecordObservableInstruments` call, and stops
  delivery through explicit `DisableMeasurementEvents`. The host prints the loaded asset's target framework, so a
  run in which the package's `net462` asset was loaded instead of the `netstandard2.0` asset cannot be reported
  as a pass.

## Series identity rule

```text
seriesKey  = entries joined by U+001F, entries sorted with StringComparer.Ordinal
entry      = keyField U+001E valueField
keyField   = {keyLength}:{key} for a delivered key, or the bare marker null for a null key
valueField = {descriptorLength}:{descriptor}
descriptor = {CLR type full name}:{value formatted with CultureInfo.InvariantCulture}
null value = descriptor text "null", so its valueField is "4:null"
oversized  = {type}#chars={count}#sha256={hex} when the descriptor exceeds the configured bound
```

Duplicate keys are retained, so a tag set is a sorted multiset rather than a set. The CLR type is part of
identity, so `int 1` and `string "1"` are different series even though a type-blind formatter would merge them.
Each field carries its own length prefix, and a length-prefixed field always starts with an ASCII digit, so the
bare `null` marker used for a null key can never be confused with a delivered key. A null key, the literal key
`"null"`, and the empty key are three different series, and a null value is not the string value `"null"`. The
runner prints the measured keys for those cases and fails if the printed rule and the measured rule disagree.

## Layout

- `src/MetricBudget.Probe.Core` — candidate identity rule, bounded tracker, and the `net8.0` observation session.
- `src/MetricBudget.Probe.NetStandard` — the same identity rule and tracker compiled for `netstandard2.0`
  against `System.Diagnostics.DiagnosticSource`, plus the parity session.
- `tests/MetricBudget.Probe.Runner` — the single executable probe entry point.
- `tests/MetricBudget.Probe.Net472Host` — the downlevel host that runs the `netstandard2.0` implementation on
  .NET Framework against the package's `netstandard2.0` asset.

Those observations were scoped to the Phase 0 probe projects and the probe-era repository state; they are not
current repository-wide claims. In the shipping repository, `src/KeelMatrix.MetricBudget` is the packable library
project, while the probe, test, sample, and package-consumer projects remain non-shipping evidence or validation
projects. The repository also has current CI and release workflow configuration in `.github/workflows/ci.yml` and
`.github/workflows/release.yml`; the release workflow is tag-triggered.
