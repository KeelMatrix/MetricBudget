# Safety bounds and bounded memory

The verifier observes exactly the failure class that can create huge numbers of combinations, so its own memory
has to be bounded and honest. Every session applies three explicit bounds.

| Option | Default | What it bounds |
| --- | --- | --- |
| `MaxTrackedSeries` (per instrument identity) | 100,000 | Distinct observed series retained for one instrument identity. |
| `MaxTrackedValuesPerTag` (per instrument identity) | 5,000 | Distinct values retained per tag key and instrument identity. |
| `MaxTagValueLength` | 256 | Length of a tag value's invariant text before it is replaced by a stable digest in the identity. |

The defaults exist so an accidentally explosive workload cannot make the verifier unbounded. They are not budgets,
they are not recommendations for an application, and reaching one never turns a failing workload into a passing
report.

The series bound is **per instrument identity**, not one shared pool for the session. A session that matches several
instrument identities can therefore retain up to *(number of matched instrument identities) x `MaxTrackedSeries`*
series descriptions, plus the per-tag value sets described below. Size CI memory from that product rather than from
a single per-instrument-identity `MaxTrackedSeries` value, and expect `ObservationIncomplete` as soon as **any**
instrument identity's bound is reached - the outcome is not deferred until every instrument is full.

## What happens when a bound is reached

- The observation is still counted as delivered.
- The observation is recorded as **untracked**; it is never matched to an existing series, so the session cannot
  silently undercount.
- The report names the bound, the instrument that reached it, and how many observations could not be tracked. Once
  any instrument reports an exhausted bound, the whole session outcome is `ObservationIncomplete`.
- Counts that depend on the exhausted bound become explicit lower bounds:
  `MetricBudgetInstrumentResult.SeriesTrackingIncomplete`,
  `MetricBudgetTagResult.ValueTrackingIncomplete`, and `MetricBudgetSafetyReport.IsComplete` state this
  machine-readably.
- The session outcome becomes `ObservationIncomplete`, which is not a pass, and
  `AssertWithinBudget()` fails with the bounded-state diagnostics.

A definite budget breach that is already proven is reported as `Violation`; reaching a bound never hides it.

## Memory characteristics

- Series identity and tag-value identity are held as fixed-size SHA-256 digests, not as the canonical text, so no
  raw tag value is retained.
- Retained memory grows with tracked series and tracked distinct values, and stops growing when the bounds are
  reached. Because the series bound applies per instrument identity, the retained series ceiling grows with the
  number of matched instrument identities: *(matched instrument identities) x `MaxTrackedSeries`* fixed-size
  digests, plus the per-tag value sets.
- Canonicalization cost is linear in the number of delivered tags per measurement plus one ordinal sort of those
  entries; the resource gate in the test suite measures per-measurement cost at representative series counts.

## Choosing tighter bounds

You can lower the per-instrument-identity bounds to fail faster on a workload you know well, for example the
per-instrument-identity `MaxTrackedSeries = 10_000`. Keep the bounds above the cardinality you legitimately expect: a
bound that is too low turns a valid verification into
`ObservationIncomplete`, which is a louder failure, not a false pass.
