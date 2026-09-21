// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

// A small application that emits a handful of metric series and verifies the observed cardinality of the workload
// it just ran. Run it with: dotnet run --project samples/KeelMatrix.MetricBudget.Sample -c Release

Environment.SetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY", "1");

using Meter meter = new Meter("Sample.Service", "1.0.0");

Counter<long> requests = meter.CreateCounter<long>("http.client.request.duration.count");
Histogram<double> duration = meter.CreateHistogram<double>("http.client.request.duration", unit: "ms");

MetricBudgetOptions options = new MetricBudgetOptions()
    .ForInstrument("Sample.Service", "http.client.request.duration", budget =>
    {
        // The workload below emits two addresses times three status codes, so six observed series are expected.
        budget.MaxObservedSeries = 6;
        budget.Tag("server.address").MaxDistinctValues = 2;
        budget.Tag("http.response.status_code").MaxDistinctValues = 3;
    });

using MetricBudgetSession session = MetricBudgetSession.Start(options);

Exercise(requests, duration);

MetricBudgetReport report = session.Complete();

Console.WriteLine(report.ToDiagnosticString());

// The assertion throws MetricBudgetAssertionException with the same privacy-safe diagnostics when the workload
// exceeds a configured budget.
_ = report.AssertWithinBudget();

Console.WriteLine("Observed cardinality is within the configured budgets.");

static void Exercise(Counter<long> requests, Histogram<double> duration)
{
    string[] addresses = { "api.internal", "cache.internal" };
    int[] statusCodes = { 200, 404, 503 };

    for (int address = 0; address < addresses.Length; address++)
    {
        for (int status = 0; status < statusCodes.Length; status++)
        {
            requests.Add(
                1,
                new KeyValuePair<string, object?>("server.address", addresses[address]),
                new KeyValuePair<string, object?>("http.response.status_code", statusCodes[status]));

            duration.Record(
                12.5 + address + status,
                new KeyValuePair<string, object?>("server.address", addresses[address]),
                new KeyValuePair<string, object?>("http.response.status_code", statusCodes[status]));
        }
    }
}
