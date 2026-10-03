# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

## [0.1.0] - 2026-10-03

### Added

- A cross-targeted `net8.0` and `netstandard2.0` package for verifying observed `System.Diagnostics.Metrics`
  cardinality in tests and CI without a collector, exporter, or observability backend.
- Metric-budget sessions that select instruments and report the distinct observed series and per-tag distinct values
  produced by the exercised workload, with explicit per-instrument series and per-tag value limits.
- Deterministic series and instrument identity that treats tag sets as order-independent, includes relevant instrument
  metadata, and provides privacy-safe discriminators for selecting same-name streams.
- Structured reports and test-framework-independent assertion helpers with explicit outcomes for budget violations,
  invalid configuration, unobserved instruments, empty observations, ambiguous selections, and incomplete accounting.
- Bounded in-memory accounting with dedicated limits for delivered tags and static instrument metadata, and explicit
  safety diagnostics when observations or identity dimensions exceed retained-state capacity.
- Privacy-safe diagnostics that retain the context needed to investigate instrument and tag-key budgets without
  exposing raw tag values, metric values, or workload samples.
