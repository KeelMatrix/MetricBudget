# Observed cardinality vs possible production cardinality

The package answers one question:

> During the workload this test ran, how many distinct series and distinct tag values did these instruments
> produce?

It cannot answer a different question:

> What is the maximum cardinality this metric will produce in production?

## What an observed report means

- The measurement callbacks were real: they came from the `System.Diagnostics.Metrics` instruments the workload
  exercised, not from a static analysis of the source.
- Series identity is deterministic and order-independent, so the count reflects distinct tag combinations rather
  than traffic volume.
- Counts are exact while the safety bounds hold; when a bound is reached the report says so and the outcome is
  `ObservationIncomplete` instead of a pass.

## What it does not mean

- It does not prove safety. A path that the test never exercises can still emit a new tag value per request in
  production.
- It is not a cost estimate. Pricing, ingestion rules, sampling, and vendor-specific limits are out of scope.
- It is not a substitute for reviewing what each tag can carry. If a tag is derived from user input, a passing
  test says the exercised inputs were limited, not that the tag is bounded.
- It does not see metrics emitted by processes the test does not run.

## How to use it well

- Choose a workload that is representative of the paths you care about; a happy-path request is usually not
  enough, so add the error, tenant, and content variants your service really produces.
- Set budgets from what your platform can afford, then tighten them deliberately. The package never proposes a
  threshold, because no default is universally safe.
- Treat a rising observed-series count as a design signal about tag fan-out, and re-run the workload whenever a
  tag is added or changed.
- Keep the report in CI output. It contains tag keys and counts, which usually explain a breach without exposing
  the values.
