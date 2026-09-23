# KeelMatrix.MetricBudget

Catch high-cardinality .NET metrics in tests and CI. `KeelMatrix.MetricBudget` observes the metric series and tag
values emitted by the workload you run and checks them against explicit budgets, without a collector, exporter, or
observability backend.

## Install

```text
dotnet add package KeelMatrix.MetricBudget --version 0.1.0
```

The package has no OpenTelemetry dependency. It works with libraries that emit `System.Diagnostics.Metrics`
instruments, including ASP.NET Core, `System.Net.Http`, EF Core, and custom meters.

## Quick start

```csharp
using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

[Fact]
public async Task ClientRequestsStayWithinObservedCardinality()
{
    using MetricBudgetSession session = MetricBudgetSession.Start(
        new MetricBudgetOptions()
            .ForInstrument("My.Service", "http.client.request.duration", budget =>
            {
                budget.MaxObservedSeries = 20;
                budget.Tag("server.address").MaxDistinctValues = 4;
            }));

    await MyApplication.HandleAsync(new Request("/orders/42"));

    MetricBudgetReport report = session.Complete();
    report.AssertWithinBudget();
}
```

Start the session before the workload, complete it after the workload, and assert the returned report. A passing run
means the exercised workload stayed within the configured budgets. A failing run names the instrument, counts, limits,
and tag keys, but never prints tag or metric values.

## Important limitations

- **Observed, not production, cardinality.** The report covers only the paths and inputs exercised by this run. It
  cannot prove the maximum cardinality production can produce or estimate backend cost. See
  [observed-vs-production-cardinality.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/observed-vs-production-cardinality.md).
- **Deterministic multiset identity.** Tag order does not change a series, but duplicate keys are retained. The CLR
  type of a tag value is part of identity. See
  [series-identity.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/series-identity.md).
- **Completeness is explicit.** Filling a safety bound is not itself incomplete; incompleteness begins when an
  additional observation or state entry is rejected. `InvalidConfiguration` and a proven `Violation` have higher
  outcome precedence than `ObservationIncomplete`, so inspect `report.Safety`, the per-result completeness flags,
  and `report.AccountingIsConsistent` for every result. See
  [safety-bounds.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/safety-bounds.md).
- **Privacy boundary.** Reports exclude tag values, metric values, and workload samples, but retain application-
  supplied meter names, meter versions, instrument names, and tag keys. Review those identifiers before sharing
  output outside its intended audience. See
  [privacy-and-telemetry.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/privacy-and-telemetry.md).
- **Observable instruments.** Observable callbacks run only when this session requests collection. Call
  `session.RecordObservableInstruments()` at each intended observation point, even if another metrics listener is
  active.
- **Session containment.** Start and complete one session around the workload it verifies. Instrument publication is
  process-global, while delivery is scoped to instruments selected and enabled by this session.

## Supported targets

The package ships `net8.0` and `netstandard2.0` assets. The repository verifies the latter through a .NET Framework
`net472` host using `System.Diagnostics.DiagnosticSource` 8.0.1. Core verification is offline; the package's optional
anonymous telemetry and opt-out rules are documented in
[privacy-and-telemetry.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/privacy-and-telemetry.md).

## Documentation

- [examples.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/examples.md) - canonical custom-meter and
  ASP.NET Core/OpenTelemetry-flavored consumer examples.
- [safety-bounds.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/safety-bounds.md) - bounded memory,
  lifecycle state, and outcome precedence.
- [troubleshooting.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/troubleshooting.md) - recurring
  outcomes and diagnostic guidance.
- [privacy-and-telemetry.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/privacy-and-telemetry.md) -
  report data boundary and telemetry behavior.
- [DEV.md](https://github.com/KeelMatrix/MetricBudget/blob/main/docs/DEV.md) - repository validation commands.

Source and the full documentation set are available at
<https://github.com/KeelMatrix/MetricBudget>.
