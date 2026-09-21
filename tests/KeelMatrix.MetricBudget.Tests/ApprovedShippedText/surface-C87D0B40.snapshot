# Safety bounds and bounded memory

The verifier observes exactly the failure class that can create huge numbers of combinations, so its own memory
has to be bounded and honest. Every session applies explicit bounds to every retained accounting dimension.

| Option | Default | What it bounds |
| --- | --- | --- |
| `MaxTrackedSeries` (per instrument identity) | 100,000 | Distinct observed series retained for one instrument identity. |
| `MaxTrackedValuesPerTag` (per instrument identity) | 5,000 | Distinct values retained per tag key and instrument identity. |
| `MaxTagValueLength` | 256 | Length of a tag value's invariant text before it is replaced by a stable digest in the identity. |
| `MaxTrackedInstrumentIdentities` | 1,024 | Selected instrument identities retained by one session. Later identities are not enabled; an affected retained same-name result is marked incomplete. The bounded rejected-name index records overflow uncertainty, so every identity admitted after that overflow is also marked incomplete. |
| `MaxTrackedInstrumentInstances` | 2,048 | Physical instrument instances retained and enabled by one session. A retained identity affected by a rejected instance is marked incomplete. |
| `MaxTrackedConflicts` | 1,024 | Ambiguous identity records retained; a conflict requiring more rule indexes than this is also dropped. |
| `MaxTrackedTagKeysPerInstrument` | 256 | Distinct delivered tag keys retained per instrument identity. |
| `MaxTagCount` | 64 | Delivered tags admitted from one measurement. |
| `MaxInstrumentIdentityLength` | 256 | Length of each meter name, meter version, and instrument name admitted after selector matching. Oversized unselected identities are ignored; oversized selected identities are rejected and counted separately. |
| `MaxTagKeyLength` | 256 | Length of a delivered tag key admitted to identity construction. |

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
- A series-cap exhaustion stops admission of series state but continues bounded updates for already retained tag
  keys. A tag-key cap stops admission of new keys while preserving already tracked keys. An oversized tag set,
  unsupported value, oversized key, or rejected instrument identity is not canonicalized or retained.
- The report names the bound, the instrument that reached it, and how many observations could not be tracked. Once
  any instrument reports an exhausted bound, the whole session outcome is `ObservationIncomplete`.
- Counts that depend on the exhausted bound become explicit lower bounds:
  `MetricBudgetInstrumentResult.SeriesTrackingIncomplete`,
  `MetricBudgetInstrumentResult.InstrumentTrackingIncomplete`,
  `MetricBudgetTagResult.ValueTrackingIncomplete`,
  `MetricBudgetTagResult.SeriesTrackingIncomplete`,
  `MetricBudgetTagResult.TagSetTrackingIncomplete`,
  `MetricBudgetTagResult.TagKeyTrackingIncomplete`,
  `MetricBudgetTagResult.InstrumentTrackingIncomplete`, and `MetricBudgetSafetyReport.IsComplete` state this
  machine-readably. Every per-tag completeness flag makes that tag's `IsWithinBudget` false; a count with any such
  flag is a lower bound and must not be read as complete.
- The rejected-name index is bounded by `MaxTrackedInstrumentIdentities`. When it is full, the session remembers the
  overflow without retaining another name. Every later newly admitted identity is marked incomplete because its
  name may be one of the forgotten rejected names; a rejected name that fits in the index remains scoped to its own
  same-name identity.
- Identity admission loss is separate from identity component-length rejection. The safety report exposes
  `InstrumentIdentityLengthTrackingIncomplete` and `UntrackedInstrumentIdentityLengths` for the latter, and the
  diagnostic recommends `MaxInstrumentIdentityLength`; it recommends `MaxTrackedInstrumentIdentities` or
  `MaxTrackedInstrumentInstances` for the corresponding admission bound.
- The session outcome becomes `ObservationIncomplete`, which is not a pass, and
  `AssertWithinBudget()` fails with the bounded-state diagnostics.

A definite budget breach that is already proven is reported as `Violation`; reaching a bound never hides it.

## Memory characteristics

- Series identity and tag-value identity retained by accounting are fixed-size SHA-256 digests, not the canonical
  text. Canonical strings and reusable UTF-16 byte scratch can briefly exist in ordinary managed process memory
  during hashing; the scratch is cleared after each digest, but this is not a secure-erasure guarantee.
- Retained memory grows with tracked identities, physical instances, conflicts, series, tag keys, and distinct tag
  values only up to their corresponding bounds. Because the series bound applies per instrument identity, the
  retained series ceiling grows with the number of admitted identities: *(matched instrument identities) x
  `MaxTrackedSeries`* fixed-size digests, plus the bounded per-tag key/value state.
- Identity components and delivered tag keys are length-bounded, and each delivered tag set is count-bounded before
  canonicalization. The conflict bound also limits the number of rule indexes retained in one conflict record.
- Canonicalization cost is linear in the bounded number of delivered tags per measurement plus one ordinal sort of
  those entries; the resource gate in the test suite measures per-measurement cost at representative series counts.

## Choosing tighter bounds

You can lower the per-instrument-identity bounds to fail faster on a workload you know well, for example the
per-instrument-identity `MaxTrackedSeries = 10_000`. Keep the bounds above the cardinality you legitimately expect: a
bound that is too low turns a valid verification into
`ObservationIncomplete`, which is a louder failure, not a false pass.
