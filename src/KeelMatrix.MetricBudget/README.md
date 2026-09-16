# KeelMatrix.MetricBudget

Catch high-cardinality .NET metrics in tests and CI. Run your normal workload, observe the metric series and tag
values it actually emits, and fail when an instrument exceeds an explicit observed-series or per-tag
distinct-value budget - with no collector, exporter, or observability backend.

```csharp
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

using MetricBudgetSession session = MetricBudgetSession.Start(
    new MetricBudgetOptions()
        .ForInstrument("My.Service", "http.client.request.duration", budget =>
        {
            budget.MaxObservedSeries = 50;
            budget.Tag("server.address").MaxDistinctValues = 5;
        }));

await ExerciseApplication();

MetricBudgetReport report = session.Complete();
report.AssertWithinBudget();
```

## Why

Metrics with unbounded or unexpectedly large tag values create many time-series combinations. Backends charge
for series, apply cardinality limits, or start aggregating values, and by then the problem is already in
production. `KeelMatrix.MetricBudget` moves that discovery into the test run: it listens to the real
`System.Diagnostics.Metrics` measurements your workload emits and fails locally when the observed cardinality
exceeds the budget you declared.

## Install

```text
dotnet add package KeelMatrix.MetricBudget
```

The package has no OpenTelemetry dependency. It works with any library that emits `System.Diagnostics.Metrics`
instruments - ASP.NET Core, `System.Net.Http`, EF Core, and your own meters.

## Five-minute quick start

1. Add the package to a test project.
2. Declare a budget for one instrument, and optionally for one tag key.
3. Start a session before the workload runs.
4. Run the workload you already have.
5. Complete the session and assert the report.

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
    report.AssertWithinBudget();   // fails with actionable, privacy-safe diagnostics
}
```

A passing run reports that the observed series stayed within the configured budgets. A failing run names the
instrument, the breached budget, the observed count, the configured limit, and the offending tag keys - never the
tag values.

## Observed cardinality is not production cardinality

This package verifies the workload you ran. It has no way to know which tag values production will send tomorrow.

- **Observed series** is what your exercised code paths produced, counted exactly.
- **Possible production cardinality** depends on user input, tenants, hosts, routes, error strings, and feature
  configuration that a test run may never touch.

So a passing report means "the exercised workload stayed within these budgets", not "this metric is safe in
production". Use it as a regression guard for the paths you do exercise, and keep reviewing what data each tag can
carry. `MaxObservedSeries` is deliberately named for what is measured; there is no default budget value that is
universally safe, and the package never invents one for you.

## Budgets

### Per instrument: maximum observed series

```csharp
new MetricBudgetOptions()
    .ForInstrument("My.Service", "http.client.request.duration", budget =>
        budget.MaxObservedSeries = 50);
```

One observed series is one combination of instrument identity and tag set. Repeating the same tag set - even with
different metric values - stays one series, so the number does not grow with traffic, only with distinct tag
combinations.

### Per tag: maximum distinct values

```csharp
new MetricBudgetOptions()
    .ForInstrument("My.Service", "http.client.request.duration", budget =>
    {
        budget.MaxObservedSeries = 50;
        budget.Tag("server.address").MaxDistinctValues = 5;
        budget.Tag("http.response.status_code").MaxDistinctValues = 10;
    });
```

Tag budgets are counted per instrument identity. A key that the workload never delivers is reported in the result
so that a typo in a tag key is visible rather than silent.

### Selecting instruments and meters

```csharp
// One instrument name in any meter.
options.ForInstrument("http.server.request.duration", budget => budget.MaxObservedSeries = 30);

// One instrument in one meter (every version of that meter).
options.ForInstrument("Microsoft.AspNetCore.Hosting", "http.server.request.duration", budget => budget.MaxObservedSeries = 30);

// Every instrument a meter publishes.
options.ForMeter("My.Service", budget => budget.MaxObservedSeries = 100);

