# Contributing

Thank you for considering contributing to this project! We welcome contributions in the form of bug reports,
feature requests, documentation improvements, and pull requests.

## Before you begin

1. Ensure there is no existing issue addressing the same problem. If there is, feel free to add additional context
   or details.
2. Read through our [Code of Conduct](CODE_OF_CONDUCT.md) to understand our expectations for participant behavior.
3. For security-related issues, see [SECURITY.md](SECURITY.md) and use the recommended disclosure channels instead
   of filing a public issue.

## Making changes

1. Fork the repository and create your feature branch from `main`: `git checkout -b my-feature`.
2. If you added code that should be tested, add tests.
3. Ensure the test suite passes: `dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release`.
4. Format your code with `dotnet format` and ensure there are no warnings in a Release build.
5. Update the relevant documentation, including the package README when user-visible behavior changes.

Repository-only feasibility and test-internals evidence is linked from [docs/DEV.md](docs/DEV.md); it is not part of
the consumer documentation set.

## Behavior that must not regress

- **Observed cardinality language.** Documentation and diagnostics describe what the exercised workload produced.
  They never claim to prove production cardinality or observability cost.
- **Privacy.** Reports, diagnostics, and assertion messages contain meter and instrument identity, limits, counts,
  and tag keys. They never contain tag values, metric values, or samples of the workload.
- **Bounded memory.** Series and per-tag value accounting stay inside explicit safety bounds, and a bounded run is
  reported as incomplete rather than as a pass.
- **Explicit containment.** A session disables measurement events for every instrument it enabled, because
  `MeterListener.Dispose` does not stop delivery by itself.
- **Deterministic identity.** Series identity stays order-independent; the rule is documented in
  [docs/series-identity.md](docs/series-identity.md) and must match the implementation.

## Submitting a pull request

1. Open a pull request against the `main` branch and describe what your change does.
2. A maintainer will review your changes and provide feedback.
3. Make any requested changes and update the pull request.

## Public API surface

The shipping library uses Roslyn public API analyzers, so the supported surface is tracked in
`src/KeelMatrix.MetricBudget/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. When you add or change a public
member, update the affected files (the analyzer reports `RS0016` until you do) and review the diff as carefully as
the code change. After a release, accepted entries move from `Unshipped` to `Shipped`.

We appreciate your time and effort to improve this project! If you are unsure how to get started, open an issue to
discuss the idea.
