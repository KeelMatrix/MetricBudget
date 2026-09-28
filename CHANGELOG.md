# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

- Complete instrument identity with unit, description, measurement type, meter tags, and instrument tags, and make
  admitted shutdown callbacks commit before completion returns.
- Expose a privacy-safe identity discriminator for complete retained identities and make focused assertions able to
  select same-name streams by that discriminator.
- Give static metadata its own bounded options and dimension-specific safety failures; delivered-tag bounds no longer
  reject published metadata.
- Require release validation to run from the exact current `origin/main` commit.
- Make local package archive provenance reproducible from normal and detached candidate checkouts.
- Route documented package validation through the provenance-aware gate and guard against raw pack bypasses.

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
