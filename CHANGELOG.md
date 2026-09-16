# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added

- Observed-cardinality verification for `System.Diagnostics.Metrics`: a session selects instruments, observes the
  measurements a workload emits, and reports the distinct observed series and distinct per-tag values it produced.
- Explicit per-instrument observed-series budgets and per-tag distinct-value budgets, with order-independent,
  deterministic series identity.
- A structured report with observed counts, configured limits, offending tag keys, and series-growth diagnostics
  that never contains tag values.
- Test-friendly assertion helpers that require no test-framework dependency and fail with privacy-safe diagnostics.
- Explicit outcomes for a budget violation, invalid configuration, an instrument that was never observed, a
  session that observed no measurements, and incomplete accounting.
- Hard in-memory safety bounds with explicit bounded-state diagnostics that can never be reported as a pass.