// A selector built directly, for reuse.
InstrumentSelector selector = InstrumentSelector.InstrumentInMeter("My.Service", "queue.depth");
options.ForInstrument(selector, budget => budget.MaxObservedSeries = 4);
```

Names are matched exactly and case-sensitively, because metric names are case-sensitive. A rule must declare at
least one limit: starting a session with a rule that declares nothing throws
`MetricBudgetConfigurationException` instead of silently verifying nothing.

## Outcomes

`MetricBudgetReport.Outcome` distinguishes results that are not pass/fail:

| Outcome | Meaning |
| --- | --- |
| `Passed` | Every configured rule selected an instrument, every selected instrument delivered measurements, accounting was complete, and no budget was exceeded. |
| `Violation` | The observed workload exceeded a configured budget. |
| `InvalidConfiguration` | Configuration could not be applied - for example one instrument matched two rules, so no budget is unambiguous. Ambiguous instruments are not observed. |
| `NoMatchingInstrument` | A configured rule matched no published instrument: a misspelled name, a scenario the workload never exercises, or an instrument created after the session completed. |
| `NoMeasurementsObserved` | A selected instrument was published but delivered no measurements. |
| `ObservationIncomplete` | A safety bound was reached, so counts are lower bounds, or the session detected an impossible accounting state. |

Only `Passed` means the workload stayed within budget. A session that observed nothing never passes, which is what
keeps this verifier from silently verifying nothing.

## Assertion helpers

`KeelMatrix.MetricBudget.Assertions` provides helpers that throw `MetricBudgetAssertionException` with the
diagnostic report as the message. They take no dependency on a test framework, so xUnit, NUnit, MSTest, a console
harness, or a build script can all use them.

```csharp
report
    .AssertWithinBudget()
    .AssertOutcome(MetricBudgetOutcome.Passed)
    .AssertInstrumentObserved("My.Service", "http.client.request.duration")
    .AssertObservedSeriesAtMost("My.Service", "http.client.request.duration", 20)
    .AssertTagDistinctValuesAtMost("My.Service", "http.client.request.duration", "server.address", 4);
```

Prefer `AssertWithinBudget` in normal tests. The narrower helpers are useful when one test owns a single concern,
such as asserting only a tag budget while another test owns the series budget.

## Example: an in-process custom meter

```csharp
using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

using Meter meter = new Meter("Orders.Service", "1.0.0");
Counter<long> orders = meter.CreateCounter<long>("orders.placed");

using MetricBudgetSession session = MetricBudgetSession.Start(
    new MetricBudgetOptions()
        .ForInstrument("Orders.Service", "orders.placed", budget =>
        {
            budget.MaxObservedSeries = 12;
            budget.Tag("tenant").MaxDistinctValues = 10;
        }));

foreach (string tenant in new[] { "acme", "globex" })
{
    orders.Add(1, new KeyValuePair<string, object?>("tenant", tenant));
}

MetricBudgetReport report = session.Complete();
report.AssertWithinBudget();
```

## Example: ASP.NET Core with OpenTelemetry-style instrumentation

ASP.NET Core publishes its hosting metrics through `System.Diagnostics.Metrics` whether or not an OpenTelemetry
SDK is installed, so the same code verifies a real server without any exporter or backend.

```csharp
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

// Start the session before the requests you want to observe.
using MetricBudgetSession session = MetricBudgetSession.Start(
    new MetricBudgetOptions()
        .ForInstrument("Microsoft.AspNetCore.Hosting", "http.server.request.duration", budget =>
        {
            budget.MaxObservedSeries = 40;
            budget.Tag("http.response.status_code").MaxDistinctValues = 6;
        }));

await RunRequestScenariosAsync();   // your own HTTP client or WebApplicationFactory calls

