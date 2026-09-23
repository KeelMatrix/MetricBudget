# KeelMatrix.MetricBudget examples

These examples verify the observed cardinality emitted by one exercised workload. The configured limits are
acceptance criteria for that run, not static maximums for production. The package observes the
`System.Diagnostics.Metrics` surface directly, so neither example needs an exporter, collector, or backend.

## In-process custom `Meter`

Use a session around the workload that creates and records the instruments. This is the same custom-meter pattern as
the runnable sample in `samples/KeelMatrix.MetricBudget.Sample/Program.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

using Meter meter = new Meter("Sample.Service", "1.0.0");
Counter<long> requests = meter.CreateCounter<long>("http.client.request.duration.count");
Histogram<double> duration = meter.CreateHistogram<double>("http.client.request.duration", unit: "ms");

MetricBudgetOptions options = new MetricBudgetOptions()
    .ForInstrument("Sample.Service", "http.client.request.duration", budget =>
    {
        budget.MaxObservedSeries = 6;
        budget.Tag("server.address").MaxDistinctValues = 2;
        budget.Tag("http.response.status_code").MaxDistinctValues = 3;
    });

using MetricBudgetSession session = MetricBudgetSession.Start(options);

string[] addresses = { "api.internal", "cache.internal" };
int[] statusCodes = { 200, 404, 503 };
for (int address = 0; address < addresses.Length; address++)
{
    for (int status = 0; status < statusCodes.Length; status++)
    {
        KeyValuePair<string, object?>[] tags =
        {
            new("server.address", addresses[address]),
            new("http.response.status_code", statusCodes[status]),
        };

        requests.Add(1, tags);
        duration.Record(12.5 + address + status, tags);
    }
}

MetricBudgetReport report = session.Complete();
Console.WriteLine(report.ToDiagnosticString());
report.AssertWithinBudget();
```

The workload above produces six observed series for the histogram. Repeating one of those combinations does not add a
series; a new combination can. The report describes only what this workload delivered, and `AssertWithinBudget`
fails when the observed result is over budget or cannot be fully verified.

For an observable instrument, call `RecordObservableInstruments` at each intended observation point before
`Complete`; the callback is not run merely because the session started:

```csharp
session.RecordObservableInstruments();
MetricBudgetReport report = session.Complete();
report.AssertWithinBudget();
```

Include a budget for the observable instrument in the same `MetricBudgetOptions` configuration when it is part of the
workload being verified.

## ASP.NET Core / OpenTelemetry-flavored application

ASP.NET Core publishes hosting metrics through `System.Diagnostics.Metrics`. An OpenTelemetry-flavored application
can consume that same surface, but the budget check can run without configuring OpenTelemetry, an exporter, or a
backend. This test-shaped example makes two local requests so the built-in
`Microsoft.AspNetCore.Hosting/http.server.request.duration` instrument emits two status-code values:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

public sealed class MetricBudgetExamples
{
    public static async Task VerifyServerMetricsAsync()
    {
        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument("Microsoft.AspNetCore.Hosting", "http.server.request.duration", budget =>
            {
                budget.MaxObservedSeries = 8;
                budget.Tag("http.response.status_code").MaxDistinctValues = 2;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        app.MapGet("/", static () => "ok");

        await app.StartAsync();
        try
        {
            string address = app.Urls.First();
            using HttpClient client = new HttpClient();
            using HttpResponseMessage success = await client.GetAsync(new Uri(address + "/"));
            using HttpResponseMessage missing = await client.GetAsync(new Uri(address + "/missing"));

            if (success.StatusCode != HttpStatusCode.OK || missing.StatusCode != HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException("The example requests did not produce the expected responses.");
            }
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        MetricBudgetReport report = session.Complete();
        report.AssertWithinBudget();
    }
}
```

The limits apply to the observed requests in this test, not to every route, status code, or production traffic shape.
Reports and assertion diagnostics include instrument identities, counts, limits, and tag keys for explanation, but do
not include tag values, metric values, or workload samples. Review application-supplied meter names, instrument
names, versions, and tag keys before sharing diagnostics.

### Reading completeness and outcomes

A safety bound being filled is not `ObservationIncomplete` by itself. That outcome begins when an additional
observation or state entry is rejected, and affected counts are then lower bounds. Check `report.Safety`,
`report.AccountingIsConsistent`, the per-instrument completeness flags such as
`SeriesTrackingIncomplete`, `TagSetTrackingIncomplete`, `TagKeyTrackingIncomplete`,
and `InstrumentTrackingIncomplete`, plus the per-tag `ValueTrackingIncomplete` flag. A proven `Violation` or
`InvalidConfiguration` takes
precedence over `ObservationIncomplete`; no result is a pass when the relevant accounting is incomplete.
