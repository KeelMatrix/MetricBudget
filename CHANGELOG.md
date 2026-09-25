# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

## [0.1.0] - 2026-09-25

### Added

- A cross-targeted `net8.0` and `netstandard2.0` package for verifying observed `System.Diagnostics.Metrics`
  cardinality in tests and CI without a collector, exporter, or observability backend.
- Metric-budget sessions that select instruments and report the distinct observed series and per-tag distinct values
  produced by the exercised workload, with explicit per-instrument series and per-tag value limits.
- Deterministic, order-independent tag-set identity, plus structured reports and test-framework-independent assertion
  helpers with explicit outcomes for budget violations, invalid configuration, unobserved instruments, empty
  observations, and incomplete accounting.
- Privacy-safe diagnostics that retain instrument and tag-key context while excluding raw tag values, metric values,
  and workload samples.
- Bounded in-memory accounting with explicit safety limits and failure-safe incomplete-state diagnostics for
  observations that exceed the verifier's retained-state capacity.