MetricBudgetReport report = session.Complete();
report.AssertWithinBudget();
```

If you already run an OpenTelemetry SDK in the same process, the metrics it exports come from the same instruments,
so the same session observes them. The package neither requires nor controls an exporter.

## Measurements it observes

A session registers a callback for each of the seven numeric measurement types the BCL supports: `byte`, `short`,
`int`, `long`, `float`, `double`, and `decimal`. That set is complete, because the runtime rejects any other
measurement type when the instrument is created. All six instrument kinds are observed: `Counter`,
`UpDownCounter`, `Histogram`, `ObservableCounter`, `ObservableUpDownCounter`, and `ObservableGauge`.

Observable instruments deliver measurements only when their callbacks run, and the BCL never runs them when a
listener starts. Call `session.RecordObservableInstruments()` once per collection point if the application under
test has no metrics SDK doing it, otherwise the session reports `NoMeasurementsObserved`.

## Lifecycle, containment, and isolation

`MeterListener.Dispose` does **not** stop measurement delivery for instruments a listener already enabled:
verified on .NET 8.0.31, callbacks keep firing and `Instrument.Enabled` stays `true` after disposal. This session
therefore disables measurement events explicitly for every instrument it enabled, both when `Complete()` runs and
when it is disposed without completing. Disposal is not the containment mechanism; the explicit disable is. After
a session completes, `Instrument.Enabled` is `false` for everything it enabled, and later measurements are not
accounted.

Instrument publication is process-global, so a session sees instruments created by any code in the process,
including other tests running in parallel. Delivery is scoped: a session receives measurements only from the
instruments it enabled, and a completed session stops delivery for all of them. There is no way to hide another
test's instrument names from a running session; keep metric names specific and start sessions close to the
workload they verify.

## Safety bounds and memory

Cardinality is exactly the failure mode this package observes, so its own accounting is bounded:

| Bound | Default | Behavior when reached |
| --- | --- | --- |
| `MaxTrackedSeries` | 100,000 | Applies per instrument identity. Further distinct series of an instrument at its bound are counted as untracked observations, the report says series tracking is incomplete, and the outcome becomes `ObservationIncomplete`. |
| `MaxTrackedValuesPerTag` | 5,000 | Further distinct values for that tag key are counted as untracked, the report names the key, and the outcome becomes `ObservationIncomplete`. |
| `MaxTagValueLength` | 256 | A longer tag value is replaced by a stable digest in the identity, so one pathological value cannot inflate the session. |

A bounded run is never reported as a pass, and untracked observations are never matched to an existing series, so
the session cannot silently undercount. The defaults are safety bounds for the verifier, not budgets, and not
recommended cardinality for any application: raise them deliberately when your workload legitimately observes more.

`MaxTrackedSeries` and `MaxTrackedValuesPerTag` both apply per instrument identity rather than once per session, so
the whole session retains up to *(number of matched instrument identities) x `MaxTrackedSeries`* series
descriptions plus the per-tag value sets. Size memory from that product, and note that `ObservationIncomplete` is
reported as soon as any single instrument reaches its bound.

The session stores only fixed-size digests of series and tag values. It never retains the tag values themselves.
The full bounded-memory contract is documented at
<https://github.com/KeelMatrix/MetricBudget/blob/main/docs/safety-bounds.md>.

## Privacy

Reports and assertion messages contain meter names, instrument names, tag **keys**, counts, limits, and safety
state. Tag **values** are never printed, logged, or sent anywhere, and metric values are never read. The report is
generated locally and needs no network access. Only fixed-size digests of series and tag values are retained in
memory. If you need to know which value produced a breach, reproduce it locally with a debugger rather than
printing values into CI logs.

## Telemetry

A **completed verification that observed at least one selected instrument** requests one activation event;
installing, restoring, loading the assembly, or constructing a session reports nothing, and later completions
request a low-frequency heartbeat. The aggregate fields this product is permitted to supply are package version,
target framework, coarse OS family, a coarse observed-instrument bucket, the configured-rule count, and a coarse
outcome (pass, fail, invalid configuration); the internal allowlist is enforced by tests. That allowlist is a
ceiling on the product's own contribution, not a description of the transmitted payload: the shared
`KeelMatrix.Telemetry` client currently emits its own fixed schema and no product-specific fields. Meter names,
instrument names, tag keys, tag values, metric values, URLs, application or repository names, exception messages,
and file paths are never sent, and telemetry failure can never change a verification. Opt out with
`KEELMATRIX_NO_TELEMETRY=1`.

The canonical privacy and telemetry reference, including the exact field allowlist, is
<https://github.com/KeelMatrix/MetricBudget/blob/main/docs/privacy-and-telemetry.md>.

## Supported targets and platforms

- `net8.0` (uses the framework metrics implementation),
- `netstandard2.0` (uses `System.Diagnostics.DiagnosticSource` 8.0.1, so .NET Framework 4.6.2+, .NET Core, and
  later .NET versions can consume the same API).

The `netstandard2.0` asset also resolves the downlevel dependency closure of `KeelMatrix.Telemetry` 0.1.0:
`System.Text.Json`, `System.IO.Pipelines`, `System.Text.Encodings.Web`, `System.Memory`, `System.Buffers`,
`System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe`, `System.Threading.Tasks.Extensions`, and
`Microsoft.Bcl.AsyncInterfaces`. A .NET Framework consumer that already references an older `System.Text.Json` can
therefore hit version-unification prompts and possibly need binding redirects for that larger closure; NuGet
resolves it automatically for a project that has no conflicting pin.

Core verification is offline and needs no file system access, no sockets, and no backend. The library is plain
managed code and is expected to behave identically on Windows, Linux, and macOS; this release verifies the two
target frameworks on Windows.

## Troubleshooting

**`NoMatchingInstrument`** - a rule matched no published instrument. Check that the meter and instrument names are
exact (they are case-sensitive), that the workload really creates the instrument, that the `Meter` is not disposed
before the session starts, and that the session starts before the code under test runs. `MeterListener` sees
instruments created before and after the session starts, so a rule that never matches is usually a name mismatch
or a code path the workload did not reach.

**`NoMeasurementsObserved`** - the instrument exists but delivered nothing. For observable instruments, call
`session.RecordObservableInstruments()` (the BCL never invokes observable callbacks on its own). For counters and
histograms, make sure the recorded operation was actually executed.

**`InvalidConfiguration`** - one instrument matched more than one rule. Overlaps such as `ForMeter("M", ...)` plus
`ForInstrument("M", "x", ...)` leave no unambiguous budget, so the instrument is not observed and the report names
both rules. Make the rules disjoint.

**`ObservationIncomplete`** - a safety bound was reached. The report names the bound, the instrument, and how many
observations could not be tracked. Raise `MaxTrackedSeries` or `MaxTrackedValuesPerTag` if the workload is
representative, or narrow the workload if the cardinality is the finding you were looking for.

**A test fails in parallel CI but passes alone** - another test in the same process publishes instruments with the
same identity. Metric identity is meter name, meter version, instrument name, and kind, so generic names collide.
Use unique, specific names, and keep sessions scoped to the workload they verify.

More failure modes, including a tag budget that never triggers and unexplained series counts, are documented at
<https://github.com/KeelMatrix/MetricBudget/blob/main/docs/troubleshooting.md>.

## Series identity

Series identity is deterministic and order-independent: instrument identity (meter name, meter version, instrument
name, kind) plus a canonical tag-set identity. Tag order never changes identity, duplicate keys are retained as a
sorted multiset, the CLR type of a value is part of identity, and a value longer than `MaxTagValueLength` is
replaced by a stable digest. The exact rule is documented at
<https://github.com/KeelMatrix/MetricBudget/blob/main/docs/series-identity.md>.

## Repository

Source, tests, samples, and the full documentation set live at
<https://github.com/KeelMatrix/MetricBudget>.
