# Troubleshooting

Every report starts with the outcome and lists the configured rules, the instruments each rule selected, the
observed counts, the configured limits, and the tag keys involved. Start there: the diagnostic is written to be
read from a failing test log without any extra tooling.

## `NoMatchingInstrument`

At least one configured rule selected no published instrument.

- Check spelling and case. Meter and instrument names are matched exactly and are case-sensitive.
- Check that the workload actually creates the instrument; a rule for a path the test never reaches will not
  match.
- Check that the `Meter` is still alive. Disposing a meter removes its instruments from the process.
- Check the order: the session observes instruments created before and after it starts, but it must be started
  before the measurements you want to verify.
- If the same code is used in several tests, make sure each test's session uses the same names its workload uses.

`MeterListener` cannot hide other tests' instruments from a running session, but it only delivers measurements
from the instruments this session enabled, so an unrelated instrument name is harmless - it simply does not match.

## `NoMeasurementsObserved`

The instrument exists but delivered nothing.

- Observable instruments deliver measurements only when their callbacks run. If the application under test has no
  metrics SDK collecting them, call `session.RecordObservableInstruments()` where you want the collection to
  happen. Call it even when OpenTelemetry or another metrics listener is active: observable callbacks deliver to the
  specific listener that requested collection, and another listener's collection does not populate this session.
  The BCL never invokes observable callbacks when a listener starts.
- Counters and histograms only record when the instrumented operation runs. A request that short-circuits, a
  cache hit, or an early return can skip the measurement.

## `InvalidConfiguration`

One instrument identity matched more than one rule. Overlaps such as `ForMeter("My.Service", ...)` together with
`ForInstrument("My.Service", "queue.depth", ...)` leave no unambiguous budget, so the instrument is not observed
and the report names every rule it matched. Make the rules disjoint.

Invalid *options* fail earlier and louder: starting a session with no rules, with a rule that declares no limit, or
with a non-positive safety bound throws `MetricBudgetConfigurationException` before anything is observed.

## `ObservationIncomplete`

A safety bound was reached, or a delivered value/tag set could not be admitted under the supported identity policy.
The report names the bound or incomplete state, and every affected count is a lower bound. Per-tag results expose
the specific series, tag-set, tag-key, instrument-admission, and per-key-value completeness flags; any such flag
makes that tag's `IsWithinBudget` false. See
[safety-bounds.md](safety-bounds.md). Raise the relevant bound when the workload is representative; narrow the
workload when the cardinality is the finding you were looking for. Unsupported tag values are rejected without
calling user-defined `ToString()`.

Instrument identity admission and identity component length are distinct cases. An oversized instrument that no
rule selects is ignored and does not invalidate the session. An oversized selected instrument is rejected and the
diagnostic names `MaxInstrumentIdentityLength`; a selected identity or physical instance rejected by an admission
bound names `MaxTrackedInstrumentIdentities` or `MaxTrackedInstrumentInstances` instead. A retained result affected
by a rejected same-name identity or physical instance has `InstrumentTrackingIncomplete = true`, so focused budget
assertions fail closed for that target. Unrelated instruments' incomplete tracking does not invalidate a fully
tracked target.

## A tag budget never triggers

- Tag budgets are matched against exact tag keys. Compare the key in your budget with the keys in the report.
- A configured key the workload never delivered is reported with `WasObserved = false`, so a typo is visible. It
  does not fail the session by itself; the message "configured, never delivered by the workload" is the signal to
  check the key.
- A measurement can deliver a `null` tag key. Those measurements are counted and reported, but no tag budget can
  name them, so fix the caller rather than the budget.

## A test fails in parallel CI but passes alone

Another test in the same process publishes instruments with the same identity. Identity is meter name, meter
version, instrument name, and instrument kind, so generic names collide across libraries. Use specific names, and
keep each session scoped to the workload it verifies. A completed session disables every instrument it enabled,
so it cannot leak delivery into later tests.

## The report says a series count I cannot explain

- Repeated measurements with the same tag values are one series; distinct combinations add series.
- Tag order never matters, but duplicate keys do: the same key delivered twice produces a different series from
  the same key delivered once.
- The CLR type of a tag value is part of identity, so `1`, `1.0`, and `"1"` are three different values.
- A null tag key, the literal key `null`, and the empty key are three different identities.

See [series-identity.md](series-identity.md) for the exact rule, and run the sample or a focused test with the same
tag keys to reproduce the count.
