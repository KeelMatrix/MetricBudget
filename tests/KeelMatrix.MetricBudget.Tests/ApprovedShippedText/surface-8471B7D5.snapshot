// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

// Isolated package-consumer smoke test.
//
// This project references the built NuGet package from a local package source only. It defines its own meter and
// instruments, runs one workload that must stay within budget, and one workload that must exceed it. Both the
// passing and the failing path have to behave as documented for the smoke test to succeed.

Environment.SetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY", "1");

int exitCode = 0;

exitCode += RunPassingBudget();
exitCode += RunFailingBudget();

Console.WriteLine(exitCode == 0
    ? "PACKAGE CONSUMER SMOKE: PASS"
    : "PACKAGE CONSUMER SMOKE: FAIL");

return exitCode;

static int RunPassingBudget()
{
    Console.WriteLine("--- passing budget ---");

    using Meter meter = new Meter("Smoke.Consumer", "1.0.0");
    Counter<long> requests = meter.CreateCounter<long>("requests");

    MetricBudgetOptions options = new MetricBudgetOptions()
        .ForInstrument("Smoke.Consumer", "requests", budget =>
        {
            budget.MaxObservedSeries = 3;
            budget.Tag("route").MaxDistinctValues = 3;
        });

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    requests.Add(1, new KeyValuePair<string, object?>("route", "/a"));
    requests.Add(1, new KeyValuePair<string, object?>("route", "/b"));
    requests.Add(1, new KeyValuePair<string, object?>("route", "/a"));

    MetricBudgetReport report = session.Complete();
    Console.WriteLine(report.ToDiagnosticString());

    try
    {
        _ = report.AssertWithinBudget();
    }
    catch (MetricBudgetAssertionException exception)
    {
        Console.WriteLine("UNEXPECTED: passing workload failed the budget: " + exception.Message);
        return 1;
    }

    bool expected = report.Outcome == MetricBudgetOutcome.Passed
        && report.TotalMeasurementsObserved == 3
        && report.ObservedSeriesCount == 2;

    if (!expected)
    {
        Console.WriteLine("UNEXPECTED: passing workload reported " + report);
        return 1;
    }

    Console.WriteLine("passing workload reported " + report);
    return 0;
}

static int RunFailingBudget()
{
    Console.WriteLine("--- failing budget ---");

    using Meter meter = new Meter("Smoke.Consumer.Failing", "1.0.0");
    Counter<long> requests = meter.CreateCounter<long>("requests");

    MetricBudgetOptions options = new MetricBudgetOptions()
        .ForInstrument("Smoke.Consumer.Failing", "requests", budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("tenant").MaxDistinctValues = 1;
        });

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    requests.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-a"));
    requests.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-b"));

    MetricBudgetReport report = session.Complete();
    Console.WriteLine(report.ToDiagnosticString());

    if (report.Outcome != MetricBudgetOutcome.Violation)
    {
        Console.WriteLine("UNEXPECTED: over-budget workload reported " + report.Outcome);
        return 1;
    }

    try
    {
        _ = report.AssertWithinBudget();
        Console.WriteLine("UNEXPECTED: over-budget workload did not fail the assertion.");
        return 1;
    }
    catch (MetricBudgetAssertionException exception)
    {
        Console.WriteLine("over-budget workload failed the assertion as expected.");
        Console.WriteLine("first line: " + exception.Message.Split('\n')[0]);
    }

    return 0;
}
